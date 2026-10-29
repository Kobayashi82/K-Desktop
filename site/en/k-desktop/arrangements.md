# Layouts

A layout cuts your monitors into **zones** and says which programs go in each one. Then it puts them like that whenever you
want: with a hotkey, from a QuickMenu or on its own, with an event. A layout covers every monitor; one without zones in it
is not touched.

1. Place your programs the way you like (the browser on the left, the editor on the right, music on the other monitor…).
2. *Layouts → New layout*: it already has the windows open now, each one as a zone with its program. Give it a name
   («Work»).
3. **Test** puts it now. **Create a command that applies it** leaves you a command ready to give a hotkey.

**Save as they are now** replaces the zones of every monitor with the windows open at that moment: each window, a zone of
its monitor with its program (a maximized one, the whole screen; minimized ones do not count).

## Zones

At the top of **Zones** there is a pill per monitor: the connected ones and, faded, the ones that were connected at some
point (K-Desktop remembers them), so you can prepare the TV layout even when it is not plugged in. A dot on the pill means
that monitor has zones in this layout. Below is the drawing of the chosen monitor:

- **Click** a zone to mark it; **another click** on the marked one cuts it in two where the dotted line shows (vertically;
  with **Shift**, horizontally).
- **Drag** the lines between zones to change their size (they stay at a half, a third or a quarter when you pass close).
- The **✕** on a line joins its two zones, with their programs.
- **Templates**: whole screen, 2 or 3 columns, grid, one big and two small, or 2 rows. The programs each zone had go to the
  new zone where its middle fell. **Remove zones** leaves the monitor without zones.

Under the drawing are the **programs of the marked zone**, as in K-Board's *Only in these applications*: type the program
or pick it from the list and press **Add**, or press the cursor button and click its window. A zone without programs is only
there to snap windows into.

## When it is put

- **If the program is open**, its window goes to its zone; it does not open another one. A whole-screen zone leaves it
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
