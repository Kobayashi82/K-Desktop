# Ming-3.0-tts frente a Kokoro (prueba del 25/10/2026, rama `decision_nvidia`)

**Conclusión: descartado como voz de los asistentes.** Habla bien en español y entona mejor que Kokoro, pero en la RTX
4070 SUPER va a tiempo real o más lento (Kokoro va 6 veces más rápido que el tiempo real, en el procesador), ocupa
~2,9 GB de VRAM que le quitaría al cerebro y solo funciona con Python + PyTorch + CUDA (varios GB de dependencias).

## Qué se probó

- **Ming-3.0-tts-0.5b** (inclusionAI, MIT, octubre de 2026): modelo de lenguaje + cabeza DiT de *flow matching* +
  códec propio, 44,1 kHz. Tarea `MULTILINGUAL`, `es`, copiando la voz de una frase de referencia dicha por Kokoro
  (`ef_dora`, la de los asistentes del usuario), así las dos suenan a la misma persona. PyTorch 2.6 + CUDA 12.4,
  bf16, sin flash-attn (no hay rueda para Windows; usa SDPA). El modelo «Tiny» (7,9B MoE) ni se probó: piden una H800.
- **Kokoro** tal como lo usa K-Desktop (`KokoroEngine`, fonemas españoles propios, `ef_dora`, procesador).
- Las 8 frases de `tools/tts/frases.txt` (respuestas típicas, de 1 a 10 s). Ming: una pasada de calentamiento y dos de
  medida; también con 5 pasos de *flow* en vez de 10 (lo más rápido que tiene sentido).
- Que se entienda: cada audio leído con el Parakeet de K-Desktop y comparado con la frase.

## Cifras

| | Kokoro (CPU) | Ming 0.5b, 10 pasos | Ming 0.5b, 5 pasos |
|---|---|---|---|
| Tiempo / duración del audio | **0,16** | 1,33–1,71 (media ~1,43) | 0,84–1,20 (media ~0,92) |
| «Vale, ya está.» (1,2 s) | 0,23 s | 1,76 s | 1,20 s |
| Frase de 4,5 s | 0,70 s | 6,2 s | 3,8 s |
| Primer trozo de la frase larga (10 s) | 0,34 s | 9,4 s | 5,9 s |
| Memoria | ~RAM, sin gráfica | 2,9 GB de VRAM | igual |
| Carga | ~1 s | ~2,5 s (en caliente) | igual |
| Errores al leerlo con Parakeet | 4,9 % | 5,8 % | 4,9 % |

- Los errores de lectura son casi todos números (Parakeet escribe «5», «20», «120»); los dos se entienden igual. Kokoro
  dijo «Trome» por «Chrome» y «veinte» se oyó «2»; Ming dijo bien «Chrome».
- Parakeet puso los «¿…?» en las preguntas de Ming: entona mejor las preguntas y las exclamaciones.
- La gráfica trabajaba al ~30 % con Ming: el freno es el bucle de Python paso a paso (un paso del modelo de lenguaje
  + 5–10 de la cabeza DiT por cada trozo de audio), no la gráfica. Sin vLLM-Omni (solo Linux) no hay forma de acelerarlo
  aquí; tampoco hay ONNX ni GGUF para meterlo en el proceso de los asistentes.
- No se midió con Bonsai cargado: ya solo va a tiempo real o más lento, y compartiendo gráfica con el cerebro solo
  puede ir peor (y 2,9 GB más le acercan a los ~8 GB donde el PC se pone lento).

## Qué haría falta para volver a mirarlo

Una versión en ONNX/GGUF, o una que vaya bastante más deprisa que el tiempo real en una gráfica de 12 GB compartida con
el LLM. Lo que sí tiene y Kokoro no: copiar voces de una grabación, voces descritas con palabras y marcas de risa,
suspiro… (estas solo en chino e inglés).

## Cómo repetirlo

`tools/tts/README.md`.
