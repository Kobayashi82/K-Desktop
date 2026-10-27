# Getting started

## Requirements

- Windows 10 (1809 or later) or Windows 11, 64-bit.
- For the assistants' AI (optional): a graphics card with enough memory for the model you choose (a 4B model with vision
  takes about 5 GB), or another AI server on your network.
- For the phone remote: the PC and the phone on the same network. The Android app needs Android 5.0 or later.

## Installing

Download **KDesktop.exe** from the [releases](https://github.com/Kobayashi82/K-Desktop/releases). It is a single portable
executable: put it wherever you like and open it. .NET does not need to be installed.

- The first time, K-Desktop registers itself to start with Windows (you can turn that off in *Settings → General*).
- It runs **as administrator**, so its shortcuts also work over programs that run as administrator. Each action that opens
  something decides whether that program starts as administrator or as a normal user.
- Your data lives in `%APPDATA%\K-Desktop 5`: settings, commands, menus, macros, history, backups, logs and the
  assistants.

## Your first five minutes

1. **Create a command**: *Commands → New command*, give it a name, a hotkey (press the keys or mouse buttons you want)
   and one or more actions. Press **Run** to try it.
2. **Put it in a menu**: in *QuickMenu*, build the menu that opens at the mouse, with its own shortcut.
3. **Talk to it**: *Settings → Voice* downloads the speech models and records your wake word. Then say «K-Desktop,
   *command name*».
4. **Use your phone**: *Settings → Web app*, scan the QR code with your phone, and your commands are there.
5. **Automate**: *Events* runs commands at a time, when a program opens, when you leave the PC…

![Commands](../../images/en/commands.png)

## The window

The column on the left has every page: **Commands**, **Events**, **QuickMenu**, **K-Board**, **Macros**, **Assistant**
(with the assistant module on), **History**, **Settings**, **Achievements** and **About**. At the bottom, the status icons
(paused, remote control, phone, voice, assistant) can be clicked.

Closing the window does not close K-Desktop: it stays in the notification area (tray). Right-click its icon to open the
launcher, pause it, switch K-Board profiles, show the window or exit. While **paused**, no command runs (hotkeys,
QuickMenus, K-Board, remote…): only the hotkeys to show the window, pause and exit work.

## Updates

K-Desktop looks for new versions at start and every 6 hours. In *Settings → Updates* you choose what happens when there is
one: let you know, or install it by itself (it downloads it and restarts K-Desktop at a moment you are not using it, in a
couple of seconds). The Android app is updated from the PC too.

## Command line

```
KDesktop.exe --menu "Main"          opens a QuickMenu (in the running instance)
KDesktop.exe --run "Name"           runs a command
KDesktop.exe --event "Name"         runs an event's commands
KDesktop.exe --launcher             opens the launcher
KDesktop.exe --show | --exit        shows the window / closes K-Desktop
KDesktop.exe --page macros          opens the window on a page (commands, quickmenus, events, macros, kboard,
                                    assistant, achievements, stats, settings, about)
KDesktop.exe --data "C:\Folder"     uses another data folder (an independent instance)
KDesktop.exe --no-hooks --no-udp --no-web --no-elevate   for diagnostics
```

Any program, script or shortcut can use these to drive the K-Desktop that is already running.
