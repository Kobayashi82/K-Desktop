# Protocolo de la tubería K-Desktop ↔ asistente

Versión del protocolo: **4** (la 2, de la etapa 1.11, lleva el id del asistente en los mensajes: puede haber varios a la vez; la 3,
de la etapa 2.3, deja el menú del clic derecho a K-Desktop —`menu`— y añade `extraState` y `extraKeys`; la 4, de la etapa
13.1, rehace los motivos de `load` y `unload`).

K-Desktop (proceso elevado) y el proceso del asistente (`KDesktop.exe --assistant`, usuario normal) se hablan por
una *named pipe* local. Este documento es el contrato entre los dos. Cada mensaje tiene un ejemplo en
[`ejemplos/`](ejemplos/), y las pruebas de las dos partes los usan.

## Transporte

- **Tubería:** `\\.\pipe\KDesktop.Assistant.<sesión>.<carpeta>`, donde `<sesión>` es el id de la sesión de Windows y
  `<carpeta>` son los 8 primeros caracteres hexadecimales del SHA-256 de la carpeta de datos (en minúsculas). Así,
  una instancia de prueba con `--data` nunca choca con la de diario. K-Desktop pasa el nombre al lanzar el proceso:
  `KDesktop.exe --assistant --pipe <nombre> [--data <carpeta>]`.
- **Servidor:** K-Desktop. **Cliente:** el proceso del asistente. Un solo cliente a la vez.
- **Formato:** UTF-8, un objeto JSON por línea (`\n`), sin BOM. Máximo **1 MiB** por línea; una línea más larga
  cierra la conexión.
- **Campo `t`:** tipo de mensaje (obligatorio). Los nombres de campo van en *camelCase*.
- **Compatibilidad hacia delante:** los tipos de mensaje y los campos desconocidos se **ignoran** (y se anotan en el
  registro en modo depuración). Añadir un mensaje o un campo opcional no cambia la versión; quitar o cambiar el
  significado de algo, sí.
- **Peticiones y respuestas:** las peticiones que esperan respuesta llevan un `id` (texto, único por conexión) y la
  respuesta lo repite. Una petición inválida se contesta con `error` y el mismo `id`.

## Seguridad (§3.3 del plan)

- La tubería se crea con una DACL que solo da acceso a **tu usuario** (y a SYSTEM) y con **etiqueta de integridad
  media**, para que un proceso sin elevar pueda abrirla. Nadie más de la máquina puede conectarse.
- Al aceptar una conexión, K-Desktop comprueba con `GetNamedPipeClientProcessId` que el cliente:
  - es **el mismo ejecutable** que K-Desktop (ruta completa igual),
  - corre con **tu usuario** y en **tu sesión**,
  - es **el PID que K-Desktop lanzó** (o, con `--assistant-dev`, un proceso del mismo ejecutable).

  Si algo falla, cierra la conexión sin contestar y lo anota en el registro.
- Lo que el asistente puede pedir es una lista cerrada: buscar y ejecutar **comandos que ya existen** (por id),
  operaciones de ventana sobre un `hwnd`, escuchar, vectores de significado, capturar teclas mientras juega y
  abrir una página de Opciones. **Nunca** lanzar un programa, un script o una URL arbitrarios.

## Varios asistentes a la vez

Puede haber **varios asistentes en el escritorio** a la vez, todos en el mismo proceso (cada uno en su
`AssemblyLoadContext`), compartiendo mundo, capa y GPU. Por eso los mensajes que tratan de un asistente llevan su id
en `assistant`:

- **K→A:** a quién va (`load`, `unload`, `config` de `assistantSettings`, `extra`, `listening`, `heard`, `cancel`, `say`,
  `observed`, `key`, `undo`). Los pasos de un `run` (`step`, `result`, `end`) van al asistente que lo pidió.
- **A→K:** quién lo dice (`loaded`, `status`, `listen`, `dictate`, `talk`, `run`, `keys`, `log`, `chat`, `panel`, `notify`, `open`,
  `hide`). El host lo pone él: un asistente nunca habla en nombre de otro.
- **Del proceso entero**, sin `assistant`: conexión, `pause`, `config` de las secciones del módulo, `status` sin
  `assistant` (modelo, memoria), `match`, `media`, `window`/`windowDone`, `embed`/`embedded` y la rueda del ratón
  (`watchWheel`, `wheel`, `fastScroll`: el host la reparte entre los que la siguen).

**A quién va cada cosa.** La palabra de activación elige: cada asistente puede tener la suya (Opciones → Asistentes) y
esa orden va a él, que contesta, hace los gestos y habla con su voz. Lo que **no va dirigido a nadie** (un comando con
gestos lanzado por un atajo o un QuickMenu, `say`, `observed`, la palabra de activación general, el chat del panel
sin elegir) va al que diga el ajuste «Cuando no le hablas a nadie en concreto»: **el último con el que hablaste** (por
defecto; si aún ninguno, el primero que se puso en el escritorio), **uno al azar** (elegido una vez por orden, no por
gesto) o **uno concreto** (si no está en el escritorio, como «el último»). Lo decide siempre K-Desktop.

## Ciclo de vida

```
asistente                               K-Desktop
   │ ── hello ─────────────────────────► │  comprueba el cliente y la versión
   │ ◄──────────────────────── welcome ─ │  (o reject y cierra)
   │ ◄─────────────────────────── load ─ │  uno por asistente en el escritorio (si hay)
   │ ── status ────────────────────────► │
   │        … mensajes …                 │
   │ ◄──── ping (cada 5 s sin tráfico) ─ │
   │ ── pong ──────────────────────────► │
   │ ◄──────────────────────────── bye ─ │  apagar el módulo / salir
   │   (cierra en ≤ 3 s; si no, K-Desktop termina el proceso)
```

- **Latido:** quien lleve 5 s sin recibir nada manda `ping`; el otro contesta `pong`. Sin respuesta en 15 s, la
  conexión se da por muerta. Si muere el asistente, K-Desktop lo relanza con espera creciente (1, 2, 4, 8… hasta
  60 s); **3 caídas seguidas** con el mismo asistente como el último cargado (menos de 2 min entre una y otra) → lo
  quita del escritorio y lo dice en el panel (con varios, el más reciente es el sospechoso más probable; si sigue
  cayendo, le toca al siguiente).
- **Si muere K-Desktop**, el asistente se cierra solo (la tubería se rompe).
- **Versión distinta:** si `hello.protocol` no es una versión que K-Desktop entienda, contesta `reject` con
  `reason: "protocol"` y cierra. (Con un único ejecutable no debería pasar; sí con `--assistant-dev` de otra
  compilación.)

## Mensajes

Dirección: **A→K** del asistente a K-Desktop; **K→A** de K-Desktop al asistente.

