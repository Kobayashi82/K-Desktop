# Plan de la voz

Plan acordado el 2026-10-15. Sustituye al resumen anterior de `ROADMAP.md` (que usaba el *keyword spotting* de
sherpa-onnx para la palabra clave y un LLM como segunda capa). Se trabaja por etapas; cada etapa se marca en
`ROADMAP.md` al terminarla.

## Objetivo

Controlar K-Desktop por voz, 100 % local, sin servicios en la nube:

1. Activación por atajo (mantener pulsado mientras hablas) **y** por palabra de activación personalizada que el
   usuario graba desde la propia app (p. ej. «KDesktop», que no es palabra española: por eso **no** se puede depender
   de que el reconocedor la escriba bien; se aprende de la voz del usuario).
2. Voz a texto con **Parakeet TDT 0.6B v3** (multilingüe, español incluido) mediante sherpa-onnx.
3. Elegir el comando a ejecutar con probabilidad/confianza (estilo «Jev», pero local), y ejecutarlo.

## Decisiones ya tomadas

- Librerías: NuGet `org.k2fsa.sherpa.onnx` (API C#; comprobar la última versión estable, la última apuntada era
  1.13.8) para Parakeet y para el detector de voz Silero VAD; `Microsoft.ML.OnnxRuntime` para los modelos ONNX propios
  (palabra de activación y embeddings). Captura de micro con NAudio.Wasapi (ya referenciado): WasapiCapture → mono
  16 kHz float.
- Los modelos **nunca** van dentro del exe: se descargan desde Opciones a una carpeta `Models` dentro de la carpeta de
  datos (AppPaths), con progreso, comprobación de tamaño/hash y botón para borrar. Sin modelos, la voz aparece
  desactivada con un aviso.
- Whisper descartado. Porcupine descartado (no es open source).
- Todo el procesado en hilos de fondo; la UI nunca se bloquea. Consumo en reposo mínimo.

## Arquitectura (`KDesktop.Core/Voice/…`)

- `MicCapture`: dispositivo elegible en Opciones (o el predeterminado), reconexión si cambia el dispositivo, búfer
  circular de los últimos ~3 s.
- `VoiceActivity` (Silero VAD de sherpa-onnx): corta los segmentos de voz.
- `SpeechToText` (Parakeet, reconocedor offline de sherpa-onnx): transcribe un segmento. Carga perezosa del modelo y
  descarga tras X minutos sin uso (configurable).
- `WakeWordDetector` (fase 1, ver abajo).
- `WakeWordVerifier` (fase 2, ver abajo).
- `IntentMatcher`: texto → comando + confianza (ver abajo).
- `VoiceService`: orquesta los estados: Reposo → (atajo o palabra) → Escuchando → Transcribiendo → Decidiendo →
  Ejecutar / Confirmar / «No te he entendido». Eventos para la UI.

## Palabra de activación personalizada

### Fase 1: detector acústico por ejemplos (siempre activo, ligero)

- Usar los modelos de preprocesado de openWakeWord (Apache 2.0): `melspectrogram.onnx` y `embedding_model.onnx`.
  Portar a C# exactamente su pipeline, **verificándolo con el código fuente de openWakeWord** (utils.py /
  AudioFeatures). Lo que se recuerda: audio 16 kHz en bloques de 1280 muestras (80 ms), mel → transformación
  (x/10 + 2), ventanas de 76 frames mel con paso 8 → embedding de 96 dimensiones cada 80 ms.
- Grabación (enrollment): con las grabaciones del usuario se guardan sus secuencias de embeddings como plantillas. En
  tiempo real se compara una ventana deslizante con las plantillas por DTW (distancia coseno) y se normaliza a una
  puntuación de 0 a 1.
- Además, entrenar en el propio PC (segundos, en C#, sin PyTorch) un clasificador pequeño (regresión logística o MLP
  de una capa oculta) sobre los embeddings:
  - Positivos: las grabaciones + aumentos (ruido, ganancia, velocidad ±10 %, desplazamiento).
  - Negativos: el silencio/ruido grabado, el habla normal grabada en la prueba y, opcionalmente, un paquete
    descargable de características negativas (openWakeWord publica las suyas; comprobar el tamaño y la licencia y
    usar un subconjunto razonable).
- Puntuación final = combinación de DTW + clasificador, con un umbral ajustable (control de sensibilidad). Además,
  tiempo de espera tras una detección para no disparar dos veces.

### Fase 2: verificación con Parakeet (solo cuando dispara la fase 1)

- Transcribir los últimos ~2 s del búfer y compararlos con las variantes que Parakeet produjo durante la grabación de
  la palabra.
- La comparación es por sonido: una «clave fonética» española (minúsculas, sin tildes; c/k/qu → k; z/ce/ci → s;
  v → b; h muda fuera; ll/y → y; quitar espacios y guiones; letras repetidas → una) y distancia de Levenshtein
  normalizada con un umbral.
- Si la fase 1 tiene muchísima confianza, se puede saltar la fase 2 (configurable). Si la fase 2 falla, se ignora en
  silencio (registrarlo en el Log para depurar).
- Lo que se diga después de la palabra en la misma frase («KDesktop, abre Firefox») se usa directamente como orden; si
  no se dice nada, se pasa a Escuchando.

### Pantalla de grabación (Opciones → Voz → Palabra de activación → «Configurar…»)

Asistente en un diálogo con estilo WPF-UI, textos en es/en:

