# Hoja de ruta

## Hecho (sesiÃ³n 1)

- [x] SoluciÃ³n .NET 10: `KDesktop.Core` (lÃ³gica) + `KDesktop` (WPF + WPF-UI).
- [x] Modelo de datos en JSON con guardado atÃ³mico, debounce y copias de seguridad diarias.
- [x] Importador de K-Desktop 4: comandos, acciones, QuickMenus, opciones, sonidos e iconos.
- [x] Hooks de teclado y ratÃ³n en un hilo dedicado: acordes de ratÃ³n, rueda, bloqueo de teclas y supresiÃ³n del menÃº Inicio/Alt; se reinstalan periÃ³dicamente.
- [x] Motor de comandos asÃ­ncrono y cancelable (Esc), con filtros por proceso y la lista "Desactivar en".
- [x] Lanzamiento elevado / no elevado sin KPS.exe (el propio ejecutable hace de auxiliar con el token del Explorador).
- [x] 16 tipos de acciÃ³n.
- [x] QuickMenu acrÃ­lico en cascada: teclado, clic derecho que no cierra, clic central para editar.
- [x] OSD animado.
- [x] Servidor UDP compatible con K-Desktop 4.
- [x] Ventana principal: comandos (editor completo), opciones y acerca de.
- [x] Editor visual de QuickMenus:
  - lienzo en vivo con los submenÃºs en columnas;
  - arrastrar y soltar (elementos, comandos, archivos, carpetas y enlaces);
  - menÃº contextual, renombrar en el sitio, iconos;
  - cortar, copiar, pegar y duplicar; deshacer y rehacer;
  - comando rÃ¡pido.
- [x] "Elegir ventana" para capturar el proceso de una aplicaciÃ³n con un clic.
- [x] Tema claro, oscuro o del sistema; idiomas espaÃ±ol e inglÃ©s en JSON con cambio en caliente.
- [x] Instancia Ãºnica, lÃ­nea de comandos (`--menu`, `--run`, `--show`, `--exit`), bandeja e inicio con Windows (tarea programada).

## Hecho (sesiÃ³n 2)

- [x] **Logros y estadÃ­sticas** (empiezan de cero en la 5.0, sin importar los de la 4.0):
  - 112 logros en 10 categorÃ­as, con niveles, progreso y logros secretos; nombres clÃ¡sicos de la 4.0 y nuevos (macros, acordes, QuickMenus anidados, noctÃ¡mbuloâ€¦);
  - estadÃ­sticas en `stats.json`: contadores, ejecuciones por comando y por origen (atajo, QuickMenu, remotoâ€¦);
  - notificaciÃ³n animada (insignia, destello, barra de tiempo, sonido `Logro`), en cola, clic para abrir la pÃ¡gina;
  - pÃ¡gina propia con pestaÃ±as Logros / EstadÃ­sticas, filtros, bÃºsqueda y reinicio.
- [x] **Macros**:
  - grabaciÃ³n de teclado y ratÃ³n con tiempos (barra flotante con cuenta atrÃ¡s, pausa y detener; MayÃºs+Esc termina);
  - editor de pasos: insertar, editar, arrastrar, eliminar, deshacer y simplificar (unir pulsaciones, quitar movimientos, cambiar tiempos);
  - acciÃ³n "Reproducir macro" con repeticiones, bucle, velocidad y pausa entre repeticiones;
  - MayÃºs+Esc la detiene en cualquier momento y suelta las teclas y botones que hubiera pulsado.
- [x] Reordenar acciones (y pasos de macro) arrastrando, con animaciÃ³n.
- [x] `--data "carpeta"` (instancia independiente con otros datos) y `--page nombre`.
- [x] Correcciones: menÃº de la bandeja traducido; las acciones ya no guardan `kind`/`summary` en config.json; textos fijos pasados a los idiomas.

## Hecho (sesiÃ³n 3)

