using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Remote.Web;

namespace KDesktop.Core.Tests;

/// <summary>B11 (5.0.353): what K-Desktop sends to the phones, kept until each paired device has it (a folder of the test).</summary>
public sealed class PhoneInboxTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("kd-phone-").FullName;

    private static readonly WebDevice Phone = new() { Id = "p", Name = "Pixel 8 · App" };
    private static readonly WebDevice Tablet = new() { Id = "t", Name = "Galaxy Tab · Chrome" };
    private static readonly WebDevice ThisPc = new() { Id = "pc", Name = "Windows · Edge", ThisPc = true };

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private PhoneInbox Inbox(params WebDevice[] devices) => new(_folder) { Devices = () => devices };

    [Fact]
    public void ToEveryDeviceButThisPcUntilEachHasIt()
    {
        var inbox = Inbox(Phone, Tablet, ThisPc);
        var added = 0;
        inbox.Added += () => added++;
        var sent = inbox.Send("Fontanero", "Luis: 600 123 456", null, null, "Kobii");
        Assert.Null(sent.Reason);
        Assert.Equal([Phone.Name, Tablet.Name], sent.Names);
        Assert.Equal(1, added);

        var waiting = Assert.Single(inbox.For(Phone.Id));
        Assert.Equal(("Fontanero", "Luis: 600 123 456", "Kobii"), (waiting.Title, waiting.Text, waiting.From));
        Assert.Empty(inbox.For(ThisPc.Id));
        inbox.Ack(Phone.Id, [waiting.Id]);
        Assert.Empty(inbox.For(Phone.Id));
        Assert.Single(inbox.For(Tablet.Id));
        // It survives a restart of K-Desktop.
        Assert.Single(Inbox(Phone, Tablet).For(Tablet.Id));
    }

    [Fact]
    public void ToTheOneNamed()
    {
        var inbox = Inbox(Phone, Tablet);
        Assert.Equal([Tablet.Name], inbox.Send("", "lista", null, null, "K-Desktop", "galaxy").Names);
        Assert.Empty(inbox.For(Phone.Id));
        Assert.Equal("noDevice", inbox.Send("", "lista", null, null, "K-Desktop", "iphone").Reason);
        Assert.Equal("noDevice", Inbox(ThisPc).Send("", "hola", null, null, "K-Desktop").Reason);
        Assert.Equal("empty", inbox.Send(" ", " ", " ", null, "K-Desktop").Reason);
    }

    [Fact]
    public void AFileIsCopiedAndOnlyForItsDevices()
    {
        var source = Path.Combine(_folder, "factura.pdf");
        File.WriteAllText(source, "pdf");
        var inbox = Inbox(Phone, Tablet);
        var sent = inbox.Send("", "", null, source, "K-Desktop", "pixel");
        File.Delete(source);
        var message = sent.Message!;
        Assert.Equal(("factura.pdf", 3L), (message.File, message.FileSize));
        var kept = inbox.FileOf(message.Id, Phone.Id);
        Assert.NotNull(kept);
        Assert.Equal("pdf", File.ReadAllText(kept));
        Assert.Null(inbox.FileOf(message.Id, Tablet.Id));
        // Still there once acknowledged: the app downloads it when the user taps it.
        inbox.Ack(Phone.Id, [message.Id]);
        Assert.NotNull(inbox.FileOf(message.Id, Phone.Id));

        Assert.Equal("fileMissing", inbox.Send("", "", null, Path.Combine(_folder, "no.pdf"), "K-Desktop").Reason);
    }

    [Fact]
    public void TheAssistantsAreToldWhatThereIs()
    {
        var inbox = Inbox(Phone, Tablet);
        var ok = AssistantPhone.Handle(new SendPhoneMessage("c-1", "kobii", "", "hola"), "Kobii", inbox);
        Assert.True(ok.Ok);
        Assert.Equal(2, ok.Names.Count);
        var none = AssistantPhone.Handle(new SendPhoneMessage("c-2", "kobii", "", "hola", Device: "iphone"), "Kobii", inbox);
        Assert.Equal(("noDevice", false), (none.Reason, none.Ok));
        Assert.Equal([Phone.Name, Tablet.Name], none.Devices);
    }

    [Fact]
    public void NotForever()
    {
        var inbox = Inbox(Phone);
        for (var i = 0; i < PhoneInbox.MaxKept + 5; i++) inbox.Send("", $"m{i}", null, null, "K-Desktop");
        var left = inbox.For(Phone.Id);
        Assert.Equal(PhoneInbox.MaxKept, left.Count);
        Assert.Equal("m5", left[0].Text);
    }
}
