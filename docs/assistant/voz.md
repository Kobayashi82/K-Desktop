# Voz de los asistentes: eco y rapidez (etapa 5.7)

> **Desde 5.0.94 la voz natural es Kokoro**, no Pocket TTS (que es con lo que se midió esto). Kokoro no da el audio a
> trozos de una décima: hace cada frase (o cada trozo de una frase larga, el primero hasta la primera coma) de una vez, en
> unas 0,15 veces lo que dura. Medido en el PC del usuario: 8,6 s de audio en 1,35 s; la primera frase de «Probar» suena
> a los 0,35–0,4 s con el modelo cargado y a ~1,1 s en frío (carga del modelo y la voz, 0,7 s). Parakeet entiende las
> tres voces en español (ef_dora, em_alex, em_santa) casi sin fallos: los que cuenta son cifras («44» por «cuarenta y
> cuatro»). El modelo cuantizado (92 MB) era 4 veces más lento en este procesador: se usa el de 325 MB.

Medido el 21/10/2026 en el PC del usuario (RTX 4070 SUPER), con una instancia de prueba de K-Desktop (compilación
Debug, `--data` de prueba), Dot y el Lander 0.7.0 en el escritorio, Ollama con `qwen3-4b-instruct`, Pocket TTS en
español (voz «eve») y el micrófono simulado (`KDESKTOP_VOICE_FAKE_STREAM`) con órdenes dichas por la voz Helena de Windows.

## Su propia voz no lo dispara

Tres defensas, de la primera a la última:

1. **Cancelación de eco** (`EchoCanceller`, Speex MDF): la referencia es lo que suena por los altavoces (bucle del
   dispositivo predeterminado), así que la voz del asistente se quita del micrófono igual que la música.
2. **Mientras le hablas, baja la voz**: al llegar `listening`, el host deja su voz al 20 % (`ModuleVoice.ListeningGain`)
   hasta que acaba la escucha; además, el «Bajar el resto del sonido» de K-Desktop (Opciones › Voz) también lo atenúa.
3. **Guarda por contenido** (`Core/Assistant/SelfEcho`): K-Desktop recuerda lo que dicen los asistentes del escritorio
   (las líneas del chat y las pruebas de Opciones → Voz) y hasta cuándo pueden estar sonando. Lo oído tras una palabra que
   tenga 3 palabras o más y coincida en un 70 % y en orden con eso se ignora, tanto el tramo de la palabra («Hola, soy
   Lander») como la orden que la sigue. Las órdenes cortas («para», «calla») nunca se descartan.

Banco (`echobench` del scratchpad): la voz del Lander sintetizada («Hola, soy Lander. He revisado tu escritorio…»,
«Lander al habla: escritorio despejado…») pasa por una habitación simulada (40 ms de retardo, cola de reverberación con
RT60 0,35 s, −9 dB) y se mezcla con ruido.

| Caso | Sin cancelador | Con cancelador |
|---|---|---|
| Nivel del eco (s 4–12) | −13 … −34 dB | 15–27 dB menos (los 2 primeros segundos casi nada: está aprendiendo la habitación) |
| Lo que entiende Parakeet | «Hola, soy Lander, he revisado tu escritor y todo está en orden» | «Handel and I have to be like her» |
| Palabra «Lander» (fase 2) | sí | no |
| Guarda | **eco** (descartado) | — |
| Tú encima, a los 2 s («Lander, ¿qué hora es?») | tapado: «…hereditante de Todito de España» | «Landed. ¿Qué hora?» → palabra sí, orden «¿qué hora?» |

Con el recorrido real de K-Desktop (`KDESKTOP_VOICE_FAKE_RENDER` = lo que «suena»), y en el peor caso de un detector
entrenado con la propia voz del Lander diciendo «Lander»:

- **sin cancelador**, su eco dispara la palabra dos veces y se manda a sí mismo «he revisado tu escritorio y todo está en
  orden» (en el uso real la guarda lo pararía: esas frases están en su chat);
- **con cancelador**, salta una vez en el primer segundo (aún sin adaptar) y la verificación la descarta («I'm gonna»);
  la segunda frase ya no la dispara.

**Sin probar con el micrófono y los altavoces reales** (las pruebas no abren el micrófono): la habitación de verdad no
es lineal y el retardo cambia; la guarda y la voz baja cubren lo que se escape.

## «Para» mientras habla

«Lander… para» mientras contaba una historia de cuatro frases: la orden llega («Para.»), la regla `cancel` corta lo que
decía (una orden nueva sustituye a la anterior) y contesta «Maniobra abortada.».

## Tiempos (§4: comando < 1 s, la IA empieza a sonar < 3 s)

Desde que se acaba de hablar (final de la voz en el audio) hasta cada momento del registro:

| Orden | Antes (5.0.80) | Ahora (5.0.81) |
|---|---|---|
| Regla («¿qué hora es?»), primer audio | 1,84 s (Parakeet y Pocket en frío) | 1,15 s |
| Orden entregada al asistente | 1,05–1,22 s | 0,88–0,94 s |
| Primer comando de la sesión, encontrado | ~1,8 s (EmbeddingGemma cargando) | 1,05 s (primer audio 1,16 s) |
| Comando ya en caliente, encontrado | 1,05 s | 0,89 s (primer audio 0,98 s) |
| IA con el modelo fuera de memoria, primer audio | 3,45 s | 1,56 s |
| IA con el modelo cargado, primer audio | ~1,7 s | 1,54 s |

Lo que se cambió:

- **Transcribir en la pausa**: `WakeCapture` lleva un segundo detector de voz con 250 ms de silencio; en cada pausa se
  transcribe lo dicho hasta ahí y, si la orden acaba ahí (el detector de 800 ms cierra el mismo tramo, ±0,1 s), se usa
  sin volver a transcribir (`VoiceService.HandleWakeAsync`, «la orden ya estaba transcrita en la pausa»).
- **Preparar mientras hablas**: con `listening`, el host pide a Ollama que cargue el modelo (`TalkService.Prepare`, como
  mucho una vez por minuto; «modelo … listo mientras le hablan») y carga Pocket TTS; K-Desktop hace una búsqueda de
  comandos de calentamiento.
- **EmbeddingGemma se cargaba tarde**: con todos los nombres ya en la caché de vectores, `SemanticMatcher.Preload` no
  cargaba el modelo y lo hacía la primera búsqueda (también en los comandos de voz de K-Desktop). Ahora lo carga.

**Lo que falta para el «< 1 s»:** el suelo son los **0,8 s de silencio** que dan la orden por terminada. No se baja: con
menos se cortan órdenes con pausas (en la prueba, «¿Por qué el cielo es azul? Contesta corto.» ya se cortó en el «?»).
Los comandos quedan en 0,9–1,05 s, y el primero de la sesión en el límite. Medido en Debug; el portable (ReadyToRun) va
algo más rápido en lo que es código.
