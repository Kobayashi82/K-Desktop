using System.IO.MemoryMappedFiles;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Stage;

/// <summary>
/// The stage's pictures for K-Desktop (phase 16): two halves of a shared memory, written one after the other, and a
/// <c>stageFrame</c> saying which one is new. K-Desktop (elevated) opens it to read; nothing of it is ever written by K-Desktop.
/// </summary>
internal sealed class StageFrames : IDisposable
{
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Action<PipeMessage> _send;
    private readonly int _width, _height;
    private long _seq;

    public StageFrames(int width, int height, Action<PipeMessage> send)
    {
        _width = width;
        _height = height;
        _send = send;
        Name = $"KDesktop5.Stage.{Environment.ProcessId}";
        _map = MemoryMappedFile.CreateNew(@"Local\" + Name, (long)width * height * 4 * 2, MemoryMappedFileAccess.ReadWrite);
        _view = _map.CreateViewAccessor();
    }

    /// <summary>The name K-Desktop opens (under <c>Local\</c>).</summary>
    public string Name { get; }

    public void Write(StageCompositor picture)
    {
        var slot = (int)(++_seq % 2);
        _view.WriteArray((long)slot * _width * _height * 4, picture.Pixels, 0, picture.Pixels.Length);
        _send(new StageFrameMessage(Name, slot, _width, _height, _seq));
    }

    public void Dispose()
    {
        _view.Dispose();
        _map.Dispose();
    }
}
