# With code

An assistant with code is a .NET class library (`net10.0-windows`, WPF) that implements `IAssistant` from the SDK. The
SDK is always provided by K-Desktop: a package never carries it.

## Starting from the template

You need the .NET 10 SDK. The template and the SDK come from NuGet
([KDesktop.Assistant.Templates](https://www.nuget.org/packages/KDesktop.Assistant.Templates),
[KDesktop.Assistant.Sdk](https://www.nuget.org/packages/KDesktop.Assistant.Sdk)):

```powershell
dotnet new install KDesktop.Assistant.Templates
dotnet new kdassistant -n MyAssistant --authorName "Me"
cd MyAssistant
dotnet build          # → bin\Debug\…\myassistant-1.0.0.kai
```

To build against a clone of the [K-Desktop repository](https://github.com/Kobayashi82/K-Desktop) instead (to try a change in
the SDK), add `--sdk "<K-Desktop>\src\KDesktop.Assistant.Sdk"`.

| | |
|---|---|
| `MyAssistant.cs` | The class that implements `IAssistant`: it draws itself, has a click area and knows `Communicate`, `React` and `Announce` |
| `Package\assistant.json` | The [manifest](index.md#the-manifest) |
| `Package\…` | Everything else that goes into the package as it is: `icon.png`, `settings.json`, `panel.json`, `persona\`, `voice\`, `assets\` |

Every build leaves the package in `bin\…\package\` exactly as it is installed and, next to it, the `.kai` (signed, if you
have a key: see [Signing](publishing.md#signing-the-package)).

## Trying it without installing

```powershell
KDesktop.exe --assistant-dev "<project folder>"
```

It loads the newest `bin\…\package` (a copy, so you can keep building) and **reloads it on every build**. If K-Desktop is
open with the same data folder it connects to it; otherwise it works alone. The log is `Logs\assistant.log` in the data
folder. `KDesktop.exe --assistant --exit` closes it.

## The smallest assistant

Only a few members are required; every other one has a default that does nothing (or says no).

```csharp
public sealed class MyAssistant : IAssistant
{
    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private Point _at;
    private bool _dirty = true;

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - 20);

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
    }

    public void Enter(EntranceKind how)
    {
        var work = _host.World.Current.PrimaryMonitor?.WorkArea ?? new PixelRect(0, 0, 800, 600);
        _at = new Point(work.Right - 200, work.Bottom - 24);
    }

    public Task ExitAsync(ExitKind why, CancellationToken ct) => Task.CompletedTask;

    public double Tick(TickInfo tick)
    {
        if (_dirty)
        {
            _dirty = false;
            var area = new PixelRect((int)_at.X - 24, (int)_at.Y - 24, (int)_at.X + 24, (int)_at.Y + 24);
            _surface.Present(area, dc => dc.DrawEllipse(Brushes.MediumPurple, null, _at, 18, 18));
            _hit.Place(new HitShape.Ellipse(area));
        }
        return 30;   // nothing moves: sleep (anything that matters calls RequestTick)
    }

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (gesture is Gesture.Communicate)
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        return GestureOutcome.Committed;
    }

    public void SetState(AssistantState state, StateDetail detail) { }

    public void Dispose()
    {
        _hit.Dispose();
        _surface.Dispose();
    }
}
```

## Rules

- Everything is called on the host's **engine thread**, and `await`s come back to it. Nothing needs locking; never block
  that thread.
- `Tick` returns **how long it may sleep**: at rest, seconds; while animating, `Rendering.FrameInterval`. Only draw
  (`Present`) when something changes: the last frame stays on screen, even if the GPU is lost or a full-screen game ends.
- Coordinates are **physical pixels** of the virtual screen. For sizes, the monitor's scaling (`MonitorInfo.DpiScale`).
- **You are not alone**: other assistants may be on the desktop at the same time, in the same process and on the same
  layer. Do not take the whole screen; `World.Others` says where the others are. What reaches you (gestures, orders,
  extras, settings) is yours alone.
- Never throw to say no: return `GestureOutcome.NotPerformed`. An exception is logged; several in a row unload the
  assistant.

## Coming in and leaving

`Enter(how)` and `ExitAsync(why, ct)` say **why** it arrives or leaves:

| `EntranceKind` | When | What to do |
|---|---|---|
| `Startup` | K-Desktop starts or the module is switched on | Its entrance |
| `Summoned` | The user puts it on the desktop | An entrance that says «here I am» |
| `Reloaded` | It was there a moment ago and comes back: a new version, another language, the process restarted or recovered from a crash, rebuilt with `--assistant-dev` | Reappear where it was, without a show |

| `ExitKind` | When | What to do |
|---|---|---|
| `Shutdown` | K-Desktop closes or the module is switched off | Leave at once |
| `Dismissed` | The user hides it (Settings, the tray, its menu) | Its goodbye |
| `Uninstalling` | It is being removed (its data folder may be deleted right after) | The last goodbye |
| `Reloading` | It comes back at once with `Reloaded` | Leave at once and save in `DataDirectory` where it was: the new one may be another process |

The host waits for the exit 3 s at most (then the token is cancelled).

## Common settings

What every assistant has the same way does not go in its `settings.json`: K-Desktop shows it in the *Basics* tab of its
card, with the same units for all. The manifest says which ones it takes:

```json
"common": ["size", "volume", "liveliness", "dragging"]
```

| Name | What it is | Who does it |
|---|---|---|
| `size` | Size, from 50 % to 200 % | The assistant: `host.Common.Size` (1 = the size it is drawn for), on top of the monitor's scaling |
| `volume` | Volume of its sounds, from 0 % to 100 % | The host, on everything played through `IAudio` (its voice is separate) |
| `liveliness` | How lively it is, from 0 % (as little as possible) to 200 % | The assistant: `host.Common.Liveliness` (1 = normal) for what it does on its own |
| `dragging` | Picking it up with the mouse | The host does not pass on the drags of its `body` click area when it is off; `host.Common.Dragging` for the rest |

When they change, `OnSettingsChanged` arrives with `host.Common` already up to date.

## Settings and panel

K-Desktop draws them with its own controls, without loading any code of the assistant:

- **`settings.json`** ([schema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/settings.schema.json))
  → its tabs on its card in *Settings → Assistants* (or its right-click menu → *Settings*). Pages (tabs if there are
  several), sections and controls: `toggle`, `slider`, `number`, `choice`, `text`, `color`, `gallery`, `apps`, `hotkey`,
  `button` (runs an extra or, with `resetData`, deletes its data folder after asking), `info` and `keys`. `enabledWhen` dims
  a control while a switch is off. What the user changes is saved (only what differs from the default) and, if it is on the
  desktop, reaches it at once: `OnSettingsChanged` with **all** the values (also in `host.Settings`).
- **`panel.json`** ([schema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/panel.schema.json))
  → *Panel → Assistant*, on its own tab (kept, so it shows while it is not on the desktop): `stat` (in a row), `progress`, `list`, `achievements`, `buttons` and `text`. The
  data arrives with `host.PublishPanel`, which is **merged** with what was published before: publish everything in `Enter`
  and then only what changes. A gallery with `source` takes its pictures from that same data
  (`[{ id, name, image, locked, lockText }]`).

## Extras

`extras` in the manifest are its own actions: they show in its right-click menu, in its panel and as **Assistant: action**
in K-Desktop's commands; with `hotkey`, the install card offers to create a command with that shortcut. They arrive in
`OnExtraAsync`.

- `host.SetExtraState(id, Shown | Disabled | Hidden)` shows, dims or hides an extra in the menu and the panel's buttons;
  after each load all go back to `Shown`. Commands and voice still reach `OnExtraAsync` while it is hidden: explain there
  why it cannot be done.
- `host.ExtraHotkey(id)` gives the shortcut of the K-Desktop command that runs it («CTRL + ALT + L»), to tell the user what
  to press.

## Voice and personality

- **Its voice**: what it answers is said aloud by the host, with the `voice` profile of its manifest (`voice/<id>.json`,
  [schema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/voice.schema.json)): an engine and a
  chain of effects —`pitch`, `formant`, `ring`, `vocoder`, `bitcrush`, `radio`, `chorus`, `flanger`, `echo`, `glitch`— and
  `moods`, the effects added in some situations. `OnVoiceLevel` says how loud it sounds while it talks (for lights or a
  mouth); `host.SetVoiceMood("error")`, `"happy"` or `null` change the mood of what it says from then on.
- **Its personality**: `persona/{lang}.json`
  ([schema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/persona.schema.json)), what tells the AI
  who it is and how it talks, and its fixed lines. Without the user's language, English or the first one there is.
- **What the user changes**: in *Personality* and *Voice* the user can write it another personality (your fixed lines
  stay), and give it another language and voice. With another language, `host.Language` is that one and not K-Desktop's:
  read it in `Attach` (when it changes, the host loads the assistant again).

## Its model

`"model": { "suggested": [...] }` in the manifest: models of Hugging Face («owner/repo/file.gguf» or «owner/repo:Q4_K_M»),
the best first. K-Desktop offers (unticked) the first one that fits in the user's graphics card, with its vision projector
if there is one, and only if that file is not there yet; once downloaded, that assistant thinks with it. The Lander, for
example:

```json
"model": { "suggested": ["unsloth/Qwen3.5-9B-GGUF:Q4_K_M", "unsloth/Qwen3.5-4B-GGUF:Q4_K_M"] }
```

`"think"` (one or several too) suggests the model it **thinks harder** with: offered apart, unticked too. If it is the
same file as `suggested`, it is not offered twice; without it, it thinks harder with its usual model.

## What next

- [What the host gives](host.md): bubble, states, sound, click areas, dropped files, keyboard, windows, music…
- [Gestures](gestures.md): carrying out the user's commands and the AI's tools.
- [Showcase](showcase.md): the *Try* tab and the catalog's clips.
- [API reference](../../api/KDesktop.Assistant.Sdk.yml).
