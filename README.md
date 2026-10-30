<p align="center">
  <img src="src/KDesktop.Core/Web/icons/icon-192.png" width="96" alt="K-Desktop">
</p>

<h1 align="center">K-Desktop 5</h1>

<p align="center">
  <b>Keyboard and mouse shortcuts, QuickMenus, macros, events, voice control and a phone remote for Windows,<br>
  with optional desktop assistants powered by local AI.</b>
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white">
  <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-5C2D91">
  <img alt="Languages" src="https://img.shields.io/badge/languages-en%20%7C%20es%20%7C%20fr%20%7C%20de%20%7C%20it%20%7C%20pt-F5C542">
  <img alt="Offline" src="https://img.shields.io/badge/voice%20%26%20AI-100%25%20local-2EA043">
</p>

<p align="center">
  <b>English</b> · <a href="README.es.md">Español</a> · <a href="https://kobayashi82.github.io/K-Desktop/en/">Documentation</a>
</p>

> [!WARNING]
> **Work in progress.** K-Desktop 5 is used every day, but it is still under active development: the releases are
> previews, things change from one version to the next, and the assistant module is still being finished. If you try
> it, expect rough edges.

<p align="center">
  <img src="docs/images/k-desktop-en.png" alt="K-Desktop main window with the command list and a command being edited" width="900">
</p>

K-Desktop turns any key, mouse button or combination into a sequence of actions: open programs, arrange windows,
change the volume, take screenshots, call Home Assistant, type text, read text off the screen… The same commands can
be launched from a menu at the mouse, a search box, your voice, your phone, the network, or automatically when
something happens on the PC.

On top of that, the optional **Assistant module** puts characters on your desktop that you can talk to. They run on a
local AI model, use your commands with visible gestures, and come with tools for files, the web, the screen, music,
timers, notes and your own documents.

K-Desktop 5 is a full rewrite of K-Desktop 4 (VB.NET / WinForms) in **C# / WPF / .NET 10**. The commands, QuickMenus and
icons of K-Desktop 4 are brought over once with a small program of their own, in `importer/`.

## Video

https://github.com/user-attachments/assets/f8e5aefc-33bf-4548-b61d-3f6fdc5642fb

<p align="center">
  <a href="docs/videos/K-Desktop-en.srt">Subtitles (.srt)</a>
</p>

## Contents

