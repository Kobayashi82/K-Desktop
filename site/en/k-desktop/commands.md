# Commands

A **command** is a list of actions with a name. It can be run by a hotkey, from a QuickMenu, the launcher, your voice,
your phone, the network, an event, another command or an assistant.

![A command being edited](../../images/en/action-window.png)

## Hotkeys

A hotkey is any combination of keys, mouse buttons and the wheel: `CTRL + ALT + O`, `F13`, `Mouse 5`,
`Mouse 4 + Mouse 1`, `CTRL + Wheel Up`… To record one, click the hotkey box and press it. A command can have several.

- **For one program only**: limit the command to some programs (by their process name). When the same hotkey exists
  globally and for one program, the specific one wins inside that program and the global one everywhere else.
- **Disable in**: in *Settings → Disable and block* you list programs (games, remote desktops…) where K-Desktop's hotkeys do
  nothing. The action *Disable in current app* toggles it for the program in front.
- K-Desktop warns you when a hotkey is already used by another command, by K-Desktop itself or by an assistant.

## Actions

Each command has a list of actions that run in order: open a program, place a window, send keys, change the volume, wait,
run another command… See the [complete list of actions](actions.md).

![Choosing an action](../../images/en/action-picker.png)

- Each action can be switched off without deleting it, and its order changed by dragging.
- **Run** in the editor tries the command at once.
- Two commands cannot have the same name (nor two events, QuickMenus, macros or K-Board profiles): if you type one that
  already exists, the previous name stays.
- An action that opens something chooses whether it runs **as administrator or as a normal user**.

## If… (conditions)

The **If…** action checks something and, if it is not so, stops the command or skips the next actions. It can check:

| Condition | Example |
|---|---|
| A program is open / is in front | «If Steam is open» |
| A time and days | «If it's Monday to Friday from 9:00 to 18:00» |
| A device is connected | «If the Xbox controller is connected» |
| The sound plays through a device / something is playing | «If sound plays through the headphones» |
| A network | «If connected to HomeWiFi», «If there's no network» |
| Battery or mains | «If running on battery» |
| A host answers | «If 192.168.1.10 answers» |
| A file exists | «If D:\backup.lock exists» |
| The clipboard | «If the clipboard contains "http"» |
| CPU, GPU or memory use | «If the processor is above 80 %» |

Every condition can be negated («isn't», «doesn't»…).

## History

*History* lists everything that ran: which command, when, from where (hotkey, menu, launcher, voice, phone, network,
event, assistant, MCP server, command line…) and whether it went well.

![History](../../images/en/history.png)

## Organising

- **Search** by name, hotkey or action, and **filter** the list: all commands, those in no QuickMenu, or those of one
  QuickMenu.
- **Enable or disable** a command, **hide** it from the list (it still works), **duplicate** it or delete it (it also leaves
  the QuickMenus it was in).
- **Available for remote control**: whether the phone and the network can see and run it.
- **Icon**: automatic (from its first action), a built-in vector icon in any colour, a Windows icon library, any image or a
  website's favicon.
- In the editor, **Test only this action** runs one action alone.
