# Plan de prueba: Nemotron 3.5 Bolt E4B

Plan para decidir si NVIDIA Nemotron 3.5 Bolt E4B (publicado el 23 de octubre de 2026) sirve como modelo pequeño de
los asistentes, en lugar de Qwen3.5-4B o Gemma 4 E4B, para gráficas de 8 GB. No se trata de sustituir a Bonsai 27B.

## Por qué probarlo

- Está hecho para asistentes locales que hablan y usan herramientas: lo mismo que hace K-Desktop.
- Según NVIDIA (cifras sin verificar por nadie más), gana a Qwen3.5-4B en lo que más falla hoy: decidir **cuándo no**
  llamar a una herramienta o cuándo preguntar (When2Call-MT 63,4 frente a 37,8), no inventarse herramientas
  (RelyToolBench 63,2 frente a 43,3) y respetar el prompt de sistema (IHEval 84,8 frente a 60,6).
- Pierde en tareas largas de varios turnos (Tau2-Bench 55,1 frente a 75,1) y en contexto largo. Con órdenes cortas y
  16K de contexto debería notarse poco.
- Sale de Gemma 4 E4B (arquitectura `gemma4`) y trae projector de imagen y audio.

## Lo que K-Desktop ya hace bien con él

- `LlamaServerPool.cs` lo arranca con `--jinja` y `--chat-template-kwargs {"enable_thinking": false}`. Es
  importante, porque llama-server lo pone a pensar por defecto con este modelo.
- Las llamadas a herramientas usan el formato de Gemma 4 y llama-server las devuelve como `tool_calls`. No haría falta
  tocar `TextToolCalls`, salvo que se le escapen como texto (se comprueba en el paso 3).
- `--mmproj` se pasa cuando hay projector, así que la visión debería funcionar.

## Requisitos

| Qué | Valor |
|---|---|
| llama.cpp | Con soporte de `gemma4`: b11790 o posterior (bartowski cuantizó con b11845). |
| Gráfica | 8 GB para Q4_K_M. Con 6 GB, IQ4_XS (4,77 GB) o Q3_K_M (4,06 GB). |
| Bonsai | No cabe junto a Bolt en 12 GB: durante la prueba solo debe estar cargado uno. |

El nombre "4GB" engaña: el Q4_K_M de bartowski pesa 5,16 GB y el de NVIDIA 6,18 GB. Con 16K de contexto en q8 y el
projector, cuenta con 6–7 GB.

## Modelos a comparar

| Modelo | Descarga (en Cerebro) | Para qué |
|---|---|---|
| Nemotron 3.5 Bolt E4B | `bartowski/NVIDIA-Nemotron-3.5-Bolt-E4B-GGUF:Q4_K_M` | El candidato |
| Qwen3.5-4B | `unsloth/Qwen3.5-4B-GGUF:Q4_K_M` | El pequeño de ahora |
| Gemma 4 E4B | `unsloth/gemma-4-E4B-it-GGUF:Q4_K_M` | El modelo del que sale Bolt: dice cuánto aporta el reentreno de NVIDIA |
| Bonsai 27B | el que ya tienes | Referencia de lo que se puede conseguir |

Mismo cuantizado (Q4_K_M) para los tres pequeños, para que la comparación sea justa. Comprueba el nombre exacto de
los repositorios en Hugging Face antes de descargar.

## Pasos

### 1. Preparar

1. Actualiza llama-server a una versión con `gemma4` (ver Requisitos).
2. Descarga los tres modelos pequeños desde *Opciones → Cerebro*. Comprueba que Bolt baja también su `mmproj`.
3. Apunta la gráfica, la versión de llama-server y la de K-Desktop: van en los resultados.

### 2. Que arranca

1. Elige Bolt como modelo general y mira en el registro que carga sin errores, con `--mmproj`.
2. Mira la memoria de la gráfica con el modelo cargado y en reposo.
3. Pregunta algo sencillo («¿qué eres?») y comprueba que **no piensa**: la respuesta llega sin pausa y sin texto de
   razonamiento.

Si no carga, para aquí: casi seguro es la versión de llama-server.

### 3. Banco de pruebas incluido

*Opciones → Cerebro → Prueba el modelo*, con el banco en español (`bench.es.json`, 56 órdenes), una vez por modelo.
Antes, aparta tu `Assistant\bench.json` si lo tienes, porque si existe se usa ese en vez del incluido.

Apunta por modelo:

- el porcentaje de aciertos;
- el tiempo medio por orden;
- las órdenes falladas y qué hizo en cada una;
- si alguna llamada a herramienta aparece **como texto** en la respuesta (`call:…{…}` o `<tool_call>`). Si pasa,
  hay que enseñar ese formato a `TextToolCalls`.

Repítelo dos veces con Bolt: con una temperatura de 0,3–0,5 los resultados cambian algo de una vez a otra.

### 4. Banco de «no hacer nada» y «preguntar»

