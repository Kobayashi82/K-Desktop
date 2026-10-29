# Vídeo: cómo funcionan los asistentes por dentro

Un vídeo en inglés que sigue una orden («Lander, what time is it?») paso a paso, desde la palabra de activación hasta que
el asistente termina de hablar: los dos procesos y la tubería, la palabra de activación (detector y verificación),
escuchar la orden, quién decide qué se hace (reglas fijas, tus comandos por palabras y por significado, la decisión de
una letra, los programas y la IA), los permisos y los gestos, qué herramientas ve la IA y cómo se eligen, los tipos de
herramientas, el bucle de herramientas y sus comprobaciones, comandos y eventos, la voz y lo que pasa después.

Se hace igual que el tutorial de [`../video`](../video): el guion es un JSON, la voz la pone
[Kokoro](https://huggingface.co/hexgrad/Kokoro-82M) con la misma voz que el tutorial en inglés (`af_heart`, en-us) y
la imagen son diagramas animados con [Remotion](https://www.remotion.dev/). No usa capturas de K-Desktop.

- **Duración:** unos 21 minutos, en 19 capítulos.
- **Formato:** 1920 × 1080, 30 fps, H.264, audio AAC masterizado a −15 LUFS. La copia de `docs/videos` está
  recomprimida para quedar por debajo de 100 MB (GitHub no admite archivos mayores; ver «Comprimirlo» abajo).
- **Subtítulos:** nunca grabados en la imagen; van aparte, en un `.srt` con el mismo nombre que el `.mp4`.
- **Datos:** los umbrales, tiempos y nombres salen del código (`src/KDesktop.Assistant.Brain/Talk`,
  `src/KDesktop.Assistant.Tools`, `src/KDesktop.Core/Voice`) y de [`../../assistant`](../../assistant).

## Estructura

```
script/en.json                 el guion: capítulos → beats (frases), con marcas {cue} para sincronizar
script/pronunciation.en.json   cómo debe decir la voz algunas palabras (K-Desktop → «Kay Desktop», 0.82…)
src/Video.tsx                  el montaje: capítulos, títulos, barra de progreso, voz y música
src/scenes/*.tsx               la imagen de cada capítulo (src/scenes/index.ts los asocia por id)
src/components/Kit.tsx         piezas: paneles, mensajes que viajan, código, medidores, el Lander…
tools/tts.py                   la voz y la línea de tiempo (public/generated/timeline.en.json, out/subtitles.en.srt)
tools/assets.py                efectos y música (con la síntesis de ../video/tools/assets.py)
tools/icons.mjs                los iconos Fluent que usan las escenas
tools/stills.mjs               fotogramas sueltos para revisar sin hacer el vídeo
render.mjs                     renderiza el vídeo, masteriza el audio y deja el .srt al lado
```

## Generarlo

Necesita Node 22, Python 3.11+ (`pip install -r tools/requirements.txt`, mejor en un `.venv`), ffmpeg y Chromium (lo
busca como el tutorial, ver `tools/common.mjs`).

```
npm install
python tools/assets.py            # efectos y música
node tools/icons.mjs              # iconos de las escenas
python tools/tts.py --lang en     # voz, línea de tiempo y out/subtitles.en.srt
node render.mjs                   # → out/K-Desktop-Assistants-en.mp4 y out/K-Desktop-Assistants-en.srt
```

`render.mjs` acepta `--crf`, `--scale 0.5` (versión ligera), `--chapter <id>` y `--concurrency n`. Para revisar:
`node tools/stills.mjs wake 120 50%` o `node tools/stills.mjs all --every 180 --scale 0.25`, y
`python tools/sheet.py hoja.png out/stills/all-*.png` junta los fotogramas en una hoja. `npm run studio` abre Remotion
Studio.

## Comprimirlo para GitHub

El render ocupa unos 700 MB (sobre todo por el grano de película). Para dejarlo en ~96 MB: dos pasadas de x264 a
510 kbps quitando el grano, y el audio a 96 kbps con 2 dB menos y un limitador (sin él, la recompresión dejaba picos
por encima de 0 dB):

```
ffmpeg -i out/K-Desktop-Assistants-en.mp4 -vf hqdn3d=3:3:8:8 -c:v libx264 -preset slower -b:v 510k -pass 1 -an -f null -
ffmpeg -i out/K-Desktop-Assistants-en.mp4 -vf hqdn3d=3:3:8:8 -c:v libx264 -preset slower -b:v 510k -pass 2 -an out/v.mp4
ffmpeg -i out/v.mp4 -i out/K-Desktop-Assistants-en.mp4 -map 0:v -map 1:a -c:v copy -af "volume=-2dB,alimiter=limit=0.85:level=false" \
  -c:a aac -b:a 96k -movflags +faststart ../../videos/K-Desktop-Assistants-en.mp4
```

## Cambiar cosas

- **Lo que se dice:** `script/en.json`. Al volver a pasar `tools/tts.py` solo se dicen de nuevo las frases que cambiaron.
- **Sincronía:** `{nombre}` delante de una palabra marca el momento en que se dice; las escenas lo usan con
  `B('beat').cue('nombre')`.
- **La voz:** `tts.voice` y `speed` del guion (es la del tutorial en inglés: cámbiala en los dos si quieres otra).
- **Una escena:** `src/scenes/<capítulo>.tsx`.

El vídeo terminado está en [`../../videos/K-Desktop-Assistants-en.mp4`](../../videos/K-Desktop-Assistants-en.mp4), con
sus subtítulos en [`K-Desktop-Assistants-en.srt`](../../videos/K-Desktop-Assistants-en.srt).

## Licencias

Las mismas que el tutorial: Kokoro-82M (Apache 2.0), kokoro-onnx (MIT), Inter y JetBrains Mono (SIL OFL 1.1, en
`public/fonts/LICENSE.md`), Fluent UI System Icons (MIT) y Remotion
([su licencia](https://www.remotion.dev/license)). La música y los efectos se sintetizan.
