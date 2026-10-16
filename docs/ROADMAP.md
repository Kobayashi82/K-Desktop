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
- [x] **Icono nuevo** (squircle dorado con una K), generado en vectores por `tools\icon\make-icon.ps1`
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
      demás abren la descarga. Publicar: subir `<Version>` en Directory.Build.props y publicar el portable: el propio
      publish deja en `artifacts\update\` el exe, la APK y latest.json (con `android`: versión, código, url, sha256),
      listos para subir al VPS. Notas: `tools\release\make-update.ps1 -NotesEs "…" -NotesEn "…"` (se conservan
      mientras no cambie la versión).
      Probado en local de punta a punta (5.0.0 → 5.0.1 con un servidor en 127.0.0.1).
- [x] **App web rediseñada** (2026-10-16): luz ambiental y cristal, título grande que se compacta, píldora de
      conexión en vivo, halo del color de cada icono (se calcula en el navegador), submenús como carpetas con vista
      previa, migas de pan, «Más usados» y secciones por letra, dial de volumen arrastrable, Ajustes en lista agrupada.
- [x] **App de Android** (`android\`, `tools\android\build-apk.ps1` → `artifacts\android\KDesktop.apk`, ~125 KB):
      la web en su propia ventana (WebView, sin AndroidX ni Gradle: aapt2 + javac + d8 + apksigner; minSdk 21 (Android 5.0),
      target 34). La primera vez pide la dirección (proxy HTTPS o `IP:2502`; vale pegar el enlace de emparejar) y la
      recuerda; «Cambiar de servidor» en Ajustes de la web (`window.KDesktopApp`); si no conecta vuelve a esa pantalla
      con el error y «Reintentar». Atrás cierra submenús; los enlaces externos van al navegador; acepta «Compartir»
      un enlace de emparejar. Versión = la de K-Desktop (5.0.0 → código 50000). Firma: `%USERPROFILE%\.android\
      kdesktop-release.jks` (+ `.password.txt`), creada la primera vez: guardarla, las actualizaciones necesitan la
      misma. JDK 17 portable en `%LOCALAPPDATA%\Programs\jdk-17` y SDK en `%LOCALAPPDATA%\Android\Sdk`.
      **Se actualiza desde el PC** (2026-10-16): una compilación Release de KDesktop.Core crea la APK (si cambió
      `android\` y están el JDK y el SDK) y la lleva dentro (`Remote/Web/AndroidApp`); la sesión dice su versión y la
      web, dentro de una app más vieja, avisa una vez y muestra «Actualizar la app» en Ajustes → la app la baja de
      `api/android` con su token y la instala con PackageInstaller (la primera vez pide antes «instalar apps de este
      origen» y sigue sola al volver). Probado en el emulador 5.0.0 → 5.0.1. Las apps anteriores a esto no saben
      actualizarse: hay que instalar esta a mano una vez.
- [x] **Idiomas**: francés, alemán, italiano y portugués (Brasil) incluidos, también los «¿Sabías que…?», las
      palabras de la voz (`voice.words.*`, `voice.syn.*`) y la web. Sin idioma elegido se usa el de Windows si existe.
- [ ] Web: micrófono del móvil como otra fuente de audio de la voz (después de la voz).
- [x] **Emparejar una vez, cualquier dirección** (2026-10-16): K-Desktop dice sus direcciones en `api/session`
      (`WebServer.Addresses()`: la pública configurada o vista por el proxy, y las de la red local salvo con «solo el
      proxy»). La app de Android guarda el token y las direcciones (`Addresses.java`) y al abrirse, al fallar la
      carga o al volver tras 20 s en segundo plano prueba todas (`api/ping`, comprobando el nombre del PC) y usa la
      local si responde o si no la primera que conteste: en casa y fuera con el mismo emparejamiento. En el navegador
      (cada dirección es otro sitio para él) Ajustes muestra «Usar también en …»: un código de enlace de un solo uso
      (`api/link`, `#link=`) empareja esa dirección como el mismo dispositivo (`WebDevice.LinkedTokenHashes`). Con
      solo la cabecera Bearer `api/session` pone la cookie (iconos y eventos la necesitan). Desemparejar desde
      cualquier dirección quita el dispositivo entero. Probado con dos proxies en el emulador (app y Chrome).
