# Ideas K-Desktop: acciones, eventos y herramientas

Sacado del código (`ActionCatalog.cs`, `EventCatalog.cs`, `EventRule.cs`, `KDesktop.Assistant.Tools`) sin ejecutarlo. Si alguna ya existe, se descarta.

## Prioridad

- [ ] Herramienta para que los asistentes hagan acciones de K-Desktop directamente
- [ ] Variables para pasar resultados entre acciones
- [ ] Evento por nivel de batería
- [ ] Tipo de cambio en el evento «Archivo»

## Acciones

- [ ] **Multimedia** (reproducir/pausar, siguiente, anterior) como acción propia. Ahora se hace con «Enviar entrada» y teclas; los asistentes ya tienen `control_media`.
- [ ] **Forzar cierre de un programa**: «Controlar aplicación» solo tiene activar, cerrar y esperar. Los asistentes ya tienen `end_process`.
- [ ] **Escritorios virtuales**: cambiar de escritorio y mover la ventana a otro (encaja con «Ventana»).
- [ ] **Pasar resultados de una acción a la siguiente**: solo hay `{fecha}`, `{hora}` y `{portapapeles}`. Lo que sale del texto en pantalla, el dictado o una petición HTTP solo se encadena por el portapapeles. Añadir variables (`{resultado}` o con nombre).
- [ ] **«Si…» con «si no»**: ahora para el comando o se salta N acciones. Añadir «si no, ejecutar otro comando».
- [ ] **Pedir confirmación** («¿Seguro? Sí/No») antes de seguir, útil delante de Energía o Archivos. Comprobar antes que «Mostrar mensaje» no lo hace ya.

## Eventos

- [ ] **Nivel de batería** («por debajo del 20 %»): ahora solo hay batería o corriente.
- [ ] **«Archivo» con tipo de cambio**: elegir entre creado, modificado o borrado (ahora solo carpeta, filtro y subcarpetas).
- [ ] **Amanecer y atardecer**: casa con «Tema de Windows» (claro de día, oscuro de noche).
- [ ] **Pantalla conectada o desconectada**: cambiar de proyección o de audio al enchufar el monitor o la tele.
- [ ] **«Solo si» más completo**: ahora solo hora, días y un programa abierto o cerrado. Reutilizar las condiciones de la acción «Si…» (red, corriente, uso del equipo, archivo existe…) para que las dos funcionen igual.

## Herramientas de los asistentes

- [ ] **Hacer acciones de K-Desktop directamente** (volumen, ventana, captura, bloquear, salida de audio, wifi, tema…). Ahora solo con `run_command`, si hay un comando tuyo para eso. `ActionRisk.cs` ya clasifica el riesgo de todas las acciones, así que los permisos están medio hechos.
- [ ] **Archivos**: hay `delete_file` y `move_file`, pero no copiar, crear carpeta ni comprimir (la acción «Archivos» sí lo hace).
- [ ] **Crear eventos de cualquier tipo**: `schedule` solo crea «A una hora». Por ejemplo: «cada vez que abra Steam, baja el volumen al 30 %». Que el evento con su comando quede abierto en el editor para revisarlo antes de guardar.
- [ ] **Portapapeles**: leerlo y escribirlo de forma explícita, si `transform_text` no lo cubre ya.
- [ ] **Borrar y editar notas**: solo hay `save_note` y `find_notes` («quita la leche de la lista de la compra», «borra la nota del fontanero»)
- [ ] **Escribir un archivo**: «guárdame este resumen en un `.md` en el escritorio». Con aprobación y sin sobrescribir sin preguntar
- [ ] **Listar lo que vigilan**: hay `notify_when` y `stop_watching`, pero no «¿qué me estás vigilando?»
- [ ] **Mandar algo al móvil** con la app Android emparejada: «mándame este enlace al móvil», «avísame en el móvil cuando acabe la descarga»
- [ ] **Consultar el Registro y las estadísticas**:
  - «¿qué comandos ejecuté ayer?»
  - «¿por qué no saltó el evento de las 9?» (el Registro guarda el motivo)
  - «¿cuántas veces he usado el lanzador esta semana?»
- [ ] **Explicar y diagnosticar un comando o evento**: después de `describe_command`, «¿por qué no funciona mi comando de Spotify?», revisando sus acciones y los errores del Registro
- [ ] **Instalar y actualizar programas con winget**: «instálame VLC», «¿hay actualizaciones?». Siempre con aprobación
- [ ] **Red**: «¿va internet?», «¿está encendido el NAS?», «¿cuál es mi IP?» (la condición «Equipo accesible» de «Si…» ya lo hace por dentro)
- [ ] **Ventanas abiertas**: «¿qué tengo abierto?», «pon el navegador y VS Code uno al lado del otro»
- Prioridad: borrar y editar notas, listar vigilancias, consultar el Registro, mandar al móvil y escribir archivos