1. «Silencio» 3 s → mide el nivel de ruido (y guarda negativos).
2. «Di la palabra» × 6 (mínimo 4), con medidor de nivel. Cada grabación se recorta con el VAD y se descarta si es
   demasiado corta o larga, está saturada, es muy baja o es atípica respecto a las demás por DTW («Repítela»). Se
   muestra lo que entendió Parakeet cada vez. (Etapa 4: lo atípico se mide por duración y por DTW sobre los
   embeddings.)
3. «Habla normal sin decirla» 10 s → negativos y calibración automática del umbral.
4. Prueba en vivo: «Dila otra vez» → ✓ detectada. Se muestra la puntuación.
5. Guardar en `Voice/WakeWord/` de la carpeta de datos: wavs, plantillas, pesos del clasificador, variantes de texto,
   umbral, fecha y dispositivo. Botones: Volver a grabar, Probar, Borrar, sensibilidad. Permitir varias personas
   (perfiles de voz) si no complica demasiado.

## Del texto al comando (`IntentMatcher`, por capas)

1. Coincidencia directa con los nombres (y alias de voz) de los comandos reutilizando `FuzzyMatch`. Si hay un claro
   ganador, ejecutar.
2. Clasificador por embeddings: modelo de embeddings multilingüe pequeño en ONNX (p. ej. multilingual-e5-small;
   tokenizador con Microsoft.ML.Tokenizers/SentencePiece). Para cada comando «disponible por voz» se precalculan los
   embeddings de su nombre, alias y frases de ejemplo (caché en disco, invalidada cuando cambian los comandos).
   Similitud coseno → softmax con temperatura → probabilidad. Umbral alto → ejecutar; umbral medio → pedir
   confirmación; bajo → no entendido.
   **Hecho (2026-10-16), tras medirlo con los comandos reales:** EmbeddingGemma 300M int8, no e5-small. Con los
   nombres cortos y mezclados de los comandos, e5-small y Granite R2 97M lo ven todo parecido (~0,85) y aciertan
   26–29 de 50; EmbeddingGemma con el prompt «task: sentence similarity» acierta 44. En vez de softmax: el más
   parecido gana si pasa 0,62 (0,80 para las órdenes integradas) y saca 0,02 al siguiente, y compiten frases de
   «esto no es mío» (`voice.none`: luces, tele, alarmas…) para que «apaga la tele» no sea "Apagar Equipo". El umbral
   medio es el «¿Querías decir…?» de siempre; lo de Energía encontrado solo por significado no se ejecuta (etapa 6:
   confirmar por voz).
3. (Opcional, al final y detrás de una opción) LLM local pequeño (Qwen3 0.6B/1.7B GGUF con LLamaSharp) con la salida
   forzada por gramática a los ids de los comandos. La probabilidad sale de los logprobs, renormalizados solo entre las
   opciones válidas (los logprobs de llama.cpp son anteriores a la máscara de la gramática). Ids de un solo token si es
   posible. Sin «thinking». Solo se usa si las capas 1 y 2 no están seguras.

Antes de dar por buena cada capa, hacer una pequeña prueba de aciertos/latencia con frases de ejemplo sobre los
comandos reales (un test o un modo de diagnóstico).

## Seguridad y UI

- Por comando: casilla «Disponible por voz» + campo «Alias de voz» (frases alternativas) en el editor de comandos.
  Casilla «Pedir confirmación por voz» (activada por defecto en Energía, cerrar procesos y similares).
- OSD (reutilizar OsdWindow): «Escuchando…» con nivel de micro → texto entendido → comando que se va a ejecutar (o la
  pregunta «¿Ejecutar X? Di sí/no», que se responde por voz, o Enter/Esc).
- Sonidos opcionales al empezar y terminar de escuchar (sistema de sonidos existente).
- Icono o estado en la bandeja: voz activa/en pausa.
- **Decisión del usuario (2026-10-16):** la voz no es una acción de comando. Se activa con su propio atajo en
  Opciones › Voz (como los de abrir/salir: mantener pulsado para hablar, o un toque) y con las palabras de activación
  (puede haber varias, cada una con su grabación). Lo que se dice después elige y ejecuta el comando.
- Estadísticas: ejecuciones con origen «voz». Si encaja, algunos logros de voz.
- Opciones → página/sección «Voz»: activar, micrófono, atajo, palabra de activación (asistente), sensibilidad, modelos
  (descargar/estado/borrar), confirmaciones, descargar los modelos de la memoria tras X min, capa LLM sí/no.

## Etapas

Cada una se compila y se sube con su commit, y al terminarla se actualiza `ROADMAP.md`.

1. Captura + VAD + Parakeet + atajo de mantener pulsado + OSD + IntentMatcher capa 1.
2. Descarga de modelos y página de Opciones.
3. Clave fonética + fase 2 + asistente de grabación.
4. Fase 1 (port del preprocesado de openWakeWord, DTW, clasificador).
5. Capa de embeddings.
6. Seguridad/confirmaciones/estadísticas.
7. Capa LLM opcional.

## Pruebas

- Las partes puras (clave fonética, Levenshtein, DTW, mel/embeddings comparando con valores de referencia de
  openWakeWord en Python, clasificador, softmax/umbral) deben tener tests o un modo de diagnóstico
  `--voice-test archivo.wav` que imprima puntuaciones (el usuario prueba con su micro en Windows).
- Indicar claramente qué no se ha podido probar sin micrófono.
- Si alguna decisión de aquí resulta inviable (licencias, tamaño, rendimiento), **parar y preguntar** antes de
  cambiarla.
