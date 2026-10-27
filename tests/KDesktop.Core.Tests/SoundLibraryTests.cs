using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>«Elegir sonido»: the sounds by kind, Windows' with readable names, the user's in any format that plays.</summary>
public class SoundLibraryTests
{
    [Theory]
    [InlineData("Alarm01", SoundKind.Alarm)]
    [InlineData("Ring 3", SoundKind.Ring)]
    [InlineData("Ringout", SoundKind.Ring)]
    [InlineData("Notify Email", SoundKind.Alert)]
    [InlineData("Proximity Notification", SoundKind.Alert)]
    [InlineData("Tada", SoundKind.Alert)]
    [InlineData("Logro", SoundKind.Alert)]
    [InlineData("Toc", SoundKind.Alert)]
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
        Assert.Equal("Toc", SoundLibrary.NameOf("Toc"));
        Assert.Equal("mi aviso", SoundLibrary.NameOf(@"D:\Sonidos\mi aviso.mp3"));
        // ding.wav and Windows Ding.wav are different sounds: they keep different names.
        if (File.Exists(Path.Combine(media, "ding.wav")) && File.Exists(Path.Combine(media, "Windows Ding.wav")))
            Assert.NotEqual(SoundLibrary.NameOf(Path.Combine(media, "ding.wav")), SoundLibrary.NameOf(Path.Combine(media, "Windows Ding.wav")));
    }

    [Fact]
    public void EverySoundOnceAndOnlyKDesktopsOwnBuiltIn()
    {
        var all = SoundLibrary.All();
        Assert.Contains(all, s => s.Value == "Toc" && s.Source == SoundSource.KDesktop);
        Assert.Equal(all.Count, all.Select(s => s.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(all.Count(s => s.Source == SoundSource.Windows), all.Where(s => s.Source == SoundSource.Windows).Select(s => s.Name).Distinct().Count());
        // No built-in sound is a Windows one any more, and every old name has its Windows file.
        Assert.DoesNotContain(Sounds.BuiltInNames(), SoundLibrary.FromWindows.ContainsKey);
        Assert.All(SoundLibrary.FromWindows.Keys, name => Assert.True(File.Exists(SoundLibrary.Upgrade(name)), name));
    }

    [Fact]
    public void TheOldNamesBecomeTheWindowsFiles()
    {
        var config = new AppConfig();
        config.Settings.NotifySound = "Notification 1";
        config.Settings.StartupSound = "Toc";
        config.Settings.PauseSound = "Email";
        var cmd = new Command { Name = "Sonar", Sound = "Tada" };
        cmd.Actions.Add(new PlaySoundAction { Sound = "Ring 2" });
        cmd.Actions.Add(new ShowNotificationAction { Sound = ShowNotificationAction.GlobalSound });
        config.Commands.Add(cmd);

        Assert.True(config.Upgrade());
        var media = SoundLibrary.WindowsMedia;
        Assert.Equal(Path.Combine(media, "Windows Proximity Notification.wav"), config.Settings.NotifySound);
        Assert.Equal("Toc", config.Settings.StartupSound);
        Assert.Equal(Path.Combine(media, "Windows Notify Email.wav"), config.Settings.PauseSound);
        Assert.Equal(Path.Combine(media, "tada.wav"), cmd.Sound);
        Assert.Equal(Path.Combine(media, "Ring06.wav"), ((PlaySoundAction)cmd.Actions[0]).Sound);
        Assert.Equal(ShowNotificationAction.GlobalSound, ((ShowNotificationAction)cmd.Actions[1]).Sound);
        Assert.Equal(new AppSettings().NotifySound, config.Settings.NotifySound);
    }

    [Theory]
    [InlineData(".m4a")]
    [InlineData(".wma")]
    public void OtherFormatsAreRead(string extension)
    {
        // A built-in sound in that format (Windows encodes it), read the way it would play.
        var file = Path.Combine(AppPaths.DataDir, "Logro" + extension);
        Directory.CreateDirectory(AppPaths.DataDir);
        using (var wav = new NAudio.Wave.WaveFileReader(new MemoryStream(Sounds.BuiltInBytes("Logro")!)))
        {
            if (extension == ".m4a") NAudio.Wave.MediaFoundationEncoder.EncodeToAac(wav, file, 128000);
            else NAudio.Wave.MediaFoundationEncoder.EncodeToWma(wav, file, 128000);
        }
        using var reader = Sounds.Open(file);
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.2, 5);
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