## Funciones nuevas

### Expansor de texto
- [ ] Escribir una abreviatura (`;firma`, `;dir`) y que se sustituya por un texto
- [ ] Con variables dentro (`{fecha}`, `{hora}`, `{portapapeles}`)
- [ ] Hoy K-Board reasigna teclas a texto, pero no abreviaturas

### Historial del portapapeles
- [ ] Guardar lo último copiado: textos, imágenes y rutas de archivos (Win+V se queda en unas 25 cosas y se borra al reiniciar salvo lo fijado)
- [ ] Atajo propio, y también desde el lanzador o un QuickMenu, con búsqueda mientras escribes
- [ ] Fijar lo que se usa a menudo
- [ ] Pegar como texto sin formato o con las transformaciones de la acción «Portapapeles»
- [ ] Respetar la marca de los gestores de contraseñas para no guardar lo que copian
- [ ] Guardarlo cifrado con DPAPI
- [ ] `{portapapeles:2}` para lo penúltimo copiado
- [ ] Que los asistentes puedan consultarlo («¿qué enlace copié antes?»)

### Exportar e importar macros y perfiles de K-Board
- [ ] Exportar e importar una macro o un perfil de K-Board suelto (ahora solo existe la copia de todo, `DataArchive.cs`)

### IA
- [ ] **Menú de IA sobre el texto seleccionado**: un atajo que abra un QuickMenu con «Traducir / Corregir / Resumir / Explicar / Responder», en cualquier programa y sin hablar. `transform_text` ya lo hace por voz; si ya se puede montar con un QuickMenu y «Pedir a un asistente», traerlo hecho de serie
- [ ] **Preguntar a la IA desde el lanzador**: escribir `? cuánto es 3 tazas en gramos` y ver la respuesta ahí mismo, sin abrir el chat
- [ ] **Transcribir y resumir grabaciones**: unir «Grabar audio» con Parakeet para sacar el texto y un resumen de una reunión o un audio, todo en local
- [ ] **Traducir lo que hay en pantalla**: el OCR de «Texto en pantalla» más el modelo, para traducir una zona y mostrarlo superpuesto
- [ ] **Sugerencias a partir de lo que haces** (opcional, desactivado de serie): con las estadísticas, «Abres VS Code y Spotify juntos casi cada mañana, ¿te creo un comando?»

### Productividad
- [ ] **Disposiciones de ventanas**: guardar dónde está cada ventana y restaurarlo de golpe («modo trabajo», «modo juego»), como acción y desde eventos (por ejemplo, al conectar el segundo monitor)
- [ ] **Buscar archivos en el lanzador** (los asistentes ya tienen `find_files`)
- [ ] **Cuentagotas de color** que copie el `#hex`, y quizá una regla

### Captura
- [ ] **Grabar vídeo de pantalla** (zona, ventana o monitor) con el audio de «Grabar audio». Con Windows.Graphics.Capture y Media Foundation, sin ffmpeg
- [ ] **Repetición de vídeo**: guardar los últimos 30 segundos, como «Guardar repetición» del audio
- [ ] **GIF corto de una zona**
- [ ] **Anotar la captura**: flechas, recuadros y difuminar antes de guardar o copiar

### Sistema
- [ ] **Mantener el PC despierto**: sin suspender ni apagar la pantalla durante N minutos o mientras un programa esté abierto
- [ ] **Esquinas activas y gestos del ratón** para lanzar comandos (ratón a una esquina, botón derecho y dibujar una «L»)
- [ ] **Luz nocturna** (activar o desactivar) en la acción «Pantalla»
- [ ] **Leer en voz alta el texto seleccionado** con las voces locales (Kokoro o Piper), sin pasar por un asistente

### Integraciones
- [ ] **Notificaciones de Windows como evento** (con permiso del usuario):
  - «cuando llegue un mensaje de X en WhatsApp de escritorio…»
  - que los asistentes puedan leer las notificaciones
- [ ] **Calendario** (Google u Outlook, aunque sea leyendo un enlace `.ics`):
  - evento «X minutos antes de una reunión»
  - que los asistentes sepan la agenda («¿qué tengo mañana?»)
- [ ] **Servidores MCP recomendados**: lista de servidores probados (correo, calendario, Notion…) que se añadan con un clic

## Accesibilidad

