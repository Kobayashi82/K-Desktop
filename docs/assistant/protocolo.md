# Protocolo de la tubería K-Desktop ↔ asistente

Versión del protocolo: **1**.

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

## Ciclo de vida

```
asistente                               K-Desktop
   │ ── hello ─────────────────────────► │  comprueba el cliente y la versión
   │ ◄──────────────────────── welcome ─ │  (o reject y cierra)
   │ ◄─────────────────────────── load ─ │  asistente activo (si hay)
   │ ── status ────────────────────────► │
   │        … mensajes …                 │
   │ ◄──── ping (cada 5 s sin tráfico) ─ │
   │ ── pong ──────────────────────────► │
   │ ◄──────────────────────────── bye ─ │  apagar el módulo / salir
   │   (cierra en ≤ 3 s; si no, K-Desktop termina el proceso)
```

- **Latido:** quien lleve 5 s sin recibir nada manda `ping`; el otro contesta `pong`. Sin respuesta en 15 s, la
  conexión se da por muerta. Si muere el asistente, K-Desktop lo relanza con espera creciente (1, 2, 4, 8… hasta
  60 s); **3 caídas seguidas** con el mismo asistente cargado (menos de 2 min entre una y otra) → lo descarga, lo
  marca como desactivado y lo dice en el panel.
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
| `config` | K→A | `section`, `value` | Cambió algo en Opciones → Asistente. Secciones: `general`, `brain`, `voice`, `tools`, `privacy`, `assistantSettings` (ajustes del asistente activo, según su `settings.json`: todos sus valores, cada vez que se cambia uno en Opciones) |
| `pause` | K→A | `paused` | Se ha pausado o reanudado K-Desktop: en pausa no se atienden órdenes |
| `load` | K→A | `assistant` (id), `version`, `path` (carpeta del paquete), `data` (su carpeta de datos), `entrance` (`startup`, `switched`) | Carga este asistente (y descarga el anterior con su salida) |
| `unload` | K→A | `reason` (`disabled`, `uninstalling`, `hiding`, `reloading`) | Descarga el asistente activo y sigue en modo sin cuerpo. `reloading`: vuelve enseguida con un `load` (p. ej. tras «Empezar de cero», que borra su carpeta de datos) |
| `loaded` | A→K | `assistant`, `ok`, `error` (si falló) | Resultado de `load` |

### Entradas: lo que se le dice al asistente

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `listening` | K→A | `active` | Se oyó su palabra (o se pulsó su tecla): está escuchando / ya no |
| `heard` | K→A | `text`, `source` (`voice`, `panel`, `bubble`, `phone`, `command`) | Una orden para el asistente |
| `cancel` | K→A | `source` (`voice`, `escape`, `panel`) | «Lander, para» o Escape: cancela lo que esté haciendo (`skip` en los pasos que queden) |
| `listen` | A→K | `seconds` (máx. 10), `expect` (lista opcional de respuestas previstas) | Escucha sin palabra de activación (confirmaciones y seguir la conversación) |
| `talk` | A→K | `active` | Clic mantenido sobre el personaje: empieza / termina de grabar |