El banco incluido solo tiene 5 casos `noTool`, y esa es justo la ventaja que promete Bolt. Copia `bench.es.json` a
`Assistant\bench.json` y añade casos como estos:

```json
{ "say": "qué cansado estoy hoy", "expect": { "noTool": true } },
{ "say": "gracias, eso era todo", "expect": { "noTool": true } },
{ "say": "¿tú crees que debería cambiar de trabajo?", "expect": { "noTool": true } },
{ "say": "ayer apagué la pantalla y no volvió a encenderse", "expect": { "noTool": true } },
{ "say": "solo dime cuántos segundos son 25 minutos", "expect": { "answerContains": ["1500", "1.500"] } },
{ "say": "guarda una copia de todo", "expect": { "noTool": true } },
{ "say": "ponlo más alto", "expect": { "noTool": true } },
{ "say": "¿cuánto es el 15 % de 80?", "expect": { "tool": "calculate", "answerContains": "12" } },
{ "say": "¿cuántos días faltan para Navidad?", "expect": { "tool": "date_math" } },
{ "say": "mi hermana dice que mañana llueve, a ver si es verdad", "expect": { "tool": "weather" } }
```

Evita frases que caigan en las reglas fijas («pon un temporizador», «qué tiempo hace»): esas no llegan al modelo y no
dicen nada de él.

«Guarda una copia de todo» y «ponlo más alto» no dicen qué ni dónde: lo correcto es preguntar, no actuar. Añade
también 10–15 órdenes tuyas reales con tus comandos (`"command": "…"`), y algunas mal dichas o a medias, como las dirías
hablando.

Pasa este banco con los cuatro modelos y apunta lo mismo que en el paso 3, separando los aciertos de `noTool`.

### 5. Uso real con voz

Una tarde usando Bolt como modelo general, hablando:

- ¿Las respuestas son cortas y naturales para oírlas, o largas y con formato (listas, negritas)?
- ¿Entiende el español coloquial? NVIDIA solo lo reentrenó en inglés y el español viene de Gemma 4.
- ¿Cuánto tarda desde que acabas de hablar hasta que empieza a contestar?
- Con **mirar la pantalla** (`look_at_screen`): ¿describe bien lo que hay? ¿Repite texto en bucle, como hacía Bonsai?

### 6. Opcional: modo conversacional

El template tiene `conversational_mode`, que le pide respuestas breves y en lenguaje hablado. K-Desktop no lo envía.
Si en el paso 5 contesta largo, prueba a añadir `"conversational_mode": true` en `--chat-template-kwargs` de
`LlamaServerPool.cs` y repite los pasos 3–5. Comprueba que no empeora al usar herramientas: nadie lo ha medido
todavía.

## Cuándo vale la pena

Bolt pasa a ser el modelo pequeño sugerido si:

1. en el banco incluido acierta **al menos lo mismo** que Qwen3.5-4B (o como mucho 2 puntos menos);
2. en el banco de «no hacer nada» acierta **claramente más** que Qwen3.5-4B (+10 puntos o más);
3. ninguna llamada a herramienta sale como texto, o salen pocas y se pueden recoger en `TextToolCalls`;
4. cabe con el projector en 8 GB y deja al menos 1 GB libre (lo que guarda `SpareBytes`);
5. en el uso real, el español no se nota peor que con Gemma 4 E4B.

Si gana a Qwen pero no a Gemma 4 E4B, el reentreno no aporta nada en español: mejor sugerir Gemma, que es la base.

## Resultados

| Modelo | Incluido (%) | «No hacer nada» (%) | Tiempo medio | Herramientas como texto | Memoria gráfica | Notas |
|---|---|---|---|---|---|---|
| Bolt E4B (1.ª) | | | | | | |
| Bolt E4B (2.ª) | | | | | | |
| Qwen3.5-4B | | | | | | |
| Gemma 4 E4B | | | | | | |
| Bonsai 27B | | | | | | |

Equipo: · llama-server: · K-Desktop:

**Decisión:**

## Fuentes

- [NVIDIA Research: ficha y benchmarks](https://research.nvidia.com/labs/nemotron/Nemotron-3.5-Bolt-E4B-4GB/)
- [Modelo en Hugging Face](https://huggingface.co/nvidia/NVIDIA-Nemotron-3.5-Bolt-E4B-4GB)
- [GGUF de bartowski](https://huggingface.co/bartowski/NVIDIA-Nemotron-3.5-Bolt-E4B-GGUF)
- [GGUF de terelabs](https://huggingface.co/terelabs/NVIDIA-Nemotron-3.5-Bolt-E4B-GGUF) (llama-server y `--reasoning`)
- [Prueba independiente de Classmethod en DGX Spark](https://dev.classmethod.jp/en/articles/dgx-spark-nemotron-3-5-bolt-e4b-4gb-first-touch/)
- [OrcaRouter: qué se publicó realmente](https://www.orcarouter.ai/blog/nemotron-3-5-bolt-e4b-4gb-what-shipped)
