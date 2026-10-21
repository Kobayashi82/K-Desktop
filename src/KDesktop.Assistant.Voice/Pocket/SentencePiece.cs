using System.Text;

namespace KDesktop.Assistant.Voice.Pocket;

/// <summary>
/// Reads a SentencePiece <c>tokenizer.model</c> (Pocket TTS's) and turns text into ids and back, exactly like the original
/// library. Only what those models use: a Unigram vocabulary, byte fallback and the <c>identity</c> normalizer. Anything
/// else is an error rather than a wrong split: a wrong id does not fail, it just sounds odd. Ported from Kobii.
/// </summary>
/// <remarks>
/// The file is a plain protobuf <c>ModelProto</c>, so a few lines read its fields. Scores are added in single precision,
/// as in C++: Viterbi ties are decided by the order of the nodes, and a sum rounded differently would split otherwise.
/// </remarks>
public sealed class SentencePiece
{
    /// <summary>Taken off the worst piece for an unknown character (kUnkPenalty in SentencePiece).</summary>
    private const float UnknownPenalty = 10;

    /// <summary>The space as SentencePiece writes it (U+2581).</summary>
    private const char Space = '▁';

    private const int Normal = 1, Unknown = 2, Control = 3, UserDefined = 4, Byte = 6;

    private readonly List<string> _pieces = [];
    private readonly List<int> _types = [];
    private readonly List<float> _scores = [];
    private readonly Dictionary<string, (int Id, float Score)> _matchable = new(StringComparer.Ordinal);
    private readonly int[] _byteIds = new int[256];
    private int _maxMatch;
    private int _unknownId;
    private float _unknownScore;
    private bool _byteFallback;
    private bool _addDummyPrefix = true;
    private string _unknownSurface = " ⁇ ";

    private SentencePiece() => Array.Fill(_byteIds, -1);

    public int Count => _pieces.Count;

    public static SentencePiece Load(byte[] model)
    {
        var tokenizer = new SentencePiece();
        tokenizer.Read(model);
        return tokenizer;
    }

    /// <summary>The text split into ids (with the leading space SentencePiece adds).</summary>
    public List<int> Encode(string text)
    {
        var ids = new List<int>();
        if (text.Length == 0) return ids;
        var normalized = Encoding.UTF8.GetBytes((_addDummyPrefix ? " " + text : text).Replace(' ', Space));
        foreach (var (begin, end, id) in Viterbi(normalized))
        {
            if (id == _unknownId && _byteFallback)
                for (var i = begin; i < end; i++) ids.Add(_byteIds[normalized[i]]);
            else ids.Add(id);
        }
        return ids;
    }

    /// <summary>The ids back into text (loose bytes are joined, control pieces do not count).</summary>
    public string Decode(IEnumerable<int> ids)
    {
        var text = new StringBuilder();
        var pending = new List<byte>();
        var first = true;
        foreach (var id in ids)
        {
            if (id < 0 || id >= _pieces.Count) throw new ArgumentOutOfRangeException(nameof(ids), $"Pieza {id} fuera del vocabulario");
            var kind = _types[id];
            if (kind == Byte && _byteFallback)
            {
                pending.Add(Convert.ToByte(_pieces[id][3..5], 16));
                continue;
            }
            if (pending.Count > 0)
            {
                text.Append(Encoding.UTF8.GetString([.. pending]));
                pending.Clear();
                first = false;
            }
            if (kind == Control) continue;
            var piece = kind == Unknown ? _unknownSurface : _pieces[id];
            if (kind != Unknown)
            {
                if (first && _addDummyPrefix && piece.StartsWith(Space)) piece = piece[1..];
                piece = piece.Replace(Space, ' ');
            }
            first = false;
            text.Append(piece);
        }
        if (pending.Count > 0) text.Append(Encoding.UTF8.GetString([.. pending]));
        return text.ToString();
    }

