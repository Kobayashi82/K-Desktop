# Showcase

On the card of each installed assistant, the **Try** tab shows it on a **stage**: a small pretend desktop of its own
(720×405 at 125 %, with a taskbar, a tray, a clock and three pretend windows: Notes, Photos and a Calculator that opens when
asked), drawn off the screen by a separate copy of your assistant (`KDesktop.exe --assistant-showcase`) and shown live in
the tab. The user can pet it or pick it up with the mouse over the picture, and choose each thing it can show (one at a
time: another one, or *Stop*, cuts the one before).

![Try](../../images/en/lander-try.png)

Your assistant does not know it is on a stage: it gets the stage's monitor, taskbar and windows in its `World`, moves and
presses them through `IWindowServices` as usual, and nothing of it ever lies over the user's screen. It gets a data folder
of its own there, empty, and its default settings plus what the user changed.

## What the host shows by itself

Without you doing anything:

- **entrances and exits** by reason (it leaves and is loaded again; an entrance counts as over once it came in and its body
  stopped moving, at most 7 s; after an exit it comes back with `Reloaded`, without a show);
- the **states** for a few seconds (talking, with a made-up voice level);
- the four **reactions**;
- the **gestures** that need no window: `Announce`, `Communicate`, `Mark` on the clock, `PointAt` the tray, `GoTo` the mouse,
  `Scan` the notes window, `Typing` into it;
- what it does **to a window**, as a step of an order: `PressButton` to minimise, maximise and close the notes window,
  `MoveWindow` it to the other side, `ActivateWindow` the photos, and `WaitForWindow` while the calculator opens. Your
  gesture calls `Commit` at the right moment and the window changes then. What you cannot do, the stage does anyway and the
  bubble says so, as in use.

## Its own animations

A dance, a trick, how it plays with the ball… List them in `Showcase` (id, name per language and group: `Idle`, `Moving`,
`Playing`, `Feelings`, `Tricks` or `Other`) and play them in `PlayShowcaseAsync`: start at once, whatever you were doing,
finish when it is over (K-Desktop waits up to 30 s) and stop right away if the token is cancelled; return `false` if you
do not know the id or cannot now.

```csharp
public IReadOnlyList<ShowcaseItem> Showcase { get; } =
    [new("dance", new Dictionary<string, string> { ["en"] = "Dance", ["es"] = "Bailar" }, ShowcaseGroup.Tricks)];

public async Task<bool> PlayShowcaseAsync(string id, CancellationToken ct)
{
    if (id != "dance") return false;
    StartDance();
    try { await Task.Delay(2500, ct); }
    finally { StopDance(); }
    return true;
}
```

Assistants without code show as their own the animations of their `character.json` that nothing else shows (`walk`,
`sleep`, `surprised`, `click`, `catch`).

## Clips for the catalog

In the manifest, `"showcase"` lists short clips of what it shows (an animated WebP, a GIF or a PNG of the package, 8 MB at
most each), so the catalog can show them without loading your code or downloading the whole package. `item` is the id of
what it shows: one of K-Desktop's (`enter.summoned`, `state.thinking`, `react.celebration`, `gesture.announce`,
`window.minimize`…) or one of your own, which then carries `name` (per language) and `group`.

```json
"showcase": [
  { "item": "enter.summoned", "clip": "showcase/enter.webp" },
  { "item": "dance", "clip": "showcase/dance.webp", "name": { "en": "Dance", "es": "Bailar" }, "group": "tricks" }
]
```

On install K-Desktop checks that each clip is there and is a picture, and that no item has two. The catalog shows them in
each assistant's detail window, where the user picks which one to watch: only the clips opened are downloaded (by HTTP
ranges), never the whole package. Installed assistants leave the clips out: nothing but the catalog uses them.

## Recording them

You do not have to make them by hand. Build with the showcase switched on and the SDK's build targets put the built package
on the stage of a K-Desktop you name, off the screen, have it do each thing it can show, record a clip of each one it does
(lossless animated WebP, 25 frames a second, at most 10 s) into `Package\showcase\`, and rewrite the `"showcase"` of
`Package\assistant.json` (only that part of the file) before packing:

```powershell
dotnet build -c Release -p:AssistantShowcase=true -p:KDesktopExe="C:\Path\To\KDesktop.exe"
```

It takes a few minutes. The clips are recorded in English (the bubble's text, the stage's windows);
`-p:AssistantShowcaseLanguage=es` (or `fr`, `de`, `it`, `pt`) records them in another one. They stay in your sources, so
later builds just pack them; record again when what it shows changes. What it cannot do is not recorded.

Without code, run the stage yourself on your package folder:

```powershell
KDesktop.exe --assistant-showcase <folder> --record <folder>\showcase --manifest <folder>\assistant.json
```

Long lively animations make big clips (a creature that is never still: about 1 MB a second), and they all travel in the
package: keep your own ones short.
