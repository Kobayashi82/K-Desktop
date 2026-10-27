using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>«Elegir sonido»: the sounds by kind, Windows' with readable names, the user's in any format that plays.</summary>
public class SoundLibraryTests
{
    [Theory]
    [InlineData("Alarm01", SoundKind.Alarm)]
    [InlineData("Alarma 2", SoundKind.Alarm)]
    [InlineData("Ring 3", SoundKind.Ring)]
    [InlineData("Ringout", SoundKind.Ring)]
    [InlineData("Notify Email", SoundKind.Alert)]
    [InlineData("Notification 1", SoundKind.Alert)]
    [InlineData("Tada", SoundKind.Alert)]
    [InlineData("Logro", SoundKind.Alert)]
    [InlineData("Shutdown", SoundKind.System)]
    [InlineData("Hardware Insert", SoundKind.System)]
    public void EachSoundHasItsKind(string name, SoundKind kind) => Assert.Equal(kind, SoundLibrary.KindOf(name));

    [Fact]
    public void WindowsSoundsHaveReadableNames()
    {
        var media = SoundLibrary.WindowsMedia;
        Assert.Equal("Notify Email", SoundLibrary.NameOf(Path.Combine(media, "Windows Notify Email.wav")));
        Assert.Equal("Alarm 01", SoundLibrary.NameOf(Path.Combine(media, "Alarm01.wav")));
        Assert.Equal("Chimes", SoundLibrary.NameOf(Path.Combine(media, "chimes.wav")));
        Assert.Equal("Alarm 01 · Garden", SoundLibrary.NameOf(Path.Combine(media, "Garden", "Alarm01.wav")));
        Assert.Equal("Tada", SoundLibrary.NameOf("Tada"));
        Assert.Equal("mi aviso", SoundLibrary.NameOf(@"D:\Sonidos\mi aviso.mp3"));
    }

    [Fact]
    public void EverySoundOnceWithTheBuiltInOnesFirst()
    {
        var all = SoundLibrary.All();
        Assert.Contains(all, s => s.Value == "Tada" && s.Source == SoundSource.KDesktop);
        Assert.Equal(all.Count, all.Select(s => s.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Windows' copies of the built-in ones are left out (Windows Default.wav is the built-in «Default»).
        var builtIn = Sounds.BuiltInBytes("Default");
        Assert.NotNull(builtIn);
        foreach (var s in all.Where(s => s.Source == SoundSource.Windows))
            Assert.False(new FileInfo(s.Value).Length == builtIn.Length && File.ReadAllBytes(s.Value).AsSpan().SequenceEqual(builtIn), s.Value);
    }

    [Theory]
    [InlineData(".m4a")]
    [InlineData(".wma")]
    public void OtherFormatsAreRead(string extension)
    {
        // A built-in sound in that format (Windows encodes it), read the way it would play.
        var file = Path.Combine(AppPaths.DataDir, "Tada" + extension);
        Directory.CreateDirectory(AppPaths.DataDir);
        using (var wav = new NAudio.Wave.WaveFileReader(new MemoryStream(Sounds.BuiltInBytes("Tada")!)))
        {
            if (extension == ".m4a") NAudio.Wave.MediaFoundationEncoder.EncodeToAac(wav, file, 128000);
            else NAudio.Wave.MediaFoundationEncoder.EncodeToWma(wav, file, 128000);
        }
        using var reader = Sounds.Open(file);
        Assert.InRange(reader.TotalTime.TotalSeconds, 1, 5);
        var buffer = new byte[4096];
        Assert.True(reader.Read(buffer, 0, buffer.Length) > 0);
    }

    [Fact]
    public void YourSoundsCanBeOfAnyFormatThatPlays()
    {
        Directory.CreateDirectory(AppPaths.SoundsDir);
        var mp3 = Path.Combine(AppPaths.SoundsDir, "Mi timbre.mp3");
        var txt = Path.Combine(AppPaths.SoundsDir, "notas.txt");
        File.WriteAllBytes(mp3, [0]);
        File.WriteAllText(txt, "no");
        try
        {
            Assert.Contains("Mi timbre", Sounds.UserNames());
            Assert.DoesNotContain("notas", Sounds.UserNames());
            Assert.Contains(SoundLibrary.All(), s => s.Value == "Mi timbre" && s.Source == SoundSource.Yours && s.Kind == SoundKind.System);
        }
        finally
        {
            File.Delete(mp3);
            File.Delete(txt);
        }
    }
}
