using System.Diagnostics;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Knowledge;

/// <summary>What a search found, and how.</summary>
/// <param name="Mixed">By words and meaning; false: only by words (no EmbeddingGemma, or it did not answer).</param>
public sealed record KnowledgeFound(IReadOnlyList<KnowledgeHit> Hits, bool Mixed, int Milliseconds);

/// <summary>
/// What the assistants know (stage 19): the folders of documents (and single files) the user chose, read once and kept up to date. On disk,
/// a file per document (<see cref="KnowledgeStore"/>); in memory, their words and vectors (<see cref="KnowledgeIndex"/>).
/// When it starts it loads what was read, looks at the folders and reads only what is new or changed (by size and date);
/// while it runs it watches them. One file at a time, resting as long as it works (half a core's worth at most), and it waits
/// while <see cref="WaitReason"/> says so (a game on the whole screen, K-Desktop paused, an assistant answering). A change of
/// the model of the vectors or of the cutting reads the documents again in the background; meanwhile they are found by their
/// words. It can be searched at any time, also while it reads.
/// </summary>
public sealed class KnowledgeBase : IDisposable
{
    /// <summary>How documents are cut: another way here, and they are read again.</summary>
    public const string Chunker = "1200-v1";

    /// <summary>Pieces per request to K-Desktop (each some 0,35 s of EmbeddingGemma).</summary>
    private const int EmbedBatch = 8;

    /// <summary>Files looked at in the folders at most (a folder of code with everything inside is not documents).</summary>
    public const int MostFiles = 50_000;

    /// <summary>How long it rests for each second it works (the tests do not rest).</summary>
    public double Rest { get; set; } = 1.0;

    /// <summary>The rest of each speed of Options → «Lo que sabe»: <c>gentle</c>, <c>normal</c> (also an unknown one) or <c>fast</c>.</summary>
    public static double RestFor(string? speed) => speed switch
    {
        "gentle" => 2.0,
        "fast" => 0,
        _ => 1.0,
    };

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleLook = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FullLook = TimeSpan.FromHours(6);
    private static readonly TimeSpan PauseLook = TimeSpan.FromSeconds(5);

    private readonly KnowledgeStore _store;
    private readonly KnowledgeIndex _index = new();
    private readonly ITextMeaning? _meaning;
    private readonly IScreenReader? _ocr;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, FileSignature> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly PriorityQueue<string, (int Priority, long Order)> _queue = new();
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _offline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _settleTimer;
    private readonly Timer _changedTimer;
    private readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _folders = [];
    private bool _readPictures = true;
    private volatile bool _userPaused;
    private bool _scanAll;
    private long _order;
    private Task? _worker;
    private string _phase = "off";
    private string? _current;
    /// <summary>The pieces of the file being read and how many have their meaning (a big file takes minutes: it is seen moving).</summary>
    private int _currentPieces, _currentDone;
    private string? _paused;
    private string? _model;
    private bool _probed;
    private int _raising;
    private DateTime _lastFullLook = DateTime.MinValue;
    private DateTime _lastProbe = DateTime.MinValue;

    private readonly record struct FileSignature(long Size, long Written);

    /// <param name="folder">Where the index is kept (<c>&lt;data&gt;\Assistant\Knowledge</c>).</param>
    /// <param name="meaning">EmbeddingGemma in K-Desktop; null: only by words.</param>
    /// <param name="ocr">The OCR of Windows for pictures and scanned pages; null: none.</param>
    public KnowledgeBase(string folder, ITextMeaning? meaning, IScreenReader? ocr)
    {
        _store = new KnowledgeStore(folder);
        _meaning = meaning;
        _ocr = ocr;
        _settleTimer = new Timer(_ => Settled());
        _changedTimer = new Timer(_ =>
        {
            Volatile.Write(ref _raising, 0);
            Changed?.Invoke();
        });
    }

    /// <summary>Why it should wait now (<c>game</c>, <c>kdesktop</c>, <c>busy</c>), or null to go on. Asked between files and pieces.</summary>
    public Func<string?> WaitReason { get; set; } = () => null;

    /// <summary>Raised when what <see cref="State"/> says changes (at most twice a second, on the pool).</summary>
    public event Action? Changed;

    /// <summary>The folders and single files chosen (and that count: none inside another).</summary>
    public IReadOnlyList<string> Folders => _folders;