- [x] **Salida de audio**: acciÃ³n que cambia el dispositivo de sonido predeterminado (tambiÃ©n el de comunicaciones si se quiere); con varios elegidos, cada ejecuciÃ³n pasa al siguiente conectado, con mensaje en pantalla y sonido de prueba opcional. Lista ordenable arrastrando; sin dispositivos elegidos, rota por todos los conectados. Se importa de la 4.0.
- [x] **EnergÃ­a â†’ Cambiar de usuario** (tambiÃ©n se importa asÃ­ de la 4.0, antes se convertÃ­a en Â«BloquearÂ»).
- [x] **Selector de iconos** nuevo:
  - colecciÃ³n propia de K-Desktop 5: mosaicos vectoriales con degradado y cualquier sÃ­mbolo de Windows 11 (mÃ¡s de 9.000, con buscador), 15 colores y estilo mosaico o sÃ­mbolo;
  - iconos de las bibliotecas de Windows (imageres, shell32, ddoresâ€¦ o cualquier dll/exe) con su Ã­ndice;
  - desde archivo, o volver al icono automÃ¡tico;
  - los iconos por defecto de las acciones y de las ubicaciones especiales usan la colecciÃ³n nueva; se han quitado los de la 4.0.
- [x] **Macros â†’ paso Â«Desplazar ratÃ³nÂ»** (relativo), exacto en pÃ­xeles o Â«en brutoÂ» para juegos.
- [x] **Â¿SabÃ­as queâ€¦?** en Acerca de: 134 trucos (Windows, Explorador, navegadores, texto y K-Desktop) en un carrusel animado con filtros, avance automÃ¡tico y logros.
- [x] **K-Board** rehecho desde cero:
  - perfiles con reasignaciones de teclas, botones del ratÃ³n y rueda â†’ teclas o combinaciones, texto, nada o un comando;
  - capas con modificadores (Ctrl + J â†’ â†“ suelta Ctrl mientras tanto) y comodÃ­n (Shift + Bloq MayÃºs usa la regla de Bloq MayÃºs);
  - se mantienen pulsadas como la original y se sueltan exactamente al soltarla, aunque se apague el perfil a medias;
  - por aplicaciones (o en todas, respetando Â«Desactivar enÂ»), activables a mano, desde un comando (acciÃ³n K-Board), desde la bandeja;
  - editor visual: teclado ISO espaÃ±ol completo con bloque numÃ©rico y ratÃ³n dibujados, capas, captura de teclas y lista de reasignaciones;
  - estadÃ­sticas y logros propios.

## Pendiente

- [ ] Probar el K-Board en el uso diario (sobre todo juegos y combinaciones con modificadores).
- [ ] Probar la salida de audio con varios dispositivos reales.
- [ ] Probar a fondo en el uso diario: hotkeys, bloqueo, acordes, elevaciÃ³n y macros.
- [x] PublicaciÃ³n: perfil `SelfContained` (self-contained + single file + ReadyToRun, sin .NET instalado).
      `dotnet publish src\KDesktop\KDesktop.csproj -c Release -p:PublishProfile=SelfContained` â†’ `artifacts\portable\KDesktop.exe`.
      Cualquier compilaciÃ³n Release lo genera tambiÃ©n (target `BuildPortable`, con bin/obj propios en
      `artifacts\build`; `-p:BuildPortable=false` lo omite). Idiomas es/en y sonidos van incrustados en
      KDesktop.Core.dll; `%APPDATA%\K-Desktop 5\Languages` y `\Sounds` se fusionan encima (gana la carpeta).
- [ ] Firmar el ejecutable con un certificado Authenticode (OV/EV). Es el remedio real contra los
      falsos positivos de antivirus: sin firma, SmartScreen y las heurÃ­sticas marcan cualquier .exe
      nuevo y sin reputaciÃ³n. Mitigaciones ya aplicadas mientras no haya certificado: metadatos de
      versiÃ³n/empresa completos, single file SIN compresiÃ³n (no parece un "packer"), ReadyToRun (sin
      compilaciÃ³n en runtime) y sin recorte de IL. Si aun asÃ­ algÃºn motor da falso positivo, enviar la
      muestra a su portal (Microsoft: aka.ms/wdsi) para que lo incluyan en la lista blanca.
