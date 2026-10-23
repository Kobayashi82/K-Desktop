# K-Desktop Assistant SDK

The SDK for writing assistants for [K-Desktop](https://github.com/Kobayashi82/K-Desktop): characters that live on top of
your windows, talk to you with their own voice, think with a local AI and run your K-Desktop commands with visible
gestures.

An assistant is a `.kai` package: a manifest (`assistant.json`), its pictures and settings, and its code in `lib/`. This
assembly is the only one it compiles against. K-Desktop always provides it, so a package never ships it: an assistant
built against SDK 1.x runs on any K-Desktop with SDK 1.y, y ≥ x.

```csharp
public sealed class MyAssistant : IAssistant
{
    public void Attach(IAssistantHost host) { /* surfaces, click areas, settings */ }
    public void Enter(EntranceKind how) { /* its entrance: Startup, Summoned, or Reloaded (quietly) */ }
    public double Tick(TickInfo tick) { /* move, Present when something changed; return how long it may sleep */ }
    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;
    public Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct) { /* … */ }
    public Task ExitAsync(ExitKind why, CancellationToken ct) { /* its goodbye */ }
    public void Dispose() { }
}
```

What the host gives it: a drawing layer over the whole desktop (WPF `DrawingContext`), click areas, the world (windows,
monitors, taskbar, the other assistants, music, system events), sound, keyboard and mouse wheel, window services, and the
gestures of the AI and the commands it is asked to perform. The AI, the tools, the voice and K-Desktop itself are never
seen by the assistant.

- **Guide:** [docs/assistant/creating-an-assistant.md](../../docs/assistant/creating-an-assistant.md)
- **Schemas** of the package files: [docs/assistant/schemas](../../docs/assistant/schemas)
- **Template:** `dotnet new install templates/kdassistant`, then `dotnet new kdassistant -n MyAssistant`
- **Without code:** an assistant can be just pictures and a `character.json` (see Pip, `samples/CharacterAssistant`)

`dotnet build` of an assistant project makes its `.kai` (the targets in `build/` pack it); try it without installing with
`KDesktop.exe --assistant-dev "<project folder>"`.
