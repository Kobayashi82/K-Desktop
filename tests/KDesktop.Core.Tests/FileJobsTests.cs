using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>«Archivos y carpetas» on a scratch folder: above all, nesting never loops or touches what it should not.</summary>
public sealed class FileJobsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kd-filejobs-" + Guid.NewGuid().ToString("N")[..8]);

    public FileJobsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string P(params string[] parts) => Path.Combine([_root, .. parts]);

    private void Write(string relative, string text = "hola")
    {
        var path = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static Task<FileJobResult> Run(FileJob job) => FileJobs.RunAsync(job, null, CancellationToken.None);

    [Fact]
    public async Task BackupInsideTheSourceDoesNotCopyItself()
    {
        Write(@"Datos\a.txt");
        Write(@"Datos\sub\b.txt");
        var job = new FileJob(FileOperation.Copy, [P("Datos")], P("Datos", "Copias"), Exists: FileExists.Replace);
        await Run(job);
        var second = await Run(job);

        Assert.Null(second.Refused);
        Assert.Equal(2, second.Done);
        Assert.True(File.Exists(P(@"Datos\Copias\Datos\a.txt")));
        Assert.True(File.Exists(P(@"Datos\Copias\Datos\sub\b.txt")));
        Assert.False(Directory.Exists(P(@"Datos\Copias\Datos\Copias")));
    }

    [Fact]
    public async Task SourceInsideTheDestinationIsCopiedWhole()
    {
        Write(@"D\x\Docs\a.txt");
        var result = await Run(new FileJob(FileOperation.Copy, [P(@"D\x\Docs")], P("D")));
        Assert.Equal(1, result.Done);
        Assert.True(File.Exists(P(@"D\Docs\a.txt")));
    }

    [Fact]
    public async Task OntoItselfOrMovedInsideItselfIsRefused()
    {
        Write(@"A\a.txt");
        Assert.Equal("files.sameFolder", (await Run(new FileJob(FileOperation.Copy, [P("A")], _root))).Refused);
        Assert.Equal("files.sameFolder", (await Run(new FileJob(FileOperation.Copy, [P("A")], P("A")))).Refused);
        Assert.Equal("files.moveInside", (await Run(new FileJob(FileOperation.Move, [P("A")], P(@"A\B")))).Refused);
        Assert.True(File.Exists(P(@"A\a.txt")));
    }

    [Fact]
    public async Task MirrorCleansOnlyInsideTheMirror()
    {
        Write(@"Src\keep.txt");
        Write(@"Src\gone.txt");
        Write(@"Bak\other.txt");
        var job = new FileJob(FileOperation.Backup, [P("Src")], P("Bak"), MirrorDelete: true);
        await Run(job);
        File.Delete(P(@"Src\gone.txt"));
        await Run(job);

        Assert.True(File.Exists(P(@"Bak\Src\keep.txt")));
        Assert.False(File.Exists(P(@"Bak\Src\gone.txt")));
        Assert.True(File.Exists(P(@"Bak\other.txt")));
    }

    [Fact]
    public async Task DatedBackupsKeepTheLastOnes()
    {
        Write(@"Docs\a.txt");
        for (var i = 0; i < 4; i++)
        {
            var job = new FileJob(FileOperation.Backup, [P("Docs")], P(@"Docs\Copias"), BackupMode: BackupMode.Dated, Keep: 2,
                Now: new DateTime(2026, 10, 20 + i, 3, 0, 0, DateTimeKind.Local));
            var result = await Run(job);
            Assert.Null(result.Refused);
        }
        var copies = Directory.GetDirectories(P(@"Docs\Copias")).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["Docs 2026-10-22 03.00", "Docs 2026-10-23 03.00"], copies);
        // Each copy has the file and none of the earlier copies.
        Assert.True(File.Exists(P(@"Docs\Copias\Docs 2026-10-23 03.00\Docs\a.txt")));
        Assert.False(Directory.Exists(P(@"Docs\Copias\Docs 2026-10-23 03.00\Docs\Copias")));
    }

    [Fact]
    public async Task PatternsKeepTheirFolders()
    {
        Write(@"Music\a.mp3");
        Write(@"Music\x.txt");
        Write(@"Music\Rock\b.mp3");
        var result = await Run(new FileJob(FileOperation.Copy, [P(@"Music\*.mp3")], P("Out"), Subfolders: true));
        Assert.Equal(2, result.Done);
        Assert.True(File.Exists(P(@"Out\a.mp3")));
        Assert.True(File.Exists(P(@"Out\Rock\b.mp3")));
        Assert.False(File.Exists(P(@"Out\x.txt")));
    }

    [Fact]
    public async Task ExistingFilesFollowTheChoice()
    {
        Write(@"S\a.txt", "nuevo");
        Write(@"T\a.txt", "viejo");
        await Run(new FileJob(FileOperation.Copy, [P(@"S\a.txt")], P("T"), Exists: FileExists.Skip));
        Assert.Equal("viejo", File.ReadAllText(P(@"T\a.txt")));
        await Run(new FileJob(FileOperation.Copy, [P(@"S\a.txt")], P("T"), Exists: FileExists.KeepBoth));
        Assert.Equal("nuevo", File.ReadAllText(P(@"T\a (2).txt")));
        await Run(new FileJob(FileOperation.Copy, [P(@"S\a.txt")], P("T"), Exists: FileExists.Replace));
        Assert.Equal("nuevo", File.ReadAllText(P(@"T\a.txt")));
    }

    [Fact]
    public async Task ZipAndUnzip()
    {
        Write(@"Proj\a.txt", "uno");
        Write(@"Proj\src\b.txt", "dos");
        var zipped = await Run(new FileJob(FileOperation.Zip, [P("Proj")], P(@"Proj\out.zip")));
        Assert.Equal(2, zipped.Done);
        var unzipped = await Run(new FileJob(FileOperation.Unzip, [P(@"Proj\out.zip")], P("Back")));
        Assert.Equal(2, unzipped.Done);
        Assert.Equal("dos", File.ReadAllText(P(@"Back\Proj\src\b.txt")));
    }

    [Fact]
    public async Task MoveRenameDeleteAndCreate()
    {
        Write(@"M\a.txt");
        Write(@"M\b.txt");
        await Run(new FileJob(FileOperation.Move, [P(@"M\a.txt")], P("N")));
        Assert.True(File.Exists(P(@"N\a.txt")));
        Assert.False(File.Exists(P(@"M\a.txt")));

        await Run(new FileJob(FileOperation.Rename, [P(@"M\b.txt")], string.Empty, NewName: "c"));
        Assert.True(File.Exists(P(@"M\c.txt")));

        await Run(new FileJob(FileOperation.Delete, [P(@"M\c.txt")], string.Empty, Recycle: false));
        Assert.False(File.Exists(P(@"M\c.txt")));

        await Run(new FileJob(FileOperation.CreateFolder, [], P(@"Nueva\Sub")));
        Assert.True(Directory.Exists(P(@"Nueva\Sub")));
    }

    [Fact]
    public async Task MissingSourcesAreCountedAndTheRestGoesOn()
    {
        Write(@"S\a.txt");
        var result = await Run(new FileJob(FileOperation.Copy, [P(@"S\nada.txt"), P(@"S\a.txt")], P("T")));
        Assert.Equal(1, result.Done);
        Assert.Equal(1, result.Failed);
    }
}