### Conexión

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `hello` | A→K | `protocol` (número), `sdk` (texto, p. ej. `"1.0"`), `pid` | Primer mensaje del asistente |
| `welcome` | K→A | `lang`, `theme` (`light`/`dark`), `paused`, `config` (objeto, §Configuración) | Aceptado |
| `reject` | K→A | `reason` (`protocol`, `client`, `busy`) | Rechazado; K-Desktop cierra a continuación |
| `ping` / `pong` | ambas | — | Latido |
| `bye` | K→A | `reason` (`disable`, `exit`, `restart`) | Ciérrate (con la salida del personaje, máx. 3 s). Con `restart` (otro idioma, otro tema, «Reiniciar» del panel) viene otro proceso enseguida: los asistentes salen con `Reloading` y vuelven con `reloaded`; con lo demás, con `Shutdown` |
| `error` | ambas | `id` (si respondía a una petición), `code`, `message` | Petición inválida o fallida |

### Configuración y estado de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `config` | K→A | `section`, `value`, `assistant` (solo en `assistantSettings`) | Cambió algo en Opciones → Asistente. Secciones: `general` (`sound`, sí/no, y `volume`, 0…1: el sonido de los asistentes, que mezcla el host; `manoeuvres`, desde 5.0.175: `showy`, `normal` o `fast`, la rapidez de los gestos que no llegan con un paso —los de las herramientas, llamar la atención—), `brain` (desde 5.0.107; ver «El cerebro»: `model`, el general —vacío = automático, `none` = sin IA—; `models`, el modelo propio de cada asistente que lo tenga, id → modelo (el de K-Desktop con `kdesktop`): si no está, ese asistente usa el general; `servers`, los `llama-server.exe` que arranca el proceso, el primero el normal; `folders`, dónde buscar los `.gguf`, la primera la de descargas de K-Desktop; `modelArgs`, las opciones de llama-server propias de cada modelo —ruta → «-c 32768»—, que van detrás de las de siempre (desde 5.0.111; antes `serverArgs`, unas para todos); `keepMinutes`, minutos sin usarlo antes de sacar un modelo de la gráfica, 0 = solo si se pide; sin él, 30; `oneAtATime`, sin él sí; `vision`, sin él sí; `remotes`, otros servidores: `id`, `kind` —`ollama` u `openai`; uno `openai` que contesta `/running` se usa como llama-swap (dice lo que tiene cargado y lo descarga); `llamaSwap` queda para los de antes—, `endpoint` —vacío = el de siempre de su tipo— y `apiKey`, en claro; `thinkModel`, el modelo para pensar más —vacío = el mismo pensando antes, `none` = nunca—, y `thinkTokens`, cuánto puede pensar (desde 5.0.150); `thinkModels`, el modelo propio de cada asistente para pensar más, id → modelo, como `models` (desde la etapa 13.10: si no está, el de `thinkModel`); `freeForGames`), `voice` (la voz: ver «La voz»), `tools` (`trusted`: las herramientas que ya no piden permiso; `learn`: si aprenden de lo que se habla, Opciones → Memoria; `askBefore`; `settings`: lo elegido para cada herramienta en Opciones → Herramientas, nombre → `enabled` y `risk` —null = el suyo—; `share`: las herramientas para otros programas, `enabled`, `scope` —`read` o `all`— y `key`, el SHA-256 de la clave en hexadecimal, nunca la clave), `personal` (lo propio de cada asistente: ver «Lo propio de cada asistente»), `common` (desde 5.0.169, etapa 13.13: los ajustes comunes de cada asistente que no estén como siempre, id → `size` 0,5…2, `volume` 0…1, `liveliness` 0…2, `dragging`, `kdesktopBubble` —desde 5.0.176, etapa 13.7: lo que dice siempre en el bocadillo de K-Desktop, solo para uno con `"speech": "own"`—; el host da a cada uno los que nombra su manifiesto en `common` —uno sin código, todos—, aplica él el volumen y no pasa los arrastres de su zona `body` sin `dragging`; los demás le llegan en `host.Common` con un `OnSettingsChanged`), `privacy`, `assistantSettings` (ajustes de ese asistente, según su `settings.json`: todos sus valores, cada vez que se cambia uno en Opciones). La bienvenida trae las secciones del módulo en `config` |
| `pause` | K→A | `paused` | Se ha pausado o reanudado K-Desktop: en pausa no se atienden órdenes |
| `load` | K→A | `assistant` (id), `version`, `path` (carpeta del paquete), `data` (su carpeta de datos), `entrance` | Pone este asistente en el escritorio, junto a los que ya haya. `entrance` (etapa 13.1, `EntranceKind` del SDK): `startup` = arranca K-Desktop o se enciende el módulo (la primera conexión del proceso; también tras «Empezar de cero», que borra sus datos); `summoned` = el usuario lo pone en el escritorio; `reloaded` = estaba hace un momento y vuelve sin espectáculo donde estaba: versión nueva, otro idioma propio, proceso reiniciado o recuperado de una caída (toda conexión después de la primera). Si ya estaba (otra versión), el nuevo código se carga y después el viejo sale con `Reloading` y entra el nuevo |
| `unload` | K→A | `assistant`, `reason` | Quita ese asistente del escritorio (con su salida); los demás siguen. Sin ninguno no hay asistente (K-Desktop, el del sistema, nunca llega en `load`: ver «K-Desktop, un asistente más»). `reason` (`ExitKind` del SDK): `dismissed` = el usuario lo esconde; `uninstalling` = se desinstala; `reloading` = vuelve enseguida con un `load` (p. ej. «Empezar de cero»). Apagar el módulo o cerrar K-Desktop no manda `unload`: llega `bye` y salen con `Shutdown` |
| `loaded` | A→K | `assistant`, `ok`, `error` (si falló) | Resultado de `load` |

### Entradas: lo que se le dice al asistente

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `listening` | K→A | `assistant` (sin él: K-Desktop), `active`, `for` (opcional: `dictation`) | K-Desktop está escuchando para ese asistente / ya no: se oyó su palabra, se pulsó la tecla para hablar con el asistente (Opciones → Módulo Asistente → General) o se mantiene pulsado el clic sobre él. Llega **antes** del `heard` con lo dicho; sin orden, solo `active: false`. Con `for: "dictation"` (etapa 13.4) es el dictado que pidió con `dictate`, de principio a fin: el personaje escucha con la actividad `Dictation` y el host no prepara nada. El host lo pasa al personaje con `SetState(Listening)` y, al acabar, a reposo si no está contestando. Mientras dura, su voz baja al 20 %; al empezar, el host prepara el modelo de IA (como mucho una vez por minuto) y la voz |
| `heard` | K→A | `assistant` (sin él: K-Desktop), `text`, `source` (`voice`, `panel`, `bubble`, `phone`, `command`), `files` y `act` (desde la etapa 14.1) | Una orden para ese asistente (el de la palabra, el elegido en el panel o, si no va dirigido a nadie, el de Opciones → Asistentes). Primero las **reglas fijas** (hora, temporizador, nota, «cancela», «repite», «deshaz»), que contestan sin IA; lo demás va a la IA con la personalidad de ese asistente y sus últimas 3 intervenciones. Una orden nueva sustituye a la que siga en curso. Se contesta con `chat` y `status`; en pausa se ignora |
| `cancel` | K→A | `assistant`, `source` (`voice`, `escape`, `panel`) | «Lander, para» o Escape: cancela lo que esté haciendo (la respuesta que piensa o dice; `skip` en los pasos que queden). Escape va al que esté haciendo algo. Sin respuesta (decirle «cancela» sí contesta) |
| `listen` | A→K | `assistant`, `seconds` (máx. 10), `expect` (lista opcional de respuestas previstas, solo orientativa) | Escucha sin palabra de activación: espera hasta `seconds` a que el usuario empiece a hablar y, si no dice nada, se cierra sin aviso; lo dicho llega como `heard` (`voice`). El host la pide tras contestar con una pregunta («¿A o B?», o una respuesta de la IA que acaba en «?») a una orden dicha en voz alta. La ventanita de permiso (`ask` y los comandos que preguntan) escucha ella sola si la última orden de ese asistente fue por voz: «sí», «no» y «sí, y no vuelvas a preguntar» la contestan sin llegar al asistente |
| `dictate` | A→K | `assistant` | «Escribe lo que te diga» (etapa 10.5): el host lo manda cuando el asistente **ha acabado de hablar**. K-Desktop empieza un dictado (lo dicho se escribe donde está el cursor, a trozos en cada pausa, sin IA ni `heard`) que acaba tras unos segundos de silencio, con Escape o con «deja de dictar» |
| `talk` | A→K | `assistant`, `active` | Clic mantenido sobre el personaje (el host lo detecta: 450 ms pulsado sin arrastrar; entonces no hay `Click` ni arrastre): empieza / termina de grabar (y pasa a ser «el último con el que hablaste») |