    private void Read(byte[] model)
    {
        var trainer = Array.Empty<byte>();
        var normalizer = Array.Empty<byte>();
        foreach (var field in Fields(model))
        {
            switch (field.Number)
            {
                case 1 when field.Bytes is { } piece:
                    AddPiece(piece);
                    break;
                case 2 when field.Bytes is { } spec:
                    trainer = spec;
                    break;
                case 3 when field.Bytes is { } spec:
                    normalizer = spec;
                    break;
            }
        }
        if (_pieces.Count == 0) throw new InvalidDataException("El modelo de SentencePiece no trae piezas");
        ReadTrainer(trainer);
        ReadNormalizer(normalizer);

        // An unknown character scores below the worst normal piece: it always loses to a real one.
        var worst = float.MaxValue;
        for (var i = 0; i < _pieces.Count; i++)
            if (_types[i] == Normal) worst = Math.Min(worst, _scores[i]);
        if (worst == float.MaxValue) throw new InvalidDataException("El modelo de SentencePiece no trae piezas normales");
        _unknownScore = worst - UnknownPenalty;
        if (_byteFallback && _byteIds.Any(id => id < 0))
            throw new InvalidDataException("El modelo de SentencePiece usa bytes de reserva pero le faltan piezas <0xNN>");
    }

    private void AddPiece(byte[] blob)
    {
        var piece = string.Empty;
        float score = 0;
        var kind = Normal;
        foreach (var field in Fields(blob))
        {
            switch (field.Number)
            {
                case 1 when field.Bytes is { } text:
                    piece = Encoding.UTF8.GetString(text);
                    break;
                case 2:
                    score = field.Float;
                    break;
                case 3:
                    kind = (int)field.Value;
                    break;
            }
        }
        var id = _pieces.Count;
        _pieces.Add(piece);
        _scores.Add(score);
        _types.Add(kind);
        switch (kind)
        {
            case Normal or UserDefined:
                _matchable[piece] = (id, score);
                _maxMatch = Math.Max(_maxMatch, Encoding.UTF8.GetByteCount(piece));
                break;
            case Unknown:
                _unknownId = id;
                break;
            case Byte when piece.Length == 6 && piece.StartsWith("<0x", StringComparison.Ordinal) && piece.EndsWith('>'):
                _byteIds[Convert.ToByte(piece[3..5], 16)] = id;
                break;
        }
    }

    private void ReadTrainer(byte[] blob)
    {
        long modelType = 1;
        foreach (var field in Fields(blob))
        {
            switch (field.Number)
            {
                case 3:
                    modelType = field.Value;
                    break;
                case 24 when field.Value != 0:
                    throw new InvalidDataException("SentencePiece: treat_whitespace_as_suffix no está soportado");
                case 35:
                    _byteFallback = field.Value != 0;
                    break;
                case 44 when field.Bytes is { } surface:
                    _unknownSurface = Encoding.UTF8.GetString(surface);
                    break;
            }
        }
        if (modelType != 1) throw new InvalidDataException($"SentencePiece: solo se leen modelos Unigram (este es del tipo {modelType})");
    }

    private void ReadNormalizer(byte[] blob)
    {
        var name = "identity";
        var rules = false;
        var removeExtraSpaces = false;
        var escapeSpaces = true;
        foreach (var field in Fields(blob))
        {
            switch (field.Number)
            {
                case 1 when field.Bytes is { } text:
                    name = Encoding.UTF8.GetString(text);
                    break;
                case 2 or 6 when field.Bytes is { Length: > 0 }:
                    rules = true;
                    break;
                case 3:
                    _addDummyPrefix = field.Value != 0;
                    break;
                case 4:
                    removeExtraSpaces = field.Value != 0;
                    break;
                case 5:
                    escapeSpaces = field.Value != 0;
                    break;
            }
        }
        if (name != "identity" || rules || removeExtraSpaces || !escapeSpaces)
            throw new InvalidDataException($"SentencePiece: solo se lee el normalizador «identity» (este es «{name}»)");
    }

