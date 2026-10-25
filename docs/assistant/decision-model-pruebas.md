# Pruebas: modelos de decisión, Nemotron Bolt y palabra de activación

Rama `decision_nvidia`, 25/10/2026. Experimento: **no cambia K-Desktop**. El arnés está en `tools/decision/` (ver su
README); el conjunto de pruebas lleva los comandos reales del usuario y queda fuera del repositorio
(`artifacts/decision/`).

## 1. Equipo y versiones

- Ryzen 7 7800X3D, RTX 4070 SUPER 12 GB (driver 621.01), K-Desktop 5.0.199.
- llama-server del usuario: b10405 (no sirve ni `gemma4` ni `/v1/systemone`). Para las pruebas, **b11871** aparte
  (CUDA 13.4, en `%LOCALAPPDATA%\K-Desktop-exp`). Bonsai con su propio llama-server (PrismML, b10709).
- Todos los modelos **sin thinking** (`enable_thinking: false`); los de decisión no generan texto.

## 2. Lo comprobado frente a los documentos de partida

| Dato de los documentos | Realidad |
|---|---|
| pplx-decider-flash: repositorio con acceso restringido | Abierto, sin token (Apache 2.0) |
| d1-3B Q4_K_M ~2,3–2,6 GB | 1,67 GB (Q8_0 2,87 GB); ~1,9 GB de VRAM con 4K de contexto |
| d1-3B «el mejor bajo 10B» (48,57) | Cifra de Liquid sobre el índice 0.2.1. En el Decision Index 0.4 público saca **31,1** |
| `/v1/systemone` solo para algunos modelos; ¿d1-3B? | d1-3B (`lfm2-d1`) sí, y también **pplx-decider** de forma nativa (hasta 255 opciones) |
| pplx-decider: necesita su ejecutor propio | llama.cpp lo sirve, pero **sin la atención no causal** de la v1.2: hay que forzarla (`--override-kv qwen35.attention.causal=bool:false`) o pierde ~7 puntos |
| Bolt: llama.cpp b11790 o posterior | La ficha de NVIDIA usa b11459 |
| Bolt: gana a Qwen en When2Call (63,4 vs 37,8)… | Cierto, pero **medido pensando** (la ficha lo dice); K-Desktop lo usa sin pensar. En Q4_K_M, Gemma 4 E4B le gana en When2Call (61,1 vs 60,5) |
| Bolt: «NVIDIA solo lo reentrenó en inglés» | Falso: la ficha incluye el español entre los idiomas principales del reentreno |
| Laya multilingüe por llama.cpp | El GGUF multilingüe es de `ggmlc` (no carga en llama.cpp); el de llama.cpp (`ggml-org/Laya-GGUF`) es solo inglés. Índice 0.4: 9,2 |
| d1-omni-600M «flojo» (15,95) | Índice 0.4: 11,8. En las pruebas, inservible (abajo) |

Otros modelos de decisión locales que existen (Decision Index 0.4, benchlm, 20/10/2026), por tamaño útil aquí:
StartLux-Decision 4B (54,8; **CC BY-NC**), pplx-decider-flash 4B (54,3; Apache), KnowLine 4B (56,0; LoRA sobre Qwen
que contesta con letras, inglés/chino), d3-mini 4B (50,0), elephant-1 9B (60,3; MIT), Kev 4B (34,9), Winnow-E4B
(32,7), GLiClass edge 0,4B (31,2). Por debajo de 1B ninguno pasa de ~30.

## 3. Comandos: el conjunto y cómo se decide

184 órdenes con los 117 comandos reales: dichas con otras palabras (80), errores de transcripción (8), apagar/no
apagar (16), «no es mío» (20), para la IA (11), casi empatadas (10), compuestas (7) y 32 en inglés, francés, alemán,
italiano y portugués. Cada caso dice qué vale: `run:…`, `ask:A|B` o `none`.

- **Hoy (voz)**: `IntentMatcher.Plan` con `SemanticMatcher` y sus umbrales, tal cual.
- **Hoy (asistente, sin IA)**: lo que hace `TalkService` con la respuesta de `AssistantCommands.Match` (ejecutar si es
  seguro, «¿A o B?», o a la IA). Su «no hace» es «se lo pasa a la IA», que puede acertar después: no se midió.
- **Híbrido**: lo que las palabras nombran seguro y lo que EmbeddingGemma ve claro (≥0,80 y 0,08 por delante) sigue
  igual; el resto (128 de 184 casos, la «zona dudosa») lo decide el modelo entre los candidatos de EmbeddingGemma más
  «ninguno»: ejecuta desde un umbral, pregunta si los dos primeros lo suman, y si no, nada.