- [ ] Probar la app web en el móvil real a través de Caddy (instalación como PWA, iPhone y Android).
- [ ] **Voz** (plan en [VOZ.md](VOZ.md), etapas abajo). Va después del touchpad/teclado de la web.
- [x] **Atajos: lo específico gana** (2026-10-16): si un atajo lo tienen un comando para todas las aplicaciones y otro
      limitado a algunas, en esas aplicaciones solo corre el específico (`HotkeyIndex.Resolve`, para teclas, botones,
      acordes y rueda); dos generales o dos del mismo programa siguen corriendo los dos. En el K-Board, el perfil de la
      aplicación en primer plano gana al general aunque vaya después. El aviso de atajo repetido lo explica.
      Pruebas en `tests/KDesktop.Core.Tests` (xUnit, `dotnet test tests\KDesktop.Core.Tests`).
- [x] **Acción «Petición web»** (2026-10-16, `HttpRequestAction`, `Engine/HttpRequests.cs`): GET/POST/PUT/PATCH/DELETE
      sin abrir el navegador, con cabeceras, cuerpo (JSON por defecto), límite de tiempo (10 s), «mostrar la respuesta»
      (nunca / solo si falla / siempre, por el OSD y `ExecutionFeedback`, así llega a la web) y «aceptar cualquier
      certificado». Un `HttpClient` compartido; Esc la cancela. Cabeceras **secretas**: casilla por cabecera, se
      guardan cifradas con DPAPI para el usuario de Windows (`Platform/Secrets.cs`); en otro PC no se pueden leer, se
      conservan tal cual y el editor pide volver a escribirlas. Variables {fecha}/{date}, {hora}/{time} y
      {portapapeles}/{clipboard} en URL y cuerpo (`TextVariables`, escapadas para URL o JSON; servirá también para
      «Enviar texto»). Botón «Probar» en el editor con el código y la respuesta. Sin probar con un Home Assistant real.
- [x] **Eventos** (2026-10-16, etapas 1–3; página `EventsPage`, `--page events`, `--event "Nombre"`): reglas
      «cuando pase X, ejecuta estos comandos» (`Model/EventRule.cs`, `Config.Events`, por id de comando, en orden).
      Tipos: al iniciar / al salir de K-Desktop, a una hora (días, «si se perdió, ejecutarlo al arrancar»), cada N
      minutos, al abrir un programa (fin: se cierra el último), en primer plano (fin: deja de estarlo; 300 ms de
      margen), sin usar el PC N minutos (fin: al volver; la entrada inyectada por macros, K-Board o el touchpad web
      cuenta como uso), bloquear / desbloquear / suspender / reanudar. «Al terminar», «Solo si» (franja que puede
      cruzar medianoche, días, programa abierto / no abierto), «no repetir antes de» (2 s por defecto en programas y
      sesión) y limitador de bucles (más de 10 disparos en un minuto → se detiene 10 min con aviso).
      **«Ejecutar al iniciar / al salir» salen de los comandos**: `AppConfig.Upgrade` (esquema 3) los convierte en dos
      eventos con esos comandos en el orden de la lista (también los deshabilitados) y guarda antes
      `Backups\config.schema2.<fecha>.json`; el importador de K-Desktop 4 crea esos eventos directamente.
      `RunStartupCommandsAsync` (en serie, `TriggerSource.Startup`) y `RunExitCommandsAsync` (a la vez, 5 s,
      `TriggerSource.Exit`) leen ahora los eventos; tras `--after-update` no corren, la pausa los bloquea.
      Motor `Engine/Events/EventService.cs`: solo enciende los detectores que algún evento activo necesita.
      Hora/intervalo: un temporizador que se revisa cada 30 s como mucho (nota el PC dormido y los cambios de reloj;
      una hora vista con más de 2 min de retraso cuenta como perdida); `EventSchedule` en UTC (cambio de hora: la hora
      que no existe salta al cambio, la repetida una vez). Programas: instantánea Toolhelp cada 2 s (~1 ms, sin
      administrador; se descartó WMI `Win32_ProcessStartTrace` por fiabilidad y porque su nombre puede venir cortado).
      Primer plano y sesión: ventana oculta propia (`Platform/SystemWatcher.cs`: `SetWinEventHook`,
      `WTSRegisterSessionNotification`, `WM_POWERBROADCAST`; al suspender espera hasta 2 s a los comandos).
      Inactividad: `GetLastInputInfo` cada 5 s (1 s mientras se está fuera). Estado (última vez, hasta cuándo se
      estuvo mirando) en `events-state.json`, no en config.json. Al salir de K-Desktop no corren los «Al terminar».
      «Desactivar en» no afecta a los eventos (es para que los atajos no molesten en una aplicación).
      Integración: `TriggerSource.Event` (estadísticas «Eventos»), «Lo usan: evento…/QuickMenu…» en el editor de
      comandos con enlace, acción Comandos sobre un evento (ejecutar / activar / desactivar / alternar), submenú en la
      bandeja, logros (categoría Eventos: crear 1/10, disparos 10/100/1000, primer «Al terminar»; los migrados no
      cuentan como creados). El buscador de «Añadir comando» usa `FuzzyMatch`.
      Probado de verdad: al iniciar (migrado), abrir/cerrar el Bloc de notas, a una hora, primer plano (con la ventana
      de una instancia de prueba). **Sin probar**: bloquear/desbloquear, suspender/reanudar, inactividad real.
