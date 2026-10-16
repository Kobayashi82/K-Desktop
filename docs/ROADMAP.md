# Hoja de ruta

## Hecho (sesión 1)

- [x] Solución .NET 10: `KDesktop.Core` (lógica) + `KDesktop` (WPF + WPF-UI).
- [x] Modelo de datos en JSON con guardado atómico, debounce y copias de seguridad diarias.
- [x] Importador de K-Desktop 4: comandos, acciones, QuickMenus, opciones, sonidos e iconos.
- [x] Hooks de teclado y ratón en un hilo dedicado: acordes de ratón, rueda, bloqueo de teclas y supresión del menú Inicio/Alt; se reinstalan periódicamente.
- [x] Motor de comandos asíncrono y cancelable (Esc), con filtros por proceso y la lista "Desactivar en".
- [x] Lanzamiento elevado / no elevado sin KPS.exe (el propio ejecutable hace de auxiliar con el token del Explorador).
- [x] 16 tipos de acción.
- [x] QuickMenu acrílico en cascada: teclado, clic derecho que no cierra, clic central para editar.
- [x] OSD animado.
- [x] Servidor UDP compatible con K-Desktop 4.
- [x] Ventana principal: comandos (editor completo), opciones y acerca de.
- [x] Editor visual de QuickMenus:
  - lienzo en vivo con los submenús en columnas;
  - arrastrar y soltar (elementos, comandos, archivos, carpetas y enlaces);
  - menú contextual, renombrar en el sitio, iconos;
  - cortar, copiar, pegar y duplicar; deshacer y rehacer;
  - comando rápido.
- [x] "Elegir ventana" para capturar el proceso de una aplicación con un clic.
- [x] Tema claro, oscuro o del sistema; idiomas español e inglés en JSON con cambio en caliente.
- [x] Instancia única, línea de comandos (`--menu`, `--run`, `--show`, `--exit`), bandeja e inicio con Windows (tarea programada).

## Hecho (sesión 2)

- [x] **Logros y estadísticas** (empiezan de cero en la 5.0, sin importar los de la 4.0):
  - 112 logros en 10 categorías, con niveles, progreso y logros secretos; nombres clásicos de la 4.0 y nuevos (macros, acordes, QuickMenus anidados, noctámbulo…);
  - estadísticas en `stats.json`: contadores, ejecuciones por comando y por origen (atajo, QuickMenu, remoto…);
  - notificación animada (insignia, destello, barra de tiempo, sonido `Logro`), en cola, clic para abrir la página;
  - página propia con pestañas Logros / Estadísticas, filtros, búsqueda y reinicio.
- [x] **Macros**:
  - grabación de teclado y ratón con tiempos (barra flotante con cuenta atrás, pausa y detener; Mayús+Esc termina);
  - editor de pasos: insertar, editar, arrastrar, eliminar, deshacer y simplificar (unir pulsaciones, quitar movimientos, cambiar tiempos);
  - acción "Reproducir macro" con repeticiones, bucle, velocidad y pausa entre repeticiones;
  - Mayús+Esc la detiene en cualquier momento y suelta las teclas y botones que hubiera pulsado.
- [x] Reordenar acciones (y pasos de macro) arrastrando, con animación.
- [x] `--data "carpeta"` (instancia independiente con otros datos) y `--page nombre`.
- [x] Correcciones: menú de la bandeja traducido; las acciones ya no guardan `kind`/`summary` en config.json; textos fijos pasados a los idiomas.

## Hecho (sesión 3)

- [x] **Salida de audio**: acción que cambia el dispositivo de sonido predeterminado (también el de comunicaciones si se quiere); con varios elegidos, cada ejecución pasa al siguiente conectado, con mensaje en pantalla y sonido de prueba opcional. Lista ordenable arrastrando; sin dispositivos elegidos, rota por todos los conectados. Se importa de la 4.0.
- [x] **Energía → Cambiar de usuario** (también se importa así de la 4.0, antes se convertía en «Bloquear»).
- [x] **Selector de iconos** nuevo:
  - colección propia de K-Desktop 5: mosaicos vectoriales con degradado y cualquier símbolo de Windows 11 (más de 9.000, con buscador), 15 colores y estilo mosaico o símbolo;
  - iconos de las bibliotecas de Windows (imageres, shell32, ddores… o cualquier dll/exe) con su índice;
  - desde archivo, o volver al icono automático;
  - los iconos por defecto de las acciones y de las ubicaciones especiales usan la colección nueva; se han quitado los de la 4.0.