### Comandos de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `match` | A→K | `id`, `text` | Busca comandos para un texto, sin ejecutar: lo mismo que la voz (`IntentMatcher.Plan`: palabras, significado y, con Opciones → Multimedia, las órdenes de música), entre los comandos activos |
| `matched` | K→A | `id`, `parts` (lista), `unmatched` (trozos sin casar), `score` | Resultado. Cada parte: `text`, `sure` (el primer candidato gana claro: por voz se ejecutaría) y `candidates` (mejor primero, 4 como mucho: `{ command, name, score }` o, para una orden de música integrada, `{ name, score, media, value }` con `media` = `volumeDown`, `pause`…). Si no nombra nada claro: una sola parte, el texto entero, sin `sure`, con lo más parecido, y además `near`: los 10 más cercanos por significado a toda la orden, para que el modelo del asistente elija con una letra (5.0.200). `power: true` en los comandos que apagan, reinician o cierran sesión (no se escribe si es `false`): esos nunca se ejecutan sin llegar al 0,90 de la voz |
| `run` | A→K | `assistant` (vacío: K-Desktop), `run` (id de ejecución), `command` (id), `choreography` (sí/no) | Ejecuta un comando (activo). Sale en el historial de K-Desktop con origen «Asistente» y quién lo pidió. Sin coreografía (fase 4) se contesta con `end` al terminar; en pausa o si el comando no existe, `end` `failed` al momento |
| `step` | K→A | `run`, `index` (único y creciente en el run: los pasos de los comandos que llama siguen contando), `count` (pasos del comando al que pertenece), `depth`, `kind` (`Window`, `Volume`…), `op` (`Snap`, `Minimize`, `Close`… o vacío), `summary`, `target` (opcional), `timeoutMs` | El paso que va a ejecutar. `target`: `hwnd` (0 si aún no hay ventana: un programa por abrir, solo `process`), `process`, `title`, `rect` (su parte visible ahora; no está si está minimizada), `result` (dónde quedará, si se sabe: «Colocar» y «Mover a otro monitor» al píxel, restaurar o centrar una maximizada a un par de píxeles), `state` (`normal`, `maximized`, `minimized`), `elevated` (de un proceso de administrador: solo K-Desktop puede moverla). `speed` (opcional): cuánto puede durar la maniobra, `showy`, `normal` (si falta) o `fast` (Opciones → Módulo Asistente → General → Maniobras; en `fast` ninguna pasa de medio segundo: el host hace la acción a los 0,5 s). En un comando con más acciones que «Comandos largos», solo llegan como pasos las de ventanas (`Window`, `AppControl` `Activate`/`Close`); el resto se ejecuta sin preguntar. Una acción que solo llama a otro comando o evento no es un paso: lo son sus acciones, con `depth` + 1. Solo llegan si el `run` pidió `choreography` (el host la pide cuando el asistente está en el escritorio) |
| `go` | A→K | `run`, `index` | «Hazlo ahora» (en el impacto del disparo) |
| `done` | A→K | `run`, `index` | «Ya lo he hecho yo» (ventana arrastrada). K-Desktop lo verifica y aplica el resultado exacto igualmente |
| `skip` | A→K | `run`, `index`, `all` (opcional) | No ejecutes este paso. Con `all: true` (y `index` 0), nada de lo que queda: la orden se canceló; K-Desktop termina con `end` `cancelled` |
| `result` | K→A | `run`, `index`, `status` (`ok`, `failed`, `skipped`), `message` (si falló, en el idioma del usuario) | Resultado del paso |
| `end` | K→A | `run`, `status` (`ok`, `failed`, `cancelled`, `declined` —el usuario no dio permiso—), `messages` (lista: lo que dijo el comando, sus avisos y errores, en el idioma del usuario), y para un `act`: `name` (el programa que encontró, con su nombre: «Google Chrome» para «chrome») y `reason` (si no se hizo: `noWindow`, `noProcess`, `notAllowed`) | Fin de la ejecución |
| `describe` | A→K | `id`, `command` (id) | Qué hace un comando (herramienta `describe_command`), sin ejecutarlo |
| `described` | K→A | `id`, `found`, `name`, `actions` (lo que hace cada acción activa, en orden: «Abrir programa: Steam»), `hotkeys`, `asks` (`no`, `once`, `always`: si un asistente tiene que preguntar antes) | Resultado |
| `phrase` | A→K | `id`, `command` (id), `phrase`, `remove` (opcional) | Otra forma de llamar a un comando por voz (5.0.132, herramienta `add_voice_phrase`, o aprendida de una corrección: «abre Kamugi» no encontró nada y luego «se llama K-Movies» lo ejecutó). Se añade a sus frases de voz (Comandos → frases de voz) y desde entonces la entienden la voz de K-Desktop y los asistentes, sin IA. Con `remove`, se quita (deshacer). Nunca una frase que ya tenga otro comando. Se contesta con `phraseDone` |
| `phraseDone` | K→A | `id`, `ok`, `name` (el comando), `reason` (si no: `noCommand`, `empty`, `exists`, `taken`, `noPhrase`), `other` (con `taken`: el comando que ya la tiene) | Resultado |
| `act` | A→K | `assistant`, `run`, `op` (`closeWindow`, `quit`, `minimize`, `maximize`, `restore`, `front`, `left`, `right`, `center`, `otherMonitor`, `mute`, `unmute`, `volumeUp`, `volumeDown`, `volume`, `kill`), `app` (el programa con las palabras del usuario; vacío = la ventana de delante), `value` (para `volume`, 0–100), `choreography` | Hace una cosa a un programa (etapa 8.1): K-Desktop lo busca entre las ventanas abiertas (y, para `quit` y el volumen, entre los procesos) y lo hace como un comando de usar y tirar con las acciones de su catálogo: queda en el historial, pregunta antes como un comando y, con `choreography`, con los pasos del asistente. Solo esas operaciones, nunca una acción cualquiera (el asistente no tiene permisos de administrador y K-Desktop sí). `closeWindow` cierra esa ventana; `quit`, el programa entero; `kill` le pide cerrarse y, si sigue a los 3 s, lo termina a la fuerza (sin `app` o con «lo que se ha colgado», el que no responde o el de delante; nunca procesos de Windows ni K-Desktop: `notAllowed`; pregunta siempre, 🔴). Se contesta con `end` |
| `text` | A→K | `assistant`, `run`, `op` (`grab` o `put`), `source` (grab: `auto` —lo seleccionado y, si no hay, lo copiado—, `selection` o `clipboard`), `text` (put), `mode` (put: `paste` o `copy`), `hwnd` (put: la ventana de la que salió) | Un texto con el que trabajar (etapa 8, `transformar_texto`). `grab`: K-Desktop manda Ctrl+C a la ventana de delante y devuelve el portapapeles a como estaba (todos sus formatos); sin selección, o si delante está una ventana de K-Desktop, lo copiado. `put`: lo pega con Ctrl+V solo si la ventana de delante sigue siendo `hwnd` (y después devuelve el portapapeles); si no, o con `copy`, lo deja copiado. Lo hace K-Desktop porque puede escribir en ventanas de administrador. Se contesta con `textDone` |
| `textDone` | K→A | `run`, `ok`, `text` y `window` (grab), `from` (grab: `selection`/`clipboard`; put: `pasted`/`copied`), `hwnd`, `reason` (`empty`, `noSelection`, `tooLong`, `busy`; un put que acabó `copied` por otra ventana delante lleva `moved`) | Cómo fue un `text` |
| `runEvent` | A→K | `assistant`, `run`, `event` (su nombre; vale el más parecido si se parece claramente) | Ejecuta los comandos de un evento de K-Desktop. Pregunta antes si alguno de sus comandos preguntaría (manda el más arriesgado). Se contesta con `end` |
| `media` | A→K | `order` (`pause`, `resume`, `next`, `previous`, `volumeUp`, `volumeDown`, `volumeSet`, `mute`, `unmute`, `musicOn`, `musicOff`, `cancel`), `value` (el volumen o el paso, si lo tiene), `run` (opcional) | Orden de música integrada de K-Desktop (salida de `match`). Con `run` se contesta con `end` |
| `say` | K→A | `assistant` (opcional: sin él, K-Desktop), `text`, `aloud` (desde la etapa 14.2) | Acción «Asistente: decir» (al de la acción o, si no dice cuál, al de Opciones → Asistentes): lo dice en su bocadillo y en el chat, sin la IA. Uno que no está en el escritorio (o K-Desktop, que no tiene figura): el aviso de K-Desktop. Con `aloud`: «Léemela» de la hoja de lectura; `text` es Markdown, se lee como frases, solo con la voz (sin bocadillo, chat ni historial); una orden o `cancel` lo para |
| `extra` | K→A | `assistant`, `action` (id del extra, p. ej. `lander.manual`) | Acción «Asistente: acción», o un botón de sus ajustes o de su panel. Si el asistente no estaba en el escritorio, K-Desktop lo pone, espera a su `loaded` y entonces lo manda (si tarda más de 20 s, no): puede llegar antes de que el asistente tenga su mundo listo, y entonces debe guardarlo y hacerlo al arrancar |
| `extraKeys` | K→A | `assistant`, `keys` (objeto: id del extra → atajo como lo enseña K-Desktop, `"CTRL + ALT + L"`; los que no tienen, no salen) | Los atajos de los comandos que ejecutan sus extras. Llega tras cada `load` y cada vez que cambian los comandos. El SDK lo da con `IAssistantHost.ExtraHotkey` (p. ej. para decir «pulsa Ctrl+Alt+L») |
| `observed` | K→A | `assistant`, `source` (`hotkey`, `menu`, `launcher`, `voice`, `web`, `udp`, `event`, `manual`), `command`, `steps` (`kind`, `op`, `summary`, `target` como en `step`) | (Opciones → Módulo Asistente → General → «Enseñar también los comandos que lanzas tú», desactivado por defecto) una acción de un comando lanzado por otra vía, justo antes de ejecutarse, para el asistente que contesta lo que no va a nadie. **Nada la espera**: es solo adorno (el host la enseña en «rápido», sin bocadillo y nunca encima de un paso de una orden) |

