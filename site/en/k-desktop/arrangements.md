# Layouts

A layout saves where each window goes: which monitor each program is on, in which place, and whether it is maximized or
minimized. Then it puts them back like that whenever you want: with a hotkey, from a QuickMenu or on its own, with an event.

1. Place your programs the way you like (the browser on the left, the editor on the right, the music on the other monitor…).
2. *Layouts → New layout*: it already has the windows open now, as they are. Give it a name («Work»).
3. **Try it** puts them as in the layout now. **Create a command that applies it** leaves you a command ready to get a
   hotkey.

**Windows** has one row per window, with its program, its title and where it goes. Switch off the ones you don't want to
place, or remove them with ✕. **Save as they are now** replaces them all with the ones open at that moment.

- **If the program is open**, the layout moves its window; it does not open another one. With several windows of the same
  program (two of Chrome), it tells them apart by their title and, if not, by order.
- **If it is not open**, it is skipped, unless *Options → Open the programs that are not open* is on: then it opens it and
  places it as soon as its window appears. Windows Store apps cannot be opened this way. The notice says which ones were
  not open.
- **Monitors** are recognized by their name, even if Windows changes their number. If a window's monitor is not
  connected, the window goes to the main one, in the same place.

## Using it

- The action **Window layout** applies a layout from any command.
- With a **Screen plugged in or out** [event](events.md) it is applied on its own: for example, a «Movie» layout when you
  connect the TV and a «Work» one when you disconnect it.
