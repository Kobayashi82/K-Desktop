using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The context of a gesture that is not a step of a command (the module saying an answer): nothing to commit, one step,
/// and the time it stays.
/// </summary>
internal sealed class SpeechGestureContext(EngineLoop engine, TimeSpan duration) : IGestureContext
{
    private readonly double _end = engine.Now + duration.TotalSeconds;

    public void Commit() { }

    public Task<bool> Committed { get; } = Task.FromResult(true);

    public Task<WindowInfo?>? Appeared => null;

    public TimeSpan TimeLeft => TimeSpan.FromSeconds(Math.Max(0, _end - engine.Now));

    public ManoeuvreSpeed Speed => ManoeuvreSpeed.Normal;

    public int Step => 1;

    public int StepCount => 1;
}