### Ventanas, voz y teclado

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `window` | A→K | `id`, `hwnd`, `op` (`move`, `minimize`, `maximize`, `restore`, `activate`), `rect` (para `move`: su parte visible) | Lo que el proceso del asistente no puede hacer él: una ventana de un proceso de administrador, o traer una ventana delante (Windows solo deja a K-Desktop). Solo ventanas de nivel superior de otros programas; nunca las de K-Desktop ni el escritorio. Los `move` de un arrastre llegan como mucho cada 33 ms (el último gana); K-Desktop los aplica en orden. El SDK lo hace solo en `TryMove` y `TryCommand` (SDK 1.3) |
| `windowDone` | K→A | `id`, `ok` (falso si no está permitido o no se pudo) | Resultado |
| `embed` | A→K | `id`, `texts` (lista, máx. 64), `keep` (opcional), `use` (opcional: `query` o `document`), `size` (opcional) | Vectores de significado con el EmbeddingGemma que K-Desktop ya carga para la voz (una sola copia del modelo en memoria). Con `keep: true` (las frases de ejemplo de las herramientas, que se piden una y otra vez) K-Desktop guarda sus vectores en disco (`Models\embeddinggemma\assistant.bin`); las órdenes nunca se guardan. El proceso espera 5 s por una orden y 30 s por las frases; sin respuesta, elige las herramientas por palabras. Desde la 5.0.186 (fase 19), `use` pide los vectores para buscar documentos: `query` (una pregunta, 128 tokens) o `document` (un trozo de un documento, 512 tokens), cada uno con el prompt de EmbeddingGemma para eso; `size` deja solo los primeros números (Matryoshka: 768, 512, 256 o 128; los documentos usan 256). K-Desktop calcula los trozos de cuatro en cuatro, así una orden de voz no espera a un documento entero |
| `embedded` | K→A | `id`, `vectors` (lista de listas de números), `model` | Resultado (vectores normalizados, en el orden de `texts`). `model` dice qué modelo los hizo: vectores de modelos distintos no se comparan. Si K-Desktop no tiene el modelo: `error` con `code: "noModel"` y el `id` de la petición |
| `keys` | A→K | `assistant`, `capture` (lista de teclas, nombres de `Key` de WPF: `Up`, `A`, `Escape`…; vacía = soltar) | Que K-Desktop se trague esas teclas cuando se pulsan sin Ctrl, Alt ni Win (así los atajos siguen funcionando) y se las reenvíe a ese asistente (juego del Lander). Los modificadores no se capturan. En pausa, o si el asistente sale o el proceso se cae, K-Desktop las suelta solo |
| `key` | K→A | `assistant`, `key`, `down` | Una tecla capturada bajó o subió (la repetición automática no se manda) |
| `watchWheel` | A→K | `watch` | Que K-Desktop mande cada giro de la rueda (`wheel`) o deje de hacerlo. El host lo pide mientras algún asistente lo quiera (`IPointer.WatchWheel`) |
| `wheel` | K→A | `x`, `y`, `delta` (120 por muesca; positivo = arriba o a la derecha), `horizontal`, `hwnd` (la ventana de nivel superior bajo el cursor) | Un giro de la rueda. Sale del gancho de K-Desktop, que la ve también sobre ventanas de administrador |
| `fastScroll` | K→A | `strength` (0…1, positivo = hacia abajo), `x`, `y` | El usuario ha girado la rueda con fuerza (varias muescas seguidas; luego descansa 1,2 s). Se manda siempre: es raro |