Ya hay unos 360 `AutomationProperties` (casi todos `Name`). Solo queda probarlo:
- [ ] Recorrer un par de páginas con el Narrador (Ctrl+Win+Enter) y Tab
- [ ] Que se vea qué control tiene el foco, sobre todo en el tema claro
- [ ] Que el OSD y el círculo de cuenta atrás se anuncien (`LiveSetting` solo se usa una vez)

## Distribución

### Compilar en GitHub Actions
- [ ] Workflow en `windows-latest`: .NET 10 (`setup-dotnet`), JDK 17 y Android SDK (build-tools 34)
- [ ] Compilar la APK antes que el exe (el exe la lleva dentro)
- [ ] Clave de la APK (`kdesktop-release.jks` en base64 y su contraseña) en los secretos de GitHub. Tiene que ser la misma de siempre, o los móviles no aceptan la actualización
- [ ] `make-update.ps1` y `publish-release.ps1` en el workflow (`gh` usa el token del propio workflow)
- [ ] Lanzarlo al crear una etiqueta `v5.0.x` o a mano

### Firma con SignPath Foundation (gratis para código abierto)
Ojo: el certificado sale a nombre de **«SignPath Foundation»**, no al tuyo. Windows muestra como editor a SignPath Foundation; SmartScreen deja de avisar, pero tu nombre no aparece.

- [ ] Solicitarlo en signpath.org (lo aprueban ellos; miran que el proyecto esté activo y tenga reputación)
- Licencia OSI y todo el código abierto: MIT ✅. Comentar al pedirlo que los modelos (Parakeet, EmbeddingGemma, Kokoro…) se descargan aparte y no van dentro del exe
- [ ] Compilación en CI desde el repo (lo anterior)
- [ ] Aprobación manual de cada versión antes de firmarla
- Nombre del producto y versión en el exe: ya están en `Directory.Build.props` ✅
- [ ] MFA en GitHub y en SignPath
- [ ] Definir roles: autor, revisor y quien aprueba (puedes ser tú los tres)
- [ ] Sección «Code signing policy» en la web y en la página de descargas (roles, privacidad, mención a SignPath)
- [ ] **Forma de desinstalar**: opción tipo «Quitar K-Desktop de este equipo» (arranque con Windows, datos de `%APPDATA%`)
- [ ] Quitar de las notas de la versión el aviso de SmartScreen cuando esté firmado
- [ ] Publicar en **winget** (no exige firma; se puede hacer ya)
- Alternativa de pago: **Certum Open Source Code Signing** (anual, mirar precio)

### Orden recomendado
1. Publicar en winget (gratis y sin firma)
2. Montar el workflow de GitHub Actions
3. Añadir la opción de desinstalar
4. Pedir SignPath

## Modelos y voz por API

### LLM por API (ahora: cuesta poco y es lo que más nota quien no tiene gráfica)

Ya existen los «otros servidores» de tipo OpenAI con clave (DPAPI) y el aviso de `EndpointPlace.Internet`. ChatGPT, Gemini y Claude tienen direcciones compatibles con OpenAI, igual que OpenRouter, Groq, Mistral o DeepSeek. Debería funcionar ya poniendo la dirección y la clave a mano (sin probar).

- [ ] **Proveedores ya preparados** en el desplegable (OpenAI, Gemini, Claude, OpenRouter…): elegir y poner solo la clave
- [ ] **Probar con cada uno**: herramientas (tool calling), streaming, visión (`look_at_screen`, `read_file` con imágenes) y razonamiento
- [ ] **Privacidad a la vista**: avisar al elegirlo de que salen fuera lo que dices, la memoria, las notas, los documentos de «Conocimiento» y las capturas de pantalla
- [ ] Permitir un modelo distinto por asistente (local para uno, API para otro); ya pueden tener su propio modelo
- [ ] **Coste**: revisar qué llama al modelo sin que el usuario hable (reacciones, «avísame cuando») para no gastar tokens de fondo

### Voz por API (más adelante, opcional)

- [ ] Plantearlo como un **motor de voz por API genérico** (ElevenLabs, OpenAI TTS, Azure…), no solo ElevenLabs
- [ ] **Caché** de las frases fijas
- [ ] Antes, comprobar:
  - **Coste por carácter**: los asistentes hablan mucho y en frases cortas
  - **Latencia**: el viaje por internet en cada frase, frente a Kokoro y Piper en local
  - **Animación**: si los personajes usan los fonemas de eSpeak (`EspeakPhonemes.cs`), ElevenLabs no los da igual
  - **Efectos y perfiles de voz** por asistente sobre el audio de la API
- [ ] La voz a texto se queda en local (Parakeet)
