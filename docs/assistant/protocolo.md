# Protocolo de la tubería K-Desktop ↔ asistente

Versión del protocolo: **3** (la 2, de la etapa 1.11, lleva el id del asistente en los mensajes: puede haber varios a la vez; la 3,
de la etapa 2.3, deja el menú del clic derecho a K-Desktop —`menu`— y añade `extraState` y `extraKeys`).

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
- **A→K:** quién lo dice (`loaded`, `status`, `listen`, `talk`, `run`, `keys`, `log`, `chat`, `panel`, `notify`, `open`,
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
| `bye` | K→A | `reason` (`disable`, `exit`, `restart`) | Ciérrate (con la salida del personaje, máx. 3 s) |
| `error` | ambas | `id` (si respondía a una petición), `code`, `message` | Petición inválida o fallida |

### Configuración y estado de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `config` | K→A | `section`, `value`, `assistant` (solo en `assistantSettings`) | Cambió algo en Opciones → Asistente. Secciones: `general` (de momento `sound`, sí/no, y `volume`, 0…1: el sonido de los asistentes, que mezcla el host), `brain` (`provider`: `ollama`, `llamaSwap`, `openai` o `none`; `endpoint`, siempre con la dirección que se usa; `apiKey`, en claro; `model`, vacío = automático), `voice`, `tools`, `privacy`, `assistantSettings` (ajustes de ese asistente, según su `settings.json`: todos sus valores, cada vez que se cambia uno en Opciones). La bienvenida trae las secciones del módulo en `config` |
| `pause` | K→A | `paused` | Se ha pausado o reanudado K-Desktop: en pausa no se atienden órdenes |
| `load` | K→A | `assistant` (id), `version`, `path` (carpeta del paquete), `data` (su carpeta de datos), `entrance` (`startup`, `switched`) | Pone este asistente en el escritorio, junto a los que ya haya. Si ya estaba (otra versión: `switched`), el nuevo código se carga y después el viejo sale y entra el nuevo |
| `unload` | K→A | `assistant`, `reason` (`disabled`, `uninstalling`, `hiding`, `reloading`) | Quita ese asistente del escritorio (con su salida); los demás siguen. Sin ninguno, el módulo sigue sin cuerpo. `reloading`: vuelve enseguida con un `load` (p. ej. tras «Empezar de cero», que borra su carpeta de datos) |
| `loaded` | A→K | `assistant`, `ok`, `error` (si falló) | Resultado de `load` |

### Entradas: lo que se le dice al asistente

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `listening` | K→A | `assistant`, `active` | Se oyó su palabra (o se pulsó su tecla): está escuchando / ya no |
| `heard` | K→A | `assistant`, `text`, `source` (`voice`, `panel`, `bubble`, `phone`, `command`) | Una orden para ese asistente (el de la palabra, el elegido en el panel o, si no va dirigido a nadie, el de Opciones → Asistentes) |
| `cancel` | K→A | `assistant`, `source` (`voice`, `escape`, `panel`) | «Lander, para» o Escape: cancela lo que esté haciendo (`skip` en los pasos que queden). Escape va al que esté haciendo algo |
| `listen` | A→K | `assistant`, `seconds` (máx. 10), `expect` (lista opcional de respuestas previstas) | Escucha sin palabra de activación (confirmaciones y seguir la conversación) |
| `talk` | A→K | `assistant`, `active` | Clic mantenido sobre el personaje: empieza / termina de grabar (y pasa a ser «el último con el que hablaste») |

### Comandos de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `match` | A→K | `id`, `text` | Busca comandos para un texto, sin ejecutar |
| `matched` | K→A | `id`, `parts` (lista), `unmatched` (trozos sin casar), `score` | Resultado. Cada parte: `text`, `candidates` (lista de `{ command, name, score }`, mejor primero) o `media` (orden de música integrada) |
| `run` | A→K | `assistant`, `run` (id de ejecución), `command` (id), `choreography` (sí/no) | Ejecuta un comando. Con coreografía, K-Desktop manda cada paso a ese asistente y espera |
| `step` | K→A | `run`, `index`, `count`, `depth`, `kind`, `op`, `summary`, `target` (opcional), `timeoutMs` | El paso que va a ejecutar. `target`: `hwnd`, `process`, `title`, `rect`, `result` (dónde quedará, si se sabe), `state` (`normal`, `maximized`, `minimized`), `elevated` |
| `go` | A→K | `run`, `index` | «Hazlo ahora» (en el impacto del disparo) |
| `done` | A→K | `run`, `index` | «Ya lo he hecho yo» (ventana arrastrada). K-Desktop lo verifica y aplica el resultado exacto igualmente |
| `skip` | A→K | `run`, `index` | No ejecutes este paso (cancelado desde el asistente) |
| `result` | K→A | `run`, `index`, `status` (`ok`, `failed`, `skipped`), `message` | Resultado del paso |
| `end` | K→A | `run`, `status` (`ok`, `failed`, `cancelled`), `messages` (lista) | Fin de la ejecución |
| `media` | A→K | `order` (`play`, `pause`, `next`, `previous`, `volumeUp`, `volumeDown`, `mute`, …) | Orden de música integrada de K-Desktop (salida de `match`) |
| `say` | K→A | `assistant`, `text` | Acción «Asistente: decir» (al de la acción o, si no dice cuál, al de Opciones → Asistentes) |
| `extra` | K→A | `assistant`, `action` (id del extra, p. ej. `lander.manual`) | Acción «Asistente: acción», o un botón de sus ajustes o de su panel. Si el asistente no estaba en el escritorio, K-Desktop lo pone, espera a su `loaded` y entonces lo manda (si tarda más de 20 s, no): puede llegar antes de que el asistente tenga su mundo listo, y entonces debe guardarlo y hacerlo al arrancar |
| `extraKeys` | K→A | `assistant`, `keys` (objeto: id del extra → atajo como lo enseña K-Desktop, `"CTRL + ALT + L"`; los que no tienen, no salen) | Los atajos de los comandos que ejecutan sus extras. Llega tras cada `load` y cada vez que cambian los comandos. El SDK lo da con `IAssistantHost.ExtraHotkey` (p. ej. para decir «pulsa Ctrl+Alt+L») |
| `observed` | K→A | `assistant`, `source` (`hotkey`, `menu`, `voice`, `web`, `udp`, `event`), `command`, `steps` | (Opcional, desactivado por defecto) un comando lanzado por otra vía, para adornarlo (al de Opciones → Asistentes) |

### Ventanas, voz y teclado

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `window` | A→K | `id`, `hwnd`, `op` (`move`, `minimize`, `maximize`, `restore`, `activate`, `close`), `rect` (para `move`) | Operación sobre una ventana de administrador (el asistente no puede tocarla) |
| `windowDone` | K→A | `id`, `ok` | Resultado |
| `embed` | A→K | `id`, `texts` (lista, máx. 64) | Vectores de significado con el EmbeddingGemma de K-Desktop |
| `embedded` | K→A | `id`, `vectors` (lista de listas de números), `model` | Resultado (vectores normalizados). Si K-Desktop no tiene el modelo: `error` con `code: "noModel"` |
| `keys` | A→K | `assistant`, `capture` (lista de teclas, nombres de `Key` de WPF: `Up`, `A`, `Escape`…; vacía = soltar) | Que K-Desktop se trague esas teclas cuando se pulsan sin Ctrl, Alt ni Win (así los atajos siguen funcionando) y se las reenvíe a ese asistente (juego del Lander). Los modificadores no se capturan. En pausa, o si el asistente sale o el proceso se cae, K-Desktop las suelta solo |
| `key` | K→A | `assistant`, `key`, `down` | Una tecla capturada bajó o subió (la repetición automática no se manda) |
| `watchWheel` | A→K | `watch` | Que K-Desktop mande cada giro de la rueda (`wheel`) o deje de hacerlo. El host lo pide mientras algún asistente lo quiera (`IPointer.WatchWheel`) |
| `wheel` | K→A | `x`, `y`, `delta` (120 por muesca; positivo = arriba o a la derecha), `horizontal`, `hwnd` (la ventana de nivel superior bajo el cursor) | Un giro de la rueda. Sale del gancho de K-Desktop, que la ve también sobre ventanas de administrador |
| `fastScroll` | K→A | `strength` (0…1, positivo = hacia abajo), `x`, `y` | El usuario ha girado la rueda con fuerza (varias muescas seguidas; luego descansa 1,2 s). Se manda siempre: es raro |

### El cerebro (Opciones → Asistente → Cerebro)

Lo hace todo el proceso del asistente (sin administrador): buscar el servidor, instalar Ollama con winget y descargar modelos. K-Desktop solo enseña y pide.

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `brain` | K→A | `op` (`check`, `scan`, `installOllama`, `startOllama`, `downloadPage`, `pull`, `cancel`, `unload`), `model` (para `pull`) | Mirar el servidor otra vez, buscar en los sitios habituales de este PC, instalar Ollama, abrirlo, abrir su página de descarga en el navegador del usuario, descargar un modelo, cancelar la descarga o liberar la gráfica. Se contesta con `brainState` |
| `brainState` | A→K | `state` (`off`, `checking`, `ready`, `unreachable`, `noModels`, `modelMissing`, `error`), `provider`, `endpoint`, `model` (el que se usa), `automatic`, `models` (`name`, `billions`, `sizeBytes`, `tools`), `loaded` (los que están en memoria), `message` (qué falló, en el idioma del usuario), `ollama` (`installed`, `canInstall`; solo con Ollama en este PC), `job` (`kind`: `pull` o `install`; `state`: `running`, `done`, `failed`, `cancelled`; `model`, `completed`, `total`, `error`), `suggested` (`name`, `downloadGb`: el modelo para descargar según la memoria de vídeo), `found` (otros servidores encontrados: `provider`, `endpoint`, `models`), `videoMemoryGb` | Cómo está el cerebro: tras cada cambio y cada `brain`. Durante una descarga, como mucho 4 por segundo |

### Del asistente al panel de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `status` | A→K | `assistant` (opcional), `state` (`idle`, `listening`, `thinking`, `speaking`, `awaitingConfirmation`, `busy`, `sleeping`), `model`, `modelLoaded`, `busy`, `memoryMb`, `lastError` | Estado para el panel: con `assistant`, el de ese asistente; sin él, el del proceso (modelo, memoria) |
| `log` | A→K | `assistant`, `entry`: `at`, `heard`, `route` (`rule`, `kdesktop`, `ai`), `did`, `ok`, `undo` (id, si se puede deshacer) | Una línea del registro de actividad |
| `chat` | A→K | `assistant`, `role` (`user`, `assistant`), `text`, `final` (si es parcial, llegan más con el mismo `turn`), `turn` | Conversación del panel |
| `panel` | A→K | `data`, `assistant` (id, lo pone el host) | Datos del panel propio del asistente (los `bind` de su `panel.json` y las galerías con `source` de su `settings.json`). Se **fusionan** con lo publicado antes: los objetos clave a clave; lo demás (números, textos, listas) sustituye. El host manda como mucho cuatro por segundo por asistente y K-Desktop lo olvida en cada `load` de ese asistente |
| `notify` | A→K | `assistant` (lo pone el host), `title`, `text` | Aviso de K-Desktop (logro desbloqueado…) |
| `open` | A→K | `page` (`assistant`, `assistantSettings`, `assistantVoice`, …), `assistant` (opcional) | Abre esa página de Opciones (menú del clic derecho → *Ajustes*), la de ese asistente si lo dice. `assistant` es Panel → Asistente |
| `menu` | A→K | `assistant`, `x`, `y` (píxeles de pantalla) | Clic derecho sobre el asistente: K-Desktop enseña su menú allí, como los demás menús de K-Desktop (panel, ajustes, sus extras con su icono y su atajo, esconder). Sin conexión, el host enseña uno nativo con lo mismo |
| `extraState` | A→K | `assistant` (lo pone el host), `extra`, `state` (`shown`, `disabled`, `hidden`) | Enseña, atenúa o esconde uno de sus extras en su menú y en los botones de su panel (`IAssistantHost.SetExtraState`). Tras cada `load` todos vuelven a `shown`. Los comandos y la voz lo siguen mandando: el asistente decide qué hacer |
| `hide` | A→K | `assistant` | Menú del clic derecho → *Esconder*: K-Desktop quita ese asistente del escritorio (`unload` con `hiding`); los demás siguen |
| `undo` | K→A | `assistant`, `undo` (id) | Deshacer una entrada de su registro desde el panel |

## Reglas de la coreografía (§3.2 del plan)

- **Tiempo máximo por paso** (`step.timeoutMs`). Si el asistente no contesta `go`, `done` o `skip` a tiempo,
  K-Desktop ejecuta el paso y sigue. **Nunca se queda colgado.**
- **`done` siempre se verifica:** K-Desktop aplica igualmente el resultado exacto (las operaciones son
  idempotentes). Si la ventana quedó a 3 px, la deja en su sitio.
- `target.result` lo calcula **K-Desktop**, que es quien sabe qué significa «Ajustar a la izquierda» o «Mover al
  monitor 2».
- Las acciones sin sitio (esperar, portapapeles, petición web…) llegan **sin** `target`.
- Un comando que llama a otro: sus pasos llegan con el mismo `run` y `depth` + 1.
- Los atajos, el móvil, la web, UDP, los eventos y la voz de «K-Desktop, …» **no** usan coreografía.
- Un `cancel` mientras hay un `run` en curso: el asistente contesta `skip` a los pasos que lleguen; K-Desktop
  termina con `end` y `status: "cancelled"`.

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
