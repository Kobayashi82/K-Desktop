# Vídeo explicativo de K-Desktop

El vídeo se hace con código: el guion es un JSON, la voz la pone [Kokoro](https://huggingface.co/hexgrad/Kokoro-82M)
(la misma voz natural de los asistentes de K-Desktop) y la imagen es una página web animada con
[Remotion](https://www.remotion.dev/) que se graba fotograma a fotograma. Cambiar una frase, una escena o el idioma
es editar un archivo y volver a generar.

- **Duración:** unos 17 minutos, en 14 capítulos (intro, idea general, comandos y acciones, QuickMenu, lanzador,
  eventos, macros, K-Board, voz, control remoto, logros, opciones, asistentes y cierre).
- **Formato:** 1920 × 1080, 30 fps, H.264, audio AAC masterizado a −15 LUFS. Subtítulos aparte, en `.srt`.
- **Imagen:** interfaces recreadas y animadas a partir del código de K-Desktop (colores, iconos Fluent, textos de
  `en.json`), las capturas reales de [`../captures/en`](../captures/en) y la app web real del móvil
  (`src/KDesktop.Core/Web`) renderizada con datos inventados.
- **Sonido:** la narración, efectos y una música de fondo sintetizados aquí mismo (sin licencias de terceros) y el
  sonido de logro de K-Desktop (`Logro.wav`).

## Estructura

```
script/en.json              el guion: capítulos → beats (frases), con marcas {cue} para sincronizar
script/pronunciation.en.json  cómo debe pronunciar la voz algunas palabras (K-Desktop → «Kay Desktop»…)
src/Video.tsx               el montaje: capítulos, transiciones, barra de progreso, voz, música
src/scenes/*.tsx            la imagen de cada capítulo (src/scenes/index.ts los asocia por id)
src/components/             piezas: cámara, cursor, teclas, ventanas, QuickMenu, iconos, capturas…
tools/tts.py                genera la voz y la línea de tiempo (public/generated/timeline.<lang>.json)
tools/assets.py             grano, efectos, música y copia de las capturas
tools/webapp.mjs            capturas de la app web real en un móvil (public/webapp/<lang>/)
tools/icons.mjs             extrae los iconos Fluent que usan las escenas
tools/stills.mjs            renderiza fotogramas sueltos para revisar sin hacer el vídeo
render.mjs                  renderiza el vídeo y masteriza el audio
public/brand, public/fonts  logo, Lander, Kobii, Pip y las fuentes (Inter y JetBrains Mono, OFL)
```

Lo generado (voz, efectos, capturas copiadas, `out/`) no se sube al repositorio: se rehace con las herramientas.

## Requisitos

- Node 22 y `npm install` en esta carpeta.
- Python 3.11+ con `pip install -r tools/requirements.txt`, mejor en un entorno virtual en `.venv` (git lo ignora):
  `uv venv .venv` y `uv pip install --python .venv/Scripts/python.exe -r tools/requirements.txt`. El modelo de Kokoro
  (~350 MB) se descarga solo la primera vez en `~/.cache/kokoro` (o en `KOKORO_DIR`).
- ffmpeg en el `PATH`.
- Chromium: el *headless shell* que Playwright haya descargado (`tools/common.mjs` lo busca en Linux, Windows y
  macOS; también `REMOTION_BROWSER`), si no Remotion descarga el suyo. `tools/webapp.mjs` necesita Playwright
  (`npm i --no-save playwright`); usa ese mismo Chromium.

## Generarlo

```
npm install
python tools/assets.py            # efectos, música, grano y capturas
node tools/webapp.mjs --lang en   # la app web del móvil
node tools/icons.mjs              # iconos de las escenas
python tools/tts.py --lang en     # voz, línea de tiempo y out/subtitles.en.srt
node render.mjs                   # → out/K-Desktop-en.mp4 y out/K-Desktop-en.srt
```

Opciones de `render.mjs`: `--crf 20` (calidad; más bajo, más calidad y más peso), `--scale 0.5` (versión ligera para revisar), `--chapter voice` (un capítulo solo), `--frames 2600-2899` (solo esos fotogramas).
En Windows, Chromium dibuja con la tarjeta gráfica (`--gl angle`, por defecto; `REMOTION_GL` lo cambia): con una RTX
4070 SUPER va unas 2,5 veces más rápido que por CPU (`--gl swiftshader`), unos 25 fotogramas por segundo.
Usa todos los núcleos (`--concurrency n` para menos). El vídeo entero tarda unas 2 horas en un ordenador de 4 núcleos.
Los subtítulos nunca van grabados en la imagen: el `.srt` va al lado del `.mp4`, con el mismo nombre, y los
reproductores lo cargan solos.

Para ver y retocar en directo: `npm run studio` abre Remotion Studio en el navegador, con la línea de tiempo, los
capítulos (composición `chapter`, propiedad `chapter`) y el vídeo entero (`kdesktop-en`).

## Cambiar cosas

- **Lo que se dice:** `script/en.json`. Cada beat es una frase o un párrafo corto; `pause` (segundos) alarga el
  silencio de después, y `"heading": true` dice como título aparte lo que va antes de los dos puntos («Sound: the
  volume…» → «Sound.», pausa, «the volume…»). Al volver a pasar `tools/tts.py` solo se dicen de nuevo las frases que cambiaron, y todo lo
  demás se recoloca solo.
- **Sincronía:** `{nombre}` delante de una palabra marca el momento en que se dice; las escenas lo usan con
  `B('beat').cue('nombre')`. Si se quita una marca que una escena usa, el render avisa de cuál falta.
- **Pronunciación:** `script/pronunciation.<lang>.json`, reglas `[expresión regular, cómo decirlo]`.
- **Subtítulos:** salen solos de lo que se dice: frases de hasta dos líneas, cortadas donde la voz hace pausa.
  En el guion, `subtitles.width` son los caracteres por línea y `subtitles.keep` los nombres que nunca se
  parten (Home Assistant, Wake on LAN…). Van al `.srt`.
- **La voz:** `tts.voice` del guion (por ejemplo `af_heart`, `bf_emma`, `am_michael`; en español `ef_dora`,
  `em_alex`) y `speed`.
- **La música:** sustituye `public/sfx/music.wav` por tu pista (estéreo, 48 kHz, exactamente 4 minutos y que
  enlace sin cortes, porque las copias van seguidas sin fundido; `tools/assets.py` no la pisa si ya está). Su volumen y cuánto baja cuando habla la voz están en `src/Video.tsx` (`Music`).
- **Una escena:** `src/scenes/<capítulo>.tsx`. Para revisar sin renderizar el vídeo:
  `node tools/stills.mjs voice 120 50% 900` deja PNG en `out/stills/`, y
  `node tools/stills.mjs all --every 150 --scale 0.25` un fotograma cada 5 segundos de todo el vídeo
  (con `python tools/sheet.py hoja.png out/stills/all-*.png` se juntan en una hoja). Las escenas dejan libre la
  franja de abajo, donde el reproductor pone los subtítulos.
- **Las capturas:** en [`../captures/en`](../captures/en) (las de español en `../captures/es`), con los nombres que usan las escenas (`commands`,
  `action-picker`, `quickmenu-closeup`, `settings-web`, `pair-qr`, `history`, `achievements`, `stats`,
  `achievement-toast`, `settings-appearance`, `settings-launcher`, `settings-voice`, `assistant-settings`,
  `assistant-install`, `desktop-assistants`, `brain`, `assistant-voice`, `tools-builtin`, `tools-mcp-server`,
  `memory`, `knowledge` y el clip `clips/lander-says-hello.mp4`). Tras cambiarlas: `python tools/assets.py`.

## Vídeos cortos

Vídeos de 2 a 3 minutos, uno por función (el plan de los 17 está en `ARREGLAR.md`, local). Usan este mismo proyecto:
la voz, la música, los efectos, las capturas, los componentes y muchas escenas del vídeo largo.

```
shorts/<nn-nombre>/en.json        su guion (mismo formato que script/en.json, más number, title, next y nextNumber)
shorts/<nn-nombre>/scenes/        sus escenas (index.ts las asocia a los capítulos y pone los colores de fondo)
src/shorts.ts                     la lista de vídeos cortos (añadir ahí cada uno nuevo)
src/components/Short.tsx          lo común: la cartela del título, «qué vas a ver» y el cierre («Next up»)
```

- Cada vídeo tiene capítulos con la misma estructura: `hook` (título y qué resuelve), los del ejemplo, `tip` (el truco)
  y `end` (el siguiente vídeo y gracias, con `ShortEnd`).
- Para reutilizar una escena del vídeo largo, el guion lleva los mismos ids de beats y las mismas marcas `{cue}` que
  ella espera (por ejemplo `BuildPart` necesita `page`, `hotkey`, `hotkey2` y `actions` en el mismo capítulo).
- Los clips reales de los asistentes (los WebP del escaparate, los de «Probar») se usan con `<Showcase clip="Lander/window.minimize" />`
  (`src/components/Showcase.tsx`); `python tools/showcase.py` busca los que nombran las escenas y los pasa a MP4 en
  `public/generated/showcase/` (Lander, Kobii y Kumo salen de `assistants/<Nombre>/src/*/Package/showcase`, el repositorio de
  asistentes clonado dentro; Pip, de `samples/CharacterAssistant/Package/showcase`). Hay que ejecutarlo tras añadir un clip a una escena.
- Lo generado va aparte del vídeo largo: `public/audio/shorts/<id>/`, `public/generated/shorts/<id>/` y
  `out/shorts/<id>/`.

```
python tools/tts.py --lang en --short 01-getting-started
node tools/stills.mjs all --short 01-getting-started --every 75 --scale 0.25   # fotogramas para revisar
node render.mjs --short 01-getting-started    # → out/shorts/01-getting-started/K-Desktop-01-getting-started-en.mp4 (+ .srt)
```

Los vídeos cortos salen ya comprimidos: el masterizado vuelve a codificar la imagen con x264 (`slow`, crf 28: unos 0,6 Mbit/s, como el vídeo largo, sin diferencia visible con crf 22). Con
`--compress <crf>` se cambia (también sirve para el vídeo largo, que por defecto no se recomprime). En Remotion Studio
cada uno es la composición `short-<id>-<lang>`.

## Versión en español

Está hecha: `script/es.json` (mismos ids de capítulos y beats y mismas marcas `{cue}`, sobre la palabra equivalente),
narrada con **ElevenLabs** (voz «Cristina», `eleven_v4_turbo`), con las capturas de `../captures/es/` (las que falten se
toman de `../captures/en/`) y los rótulos de las escenas traducidos. Se rehace con:

```
python tools/assets.py
node tools/webapp.mjs --lang es
python tools/tts.py --lang es       # necesita ELEVENLABS_API_KEY (también la lee de las variables de usuario de Windows)
node render.mjs --lang es           # → out/K-Desktop-es.mp4 y out/K-Desktop-es.srt
```

- **ElevenLabs:** en el guion, `tts` con `"engine": "elevenlabs"`, `voice` (id de la voz), `model`, `language` y
  opcionalmente `speed`, `seed` y `settings` (`voice_settings` de la API). Da el momento exacto de cada letra, así que
  las marcas `{cue}` caen justo en su palabra. Cada frase dicha se guarda en `.cache/tts/es` y `tts.py` dice cuántos
  créditos ha gastado (el guion entero, unos 17 000 caracteres, fueron ~3 300 créditos). El plan gratuito no deja usar
  voces de la biblioteca por la API.
- **Otra voz en una frase:** `[[kokoro ef_dora]]` al principio de una frase la dice con ese motor y esa voz (en la
  versión en español, Kokoro se presenta con su voz española: «Así suena en español: [[kokoro ef_dora]] Hola, soy
  Kokoro…»).
- **Pronunciación:** `script/pronunciation.es.json` (QuickMenu → QuickMenú). «K-Desktop» se deja como lo dice la voz.
- **Rótulos de las escenas:** están escritos en inglés dentro de `t('…')` (o `tr('…')` donde la escena ya tiene una
  variable `t`), y `src/lib/i18n.es.ts` tiene la traducción de cada uno; lo que no está sale en inglés (nombres,
  código, teclas). `node tools/i18n-check.mjs --lang es` lista los que faltan. El idioma lo toma `src/lib/i18n.ts` de
  las propiedades del render. En el lanzador, los resultados que resaltan las letras escritas («cmfy») conservan esas
  letras en su sitio («ComfyUI: resultados»), o se quedan en inglés.
- Las capturas en español se hacen con `tutorial/tools` (`../tools`) y `KD_TUTORIAL_LANG=es` (ver su README).

## Licencias

- Kokoro-82M: Apache 2.0. kokoro-onnx: MIT.
- Inter y JetBrains Mono: SIL Open Font License 1.1 (`public/fonts/LICENSE.md`).
- Fluent UI System Icons: MIT.
- Remotion: [licencia de Remotion](https://www.remotion.dev/license) (gratuita para particulares y equipos pequeños).
- La música y los efectos se sintetizan en `tools/assets.py`; el resto de imágenes son de K-Desktop y sus asistentes.
