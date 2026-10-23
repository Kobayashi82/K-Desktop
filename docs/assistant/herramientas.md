# Herramientas JSON

Una herramienta JSON le da a los asistentes algo nuevo que hacer sin compilar nada: encender una luz de Home
Assistant, mandarte un Telegram, ejecutar uno de tus comandos con frases propias, encadenar varios pasos o lanzar un
script tuyo.

- **Dónde:** `%APPDATA%\K-Desktop 5\Assistant\Tools\`, un archivo `.json` por herramienta. Al guardar, añadir o
  quitar un archivo (o un script) se recargan todas en un momento, sin reiniciar nada.
- **Esquema:** [`schemas/tool.schema.json`](schemas/tool.schema.json). Pon `"$schema"` apuntando a él y tu editor te
  avisará de los errores mientras escribes. Si un archivo tiene errores, K-Desktop te lo dice en un aviso al guardarlo y
  esa herramienta no se carga (las demás sí).
- **Ejemplos:** en [`herramientas/`](herramientas/) hay uno de cada tipo.
- **En Opciones → Módulo Asistente → Herramientas** están todas (las incluidas, las tuyas y las de cada servidor MCP),
  con su interruptor, cuándo preguntan y «Probar». Dentro: **Servidores MCP**, **Tus herramientas** (los archivos, sus
  errores y «Nueva» desde una plantilla) y **Secretos**.

## Lo común

```jsonc
{
  "name": "luz_salon",                       // minúsculas, números y _ (mcp_ está reservado)
  "type": "http",                            // http · kdesktop · routine · script
  "description": "Turns the living room light on or off.",   // para el modelo
  "examples": ["enciende la luz del salón", "apaga el salón"],
  "parameters": {
    "estado": { "type": "string", "enum": ["on", "off"], "description": "on or off" }
  },
  "intent": "poner la luz del salón en {{estado}}",          // lo que verás en la actividad y al preguntar
  "say": { "ok": "Luz del salón: {{estado}}.", "error": "La base no responde." },
  "risk": "safe",                            // safe · askOnce · askAlways
  "readsOnly": false,
  "gesture": "waitForWindow",                // opcional: waitForWindow · tray · clock · typing
  "timeout": 15,
  "enabled": true
}
```

- **`description`** es para el modelo: qué hace y cuándo usarla. En inglés los modelos la siguen mejor, pero vale
  cualquier idioma.
- **`examples`**: frases que la piden. Con ellas se elige qué herramientas llegan al modelo en cada orden (solo las 3–5
  que vienen a cuento). Además, si dices **exactamente** una de ellas y la herramienta no necesita que el modelo le dé
  nada, se hace al momento sin pasar por el modelo («buenas noches» → la rutina).
- **`parameters`**: lo que el modelo tiene que darle (`string`, `number`, `integer`, `boolean`; con `enum`, `default`,
  `minimum`, `maximum`). Por defecto son obligatorios los que no tienen `default`; `required` lo cambia. Si el modelo
  da un valor que no vale, se le dice y puede corregirlo.
- **`say`**: lo que dice el asistente al acabar. Sin él, lo dice el modelo con sus palabras.
- **`readsOnly`**: solo consulta algo (una temperatura, un estado). El modelo contesta con lo que devuelve, y el banco de
  pruebas la ejecuta de verdad.
- **`gesture`** (opcional): lo que hace el personaje mientras la herramienta trabaja. `waitForWindow` = espera la
  ventana de lo que abre; `tray` = va a la bandeja (música, volumen…); `clock` = deja una marca junto al reloj; `typing` =
  teclea en la ventana de delante. Es solo adorno: la herramienta no espera al gesto. En las de tipo `kdesktop` no hace
  falta, porque tu comando ya se ve con sus propios gestos.
- **`risk`**: `safe` no pregunta; `askOnce` pregunta la primera vez (hasta «Sí, y no vuelvas a preguntar»);
  `askAlways` pregunta siempre. Con «Preguntar antes de hacer algo» en «Nunca», ninguna pregunta… salvo los scripts nuevos
  (ver abajo).

### Huecos `{{…}}`

| Hueco | Qué es | Dónde |
|---|---|---|
| `{{parámetro}}` | El valor que dio el modelo | En todas partes |
| `{{secret:nombre}}` | Un secreto (ver abajo) | URL, cabeceras, cuerpo, argumentos y entorno de un script |
| `{{result}}` | La respuesta (o la parte elegida con `pick`) | `say` |
| `{{result.campo}}` | Un campo de la respuesta JSON (`attributes.temperature`, `items.0.name`) | `say` |
| `{{status}}` | Código HTTP o de salida del script | `say` |

En un `body` JSON, un texto que es **solo** `"{{parámetro}}"` se sustituye con su tipo: un número sigue siendo número.

## Secretos

Van en `%APPDATA%\K-Desktop 5\Assistant\secrets.json`:

```json
{ "ha_token": "eyJhbGciOi…", "telegram_token": "123456:ABC…" }
```

Puedes escribirlos en claro: la primera vez que se lee el archivo, cada valor se cifra con tu usuario de Windows
(DPAPI) y el archivo queda con `dpapi:…`. **El modelo nunca ve un secreto:** solo se pone al enviar la petición, y si
una respuesta lo contiene se tapa con `•••`. Una copia de seguridad restaurada en otro PC no puede leerlos: hay que
volver a escribirlos.

## Tipos

### `http`: cualquier API

```jsonc
"request": {
  "method": "POST",                                   // GET por defecto
  "url": "http://homeassistant.local:8123/api/services/light/turn_{{estado}}",
  "headers": { "Authorization": "Bearer {{secret:ha_token}}" },
  "query": { "lang": "es" },                         // se añade a la URL
  "body": { "entity_id": "light.salon" },             // o "bodyText": "…" con "contentType"
  "pick": "state"                                     // la parte de la respuesta que se le da al modelo
}
```

Los valores que se meten en la URL se codifican. Un código de error (4xx, 5xx) es un fallo y dice `say.error`.

### `kdesktop`: tus comandos con frases propias

```jsonc
"command": "Modo cine"          // el nombre exacto de uno de tus comandos; admite {{parámetros}}
```

Se busca por el nombre exacto: si no hay ninguno que se llame así, no se cambia por otro parecido. K-Desktop pregunta
antes de lo arriesgado como con cualquier comando.

### `routine`: varios pasos

```jsonc
"steps": [
  { "tool": "luz_salon", "arguments": { "estado": "off" }, "continueOnError": true },
  { "wait": 1 },
  { "command": "Apagar pantalla" }
]
```

Cada paso es otra herramienta (incluida o JSON, nunca otra rutina), uno de tus comandos o una espera de hasta 30 s. Cada
herramienta pregunta y queda en la actividad como si se la pidieras. Un paso que falla para la rutina, salvo con
`continueOnError`.

### `script`: tus scripts

```jsonc
"script": {
  "file": "comprobar_conexion.ps1",                  // relativa a la carpeta Tools, o completa
  "args": ["{{host}}"],
  "env": { "API_KEY": "{{secret:mi_clave}}" }        // los secretos, mejor por el entorno
}
```

| Extensión | Con qué se ejecuta |
|---|---|
| `.ps1` | Windows PowerShell, sin perfil |
| `.bat`, `.cmd` | cmd. Un argumento con `" & \| < > ^ % !` se rechaza: cmd los volvería a leer |
| `.py` | `uv run --script` si tienes uv (lee las dependencias que declara el script), si no `py -3` o `python` |
| `.js` | Node.js |
| `.exe` | Tal cual |

