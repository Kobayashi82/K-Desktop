# K-Desktop

K-Desktop turns any key, mouse button or combination into a sequence of actions: open programs, arrange windows, change
the volume, take screenshots, call Home Assistant, type text, read text off the screen… The same commands can be
launched from a menu at the mouse, a search box, your voice, your phone, the network, or automatically when something
happens on the PC.

On top of that, the optional **assistant module** puts characters on your desktop that you can talk to. They think with a
local AI model, carry out your commands with gestures you can see, and come with tools for files, the web, the screen,
music, timers, notes and your own documents.

![K-Desktop's main window](../images/k-desktop-en.png)

> [!WARNING]
> K-Desktop 5 is used every day but is still under active development: things change from one version to the next.

## What is in this documentation

| | |
|---|---|
| **[K-Desktop](k-desktop/getting-started.md)** | Installing it, commands and their actions, QuickMenus, the launcher, events, macros, K-Board, voice, the phone and the settings |
| **[Assistant module](assistant/index.md)** | Assistants on the desktop: installing them, their brain (the AI), their tools, memory and documents, and their voices |
| **[Making assistants](sdk/index.md)** | For developers and artists: assistants without code (pictures and JSON) and with code (C# and the SDK), signing and publishing them |
| **[SDK API reference](../api/KDesktop.Assistant.Sdk.yml)** | Every type of the assistant SDK |

## In a few words

- **Commands**: actions bound to shortcuts (`CTRL + ALT + O`, `Mouse 5`, `CTRL + Wheel Up`…), globally or for one program.
- **QuickMenu and launcher**: a menu at the mouse and a search box that find and run everything.
- **Events**: «when this happens, run these commands» (a time, a program opening, the PC idle, a USB drive…).
- **Macros and K-Board**: record keyboard and mouse, remap keys and buttons per program.
- **Voice**: your own wake word, recognition on your PC (no internet), dictation.
- **Phone**: a web app and an Android app with your commands, a touchpad, a keyboard and the assistants' chat.
- **Six languages**: English, Spanish, French, German, Italian and Portuguese.
- **Private**: speech, voices and the AI run on your PC. See [Privacy](k-desktop/settings.md#privacy).