- [x] **Macros → paso «Desplazar ratón»** (relativo), exacto en píxeles o «en bruto» para juegos.
- [x] **¿Sabías que…?** en Acerca de: 134 trucos (Windows, Explorador, navegadores, texto y K-Desktop) en un carrusel animado con filtros, avance automático y logros.
- [x] **K-Board** rehecho desde cero:
  - perfiles con reasignaciones de teclas, botones del ratón y rueda → teclas o combinaciones, texto, nada o un comando;
  - capas con modificadores (Ctrl + J → ↓ suelta Ctrl mientras tanto) y comodín (Shift + Bloq Mayús usa la regla de Bloq Mayús);
  - se mantienen pulsadas como la original y se sueltan exactamente al soltarla, aunque se apague el perfil a medias;
  - por aplicaciones (o en todas, respetando «Desactivar en»), activables a mano, desde un comando (acción K-Board), desde la bandeja;
  - editor visual: teclado ISO español completo con bloque numérico y ratón dibujados, capas, captura de teclas y lista de reasignaciones;
  - estadísticas y logros propios.

## Pendiente

- [ ] Probar el K-Board en el uso diario (sobre todo juegos y combinaciones con modificadores).
- [ ] Probar la salida de audio con varios dispositivos reales.
- [ ] Probar a fondo en el uso diario: hotkeys, bloqueo, acordes, elevación y macros.
- [x] Publicación: perfil `SelfContained` (self-contained + single file + ReadyToRun, sin .NET instalado).
      `dotnet publish src\KDesktop\KDesktop.csproj -c Release -p:PublishProfile=SelfContained` → `artifacts\portable\KDesktop.exe`.
      Cualquier compilación Release lo genera también (target `BuildPortable`, con bin/obj propios en
      `artifacts\build`; `-p:BuildPortable=false` lo omite). Idiomas es/en y sonidos van incrustados en
      KDesktop.Core.dll; `%APPDATA%\K-Desktop 5\Languages` y `\Sounds` se fusionan encima (gana la carpeta).
- [ ] Firmar el ejecutable con un certificado Authenticode (OV/EV). Es el remedio real contra los
      falsos positivos de antivirus: sin firma, SmartScreen y las heurísticas marcan cualquier .exe
      nuevo y sin reputación. Mitigaciones ya aplicadas mientras no haya certificado: metadatos de
      versión/empresa completos, single file SIN compresión (no parece un "packer"), ReadyToRun (sin
      compilación en runtime) y sin recorte de IL. Si aun así algún motor da falso positivo, enviar la
      muestra a su portal (Microsoft: aka.ms/wdsi) para que lo incluyan en la lista blanca.
- [ ] Instalador (Inno Setup) o zip portable a partir de `artifacts\portable`.
      Tamaño (medido 2026-10-15): exe actual 162 MB → zip 69 MB. Comprimido dentro del exe 74 MB, pero
      arranca ~200 ms más lento y usa ~75 MB más de RAM todo el rato (descomprime en memoria). Sin
      ReadyToRun 142 MB. Decisión: exe sin comprimir y distribuir en zip.
- [x] Favicons para las acciones web: se descargan de la propia web (link rel=icon, manifest de app web, /favicon.ico; SVG con SharpVectors), uno por host+puerto, en `Favicons` de la carpeta de datos. Opción en Apariencia (y botón para volver a descargarlos).
- [x] **Lanzador** (acción «Lanzador»): caja de búsqueda centrada sobre los comandos, los programas del menú Inicio
      (shell:AppsFolder, también apps de la Store) y la web, con calculadora. Búsqueda difusa (`FuzzyMatch`: inicio,
      palabras, iniciales, letras sueltas; sin acentos) y orden por uso (ejecuciones de comandos y `launcher.json`).