- [x] **Web, pestaña Audio** (2026-10-16): cruceta de flechas con Espacio en el centro para el reproductor que esté
      delante (avanzar/retroceder, su propio volumen, pausar); mantener una flecha la repite. `POST api/audio {key}`,
      solo con «Touchpad y teclado» permitido (si no, la cruceta no sale). La casilla «Aplicación web» de los comandos
      desaparece: todos los comandos habilitados están en la web (la propiedad `web` de config.json se ignora).
- [x] **Eventos, fase 2** (2026-10-16, un commit por tipo): batería o corriente (`RegisterPowerSettingNotification`),
      archivo nuevo en una carpeta (`FileSystemWatcher` + `FileSettler`: espera a que deje de crecer y se pueda leer;
      una vez por archivo), unidad conectada por letra o etiqueta con «Al terminar» al retirarla (`WM_DEVICECHANGE`),
      red por nombre o cualquiera / sin red (`NetworkChange` + nombres del Network List Manager, 3 s de espera) y
      dispositivo de audio conectado o predeterminado (`AudioWatcher`: `IMMNotificationClient` propio, NAudio 3 lo
      tiene interno; 700 ms de espera). Probados: archivo (de extremo a extremo), lectura de redes, registro de avisos
      de audio. **Sin probar de verdad**: batería, USB, cambio de red, conectar unos cascos.

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
- [x] **Órdenes multimedia integradas** (Opciones › Multimedia, `VoiceMedia`, activadas por defecto): pausar,
      reanudar, siguiente, anterior, subir/bajar el volumen (paso configurable; «un poco» = la mitad, «mucho» = el
      doble, o un número dicho), volumen a N («al 40 %», «al máximo», «a la mitad»), silenciar, quitar el silencio,
      «pon música» (reanudar, un comando o abrir un programa —si ya está abierto solo reanuda—) y «quita la música»
      (pausar, un comando o cerrar un proceso). Sin crear comandos (`MediaOrders`, `VoiceService.Media.cs`). Pausar
      y reanudar miran antes si algo suena (picos de las sesiones de audio, `MediaPlayback`): «pausa» no arranca la
      música ni «continúa» la para. Frases por orden en los idiomas (`voice.media.<Orden>`, «|»), números en palabras
      (`voice.numbers`, compuestos como «treinta y cinco», «fünfunddreißig», «trentacinque», «quatre-vingt-dix»),
      todos los idiomas a la vez y sin sinónimos entre conceptos («pon» no vale por «riprendi»). Compiten con los
      comandos en `IntentMatcher`: un comando del usuario a 0,06 o menos gana (su propio «Siguiente»). Se encadenan
      con los comandos («pausa y abre la papelera»). Probado con `--voice-test` en los seis idiomas; sin probar con el
      micro.
      Después (2026-10-16): «pon música» con un programa lo abre si está cerrado y lo pone a sonar (play/pause a su
      ventana con WM_APPCOMMAND; si no responde y no suena nada, la tecla multimedia), o lo reanuda si está en pausa;
      «cierra la música» cierra el reproductor y «para», «detén», «quita la música», «apaga la música» solo pausan.