- [Features](#features)
- [Actions](#actions)
- [Privacy](#privacy)
- [Requirements](#requirements)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Assistants](#assistants)
- [Command line](#command-line)
- [Building from source](#building-from-source)
- [Project structure](#project-structure)
- [Third-party licences](#third-party-licences)
- [License](#license)

## Features

### Automation

- **Commands**: sequences of actions bound to keyboard or mouse shortcuts (`CTRL + ALT + O`, `Mouse 5`,
  `Mouse 4 + Mouse 1`, `CTRL + Wheel Up`…). A command can be limited to certain applications; when a shortcut exists
  both globally and for one application, the specific one wins there. **If…** actions add conditions.
- **QuickMenu**: a cascading menu at the mouse position, with an acrylic background, icons and keyboard navigation.
  Left click runs and closes, right click runs and keeps it open, middle click edits the item.
- **Launcher**: a search box with its own shortcut. It finds commands and Start menu programs from a few letters,
  searches the web and does maths. The most used come first.
- **Events**: "when X happens, run these commands". At a time or every N minutes, when a program opens or closes or
  comes to the foreground, after the PC has been idle, on lock or sleep, on battery, with a new file in a folder, when
  a controller, USB drive or audio device is connected, when the network changes, depending on CPU/GPU/memory usage,
  or when another computer (or Home Assistant) calls it by name. With conditions, a minimum time between runs and loop
  protection.
- **Macros**: record keyboard and mouse with their timings, edit the steps and play them back with repetitions, in a
  loop or at another speed. A game mode sends raw relative movement.
- **K-Board**: profiles that remap keys, mouse buttons and the wheel to other keys, combinations, text or commands,
  globally or per application, with modifier layers, edited on a drawn keyboard and mouse.
- **With or without administrator rights**: K-Desktop runs elevated so its shortcuts also work over elevated windows,
  and each action decides whether its program starts as administrator or as a normal user.

### Voice

- **Wake words and push-to-talk**: record your own wake word in a few seconds from Settings, or hold a key.
- **Local recognition**: Parakeet (sherpa-onnx) transcribes, and the command is picked by its name or by its meaning
  (EmbeddingGemma). No internet needed.
- **Music orders** ("play music", "turn the volume up"…) and echo cancellation of whatever is playing.
- **Dictation**: hold a key and speak; the text is typed where the cursor is, with spoken line breaks and "delete
  that".

### Calendar, mail and Telegram

- **Agenda**: your appointments by days from your own calendar, `.ics` subscriptions (holidays, a shared calendar,
  Outlook read-only…) and Google Calendar, each in its colour. Create, edit and delete with repetition (every day, weekdays, the
  second Tuesday, the last Friday…), reminders and an online-meeting link; "Join" opens Teams, Meet or Zoom. Import and
  export `.ics`. Also on the phone, in its own tab.
- **Mail**: IMAP/SMTP accounts (Gmail, iCloud, Yahoo or any other that takes an app password; not Outlook or Hotmail) watched with IDLE; a notice for every new
  mail, only the important ones or none, as a notification, an on-screen message or said by an assistant; an invitation in
  a mail goes to the calendar with one click.
- **Telegram**: a bot of your own and/or an account with its own number, each with its owner and its contacts. Run your
  commands from Telegram, talk to the assistant (also with voice notes, answered in its own voice), get the important
  mails, and decide what each person may do: only chat, chosen commands, tools that only read, chosen tools or everything
  like you. Whatever needs confirming is always asked to you; texts from others are never taken as orders.
- **Events and actions**: "An appointment" (minutes before, when it starts or ends), "A mail arrives" (by sender,
  subject or importance) and "A Telegram message arrives" as triggers; "Create appointment", "Join the meeting", "Send
  mail" and "Send via Telegram" as actions.
- **Assistants**: "what do I have tomorrow?", "move the dentist to Friday", "do I have any important mail?", "answer
  Ana that I'll be there". Sending always asks first, and after reading a mail no other tool runs without asking:
  what a mail says is never taken as an order.
- **Accounts**: Google connects with OAuth in your browser; the tokens, the bot's token and the Telegram session stay
  encrypted on your PC.

### Remote control

- **Phone**: an installable web app (PWA) and an Android app with your commands (favorites, events) and menus, a
  touchpad with gestures and keyboard, what plays on the PC and the volume of each app, zoom, voice orders, and the
  assistants, whose answers can play on the phone. Lock, sleep, restart or shut down the PC from the phone, and turn it
  on with Wake-on-LAN (also from away, through a URL). The Android app keeps several PCs, shares texts, links and files
  to the PC, and has a widget, Quick Settings tiles and a media notification. Devices pair with a QR code and can be
  revoked one by one.
- **UDP control**, compatible with the K-Desktop 4 protocol (port 2501).

### Everyday

- **History** of everything that ran, when, and from where (shortcut, menu, voice, web, event, assistant…).
- **Achievements and statistics**: over 100 achievements with animated notifications.
- **Icons**: a built-in vector set (any Windows 11 symbol in 15 colours), Windows icon libraries, any image or a
  website's favicon.
- **Modern interface** with light, dark or system theme and animations.
- **Five languages** (English, Spanish, French, German and Italian) built into the executable. Drop a JSON
  file in `%APPDATA%\K-Desktop 5\Languages` to add one or change texts.
- **Backups**: daily copies of the settings, full `.zip` backups every N days, export and import.
- **Automatic updates** of the program and the Android app.

## Actions

| Open and run | Windows | Keyboard and mouse | Sound |
|---|---|---|---|
| Run application | Control application (activate, close…) | Send keystroke / text | Volume (system, microphone or an app) |
| Open folder or special location | Window (snap, move, minimise…) | Clipboard | Audio output |
| Open web page | Always on top | Play macro | Play sound |
| Run script (CMD / PowerShell) | | K-Board profile | Record audio |
| | | Dictate | |

| Screen | Network and internet | System | K-Desktop and flow |
|---|---|---|---|
| Screenshot | Web request (GET, POST…) | Hidden files | If… (conditions) |
| Copy text from the screen (OCR) | Wake computer (Wake-on-LAN) | Power plan | Wait |
| Magnifier | Wi-Fi and Bluetooth | Power (shut down, restart, lock…) | Run commands and events |
| Display (brightness, projection, off) | | | Open QuickMenu |
| Windows theme | | | Show message |

| Calendar, mail and Telegram |
|---|
| Create appointment |
| Join the meeting (Teams, Meet, Zoom) |
| Send mail |
| Send via Telegram |

With the Assistant module on there are four more: **ask** an assistant, make it **say** something, **load or free its
brain**, and run one of its own **extras** (such as the Lander's manual mode).

## Privacy

Speech recognition, wake words, voices and the assistants' AI all run **on your PC**. Nothing you say or write is sent
anywhere. K-Desktop only goes online for what you ask of it: web requests in your commands, the assistants' web,
weather and currency tools, the tools and MCP servers you add yourself, the calendars, subscriptions, mail accounts
and Telegram bot or account you connect, updates, and downloading voices or models. The
phone remote talks to the PC over your local network and needs pairing, even from the same PC.

## Requirements

- Windows 10 (1809 or later) or Windows 11, 64-bit.
- For the assistants' AI (optional): a GPU with enough memory for the model you choose (a 4B model with vision takes
  about 5 GB), or another AI server on your network.
- For the phone remote: the PC and the phone on the same network. The Android app needs Android 5.0 or later.

## Installation

Download the latest version from [Releases](https://github.com/Kobayashi82/K-Desktop/releases/latest):

- **[KDesktop.exe](https://github.com/Kobayashi82/K-Desktop/releases/latest/download/KDesktop.exe)**: the program, a
  single portable executable. .NET does not need to be installed.
- **[KDesktop.apk](https://github.com/Kobayashi82/K-Desktop/releases/latest/download/KDesktop.apk)**: the Android app.
  It can also be downloaded from K-Desktop itself, which keeps it up to date.

The first time it opens, K-Desktop registers itself to start with Windows (this can be turned off in Settings). Data
is stored in `%APPDATA%\K-Desktop 5`.

## Getting started

1. **Create a command**: *Commands → New command*, add a hotkey and one or more actions, and press **Run** to try it.
2. **Put it in a menu**: *QuickMenu* lets you build the menu that opens at the mouse, with its own shortcut.
3. **Talk to it**: *Settings → Voice* downloads the speech models and records your wake word. Then say
   "K-Desktop, *command name*".
4. **Use your phone**: *Settings → Web app*, scan the QR code with the phone, and the commands are there.
5. **Automate it**: *Events* runs commands at a time, when a program opens, when you leave the PC…

## Assistants

> [!NOTE]
> The Assistant module is in active development. While it is off, K-Desktop loads nothing from it and behaves exactly
> as before.

An assistant is a character that lives on top of your windows. It walks or flies around, reacts to what happens on
the PC, and you can talk to it ("Lander, minimise Chrome", "Kobii, what's the weather tomorrow?"). When it runs one
of your commands, every step can be seen: the Lander flies over and shoots the minimise button, and the window
minimises right on impact. Assistants are installed as `.kai` packages, each one runs in its own process without
administrator rights, and K-Desktop never loads their code.

| Assistant | What it is |
|---|---|
| **[Lander](https://github.com/Kobayashi82/K-Desktop-Assistants/tree/main/Lander)** | A small lunar module that flies between your windows, lands on them, tows them with a grappling hook, and has its own game |
| **[Kobii](https://github.com/Kobayashi82/K-Desktop-Assistants/tree/main/Kobii)** | A little creature that walks, climbs your windows, plays, eats, sleeps and lets you pet, brush or high-five it |
| **[Kumo](https://github.com/Kobayashi82/K-Desktop-Assistants/tree/main/Kumo)** | A soft cloud that floats about, wraps windows to carry them, rains, makes rainbows and dances to your music |
| **[Pip](samples/CharacterAssistant)** | A no-code example: just images and a JSON file |
| **K-Desktop** | The built-in assistant without a character, for when you want answers without anyone on the desktop |

**Brain.** Assistants think with a local model. K-Desktop runs GGUF models with `llama-server` itself: it starts
them on demand, keeps one or several loaded depending on GPU memory, downloads models from Hugging Face, and frees the
GPU when a full-screen game starts. Ollama, llama-swap and any OpenAI-compatible server work too. Each assistant can
think with a different model, and can be asked to "think it through" for harder questions.

**What they can do.** Run your commands and control programs and windows, find, open, read, move and delete files,
search and read the web (with a built-in browser for pages drawn by scripts), look at the screen (vision or Windows
OCR), tell and control what is playing, set timers, take notes and remember things about you, do maths, dates, unit
and currency conversions, check the weather, report the PC's status, end a frozen program, warn you when something
happens ("tell me when the download finishes"), and translate, correct or summarise the text you have selected.

**Your own tools.** A `.json` file in `%APPDATA%\K-Desktop 5\Assistant\Tools\` adds a tool without compiling: a
request to any API (Home Assistant, a Telegram bot), one of your commands with its own phrases, a routine of several
steps, or a script (`.ps1`, `.bat`, `.py`, `.js`). **MCP servers** can be added or imported from Claude Desktop or
mcpo, and K-Desktop can also share its own tools as an MCP server. Secrets are stored encrypted and the model never sees
them. Every tool has its own switch and its own setting for when to ask first.

**What it knows.** Pick folders or files with your documents (notes, manuals, contracts, invoices…). They are read on
the PC (text, Markdown, code, Word, PowerPoint, web pages, PDF and, with OCR, images and scanned PDFs), kept up to date
as they change, and searched by words and by meaning. Answers say which document and page they come from.

**Voice.** Each assistant has its own wake word, talk key, language, personality and voice. Answers are spoken
sentence by sentence while the AI writes, with **Kokoro** or **Piper** (natural voices that run on the processor) or
the Windows voices, plus voice effects (robot, radio, 8-bit…) that can be edited. Long answers are summarised out loud
and shown in full on a reading sheet next to the assistant. You can also drop files on an assistant and choose what to
do with them.

### Installing an assistant

1. *Settings → Assistant module → General →* **Turn on the Assistant module**.
2. Open a `.kai` package with a double click, or use *Assistants → Add assistant* or the *Catalog*. The package card
   shows what it is, what it can do and who signed it, and offers to record its name as a wake word and to create
   commands for its shortcuts.
3. Press **Install**. From its page in *Settings → Assistant module → Assistants* you can put it on the desktop or hide
   it, change its wake word and key, the model it thinks with, its personality and its voice, and watch its
   animations in the **Try** tab.

A new version installed over the old one is swapped in live, keeping the assistant's data
(`%APPDATA%\K-Desktop 5\Assistants\Data\<id>`, included in backups).

To create your own assistant, see [Making assistants](https://kobayashi82.github.io/K-Desktop/en/sdk/index.html)
or start from the `dotnet new` template in [`templates/kdassistant`](templates/kdassistant). JSON tools are described
in [Tools](https://kobayashi82.github.io/K-Desktop/en/assistant/tools.html).

## Command line

```
KDesktop.exe --menu "Main"          opens a QuickMenu (in the running instance)
KDesktop.exe --run "Name"           runs a command
KDesktop.exe --event "Name"         runs an event's commands
KDesktop.exe --launcher             opens the launcher
KDesktop.exe --show | --exit        shows the window / closes K-Desktop
KDesktop.exe --page macros          opens the window on a page (commands, quickmenus, events, macros, kboard, assistant,
                                    achievements, stats, settings, about)
KDesktop.exe --data "C:\Folder"     uses another data folder (independent instance)
KDesktop.exe --no-hooks --no-udp --no-web --no-elevate   diagnostics
```

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build KDesktop.slnx                       # Debug
dotnet build KDesktop.slnx -c Release            # Release + portable in artifacts\portable\KDesktop.exe
dotnet test tests\KDesktop.Core.Tests            # core tests
dotnet test tests\KDesktop.Assistant.Tests       # Assistant module tests (start the real process)
```

A Release build also produces the portable executable: a single ReadyToRun `.exe` with languages, sounds and icons
inside (skip it with `-p:BuildPortable=false`). If JDK 17 and the Android SDK are installed, it also builds the Android
app (`tools\android\build-apk.ps1`). The build has no warnings, with the .NET analysers at `latest-recommended`.

## Project structure

```
src/KDesktop.Core             core without UI: model, storage, hooks, action engine, events, voice, web and UDP server; the assistant package schemas (Assistant/Packages/Schemas)
src/KDesktop                  WPF application: main window, QuickMenu, launcher, OSD, tray, themes
src/KDesktop.Assistant.Sdk    assistant SDK (IAssistant and what the host provides) and .kai packaging
src/KDesktop.Assistant.Host   the assistants' process (KDesktop.exe --assistant): drawing layer, world, sound, stage
src/KDesktop.Assistant.Brain  the assistants' AI: model servers, order routing, memory, test bench
src/KDesktop.Assistant.Tools  built-in tools, JSON tools, MCP client and server, document knowledge, permissions
src/KDesktop.Assistant.Voice  speech: Kokoro and Piper on the processor, Windows voices, voice effects
samples/CharacterAssistant    Pip, the no-code sample assistant
samples/Tools                 sample JSON tools for the assistants
templates/kdassistant         dotnet new template for a new assistant
android/                      Android app (WebView of the PC's web app; native microphone, voice, widget, tiles…)
tests/                        core tests, Assistant module tests and Dot, the test assistant
site/                         documentation website (DocFX), published to GitHub Pages with site/publish.ps1
docs/assistant/               pipe protocol and internal notes (in Spanish)
docs/tutorial/                the tutorial video (Remotion) and its screenshots
tools/                        release scripts, KaiSign, icon, APK, Pip's pictures, tutorial captures
catalog.json                  the official assistant catalog read by K-Desktop
```

The assistants live in their own repository,
[K-Desktop-Assistants](https://github.com/Kobayashi82/K-Desktop-Assistants).

## Third-party licences

See [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md).

## License

K-Desktop is released under the [MIT License](LICENSE).
