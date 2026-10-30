using KDesktop.Core.Assistant;

namespace KDesktop.Core.Tests;

public sealed class ChatImagesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-chatimages-" + Guid.NewGuid().ToString("N")[..8]);

    public ChatImagesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private string Picture(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public void AnAnswerNamesItsPictures()
    {
        var shot = Picture("Captura 1.png");
        var photo = Picture("foto.jpg");
        var (text, pictures) = ChatImages.FromText($"Aquí está: ![la captura]({shot}) y también {photo}. ![otra](C:\\no\\está.png)");
        Assert.Equal([shot, photo], pictures);
        // The Markdown of a picture that is there leaves its words; one that is not there stays as it was.
        Assert.StartsWith("Aquí está: la captura y también", text, StringComparison.Ordinal);
        Assert.Contains("![otra](C:\\no\\está.png)", text, StringComparison.Ordinal);
        Assert.False(ChatImages.IsPicture(Path.Combine(_folder, "nota.txt")));
        Assert.False(ChatImages.IsPicture("foto.jpg"));
    }

    [Fact]
    public async Task WhatTheToolsMakeIsCollectedForTheOrder()
    {
        var shot = Picture("a.png");
        ChatImages.Made([shot]); // nobody collects: forgotten
        using (var order = ChatImages.Collect())
        {
            await Task.Run(() => ChatImages.Made([shot, Path.Combine(_folder, "gone.png")]));
            await Task.Yield();
            ChatImages.Made([shot]);
            Assert.Equal([shot], order.Taken);
        }
        using var next = ChatImages.Collect();
        Assert.Empty(next.Taken);
    }
}
