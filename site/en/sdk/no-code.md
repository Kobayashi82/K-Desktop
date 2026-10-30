# Without code

An assistant can be just pictures and JSON: instead of an assembly, its `entry` is a `character.json`
([schema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/character.schema.json)) and
K-Desktop moves it.

It stands on the taskbar or on top of the windows, strolls now and then (and sometimes falls off the edge of a window),
follows the window it stands on, can be dragged and dropped, sleeps when you are away, and plays an animation for each
thing: listening to you, thinking, talking in its bubble, being glad or sorry about what happened, a click. It talks,
thinks and uses tools like any other assistant; its voice and personality are set in *Personality* and *Voice*. With
several assistants on the desktop it never stays on top of another.

Nothing of the package runs: K-Desktop only reads its pictures and sounds, and its install card says it has no code.
**Pip** ([`samples/CharacterAssistant`](https://github.com/Kobayashi82/K-Desktop/tree/main/samples/CharacterAssistant)) is a
complete example.

## The files

```
MyCharacter\
  assistant.json      "entry": "character.json", "traits": { "travel": "walk", "gestures": ["communicate", "react", "announce"] }
  character.json
  icon.png
  frames\idle0.png …  PNG with transparency, all the same height
  sounds\hello.wav    optional
```

```json
{
  "height": 80,
  "faces": "right",
  "walkSpeed": 60,
  "animations": {
    "idle": { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2 },
    "walk": { "strip": "frames/walk.png", "frames": 6, "fps": 10 },
    "talk": { "frames": ["frames/talk0.png", "frames/talk1.png"], "fps": 8, "sound": "sounds/hello.wav" }
  }
}
```

| Field | |
|---|---|
| `height` | Its height at 100 % size; every frame is drawn this tall. Its feet are the bottom edge of the picture, unless `ground` says otherwise |
| `ground` | Transparent rows under its feet in the pictures (pixels of the pictures, 0 by default). That much of each picture goes below the ground, so the feet touch it (a shadow under the feet can be left there) |
| `faces` | The way the pictures look; walking the other way, they are mirrored |
| `walkSpeed` | How fast it walks |
| `animations` | The animations, by name (below) |

## Animations

| Name | When |
|---|---|
| `idle` | At rest (**the only required one**) |
| `walk` | Walking |
| `talk`, `listen`, `think` | Talking in its bubble, listening to you, thinking |
| `happy`, `sad`, `surprised` | How it went: well, badly, or a surprise |
| `click` | You clicked it |
| `drag`, `fall` | Picked up with the mouse, falling |
| `sleep` | You are away |
| `enter`, `exit` | Coming onto the desktop and leaving |
| `announce` | Before saying something on its own (a timer, a notice): it walks towards the mouse first |
| `catch` | Files dropped on it (while they are over it, it carries their picture over its head) |

A missing one uses another: `think` → `listen` → `idle`, `click` → `happy` → `idle`, `announce` and `catch` → `happy`,
`drag` → `fall` → `idle`…

Each animation has:

- `frames` (a list of pictures) **or** `strip` + `frames` (one strip of N frames of the same width);
- `fps`;
- `loop`: by default all repeat except `happy`, `sad`, `surprised`, `click`, `enter`, `exit`, `announce` and `catch`;
- `sound`: a WAV played when it starts.

## Variants

Instead of one animation, a list. Each time it plays, K-Desktop picks one by its `weight` (twice the weight, twice as
often; 1 by default), respecting its `cooldown` (seconds it rests after coming up), and never repeating the last one for
those that play once; those that repeat (`idle`, `walk`, `talk`…) pick again at the end of each round.

In `enter` and `exit`, `when` says which reason each one is for: `startup` (K-Desktop starts or the module is switched
on) and `summoned` (the user puts it on the desktop); `dismissed` (the user hides it) and `uninstalling` (it is removed).
With none for that reason there is no entrance or exit; coming back after a reload (a new version, another language) never
has one.

```json
"idle": [
  { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2, "weight": 6 },
  { "frames": ["frames/look0.png", "frames/look1.png"], "fps": 2, "cooldown": 20 }
],
"enter": [
  { "frames": ["frames/wake0.png", "frames/wake1.png"], "fps": 4, "when": ["startup"] },
  { "frames": ["frames/drop0.png", "frames/drop1.png"], "fps": 8, "when": ["summoned"] }
]
```

## Settings

Size, volume, how lively it is and whether it can be picked up with the mouse are set by the user on its card (the
[common settings](getting-started.md#common-settings)); a character takes them all.

## Making the package

```powershell
tools\character\pack.ps1 <folder>
```

It zips the folder (with `assistant.json` at its root) into `<id>-<version>.kai` with the paths as K-Desktop wants them.
Open it with K-Desktop to install it. To record clips of it for the catalog, see [Showcase](showcase.md#recording-them).
