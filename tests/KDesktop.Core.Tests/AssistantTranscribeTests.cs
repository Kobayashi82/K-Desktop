using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Core.Tests;

/// <summary>D5 (5.0.373): K-Desktop's side of transcribe_audio, without Parakeet (a .txt already made is read).</summary>
public sealed class AssistantTranscribeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("kd-recordings-").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static Task<string> Ready(string file) => Task.FromResult(file);

    private string Audio(string name, DateTime created)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, created);
        return path;
    }

    [Fact]
    public void TheLatestRecordingIsTheNewestAudioThatIsNotBeingRecorded()
    {
        var old = Audio("Audio 1.opus", DateTime.UtcNow.AddHours(-2));
        var newer = Audio("Audio 2.mp3", DateTime.UtcNow.AddHours(-1));
        var recording = Audio("Audio 3.wav", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(_folder, "notas.txt"), "no es audio");

        Assert.Equal(recording, AssistantTranscribe.Newest(_folder));
        Assert.Equal(newer, AssistantTranscribe.Newest(_folder, recording));
        Assert.NotEqual(old, AssistantTranscribe.Newest(_folder, recording));
        Assert.Null(AssistantTranscribe.Newest(Path.Combine(_folder, "no está")));
    }

    [Fact]
    public async Task ATranscriptionAlreadyMadeIsReadAndAnOldOneIsNot()
    {
        var audio = Audio("Reunión.opus", DateTime.UtcNow.AddMinutes(-10));
        var txt = Path.ChangeExtension(audio, ".txt");
        await File.WriteAllTextAsync(txt, "Empezamos la reunión.");

        var done = await AssistantTranscribe.HandleAsync(new TranscribeMessage("c-1"), null, _folder, null, Ready, default);
        Assert.True(done.Ok);
        Assert.Equal("Empezamos la reunión.", done.Text);
        Assert.Equal(audio, done.File);
        Assert.Equal(txt, done.Saved);

        // A .txt older than the audio is of another recording: it is transcribed again (here, no voice models).
        File.SetLastWriteTimeUtc(txt, DateTime.UtcNow.AddHours(-1));
        var again = await AssistantTranscribe.HandleAsync(new TranscribeMessage("c-2", audio), null, _folder, null, Ready, default);
        Assert.False(again.Ok);
        Assert.Equal("noModels", again.Reason);
    }

    [Fact]
    public async Task NoRecordingOrNotAudioSaysWhy()
    {
        Assert.Equal("notFound", (await AssistantTranscribe.HandleAsync(new TranscribeMessage("c-1"), null, _folder, null, Ready, default)).Reason);
        var pdf = Path.Combine(_folder, "factura.pdf");
        await File.WriteAllTextAsync(pdf, "x");
        Assert.Equal("notAudio", (await AssistantTranscribe.HandleAsync(new TranscribeMessage("c-2", pdf), null, _folder, null, Ready, default)).Reason);
        Assert.Equal("notFound", (await AssistantTranscribe.HandleAsync(new TranscribeMessage("c-3", Path.Combine(_folder, "no.mp3")), null, _folder, null, Ready, default)).Reason);
    }
}
