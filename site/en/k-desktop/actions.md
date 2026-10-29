# Actions

Every action a command can run. Add them with *Add action* in the command editor; they run in order.

## Open and run

| Action | What it does |
|---|---|
| **Run application** | Opens a program, file, shortcut or script, as the normal user or as administrator. |
| **Open folder** | Opens a folder or a special Windows location. |
| **Open web page** | Opens a URL in the browser of your choice. |
| **Control application** | Activates, closes, keeps on top or waits for an application by its process name. |
| **Run script** | Runs a CMD (BAT), PowerShell or VBScript script written right there. |

## Windows

| Action | What it does |
|---|---|
| **Window** | Maximizes, minimizes, places (halves, quarters, thirds), centres, moves to another monitor or makes a window transparent. |
| **Always on top (on/off)** | Pins the active window above the others, or releases it. |

## Keyboard and mouse

| Action | What it does |
|---|---|
| **Send keys / text** | Sends a key combination, types text or moves the mouse. |
| **Clipboard** | Copies a text (with date, time…) or changes the one there: plain, upper case, lower case, clean links… and pastes it. Or opens the [clipboard history](clipboard.md). |
| **Play macro** | Plays a recorded macro (keyboard and mouse with their timing), once or several times. |
| **K-Board** | Turns a K-Board profile (remapped keys and buttons) on, off, or toggles it. |
| **Dictate** | Types what you say where the cursor is, without AI. Ends after a few seconds of silence, with Esc or by saying «stop dictating». |

## Sound

| Action | What it does |
|---|---|
| **Volume** | Sets, raises, lowers or mutes the volume: of the speakers or the microphone (the default ones or one you choose), one program or the one in front. |
| **Audio output** | Changes the default sound device. With several chosen, each run switches to the next one. |
| **Audio input** | Changes the default microphone. With several chosen, each run switches to the next one. |
| **Play sound** | Plays a sound chosen in a panel by kind (alerts, alarms, calls, system and yours) with K-Desktop's and Windows' sounds, which play when clicked, or another file (.wav, .mp3, .wma, .m4a, .aac, .flac). The ones you put in your sounds folder show under «Yours». |
| **Media** | Plays, pauses, skips to the next or previous one, or stops, with Windows' media controls (Spotify, browsers, Media Player…): in what is playing or in a program you choose from the ones that have something now. A player that does not show up gets the media keys. The OSD shows the song that plays. |
| **Record audio** | Records what the PC plays, the microphone or both, or saves what just played (*Settings → Audio recording*). |

## Screen

| Action | What it does |
|---|---|
| **Screenshot** | Captures a region you pick with the mouse, the active window, a monitor or all of them; copies it, saves it and, if you want, opens it. |
| **Copy text from the screen** | Reads the text of a region you pick with the mouse, the active window or a monitor and copies it. No AI: Windows' text recognition (OCR). |
| **Magnifier** | A lens (rectangular or round) that follows the mouse and enlarges what is around it, or the whole screen it is on. |
| **Screen** | Turns the screens off (all or the ones you choose), changes the projection (Win + P), the brightness, or a monitor's resolution and refresh rate. |
| **Windows theme** | Switches Windows and the apps to light or dark mode, or toggles between them. |

## Network and internet

