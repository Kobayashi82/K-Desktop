using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>C2 (5.0.367): what clipboard_history gets from K-Desktop's clipboard history.</summary>
public sealed class ClipboardHistoryAssistantTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-cliphist-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static TextMessage Ask(string words) => new("dot", "r1", "history", words);

    [Fact]
    public void OnlyTextsWithEveryWordAndNothingWhenNotAllowed()
    {
        var history = new ClipboardHistory(_folder);
        history.Add(new ClipCapture(ClipKind.Text, "reunión del viernes en https://ejemplo.com", Source: "chrome"));
        history.Add(new ClipCapture(ClipKind.Files, string.Empty, Files: [@"C:iernes.txt"]));
        history.Add(new ClipCapture(ClipKind.Text, "lista de la compra", Source: "notepad"));

        var all = AssistantText.History(Ask(""), history, allowed: true);
        Assert.True(all.Ok);
        Assert.Equal(["lista de la compra", "reunión del viernes en https://ejemplo.com"], all.History!.Select(h => h.Text));
        Assert.Equal("notepad", all.History![0].Source);

        var friday = AssistantText.History(Ask("viernes ejemplo"), history, allowed: true);
        Assert.Single(friday.History!);

        Assert.Equal("empty", AssistantText.History(Ask("nada"), history, allowed: true).Reason);
        Assert.Equal("off", AssistantText.History(Ask(""), history, allowed: false).Reason);
        Assert.Equal("off", AssistantText.History(Ask(""), null, allowed: true).Reason);
    }
}
