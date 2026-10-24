# Vídeo explicativo de K-Desktop

El vídeo se hace con código: el guion es un JSON, la voz la pone [Kokoro](https://huggingface.co/hexgrad/Kokoro-82M)
(la misma voz natural de los asistentes de K-Desktop) y la imagen es una página web animada con
[Remotion](https://www.remotion.dev/) que se graba fotograma a fotograma. Cambiar una frase, una escena o el idioma
es editar un archivo y volver a generar.

- **Duración:** unos 17 minutos, en 14 capítulos (intro, idea general, comandos y acciones, QuickMenu, lanzador,
  eventos, macros, K-Board, voz, control remoto, logros, opciones, asistentes y cierre).
- **Formato:** 1920 × 1080, 30 fps, H.264, audio AAC masterizado a −15 LUFS. Subtítulos aparte en `.srt`.
- **Imagen:** interfaces recreadas y animadas a partir del código de K-Desktop (colores, iconos Fluent, textos de
  `en.json`), las capturas reales de [`../captures`](../captures) y la app web real del móvil
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
- Python 3.11+ con `pip install -r tools/requirements.txt` (mejor en un entorno virtual). El modelo de Kokoro
  (~350 MB) se descarga solo la primera vez en `~/.cache/kokoro` (o en `KOKORO_DIR`).
- ffmpeg en el `PATH`.
- Chromium: el de Playwright si está instalado (`tools/common.mjs` lo busca; también `REMOTION_BROWSER`), si no
  Remotion descarga el suyo. `tools/webapp.mjs` necesita Playwright.

## Generarlo

```
npm install
python tools/assets.py            # efectos, música, grano y capturas
node tools/webapp.mjs --lang en   # la app web del móvil
node tools/icons.mjs              # iconos de las escenas
python tools/tts.py --lang en     # voz, línea de tiempo y out/subtitles.en.srt
node render.mjs                   # → out/K-Desktop-en.mp4
```

Opciones de `render.mjs`: `--crf 20` (calidad; más bajo, más calidad y más peso), `--subtitles` (subtítulos
grabados en la imagen), `--scale 0.5` (versión ligera para revisar), `--chapter voice` (un capítulo solo).

Para ver y retocar en directo: `npm run studio` abre Remotion Studio en el navegador, con la línea de tiempo, los
capítulos (composición `chapter`, propiedad `chapter`) y el vídeo entero (`kdesktop-en`).

## Cambiar cosas

- **Lo que se dice:** `script/en.json`. Cada beat es una frase o un párrafo corto; `pause` (segundos) alarga el
  silencio de después. Al volver a pasar `tools/tts.py` solo se dicen de nuevo las frases que cambiaron, y todo lo
  demás se recoloca solo.
- **Sincronía:** `{nombre}` delante de una palabra marca el momento en que se dice; las escenas lo usan con
  `B('beat').cue('nombre')`. Si se quita una marca que una escena usa, el render avisa de cuál falta.
- **Pronunciación:** `script/pronunciation.<lang>.json`, reglas `[expresión regular, cómo decirlo]`.
- **La voz:** `tts.voice` del guion (por ejemplo `af_heart`, `bf_emma`, `am_michael`; en español `ef_dora`,
  `em_alex`) y `speed`.
- **La música:** sustituye `public/sfx/music.wav` por tu pista (estéreo, 48 kHz, 3 minutos; se repite con
  fundidos). Su volumen y cuánto baja cuando habla la voz están en `src/Video.tsx` (`Music`).
- **Una escena:** `src/scenes/<capítulo>.tsx`. Para revisar sin renderizar el vídeo:
  `node tools/stills.mjs voice 120 50% 900` deja PNG en `out/stills/`.
- **Las capturas:** en [`../captures`](../captures), con los nombres que usan las escenas (`commands`,
  `action-picker`, `quickmenu-closeup`, `settings-web`, `pair-qr`, `history`, `achievements`, `stats`,
  `achievement-toast`, `settings-appearance`, `settings-launcher`, `settings-voice`, `assistant-settings`,
  `assistant-install`, `desktop-assistants`, `brain`, `assistant-voice`, `tools-builtin`, `tools-mcp-server`,
  `memory`, `knowledge` y el clip `clips/lander-says-hello.mp4`). Tras cambiarlas: `python tools/assets.py`.

## Versión en español

1. Copia `script/en.json` a `script/es.json`, traduce los textos y conserva los ids de capítulos y beats y las
   marcas `{cue}` (pegadas a la palabra equivalente). En `tts`: `"voice": "ef_dora"` (o `em_alex`) y
   `"kokoroLang": "es"`. Crea `script/pronunciation.es.json` si alguna palabra suena mal.
2. Capturas en español (opcional): en `../captures/es/` con los mismos nombres; las que falten se toman de
   `../captures/`.
3. `python tools/assets.py`, `node tools/webapp.mjs --lang es`, `python tools/tts.py --lang es` y
   `node render.mjs --lang es`.

Los rótulos que dibujan las escenas (nombres de acciones, botones de las interfaces recreadas…) están en inglés en
el código de las escenas: para la versión en español hay que traducirlos también.

## Licencias

- Kokoro-82M: Apache 2.0. kokoro-onnx: MIT.
- Inter y JetBrains Mono: SIL Open Font License 1.1 (`public/fonts/LICENSE.md`).
- Fluent UI System Icons: MIT.
- Remotion: [licencia de Remotion](https://www.remotion.dev/license) (gratuita para particulares y equipos pequeños).
- La música y los efectos se sintetizan en `tools/assets.py`; el resto de imágenes son de K-Desktop y sus asistentes.
