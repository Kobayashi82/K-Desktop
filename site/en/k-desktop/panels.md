# Panels

A panel is a small window on your desktop with figures of your PC: the network, the processor, the memory and the graphics
card, always in sight. It stays **over the desktop and under your windows** (put on the taskbar, in front of it), never takes the focus and does not show in
Alt + Tab or on the taskbar. You can have several, each one with its own figures and look.

1. *Panels → New panel*: it already shows the usual figures in two rows (↑ ↓, CPU, GPU, RAM, VRAM and the temperature of
   the graphics card) and appears at the top right of the main monitor.
2. Drag it with the mouse, from anywhere on it, wherever you like. It remembers where, also on the other monitor and with another resolution.
3. The switch **On the desktop** (in the list or on the header) shows it or hides it.

## Figures

| Figure | What it shows |
|---|---|
| **Network upload** / **Network download** | What the PC sends and receives right now, in kB/s, MB/s or GB/s. |
| **Processor (CPU)** | How busy the processor is, as Task Manager shows it. |
| **Memory (RAM)** | The memory in use. |
| **Graphics card (GPU)** | How busy the graphics card is (games, video, AI). |
| **Graphics memory (VRAM)** | The graphics card's own memory in use. |
| **Graphics card temperature** | In degrees; only with an NVIDIA card. |
| **Gap** | Nothing: an empty cell, so the columns line up the way you want. |

*Add figure* adds one (also one it already has: the second one can look different), and you drag them to change the order.
On each one you choose:

- **Name shown**: its short name on the panel (CPU, ↑…); empty, the usual one.
- **How it looks**: **Figure only**, **With a bar** (a thin bar under it; for the network, full is the highest speed of the last minute) or **With a graph** (the last minute, as a small
  area under it).

## Look

- **Rows**: the figures go in columns of this many rows, from top to bottom. 1 puts them all on one line; as many as figures,
  one under the other.
- **Text size**, **Colour of the names**, **Colour of the figures** and **Background colour**: *Automatic* follows
  K-Desktop's theme (the names in its accent colour).
- **Background opacity**: 0 % leaves no background, only the text.

Every change shows on the panel at once.

## Options

- **Updates every**: from half a second to ten seconds (one second, if you don't change it).
- **Lock the position**: dragging it doesn't move it.
- **Let the clicks through** (locked): the clicks go to what is under it, as if it were not there. Its menu no longer opens,
  so you change it from here.
- **Always on top**: over every window and the taskbar, even the ones that come up later. It hides by itself while a game or a video fills its screen.
- **Move it to the corner**: back to the top right of the main monitor, in case it got lost.

## Its menu

A right click on the panel opens its menu: **Lock the position**, **Always on top**, **Background opacity**, **Edit the
panel…** (opens it here) and **Hide**. With *Show desktop* (Win + D) it stays in sight. The tray icon menu also has
**Panels**, to show or hide each one.

## From a command

The action **Panel** shows, hides, locks or unlocks a panel, or all of them (*All panels*): give a hotkey to *Show or hide*
and you have it at hand when you want it. Panels are also exported and imported (right click on the list), like commands.

K-Desktop only reads what some panel shows, and only while it is on the desktop.
