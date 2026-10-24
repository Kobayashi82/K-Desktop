# Gestures

The module asks the assistant for **gestures**: what it wants to achieve, never how. Each assistant decides how to perform
them —the Lander shoots the minimise button, Kobii throws a ball at it, Kumo pokes it—. An assistant that only knows
`Communicate` and `React` already works: for the rest the module shows the bubble and does the action itself.

```csharp
public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct) { … }
```

List the ones it knows in `traits.gestures` of its manifest. `CanPerform` says whether it can do one *now* (yes by
default); `GestureOutcome.NotPerformed` says no just as well. Never throw.

## The gestures

| Gesture | What it asks for |
|---|---|
| `PointAt(Place)` | Point at a window, a point, an area, the tray or the clock (a spotlight, an arm…) |
| `GoTo(Place)` | Go somewhere (fly, walk, teleport) |
| `PressButton(Window, Button, ButtonBounds)` | Press a caption button (minimise, maximise/restore, close) |
| `MoveWindow(Window, Destination)` | Move a window there (a maximised one arrives already restored by a `PressButton(Restore)` before) |
| `ActivateWindow(Window)` | Bring a window to the front (perch on it, knock on the door…) |
| `WaitForWindow(Process, Title)` | Something was just opened: wait for its window. `ctx.Appeared` gives it (or null) |
| `Mark(Place, Duration, Text)` | Leave a mark somewhere for a while (a beacon, a flag, an alarm clock), with an optional label |
| `Scan(Area)` | Sweep an area (a screenshot, reading the screen) |
| `Typing(Window)` | Pretend to type into a window (keys or text are being sent to it) |
| `Communicate(Text, Icon)` | Stay visible while the host shows the text in the bubble. `Icon` says what it is about when known (`clock`, `folder`, `web`, `volume`, `power`…; show the ones you do not know without a picture) |
| `React(Reaction)` | A short reaction: `Success`, `Warning`, `Failure`, `Celebration`, with its [context](#reactions) |
| `Announce(Reason, Cursor, UserAway)` | [Catch the user's eye](#catching-the-eye) before saying something on its own |

## The commands of the user

When the user asks an assistant to run a K-Desktop command, each action arrives as one or several gestures: minimise →
`PressButton(Minimize)`, place a window → `MoveWindow` with the exact target, a maximised one first `PressButton(Restore)`…

- Call **`ctx.Commit()` at the instant of the effect** (when the shot hits): the action happens right then, and
  `ctx.Committed` tells you whether it went well.
- Or do it yourself (`Windows.TryMove` frame by frame) and return **`PerformedSelf`**: K-Desktop then sets it to the pixel.
- What you cannot do (`CanPerform` false) the host shows with the bubble while K-Desktop does it.
- You have **`ctx.TimeLeft`**: if you run over, fail or do not answer, K-Desktop does it anyway and asks you for a
  `React(Warning)`. An order is never left half done; the token fires when the time is up or the user says «stop».
- **`ctx.Speed`** is the speed the user chose (`Showy`, `Normal`, `Fast`): in `Fast`, `Commit` within half a second.
- `ctx.Step` and `ctx.StepCount` say which step of the order it is.

```csharp
if (gesture is Gesture.PressButton press)
{
    // FlyNextToAsync and ShootAsync are the assistant's own animations.
    await FlyNextToAsync(press.ButtonBounds, ctx.TimeLeft, ct);
    await ShootAsync(press.ButtonBounds, ct);
    ctx.Commit();                       // the window minimises now, on impact
    return await ctx.Committed ? GestureOutcome.Committed : GestureOutcome.NotPerformed;
}
```

With *Also show the commands you launch* on, commands run by other means (a hotkey, a QuickMenu, the phone) reach you as
gestures too, always in `Fast`: they are only decoration, nothing waits for them and their `Commit` does nothing.

## The tools of the AI

What the AI's tools do reaches you as gestures too, with `ctx.Step` 0:

| Tool | Gesture |
|---|---|
| A timer | `Mark` by the clock (`Place.Clock`) with the time it rings; removing it → `PointAt(Place.Clock)` |
| Opening a website, a search, a program or a file | `WaitForWindow`, right before opening it |
| What's playing, controlling the music | `GoTo(Place.Tray)` |
| «Tell me when … closes» | `Mark` on that window |
| Rewriting a text and pasting it | `Typing` in that window |
| Reading the screen | `Scan` of what was read |

JSON tools can ask for one with `"gesture"`. They are only decoration: the tool never waits for you, `Commit` does
nothing, a step of an order takes their place and a new one replaces the one before. What you cannot do is not shown.

## Catching the eye

Before saying something on its own (a timer ringing, a «tell me when» coming true, *Assistant: say*) the host asks for
`Announce`: walk up to the mouse, wave, blink… `Reason` says why (`Timer`, `Watch`, `Message`), `Cursor` where the mouse
was (or null) and `UserAway` how many seconds without keyboard or mouse (the longer, the bigger it may be: maybe they are
not looking). The host waits for you, `ctx.TimeLeft` at most (about 2 s, 3 in `Showy` and half a second in `Fast`), and
then talks with `Communicate`: finish as soon as you have caught the eye. There is nothing to commit.

## Reactions

`Gesture.React` carries `Context` (`ReactionContext`), filled in by the host:

| | |
|---|---|
| `Source` | `Order` (a command or a K-Desktop action), `Tool` (a timer, a note, the web…), `Notice` (something it says on its own), `User` (what the user says about it: «thanks», «well done» → `Celebration`; «that's wrong» → `Failure`), or `Unknown` |
| `Streak` | How many in a row of the same kind (1 the first; back to 1 with another kind or after half an hour) |
| `FirstToday` | The first one today |
| `What` | What it was when known (`set_timer`, `run_command`, `timer`, `praise`…) |

The host reacts once per order, when the answer is ready: something done and nothing failed → `Success`; done but
something failed → `Warning`; nothing done and something failed → `Failure`. Only looking something up (the time, the
weather) does not react. Use it with `Variety<T>` not to react the same way every time: something bigger the first time
today or on a long streak, more downcast at the third failure in a row.
