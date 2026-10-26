# Actions

Every action a command can run. Add them with *Add action* in the command editor; they run in order.

## Open and run

| Action | What it does |
|---|---|
| **Run application** | Opens a program, file, shortcut or script, as the normal user or as administrator. |
| **Open folder** | Opens a folder or a special Windows location. |
| **Open web page** | Opens a URL in the browser of your choice. |
| **Control application** | Activates, closes or waits for an application by its process name. |
| **Run script** | Runs a CMD or PowerShell script written right there. |

## Windows

| Action | What it does |
|---|---|
| **Window** | Maximizes, minimizes, places (halves, quarters, thirds), centres, moves to another monitor or makes a window transparent. |
| **Always on top (on/off)** | Pins the active window above the others, or releases it. |

## Keyboard and mouse

| Action | What it does |
|---|---|
| **Send keys / text** | Sends a key combination, types text or moves the mouse. |
| **Clipboard** | Copies a text (with date, time…) or changes the one there: plain, upper case, lower case, clean links… and pastes it. |
| **Play macro** | Plays a recorded macro (keyboard and mouse with their timing), once or several times. |
| **K-Board** | Turns a K-Board profile (remapped keys and buttons) on, off, or toggles it. |
| **Dictate** | Types what you say where the cursor is, without AI. Ends after a few seconds of silence, with Esc or by saying «stop dictating». |

## Sound

| Action | What it does |
|---|---|
| **Volume** | Sets, raises, lowers or mutes the volume: of the speakers, the microphone, one program or the one in front. |
| **Audio output** | Changes the default sound device. With several chosen, each run switches to the next one. |
| **Play sound** | Plays a bundled sound or a .wav file. |
| **Record audio** | Records what the PC plays, the microphone or both, or saves what just played (*Settings → Captures and magnifier*). |

## Screen

| Action | What it does |
|---|---|
| **Screenshot** | Captures a region you pick with the mouse, the active window, a monitor or all of them; copies it, saves it and, if you want, opens it. |
| **Copy text from the screen** | Reads the text of a region you pick with the mouse, the active window or a monitor and copies it. No AI: Windows' text recognition (OCR). |
| **Magnifier** | A lens (rectangular or round) that follows the mouse and enlarges what is around it, or the whole main screen. |
| **Screen** | Turns the screens off, changes the projection (Win + P), the brightness, or a monitor's resolution and refresh rate. |
| **Windows theme** | Switches Windows and the apps to light or dark mode, or toggles between them. |

## Network and internet

| Action | What it does |
|---|---|
| **Web request** | Sends an HTTP request (GET, POST…) without opening the browser: Home Assistant, APIs, phone notifications… |
| **Wake a computer** | Turns on another PC or a server over the network (Wake-on-LAN) and, if you want, waits until it answers. |
| **Wi-Fi and Bluetooth** | Turns the Wi-Fi or the Bluetooth on, off, or toggles it. |

## System

| Action | What it does |
|---|---|
| **Files and folders** | Copy, move, rename, delete (to the recycle bin or for good), create a folder, zip, unzip or make a **backup**: sync (only what is new or changed, optionally removing from the destination what is gone) or a dated copy each time keeping the latest. The source is a list of files, folders or patterns (`C:\Music\*.mp3`) typed in or added with the buttons. The destination may be inside the source: it never copies itself. Progress shows on the OSD (Esc stops it) and the next action can wait until it is done. |
| **Hidden files (on/off)** | Shows or hides hidden files in Explorer. |
| **Power plan** | Switches the Windows power plan (High performance, Balanced…) or goes to the next one. |
| **Power** | Shuts down, restarts, sleeps, locks or signs out, with a cancellable countdown. |

## K-Desktop and flow

| Action | What it does |
|---|---|
| **If…** | Checks something (a program open, the time, a device, the network, CPU use…) and, if it isn't so, stops the command or skips the next actions. See [conditions](commands.md#if-conditions). |
| **Wait** | Pauses before the next action. |
| **Commands** | Runs, enables or disables another command. |
| **Open QuickMenu** | Shows a QuickMenu at the mouse position. |
| **Show message** | Shows an on-screen message for a while. |
| **Show notification** | A card in a corner with your title, text, picture, colour and sound, for as long as you say. Clicking it can open something (a file, a folder, a web page) or run another command, and the next action can wait until it closes. With something full screen it does what *Settings › OSD › Notifications* says. |
| **Disable in current app (on/off)** | Disables or re-enables K-Desktop hotkeys in the active application (useful in games). |

## Assistant

Only with the [assistant module](../assistant/index.md) on.

| Action | What it does |
|---|---|
| **Assistant: ask** | Says something to an assistant, as if you said it out loud. Without text, it opens the box to write to it. |
| **Assistant: say** | The assistant says something in its bubble, without the AI. Handy in events: «The backup has finished». |
| **Assistant: brain** | Takes the AI model out of the graphics card (before a game or a render) or loads it now so the first answer does not wait. |
| **Assistant: action** | Runs one of an assistant's own actions, like the Lander's manual mode. |