### Comandos de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `match` | A→K | `id`, `text` | Busca comandos para un texto, sin ejecutar |
| `matched` | K→A | `id`, `parts` (lista), `unmatched` (trozos sin casar), `score` | Resultado. Cada parte: `text`, `candidates` (lista de `{ command, name, score }`, mejor primero) o `media` (orden de música integrada) |
| `run` | A→K | `run` (id de ejecución), `command` (id), `choreography` (sí/no) | Ejecuta un comando. Con coreografía, K-Desktop manda cada paso y espera |
| `step` | K→A | `run`, `index`, `count`, `depth`, `kind`, `op`, `summary`, `target` (opcional), `timeoutMs` | El paso que va a ejecutar. `target`: `hwnd`, `process`, `title`, `rect`, `result` (dónde quedará, si se sabe), `state` (`normal`, `maximized`, `minimized`), `elevated` |
| `go` | A→K | `run`, `index` | «Hazlo ahora» (en el impacto del disparo) |
| `done` | A→K | `run`, `index` | «Ya lo he hecho yo» (ventana arrastrada). K-Desktop lo verifica y aplica el resultado exacto igualmente |
| `skip` | A→K | `run`, `index` | No ejecutes este paso (cancelado desde el asistente) |
| `result` | K→A | `run`, `index`, `status` (`ok`, `failed`, `skipped`), `message` | Resultado del paso |
| `end` | K→A | `run`, `status` (`ok`, `failed`, `cancelled`), `messages` (lista) | Fin de la ejecución |
| `media` | A→K | `order` (`play`, `pause`, `next`, `previous`, `volumeUp`, `volumeDown`, `mute`, …) | Orden de música integrada de K-Desktop (salida de `match`) |
| `say` | K→A | `text` | Acción «Asistente: decir» |
| `extra` | K→A | `action` (id del extra, p. ej. `lander.manual`) | Acción «Asistente: acción», o un botón de sus ajustes o de su panel |
| `observed` | K→A | `source` (`hotkey`, `menu`, `voice`, `web`, `udp`, `event`), `command`, `steps` | (Opcional, desactivado por defecto) un comando lanzado por otra vía, para adornarlo |

### Ventanas, voz y teclado

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `window` | A→K | `id`, `hwnd`, `op` (`move`, `minimize`, `maximize`, `restore`, `activate`, `close`), `rect` (para `move`) | Operación sobre una ventana de administrador (el asistente no puede tocarla) |
| `windowDone` | K→A | `id`, `ok` | Resultado |
| `embed` | A→K | `id`, `texts` (lista, máx. 64) | Vectores de significado con el EmbeddingGemma de K-Desktop |
| `embedded` | K→A | `id`, `vectors` (lista de listas de números), `model` | Resultado (vectores normalizados). Si K-Desktop no tiene el modelo: `error` con `code: "noModel"` |
| `keys` | A→K | `capture` (lista de teclas; vacía = soltar) | Que K-Desktop se trague esas teclas y las reenvíe (juego del Lander) |
| `key` | K→A | `key`, `down` | Una tecla capturada |

### Del asistente al panel de K-Desktop

| `t` | Dir. | Campos | Qué es |
|---|---|---|---|
| `status` | A→K | `state` (`idle`, `listening`, `thinking`, `speaking`, `awaitingConfirmation`, `busy`, `sleeping`), `model`, `modelLoaded`, `busy`, `memoryMb`, `lastError` | Estado para el panel |
| `log` | A→K | `entry`: `at`, `heard`, `route` (`rule`, `kdesktop`, `ai`), `did`, `ok`, `undo` (id, si se puede deshacer) | Una línea del registro de actividad |
| `chat` | A→K | `role` (`user`, `assistant`), `text`, `final` (si es parcial, llegan más con el mismo `turn`), `turn` | Conversación del panel |
| `panel` | A→K | `data`, `assistant` (id, lo pone el host) | Datos del panel propio del asistente (los `bind` de su `panel.json` y las galerías con `source` de su `settings.json`). Se **fusionan** con lo publicado antes: los objetos clave a clave; lo demás (números, textos, listas) sustituye. El host manda como mucho cuatro por segundo y K-Desktop lo olvida en cada `load` |
| `notify` | A→K | `title`, `text` | Aviso de K-Desktop (logro desbloqueado…) |
| `open` | A→K | `page` (`assistant`, `assistantSettings`, `assistantVoice`, …) | Abre esa página de Opciones (menú del clic derecho → *Ajustes*). `assistant` es Panel → Asistente |
| `hide` | A→K | — | Menú del clic derecho → *Esconder*: K-Desktop quita el asistente del escritorio (`unload` con `hiding`) y el módulo sigue sin cuerpo |
| `undo` | K→A | `undo` (id) | Deshacer una entrada del registro desde el panel |

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
