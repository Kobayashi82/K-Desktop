# Phone and remote control

## Web app (phone, tablet, any browser)

*Settings → Web app* turns on a small web server on your PC (port 2502 by default). From a phone on the same network you
get your commands and menus, the volume and the audio output, a touchpad and keyboard, voice orders and the assistants'
chat. It can be installed on the phone as an app (PWA).

![Pairing a device](../../images/en/pair-qr.png)

1. Turn on **Web server**.
2. **Pair a device**: scan the QR code with the phone (or open the link it shows).
3. That is it: the device stays paired until you **revoke** it in *Paired devices*.

The tabs of the app:

| Tab | What it has |
|---|---|
| **Menus** | Your QuickMenus |
| **Commands** | Every command marked *Available for remote control* |
| **Audio** | Volume, mute, play/pause, next, previous, and the audio output |
| **Touchpad** | Move the mouse, click, scroll and type on the PC (only with *Touchpad and keyboard* on) |
| **Assistant** | The chat with the assistants, written or spoken |
| **Settings** | Theme, language, the app's options |

Notes:

- Nothing works until a device is paired, **even from the same PC**.
- Listening on the network needs K-Desktop to run as administrator (as it does by default); otherwise the server only
  answers this PC.
- *Internet address* is optional, only if you come in from outside through a reverse proxy.

## Android app

The Android app is the same web app with a native microphone. *Settings → Android app → Save the APK…*, copy it to the
phone and open it (Android asks to allow installing apps from that source). It needs no store: when a phone with an older
version connects, the app offers to update itself from the PC.

## UDP

*Settings → Remote control → UDP server* (port 2501) accepts plain text messages, compatible with K-Desktop 4:

| Message | What it does |
|---|---|
| `list` | Answers with the list of remote commands |
| A number or a name | Runs that remote command |
| `event <name>` | Runs the events with *Call from outside* and that name |
| `play`, `pause`, `next`, `previous`, `subir volumen`, `bajar volumen`, `silenciar volumen` | Media keys |
| `sk <keys>` / `sendkey <keys>` | Sends a key combination |
| `show kdesktop` | Shows K-Desktop's window |

*Accept from other devices* lets other computers send them; when off, only this PC. Home Assistant, a script, another PC or
a phone app can use it.

## Web API

Paired devices can also call the API directly, for instance to run an event:

```
POST http://<PC>:2502/api/event
X-KD: 1
Authorization: Bearer <token>

{"name": "door"}
```