- [x] **Voz con música o películas sonando** (2026-10-16). Con música el micro oía los altavoces y fallaban las tres
      fases (detector, verificación y el VAD que decide cuándo acaba la orden). Ahora:
      · **Cancelación de eco** (Opciones › Voz › «Ignorar lo que suena en el PC», `VoiceEchoCancel`, activada):
      `PlaybackReference` captura en loopback la salida predeterminada (`WasapiRecorder`, sigue al dispositivo
      predeterminado) y `MicCapture` también usa `WasapiRecorder`; ambos dan la marca QPC de cada paquete
      (el loopback, la hora en que sonará: unos 40 ms en el futuro), así cada trama de 10 ms del micro se empareja
      con lo que sonaba entonces (`AudioClock`, `StreamClock`, `EchoStage`, la referencia 30 ms adelantada).
      El cancelador es el filtro adaptativo MDF de **SpeexDSP** (paquete `SpeexDSPSharp.Natives`, filtro de 256 ms)
      **solo lineal**: se probó primero el AEC3 de WebRTC y su supresor (pensado para llamadas) se comía el principio de
      la voz —justo la palabra de activación— y, con música tonal, la voz entera. `EchoDelay` (GCC-PHAT cada 2 s)
      mide si el eco llega más tarde de lo que dice Windows (Bluetooth, TV por HDMI) y recoloca la referencia si el eco
      cae fuera de 10–100 ms del filtro. Pruebas (mezclas de una toma real de «KDesktop» que el detector no vio + orden
      con TTS, sobre música sintética o una «película» en inglés, sala simulada con reverberación y algo de
      distorsión): sin cancelación el detector no pasa de 0,47 (umbral 0,52) con la música 6 dB por encima de la voz;
      con ella detecta 0,85–0,99 en los 12 casos y ejecuta la orden de punta a punta (oyente real con
      `KDESKTOP_VOICE_FAKE_STREAM` + `KDESKTOP_VOICE_FAKE_RENDER`), también con +180 ms de retardo; sin falsos positivos
      con solo música/película. Falla la película 12 dB por encima de la voz. `--aec-test mic.wav altavoz.wav`.
      · **Fase 2**: si el recorte por VAD no convence se prueba el último tramo sin recortar; y si el detector está
      muy seguro pero la palabra sola no se entiende (con música de fondo Parakeet la toma por inglés: «Can't take it
      up»), se acepta provisionalmente y se confirma transcribiéndola junto con la orden.
      · **Bajar el volumen mientras hablas** (`VoiceDuck`, al 20 %; `AudioDucker`): al detectar la palabra (fase 1) o al
      pulsar la tecla de voz se baja el volumen de cada programa que suena (mezclador, no el maestro) y se recupera
      con una rampa al tener la orden (antes de ejecutarla); si el usuario lo mueve entretanto, se respeta.
      Sin probar con el micro real.
- [x] **Pausa tras la palabra, «cancela», vídeos** (2026-10-16, 5.0.8). El detector salta antes de que acabe la
      palabra: su última sílaba (+ 800 ms de silencio) cerraba la orden y «KDesktop… (pausa) orden» acababa en «No he
      oído nada». Ahora cada tramo que termina se transcribe y, si no es una orden, se sigue escuchando 3 s más
      (`WakeCapture.Snapshot`/`SegmentCount`); lo dicho tras la pausa también se prueba solo. «Cancela» / «cancela la
      cuenta atrás» (`MediaOrderKind.Cancel`, siempre activa) hace lo de Escape; los comandos que se ejecutan por voz ya
      no bloquean la escucha (antes una cuenta atrás lanzada por voz impedía oír la siguiente orden). Pausar, seguir,
      siguiente y anterior valen para «vídeo», «película», «serie», «capítulo»… (`voice.media.things`). El número solo
      cuenta para el volumen («Siete la música» ponía música: «siete» se quitaba como número). «Cierra la música» con
      el reproductor en la bandeja (5.0.7): orden de salida de AIMP / Winamp.