Se ejecutan como tu usuario normal (nunca como administrador), sin ventana, con 30 s como mucho. Lo que escriben es la
respuesta; un código de salida distinto de 0 es un fallo.

**Un script siempre pregunta antes de su primera ejecución, aunque tengas «Preguntar antes» en «Nunca».** Con «Sí, y no
vuelvas a preguntar» queda aprobado; si cambias el script o su archivo `.json`, vuelve a preguntar. Así, un script que
aparezca en la carpeta sin que lo sepas nunca se ejecuta sin que lo veas.

## Servidores MCP

Un servidor MCP es un programa (o una dirección) que da varias herramientas de golpe: los mismos que usan Claude Desktop,
Open WebUI con mcpo, etc. Están en `%APPDATA%\K-Desktop 5\Assistant\mcp.json`, con el formato `mcpServers` de siempre
([esquema](schemas/mcp.schema.json)), y se manejan desde **Opciones → Herramientas → Servidores MCP**:

- **Importar…** lee el archivo de otro programa (el `config.json` de mcpo, el de Claude Desktop) y te deja elegir.
  Las variables que parecen secretas (`*_TOKEN`, `*_KEY`, `*_SECRET`, `*PASSWORD*`, `CHAT_ID`…) pasan a tus secretos
  cifrados como `{{secret:<servidor>.<VARIABLE>}}`. Los que ya hacen los asistentes (archivos, pantalla, portapapeles,
  memoria, hora, leer webs) salen desmarcados con el motivo.
