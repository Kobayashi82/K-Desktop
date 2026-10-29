using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>5.0.382: the launcher also finds a program by its name inside Windows («notepad» → «Bloc de notas»).</summary>
public class InstalledAppTests
{
    [Theory]
    [InlineData("Bloc de notas", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "Windows Notepad")]
    [InlineData("Bloc de notas", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe", "notepad")]
    [InlineData("Calculadora", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Windows Calculator")]
    [InlineData("Herramienta Recortes", "Microsoft.ScreenSketch_8wekyb3d8bbwe!App", "Screen Sketch")]
    public void ItsInnerNameInWords(string name, string id, string inner) =>
        Assert.Equal(inner, new InstalledApp(name, id).InnerName);

    [Theory]
    [InlineData("Chrome", "Chrome")]
    [InlineData("Algo", "5A8B2C3D4E5F")]
    public void NothingWhenItIsTheSameOrAHash(string name, string id) =>
        Assert.Null(new InstalledApp(name, id).InnerName);
}
