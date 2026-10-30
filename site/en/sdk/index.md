# Making assistants

An assistant is a `.kai` package: a `.zip` with its manifest (`assistant.json`), its pictures and settings, and —if it has
code— its assemblies in `lib/`. There are two kinds:

| | What it is | For |
|---|---|---|
| **[Without code](no-code.md)** | Pictures (PNG frames) and a `character.json`. K-Desktop moves it: it walks on the taskbar and on top of windows, falls, sleeps, and plays an animation for each thing | Artists: a character in an afternoon, no programming |
| **[With code](getting-started.md)** | A C# class library against the **SDK** (`KDesktop.Assistant.Sdk`), drawn with WPF's drawing API | Anything: flying, physics, games, manoeuvres on windows, its own settings and panel |

Either way the assistant talks, thinks and uses tools like any other: the AI, the tools, the voice and the speech
recognition belong to K-Desktop. An assistant **never sees the AI, the tools or K-Desktop**: it gets the world (windows,
monitors, taskbar, the other assistants), its services, and **gestures** to perform (press this window's minimise button,
move that window there, say this, react to how it went). Each assistant decides how to perform them.

> [!NOTE]
> The SDK is version **1.0**: from here on it only grows without breaking. A package built against SDK 1.x runs on any
> K-Desktop with SDK 1.y, y ≥ x. It is on NuGet:
> [KDesktop.Assistant.Sdk](https://www.nuget.org/packages/KDesktop.Assistant.Sdk), and its template
> [KDesktop.Assistant.Templates](https://www.nuget.org/packages/KDesktop.Assistant.Templates).

## Examples

| | |
|---|---|
| [`templates/kdassistant`](https://github.com/Kobayashi82/K-Desktop/tree/main/templates/kdassistant) | The `dotnet new` template: the smallest complete assistant with code. It draws itself, has a click area, talks, reacts, changes colour with each state and shows a rainbow in its showcase |
| [`samples/CharacterAssistant`](https://github.com/Kobayashi82/K-Desktop/tree/main/samples/CharacterAssistant) | **Pip**, an assistant without code: pictures and JSON |
| [K-Desktop-Assistants](https://github.com/Kobayashi82/K-Desktop-Assistants) | The Lander, Kobii and Kumo: full assistants with code |

## The package

```
myassistant-1.0.0.kai
  assistant.json        the manifest
  icon.png
  lib\MyAssistant.dll   its code (or character.json for one without code)
  settings.json         its settings page (optional)
  panel.json            its panel (optional)
  persona\en.json …     its personality and lines (optional)
  voice\myassistant.json  its voice profile (optional)
  assets\ …             anything else
  signature.json        when it is signed
```

## The manifest

`assistant.json` ([schema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/assistant.schema.json)):

```json
{
  "$schema": "https://raw.githubusercontent.com/Kobayashi82/K-Desktop/main/src/KDesktop.Core/Assistant/Packages/Schemas/assistant.schema.json",
  "id": "myassistant",
  "name": "MyAssistant",
  "version": "1.0.0",
  "author": "Me",
  "description": "A new assistant for K-Desktop.",
  "sdk": "1.0",
  "entry": "lib/MyAssistant.dll!MyAssistant.MyAssistant",
  "icon": "icon.png",
  "wakeWord": "MyAssistant",
  "common": ["size", "volume", "liveliness", "dragging"],
  "traits": {
    "travel": "fly",
    "canMoveWindows": true,
    "gestures": ["pressButton", "moveWindow", "communicate", "react", "announce"]
  }
}
```

| Field | |
|---|---|
| `id`, `name`, `version`, `author`, `description`, `homepage`, `license` | Who it is. `description` can be one text or one per language (`{ "en": "…", "es": "…" }`). Installing one with the same `id` and a higher `version` updates it. Reserved ids: `data`, `settings`, `dev`, `kdesktop` |
| `sdk` | The SDK it was built against (`"1.0"`) |
| `entry` | `lib/File.dll!Name.Space.Type` (the class that implements `IAssistant`), or `character.json` for one without code |
| `icon`, `preview` | Its icon and a picture for its card |
| `wakeWord` | Suggested name to call it by: on install K-Desktop offers to record it with the user's voice |
| `model` | Suggested AI models (see [Its model](getting-started.md#its-model)) |
| `voice`, `persona` | Its voice profile and personality (see [Voice and personality](getting-started.md#voice-and-personality)) |
| `settings`, `panel` | Its settings page and its panel (`settings.json` and `panel.json` by default) |
| `traits` | How it travels (`fly`, `walk`, `teleport`), whether it can move windows, and which [gestures](gestures.md) it can perform (`communicate` and `react` are always taken as known) |
| `common` | The [common settings](getting-started.md#common-settings) it takes |
| `speech` | `"own"` to draw what it says itself (see [Its own text](host.md#its-own-text)) |
| `extras` | Its own actions (see [Extras](getting-started.md#extras)) |
| `updates`, `changes` | Where its latest version is, and what this one brings (see [Publishing](publishing.md#new-versions)) |
| `showcase` | Clips of what it can show, for the catalog (see [Showcase](showcase.md#clips-for-the-catalog)) |

The other files have their schemas too:
[settings](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/settings.schema.json),
[panel](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/panel.schema.json),
[persona](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/persona.schema.json),
[voice](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/voice.schema.json),
[character](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/character.schema.json) and
[catalog](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/catalog.schema.json).

## Installing

With the assistant module on, double-clicking the `.kai` (or `KDesktop.exe --install-assistant "file"`) opens its card in
K-Desktop. Before showing it, K-Desktop checks the package **without loading its code**: safe paths and sizes; the manifest,
settings, panel, persona and voice against their schemas; that the entry, the pictures and the showcase clips are there;
the SDK's version; the [signature](publishing.md#signing-the-package); and that the id is not reserved. If something is
wrong, the card says what.

It is installed in `Assistants\<id>\<version>\` of K-Desktop's data folder (`%APPDATA%\K-Desktop 5`). A new version
replaces the old one —live, if it is on the desktop— and keeps its data (`Assistants\Data\<id>\`) and its settings.
K-Desktop's backups keep that data and the list of installed assistants, not the packages.

## How it runs

- Assistants run in **their own process** (`KDesktop.exe --assistant`, as a normal user, never as administrator), each in
  its own `AssemblyLoadContext`. K-Desktop itself only reads the manifest.
- Several assistants can be on the desktop at the same time, in the same process and on the same drawing layer.
- An assistant that fails five times in a row is unloaded (only it; the others go on); one that brings the process down
  three times in a row is taken off the desktop. When it is unloaded, the host removes whatever it left (surfaces, click
  areas, subscriptions).