- [ ] Instalador (Inno Setup) o zip portable a partir de `artifacts\portable`.
      TamaÃ±o (medido 2026-10-15): exe actual 162 MB â†’ zip 69 MB. Comprimido dentro del exe 74 MB, pero
      arranca ~200 ms mÃ¡s lento y usa ~75 MB mÃ¡s de RAM todo el rato (descomprime en memoria). Sin
      ReadyToRun 142 MB. DecisiÃ³n: exe sin comprimir y distribuir en zip.
- [x] Favicons para las acciones web: se descargan de la propia web (link rel=icon, manifest de app web, /favicon.ico; SVG con SharpVectors), uno por host+puerto, en `Favicons` de la carpeta de datos. OpciÃ³n en Apariencia (y botÃ³n para volver a descargarlos).
- [x] **Lanzador** (acciÃ³n Â«LanzadorÂ»): caja de bÃºsqueda centrada sobre los comandos, los programas del menÃº Inicio
      (shell:AppsFolder, tambiÃ©n apps de la Store) y la web, con calculadora. BÃºsqueda difusa (`FuzzyMatch`: inicio,
      palabras, iniciales, letras sueltas; sin acentos) y orden por uso (ejecuciones de comandos y `launcher.json`).
- [x] **Web/PWA de control remoto** (Opciones â€º Â«App web (mÃ³vil)Â», desactivada por defecto, puerto TCP 2502):
      servidor HttpListener/http.sys en `KDesktop.Core/Remote/Web` (0 MB extra; escucha en `http://+:puerto/`, que
      necesita administrador; sin Ã©l, solo localhost). HTTPS con Caddy delante: rutas relativas, ruta base opcional,
      `X-Forwarded-*` solo desde la IP del proxy, opciÃ³n Â«solo a travÃ©s del proxyÂ» y regla de firewall propia para el
      puerto. Emparejado con QR o cÃ³digo de un solo uso (5 min) â†’ token de 256 bits (se guarda su hash en
      `web-devices.json`); dispositivos revocables en Opciones. Desde un navegador del propio PC (localhost directo)
      no hace falta emparejar. La web (`KDesktop.Core/Web`, incrustada como los idiomas; textos `pwa.*`) tiene
      MenÃºs (QuickMenus con submenÃºs), Todos con buscador difuso, Audio (volumen, silencio, multimedia, salida) y
      Ajustes (tema, idioma); resultado de cada comando (OSD y avisos/errores del registro vÃ­a `ExecutionFeedback`),
      eventos en vivo (SSE) y service worker para abrir sin conexiÃ³n. Casilla Â«En la app webÂ» por comando y
      `TriggerSource.Web`. El exe creciÃ³ ~1,1 MB (QRCoder + web).
      Caddy (en 192.168.1.150): `kdesktop.dominio { reverse_proxy <IP del PC>:2502 }` y en Opciones poner
      Â«IP del proxyÂ» = 192.168.1.150 (y, si se quiere, Â«Solo a travÃ©s del proxyÂ») y la Â«DirecciÃ³n pÃºblicaÂ».