- [x] **Web/PWA de control remoto** (Opciones › «App web (móvil)», desactivada por defecto, puerto TCP 2502):
      servidor HttpListener/http.sys en `KDesktop.Core/Remote/Web` (0 MB extra; escucha en `http://+:puerto/`, que
      necesita administrador; sin él, solo localhost). HTTPS con Caddy delante: rutas relativas, ruta base opcional,
      `X-Forwarded-*` solo desde la IP del proxy, opción «solo a través del proxy» y regla de firewall propia para el
      puerto. Emparejado con QR o código de un solo uso (5 min) → token de 256 bits (se guarda su hash en
      `web-devices.json`); dispositivos revocables en Opciones. Desde un navegador del propio PC (localhost directo)
      no hace falta emparejar. La web (`KDesktop.Core/Web`, incrustada como los idiomas; textos `pwa.*`) tiene
      Menús (QuickMenus con submenús), Todos con buscador difuso, Audio (volumen, silencio, multimedia, salida) y
      Ajustes (tema, idioma); resultado de cada comando (OSD y avisos/errores del registro vía `ExecutionFeedback`),
      eventos en vivo (SSE) y service worker para abrir sin conexión. Casilla «En la app web» por comando y
      `TriggerSource.Web`. El exe creció ~1,1 MB (QRCoder + web).
      Caddy (en 192.168.1.150): `kdesktop.dominio { reverse_proxy <IP del PC>:2502 }` y en Opciones poner
      «IP del proxy» = 192.168.1.150 (y, si se quiere, «Solo a través del proxy») y la «Dirección pública».
- [x] **Touchpad y teclado** en la app web (pestaña «Touchpad»; Opciones › App web › «Touchpad y teclado», activado
      por defecto). WebSocket `api/input` (`WebServer.Input.cs`) con mensajes JSON pequeños; se autentica con la cookie
      o con el token en el primer mensaje (5 s), y solo lo abren las páginas de la propia app (se comprueba `Origin`:
      las cookies viajan también a WebSockets abiertos desde otro puerto del mismo servidor). Lo que el móvil deja
      pulsado (botones, modificadores fijados) se suelta al desconectarse. El puntero se mueve con SendInput absoluto
      (`InputSimulator.MovePointer`): evento real (hover, arrastrar) pero sin la aceleración de Windows encima de la de
      la app. Gestos: un dedo mueve (aceleración propia), tocar = clic (espera 210 ms por si es tocar-y-arrastrar),
      tocar dos veces = doble clic, mantener = arrastrar, dos dedos = desplazamiento (suave, con inercia; natural o
      no), toque con dos/tres dedos = clic derecho/central; botones izquierdo/central/derecho debajo. Teclado: el del
      propio móvil escribe en un cuadro oculto y se reenvía la diferencia (funcionan predicción, autocorrección y
      escritura deslizando); fila de teclas especiales (Esc, Tab, flechas con repetición, Inicio/Fin, F1–F12…) y
      modificadores: un toque = solo para la siguiente tecla, dos toques = fijado (Alt+Tab). Velocidades, desplazamiento
      natural y «tocar para hacer clic» en Ajustes de la app.
- [x] **Pausa** (icono de encendido abajo a la izquierda, menú de la bandeja o aviso de arriba; se guarda en la
      configuración): no se ejecuta nada (atajos, QuickMenus, K-Board, bloqueo de Atrás/Adelante, UDP, web, comandos
      de inicio) salvo lo que se lanza a mano desde el editor; los atajos de mostrar la ventana y salir siguen. Estado
      abajo a la izquierda como iconos (pausa, UDP, app web y voz, esta aún sin función) con tooltip, color y clic a
      sus opciones; los atajos desactivados salen como aviso arriba.
- [x] **Copia completa en .zip** (Opciones › Datos): configuración, logros y estadísticas, iconos, sonidos e idiomas
      propios (`DataArchive`). Fuera quedan registros, copias automáticas, favicons, modelos de voz y los dispositivos
      emparejados (sus claves son de este PC). Importar acepta .zip o el config.json antiguo, guarda antes la copia
      actual en `Backups\antes de importar <fecha>.zip` y reconstruye las páginas (antes seguían con la configuración vieja).
- [x] **Lista de comandos**: tres líneas (nombre; lo que hace la primera acción con «+N» si hay más; primer atajo con
      «+N», solo si tiene), los «+N» con tooltip de todas/todos; menú del botón derecho (ejecutar, duplicar, cambiar nombre, habilitar, ocultar, eliminar), Supr y
      F2. El nombre no puede quedar vacío (vuelve el anterior). Elegir un proceso del desplegable lo añade al momento.
      «Añadir acción» con scroll; X en el lanzador y en el aviso de logro.