### El cerebro (Opciones → Asistente → Cerebro)

Desde 5.0.107 el proceso del asistente (sin administrador) es su propio llama-swap: busca los `.gguf` en las carpetas, arranca cada uno con su `llama-server.exe` (el que tenga la carpeta más cerca del modelo; si no, el primero; si no puede cargarlo, el siguiente) solo cuando se le habla a un asistente o con «Cargar ahora», con su `mmproj` si lo hay y la visión está encendida, y lo para al liberar la gráfica, tras `keepMinutes` o al cerrarse (están en un Job Object). Con `oneAtATime`, cargar uno descarga los demás (también lo cargado en los otros servidores de este PC); sin él se quedan mientras quepan en la gráfica. Habla también con los otros servidores. Los modelos se nombran por dónde están: un `.gguf` por su ruta, el de otro servidor como `id/nombre` (`r1/qwen3:4b`), así que dos servidores con el mismo modelo no se confunden. Descargar de Hugging Face lo hace K-Desktop (como Kokoro y Parakeet) y luego manda `rescan`. K-Desktop solo enseña y pide.

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `brain` | K→A | `op` (`check`, `rescan`, `test`, `unload`, `load`), `model` (para `load`, sin él el que se usa; para `unload`, solo ese), `remote` (para `test`) | Mirar otra vez, mirar también las carpetas (tras una descarga), probar ya otro servidor, liberar la gráfica —todo lo cargado, o solo `model` (su «Liberar» en Opciones, desde 5.0.111); también la acción «Asistente: cerebro» y «descarga el cerebro», que el proceso entiende sin la IA— o cargar un modelo ya. Se contesta con `brainState` |
| `brainState` | A→K | `state` (`off`, `checking`, `ready`, `noModels`, `modelMissing`, `error`), `model` (el general que se usa), `automatic` (el más grande de este PC que cabe en la gráfica dejando 3 GB; sin ninguno aquí, unos 4 B de otro servidor), `models` (`name` —su referencia—, `billions`, `sizeBytes`, `tools`, `vision`, `server` —el llama-server que lo abre— o `remote` —el id de su servidor—), `loaded` (los que están en memoria, aquí y en los otros servidores que lo dicen), `loading` (el que se está cargando), `message` (qué falló, en el idioma del usuario), `videoMemoryGb`, `servers` (`path`, `exists`, `models`), `remotes` (`id`, `state` —`checking`, `ok`, `unreachable`, `error`—, `models`, `ms`, `message`) | Cómo está el cerebro: tras cada cambio y cada `brain` |

| `bench` | K→A | `op` (`start`, `cancel`, `state`), `model` (para `start`) | Opciones → Cerebro → *Probar modelo*: pasar el banco de pruebas con ese modelo (el propio, `Assistant\bench.json`, o el incluido en el idioma del usuario). Las órdenes van como de verdad (reglas y luego la IA con las herramientas elegidas por significado), pero las herramientas que hacen algo solo lo fingen; las que solo leen (`ReadsOnly`: hora, notas, cálculo) se ejecutan. Se contesta con `benchState` |
| `benchState` | A→K | `state` (`idle`, `running`, `done`, `cancelled`, `failed`), `model`, `done`, `total`, `passed`, `meanMs`, `own` (es el banco propio), `failures` (al terminar: `say`, `expected`, `passed`, `route` —`rule`, `ai` o `failed`—, `tools`, `answer`, `ms`), `history` (la última puntuación de cada modelo: `model`, `passed`, `total`, `meanMs`, `at`, `own`), `message` (si falló) | Cómo va el banco: tras cada orden y al terminar. El historial se guarda en `Assistant\bench-results.json` |

### La voz (Opciones → Asistente → Voz)

Lo que contestan lo dicen en voz alta frase a frase, mientras la IA aún escribe (las frases que ya han terminado empiezan a
sonar; Kokoro dice una frase larga a trozos, el primero mientras hace el resto; Piper, con una frase de una o dos palabras
junta la siguiente). La síntesis es del proceso del asistente; K-Desktop solo descarga los modelos (`Models\kokoro\`,
`Models\piper\` —un `.onnx` y su `.onnx.json` por voz— y `Models\espeak-ng\`, que usan Piper siempre y Kokoro para inglés,
francés, italiano y portugués) y manda los ajustes en la sección `voice` de `config` (`engine`: `auto` —Kokoro, si no Piper,
si no Windows, si no pitidos—, `kokoro`, `piper`, `windows`, `beeps` u `off`; `profile` —el perfil fijo para todos, vacío =
el de cada asistente—, `kokoroVoice` —una voz del idioma o una mezcla, `ef_dora:0.7,em_alex:0.3`—, `piperVoice` —la clave
de una voz del idioma, `es_ES-davefx-medium`, o `clave#hablante` para una de varios—, `windowsVoice`, `speed` 0,5…2, `volume` 0…1, `pan` —suena desde donde está—, `night` —de 22:00 a 8:00, a la mitad— y `fullscreen` —con algo a
pantalla completa, solo el bocadillo—; también en la bienvenida). Sin cuerpo no se dice nada en voz alta.

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `voice` | K→A | `op` (`state`, `test`, `stop`, `unload`), `text` (para `test`), `assistant` (para `test`: quién lo dice; sin él, K-Desktop, con su voz propia), `mood` (para `test`: `happy`, `error` o el que use el asistente; sin él, sin ánimo) | Preguntar cómo está la voz, decir una frase de prueba (con el bocadillo del asistente si está en el escritorio; suena aunque haya algo a pantalla completa), pararla o soltar Kokoro, Piper y espeak-ng de la memoria (antes de borrar sus archivos) |
| `voiceState` | A→K | `engine` (el que habla de verdad: `kokoro`, `piper`, `windows`, `beeps`, `off`), `windowsVoices` (las de Windows para el idioma), `kokoroLoaded`, `windowsVoicesOf` (las de Windows de cada idioma, para un asistente que habla en el suyo), `piperLoaded` (la clave de la voz de Piper en memoria, o nada) | Tras cada `voice` `state` y cada cambio de la sección `voice` |