- **Regla de apagar**: lo que apaga el PC sigue necesitando el 0,90 de EmbeddingGemma (`AcceptPower`) además del modelo.
  Sin ella, Bolt y Gemma apagaron el PC por «apaga la consola» y parecidas; con ella, **0 en todos** sin perder aciertos.
- «Un token»: los LLM normales contestan con una letra (`max_tokens` 1, `top_logprobs`) y se leen sus probabilidades.

## 4. Comandos: resultados

Mejor variante de cada uno (umbral de ejecutar entre paréntesis). «Ejecuta mal» = ejecutó algo que no tocaba.

| Método | Aciertos | Zona dudosa | Ejecuta mal | Apagar mal | Pregunta | Latencia p50/p95 | VRAM extra |
|---|---|---|---|---|---|---|---|
| Voz de K-Desktop hoy | 73,9 % | 62,5 % | 7 | 0 | 0 | — | 0 |
| Asistente hoy, sin IA | 53,3 % | 36,7 % | 5 | 1 | 9 | — | 0 |
| d1-3B Q4, GPU, todos los comandos (0,5) | 75,0 % | 64,1 % | 10 | 0 | 1 | 19/20 ms | ~1,9 GB |
| d1-3B Q8, GPU (0,5) | 72,3 % | 60,2 % | 8 | 0 | 3 | 19/20 ms | ~3,0 GB |
| d1-3B Q4, CPU (0,5) | 74,5 % | 63,3 % | 9 | 0 | 2 | 618/685 ms | 3,1 GB de RAM |
| d1-omni-600M, CPU (0,5) | 64,1 % | 48,4 % | 27 | 0 | 9 | 381/425 ms | — |
| pplx-decider-flash Q4, no causal (0,5) | 87,0 % | 81,3 % | 10 | 0 | 3 | 45/50 ms | ~3,2 GB |
| pplx-decider-flash Q4, no causal (0,7) | 85,9 % | 79,7 % | 8 | 0 | 8 | 45/50 ms | ~3,2 GB |
| **StartLux 4B Q8 (0,5)** | **88,6 %** | **83,6 %** | 5 | 0 | 4 | 46/50 ms | ~5,3 GB (4K) |
| StartLux 4B Q8 (0,7) | 85,9 % | 79,7 % | **2** | 0 | 9 | 46/50 ms | ~5,3 GB |
| **Qwen 3.5 4B, un token, top 10 (0,7)** | 83,7 % | 76,6 % | 6 | 0 | 18 | 94/106 ms | **0 si ya es el cerebro** |
| Qwen 3.5 9B, un token (0,7) | 77,7 % | 68,0 % | 10 | 0 | 9 | 124/146 ms | ídem |
| Gemma 4 E4B, un token (0,8) | 86,4 % | 80,5 % | 9 | 0 | 8 | 73/84 ms | ~3,4 GB |
| Nemotron Bolt E4B, un token (0,7) | 83,7 % | 76,6 % | 14 | 0 | 1 | 76/87 ms | ~3,6 GB |
| **Bonsai 27B, un token, top 10 (0,5)** | **87,0 %** | 81,3 % | 4 | 0 | 5 | 306/323 ms | **0 si ya es el cerebro** |

Lo que se ve:

- **La calibración importa más que el acierto bruto.** Qwen 4B da probabilidades con margen (mediana del máximo 0,88):
  el umbral filtra sus errores y pregunta cuando duda. Gemma y Bolt dicen ~1,00 casi siempre (Bolt: el 10 % más bajo
  sigue en 0,985), así que el umbral no frena nada.
- **El reentreno de Bolt no ayuda aquí**: acierta menos que Gemma 4 E4B, de la que sale, y ejecuta más cosas mal.
- **d1-3B** es rapidísimo pero elige «ninguno» por sistema; no mejora la voz de hoy. En CPU tarda 0,6 s.
- Las descripciones de las acciones ayudan a los modelos de decisión con pocas opciones y estorban con todas.
- Varios fallos no los puede arreglar ningún decisor: el comando bueno no estaba entre los candidatos de EmbeddingGemma
  (un programa que el nombre no describe: «el emulador de switch» → Eden).

## 5. Palabra de activación

### Lo que dicen los registros (20–25/10)

- **771 disparos de la fase 1 en 5 días; 424 (55 %) los descarta la verificación de Parakeet.** Unos 85 al día,
  casi todos con inglés de vídeos («Sometimes», «Dude», «Hello», «Ha ha ha») o silencio (transcripción vacía).
- Con Parakeet descargado (`voiceUnloadMinutes`, 10 min), `HandleWakeAsync` enseña **«Escuchando…» y el sonido antes
  de verificar** (`early`) y lo esconde sin decir nada si no se confirma: unos 22 «fantasmas» vistos en esos días. Es
  la activación sin decir nada que se nota, incluso sin nada sonando.