- [x] **Etapa 5: capa de significado** (2026-10-16, 5.0.9). Si las palabras no nombran un comando claro, se mira qué
      quiere decir la orden: «limpia la papelera» → Vaciar Papelera, «monta la bóveda» → Vault Mount, «cierra kokoro»
      → Kokoro - Close, «vide la corbeille», «haz que suene más bajo», «déjalo, no lo hagas» (cancela)… sin
      sinónimos por comando.
      · **Modelo**: EmbeddingGemma 300M (Google, Gemma Terms of Use), int8 ONNX de onnx-community (309 MB, se descarga
      con los demás; opcional: sin él solo cuentan las palabras y la palabra de activación sigue funcionando).
      `TextEmbedder`: tokenizador SentencePiece (Microsoft.ML.Tokenizers 2.0.1, `<bos>`…`<eos>`), salida
      `sentence_embedding`, ~65 ms por orden en CPU; se carga mientras hablas y se descarga tras los minutos sin uso.
      Comprobado contra la referencia (transformers.js): mismos tokens y coseno 1,00000 en 8 textos (acentos, emoji,
      japonés).
      · **Elección medida** con los 115 comandos reales y 50 órdenes dichas de otra forma: multilingual-e5-small 29/50,
      Granite Embedding R2 97M 26/50 (el mejor de su tamaño en búsqueda, pero con nombres cortos todo le parece
      ~0,85), EmbeddingGemma 44/50 con el prompt «task: sentence similarity» (búsqueda 41, clasificación 38; q4 es 8×
      más rápido pero 40/50). Centrar los vectores no ayuda.
      · **Decisión** (`SemanticMatcher`, `IntentMatcher.MatchMeaning`): el más parecido gana con ≥ 0,62 y 0,02 de
      ventaja sobre el siguiente; las órdenes integradas (multimedia si están activadas, «cancela» siempre; por sus
      frases del idioma de la interfaz y del inglés, de dos palabras o más) necesitan 0,80 (sus frases atraían «abre
      spotify»). Compiten frases «no es mío» (`voice.none`: luces, tele, alarmas, el tiempo, llamadas…; se ignora la
      que se parezca ≥ 0,88 a un comando del usuario). Vectores de los nombres en `Models\embeddinggemma\anchors.bin`.
      · **Más que el nombre** (5.0.10): cada comando se compara también con lo que dicen sus acciones cuando es
      descriptivo (Energía, carpetas especiales como la papelera, volumen, cerrar un programa…; no rutas ni URLs). Las
      de Energía con su texto de voz (`voice.power.*`: «apagar el ordenador»; «Apagar el equipo» valía para cualquier
      aparato) y necesitan 0,90: «apaga el ordenador / el pc / el portátil», «shut down the computer», «éteins
      l'ordinateur» dan 0,91–0,99 y se ejecutan a la primera (sin confirmación, decisión del usuario); «apaga la
      consola / la xbox / la impresora / el monitor» no pasan de 0,875 y no hacen nada. Añadir esos aparatos a
      `voice.none` se probó y se descartó: empataban con «apaga el ordenador» (0,88 / 0,88).
      · **Evaluación** `tools\voice\pruebas.txt` (`--voice-test`; «frase => Comando», «=> -», «=> #Pause»): 108 frases
      → 52 bien, 0 mal, 13 sin entender, 42 negativos sin ejecutar nada (solo palabras: 19 bien). Lo que no entiende
      es conocimiento del mundo (KeePassXC = gestor de contraseñas, WSL = terminal de Linux) o comandos casi iguales
      (ComfyUI Output / ComfyUI (Updated) - Output).
      Probado de punta a punta con la palabra grabada del usuario + TTS («limpia la papelera»; «déjalo, no lo hagas»
      cancelando una cuenta atrás lanzada por voz). Sin probar con el micro real.
- [x] **Etapa 6** (2026-10-16, 5.0.11–5.0.12; sin confirmación por voz, decisión del usuario):
      · **Sonido al escuchar** (Opciones › Voz, `VoiceListenSound`): sonido integrado nuevo «Toc» (golpecito de
      madera grave, La4, 0,16 s, generado; los de Windows duraban más de un segundo; en 5.0.11–5.0.14 era «Escuchar»,
      dos notas agudas, y se cambia solo al cargar), al confirmar la palabra o pulsar la tecla de voz.
      · **Frases de voz por comando** (`Command.VoicePhrases`, una por línea, en el editor): cuentan como su nombre
      en las dos capas (palabras y significado). Para lo que el modelo no puede saber: «gestor de contraseñas» →
      KeePassXC, «terminal de linux» → WSL (y «quiero el bash de linux» llega por significado).
      · **Estadísticas** (grupo «Voz»): órdenes por voz (comandos e integradas), entendidas por el significado, palabras
      de activación oídas. **Logros** (categoría «Voz», 10): órdenes 1/10/100/1000, palabra 1/100/1000, por
      significado 1/10/100.
      · Capa de palabras: «misma raíz» exige que la parte común sea ≥ 60 % de la palabra corta («contraseñas» ya no
      es «continúa»).
      · Pendiente si se quiere: estado de la voz en el icono de la bandeja.
- ~~Etapa 7: capa LLM opcional~~ — descartada por el usuario (2026-10-16).

**Primer arranque:** si «Iniciar con Windows» está activado (por defecto lo está) y es una copia con
carpeta de datos normal, se registra la tarea del Programador de tareas automáticamente la primera vez,
sin tener que abrir Ajustes. Lo que se ejecuta al arrancar K-Desktop está en Eventos › «Al iniciar K-Desktop».

## Ideas

- K-Link (crear enlaces duros / uniones de carpeta) y Zona Wi-Fi móvil (sustituta de la Red Ad-Hoc): sin decidir.
- Captura de pantalla, grabación de pantalla y de audio: descartadas por ahora.