| Action | What it does |
|---|---|
| **Web request** | Sends an HTTP request (GET, POST…) without opening the browser: Home Assistant, APIs, phone notifications… |
| **Wake a computer** | Turns on another PC or a server over the network (Wake-on-LAN) and, if you want, waits until it answers. |
| **Wi-Fi and Bluetooth** | Turns the Wi-Fi or the Bluetooth on, off, or toggles it. |
| **Send to phone** | A text, a link or a small file to your paired phones or browsers (all, or the one you choose), as a notification. With variables. See [What the PC sends](remote.md#what-the-pc-sends). |

## System

| Action | What it does |
|---|---|
| **Files and folders** | Copy, move, rename, delete (to the recycle bin or for good), create a folder, zip, unzip or make a **backup**: sync (only what is new or changed, optionally removing from the destination what is gone) or a dated copy each time keeping the latest. The source is a list of files, folders or patterns (`C:\Music\*.mp3`) typed in or added with the buttons. The destination may be inside the source: it never copies itself. Progress shows on the OSD (Esc stops it) and the next action can wait until it is done. |
| **Hidden files (on/off)** | Shows or hides hidden files in Explorer. |
| **Power plan** | Switches the Windows power plan (High performance, Balanced…) or goes to the next one. |
| **Keep awake** | The PC does not sleep (and, if you want, the screen stays on) for some minutes, while a program is open or until you turn it off. While it lasts a cup shows in the tray, with «30 more minutes» and «Let it sleep». Closing K-Desktop lets the PC sleep as usual. |
| **Power** | Shuts down, restarts, sleeps, locks or signs out, with a cancellable countdown. |

## Flow

| Action | What it does |
|---|---|
| **If…** | Checks something (a program open, the time, a device, the network, CPU use, a variable…) and, if it isn't so, stops the command, skips the next actions or runs another command instead. See [conditions](commands.md#if-conditions). |
| **Wait** | Pauses before the next action. |
| **Commands** | Runs, enables or disables another command. |
| **Open QuickMenu** | Shows a QuickMenu at the mouse position. |
| **Show message** | Shows an on-screen message for a while. |
| **Show notification** | A card in a corner with your title, text, picture, colour and sound, for as long as you say. Clicking it can open something (a file, a folder, a web page) or run another command, and the next action can wait until it closes. Its sound can be *Global* (the one of *Settings › Messages and notifications*). With something full screen it does what those settings say. |
| **Disable in current app (on/off)** | Disables or re-enables K-Desktop hotkeys in the active application (useful in games). |

## Assistant

Only with the [assistant module](../assistant/index.md) on.

| Action | What it does |
|---|---|
| **Assistant: ask** | Says something to an assistant, as if you said it out loud. Without text, it opens the box to write to it. **With the answer**: *Say it* (in its bubble and with its voice), *Paste it over the selection*, *Copy it*, *Show it on a sheet* or *Only keep it in {result}*. Except *Say it*, the command waits for the answer (Esc stops it), the assistant neither speaks nor shows its bubble, and the answer is left in `{result}`. With *If it is not on the desktop, it comes out to do it*, it comes out when it is hidden. Example: «Translate into Spanish: {selection}» and *Paste it over the selection*. |
| **Assistant: say** | The assistant says something in its bubble, without the AI. Handy in events: «The backup has finished». |
| **Assistant: chat** | Opens, closes or toggles a separate **chat window**, with the same conversation as *Panel → Assistant*. It starts with the last assistant you talked to in it or with a fixed one. Its icon, at the top, changes the assistant (the ones not on the desktop come out when chosen); the pin keeps it on top and Esc closes it. It remembers where it was, its size, the pin and the assistant. |
| **Assistant: brain** | Takes the AI model out of the graphics card (before a game or a render) or loads it now so the first answer does not wait. |
| **Assistant: action** | Puts an assistant on the desktop, hides it, or runs one of its own actions, like the Lander's manual mode (if it is not on the desktop, it comes out for it or nothing is done, as you choose). |

## Variables: passing data from one action to another

The texts of the actions (a message, an address, a text to type, a script…) can have variables. Type `{` in the field
and the ones you can use show up; choose one with the arrows and put it with Enter.

| Variable | What it holds |
|---|---|
| `{result}` | What the previous action that gives something gave (see below). |
| `{selection}` | What is selected in the window where you ran the command. It is copied once and the clipboard is left as it was. |
| `{clipboard}` | The copied text. |
| `{clipboard:2}` | What was copied before the last one, from the [clipboard history](clipboard.md) (`:3`, `:4`… further back). |
| `{date}` and `{time}` | Today's date (2026-10-28) and the time (09:30:05). |
| `{name}` | What an earlier action kept with **Save in a variable** and that name. |

These give something: **Copy text from the screen** (the text read), **Web request** (the answer), **Assistant: ask** (the
answer, except with *Say it*), **Clipboard** (the text
it leaves copied), **Screenshot** (the saved file) and **Run script** (what it writes, when *Wait until it finishes* is
ticked and the window is *Hidden*; not in VBScript). These actions have the **Save in a variable** field: with a name
(letters, digits and `_`), what they give stays available until the command ends, even if another action changes
`{result}` later.

Variables last one run of the command. The commands it runs with *Commands* and the commands of the same event share
them. The Spanish names work too (`{resultado}`, `{seleccion}`, `{portapapeles}`, `{fecha}`, `{hora}`). Anything else
between braces is left as it is.