- **Añadir servidor** / **Editar**: un programa (`node`, `npx`, `uvx`, un `.exe`…, sus argumentos y su carpeta) o una
  dirección HTTP; sus variables o cabeceras, cada una normal o secreta; cuánto puede tardar una llamada (60 s) y tras
  cuánto tiempo sin usarse se para (5 min).

Cómo funcionan:

- Los arranca el proceso de los asistentes, **como tu usuario normal** (nunca como administrador), solo cuando hace falta
  una de sus herramientas, y se paran solos tras el rato sin usarse. Si los asistentes se cierran, se cierran con ellos.
- Lo que el programa escribe en stderr va a `Logs\mcp-<servidor>.log`.
- Sus herramientas se llaman `mcp_<servidor>_<herramienta>` y se recuerdan entre arranques (`mcp-cache.json`): elegir
  cuáles llegan al modelo en cada orden nunca arranca un servidor. Uno nuevo o cambiado se arranca una vez, en segundo
  plano, para conocerlas.
- Por defecto preguntan la primera vez (🟡), salvo las que el servidor dice que solo leen; con «Preguntar antes» en
  «Nunca», no preguntan. Cada una se puede apagar o cambiar en Opciones.
- Si falta lo que necesita (Node.js para `npx`, uv para `uvx`, Python), lo dice al probarlo.

## Compartir con otros programas

Al revés que lo anterior: **Opciones → Herramientas → Compartir** hace de los asistentes un servidor MCP para Claude
Desktop, VS Code, Open WebUI (con mcpo) o cualquier programa con MCP. Se enciende ahí («Compartir las herramientas») y
«Copiar la configuración» deja en el portapapeles lo que hay que pegar en el programa:

```json
{
  "mcpServers": {
    "k-desktop": {
      "command": "D:\…\KDesktop.exe",
      "args": ["--mcp"],
      "env": { "KDESKTOP_MCP_KEY": "…" }
    }
  }
}
```

Cómo funciona:

- El programa arranca `KDesktop.exe --mcp`, que solo hace de puente: pasa lo que llega por stdin a una tubería del
  proceso de los asistentes (de tu usuario, sin administrador) y lo que vuelve a stdout. No abre otro K-Desktop ni pide
  permisos. K-Desktop tiene que estar abierto con el módulo Asistente encendido.
- La tubería pide la clave antes de nada; «Cambiar la clave» deja fuera a los programas que tenían la anterior. Al
  proceso solo le llega el SHA-256 de la clave.
- **Qué pueden usar:** «Solo lo que consulta» (la hora, cuentas, fechas, el tiempo, convertir, buscar archivos o en
  internet, leer webs, el estado del PC, qué suena, buscar y explicar tus comandos) o «Todo, preguntándote antes». Con
  «Todo», lo que cambia algo **pregunta siempre en este PC** con la ventanita de siempre (««Claude» quiere poner un
  temporizador de 5 min. ¿Lo hago?»), aunque en Opciones tengas puesto que los asistentes no pregunten, hasta que digas
  «Sí, y no vuelvas a preguntar». Las herramientas apagadas y las de tus propios servidores MCP no se comparten.
- Cada llamada sale en la actividad del panel con el nombre del programa, y Opciones enseña quién está conectado.
