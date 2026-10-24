# What the host gives

The host is the assistant process. Through `IAssistantHost` it gives the assistant everything it can see and use; the AI,
the tools, the voice and K-Desktop stay out of sight.

| Member | |
|---|---|
| `World` | Windows, monitors, taskbar, the other assistants, music, the system state (`IWorld`) |
| `Rendering` | Drawing surfaces over the whole desktop |
| `Pointer` | Click areas and the mouse wheel |
| `Keyboard` | Keys, and capturing them while playing |
| `Audio` | Its sounds |
| `Windows` | Moving and changing windows (`IWindowServices`) |
| `Settings`, `Common` | Its settings and the [common ones](getting-started.md#common-settings) |
| `Language` | The language it speaks in |
| `DataDirectory`, `PackageDirectory` | Its data folder (kept between versions and in backups) and its package |
| `PublishPanel`, `Notify`, `SetExtraState`, `ExtraHotkey`, `SetVoiceMood` | Its panel, a notice, its extras, its voice's mood |
| `RequestTick`, `Post`, `Log` | Wake up sooner, run something on the engine thread, write to `assistant.log` |

## Drawing

One drawing system: surfaces with WPF's drawing API. `Rendering.CreateSurface()` gives a surface; `Present(area, dc => …)`
draws a frame on it, in screen pixels. Only present when something changes: the last frame stays on screen (the host
repeats it after the GPU is lost or a full-screen game ends). `Rendering.FrameInterval` is the length of a frame of the
monitor.

## The bubble

When the assistant says something (`Communicate`), the host draws the bubble with the text, pointing at `BubbleAnchor`
(usually the top of its head; null when it is not visible) and following it while it moves, for as long as it takes to read.
The assistant only plays its «talking» gesture, or nothing: the bubble shows anyway.

A middle click on the character opens the box to write to it (so does *Write to …* in its right-click menu): left clicks
and double clicks are yours. Holding the left button down without moving it (450 ms) is talking to it: you get `Down` and
`Up`, but no `Click` or drag. The right button never reaches you: it opens its menu.

### Its own text

With `"speech": "own"` in the manifest you draw what it says yourself (a bubble of your own, a sign, words inside a
cloud…). The host calls `ShowSpeech(SpeechText)` each time the answer grows while it is being written (`Finished` false),
with all of it when it is ready (`Finished` true), and again with a later `Until` while your voice is still saying it; each
call replaces the one before. Show it until `HideSpeech` comes: the host still keeps the timing, the voice and the chat.

`Text` is plain text and arrives whole even when long: cut it to what fits (all of it is in the chat). If you cannot show
it now (hidden, in the middle of a manoeuvre, it does not fit), return `false`: the host shows its bubble for the rest of
that answer. On its card the user can choose *Always use K-Desktop's speech bubble*, and then nothing reaches you.

### Long answers

When the user asks for something long («explain it in detail», «make me a list») or the answer does not fit a bubble, the
bubble, your own text and the voice get only a short summary; K-Desktop shows the whole text on a reading sheet next to
your `BubbleAnchor`. Nothing to do on your side.

## States

`SetState(state, detail)` says what the module is doing, to show it your way (the Lander lights its antenna and shows an
icon; the template changes colour):

| `AssistantState` | |
|---|---|
| `Idle` | Back to rest |
| `Listening` | Its word, its key to talk or the long click: it is listening. With `detail.Activity` = `Dictation`, K-Desktop is typing what the user says |
| `Thinking` | The AI is working on an answer |
| `ThinkingHard` | The AI thinks it over, slowly: it may take half a minute |
| `LoadingBrain` | Its model is being loaded into memory before it can think |
| `NoBrain` | The order needed the AI and there is none; it comes right before `Speaking`, which says so (a shrug) |
| `Speaking` | It is answering |
| `AwaitingConfirmation` | A yes/no question is waiting for the user (the small window that asks for permission) |
| `Busy` | One of its tools or a command is running: `detail.Activity` says which (`SearchingWeb`, `ReadingWeb`, `Files`, `Notes`, `Memory`, `Weather`, `Music`, `Timer`, `Screen`, `Command`…; an MCP server's or the user's own: `Other` with `detail.Tool`) |
| `Sleeping` | |

`detail.Text` says it in its language («Searching the web»), in case you write it yourself. Later versions may add
activities: treat those you do not know as `Other`.

## Sound

`IAudio`: `Load` (floating-point samples, mono or stereo, any rate) or `LoadWav` (from the package), `Play` with gain and
pan, and `Loop` with live gain, pitch and pan (for an engine). The host mixes it all under the user's volume; the device
is only open while something plays. With sound off, `Enabled` is false and everything plays silence.

## Click areas

`Pointer.CreateHitArea(id)`, each with its `id`, which arrives in `PointerEvent.Area` (with several —the body and some toys,
say— give them different names; without an id it is `body`). Place it with a shape (`HitShape`) wherever the character is.
Over the area the cursor is the one you say (`Cursor`: arrow, hand, grab) or one you draw: `SetCursorImage(image, hotspot)`
with a `BitmapSource` in monitor pixels (Windows cursors are 32 px at 100 %; Kobii shows a brush while being brushed);
`null` goes back to the usual one. `OnPointer` gets entering, leaving, clicks and drags.

## Files dropped on it

Click areas take files dragged from the Explorer. They reach `OnPointer` as `FilesOver` (over it: the moment to reach
out), `FilesAway` (they left without being dropped, or the user pressed Escape) and `FilesDropped` (the moment to catch
them), with the full paths in `PointerEvent.Files`. It is only the animation: what to do with them is decided by
K-Desktop. They are never taken as «move»: the file stays where it was.

While the files are over your assistant, K-Desktop hides the picture Windows draws under the cursor and leaves only a small
label, «Give it to <name>». That picture reaches you in `PointerEvent.Preview` (a frozen `BitmapSource` in screen pixels;
null if the source program gave none), to show it your way: smaller, in its hands, hanging from a beam… The Lander holds it
with its tractor beam, Kobii raises it over its hands, assistants without code carry it over their head. Not showing it is
fine too.

## Keyboard and wheel

- `IKeyboard`: `IsDown` always; `Capture` makes K-Desktop swallow those keys (pressed without Ctrl, Alt or Win, so
  shortcuts still work) while playing, and their changes reach `KeyChanged`. `Release` when done (they are also released
  if the assistant leaves, the process crashes or K-Desktop is paused).
- `SystemEvent.FastScroll` arrives when the user turns the wheel hard (with the cursor position); each notch
  (`SystemEvent.WheelTurned`, with the window under the cursor) only with `Pointer.WatchWheel = true`: it costs a message
  per notch, so only while it matters.

## Windows

`IWindowServices`: `TryMove` (a window's visible part, for frame-by-frame dragging) and `TryCommand` (minimise, maximise,
restore, bring to front —those of an administrator process, and bringing to front, are done by K-Desktop for you—),
`GetCaptionButtons` (from DWM), `Thumbnail` (a frozen picture of the window) and `AppIcon` (its program's icon).

## The world

- `World.Current`: the monitors (bounds, work area, scaling), the windows with their bounds and the taskbars; `Changed`
  when it changes. `World.System`: the cursor and its speed, seconds without keyboard or mouse, the window in front, full
  screen… `OnSystemEvent` for what happens: a window opened, moved, activated or closed, a notification, the user left
  or came back, full screen, power and battery, the theme, the hour striking, the recycle bin emptied (with where its icon
  is, or null)…
- **Room for everyone**: say where your body is with `BodyBounds` (only the body: no particles, bubbles or effects; null when
  not visible). `World.Others` says where each of the others is and whether it is on its way somewhere (`Moving`). Before
  landing, stopping or walking to a place, look at `World.TakenBy(rectangle)`: it gives the one that stays there, or null
  when it is free. Passing in front is fine; staying on top is not.
- **Music**: with `World.ListenToMusic(true)`, `World.Music` says whether music with a beat is playing (`Playing`; a voice
  or noise does not count), its beats per minute (`Bpm`) and the beat (`BeatAt(tick.Now)`: it grows by one per beat and its
  fraction is the phase, 0 = the hit), to dance in time. The host reads each program's volume meter (nothing is recorded),
  without the assistants' own sounds. It takes some 3 s to find the beat. Stop listening when you are not visible.

## Helpers

- **`Variety<T>`**: not to do the same thing every time (entrances, reactions, idle animations). Each variant with its
  weight (`Add(x, weight: 2)` comes up twice as often; 0 = never) and its cooldown in seconds. `Pick()` picks at random by
  weight, never the same twice in a row while there is another, and if all are resting gives the one ready soonest;
  `TryPick` gives nothing then. `Pick(x => …)` picks only among those that fit now. `Use`, `CooldownLeft`, `Reset`; with a
  `seed` the sequence is always the same (tests).
- **`Rope`**: a Verlet rope in screen pixels for hooks, cables and leashes. You hold its `Start`; its end hangs
  (`ReleaseEnd`, with the speed you let it go at) or is held (`PinEnd`). It only resists stretching, never goes past
  `MaxLength` with a loose end and steps at a fixed 120 Hz (`Step(tick.Elapsed)`), so it behaves the same at any frame
  rate. `Length` to reel in or let out, `Gravity`, `Damping`, `IsTaut`, `Overstretch`, `Pull` (what a towed window follows).
  `Draw(dc, pen)` draws it; `Bounds(margin)` gives the size of the surface.

See the [API reference](../../api/KDesktop.Assistant.Sdk.yml) for every type.
