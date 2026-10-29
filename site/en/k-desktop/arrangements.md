# Layouts

A layout cuts your monitors into **zones** and says which programs go in each one. Then it puts them like that whenever you
want: with a hotkey, from a QuickMenu or on its own, with an event. A layout covers every monitor; one without zones in it
is not touched.

1. Place your programs the way you like (the browser on the left, the editor on the right, music on the other monitor…).
2. *Layouts → New layout*: it already has the windows open now, each one as a zone with its program. Give it a name
   («Work»).
3. **Test** puts it now. **Create a command that applies it** leaves you a command ready to give a hotkey.

**Save as they are now** replaces the zones of every monitor with the windows open at that moment: each monitor is cut by
its windows, and each zone gets the programs of its windows (a maximized one, the whole screen; minimized ones do not
count). Zones never overlap: windows one on top of another share a zone, and a wide free area becomes an empty zone.
Each program also remembers the exact place and size of its window, so putting the layout leaves everything as it was.

## Zones

At the top of **Zones** there is a pill per monitor: the connected ones and, faded, the ones that were connected at some
point (K-Desktop remembers them), so you can prepare the TV layout even when it is not plugged in. A dot on the pill means
that monitor has zones in this layout. Below is the drawing of the chosen monitor:

- **Click** a zone to mark it (a click outside the zones unmarks it); **another click** on the marked one cuts it in two where the dotted line shows (vertically;
  with **Shift**, horizontally).
- **Drag** the lines between zones to change their size (they stay at a half, a third or a quarter when you pass close).
- The **✕** on a line joins its two zones, with their programs.
- **Templates**: whole screen, 2 or 3 columns, grid, one big and two small, or 2 rows. The programs each zone had go to the
  new zone where its middle fell. **Remove zones** leaves the monitor without zones.

Under the drawing are the **programs of the marked zone**, as in K-Board's *Only in these applications*: type the program
or pick it from the list and press **Add**, or press the cursor button and click its window. A zone without programs is only
there to snap windows into.

Programs saved with **Save as they are now** have a pin: they go to the exact place their window had, shown dashed in the
marked zone. **Right-click → Fill the zone** forgets that place, and the program stretches to its zone, like the ones you
add by hand.

## When it is put

- **If the program is open**, its window goes to its zone (or to its saved place, if it has a pin); it does not open another one. A whole-screen zone leaves it
  maximized. With several windows of the same program (two of Chrome), it tells them apart by their title, else by order.
- **If it is not open**, it is skipped, unless *Options → Open the programs that are not open* is on: then it opens it and
  places it as soon as its window appears. Microsoft Store apps cannot be opened this way. The notice says which ones were
  not open.
- **Monitors** are recognized by their name, even if Windows numbers them differently. If a zone's monitor is not
  connected, its programs go to the main one, in the same place.

## Using it

- The action **Window layout** puts a layout from any command.
- With a **Screen plugged in or out** [event](events.md) it is applied on its own: for example, a «Movie» layout when
  the TV is plugged in and a «Work» one when it is unplugged.

## Snapping windows into the zones

Each monitor uses the zones of the **layout put on it last** (remembered after a restart). A monitor where none was ever
put does not snap.

- **Shift + drag**: while you drag a window by its title bar, hold **Shift** and the zones of each monitor show on top;
  the one under the mouse is marked and, when you drop it, the window fits into it (a whole-screen zone maximizes it). If
  you let Shift go, the zones disappear and the window stays where you drop it; **Esc** cancels. It is turned off in
  *Options → Snap with Shift while dragging* (it applies to all layouts).
- **Next or previous zone**: the **Window** action has *To the next zone* and *To the previous zone*. They follow the
  zones' numbers and, after the last one of a monitor, go to the next monitor. *Options → Create the zone hotkeys* leaves
  you both commands made with **Win + Alt + →** and **Win + Alt + ←**.
- Snapping always fills the zone, even if the program has a saved place.
