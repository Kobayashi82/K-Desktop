using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 8.2: finding files (the Windows Search index and a walk through the usual folders, which the index may not
/// cover: the user's Downloads are on D:) and opening them, never running a program or a script.
/// </summary>
public sealed class FileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kdesktop-files-" + Guid.NewGuid().ToString("N")[..8]);

    public FileTests()
    {
        Directory.CreateDirectory(_root);
        Make("Factura_Iberdrola_luz_octubre.pdf", days: 3);
        Make("factura agua.pdf", days: 40);
        Make("Recetas.docx", days: 1);
        Make("setup-steam.exe", days: 0);
        Make(Path.Combine("Fotos boda", "IMG_0001.jpg"), days: 200);
        Make(Path.Combine("node_modules", "factura luz.pdf"), days: 0);
        Make(Path.Combine("a", "b", "c", "FacturaLuz2026.pdf"), days: 10);
        // The folders are old: the newest are the files.
        foreach (var folder in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
        {
            Directory.SetCreationTime(folder, DateTime.Now - TimeSpan.FromDays(300));
            Directory.SetLastWriteTime(folder, DateTime.Now - TimeSpan.FromDays(300));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Make(string name, int days)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        var when = DateTime.Now - TimeSpan.FromDays(days);
        File.SetCreationTime(path, when);
        File.SetLastWriteTime(path, when);
    }

    /// <summary>An index that knows nothing (or the given files): the walk alone.</summary>
    private sealed class FakeIndex(params FoundFile[] files) : IFileIndex
    {
        public Task<IReadOnlyList<FoundFile>> QueryAsync(FileQuery query, string? scope, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FoundFile>>(files);
    }

    private FileSearch Search(IFileIndex? index = null) => new(index ?? new FakeIndex(), () => [_root]);

    private static JsonElement Args(object arguments) => JsonSerializer.SerializeToElement(arguments);

    [Fact]
    public async Task WordsOfTheNameFindItAndTheProgramsFoldersAreSkipped()
    {
        var found = await Search().FindAsync(FileQuery.Of(Args(new { text = "factura luz", kind = "pdf" })), default);
        var names = found.Select(f => f.Name).ToList();
        Assert.Contains("Factura_Iberdrola_luz_octubre.pdf", names);
        Assert.Contains("FacturaLuz2026.pdf", names);
        Assert.DoesNotContain("factura luz.pdf", names); // inside node_modules
        // «factura agua» has only half the words and comes after the ones with both.
        Assert.True(names.IndexOf("factura agua.pdf") is -1 or > 1);
    }

    [Fact]
    public async Task TheNewestFirstAndOnlyTheRecentOnes()
    {
        var found = await Search().FindAsync(FileQuery.Of(Args(new { sort = "newest", days = 5 })), default);
        Assert.Equal(["setup-steam.exe", "Recetas.docx", "Factura_Iberdrola_luz_octubre.pdf"], found.Where(f => !f.IsFolder).Select(f => f.Name).Take(3));
        Assert.DoesNotContain(found, f => f.Name == "factura agua.pdf");
    }

    [Fact]
    public async Task FoldersByTheirName()
    {
        var found = await Search().FindAsync(FileQuery.Of(Args(new { text = "boda", kind = "folder" })), default);
        Assert.Equal("Fotos boda", Assert.Single(found).Name);
    }

    [Fact]
    public async Task WhatTheIndexFoundByContentStaysAndRepeatsGoAway()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        FoundFile At(string path) => new(path, DateTime.Now, DateTime.Now, 10, false, InContent: true);
        var inside = At(Path.Combine(profile, "Documents", "Recetas.docx"));
        var noise = new[]
        {
            At(Path.Combine(profile, "AppData", "Local", "cache", "lentejas.json")),
            At(Path.Combine(profile, "Miniconda3", "pkgs", "lentejas.txt")),
            At(Path.Combine(profile, ".cache", "lentejas.txt")),
            At(Path.Combine(Path.GetDirectoryName(profile)!, "Public", "Documents", "lentejas.txt")),
        };
        var found = await Search(new FakeIndex([inside, .. noise, inside])).FindAsync(FileQuery.Of(Args(new { text = "lentejas" })), default);
        Assert.Equal("Recetas.docx", Assert.Single(found).Name);
    }

    [Fact]
    public void TheIndexQuery()
    {
        var sql = WindowsSearchIndex.Sql(FileQuery.Of(Args(new { text = "la factura de l'eau", kind = "pdf", days = 7 })), @"D:\Downloads", new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc));
        Assert.Contains("SCOPE='file:D:/Downloads'", sql, StringComparison.Ordinal);
        Assert.Contains("FREETEXT(*, 'factura eau')", sql, StringComparison.Ordinal);
        Assert.Contains("CONTAINS(System.FileName, '\"factura*\" OR \"eau*\"')", sql, StringComparison.Ordinal);
        Assert.Contains("System.ItemType = '.pdf'", sql, StringComparison.Ordinal);
        Assert.Contains("System.DateModified >= '2026-10-15 10:00:00'", sql, StringComparison.Ordinal);
        Assert.EndsWith("ORDER BY System.Search.Rank DESC", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsWrittenJoinedStaysOneWord()
    {
        var query = new FileQuery("la carpeta k-desktop", null, null, null, false);
        Assert.Equal(["carpeta", "kdesktop"], query.Words);
        Assert.Contains("desktop", query.Terms);
        FoundFile Folder(string name) => new(Path.Combine(@"D:\Programas", name), DateTime.Now, DateTime.Now, 0, true);
        Assert.True(FileSearch.Score(Folder("K-Desktop 5.0"), ["kdesktop"]) > FileSearch.Score(Folder("Desktop"), ["kdesktop"]) + 0.1);
        Assert.Equal(["factura", "luz", "2026"], FileSearch.NameWords("FacturaLuz2026"));
    }

    [Fact]
    public async Task TheRealIndexAnswersOrSaysNothing()
    {
        // Wherever it runs: no exception, whatever the index has.
        var search = new FileSearch(folders: () => []);
        var found = await search.FindAsync(FileQuery.Of(Args(new { kind = "pdf", sort = "newest" })), default);
        Assert.InRange(found.Count, 0, FileSearch.Shown);
    }

    [Fact]
    public async Task OpeningByNumberAndProgramsAreOnlyShown()
    {
        var search = Search();
        var opened = new List<(string Name, bool InFolder)>();
        var open = new OpenFileTool(search, (file, inFolder) => { opened.Add((file.Name, inFolder)); return true; });
        var context = new ToolContext("lander");

        Assert.True((await open.RunAsync(Args(new { number = 1 }), context, default)).Failed); // no list yet

        var list = await new FindFilesTool(search).RunAsync(Args(new { sort = "newest", days = 5 }), context, default);
        Assert.Contains("1. setup-steam.exe", list.Text, StringComparison.Ordinal);
        var exe = await open.RunAsync(Args(new { number = 1 }), context, default);
        Assert.Equal(("setup-steam.exe", true), opened[^1]);
        Assert.Contains("programa", exe.Reply, StringComparison.Ordinal);

        var second = await open.RunAsync(Args(new { number = 2 }), context, default);
        Assert.Equal(("Recetas.docx", false), opened[^1]);
        Assert.True(second.Acted);
        Assert.Equal("Abro «Recetas.docx».", second.Reply);

        // Another assistant has no list of its own.
        Assert.True((await open.RunAsync(Args(new { number = 2 }), new ToolContext("dot"), default)).Failed);

        // Words: the best match, shown in its folder when asked.
        await open.RunAsync(Args(new { text = "factura luz", in_folder = true }), context, default);
        Assert.True(opened[^1].InFolder);
        Assert.Contains("uz", opened[^1].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFolderThatIsNotThereSaysWhichOnesThereAre()
    {
        var result = await new FindFilesTool(Search()).RunAsync(Args(new { folder = "mis cosas" }), new ToolContext(null), default);
        Assert.True(result.Failed);
        Assert.Contains("downloads", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUsualFoldersExist()
    {
        Assert.NotNull(UserFolders.Find("documents"));
        Assert.NotNull(UserFolders.Find("downloads"));
        Assert.All(UserFolders.All(), folder => Assert.True(Directory.Exists(folder)));
    }
}
