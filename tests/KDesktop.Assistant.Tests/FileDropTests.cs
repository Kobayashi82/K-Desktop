using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Pipe;
using static KDesktop.Assistant.Host.Interop.Native;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Files dragged onto an assistant (stage 13.8, SDK 1.16): its hit windows take OLE drops (only files, never as a move), the
/// assistant gets <see cref="PointerEventKind.FilesOver"/> and <see cref="PointerEventKind.FilesDropped"/> with the paths,
/// and K-Desktop <c>dropped</c>.
/// </summary>
[Collection(OnTheDesktop.Name)]
public partial class FileDropTests
{
    private const int DROPEFFECT_MOVE = 2;

    private static string Exe { get; } = Path.GetFullPath(typeof(FileDropTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila samples/DotAssistant primero");

    [Theory]
    [InlineData(DROPEFFECT_COPY | DROPEFFECT_MOVE | DROPEFFECT_LINK, DROPEFFECT_COPY)]
    [InlineData(DROPEFFECT_MOVE | DROPEFFECT_LINK, DROPEFFECT_LINK)]
    [InlineData(DROPEFFECT_MOVE, DROPEFFECT_NONE)]
    [InlineData(DROPEFFECT_NONE, DROPEFFECT_NONE)]
    public void ItNeverTakesThemAsAMove(int allowed, int taken) => Assert.Equal(taken, FileDropTarget.EffectFor(allowed));

    [Fact]
    public void OnlyFilesAreTaken()
    {
        IReadOnlyList<string> files = [], text = ["?"];
        OnSta(() =>
        {
            var withFiles = new System.Windows.DataObject();
            withFiles.SetFileDropList(new StringCollection { @"C:\Users\Ana\factura.pdf", @"C:\Users\Ana\Fotos" });
            files = FileDropTarget.FilesOf((ComDataObject)withFiles);
            text = FileDropTarget.FilesOf((ComDataObject)new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "hola"));
        });
        Assert.Equal([@"C:\Users\Ana\factura.pdf", @"C:\Users\Ana\Fotos"], files);
        Assert.Empty(text);
    }

    [Fact]
    public void OverAwayAndDroppedReachTheAssistantWithTheFiles()
    {
        var events = new List<PointerEvent>();
        var dropped = new List<IReadOnlyList<string>>();
        OnSta(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetFileDropList(new StringCollection { @"C:\a.txt" });
            var target = new FileDropTarget(0, (kind, files, at) =>
            {
                events.Add(new PointerEvent(kind, "body", at) { Files = files });
                if (kind == PointerEventKind.FilesDropped) dropped.Add(files);
            }, () => "Dot");
            var at = new POINTL { X = 10, Y = 20 };

            var effect = DROPEFFECT_COPY | DROPEFFECT_MOVE;
            target.DragEnter((ComDataObject)data, 0, at, ref effect);
            Assert.Equal(DROPEFFECT_COPY, effect);
            effect = DROPEFFECT_MOVE;
            target.DragOver(0, at, ref effect);
            Assert.Equal(DROPEFFECT_NONE, effect);
            target.DragLeave();

            effect = DROPEFFECT_COPY;
            target.DragEnter((ComDataObject)data, 0, at, ref effect);
            target.Drop((ComDataObject)data, 0, at, ref effect);
            Assert.Equal(DROPEFFECT_COPY, effect);

            // Text alone: nothing reaches the assistant and the cursor says «no».
            var text = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "hola");
            effect = DROPEFFECT_COPY;
            target.DragEnter((ComDataObject)text, 0, at, ref effect);
            Assert.Equal(DROPEFFECT_NONE, effect);
            target.Drop((ComDataObject)text, 0, at, ref effect);
        });
        Assert.Equal([PointerEventKind.FilesOver, PointerEventKind.FilesAway, PointerEventKind.FilesOver, PointerEventKind.FilesDropped],
            events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Equal([@"C:\a.txt"], e.Files));
        Assert.Equal(new System.Windows.Point(10, 20), events[^1].Position);
        Assert.Single(dropped);
    }

    /// <summary>
    /// A real OLE drag (DoDragDrop) onto a hit area placed under the cursor, which drops at once: no mouse is moved and no
    /// key is sent; for a moment an invisible window sits under the cursor.
    /// </summary>
    [Fact]
    public void ARealDragDropsOnTheHitArea()
    {
        var events = new List<PointerEvent>();
        var dropped = new List<(string Area, IReadOnlyList<string> Files)>();
        var result = 0;
        var placed = false;
        OnSta(() =>
        {
            Assert.True(OleInitialize(0) >= 0);
            try
            {
                if (!GetCursorPos(out var cursor)) return;
                using var pointer = new HostPointer(events.Add, (_, _) => { }, () => { }, dropped: (area, files, _) => dropped.Add((area, files)))
                {
                    Name = "Dot",
                };
                using var area = pointer.CreateHitArea("toy");
                area.Place(new HitShape.Rectangle(new PixelRect(cursor.X - 20, cursor.Y - 20, cursor.X + 20, cursor.Y + 20)));
                placed = true;
                var data = new System.Windows.DataObject();
                data.SetFileDropList(new StringCollection { @"C:\Users\Ana\factura.pdf" });
                result = DoDragDrop((ComDataObject)data, new DropAtOnce(), DROPEFFECT_COPY | DROPEFFECT_LINK, out var effect);
            }
            finally
            {
                OleUninitialize();
            }
        });
        Assert.True(placed, "Sin cursor (¿sin escritorio?)");
        Assert.Equal(DRAGDROP_S_DROP, result);
        Assert.Equal([PointerEventKind.FilesOver, PointerEventKind.FilesDropped], events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Equal("toy", e.Area));
        var (droppedOn, files) = Assert.Single(dropped);
        Assert.Equal("toy", droppedOn);
        Assert.Equal([@"C:\Users\Ana\factura.pdf"], files);
    }

    /// <summary>The real process: Dot's hit window is registered as a drop target (OLE marks it with a property).</summary>
    [Fact]
    public async Task TheAssistantProcessTakesDrops()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "drop-" + Guid.NewGuid().ToString("N")[..8]);
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        Process? process = null;
        var loaded = new TaskCompletionSource<LoadedMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage l) loaded.TrySetResult(l);
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), "startup")));
            var result = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(result.Ok, result.Error);

            nint hit = 0;
            await MenuProcessTests.Until(() => (hit = MenuProcessTests.HitWindowOf(process.Id)) != 0);
            Assert.NotEqual(0, GetProp(hit, "OleDropTargetInterface"));

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Falló en el hilo STA", failure);
    }

    private const int DRAGDROP_S_DROP = 0x00040100;
    private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

    /// <summary>A drag that drops where the cursor already is, at the first question.</summary>
    [ComVisible(true)]
    private sealed class DropAtOnce : IDropSource
    {
        public int QueryContinueDrag(int escapePressed, int keys) => DRAGDROP_S_DROP;

        public int GiveFeedback(int effect) => DRAGDROP_S_USEDEFAULTCURSORS;
    }

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(int escapePressed, int keys);

        [PreserveSig]
        int GiveFeedback(int effect);
    }

#pragma warning disable SYSLIB1054
    [DllImport("ole32.dll")]
    private static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] ComDataObject data,
        [MarshalAs(UnmanagedType.Interface)] IDropSource source, int allowed, out int effect);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetProp(nint hwnd, string name);
}
