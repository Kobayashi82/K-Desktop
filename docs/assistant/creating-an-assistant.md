# Creating an assistant

An assistant is a `.kai` package: a `.zip` with its manifest (`assistant.json`), its pictures and settings, and its code
in `lib/`. It is written in C# against the SDK (`src/KDesktop.Assistant.Sdk`), which K-Desktop always provides: a package
never carries it. Until the SDK is on NuGet, reference a copy of this repository.

Only want a character that strolls around the desktop, without programming? See **[Without code](#without-code)**, right
below.

The examples:

| | |
|---|---|
| `templates/kdassistant` | The `dotnet new` template: the smallest complete assistant with code. It draws itself, has a click area, talks, reacts, changes colour with each state and shows a rainbow in its showcase. |
| `samples/CharacterAssistant` | **Pip**, an assistant without code: pictures and JSON (its frames are drawn by `tools/character/make-pip.ps1`). |

## Without code

An assistant can be just pictures and JSON: instead of an assembly, its `entry` is a `character.json`
([schema](schemas/character.schema.json)) and K-Desktop moves it. It stands on the taskbar or on top of the windows,
strolls now and then (and sometimes falls off the edge of a window), follows the window it stands on, can be dragged and
dropped, sleeps when you are away, and plays an animation for each thing: listening to you, thinking, talking in its
bubble, being glad or sorry about what happened, a click. It talks, thinks and uses tools like any other assistant (its
voice and personality are in «Personality and voice»).

```
MyCharacter\
  assistant.json      "entry": "character.json", "traits": { "travel": "walk", … }
  character.json
  icon.png
  frames\idle0.png …  (PNG with transparency, all the same height)
```

```json
{
  "height": 80,
  "faces": "right",
  "walkSpeed": 60,
  "animations": {
    "idle": { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2 },
    "walk": { "strip": "frames/walk.png", "frames": 6, "fps": 10 },
    "talk": { "frames": ["frames/talk0.png", "frames/talk1.png"], "fps": 8, "sound": "sounds/hello.wav" }
  }
}
```

- `height`: its height at 100 % scale; every frame is drawn this tall. Its feet are the bottom edge of the picture,
  unless `ground` says otherwise.
- `ground`: transparent rows under its feet in the pictures (pixels of the pictures, default 0). That much of each
  picture goes below the ground, so the feet touch it (a shadow under the feet can be left there).
- `faces`: the way the pictures look; walking the other way, they are mirrored.
- Animations: `idle` (required), `walk`, `talk`, `listen`, `think`, `happy`, `sad`, `surprised`, `click`, `drag`, `fall`,
  `sleep`, `enter`, `exit`, `announce`, `catch`. A missing one uses another (`think` → `listen` → `idle`, `click` → `happy`
  → `idle`, `announce` and `catch` → `happy`, `drag` → `fall` → `idle`…). Each one has `frames` (a list of pictures) or
  `strip` + `frames` (one strip of N frames of the same width), `fps`, `loop` (by default all repeat except `happy`, `sad`,
  `surprised`, `click`, `enter`, `exit`, `announce` and `catch`) and `sound` (a WAV played when it starts). Before saying
  something on its own (a timer, a notice) it walks along where it stands towards the mouse and plays `announce`; with files
  dragged over it, it carries their picture over its head and, when they are dropped, takes it in and plays `catch`.
- **Variants** (SDK 1.20: put `"sdk": "1.20"`): instead of one animation, a list. Each time it plays, K-Desktop picks one
  by its `weight` (twice the weight, twice as often; 1 by default), respecting its `cooldown` (seconds it rests after
  coming up) and never repeating the last one for those that play once; those that repeat (`idle`, `walk`, `talk`…) pick
  again at the end of each round. In `enter` and `exit`, `when` says which reason each one is for: `startup` (K-Desktop
  starts or the module is switched on) and `summoned` (you put it on the desktop); `dismissed` (you hide it) and
  `uninstalling` (you remove it). With none for that reason there is no entrance or exit; coming back after a reload (a new
  version, another language) never has one.

  ```json
  "idle": [
    { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2, "weight": 6 },
    { "frames": ["frames/look0.png", "frames/look1.png"], "fps": 2, "cooldown": 20 }
  ],
  "enter": [
    { "frames": ["frames/wake0.png", "frames/wake1.png"], "fps": 4, "when": ["startup"] },
    { "frames": ["frames/drop0.png", "frames/drop1.png"], "fps": 8, "when": ["summoned"] }
  ]
  ```
- Size, volume, how lively it is and whether it can be dragged are set by the user on its card (see
  [Common settings](#common-settings)); a character takes them all.
- To make the package, zip the folder (with `assistant.json` at its root) with `tools\character\pack.ps1 <folder>`, which
  leaves `<id>-<version>.kai` with the paths as K-Desktop wants them, and open it with K-Desktop.
- Its install card says it has no code. Nothing of the package runs: K-Desktop only reads its pictures and sounds.

## Getting started

```powershell
dotnet new install "<K-Desktop>\templates\kdassistant"
dotnet new kdassistant -n MyAssistant --sdk "<K-Desktop>\src\KDesktop.Assistant.Sdk" --authorName "Me"
cd MyAssistant
dotnet build          # → bin\Debug\…\myassistant-1.0.0.kai
```

The project has:

| | |
|---|---|
| `MyAssistant.cs` | The class that implements `IAssistant`: it draws itself, has a click area and knows `Communicate` and `React` |
| `Package\assistant.json` | The manifest ([schema](schemas/assistant.schema.json)): id, version, entry, gestures… |
| `Package\…` | Everything else that goes into the package as it is: `icon.png`, `settings.json`, `panel.json`, `persona\`, `voice\`, `assets\` |

Every build leaves the package in `bin\…\package\` exactly as it is installed and, next to it, the `.kai`.

## Trying it without installing

```powershell
KDesktop.exe --assistant-dev "<project folder>"
```

It loads the newest `bin\…\package` (a copy, so you can keep building) and **reloads it on every build**. If K-Desktop is
open with the same data folder, it connects to it; otherwise it works alone. The log is `Logs\assistant.log` in the data
folder. `KDesktop.exe --assistant --exit` closes it.

## Installing

With the Assistant module on, double-clicking the `.kai` (or `KDesktop.exe --install-assistant "file"`) opens its card in
K-Desktop. Before showing it, K-Desktop checks the package **without loading its code**: safe paths and sizes;
`assistant.json`, `settings.json`, `panel.json`, the persona and the voice against their schemas; that the entry DLL, the
pictures (PNG or JPEG) and the showcase clips are there; the SDK's major version; and that the id is not reserved
(`data`, `settings`, `dev`, `kdesktop`). If something is wrong, the card says what.

It is installed in `Assistants\<id>\<version>\`. A new version replaces the old one (also live, if it is on the desktop)
and keeps `Assistants\Data\<id>\` and its settings; whatever is still in use is deleted when the assistant process lets go
of it. K-Desktop's backups keep that data and the list of installed assistants, not the packages.

## Publishing new versions

If the manifest says where the latest package always is, K-Desktop looks for new versions together with its own (at
start, every 6 hours and with «Check now» in Settings → Updates) and **offers** them: a notice, a bar in the window, the
assistant's row in Settings → Assistants and Settings → Updates, with «Update…» (downloads it and opens its card) and
«Skip this version». It never updates an assistant without asking, even with K-Desktop on automatic updates.

```json
"updates": "https://github.com/me/my-assistants/releases/latest/download/myassistant.kai",
"changes": { "en": "Jumps higher and talks faster.", "es": "Salta más alto y habla más rápido." }
```

- `updates` is a fixed address that always gives the latest `.kai` (on GitHub, the `releases/latest/download/…` one).
  https; `http://localhost` and `http://127.0.0.1` work for testing with your own server.
- To check it, K-Desktop **only reads the `assistant.json`** of the published package, with range requests (a few KB); a
  server that does not support them sends the whole package every time.
- It is offered if the version is higher and the id is the same. If it needs a newer SDK than that K-Desktop has, the
  user is told to update K-Desktop first.
- `changes` (optional, in one or several languages) is what that version brings: it shows in the notice and on the card.

## Putting it in a catalog

Settings → Catalog shows the assistants of the catalogs it reads (K-Desktop's and any the user adds) with «Install…» and
«Update…» (downloads it and opens its card, as always). A catalog is a JSON published at a fixed address
([schema](schemas/catalog.schema.json)) that only says where each package is:

```json
{
  "catalog": 1,
  "assistants": [
    { "id": "myassistant", "package": "https://github.com/me/my-assistants/releases/latest/download/myassistant.kai" }
  ]
}
```

- `package` is the same address as `updates`: the one that always gives the latest `.kai`.
- Of each package K-Desktop only reads `assistant.json` and the icon, by ranges: the name, description, version, author
  and whether it works with that K-Desktop come from the package itself. If the `id` does not match the package's, it is
  not offered.
- Being in a catalog does not make a package trustworthy: the card says whether it is **verified** (see
  [Signing the package](#signing-the-package)) and what it can do before installing it.

## Signing the package

A signed package carries `signature.json` with the SHA-256 of **all** its files, who publishes it, its public key (ECDSA
P-256) and the signature. Nobody has to be paid: the key is yours. K-Desktop checks it when it opens the card:

| What it finds | What it shows |
|---|---|
| A good signature by a key on its list (`trusted-publishers.json`) | **Verified · name**, also in Settings → Assistants |
| A good signature by another key | «Signed by "name"», and the usual warning: that name is only what the package says |
| No signature | «Not signed» and the usual warning |
| A signature that does not match (a file changed, added or removed after signing) | **It is not installed** |

If the installed version was signed and the new one is not signed by the same key, the card warns in red.

With `tools/KaiSign` (in the K-Desktop repository):

```powershell
dotnet run --project tools\KaiSign -- keygen "Your name"      # once: the key stays in your Windows user (DPAPI)
dotnet run --project tools\KaiSign -- sign myassistant.kai     # after building, before publishing
dotnet run --project tools\KaiSign -- verify myassistant.kai
dotnet run --project tools\KaiSign -- export copy.pem "password"   # a copy of the key, to keep somewhere else
```

Sign **last**: any change to the package afterwards breaks the signature. For your packages to show as verified, your
public key (`kaisign public`) has to be on K-Desktop's list.

## Coming in and leaving

`Enter(how)` and `ExitAsync(why, ct)` say **why** it arrives or leaves (SDK 1.9):

| `EntranceKind` | When | What to do |
|---|---|---|
| `Startup` | K-Desktop starts or the module is switched on | Its entrance |
| `Summoned` | The user puts it on the desktop | An entrance that says «here I am» |
| `Reloaded` | It was there a moment ago and comes back: a new version, another language, the process restarted or recovered from a crash, rebuilt with `--assistant-dev` | Reappear where it was, without a show |

| `ExitKind` | When | What to do |
|---|---|---|
| `Shutdown` | K-Desktop closes or the module is switched off | Leave at once |
| `Dismissed` | The user hides it (Settings, the tray, its menu) | Its goodbye |
| `Uninstalling` | It is uninstalled (its data folder may be deleted right after) | The last goodbye |
| `Reloading` | It comes back at once with `Reloaded` | Leave at once and save in `DataDirectory` where it was: the new one may be another process |

The host waits for the exit 3 s at most (then the token is cancelled).

## Common settings

What every assistant has the same way does not go in its `settings.json`: K-Desktop shows it in the «Basics» tab of its
card, with the same units for all (SDK 1.9). The manifest says which ones it takes:

```json
"common": ["size", "volume", "liveliness", "dragging"]
```

| Name | What it is | Who does it |
|---|---|---|
| `size` | Size, from 50 % to 200 % | The assistant: `host.Common.Size` (1 = the size it is drawn for), on top of the monitor's scaling as always |
| `volume` | Volume of its sounds, from 0 % to 100 % | The host, on everything played through `IAudio` (its voice is separate) |
| `liveliness` | How lively it is, from 0 % (as little as possible) to 200 % | The assistant: `host.Common.Liveliness` (1 = normal) for what it does on its own |
| `dragging` | Picking it up with the mouse | The host does not pass on the drags of its `body` area when it is off; `host.Common.Dragging` for the rest (other areas, like a toy, can still be dragged) |

When they change, `OnSettingsChanged` arrives with `host.Common` already up to date. An assistant without code takes
them all.

## What the host gives

- **Bubble:** when the assistant says something (`Communicate`), the host draws the bubble with the text, pointing at
  `BubbleAnchor` (usually the top of its head; null when it is not visible) and following it while it moves, for as long as
  it takes to read. The assistant only plays its «talking» gesture (or nothing: the bubble shows anyway). A middle click on
  the character opens the box to write to it (and so does *Write* in its right-click menu): left clicks and double clicks
  are yours. Holding the left button down without moving it (450 ms) is talking to it: you get `Down` and `Up`, but no
  `Click` or drag.
- **Its own text** (SDK 1.15): with `"speech": "own"` in the manifest you draw what it says yourself (a bubble of your
  own, a sign, the words inside your cloud…). The host calls `ShowSpeech(SpeechText)` each time the answer grows while it is
  being written (`Finished` false), with all of it when it is ready (`Finished` true) and again with a later `Until` while
  your voice is still saying it; each call replaces the text of the one before. Show it until `HideSpeech` arrives: the host
  still keeps the timing, the voice and the chat. `Text` is plain text and arrives whole even when long: cut it to what fits
  (all of it is in the chat). If you cannot now (you are hidden, in the middle of a manoeuvre, it does not fit), return
  `false`: the host shows its bubble for the rest of that answer. In the «Basics» tab of your card the user can choose
  «Always use K-Desktop's bubble», and then nothing reaches you. An assistant without code always uses the host's bubble.
- **Long answers**: when the user asks for something long («explain it in detail», «make me a list», «draft…») or the
  answer does not fit a bubble, the bubble, your own text and the voice get only a summary of one or two sentences; K-Desktop
  shows the whole text on a reading sheet next to you (the point of your `BubbleAnchor`), where it can be copied, kept as a
  note or read aloud. Nothing to do on your side.
- **State** (`SetState`): K-Desktop tells you whether it is listening to you (`Listening`: its word, its key to talk or the
  long click), whether you are thinking (`Thinking`), talking (`Speaking`) or back to rest (`Idle`). Show it your way (the
  Lander lights up its antenna; the template changes colour).
- **States with detail** (SDK 1.13): implement `SetState(AssistantState state, StateDetail detail)` and the host calls that
  one instead of the usual one. These arrive as well:
  - `ThinkingHard`: it is thinking slowly (the AI asked for it or the user said «think it over»); it can take half a minute.
  - `LoadingBrain`: its model is being loaded into memory before it can think (from a few seconds to a minute).
  - `NoBrain`: the order needed the AI and there is none; it comes right before `Speaking`, which says so (a shrug).
  - `Busy` with `detail.Activity` while a tool or a command runs (`SearchingWeb`, `ReadingWeb`, `Files`, `Notes`, `Memory`,
    `Weather`, `Music`, `Timer`, `Screen`, `Command`…; those of an MCP server or the user's own, `Other` with
    `detail.Tool`), and `AwaitingConfirmation` while one asks for permission.
  - `Listening` with `Dictation` while K-Desktop types what the user says («write what I tell you»).

  `detail.Text` says it in its language («Searching the web»), in case you write it yourself. Later versions may add
  activities: treat the ones you do not know as `Other`. An assistant that only has the old `SetState` goes on the same:
  it gets the closest state it knows (`AssistantStates.Basic`: the three new ones are `Thinking`).
- **Sound** (`IAudio`): `Load` (floating-point samples, mono or stereo, any rate) or `LoadWav` (from the package), `Play`
  with gain and pan, and `Loop` with live gain, pitch and pan (for an engine). The host mixes it all under the volume of
  Settings → Assistant → General; the device is only open while something plays. With sound off, `Enabled` is false and
  everything plays silence.
- **Click areas** (`Pointer.CreateHitArea(id)`): each one with its `id`, which arrives in `PointerEvent.Area` (with several —
  the body and some toys, say— give them different names, or you will not know where each click comes from). Over the area
  the cursor is the one you say (`Cursor`: arrow, hand, grab) or, since SDK 1.8, one you draw: `SetCursorImage(image,
  hotspot)` with a `BitmapSource` in monitor pixels (Windows cursors are 32 at 100 %; Kobii shows a brush while being
  brushed); `null` goes back to the usual one.
- **Files dropped on it** (SDK 1.16): click areas take files dragged from Explorer (the assistant process runs as a normal
  user; K-Desktop, as administrator, could not receive them). They reach `OnPointer` as `FilesOver` (they are over it: the
  moment to reach out), `FilesAway` (they left without being dropped, or the user pressed Escape) and `FilesDropped` (they
  were dropped: the moment to catch them), with the full paths in `PointerEvent.Files` and the area in `Area`. It is only
  the animation: what to do with them is decided by K-Desktop. They are never taken as «move»: the file stays where it
  was. An assistant built with an older SDK does not see these events.
  **The picture of what is dragged** (SDK 1.17): while the files are over your assistant, K-Desktop hides the picture
  Windows draws under the cursor (the thumbnail or the icons, which covered it) and leaves only a small label next to the
  cursor, «Give it to <your name>»; if they leave without being dropped, it comes back. That picture reaches you in
  `PointerEvent.Preview` (an opaque, frozen `BitmapSource` in screen pixels; null if the source program gave none) with the
  three events, to show it your way: smaller, in its hands, hanging from a beam… and take it in when it is dropped. Not
  showing it is fine too. The Lander holds it with its tractor beam and lifts it when dropped, Kobii raises it over its hands
  and assistants without code carry it over their head.
- **Keyboard** (`IKeyboard`): `IsDown` always; `Capture` makes K-Desktop swallow those keys (pressed without Ctrl, Alt or
  Win, so shortcuts still work) while playing, and their changes reach `KeyChanged`. `Release` when done (they are also
  released on their own if the assistant leaves, the process crashes or K-Desktop is paused).
- **Mouse wheel:** `SystemEvent.FastScroll` always arrives when the user turns the wheel hard (with the cursor position);
  each turn (`SystemEvent.WheelTurned`, with the window under the cursor) only with `Pointer.WatchWheel = true`: it costs a
  message per notch, so only while it matters. They come from K-Desktop's hook, which also sees the wheel over
  administrator windows.
- **Windows** (`IWindowServices`): `TryMove` and `TryCommand` (minimize, maximize, restore, bring to front; SDK 1.3 —
  those of an administrator process, and bringing to front, are done by K-Desktop for you—), the title-bar buttons (from
  DWM), `Thumbnail` (a frozen picture of the window; if the window cannot be drawn, what is seen of it on screen) and
  `AppIcon` (its program's icon, cached).
- **Gestures of the commands** (`CanPerform` / `PerformAsync`): when the user asks you for a K-Desktop command, each action
  arrives as one or several gestures (minimize → `PressButton(Minimize)`, place → `MoveWindow` with the exact target, a
  maximized one first `PressButton(Restore)`…). Call `ctx.Commit()` **at the instant of the effect** (when the shot hits):
  the action happens right then, and `ctx.Committed` tells you whether it went well. Or do it yourself (`TryMove`) and
  return `PerformedSelf`: K-Desktop then sets it to the pixel. What you cannot do (`CanPerform` false) the host shows with
  the bubble while K-Desktop does it. You have `ctx.TimeLeft`: if you run over, fail or do not answer, K-Desktop does it
  anyway and asks you for a `React(Warning)`. You can never leave an order half done. `ctx.Speed` is the speed the user
  chose (`Showy`, `Normal`, `Fast`): in `Fast`, `Commit` within half a second (after that K-Desktop does it anyway). With
  «Also show the commands you run yourself» on, commands run by other means reach you as gestures too, always in `Fast`:
  they are only decoration, nothing waits for them and their `Commit` does nothing.
- **Gestures of the tools**: what the AI's tools do reaches you as gestures too, with `ctx.Step` 0: a timer → `Mark` by the
  clock (`Place.Clock`) with the time it rings; removing it → `PointAt(Place.Clock)`; opening a website, a search, a program
  or a file → `WaitForWindow` (it comes right before opening it; `ctx.Appeared` gives the new window or null); what is
  playing or controlling the music → `GoTo(Place.Tray)`; «tell me when … closes» → `Mark` on that window; rewriting a text
  and pasting it → `Typing` in that window; reading the screen → `Scan` of what was read. JSON tools can ask for one with
  `"gesture"`. They are only decoration, like the ones above: the tool never waits for you, `Commit` does nothing, a step of
  an order takes their place and a new one replaces the one before. What you cannot do (`CanPerform` false) is not shown.
- **Catching the eye** (`Gesture.Announce`, SDK 1.14): before saying something on its own (a timer ringing, a «tell me
  when» coming true, «Assistant: say») the host asks you to catch the user's eye: walk up to the mouse, wave, blink…
  `Reason` says why (`Timer`, `Watch`, `Message`), `Cursor` where the mouse was (or null) and `UserAway` how many seconds
  without keyboard or mouse (the longer, the bigger it may be: maybe they are not looking). The host waits for you to
  finish, `ctx.TimeLeft` at most (2 s, 3 in `Showy` and half a second in `Fast`), and then talks with `Communicate`: finish
  as soon as you have caught the eye. There is nothing to confirm (`Commit` does nothing). If it is already talking, a step
  of an order is in your place or you cannot do it (`CanPerform` false), it just talks.
- **Rope** (`Rope`, SDK 1.4): a Verlet rope in screen pixels for hooks, cables and leashes. You hold its start (`Start` on
  each tick); its end hangs (`ReleaseEnd`, with the speed you let it go at) or is held (`PinEnd`: stuck on a window, on a
  projectile). It only resists stretching (it sags and slackens), never goes past `MaxLength` with a loose end and steps at a
  fixed 120 Hz (`Step(tick.Elapsed)`), so it behaves the same at any frame rate. `Length` to reel in or let out,
  `Gravity`, `Damping`, `Iterations`; `IsTaut`, `Overstretch` and `Pull` (towards the start, what is over its length: what
  a towed window follows). Draw it with `Draw(dc, pen)` (a smooth curve); `Bounds(margin)` gives the size of the surface.
- **Context of the reactions** (SDK 1.12): `Gesture.React` carries `Context` (`ReactionContext`), filled in by the host:
  `Source` = where it comes from (`Order`, a command or a K-Desktop action; `Tool`, a tool such as the timer, a note or the
  web; `Notice`, something it says on its own, such as a timer ringing; `User`, what the user says about it: «thanks»,
  «well done» → `Celebration`, «that's wrong» → `Failure`), `Streak` = how many in a row of the same kind (1 the first; back
  to 1 with another kind or after half an hour without reactions), `FirstToday` = the first one today, and `What` = what it
  was when known (`set_timer`, `run_command`, `timer`, `praise`…). The host reacts once per order, when the answer is
  ready: something done and nothing failed → `Success`; done but something failed → `Warning`; nothing done and something
  failed → `Failure` (a failure the AI fixes within the same order does not count); only looking something up (the time,
  the weather) does not react. Use it with `Variety` not to react the same way every time: something bigger the first time
  today or on a long streak, more downcast at the third failure in a row. When the host does not know where it comes from,
  `ReactionContext.Unknown` arrives.
- **Room for everyone** (SDK 1.11): with several assistants on the desktop, none stays on top of another. Say where your
  body is with `BodyBounds` (only the body: no particles, bubbles or effects; null when you are not visible). If you do not,
  the host uses the bounds of your `body` click area, which may not always be there. `World.Others` says where the body of
  each of the others is (`Id`, `Body`) and whether it is on its way somewhere (`Moving`: it moved in the last half second).
  Before landing, stopping or walking to a place, look at `World.TakenBy(rectangle)`: it gives the one that stays there, or
  null when it is free (one passing by does not count unless `moving: true`). Passing in front is fine; staying on top is
  not: pick another place, or step aside if on arriving someone was already there. An assistant without code does it on
  its own.
- **Variety** (`Variety<T>`, SDK 1.10): not to do the same thing every time (entrances, reactions, idle animations, lines).
  Each variant with its weight (`Add(x, weight: 2)` comes up twice as often as one of 1; 0 = never) and its cooldown in
  seconds (`cooldown: 120`: it does not come up again for two minutes). `Pick()` picks at random by weight, never the same
  one twice in a row while there is another (`NoRepeat`), and if all are resting it gives the one ready soonest: something
  always comes out. `TryPick` picks nothing if all are resting. `Pick(x => …)` picks only among those that fit now (on the
  taskbar, on top of a window…). `Use(x)` marks one as done although it did not come from there, `CooldownLeft`, `Reset`.
  With `new Variety<T>(seed: 1)` the sequence is always the same (tests); `clock` changes the clock of the cooldowns.
- **One drawing system** (SDK 1.6): `Present` with WPF's drawing API. The last frame stays on screen even if the GPU is lost
  or a full-screen game ends (the host repeats it).
- **Music** (SDK 1.7): with `World.ListenToMusic(true)`, `World.Music` says whether music with a beat is playing (`Playing`;
  a voice or noise does not count), how many beats per minute (`Bpm`) and the beat (`BeatAt(tick.Now)`: it grows by one per
  beat and its fraction is the phase, 0 = the hit), to dance in time. The host reads the meter of each program that plays
  (the bars of Windows' volume mixer: nothing is recorded), without the assistants' sounds and voices; a hundred times a
  second while something plays and four in silence, and only while some assistant listens: stop listening
  (`ListenToMusic(false)`) when you are not visible. It takes some 3 s to find the beat.
- **Recycle bin:** `SystemEvent.RecycleBinEmptied` carries where its icon is on the desktop (or null when it is not there or
  the icons are hidden).

## Showcase

On the card of each installed assistant, the **«Try»** tab shows it on a **stage**: a small pretend desktop of its own
(720×405 at 125 %, with a taskbar, a tray, a clock and three pretend windows: Notes, Photos and a Calculator that opens when
asked), drawn off the screen by a separate copy of your assistant (`KDesktop.exe --assistant-showcase`) and shown live in
the tab. The user can pet it or pick it up with the mouse over the picture, and there is a button for each thing it can
show (one at a time: another one, or «Stop», cuts the one before). Your assistant does not know it is on a stage: it gets
the stage's monitor, taskbar and windows in its `World`, moves and presses them through `IWindowServices` as usual, and
nothing of it ever lies over the user's screen. It gets a data folder of its own there, empty, and its default settings
plus what the user changed.

The usual things are launched by the host without you doing anything: entrances and exits by reason (it leaves and is
loaded again; an entrance counts as over once it came in and its body stopped moving, at most 7 s; after an exit it comes
back with `Reloaded`, without a show), the states for a few seconds (talking, with a made-up voice level), the four
reactions, the gestures that need no window (`Announce`, `Communicate`, `Mark` on the clock, `PointAt` the tray, `GoTo` the
mouse, `Scan` the notes window, `Typing` into it) and what it does to a window, as a step of an order (`PressButton` to
minimize, maximize and close the notes window, `MoveWindow` it to the other side, `ActivateWindow` the photos, and
`WaitForWindow` while the calculator opens): your gesture calls `Commit` at the right moment and the window changes then.
What you cannot do, the stage does anyway and the bubble says it, as in use (it shows as «it can't do that»).

**Your own animations** (SDK 1.18) come first: a dance, a trick, how it plays with the ball… List them in `Showcase` (id,
name per language and group: `Idle`, `Moving`, `Playing`, `Feelings`, `Tricks` or `Other`) and play them in
`PlayShowcaseAsync`: start at once, whatever you were doing, finish when it is over (K-Desktop waits up to 30 s) and stop
right away if the token is cancelled; `false` if you do not know the id or cannot now. Both have a default body (nothing
of its own). Assistants without code show as their own the animations of their `character.json` that nothing else shows
(`walk`, `sleep`, `surprised`, `click`, `catch`).

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

**Clips for the catalog** (SDK 1.19: put `"sdk": "1.19"`): in the manifest, `"showcase"` lists short clips of what it
shows (an animated WebP, a GIF or a PNG of the package, 8 MB at most each), so the catalog can show them without loading
your code or downloading the whole package. `item` is the id of what it shows: one of K-Desktop's (`enter.summoned`,
`state.thinking`, `react.celebration`, `gesture.announce`…) or one of your own, which then carries `name` (per language)
and `group`. On install K-Desktop checks that each clip is there and is a picture, and that no item has two.

```json
"showcase": [
  { "item": "enter.summoned", "clip": "showcase/enter.webp" },
  { "item": "dance", "clip": "showcase/dance.webp", "name": { "en": "Dance", "es": "Bailar" }, "group": "tricks" }
]
```

The catalog shows them in each assistant's detail window, where the user picks which one to watch: only the clips opened are
downloaded (HTTP ranges), never the whole package. On install the clips are left out: nothing but the catalog uses them.
Clips of what it does to a window (`window.minimize`, `window.maximize`, `window.close`, `window.move`, `window.activate`,
`window.open`) need `"sdk": "1.21"`.

**Recording them** (SDK 1.21): you do not have to make them by hand. Build with the showcase switched on and the `.targets`
puts the built package on the stage of a K-Desktop you name, off the screen, has it do each thing it can show, records a
clip of each one that it does (lossless animated WebP, 25 frames a second, at most 10 s) into `Package\showcase\` and
rewrites the `"showcase"` of `Package\assistant.json` (only that part of the file) before packing:

```
dotnet build -c Release -p:AssistantShowcase=true -p:KDesktopExe="C:\Path\To\KDesktop.exe"
```

It takes a few minutes. The clips are recorded in English (the bubble's text, the stage's windows);
`-p:AssistantShowcaseLanguage=es` (or `fr`, `de`, `it`, `pt`) records them in another one. They stay in your sources, so
later builds just pack them; record again when what it shows changes. What it cannot do is not recorded. Without code, run
the stage yourself on your package folder: `KDesktop.exe --assistant-showcase <folder> --record <folder>\showcase --manifest
<folder>\assistant.json`. Long lively animations make big clips (a creature that is never still: about 1 MB a second), and
they all travel in the package: keep your own ones short.

## Settings and panel

K-Desktop draws them with its own controls, without loading any code of the assistant:

- **`settings.json`** ([schema](schemas/settings.schema.json)) → Settings → Assistants → the assistant's card (or its
  right-click menu → *Settings*). Pages (tabs if there are several), sections and controls: `toggle`, `slider`, `number`,
  `choice`, `text`, `color`, `gallery`, `apps`, `hotkey`, `button` (runs an extra or, with `resetData`, deletes its data
  folder after asking), `info` and `keys`. `enabledWhen` dims a control while a switch is off. What the user changes is
  saved in `Assistants\Settings\<id>.json` (only what differs from the default) and, if it is on the desktop, reaches it at
  once: `OnSettingsChanged` with **all** the values.
- **`panel.json`** ([schema](schemas/panel.schema.json)) → Panel → Assistant, under its card: `stat` (in a row),
  `progress`, `list`, `achievements`, `buttons` and `text`. The data arrives with `PublishPanel`, which is **merged** with
  what was published before: publish everything in `Enter` and then only what changes. A gallery with `source` takes its
  pictures from that same data (`[{ id, name, image, locked, lockText }]`).
- **Extras** (`extras` in the manifest): its own actions, which show in its right-click menu, in its panel and as
  «Assistant: action» in K-Desktop's commands; with `hotkey`, the install card offers to create a command with that
  shortcut. They arrive in `OnExtraAsync`. **Extras by the moment** (SDK 1.1): `SetExtraState(id, Shown | Disabled |
  Hidden)` shows, dims or hides an extra in the menu and the panel's buttons; after each load all go back to `Shown`.
  `ExtraHotkey(id)` gives the shortcut of the K-Desktop command that runs it («CTRL + ALT + L»), to tell the user what to
  press. Commands and voice still reach `OnExtraAsync` while it is hidden: explain there why it cannot be done.
- **Its voice:** what it answers is said aloud by the host, with the `voice` profile of its manifest (`voice/<id>.json`,
  [schema](schemas/voice.schema.json): an engine and a chain of effects —`pitch`, `formant`, `ring`, `vocoder`,
  `bitcrush`, `radio`, `chorus`, `flanger`, `echo`, `glitch`— and `moods`, the effects added in some situations).
  `OnVoiceLevel` says how loud it sounds while it talks (for lights or a mouth). With SDK 1.2, `SetVoiceMood("error")`,
  `SetVoiceMood("happy")` or `SetVoiceMood(null)` change the mood of what it says from then on. Its personality, in
  `persona/{lang}.json` ([schema](schemas/persona.schema.json)).
- **What the user changes:** in Settings → Assistants → yours → «Personality and voice» the user can write it another
  personality (your fixed lines stay), give it another language and another voice (engine, voice, profile, speed). With
  another language, `host.Language` is that one and not K-Desktop's: read it in `Attach` (when it changes, the host loads
  the assistant again).
- **The model it thinks with:** `"model": { "suggested": [...] }` in the manifest, models of Hugging Face
  («owner/repo/file.gguf» or «owner/repo:Q4_K_M»), the best first. K-Desktop offers (unticked) the first one that fits in
  the user's graphics card, with its vision projector if there is one, and only if that file is not there yet; once it is
  downloaded, that assistant thinks with it. For example, the Lander: `["unsloth/Qwen3.5-9B-GGUF:Q4_K_M",
  "unsloth/Qwen3.5-4B-GGUF:Q4_K_M"]`. With `"think"` (SDK 1.19; the same: one or several, the best first) it suggests the
  model it **thinks harder** with («think it over», or when it sees it is not enough): K-Desktop offers it apart, unticked
  too, and once downloaded it is its own model to think harder with («Basics» tab of its card). If it is the same file as
  `suggested`, it is not offered twice; without it, it thinks harder with its usual model.

## Rules

- Everything is called on the host's **engine thread**, and `await`s come back to it. Nothing needs locking; never block
  that thread.
- `Tick` returns **how long it may sleep**: at rest, seconds; while animating, `Rendering.FrameInterval`. Only draw
  (`Present`) when something changes: the last frame stays on screen.
- Coordinates are **physical pixels** of the virtual screen. For sizes, the monitor's scaling (`MonitorInfo.DpiScale`).
- **You are not alone:** there may be other assistants on the desktop at the same time, in the same process and on the
  same layer. Do not take the whole screen or assume you are the only one; `World.Others` says where the others are (their
  click areas do not count as windows). What reaches you (gestures, orders, extras, settings) is yours alone: the host and
  K-Desktop decide who each thing is for.
- An assistant that fails five times in a row is unloaded (only it; the others go on); one that brings the process down
  three times in a row is taken off the desktop. When it is unloaded, the host removes whatever it left (surfaces, click
  areas, subscriptions).
- The assistant does not see the AI, the tools, the pipe or K-Desktop: only the SDK.
