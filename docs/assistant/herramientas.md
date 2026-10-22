# Herramientas JSON

Una herramienta JSON le da a los asistentes algo nuevo que hacer sin compilar nada: encender una luz de Home
Assistant, mandarte un Telegram, ejecutar uno de tus comandos con frases propias, encadenar varios pasos o lanzar un
script tuyo.

- **Dónde:** `%APPDATA%\K-Desktop 5\Assistant\Tools\`, un archivo `.json` por herramienta. Al guardar, añadir o
  quitar un archivo (o un script) se recargan todas en un momento, sin reiniciar nada.
- **Esquema:** [`esquemas/tool.schema.json`](esquemas/tool.schema.json). Pon `"$schema"` apuntando a él y tu editor te
  avisará de los errores mientras escribes. Si un archivo tiene errores, K-Desktop te lo dice en un aviso al guardarlo y
  esa herramienta no se carga (las demás sí).
- **Ejemplos:** en [`herramientas/`](herramientas/) hay uno de cada tipo.

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