- [x] **Touchpad y teclado** en la app web (pestaÃ±a Â«TouchpadÂ»; Opciones â€º App web â€º Â«Touchpad y tecladoÂ», activado
      por defecto). WebSocket `api/input` (`WebServer.Input.cs`) con mensajes JSON pequeÃ±os; se autentica con la cookie
      o con el token en el primer mensaje (5 s), y solo lo abren las pÃ¡ginas de la propia app (se comprueba `Origin`:
      las cookies viajan tambiÃ©n a WebSockets abiertos desde otro puerto del mismo servidor). Lo que el mÃ³vil deja
      pulsado (botones, modificadores fijados) se suelta al desconectarse. El puntero se mueve con SendInput absoluto
      (`InputSimulator.MovePointer`): evento real (hover, arrastrar) pero sin la aceleraciÃ³n de Windows encima de la de
      la app. Gestos: un dedo mueve (aceleraciÃ³n propia), tocar = clic (espera 210 ms por si es tocar-y-arrastrar),
      tocar dos veces = doble clic, mantener = arrastrar, dos dedos = desplazamiento (suave, con inercia; natural o
      no), toque con dos/tres dedos = clic derecho/central; botones izquierdo/central/derecho debajo. Teclado: el del
      propio mÃ³vil escribe en un cuadro oculto y se reenvÃ­a la diferencia (funcionan predicciÃ³n, autocorrecciÃ³n y
      escritura deslizando); fila de teclas especiales (Esc, Tab, flechas con repeticiÃ³n, Inicio/Fin, F1â€“F12â€¦) y
      modificadores: un toque = solo para la siguiente tecla, dos toques = fijado (Alt+Tab). Velocidades, desplazamiento
      natural y Â«tocar para hacer clicÂ» en Ajustes de la app.
- [x] **Pausa** (icono de encendido abajo a la izquierda, menÃº de la bandeja o aviso de arriba; se guarda en la
      configuraciÃ³n): no se ejecuta nada (atajos, QuickMenus, K-Board, bloqueo de AtrÃ¡s/Adelante, UDP, web, comandos
      de inicio) salvo lo que se lanza a mano desde el editor; los atajos de mostrar la ventana y salir siguen. Estado
      abajo a la izquierda como iconos (pausa, UDP, app web y voz, esta aÃºn sin funciÃ³n) con tooltip, color y clic a
      sus opciones; los atajos desactivados salen como aviso arriba.
- [x] **Copia completa en .zip** (Opciones â€º Datos): configuraciÃ³n, logros y estadÃ­sticas, iconos, sonidos e idiomas
      propios (`DataArchive`). Fuera quedan registros, copias automÃ¡ticas, favicons, modelos de voz y los dispositivos
      emparejados (sus claves son de este PC). Importar acepta .zip o el config.json antiguo, guarda antes la copia
      actual en `Backups\antes de importar <fecha>.zip` y reconstruye las pÃ¡ginas (antes seguÃ­an con la configuraciÃ³n vieja).
- [x] **Lista de comandos**: segunda lÃ­nea con atajos Â· lo que hace la primera acciÃ³n y Â«+NÂ» si hay mÃ¡s (tooltip con
      todas); menÃº del botÃ³n derecho (ejecutar, duplicar, cambiar nombre, habilitar, ocultar, web, UDP, eliminar), Supr y
      F2. El nombre no puede quedar vacÃ­o (vuelve el anterior). Elegir un proceso del desplegable lo aÃ±ade al momento.
      Â«AÃ±adir acciÃ³nÂ» con scroll; X en el lanzador y en el aviso de logro.
- [ ] Web: micrÃ³fono del mÃ³vil como otra fuente de audio de la voz (despuÃ©s de la voz).
      APK solo si hace falta algo que la web no puede (widgets, ajustes rÃ¡pidos).
- [ ] Probar la app web en el mÃ³vil real a travÃ©s de Caddy (instalaciÃ³n como PWA, iPhone y Android).
- [ ] **Voz** (plan en [VOZ.md](VOZ.md), etapas abajo). Va despuÃ©s del touchpad/teclado de la web.

### Plan de la voz

Plan completo en [VOZ.md](VOZ.md) (2026-10-15): atajo de mantener pulsado **y** palabra de activaciÃ³n propia grabada
por el usuario (fase 1: preprocesado de openWakeWord + DTW + clasificador entrenado en el PC; fase 2: verificaciÃ³n con
Parakeet y clave fonÃ©tica), Parakeet TDT 0.6B v3 con sherpa-onnx, Silero VAD, `IntentMatcher` por capas (FuzzyMatch â†’
embeddings multilingÃ¼es â†’ LLM opcional) y siete etapas con su commit cada una.