    /// <summary>
    /// The best way to split (start, end, piece). Nodes are made in SentencePiece's order (left to right, shortest match
    /// to longest, the unknown last): ties are decided by that order.
    /// </summary>
    private List<(int Begin, int End, int Id)> Viterbi(byte[] text)
    {
        var length = text.Length;
        var beginNodes = new List<int>[length + 1];
        var endNodes = new List<int>[length + 1];
        for (var i = 0; i <= length; i++)
        {
            beginNodes[i] = [];
            endNodes[i] = [];
        }

        var starts = new List<int>();
        var ends = new List<int>();
        var ids = new List<int>();
        var scores = new List<float>();
        void AddNode(int start, int end, int id, float score)
        {
            starts.Add(start);
            ends.Add(end);
            ids.Add(id);
            scores.Add(score);
            beginNodes[start].Add(starts.Count - 1);
            endNodes[end].Add(starts.Count - 1);
        }

        var position = 0;
        while (position < length)
        {
            var charLength = Utf8Length(text[position]);
            var single = false;
            for (var size = 1; size <= Math.Min(_maxMatch, length - position); size++)
            {
                if (!IsCompleteUtf8(text, position, size) || !_matchable.TryGetValue(Encoding.UTF8.GetString(text, position, size), out var found)) continue;
                single |= size == charLength;
                AddNode(position, position + size, found.Id, found.Score);
            }
            // No piece covers this character alone: it is crossed as unknown.
            if (!single) AddNode(position, position + charLength, _unknownId, _unknownScore);
            position += charLength;
        }

        // −1 is the start node, which scores zero.
        endNodes[0].Add(-1);
        var best = new float[starts.Count];
        var previous = new int[starts.Count];
        for (position = 0; position <= length; position++)
        {
            foreach (var node in beginNodes[position])
            {
                int? bestNode = null;
                float bestScore = 0;
                foreach (var left in endNodes[position])
                {
                    var score = (left < 0 ? 0f : best[left]) + scores[node];
                    if (bestNode is null || score > bestScore)
                    {
                        bestNode = left;
                        bestScore = score;
                    }
                }
                if (bestNode is null) throw new InvalidDataException("SentencePiece: el vocabulario no puede cubrir este texto");
                previous[node] = bestNode.Value;
                best[node] = bestScore;
            }
        }

        int? last = null;
        float lastScore = 0;
        foreach (var left in endNodes[length])
        {
            var score = left < 0 ? 0f : best[left];
            if (last is null || score > lastScore)
            {
                last = left;
                lastScore = score;
            }
        }

        var path = new List<(int, int, int)>();
        for (var node = last ?? -1; node >= 0; node = previous[node]) path.Add((starts[node], ends[node], ids[node]));
        path.Reverse();
        return path;
    }

    /// <summary>A run of bytes that splits no character (otherwise it cannot be a piece).</summary>
    private static bool IsCompleteUtf8(byte[] text, int start, int size)
    {
        var end = start + size;
        return end == text.Length || (text[end] & 0xC0) != 0x80;
    }

    private static int Utf8Length(byte lead) => lead < 0x80 ? 1 : lead < 0xE0 ? 2 : lead < 0xF0 ? 3 : 4;

    private readonly record struct Field(int Number, long Value, byte[]? Bytes, float Float);

    /// <summary>The fields of a protobuf message (the length-delimited ones as bytes).</summary>
    private static IEnumerable<Field> Fields(byte[] blob)
    {
        var index = 0;
        while (index < blob.Length)
        {
            var key = ReadVarint(blob, ref index);
            var number = (int)(key >> 3);
            switch (key & 7)
            {
                case 0:
                    yield return new Field(number, ReadVarint(blob, ref index), null, 0);
                    break;
                case 2:
                    var length = (int)ReadVarint(blob, ref index);
                    yield return new Field(number, 0, blob[index..(index + length)], 0);
                    index += length;
                    break;
                case 5:
                    yield return new Field(number, 0, null, BitConverter.ToSingle(blob, index));
                    index += 4;
                    break;
                case 1:
                    index += 8;
                    break;
                default:
                    throw new InvalidDataException($"SentencePiece: tipo de campo de protobuf {key & 7} desconocido");
            }
        }
    }

    private static long ReadVarint(byte[] blob, ref int index)
    {
        long value = 0;
        var shift = 0;
        while (true)
        {
            var b = blob[index++];
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }
}