- [x] **Tema claro legible** (2026-10-16): los grises de texto de WPF-UI se oscurecen en claro (`ThemeService`,
      en los recursos de la aplicación porque WPF-UI escribe ahí algunas claves), texto oscuro en los botones dorados
      y los iconos propios casi blancos y sin color (también favicons y carpetas con icono propio) se invierten en el
      tema claro (`IconProvider.Theme.cs`; la web pide la variante de su propio tema: `icon` / `iconLight`).
- [x] **Icono nuevo** (squircle dorado con una K y una chispa), generado en vectores por `tools\icon\make-icon.ps1`
      (ico 16…256, PNG de la web y capas para Android). Opciones › Apariencia › «Icono de la bandeja» (cualquier icono,
      y volver al de K-Desktop; `TrayIcons`). La versión corta (5.0) sale del ensamblado (`AppPaths.ShortVersion`);
      Acerca de muestra la completa (5.0.0).
- [x] **Actualizaciones automáticas** (Opciones › Actualizaciones; `Core/Updates/UpdateService`): manifiesto JSON
      (`version`, `date`, `url` relativa o absoluta, `sha256`, `size`, `notes` por idioma) en una dirección
      configurable (por defecto `https://kobayashi82.net/kdesktop/latest.json`). Modos: no buscar / avisar /
      instalarla sola (por defecto). Comprueba al minuto de arrancar y cada 6 h. Descarga a `Updates`, verifica SHA-256
      y que el exe sea esa versión de K-Desktop, renombra el exe en uso a `*.old`, copia el nuevo y lo arranca con
      `--after-update <pid> <versión>`: espera a que salga el viejo, borra el `.old` y avisa «Actualizado a…» (sin
      sonido ni comandos de inicio; los de salida tampoco corren). En modo automático espera a que no se use (ventana
      cerrada, sin macros, cuentas atrás, voz ni QuickMenus). Solo el portable de un único exe se actualiza solo; los
      demás abren la descarga. Publicar: subir `<Version>` en Directory.Build.props, publicar el portable y
      `tools\release\make-update.ps1 -NotesEs "…" -NotesEn "…"` → `artifacts\update\` (exe + latest.json) al VPS.
      Probado en local de punta a punta (5.0.0 → 5.0.1 con un servidor en 127.0.0.1).
- [ ] Web: micrófono del móvil como otra fuente de audio de la voz (después de la voz).
      APK solo si hace falta algo que la web no puede (widgets, ajustes rápidos).
- [ ] Probar la app web en el móvil real a través de Caddy (instalación como PWA, iPhone y Android).
- [ ] **Voz** (plan en [VOZ.md](VOZ.md), etapas abajo). Va después del touchpad/teclado de la web.

### Plan de la voz

Plan completo en [VOZ.md](VOZ.md) (2026-10-15): atajo de mantener pulsado **y** palabra de activación propia grabada
por el usuario (fase 1: preprocesado de openWakeWord + DTW + clasificador entrenado en el PC; fase 2: verificación con
Parakeet y clave fonética), Parakeet TDT 0.6B v3 con sherpa-onnx, Silero VAD, `IntentMatcher` por capas (FuzzyMatch →
embeddings multilingües → LLM opcional) y siete etapas con su commit cada una.

- [x] Etapa 1: captura + VAD + Parakeet + atajo de mantener pulsado + OSD + IntentMatcher capa 1.
      `KDesktop.Core/Voice`: `MicCapture` (WASAPI compartido → mono 16 kHz, solo abierto durante una orden),
      `VoiceActivity` (Silero VAD, fin tras 0,8 s de silencio), `SpeechToText` (Parakeet int8 con sherpa-onnx 1.13.8,
      carga perezosa, se descarga de memoria tras `VoiceUnloadMinutes`), `IntentMatcher` (cobertura del nombre con
      raíces «abre/abrir», verbos opcionales, sinónimos es↔en y penalización de palabras sobrantes) y `VoiceService`.
      Atajo propio en Opciones › Voz (`VoiceHotkey`, integrado en `HotkeyRouter` como los de abrir/salir; no es una
      acción de comando): tecla o botón del ratón mantenido = escucha mientras se mantiene (`IsKeyHeld`), un toque o la
      rueda = hasta el silencio; pulsarlo otra vez la termina antes y Esc la cancela. OSD con
      medidor de nivel; origen «voz» en estadísticas; icono de estado. Por voz se pueden ejecutar todos los comandos,
      también los de «Energía» (decisión del usuario, 2026-10-16). Modelos en `Models` de la carpeta de datos (`VoiceModels`).
      Diagnóstico sin micro: `KDesktop.exe --voice-test a.wav carpeta "frase"… [--no-open]` → `Logs\voice-test.txt`.
      Medido: carga del modelo ~1,4 s; cada orden ~90 ms ya cargado. Sin probar con micrófono real.
- [x] Etapa 2: descarga de modelos y página de Opciones.
      `ModelDownloader`: archivos sueltos (Silero desde las releases de sherpa-onnx; Parakeet desde
      huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8, así no hace falta descomprimir .tar.bz2),
      640 MB, reanudable con `.part` + Range, comprobación de tamaño y SHA-256, en segundo plano. Opciones › Voz:
      estado/descargar/cancelar/borrar con progreso, micrófono (predeterminado o uno concreto, que se recuerda aunque
      esté desconectado), liberar la memoria tras N min, cómo usarla y botón «Crear el comando «Orden de voz»». El icono
      de estado de la voz abre esa sección. Probado: descarga, cancelación, reanudación y borrado.
- [x] Etapa 3: clave fonética + fase 2 + asistente de grabación.
      `PhoneticKey` (reglas de VOZ.md; además y final → i, ge/gi → j, x → ks, ph → f y una letra suelta se lee por su
      nombre: «K desktop» → «kadesktop»). `WakeWordVerifier`: busca en lo transcrito el tramo de palabras (hasta las de
      la variante más larga + 2) que más se parece por sonido a alguna variante (Levenshtein normalizado), devuelve lo
      dicho después como orden y calibra el umbral: por encima de lo que alcanza el habla normal (+0,1) y por debajo de
      lo que alcanzan las grabaciones entre sí (dejando una fuera). Sensibilidad por palabra (±0,15 al umbral).
      Varias palabras de activación en Opciones › Voz (lista con sensibilidad, activar, probar, volver a grabar,
      borrar), cada una en `Voice\WakeWords\<id>\` (wakeword.json + word_N.wav, silence.wav, speech.wav para la etapa 4;
      entran en la copia .zip). Asistente `WakeWordDialog`: nombre → silencio 3 s (ruido de fondo) → 6 tomas seguidas
      (mínimo 4; recortadas con el VAD y descartadas si son cortas, largas, bajas, saturadas o de duración atípica; se
      muestra lo que entendió Parakeet) → 10 s de habla normal (calibración) → prueba en vivo con puntuación y orden.
      Mientras no hay escucha continua, una orden por atajo que empieza por la palabra («KDesktop, abre Firefox») la
      quita. Diagnóstico sin micro: `--wake-test "KDesktop" carpeta` (word*/speech*/test*.wav) → `Logs\wake-test.txt`;
      para probar el asistente sin micro, `KDESKTOP_VOICE_FAKE=<carpeta de wavs>` hace que cada grabación lea el
      siguiente .wav. Probado con voz sintética (Helena): «K Desktop, abre Firefox» → sí (1,00) y orden «Abre Firefox»;
      «Cadktop sube el volumen» → sí (0,78); habla normal ≤ 0,50 → no. Sin probar con micrófono real.
      Pendiente para la etapa 4: el descarte de tomas «atípicas» por DTW (ahora solo por duración).
- [x] Etapa 4: fase 1 (port del preprocesado de openWakeWord, DTW, clasificador).
      `OwwFeatures`/`OwwStream`: port de `AudioFeatures` (openwakeword/utils.py, comprobado contra el código fuente):
      audio como valores de 16 bits → `melspectrogram.onnx` → x/10 + 2 → ventanas de 76 frames cada 8 →
      `embedding_model.onnx` → 96 valores cada 80 ms; en streaming, cada bloque de 1280 muestras con las 480 anteriores
      da 8 frames y el búfer mel empieza a unos, como el original. Modelos de la release v0.5.1 de openWakeWord (2,3 MB)
      en `Models\openwakeword`, en la misma descarga de Opciones. ONNX Runtime: solo `Microsoft.ML.OnnxRuntime.Managed`
      1.28.0, que usa el `onnxruntime.dll` 1.28.2 que ya trae sherpa-onnx (un solo runtime nativo).
      `WakeDetector` por palabra (`detector.json`): plantillas de las tomas comparadas por DTW (subsecuencia que acaba en
      el último embedding, distancia coseno) escaladas entre «tomas entre sí» y «habla normal», y regresión logística
      sobre los últimos 16 embeddings entrenada en el PC (~3,5 s, en segundo plano) con las tomas + variaciones
      (velocidad ±10 %, volumen, ruido de la habitación) frente al silencio, el habla normal, ruido y la palabra a medias.
      Puntuación = media geométrica de ambas; umbral entre lo que alcanza el habla normal y lo que alcanzan las tomas,
      movido por la sensibilidad. `WakeWordListener`: micro abierto mientras haya alguna palabra activa con detector y
      «Escucha continua» (por defecto sí; se para con la pausa), trabajo en su propio hilo, 2 s de audio previo; al
      detectar sigue grabando hasta que te callas (o 0,9 s si no dices nada más), precarga Parakeet y pasa a la fase 2
      (`VoiceService.HandleWakeAsync`): si Parakeet confirma la palabra, lo dicho después es la orden; si no hay orden,
      se abre la escucha normal. Opción «Fiarse del detector cuando está muy seguro» (salta la fase 2). Los «casi» se
      apuntan en el registro para ajustar la sensibilidad. El asistente ahora descarta tomas atípicas por DTW, entrena el
      detector mientras pruebas y enseña las dos puntuaciones. Estadística `voice.wakewords`.
      Probado sin micrófono: embeddings en streaming frente a clip entero, similitud coseno media 0,983 (mín. 0,96);
      con las grabaciones reales del usuario (5 para entrenar, la 6.ª de prueba) → 0,97 y verificación 0,89; frases sin
      la palabra → 0,00; de extremo a extremo con audio simulado (`KDESKTOP_VOICE_FAKE_STREAM`): 10 s de habla sin falsas
      alarmas y «Cadextop. Prueba mensaje» → ejecuta «Prueba mensaje». Escuchando gasta ~3 % de un núcleo.
      No hecho: valores de referencia de Python (no hay Python en el PC; la comprobación fue streaming frente a clip
      entero y con audio real) y el paquete opcional de negativos de openWakeWord (por ahora no hizo falta).
      Sin probar con micrófono real.
      Ajustes tras probarlo con el micro (2026-10-16): tras la detección se sigue grabando sin cortes (`WakeCapture`); la
      fase 2 transcribe la palabra sola, recortada como en el asistente (con el audio entero salían «Connect», «Alex»…), y
      con el detector muy seguro basta una verificación 0,15 por debajo; si la verificación pasa aparece «Escuchando…» y
      espera hasta 5 s a la orden (decir la palabra, pausa y orden ya funciona); la orden se elige entre lo dicho tras la
      palabra, todo junto sin la palabra o lo que quedó en el recorte, la que mejor casa. Varias órdenes en una frase
      («abre YouTube y vacía la papelera»): si la frase entera no es un comando, se parte por «y», «luego», comas… y se
      ejecuta cada parte (`IntentMatcher.Plan`). Las palabras de la voz (muletillas, verbos, «hazlo», enlaces y
      sinónimos por concepto) están ahora en los archivos de idioma (`voice.words.*`, `voice.syn.*`) y cuentan todos los
      idiomas instalados a la vez (`VoiceVocabulary`); comparación por sonido entre palabras y dos palabras dichas por
      una («va hacia» → vacía).
- [ ] Etapa 5: capa de embeddings.
- [ ] Etapa 6: seguridad, confirmaciones y estadísticas.
- [ ] Etapa 7: capa LLM opcional.

**Primer arranque:** si «Iniciar con Windows» está activado (por defecto lo está) y es una copia con
carpeta de datos normal, se registra la tarea del Programador de tareas automáticamente la primera vez,
sin tener que abrir Ajustes.

## Ideas

- K-Link (crear enlaces duros / uniones de carpeta) y Zona Wi-Fi móvil (sustituta de la Red Ad-Hoc): sin decidir.
- Captura de pantalla, grabación de pantalla y de audio: descartadas por ahora.