- [x] Etapa 1: captura + VAD + Parakeet + atajo de mantener pulsado + OSD + IntentMatcher capa 1.
      `KDesktop.Core/Voice`: `MicCapture` (WASAPI compartido â†’ mono 16 kHz, solo abierto durante una orden),
      `VoiceActivity` (Silero VAD, fin tras 0,8 s de silencio), `SpeechToText` (Parakeet int8 con sherpa-onnx 1.13.8,
      carga perezosa, se descarga de memoria tras `VoiceUnloadMinutes`), `IntentMatcher` (cobertura del nombre con
      raÃ­ces Â«abre/abrirÂ», verbos opcionales, sinÃ³nimos esâ†”en y penalizaciÃ³n de palabras sobrantes) y `VoiceService`.
      Atajo propio en Opciones â€º Voz (`VoiceHotkey`, integrado en `HotkeyRouter` como los de abrir/salir; no es una
      acciÃ³n de comando): tecla o botÃ³n del ratÃ³n mantenido = escucha mientras se mantiene (`IsKeyHeld`), un toque o la
      rueda = hasta el silencio; pulsarlo otra vez la termina antes y Esc la cancela. OSD con
      medidor de nivel; origen Â«vozÂ» en estadÃ­sticas; icono de estado. Los comandos con Â«EnergÃ­aÂ» no se ejecutan por voz
      hasta tener confirmaciones (etapa 6). Modelos en `Models` de la carpeta de datos (`VoiceModels`).
      DiagnÃ³stico sin micro: `KDesktop.exe --voice-test a.wav carpeta "frase"â€¦ [--no-open]` â†’ `Logs\voice-test.txt`.
      Medido: carga del modelo ~1,4 s; cada orden ~90 ms ya cargado. Sin probar con micrÃ³fono real.
- [x] Etapa 2: descarga de modelos y pÃ¡gina de Opciones.
      `ModelDownloader`: archivos sueltos (Silero desde las releases de sherpa-onnx; Parakeet desde
      huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8, asÃ­ no hace falta descomprimir .tar.bz2),
      640 MB, reanudable con `.part` + Range, comprobaciÃ³n de tamaÃ±o y SHA-256, en segundo plano. Opciones â€º Voz:
      estado/descargar/cancelar/borrar con progreso, micrÃ³fono (predeterminado o uno concreto, que se recuerda aunque
      estÃ© desconectado), liberar la memoria tras N min, cÃ³mo usarla y botÃ³n Â«Crear el comando Â«Orden de vozÂ»Â». El icono
      de estado de la voz abre esa secciÃ³n. Probado: descarga, cancelaciÃ³n, reanudaciÃ³n y borrado.
- [ ] Etapa 3: clave fonÃ©tica + fase 2 + asistente de grabaciÃ³n.
- [ ] Etapa 4: fase 1 (port del preprocesado de openWakeWord, DTW, clasificador).
- [ ] Etapa 5: capa de embeddings.
- [ ] Etapa 6: seguridad, confirmaciones y estadÃ­sticas.
- [ ] Etapa 7: capa LLM opcional.

**Primer arranque:** si Â«Iniciar con WindowsÂ» estÃ¡ activado (por defecto lo estÃ¡) y es una copia con
carpeta de datos normal, se registra la tarea del Programador de tareas automÃ¡ticamente la primera vez,
sin tener que abrir Ajustes.

## Ideas

- K-Link (crear enlaces duros / uniones de carpeta) y Zona Wi-Fi mÃ³vil (sustituta de la Red Ad-Hoc): sin decidir.
- Captura de pantalla, grabaciÃ³n de pantalla y de audio: descartadas por ahora.