### Lo que sabe (fase 19)

Opciones → Módulo Asistente → «Lo que sabe»: carpetas de documentos del usuario o archivos sueltos (nunca un disco entero; desde la 5.0.187 `folders` puede llevar archivos, y su `KnowledgeFolderInfo` es el de ese archivo) que el proceso del
asistente lee una vez, trocea, pasa a vectores (`embed` con `use: document`) y guarda en `Assistant\Knowledge\` (un archivo
por documento; fuera de la copia de seguridad: se rehace). Al arrancar compara fecha y tamaño de cada archivo y solo lee lo
cambiado; mientras está abierto, vigila las carpetas. Para con un juego a pantalla completa, con K-Desktop en pausa y mientras
un asistente contesta. Las carpetas y si se leen imágenes con el OCR van en `config` `tools` → `knowledge` (`folders`, `ocr`, `paused` —el usuario paró la lectura—, `skipped` —archivos quitados—).
Los asistentes buscan con la herramienta `search_knowledge`.

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `knowledge` | K→A | `op` (`state`, `files`, `search`, `rebuild`, `clear`), `folder` (para `files`), `text` (para `search`, y las palabras de la ruta en `files`), `filter` (para `files`: `all`, `ready`, `waiting`, `problem`, `skipped`) | Preguntar cómo va, los archivos de una carpeta, probar una búsqueda, leerlo todo otra vez u olvidar lo leído |
| `knowledgeState` | A→K | `phase` (`loading`, `scanning`, `indexing`, `paused`, `ready`, `off`), `files`, `done`, `pieces`, `folders` (`path`, `online`, `files`, `done`, `pieces`, `failed`), `current`, `paused` (`game`, `kdesktop`, `busy`, `user`), `wordsOnly` (sin EmbeddingGemma), `bytes`, `currentPieces` y `currentDone` (los trozos del archivo que lee y cuántos lleva) | Cómo va: cuando cambia (dos veces por segundo como mucho) y tras cada `knowledge` |
| `knowledgeFiles` | A→K | `folder`, `files` (`path`, `state` —`ready`, `empty`, `failed`, `waiting`, `skipped`—, `pieces`, `pages`, `ocr`), `total`, `text` y `filter` (lo pedido), `counts` (cuántos hay en cada filtro) | Los archivos de una carpeta que cumplen lo pedido (los 500 primeros) |
| `knowledgeFound` | A→K | `text`, `hits` (`path`, `page`, `text`, `score`), `how` (`mixed` o `words`), `milliseconds` | Lo que encontró una búsqueda de prueba |

### Lo propio de cada asistente

Opciones → Asistentes → uno → «Personalidad y voz». La sección `personal` de `config` (también en la bienvenida, antes de los
`load`) lleva, por id, solo los asistentes que tienen algo propio: `language` (en el que habla: sus respuestas, sus frases, su
voz, lo que dice la actividad y su `host.Language`; vacío = el general), `prompt` (su personalidad escrita por el usuario, en
vez de la de su paquete; sus frases fijas siguen siendo las del paquete), `engine`, `kokoroVoice` y `piperVoice` (ya resueltas para
su idioma), `windowsVoice`, `profile` (uno de K-Desktop, `own` = el de su paquete aunque haya uno fijo para todos, vacío = lo general) y
`speed`. Lo que falta o va vacío es lo general. Cambiar el idioma de un asistente que está en el escritorio lo vuelve a cargar
(`load`): su código lee `host.Language` al empezar. El id `kdesktop` es K-Desktop (desde 5.0.112; en 5.0.111 era el id vacío): su idioma, su personalidad (en vez de la del
módulo) y su voz; dice sus respuestas en voz alta, desde el centro.

### K-Desktop, un asistente más

Desde 5.0.112 K-Desktop es un asistente del sistema con id `kdesktop`, reservado (ningún paquete puede usarlo): sin `.kai`, sin
figura ni gestos. En la tubería es **el que no lleva `assistant`** (null o vacío): nunca llega en `load`/`unload` ni tiene
`AssistantSlot`; su modelo propio va en `brain.models` y lo suyo en `personal`, los dos con la clave `kdesktop`. Activo (en
`assistantsOnDesktop` de K-Desktop) contesta con el aviso de K-Desktop y su voz y ejecuta comandos sin coreografía. Sin ningún
asistente activo **no hay asistente**: K-Desktop no manda `heard`, `listening` ni `say` (la palabra «para el asistente», la tecla y el
chat avisan de que no hay ninguno) y no se carga ningún modelo.

### Del asistente al panel de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `status` | A→K | `assistant` (opcional), `state` (`idle`, `listening`, `thinking`, `thinkingHard`, `loadingBrain`, `noBrain`, `speaking`, `awaitingConfirmation`, `busy`, `sleeping`), `activity` (opcional, etapa 13.4: `searchingWeb`, `screen`… = `AssistantActivity` en camelCase), `tool` (opcional: la herramienta en marcha), `model`, `modelLoaded`, `busy`, `memoryMb`, `lastError` | Estado para el panel: con `assistant`, el de ese asistente; sin él, el del proceso (modelo, memoria). Solo se manda cuando cambia. El host lo junta de la conversación, las herramientas (`awaitingConfirmation` mientras una pide permiso, `busy` mientras corre), la carga del modelo de este PC (`loadingBrain` mientras piensa) y el dictado. Gana la pregunta de permiso, luego hablar, luego una herramienta, luego la carga del modelo y luego lo que diga la conversación |
| `log` | A→K | `assistant` (sin él: K-Desktop), `entry`: `id`, `at`, `heard` (lo que se le pidió; vacío si lo hizo solo), `route` (`rule`, `kdesktop`, `ai`, `undo`, `timer`, `memory`), `did`, `ok`, `undo` (id, si se puede deshacer), `undid` (en `undo`: el id que deshizo) | Una línea de Panel → Asistente → Actividad. K-Desktop guarda las 200 últimas (`Assistant/activity.json`) |
| `ask` | A→K | `id`, `assistant`, `tool`, `text` («¿Puedo cerrar Word?»), `canTrust` (ofrecer «Sí, y no vuelvas a preguntar»), `seconds` | Una herramienta pide permiso (🟡 la primera vez, 🔴 siempre). K-Desktop enseña una ventanita sin quitar el foco y contesta `answer` |
| `answer` | K→A | `id`, `choice` (`yes`, `no`, `always`, `timeout`) | La respuesta; sin respuesta a tiempo, `timeout` (= no). Con `always`, K-Desktop añade la herramienta a `assistantTrustedTools` y manda `config` `tools` |
| `chat` | A→K | `assistant` (lo pone el host; sin él: K-Desktop), `role` (`user`, `assistant`), `text`, `final` (si es `false`, llegan más con el mismo `turn`, cada una con **todo** el texto hasta ese momento; como mucho 8 por segundo), `turn` | Conversación: lo que se oyó (`user`, al empezar) y la respuesta (`assistant`), ya limpia para un bocadillo (sin Markdown, emojis ni etiquetas). Una orden sustituida o cancelada no manda su línea final. **Respuesta larga** (etapa 14.2): `written` lleva el texto entero en Markdown (también mientras crece) y `text` es solo su resumen, lo que dice la voz; `x`, `y` = dónde está el asistente (el punto de su bocadillo, píxeles de pantalla; sin ellos, no tiene cuerpo). K-Desktop lo enseña en una hoja de lectura junto a él |
| `panel` | A→K | `data`, `assistant` (id, lo pone el host) | Datos del panel propio del asistente (los `bind` de su `panel.json` y las galerías con `source` de su `settings.json`). Se **fusionan** con lo publicado antes: los objetos clave a clave; lo demás (números, textos, listas) sustituye. El host manda como mucho cuatro por segundo por asistente y K-Desktop lo olvida en cada `load` de ese asistente |
| `notify` | A→K | `assistant` (lo pone el host), `title`, `text` | Aviso de K-Desktop (logro desbloqueado…) |
| `open` | A→K | `page` (`assistant`, `assistantSettings`, `assistantVoice`, …), `assistant` (opcional) | Abre esa página de Opciones (menú del clic derecho → *Ajustes*), la de ese asistente si lo dice. `assistant` es Panel → Asistente |
| `menu` | A→K | `assistant`, `x`, `y` (píxeles de pantalla) | Clic derecho sobre el asistente: K-Desktop enseña su menú allí, como los demás menús de K-Desktop (panel, ajustes, sus extras con su icono y su atajo, esconder). Sin conexión, el host enseña uno nativo con lo mismo |
| `menuClosed` | K→A | `assistant` | El menú que abrió `menu` se cerró (o no llegó a abrirse). Mientras está abierto, el host esconde las zonas de clic de ese asistente (son «siempre visibles» y se mueven con el cuerpo: acababan encima del menú y un clic en ellas lo cerraba); con este mensaje vuelven donde el asistente las dejó. Si no llega, vuelven a los 60 s |
| `write` | A→K | `assistant`, `x`, `y` (píxeles de pantalla) | Clic central sobre el asistente: K-Desktop abre encima la caja para escribirle (también sale de su menú → *Escribir* y de la acción «Asistente: preguntar» sin texto). Lo escrito vuelve como `heard` con `source: "bubble"` |
| `dropped` | A→K | `assistant`, `files` (rutas completas), `x`, `y` (píxeles de pantalla) | Archivos soltados sobre una zona de clic del asistente (etapa 13.8). Las zonas de clic están en el proceso del asistente, que va como usuario normal, así que el Explorador puede soltar en ellas (en K-Desktop, elevado, no). El asistente ya ha recibido `PointerEventKind.FilesDropped` para hacer como que los coge; qué hacer con ellos lo decide K-Desktop (etapa 14.1): la ventana «¿Qué hago con «x»?» junto al asistente, con botones según el tipo de archivo (resumir, leérmelo, qué es, leer su texto, qué hay dentro, abrir, abrir su carpeta, buscar parecidos, enviar si hay alguna herramienta que envía), la caja para escribirlo y, con los modelos de voz, unos segundos de micrófono; lo elegido va en un `heard` con `files` (la conversación los guarda 5 minutos desde la última vez que se habla de ellos: «bórralo» después también vale) y, para abrir sin la IA, `act` `open` o `folder`. Una orden con archivos va directa a la IA (ni reglas ni comandos) con `read_file`, `open_file`, `move_file` y `delete_file` a mano y sus rutas junto a la orden; borrar y mover preguntan siempre. Solo archivos y carpetas, siempre como copia o enlace (nunca «mover») |
| `showcase` | K→A | `assistant`, `op` (`list`, `play`, `stop`), `item`, `own` | El escaparate (etapa 13.9), **solo en la tubería del escenario** (fase 16: ver «El escenario del escaparate»; el proceso de los asistentes del escritorio ya no lo atiende). `list` pide lo que sabe enseñar (`showcaseList`); `play` lo hace ahora (`showcaseDone` al acabar; uno nuevo para el anterior) y `stop` lo para. Con `own` falso es de los del host, que los lanza él: entradas y salidas (`enter.startup`, `enter.summoned`, `exit.dismissed`, `exit.uninstalling`: sale y vuelve a cargarse; una entrada acaba cuando ha entrado y su cuerpo se queda quieto, 1,5–7 s; una salida, cuando se ha ido y luego vuelve con `Reloaded`), estados unos segundos (`state.listening`, `state.thinking`, `state.speaking` con nivel de voz de mentira…), reacciones (`react.success`…), gestos sin ventana (`gesture.announce`, `gesture.communicate`, `gesture.mark`, `gesture.pointAt`, `gesture.goTo`, `gesture.scan`, `gesture.typing`) y, con las ventanas del escenario, lo que hace a una ventana (`window.minimize`, `window.maximize`, `window.close`, `window.move`, `window.activate`, `window.open`: como un paso de una orden, con `Commit`; si no sabe, lo hace el escenario y el bocadillo lo dice, y el resultado es `notPerformed`). Con `own` verdadero, uno de los suyos (`IAssistant.Showcase`, SDK 1.18) |
| `showcaseList` | A→K | `assistant`, `items` (`id`, `group`, `own`, `names`) | Lo que sabe enseñar: los del host primero (K-Desktop los nombra con sus textos) y luego los suyos con su nombre en idiomas (sin `names`: el texto de K-Desktop para ese id, si lo tiene, o el id) |
| `showcaseDone` | A→K | `assistant`, `item`, `own`, `result` (`done`, `notPerformed`, `stopped`, `unknown`, `failed`) | Acabó lo pedido con `showcase` `play` |
| `extraState` | A→K | `assistant` (lo pone el host), `extra`, `state` (`shown`, `disabled`, `hidden`) | Enseña, atenúa o esconde uno de sus extras en su menú y en los botones de su panel (`IAssistantHost.SetExtraState`). Tras cada `load` todos vuelven a `shown`. Los comandos y la voz lo siguen mandando: el asistente decide qué hacer |
| `hide` | A→K | `assistant` | Menú del clic derecho → *Esconder*: K-Desktop quita ese asistente del escritorio (`unload` con `hiding`); los demás siguen |
| `memory` | K→A | `op` (`list`, `add`, `edit`, `delete`, `clear`), `id` (para `edit` y `delete`), `text` (para `add` y `edit`) | Opciones → Asistente → Memoria: lo que saben de ti. Se contesta con `memoryState` |
| `memoryState` | A→K | `items` (`id`, `text`, `kind`: `fact`, `like` o `event`; `importance` 0…1, `createdAt`, `source`: `said` —se lo pediste—, `learned` —lo sacó de una conversación— o `added` —desde Opciones—; `lastRecalled`, `recallCount`) | Todo lo que recuerdan del usuario (compartido por todos los asistentes), lo más nuevo primero: tras cada `memory` y cada vez que cambia. Lo que aprenden solos sale también en la actividad (`route: "memory"`) y se puede deshacer |
| `notes` | K→A | `op` (`list`, `add`, `edit`, `delete`, `clear`), `id` (para `edit` y `delete`), `text` (para `add` y `edit`) | Opciones → Asistente → Notas: lo que te apuntan («apunta que…»). Se contesta con `notesState` |
| `notesState` | A→K | `items` (`id`, `text`, `at`) | Todas las notas (compartidas por todos los asistentes), lo más nuevo primero: tras cada `notes` y cada vez que cambian |
| `tools` | K→A | `op` (`list`, `test`, `start`, `stop`, `reload`), `name` (la herramienta de `test`, el servidor MCP de `start`/`stop`), `arguments` (JSON en texto, para `test`) | Opciones → Herramientas (etapa 9.3). `test` la ejecuta como lo haría un asistente (pregunta si toca y sale en la actividad, ruta `test`) y `start` arranca un servidor MCP para ver si funciona: los dos se contestan con `toolTest`. Todos, con `toolsState` |
| `toolsState` | A→K | `tools` (`name`, `group`: `builtin`, `json` o `mcp:<servidor>`; `description`, `risk` y `defaultRisk` —`safe`, `askOnce`, `askAlways`—, `readsOnly`, `enabled`, `source`: su archivo o su servidor; `example`: una frase que la pide, en el idioma del usuario; `schema`: el esquema JSON de sus parámetros, en texto, del que Opciones hace el formulario de «Probar»), `files` (las herramientas JSON: `file`, `name`, `type`, `enabled`, `errors`, `secrets`), `servers` (los MCP: `id`, `transport` —`stdio`, `http`, `sse`—, `summary`, `disabled`, `state` —`stopped`, `starting`, `running`, `failed`—, `tools`, `secrets`, `error`, `since`), `mcpErrors`, `shared` (los programas que usan las herramientas compartidas: `name`, `since`, `calls`) y `sharedTools` (cuántas ven) | Todo lo que hay: tras cada `tools` y cada vez que algo cambia (se recargan los archivos, un servidor arranca o se para, cambian las preferencias) |
| `toolTest` | A→K | `name`, `ok`, `text` (lo que devolvió, o por qué no), `ms` | Lo que dio una prueba de `tools` |
| `undo` | K→A | `assistant`, `undo` (id) | Deshacer una línea de la actividad desde el panel; se contesta con un `log` de `route` `undo` |

### El escenario del escaparate (fase 16)

La pestaña «Probar» de la ficha de un asistente instalado no lo enseña en el escritorio: K-Desktop arranca **otro proceso**,
`KDesktop.exe --assistant-showcase <carpeta del paquete> --pipe <tubería propia>` (`Core/Assistant/AssistantStage`), siempre como
usuario normal, sin mutex (puede haber varios) y con su registro en `Logs\stage.log`. Ese proceso carga una copia del asistente sobre
un **escritorio de mentira** de 720×405 al 125 % en (30000, 30000), lejos de cualquier monitor: barra de tareas con bandeja y reloj y
tres ventanas (Notas, Fotos y una Calculadora que se abre cuando se pide), que el asistente ve en su mundo y mueve y pulsa como
ventanas de verdad (`IWindowServices` del escenario). Nada se dibuja en la pantalla: el escenario se compone fuera de ella con
Direct2D y sus imágenes llegan a K-Desktop por memoria compartida. El asistente tiene su propia carpeta de datos, vacía al empezar y
borrada al acabar. La tubería es la misma que la de siempre (bienvenida con idioma, tema y ajustes; `config` de `assistantSettings`,
`general`, `common` y `personal`; `loaded`), más `showcase`/`showcaseList`/`showcaseDone` y estos dos:

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `stage` | K→A | `op` (`cursor`, `leave`, `down`, `up`, `reset`), `x`, `y` (píxeles del escenario) | El ratón del usuario sobre la imagen del escenario (botón izquierdo), o `reset`: las ventanas a su sitio. El escenario lo convierte en los eventos de las zonas de clic del asistente, que allí no son ventanas sino formas (entrar, salir, pulsar, soltar, clic, doble clic, arrastrar; soltar arrastrando fuera de él = salir) |
| `stageFrame` | A→K | `map`, `slot` (0 o 1), `width`, `height`, `seq` | Hay una imagen nueva: en la memoria compartida `Local\<map>` (la crea el escenario; K-Desktop solo la lee), mitad `slot` de dos, BGRA de arriba abajo. Una con `seq` más viejo que llega tarde no se enseña. Como mucho 30 por segundo y solo cuando algo cambia |

Sin `--pipe` y con `--record <carpeta>` el escenario **graba** (etapa 16.2): hace cada cosa de su escaparate con los ajustes por
defecto y graba de cada una que sale `done` un clip WebP animado sin pérdida (25 por segundo; 1,5 s de calma antes, 0,6 s de cola,
10 s como mucho; una entrada empieza con el escenario vacío y una salida acaba al irse), en inglés salvo `--language`. Con `--manifest
<assistant.json>` reescribe solo su `showcase`; sin él, deja `showcase.json` junto a los clips. Lo usa el `.targets` del SDK con
`-p:AssistantShowcase=true -p:KDesktopExe=…`.

## Reglas de la coreografía (§3.2 del plan)

- **Tiempo máximo por paso** (`step.timeoutMs`). Si el asistente no contesta `go`, `done` o `skip` a tiempo,
  K-Desktop ejecuta el paso y sigue. **Nunca se queda colgado.** Son 6 s; los pasos que mueven una ventana (`Snap`,
  `Center`, `MoveToMonitor`) tienen 10 s para remolcarla, salvo en `fast`.
- **`done` siempre se verifica:** K-Desktop aplica igualmente el resultado exacto (las operaciones son
  idempotentes). Si la ventana quedó a 3 px, la deja en su sitio.
- `target.result` lo calcula **K-Desktop**, que es quien sabe qué significa «Ajustar a la izquierda» o «Mover al
  monitor 2».
- Las acciones sin sitio (esperar, portapapeles, petición web…) llegan **sin** `target`.
- Un comando que llama a otro: sus pasos llegan con el mismo `run` y `depth` + 1.
- Los atajos, el móvil, la web, UDP, los eventos y la voz de «K-Desktop, …» **no** usan coreografía; con el adorno
  encendido mandan `observed`, que no los retrasa.
- Un `cancel` mientras hay un `run` en curso: el asistente manda `skip` con `all`; K-Desktop termina con `end` y
  `status: "cancelled"`. Si el usuario para al asistente desde K-Desktop (panel, Escape), K-Desktop corta el run él mismo.
- Si el proceso del asistente se cae a mitad de un run, lo que queda se ejecuta sin esperar.

## Códigos de error

| `code` | Cuándo |
|---|---|
| `badRequest` | Faltan campos o tienen un tipo incorrecto |
| `unknownCommand` | `run` con un id que no existe |
| `notAllowed` | Petición fuera de la lista cerrada, o hwnd que no es una ventana de nivel superior |
| `paused` | K-Desktop está en pausa |
| `busy` | Ya hay un `run` en curso con coreografía |
| `noModel` | `embed` sin EmbeddingGemma descargado |
| `timeout` | No se pudo completar a tiempo |