- **Cada disparo de la fase 1 baja la música en el acto** (`DuckScope` antes de verificar), aunque luego se descarte.
- Las aceptadas con verificación baja («And there.», «Land.», por `voiceWakeTrust` o el detector muy seguro) parecen
  casi todas «Lander» de verdad mal transcrito: les sigue una orden. Esa vía hace falta.

### ¿Le habla o habla de él?

Decir la palabra hablando *del* programa («le estaba enseñando a mi hermano el Lander») no es un fallo del detector:
lo que falta es ver que no se le habla. 40 frases ya transcritas (20 y 20):

| Modelo | Acierto | Latencia |
|---|---|---|
| **Qwen 3.5 4B, un token (0,5)** | **100 %** | 61 ms |
| StartLux 4B (0,7) | 97,5 % | 31 ms |
| Bolt E4B, un token (0,7) | 97,5 % | 46 ms |
| pplx-decider-flash (0,7) | 92,5 % | 31 ms |
| Gemma 4 E4B, un token (0,7) | 87,5 % | 47 ms |
| d1-3B (0,7) | 82,5 % | 16 ms |
| d1-omni-600M, CPU | inservible (al revés) | 133 ms |

Aquí Bolt sí gana a Gemma, en línea con lo que promete NVIDIA de seguir mejor las instrucciones.

Sin LLM (la palabra de K-Desktop), una regla sencilla acertaría 38 de estas 40: le habla si la palabra va al principio
o tras una muletilla («oye», «vale», «pero», «eh»…). Falla con «KDesktop lo programé en C sharp».

## 6. Thinking

En los registros del 20/10 en adelante no hay abuso: 447 respuestas del modelo, mediana 26 tokens, p90 44; la IA pidió
pensar más 2 veces. Se piensa en cuatro sitios: frase fija («piénsalo»), corrección del usuario (`IsWrongAbout`),
cuenta de varios pasos con un modelo pequeño (`IsWordProblem`) y `think_harder`. Las 84 frases «Un momento.»,
«Ahora lo miro.» son el prefacio hablado (`ModuleVoice.Preface`), no razonamiento.

Riesgo: el llama-server de Bonsai avisa de que `--chat-template-kwargs {"enable_thinking": false}` está obsoleto
(«use --reasoning off»). Si una versión nueva lo ignora, los modelos que piensan por defecto (Bolt, Qwen 3.5…) lo
harían siempre sin que nadie lo vea.

## 7. Recomendación

1. **Palabra de activación, sin modelos nuevos** (lo que más se nota):
   - no enseñar «Escuchando…» ni bajar la música hasta que la fase 2 confirme; con Parakeet frío, mantenerlo cargado
     mientras haya palabras de activación (o verificar primero);
   - para los asistentes, comprobar «¿le habla?» con el cerebro ya cargado (un token, ~60 ms con Qwen 4B) antes de
     actuar; para K-Desktop, la regla de la posición.
2. **Comandos en la ruta del asistente**: decidir la zona dudosa con el **cerebro ya cargado, un token** (Qwen 4B:
   73,9 → 83,7 %, menos errores; Bonsai: 87,0 % con 4 errores), con la regla de apagar. Sin descargas ni VRAM, y
   mucho antes que una vuelta entera de la IA. Opción en Opciones → Cerebro, encendida por defecto.
3. **Modelo de decisión aparte**: no compensa ahora. El mejor (StartLux) es no comercial y necesita su paquete de
   Python delante; pplx-decider-flash (Apache, llama.cpp nativo con la atención no causal) da 87 %, pero ocupa 3 GB
   más que el cerebro de un token, que ya hace casi lo mismo.
4. **Voz de K-Desktop sin módulo (CPU)**: ningún modelo de decisión pequeño sirve; se queda EmbeddingGemma.
5. **Bolt**: no como sustituto del modelo pequeño (peor calibrado y menos acierto que Gemma 4 E4B); quizá para
   charla y seguir instrucciones, que es donde gana.
6. **Thinking**: pasar a `--reasoning off` cuando el llama-server lo admita, manteniendo la otra forma para los viejos.

## 8. Lo que no se probó

- La vuelta entera del asistente con la IA (qué hace la IA con lo que hoy se le pasa): habría que medirlo con
  `BenchRunner` dentro de K-Desktop.
- Un verificador de la palabra por audio (d1-omni acepta audio): solo hay 6 grabaciones por palabra y ningún audio de
  disparos falsos. Para medirlo, guardar unos días los clips de la fase 1 (con permiso).
- pplx-decider-flash en Q8 y StartLux en Q4.
- `conversational_mode` de Bolt.
