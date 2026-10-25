# Settings, data and privacy

*Settings* is organised in foldable groups, with a search box at the top (Ctrl+F): type «volume» or «backup» and it goes
there.

![Settings → Appearance](../../images/en/settings-appearance.png)

## General

| Section | What it has |
|---|---|
| **Appearance** | Light, dark or system theme; language (Windows', or one of the six built in); website icons; translating |
| **General** | Pause K-Desktop (with its hotkey and sounds); start with Windows, the tray icon and its picture; the hotkeys to show the window and to exit, sounds and startup message |
| **OSD** | K-Desktop's on-screen messages: where they show (clicking on a drawing of the screen), on which monitor, their size, how long they stay, what they do with a film or a game full screen and which groups show (actions, volume and brightness, voice, warnings). Below, the achievement **notifications**: whether they show, with sound, on which monitor and corner, and whether they wait until the full-screen thing ends |
| **Launcher** | Its hotkey, what it finds (programs, hidden commands, sums, the web) and its order |

## Voice and multimedia

| Section | What it has |
|---|---|
| **Voice** | Speech models, microphone, wake words, hotkeys to talk and to dictate. See [Voice](voice.md) |
| **Multimedia** | Voice orders for music, videos and volume, and the volume step |

## Connections

| Section | What it has |
|---|---|
| **Remote control** | The UDP server. See [Phone and remote control](remote.md) |
| **Web app** | The web server, pairing and paired devices, touchpad and keyboard |
| **Android app** | Its version and «Save the APK» |

## System

| Section | What it has |
|---|---|
| **Disable in** | Programs where K-Desktop's hotkeys do nothing (games, remote desktops) |
| **Mouse** | What the Back and Forward buttons do |
| **Captures and magnifier** | Screenshots folder; **audio replay** (keeps the last seconds in memory so *Record audio → Save the replay* saves them when something happens; nothing is written to disk); the magnifier's zoom, shape and size |
| **Data** | Backups, export, import, re-import from K-Desktop 4, and the log |
| **Updates** | Version, checking now, and what to do when there is a new one |

The **assistant module** has its own group: see [Assistants](../assistant/index.md).

## Backups

- **Export** saves everything of yours to a `.zip`: settings, commands, QuickMenus, macros, K-Board profiles, achievements
  and statistics, wake words, and your own icons, sounds and languages (and the assistants' data).
- **Automatic backups**: the same full backup every N days, keeping the last ones, in the folder you choose (safer on
  another disk, OneDrive or a NAS). Besides, the settings are copied every day.
- **Import** restores a backup. The current configuration is saved to the Backups folder first.

## Languages

Six are built in: English, Spanish, French, German, Italian and Portuguese. Extra languages are JSON files in
`%APPDATA%\K-Desktop 5\Languages`. Under *Translate*, **Export template** saves the current language: translate the
texts, save the file there with the language code (`nl.json`) and pick it under *Language*. A file with only some texts
(`en.json`) changes only those.

## Achievements and statistics

Over 100 achievements with animated notifications, and statistics of what you use most.

![Achievements](../../images/en/achievements.png)

## Privacy

Speech recognition, wake words, voices and the assistants' AI all run **on your PC**. Nothing you say or write is sent
anywhere. K-Desktop only goes online for what you ask of it:

- web requests in your commands;
- the assistants' web, weather and currency tools, and the tools and MCP servers you add yourself;
- checking for updates, and downloading voices or models.

The phone remote talks to the PC over your local network and needs pairing, even from the same PC.