    public bool HasFolders => _folders.Count > 0;

    /// <summary>
    /// The folders, whether pictures and scanned pages are read, the files the user took out and whether the user stopped
    /// the reading (<c>config</c> <c>tools</c> <c>knowledge</c>).
    /// </summary>
    public void Configure(IEnumerable<string> folders, bool readPictures, IEnumerable<string>? skipped = null, bool paused = false)
    {
        var clean = KnowledgeFolders.Clean(folders);
        var skip = new HashSet<string>((skipped ?? []).Select(Full).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        if (_userPaused != paused)
        {
            _userPaused = paused;
            Log.Info(paused ? "Asistente: lo que sabe, lectura en pausa" : "Asistente: lo que sabe, sigue leyendo");
            Raise();
        }
        lock (_gate)
        {
            var same = clean.SequenceEqual(_folders, StringComparer.OrdinalIgnoreCase) && readPictures == _readPictures && skip.SetEquals(_skipped);
            _folders = clean;
            _readPictures = readPictures;
            if (same) return;
            _skipped.Clear();
            _skipped.UnionWith(skip);
            _scanAll = true;
        }
        Watch();
        Log.Info($"Asistente: lo que sabe, {clean.Count} carpeta(s){(readPictures ? " con OCR" : string.Empty)}{(skip.Count > 0 ? $", {skip.Count} archivo(s) quitados" : string.Empty)}");
        _wake.Release();
        Raise();
    }

    /// <summary>A path as the folders give it (null: not a path).</summary>
    private static string? Full(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>Starts reading (once): what was read before, then the folders.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_worker is not null) return;
            _worker = Task.Run(() => RunAsync(_stop.Token));
        }
    }

    /// <summary>Reads everything again from the files.</summary>
    public void Rebuild()
    {
        lock (_gate)
        {
            _index.Clear();
            _store.Clear();
            _scanAll = true;
        }
        Log.Info("Asistente: lo que sabe se vuelve a leer entero");
        _wake.Release();
        Raise();
    }

    /// <summary>Forgets everything read (the folders stay as chosen: with some, it starts reading them again).</summary>
    public void Clear() => Rebuild();

    // ── how it is ────────────────────────────────────────────────────────

    /// <summary>For Options → «Lo que sabe».</summary>
    public KnowledgeStateMessage State()
    {
        lock (_gate)
        {
            var folders = _folders.Select(folder =>
            {
                var files = _files.Keys.Where(f => KnowledgeFolders.Contains(folder, f)).ToList();
                var documents = _index.All.Where(d => KnowledgeFolders.Contains(folder, d.Path)).ToList();
                var done = files.Count(UpToDate);
                if (_offline.Contains(folder)) return new KnowledgeFolderInfo(folder, false, documents.Count, documents.Count, documents.Sum(d => d.Pieces.Length), documents.Count(d => d.State == KnowledgeState.Failed));
                return new KnowledgeFolderInfo(folder, true, files.Count, done, documents.Sum(d => d.Pieces.Length), documents.Count(d => d.State == KnowledgeState.Failed));
            }).ToList();
            var phase = _folders.Count == 0 ? "off" : _phase == "indexing" && _paused is not null ? "paused" : _phase;
            var reading = phase is "indexing" or "paused";
            return new KnowledgeStateMessage(phase, folders.Sum(f => f.Files), folders.Sum(f => f.Done), folders.Sum(f => f.Pieces), folders,
                reading ? _current : null, phase == "paused" ? _paused : null, _probed && _model is null, _store.Bytes(),
                reading ? _currentPieces : 0, reading ? _currentDone : 0);
        }
    }

    /// <summary>Whether it is still reading (an answer may miss what is not read yet), and how far.</summary>
    public (bool Reading, int Done, int Files) Progress
    {
        get
        {
            lock (_gate)
            {
                var files = _files.Count;
                var done = _files.Keys.Count(UpToDate);
                return (_phase is "loading" or "scanning" || done < files, done, files);
            }
        }
    }

    /// <summary>
    /// The files of <paramref name="folder"/> with how each one is: those with <paramref name="text"/> in their path (within
    /// the folder) and in <paramref name="filter"/> (see <see cref="KnowledgeMessage.Filter"/>), the first
    /// <see cref="KnowledgeFilesMessage.Most"/>; and how many there are in each filter.
    /// </summary>
    public KnowledgeFilesMessage FilesIn(string folder, string? text = null, string? filter = null)
    {
        lock (_gate)
        {
            var paths = _files.Keys.Where(f => KnowledgeFolders.Contains(folder, f))
                .Concat(_index.All.Select(d => d.Path).Where(p => KnowledgeFolders.Contains(folder, p)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var all = paths.Select(path => Info(path))
                .Concat(_skipped.Where(s => KnowledgeFolders.Contains(folder, s)).Select(s => new KnowledgeFileInfo(s, "skipped", 0, 0)))
                .ToList();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["all"] = all.Count(f => InFilter(f, "all")),
                ["ready"] = all.Count(f => InFilter(f, "ready")),
                ["waiting"] = all.Count(f => InFilter(f, "waiting")),
                ["problem"] = all.Count(f => InFilter(f, "problem")),
                ["skipped"] = all.Count(f => InFilter(f, "skipped")),
            };
            var words = text?.Trim() ?? string.Empty;
            var chosen = all.Where(f => InFilter(f, filter ?? "all")
                    && (words.Length == 0 || Path.GetRelativePath(folder, f.Path).Contains(words, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
            return new KnowledgeFilesMessage(folder, [.. chosen.Take(KnowledgeFilesMessage.Most)], chosen.Count, text, filter, counts);
        }

        KnowledgeFileInfo Info(string path)
        {
            var document = _index.Find(path);
            var state = document is null || !UpToDate(path) && _files.ContainsKey(path) ? "waiting" : document.State switch
            {
                KnowledgeState.Empty => "empty",
                KnowledgeState.Failed => "failed",
                _ => "ready",
            };
            return new KnowledgeFileInfo(path, state, document?.Pieces.Length ?? 0, document?.Pages ?? 0, document?.Ocr ?? false);
        }

        static bool InFilter(KnowledgeFileInfo file, string filter) => filter switch
        {
            "ready" or "waiting" or "skipped" => file.State == filter,
            "problem" => file.State is "empty" or "failed",
            _ => file.State != "skipped",
        };
    }

    /// <summary>Read, and as it is now on disk (and with this model's vectors, once there is one).</summary>
    private bool UpToDate(string path)
    {
        if (_index.Find(path) is not { } document || !_files.TryGetValue(path, out var file)) return false;
        return document.Size == file.Size && document.Written == file.Written && document.Chunker == Chunker
               && (document.State != KnowledgeState.Ready || _model is null || document.Model == _model);
    }

    /// <summary>Changed, at most twice a second: the first change arms it, the ones after go with it.</summary>
    private void Raise()
    {
        if (Interlocked.Exchange(ref _raising, 1) == 0) _changedTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
    }

    // ── searching ────────────────────────────────────────────────────────

    /// <summary>
    /// The pieces that answer <paramref name="question"/> (at most <paramref name="most"/>, three of a document), each with its
    /// text; <paramref name="inFile"/> narrows it to the documents with those words in their name.
    /// </summary>
    public async Task<KnowledgeFound> SearchAsync(string question, int most, string? inFile, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        float[]? vector = null;
        string? model = null;
        if (_meaning is not null && _index.All.Any(d => d.Model.Length > 0))
        {
            var asked = await _meaning.EmbedAsync([question], EmbedUse.Query, cancellationToken).ConfigureAwait(false);
            if (asked is { Vectors.Count: 1 })
            {
                vector = asked.Vectors[0];
                model = ModelKey(asked.Model);
            }
        }
        var hits = _index.Search(question, vector, model, most, 3, vector is null ? WordsFloor : MixedFloor, inFile);
        var texts = new Dictionary<string, IReadOnlyList<KnowledgeStoredPiece>?>(StringComparer.OrdinalIgnoreCase);
        var found = new List<KnowledgeHit>();
        foreach (var hit in hits)
        {
            if (!texts.TryGetValue(hit.Path, out var pieces)) texts[hit.Path] = pieces = await Task.Run(() => _store.Find(hit.Path)?.Pieces, cancellationToken).ConfigureAwait(false);
            if (pieces is not null && hit.Piece < pieces.Count) found.Add(hit with { Text = pieces[hit.Piece].Text });
        }
        return new KnowledgeFound(found, vector is not null, (int)watch.ElapsedMilliseconds);
    }

    /// <summary>Below these a piece does not answer: mixed (words and meaning), and only by words.</summary>
    internal const double MixedFloor = 0.30, WordsFloor = 0.34;

    /// <summary>The model of the vectors with how they are made (prompt and size): another one, and they do not compare.</summary>
    private static string ModelKey(string model) => model + "|doc|" + LongText.Dimensions;

    // ── reading ──────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            SetPhase("loading", null);
            await Task.Run(Load, stop).ConfigureAwait(false);
            while (!stop.IsCancellationRequested)
            {
                if (!_probed || (_model is null && DateTime.UtcNow - _lastProbe > IdleLook)) await ProbeAsync(stop).ConfigureAwait(false);
                bool scan;
                lock (_gate)
                {
                    scan = _scanAll || DateTime.UtcNow - _lastFullLook > FullLook;
                    _scanAll = false;
                }
                if (scan)
                {
                    SetPhase("scanning", null);
                    await Task.Run(() => ScanAll(stop), stop).ConfigureAwait(false);
                }
                var next = Next();
                if (next is null)
                {
                    SetPhase("ready", null);
                    await _wake.WaitAsync(IdleLook, stop).ConfigureAwait(false);
                    continue;
                }
                SetPhase("indexing", next);
                if (!await WaitAsync(stop).ConfigureAwait(false)) continue;
                try { await ReadAsync(next, stop).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn($"Asistente: no se ha podido leer {next} para lo que sabe: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Asistente: lo que sabe se ha parado", ex);
        }
    }

    /// <summary>Waits while it should; false when the folders changed meanwhile (look again before reading).</summary>
    private async Task<bool> WaitAsync(CancellationToken stop)
    {
        var waited = false;
        while ((_userPaused ? "user" : WaitReason()) is { } why)
        {
            if (!waited && why != "user") Log.Info($"Asistente: lo que sabe espera ({why})");
            waited = true;
            lock (_gate) _paused = why;
            Raise();
            // Stopped by the user: «Continuar» goes on at once (almost).
            await Task.Delay(why == "user" ? TimeSpan.FromMilliseconds(500) : PauseLook, stop).ConfigureAwait(false);
            lock (_gate)
                if (_scanAll) return false;
        }
        if (waited)
        {
            lock (_gate) _paused = null;
            Raise();
        }
        return true;
    }

    private void Load()
    {
        var watch = Stopwatch.StartNew();
        var pieces = 0;
        foreach (var record in _store.ReadAll())
        {
            _index.Put(record);
            pieces += record.Pieces.Count;
        }
        Log.Info($"Asistente: lo que sabe cargado: {_index.Count} documento(s), {pieces} trozo(s) en {watch.ElapsedMilliseconds} ms");
        lock (_gate) _scanAll = true;
    }

    /// <summary>Which model makes the vectors now (one tiny question to K-Desktop); null: none, only words for now.</summary>
    private async Task ProbeAsync(CancellationToken stop)
    {
        _lastProbe = DateTime.UtcNow;
        var vectors = _meaning is null ? null : await _meaning.EmbedAsync(["?"], EmbedUse.Query, stop).ConfigureAwait(false);
        var model = vectors is { Vectors.Count: 1 } ? ModelKey(vectors.Model) : null;
        var changed = model != _model;
        lock (_gate)
        {
            _model = model;
            _probed = true;
            // Documents with other vectors (or none) are read again with these.
            if (model is not null && changed) _scanAll = true;
        }
        if (changed) Log.Info(model is null ? "Asistente: lo que sabe, sin EmbeddingGemma: solo por palabras" : $"Asistente: lo que sabe con {model}");
        Raise();
    }

    /// <summary>Looks at every folder: what is new or changed goes to be read, what is gone is forgotten.</summary>
    private void ScanAll(CancellationToken stop)
    {
        var watch = Stopwatch.StartNew();
        IReadOnlyList<string> folders;
        bool pictures;
        lock (_gate)
        {
            folders = _folders;
            pictures = _readPictures;
            _lastFullLook = DateTime.UtcNow;
        }
        var seen = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
        var offline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            // A file chosen by itself: it, if it can be read; gone from a folder that is there, it is forgotten.
            if (new FileInfo(folder) is { Exists: true } file)
            {
                if (KnowledgeReader.Takes(file, pictures, chose: true)) seen[file.FullName] = new FileSignature(file.Length, file.LastWriteTimeUtc.Ticks);
                continue;
            }
            if (!Directory.Exists(folder))
            {
                // A disk that is not plugged in keeps what was read from it.
                if (!Directory.Exists(Path.GetDirectoryName(folder))) offline.Add(folder);
                continue;
            }
            Walk(folder, pictures, seen, stop);
        }
        lock (_gate)
        {
            // The files taken out are not there (and what was read of them is forgotten below).
            foreach (var skipped in _skipped) seen.Remove(skipped);
            // The queue is made again: what was in it from folders taken out does not stay (nor counts).
            _queue.Clear();
            _queued.Clear();
            _files.Clear();
            foreach (var (path, signature) in seen) _files[path] = signature;
            _offline.Clear();
            _offline.UnionWith(offline);
            // Forgotten: outside every folder, or gone from a folder that is there.
            foreach (var document in _index.All.ToList())
            {
                var folder = folders.FirstOrDefault(f => KnowledgeFolders.Contains(f, document.Path));
                if (folder is not null && (offline.Contains(folder) || seen.ContainsKey(document.Path))) continue;
                Forget(document.Path);
            }
            foreach (var path in seen.Keys)
                if (!UpToDate(path)) Enqueue(path, _index.Find(path) is null ? 0 : 1);
        }
        Log.Info($"Asistente: lo que sabe, {seen.Count} archivo(s) en {folders.Count} carpeta(s) mirados en {watch.ElapsedMilliseconds} ms; {_queue.Count} por leer");
        Watch();
        Raise();
    }

    /// <summary>The files to read under <paramref name="folder"/> (not in hidden folders nor in programs' ones: node_modules, .git…).</summary>
    private static void Walk(string folder, bool pictures, Dictionary<string, FileSignature> seen, CancellationToken stop)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, ReturnSpecialDirectories = false };
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0 && seen.Count < MostFiles)
        {
            stop.ThrowIfCancellationRequested();
            var directory = new DirectoryInfo(pending.Pop());
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    if (entry is DirectoryInfo sub)
                    {
                        if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint) && !KnowledgeReader.SkipsFolder(sub)) pending.Push(sub.FullName);
                    }
                    else if (entry is FileInfo file && KnowledgeReader.Takes(file, pictures))
                    {
                        seen[file.FullName] = new FileSignature(file.Length, file.LastWriteTimeUtc.Ticks);
                        if (seen.Count >= MostFiles) break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn($"Asistente: no se puede mirar {directory.FullName}: {ex.Message}");
            }
        }
    }

    /// <summary>New files first (they know nothing yet), then the ones to read again.</summary>
    private void Enqueue(string path, int priority)
    {
        if (_queued.Add(path)) _queue.Enqueue(path, (priority, _order++));
    }

    private string? Next()
    {
        lock (_gate)
        {
            while (_queue.TryDequeue(out var path, out _))
            {
                _queued.Remove(path);
                if (_files.ContainsKey(path) && !UpToDate(path)) return path;
            }
            return null;
        }
    }

    private void Forget(string path)
    {
        _index.Remove(path);
        _store.Delete(path);
    }

    /// <summary>Reads one file into the index (or only its vectors again, when only the model changed).</summary>
    private async Task ReadAsync(string path, CancellationToken stop)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            lock (_gate)
            {
                _files.Remove(path);
                Forget(path);
            }
            Raise();
            return;
        }
        var watch = Stopwatch.StartNew();
        var signature = new FileSignature(info.Length, info.LastWriteTimeUtc.Ticks);
        var old = _index.Find(path);
        bool pictures;
        lock (_gate) pictures = _readPictures;

        IReadOnlyList<(int Page, string Text)> pieces;
        KnowledgeState state;
        var pages = 0;
        var ocr = false;
        if (old is { State: KnowledgeState.Ready, Chunker: Chunker } && old.Size == signature.Size && old.Written == signature.Written
            && _store.Find(path) is { } stored)
        {
            // Only the vectors are of another model: the text read before does.
            pieces = [.. stored.Pieces.Select(p => (p.Page, p.Text))];
            (state, pages, ocr) = (KnowledgeState.Ready, stored.Pages, stored.Ocr);
        }
        else
        {
            var read = await KnowledgeReader.ReadAsync(path, pictures ? _ocr : null, stop).ConfigureAwait(false);
            if (read is null) (pieces, state) = ([], KnowledgeState.Failed);
            else
            {
                pages = read.Content.Pages;
                ocr = read.Ocr;
                var chunks = LongText.Chunks(read.Content);
                pieces = [.. chunks.Select(c => (c.Page, c.Text))];
                state = chunks.Count == 0 ? KnowledgeState.Empty : KnowledgeState.Ready;
            }
        }

        var (vectors, model) = await EmbedAsync(path, pieces, stop).ConfigureAwait(false);
        var record = new KnowledgeRecord(path, signature.Size, signature.Written, Chunker, model ?? string.Empty, state, pages, ocr,
            [.. pieces.Select((p, i) => new KnowledgeStoredPiece(p.Page, p.Text, vectors?[i]))]);
        lock (_gate)
        {
            // Changed or gone while it was being read: the next look sees it.
            if (!_files.ContainsKey(path)) return;
            _store.Write(record);
            _index.Put(record);
        }
        Log.Info($"Asistente: lo que sabe ha leído {Path.GetFileName(path)}: {pieces.Count} trozo(s){(ocr ? " con OCR" : string.Empty)}{(state == KnowledgeState.Failed ? " (no se puede leer)" : string.Empty)} en {watch.ElapsedMilliseconds} ms");
        Raise();
    }

    /// <summary>
    /// The vectors of the pieces, a few at a time, resting as long as it took; null (only words) without EmbeddingGemma.
    /// Each piece goes with the file's name: it says what the piece is about.
    /// </summary>
    private async Task<(float[][]? Vectors, string? Model)> EmbedAsync(string path, IReadOnlyList<(int Page, string Text)> pieces, CancellationToken stop)
    {
        if (_meaning is null || _model is null || pieces.Count == 0) return (null, null);
        var title = Path.GetFileNameWithoutExtension(path);
        var vectors = new float[pieces.Count][];
        string? model = null;
        for (var start = 0; start < pieces.Count; start += EmbedBatch)
        {
            lock (_gate)
            {
                _currentPieces = pieces.Count;
                _currentDone = start;
            }
            if (pieces.Count > EmbedBatch) Raise();
            await WaitAsync(stop).ConfigureAwait(false);
            var watch = Stopwatch.StartNew();
            var batch = pieces.Skip(start).Take(EmbedBatch).Select(p => title + "\n" + p.Text).ToList();
            var answer = await _meaning.EmbedAsync(batch, EmbedUse.Document, stop).ConfigureAwait(false);
            if (answer is null || answer.Vectors.Count != batch.Count)
            {
                // EmbeddingGemma went away (or did not answer): words only, and it is asked again later.
                lock (_gate)
                {
                    _model = null;
                    _lastProbe = DateTime.UtcNow;
                }
                return (null, null);
            }
            model ??= ModelKey(answer.Model);
            for (var i = 0; i < batch.Count; i++) vectors[start + i] = answer.Vectors[i];
            await Task.Delay(TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds * Rest), stop).ConfigureAwait(false);
        }
        return (vectors, model);
    }

    // ── watching ─────────────────────────────────────────────────────────

    /// <summary>A watcher on each folder that is there (one plugged in later gets it at the next look).</summary>
    private void Watch()
    {
        IReadOnlyList<string> folders;
        lock (_gate) folders = _folders;
        lock (_watchers) WatchLocked(folders);
    }

    private void WatchLocked(IReadOnlyList<string> folders)
    {
        foreach (var (folder, watcher) in _watchers.ToList())
        {
            if (folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) continue;
            watcher.Dispose();
            _watchers.Remove(folder);
        }
        foreach (var folder in folders)
        {
            if (_watchers.ContainsKey(folder)) continue;
            // A file chosen by itself: its folder, only that name.
            var single = !Directory.Exists(folder);
            if (single && !Directory.Exists(Path.GetDirectoryName(folder))) continue;
            try
            {
                var watcher = new FileSystemWatcher(single ? Path.GetDirectoryName(folder)! : folder, single ? Path.GetFileName(folder) : "*")
                {
                    IncludeSubdirectories = !single,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => Dirty(e.FullPath);
                watcher.Changed += (_, e) => Dirty(e.FullPath);
                watcher.Deleted += (_, e) => Dirty(e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    Dirty(e.OldFullPath);
                    Dirty(e.FullPath);
                };
                // Too many changes at once: everything is looked at again.
                watcher.Error += (_, _) =>
                {
                    lock (_gate) _scanAll = true;
                    _wake.Release();
                };
                watcher.EnableRaisingEvents = true;
                _watchers[folder] = watcher;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Warn($"Asistente: no se puede vigilar {folder}: {ex.Message}");
            }
        }
    }

    private void Dirty(string path)
    {
        lock (_gate) _dirty.Add(path);
        _settleTimer.Change(Settle, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Some moments after the last change: each changed path is looked at (a folder, all that is in it).</summary>
    private void Settled()
    {
        List<string> dirty;
        lock (_gate)
        {
            dirty = [.. _dirty];
            _dirty.Clear();
        }
        var any = false;
        foreach (var path in dirty)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var seen = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
                    bool pictures;
                    lock (_gate) pictures = _readPictures;
                    Walk(path, pictures, seen, CancellationToken.None);
                    lock (_gate)
                        foreach (var (file, signature) in seen)
                            any |= Seen(file, signature);
                }
                else if (new FileInfo(path) is { Exists: true } file)
                {
                    bool pictures;
                    lock (_gate) pictures = _readPictures;
                    lock (_gate)
                    {
                        if (KnowledgeReader.Takes(file, pictures, IsChosen(file.FullName)) && !InSkippedFolder(file.FullName)) any |= Seen(file.FullName, new FileSignature(file.Length, file.LastWriteTimeUtc.Ticks));
                        else any |= Gone(file.FullName);
                    }
                }
                else
                    lock (_gate)
                        any |= Gone(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Asistente: lo que sabe no puede mirar {path}: {ex.Message}");
            }
        }
        if (!any) return;
        _wake.Release();
        Raise();
    }

    /// <summary>Under a hidden or programs' folder of one of the folders (the walk skips them; so do the changes).</summary>
    private bool InSkippedFolder(string path)
    {
        var folder = _folders.FirstOrDefault(f => KnowledgeFolders.Contains(f, path));
        if (folder is null) return true;
        if (string.Equals(folder, path, StringComparison.OrdinalIgnoreCase)) return false;
        var parts = Path.GetRelativePath(folder, Path.GetDirectoryName(path)!).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = folder;
        foreach (var part in parts.Where(p => p != "."))
        {
            current = Path.Combine(current, part);
            if (KnowledgeReader.SkipsFolder(new DirectoryInfo(current))) return true;
        }
        return false;
    }

    /// <summary>A file the user chose by itself (not by its folder).</summary>
    private bool IsChosen(string path) => _folders.Contains(path, StringComparer.OrdinalIgnoreCase);

    private bool Seen(string path, FileSignature signature)
    {
        if (!_folders.Any(f => KnowledgeFolders.Contains(f, path)) || _skipped.Contains(path)) return false;
        if (_files.TryGetValue(path, out var known) && known == signature && UpToDate(path)) return false;
        _files[path] = signature;
        Enqueue(path, _index.Find(path) is null ? 0 : 1);
        return true;
    }

    /// <summary>A file or a folder gone (deleted, renamed, moved out): what was read of it is forgotten.</summary>
    private bool Gone(string path)
    {
        var gone = _files.Keys.Where(f => KnowledgeFolders.Contains(path, f)).ToList();
        var documents = _index.All.Where(d => KnowledgeFolders.Contains(path, d.Path)).Select(d => d.Path).ToList();
        foreach (var file in gone) _files.Remove(file);
        foreach (var document in documents) Forget(document);
        return gone.Count > 0 || documents.Count > 0;
    }

    private void SetPhase(string phase, string? current)
    {
        bool changed;
        lock (_gate)
        {
            changed = _phase != phase || _current != current;
            if (_current != current) _currentPieces = _currentDone = 0;
            _phase = phase;
            _current = current;
        }
        if (changed) Raise();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        lock (_watchers)
        {
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
        }
        _settleTimer.Dispose();
        _changedTimer.Dispose();
        _stop.Dispose();
    }
}
