# Crear un asistente

Un asistente es un paquete `.kai`: un `.zip` con su manifiesto (`assistant.json`), sus imágenes y ajustes, y
su código en `lib/`. Se escribe en C# contra el SDK (`src/KDesktop.Assistant.Sdk`), que K-Desktop pone siempre: el
paquete nunca lo lleva. Mientras el SDK no esté en NuGet se usa por referencia a una copia de este repositorio.

¿Solo quieres un personaje que pasee por el escritorio, sin programar? Mira **«Sin código»**, justo abajo.

## Sin código

Un asistente puede ser solo imágenes y JSON: en vez de un ensamblado, su `entry` es un `character.json`
([esquema](esquemas/character.schema.json)) y K-Desktop lo mueve. Está sobre la barra de tareas o encima de las
ventanas, pasea de vez en cuando (y a veces se cae por el borde de una ventana), sigue a la ventana en la que está, se
deja arrastrar y soltar, se duerme cuando no estás, y pone una animación para cada cosa: escucharte, pensar, hablar con
su bocadillo, alegrarse o entristecerse por lo que pasa, un clic. Habla, piensa y usa las herramientas como cualquier
otro asistente (su voz y su personalidad, en «Personalidad y voz»). El ejemplo es **Pip** (`samples/CharacterAssistant`;
sus dibujos los hace `tools/character/make-pip.ps1`).

```
MiPersonaje\
  assistant.json      "entry": "character.json", "traits": { "travel": "walk", … }
  character.json
  icon.png
  frames\idle0.png …  (PNG con transparencia, todos con el mismo alto)
  settings.json       (opcional: "scale" y "wander" los entiende el host)
```

```json
{
  "height": 80,
  "faces": "right",
  "walkSpeed": 60,
  "animations": {
    "idle": { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2 },
    "walk": { "strip": "frames/walk.png", "frames": 6, "fps": 10 },
    "talk": { "frames": ["frames/talk0.png", "frames/talk1.png"], "fps": 8, "sound": "sounds/hola.wav" }
  }
}
```

- `height`: alto con la escala al 100 %; cada fotograma se dibuja así de alto. Los pies son el borde de abajo de la imagen.
- `faces`: hacia dónde miran los dibujos; al andar hacia el otro lado se dan la vuelta solos.
- Animaciones: `idle` (obligatoria), `walk`, `talk`, `listen`, `think`, `happy`, `sad`, `surprised`, `click`, `drag`,
  `fall`, `sleep`, `enter`, `exit`, `announce`, `catch`. La que falte usa otra (`think` → `listen` → `idle`, `click` → `happy` →
  `idle`, `announce` y `catch` → `happy`, `drag` → `fall` → `idle`…). Cada una con `frames` (lista de imágenes) o `strip` + `frames` (una tira de N fotogramas
  del mismo ancho), `fps`, `loop` (por defecto se repiten todas salvo `happy`, `sad`, `surprised`, `click`, `enter`,
  `exit`, `announce` y `catch`) y `sound` (un WAV que suena al empezar). Antes de decir algo por su cuenta (un temporizador, un
  aviso) se acerca andando al ratón por donde está y hace `announce`; si le sueltas archivos encima, `catch`.
- Para el paquete: comprime la carpeta (el `assistant.json` en la raíz) con `tools\character\pack.ps1 <carpeta>`, que
  deja `<id>-<versión>.kai` con las rutas como las quiere K-Desktop, y ábrelo con K-Desktop.
- La ficha de instalación dice que no lleva código. Nada del paquete se ejecuta: K-Desktop solo lee sus imágenes y
  sonidos.

## Empezar

```powershell
dotnet new install "<K-Desktop>\templates\kdassistant"
dotnet new kdassistant -n MiAsistente --sdk "<K-Desktop>\src\KDesktop.Assistant.Sdk" --authorName "Yo"
cd MiAsistente
dotnet build          # → bin\Debug\…\miasistente-1.0.0.kai
```

El proyecto tiene:

| | |
|---|---|
| `MiAsistente.cs` | La clase que implementa `IAssistant`: se dibuja, tiene una zona de clic y sabe `Communicate` y `React` |
| `Package\assistant.json` | El manifiesto (esquema en `docs/assistant/esquemas/assistant.schema.json`): id, versión, entrada, gestos… |
| `Package\…` | Todo lo demás que va en el paquete tal cual: `icon.png`, `settings.json`, `panel.json`, `persona\`, `voice\`, `assets\` |

Cada compilación deja en `bin\…\package\` el paquete tal y como se instala y, al lado, el `.kai`.

## Probar sin instalar

```powershell
KDesktop.exe --assistant-dev "<carpeta del proyecto>"
```

Carga el `bin\…\package` más reciente (una copia, para que se pueda recompilar) y lo **recarga al recompilar**. Si
K-Desktop está abierto con la misma carpeta de datos, se conecta a él; si no, funciona solo. El registro está en
`Logs\assistant.log` de la carpeta de datos. `KDesktop.exe --assistant --exit` lo cierra.

## Instalar

Con el módulo Asistente activado, un doble clic en el `.kai` (o `KDesktop.exe --install-assistant
"archivo"`) abre su ficha en K-Desktop. Antes de enseñarla, K-Desktop comprueba el paquete **sin cargar su
código**: rutas seguras y tamaños, `assistant.json`, `settings.json`, `panel.json`, la persona y la voz contra los
esquemas, que estén la DLL de entrada y las imágenes (PNG o JPEG), el número mayor del SDK y que el id no esté
reservado (`data`, `settings`, `dev`). Si algo falla, la ficha dice qué.

Se instala en `Assistants\<id>\<versión>\`. Una versión nueva sustituye a la anterior (también en caliente, si es
la que está en el escritorio) y conserva `Assistants\Data\<id>\` y los ajustes; lo que siga en uso se borra cuando
el proceso del asistente lo suelta. Las copias de seguridad de K-Desktop guardan esos datos y la lista de
asistentes instalados, no los paquetes.

## Publicar versiones nuevas

Si el manifiesto dice dónde está siempre el último paquete, K-Desktop busca versiones nuevas a la vez que las suyas
(al arrancar, cada 6 horas y con «Comprobar» en Opciones → Actualizaciones) y las **ofrece**: un aviso, una barra en
la ventana, la fila del asistente en Opciones → Asistentes y Opciones → Actualizaciones, con «Actualizar…» (lo
descarga y abre su ficha) y «Omitir esta versión». Nunca actualiza un asistente sin preguntar, aunque K-Desktop
esté en modo automático.

```json
"updates": "https://github.com/yo/mis-asistentes/releases/latest/download/miasistente.kai",
"changes": { "es": "Salta más alto y habla más rápido.", "en": "Jumps higher and talks faster." }
```

- `updates` es una dirección fija que siempre da el último `.kai` (en GitHub, la de `releases/latest/download/…`).
  https; `http://localhost` y `http://127.0.0.1` valen para probar con un servidor propio.
- Para comprobarlo, K-Desktop **solo lee el `assistant.json`** del paquete publicado con peticiones por rangos
  (unos KB); un servidor que no los admita manda el paquete entero cada vez.
- Se ofrece si la versión es mayor y el id es el mismo. Si pide un SDK más nuevo que el de esa K-Desktop, se dice que
  hace falta actualizar K-Desktop antes.
- `changes` (opcional, en uno o varios idiomas) es lo que trae esa versión: sale en el aviso y en la ficha.

## Ponerlo en un catálogo

Opciones → Catálogo enseña los asistentes de los catálogos que se leen (el de K-Desktop y los que añada cada uno) con
«Instalar…» y «Actualizar…» (lo descarga y abre su ficha, como siempre). Un catálogo es un JSON publicado en una
dirección fija ([esquema](esquemas/catalog.schema.json)) que solo dice dónde está cada paquete:

```json
{
  "catalog": 1,
  "assistants": [
    { "id": "miasistente", "package": "https://github.com/yo/mis-asistentes/releases/latest/download/miasistente.kai" }
  ]
}
```

- `package` es la misma dirección que `updates`: la que siempre da el último `.kai`.
- De cada paquete K-Desktop solo lee el `assistant.json` y el icono, por rangos: el nombre, la descripción, la versión,
  el autor y si funciona con esa K-Desktop salen del propio paquete. Si el `id` no coincide con el del paquete, no se
  ofrece.
- Estar en un catálogo no hace a un paquete de fiar: la ficha dice si está **verificado** (ver «Firmar el paquete») y lo
  que puede hacer antes de instalarlo.

## Firmar el paquete

Un paquete firmado lleva `signature.json` con el SHA-256 de **todos** sus archivos, quién lo publica, su clave pública
(ECDSA P-256) y la firma. Sin pagar a nadie: la clave es tuya. K-Desktop lo comprueba al abrir la ficha:

| Lo que encuentra | Lo que enseña |
|---|---|
| Firma buena con una clave de su lista (`trusted-publishers.json`) | **Verificado · nombre**, también en Opciones → Asistentes |
| Firma buena con otra clave | «Firmado por «nombre»», y el aviso de siempre: ese nombre es solo lo que dice el paquete |
| Sin firma | «Sin firma» y el aviso de siempre |
| Firma que no casa (un archivo cambiado, añadido o quitado después de firmar) | **No lo instala** |

Si la versión instalada estaba firmada y la nueva no lleva la firma de la misma clave, la ficha lo avisa en rojo.

Con `tools/KaiSign` (en el repositorio de K-Desktop):

```powershell
dotnet run --project toolsKaiSign -- keygen "Tu nombre"   # una vez: la clave queda en tu usuario de Windows (DPAPI)
dotnet run --project toolsKaiSign -- sign miasistente.kai  # después de compilar, antes de publicar
dotnet run --project toolsKaiSign -- verify miasistente.kai
dotnet run --project toolsKaiSign -- export copia.pem "contraseña"   # copia de la clave, para guardarla aparte
```

Firma **lo último**: cualquier cambio al paquete después rompe la firma. Para que tus paquetes salgan como
verificados, tu clave pública (`kaisign public`) tiene que estar en la lista de K-Desktop.


## El ejemplo

`samples/DotAssistant` (Dot) es el asistente mínimo completo: un punto que se teletransporta, se puede arrastrar,
reacciona a la papelera, a los avisos, a la hora en punto y al scroll fuerte, suena al saltar (un sonido hecho en
código), tiene un extra (`dot.jump`), ajustes con un botón para ese extra y otro para «Poner a cero los saltos», una
tarjeta y un botón en el panel, y guarda sus datos en su carpeta.

## Entrar y salir

`Enter(how)` y `ExitAsync(why, ct)` dicen **por qué** llega o se va (SDK 1.9):

| `EntranceKind` | Cuándo | Qué hacer |
|---|---|---|
| `Startup` | Arranca K-Desktop o se enciende el módulo | Su entrada |
| `Summoned` | El usuario lo pone en el escritorio | Una entrada que diga «aquí estoy» |
| `Reloaded` | Estaba hace un momento y vuelve: versión nueva, otro idioma, proceso reiniciado o recuperado de una caída, recompilado con `--assistant-dev` | Reaparecer donde estaba, sin espectáculo |

| `ExitKind` | Cuándo | Qué hacer |
|---|---|---|
| `Shutdown` | Se cierra K-Desktop o se apaga el módulo | Irse ya |
| `Dismissed` | El usuario lo esconde (Opciones, la bandeja, su menú) | Su despedida |
| `Uninstalling` | Se desinstala (su carpeta de datos puede borrarse justo después) | La última despedida |
| `Reloading` | Vuelve enseguida con `Reloaded` | Irse ya y guardar en `DataDirectory` dónde estaba: el nuevo puede ser otro proceso |

El host espera la salida 3 s como mucho (el token salta entonces).

## Ajustes comunes

Lo que todos los asistentes tienen igual no va en su `settings.json`: K-Desktop lo enseña en la pestaña «General» de su
ficha, con las mismas unidades para todos (SDK 1.9). En el manifiesto se dice cuáles admite:

```json
"common": ["size", "volume", "liveliness", "dragging"]
```

| Nombre | Qué es | Quién lo hace |
|---|---|---|
| `size` | Tamaño, del 50 % al 200 % | El asistente: `host.Common.Size` (1 = el tamaño para el que está dibujado), por el escalado del monitor como siempre |
| `volume` | Volumen de sus sonidos, del 0 % al 100 % | El host, sobre todo lo que suena por `IAudio` (su voz va aparte) |
| `liveliness` | Lo movido que es, del 0 % (lo menos posible) al 200 % | El asistente: `host.Common.Liveliness` (1 = lo normal) para lo que hace por su cuenta |
| `dragging` | Cogerlo con el ratón | El host no le pasa los arrastres de su zona `body` si está apagado; `host.Common.Dragging` para lo demás (otras zonas, como un juguete, se pueden arrastrar igual) |

Cuando cambian, llega `OnSettingsChanged` con `host.Common` ya al día. Un asistente sin código los admite todos.

## Lo que da el host

- **Bocadillo:** cuando el asistente dice algo (`Communicate`), el host dibuja el bocadillo con el texto, apuntando a
  `BubbleAnchor` (lo normal, lo alto de la cabeza; null si no se ve) y siguiéndolo mientras se mueve, el tiempo que
  se tarda en leerlo. El asistente solo hace su gesto de «hablar» (o nada: el bocadillo sale igual). El clic central
  sobre el personaje abre la caja para escribirle (y su menú del clic derecho, *Escribir*): el clic y el doble clic
  izquierdos son tuyos. Mantener pulsado el botón izquierdo sin moverlo (450 ms) es hablarle: recibes `Down` y `Up`,
  pero no `Click` ni arrastre.
- **Texto propio** (SDK 1.15): con `"speech": "own"` en el manifiesto lo que dice lo dibujas tú (un bocadillo a tu manera,
  un cartel, las palabras dentro de tu nube…). El host llama a `ShowSpeech(SpeechText)` cada vez que la respuesta crece
  mientras se escribe (`Finished` falso), con toda ella cuando está lista (`Finished` verdadero) y otra vez con un `Until`
  más tarde mientras tu voz sigue diciéndola; cada llamada sustituye el texto de la anterior. Enséñalo hasta que llegue
  `HideSpeech`: el host sigue llevando los tiempos, la voz y el chat. `Text` es texto sin formato y llega entero aunque sea
  largo: córtalo a lo que te quepa (entero está en el chat). Si ahora no puedes (estás escondido, en mitad de una maniobra,
  no cabe), devuelve `false`: el host saca su bocadillo para el resto de esa respuesta. En la pestaña «General» de tu
  ficha el usuario puede elegir «Usar siempre el bocadillo de K-Desktop» y entonces no te llega nada. Un asistente sin
  código usa siempre el bocadillo del host.

- **Estado** (`SetState`): K-Desktop te dice si te está escuchando (`Listening`: su palabra, la tecla para hablar o el
  clic mantenido), si piensas (`Thinking`), si hablas (`Speaking`) o si vuelves a reposo (`Idle`). Enséñalo a tu
  manera (el Lander enciende la luz de su antena; Dot se rodea de un anillo blanco al escuchar y late al pensar).

- **Estados con detalle** (SDK 1.13): implementa `SetState(AssistantState state, StateDetail detail)` y el host te llama a
  ese en vez de al de siempre. Llegan además:
  - `ThinkingHard`: lo piensa despacio (lo pidió la IA o el usuario dijo «piénsalo bien»); puede tardar medio minuto.
  - `LoadingBrain`: su modelo se está cargando en memoria antes de poder pensar (de unos segundos a un minuto).
  - `NoBrain`: la orden necesitaba la IA y no hay; llega justo antes de `Speaking`, que lo dice (un encogerse de hombros).
  - `Busy` con `detail.Activity` mientras corre una herramienta o un comando (`SearchingWeb`, `ReadingWeb`, `Files`,
    `Notes`, `Memory`, `Weather`, `Music`, `Timer`, `Screen`, `Command`…; las de un servidor MCP o tuyas, `Other` con
    `detail.Tool`), y `AwaitingConfirmation` mientras una pide permiso.
  - `Listening` con `Dictation` mientras K-Desktop escribe lo que dice el usuario («escribe lo que te diga»).

  `detail.Text` lo dice en su idioma («Buscando en internet»), por si lo escribes tú. Puede haber actividades nuevas en
  versiones siguientes: las que no conozcas, trátalas como `Other`. Un asistente que solo tiene el `SetState` de antes
  sigue igual: recibe el estado más cercano que conoce (`AssistantStates.Basic`: los tres nuevos son `Thinking`). La
  plantilla cambia de color con cada uno.

- **Sonido** (`IAudio`): `Load` (muestras en coma flotante, mono o estéreo, a cualquier frecuencia) o `LoadWav` (del
  paquete), `Play` con ganancia y panorama, y `Loop` con ganancia, tono y panorama en vivo (para un motor). El host lo
  mezcla todo bajo el volumen de Opciones → Asistente → General; el dispositivo solo se abre mientras suena algo. Con el
  sonido apagado, `Enabled` es falso y todo suena a silencio.
- **Zonas de clic** (`Pointer.CreateHitArea(id)`): cada una con su `id`, que llega en `PointerEvent.Area` (con varias —el
  cuerpo y unos juguetes, por ejemplo— dales nombres distintos, o no sabrás de cuál viene cada clic). Sobre la zona sale el
  cursor que digas (`Cursor`: flecha, mano, agarrar) o, desde el SDK 1.8, uno dibujado por ti: `SetCursorImage(imagen,
  punta)` con un `BitmapSource` a los píxeles del monitor (los cursores de Windows miden 32 a 100 %; Kobii pone un
  cepillo al cepillarle); `null` vuelve al de siempre.
- **Archivos soltados encima** (SDK 1.16): las zonas de clic aceptan archivos arrastrados desde el Explorador (el proceso de
  los asistentes va como usuario normal; K-Desktop, como administrador, no podría recibirlos). Llegan a `OnPointer` como
  `FilesOver` (están encima: el momento de estirar los brazos), `FilesAway` (se fueron sin soltarlos, o el usuario pulsó
  Escape) y `FilesDropped` (los soltó: el momento de cogerlos), con las rutas completas en `PointerEvent.Files` y la zona en
  `Area`. Bajo el dibujo que se arrastra sale «Dárselo a <tu nombre>». Solo es la animación: qué hacer con ellos lo
  decide K-Desktop. Nunca se toman como «mover»: el archivo sigue donde estaba.
  Un asistente compilado con un SDK anterior no ve estos eventos.
- **Teclado** (`IKeyboard`): `IsDown` siempre; `Capture` hace que K-Desktop se trague esas teclas (pulsadas sin Ctrl, Alt
  ni Win, así los atajos siguen) mientras se juega, y sus cambios llegan a `KeyChanged`. `Release` al terminar (también
  se sueltan solas si el asistente sale, el proceso se cae o K-Desktop se pausa).
- **Rueda del ratón:** `SystemEvent.FastScroll` llega siempre que el usuario gira la rueda con fuerza (con la posición
  del cursor); cada giro (`SystemEvent.WheelTurned`, con la ventana bajo el cursor) solo con `Pointer.WatchWheel = true`:
  cuesta un mensaje por muesca, así que mejor solo mientras importa. Salen del gancho de K-Desktop, que también ve la
  rueda sobre ventanas de administrador.
- **Ventanas** (`IWindowServices`): `TryMove` y `TryCommand` (minimizar, maximizar, restaurar, traer delante; SDK 1.3 —
  las de un proceso de administrador y traer delante las hace K-Desktop por ti—), botones de la barra de título (de DWM), `Thumbnail` (una imagen de la ventana,
  congelada; si la ventana no se deja dibujar, lo que se ve de ella en pantalla) y `AppIcon` (el icono de su programa, en caché).
- **Gestos de los comandos** (`CanPerform` / `PerformAsync`): cuando el usuario te pide un comando de K-Desktop, cada
  acción llega como uno o varios gestos (minimizar → `PressButton(Minimize)`, colocar → `MoveWindow` con el destino
  exacto, una maximizada antes `PressButton(Restore)`…). Llama a `ctx.Commit()` **en el instante del efecto** (el impacto
  del disparo): la acción ocurre justo entonces, y `ctx.Committed` te dice si salió bien. O hazlo tú (`TryMove`) y
  devuelve `PerformedSelf`: K-Desktop la deja luego al píxel. Lo que no sepas hacer (`CanPerform` falso) lo enseña el
  host con el bocadillo mientras K-Desktop lo hace. Tienes `ctx.TimeLeft`: si te pasas, fallas o no contestas, K-Desktop
  lo hace igual y te pide un `React(Warning)`. Nunca puedes dejar una orden a medias. `ctx.Speed` es la rapidez que
  eligió el usuario (`Showy`, `Normal`, `Fast`): en `Fast` haz el `Commit` antes de medio segundo (pasado ese tiempo,
  K-Desktop lo hace igual). Con «Enseñar también los comandos que lanzas tú» encendido, los comandos lanzados por otras
  vías también te llegan como gestos, siempre en `Fast`: son solo adorno, nada los espera y su `Commit` no hace nada.
- **Gestos de las herramientas** (5.0.174): lo que hacen las herramientas de la IA también te llega como gestos, con
  `ctx.Step` 0: un temporizador → `Mark` junto al reloj (`Place.Clock`) con la hora a la que suena; quitarlo →
  `PointAt(Place.Clock)`; abrir una web, una búsqueda, un programa o un archivo → `WaitForWindow` (llega justo antes de
  abrirlo; `ctx.Appeared` da la ventana nueva o null); qué suena o controlar la música → `GoTo(Place.Tray)`; «avísame
  cuando se cierre…» → `Mark` en esa ventana; reescribir un texto y pegarlo → `Typing` en esa ventana; leer la pantalla →
  `Scan` de lo que se leyó. Las herramientas JSON pueden pedir uno con `"gesture"`. Son solo adorno, como los de arriba:
  la herramienta sigue sin esperarte, `Commit` no hace nada, un paso de una orden les quita el sitio y uno nuevo sustituye
  al anterior. Lo que no sepas hacer (`CanPerform` falso) no se enseña.
- **Llamar la atención** (`Gesture.Announce`, SDK 1.14): antes de decir algo por su cuenta (un temporizador que suena,
  un «avísame cuando» que se cumple, «Asistente: decir») el host te pide que llames la atención del usuario: acércate al
  ratón, saluda, parpadea… `Reason` dice por qué (`Timer`, `Watch`, `Message`), `Cursor` dónde estaba el ratón (o null) y
  `UserAway` cuántos segundos lleva sin tocar el teclado ni el ratón (cuanto más, más grande puede ser: quizá no está
  mirando). El host espera a que acabes, como mucho `ctx.TimeLeft` (2 s, 3 en `Showy` y medio en `Fast`), y luego habla
  con `Communicate`: termina en cuanto hayas llamado la atención. No hay nada que confirmar (`Commit` no hace nada). Si
  ya está hablando, si un paso de una orden ocupa tu sitio o si no lo sabes hacer (`CanPerform` falso), habla sin más.
- **Cuerda** (`Rope`, SDK 1.4): una cuerda de Verlet en píxeles de pantalla para ganchos, cables y correas. El
  principio lo sujetas tú (`Start` en cada tick); el final cuelga (`ReleaseEnd`, con la velocidad a la que lo sueltas) o
  va sujeto (`PinEnd`: clavado en una ventana, en un proyectil). Solo resiste estirarse (se comba y se afloja), nunca pasa
  de `MaxLength` con el final suelto y avanza a 120 Hz fijos (`Step(tick.Elapsed)`), así que se comporta igual a cualquier
  ritmo. `Length` para recoger o soltar cable, `Gravity`, `Damping`, `Iterations`; `IsTaut`, `Overstretch` y `Pull` (hacia
  el principio, lo que sobra de largo: lo que sigue una ventana remolcada). Se dibuja con `Draw(dc, pen)` (curva suave) y
  `Bounds(margen)` dice el tamaño de la superficie.
- **Contexto de las reacciones** (SDK 1.12): `Gesture.React` trae `Context` (`ReactionContext`), que rellena el host:
  `Source` = de dónde viene (`Order`, un comando tuyo o una acción de K-Desktop; `Tool`, una herramienta como el
  temporizador, una nota o la web; `Notice`, algo que dice por su cuenta, como un temporizador que suena; `User`, lo que
  el usuario dice de él: «gracias», «bien hecho» → `Celebration`, «está mal» → `Failure`), `Streak` = cuántas seguidas
  del mismo tipo (1 la primera; vuelve a 1 con otro tipo o tras media hora sin reacciones), `FirstToday` = la primera
  del día, y `What` = qué fue cuando se sabe (`set_timer`, `run_command`, `timer`, `praise`…). El host reacciona una vez
  por orden, cuando la respuesta está lista: algo hecho y nada fallido → `Success`; hecho pero algo falló → `Warning`;
  nada hecho y algo falló → `Failure` (un fallo que la IA arregla en la misma orden no cuenta); solo consultar (la hora,
  el tiempo) no reacciona. Úsalo con `Variety` para no reaccionar siempre igual: algo más grande la primera del día o
  en una racha larga, más desanimado al tercer fallo seguido. Cuando el host no sabe de dónde viene, llega
  `ReactionContext.Unknown`. Los asistentes compilados con un SDK anterior siguen funcionando igual.
- **Sitio para todos** (SDK 1.11): con varios asistentes en el escritorio, ninguno se queda encima de otro. Di dónde está tu
  cuerpo con `BodyBounds` (solo el cuerpo: sin partículas, bocadillos ni efectos; null si no se te ve). Si no lo das, el
  host usa los límites de tu zona de clic `body`, que puede no estar siempre puesta. `World.Others` dice dónde está el
  cuerpo de cada uno de los demás (`Id`, `Body`) y si va de camino a otro sitio (`Moving`: se ha movido en el último
  medio segundo). Antes de posarte, pararte o pasear hasta un sitio, mira `World.TakenBy(rectángulo)`: da el que se ha
  quedado ahí, o null si está libre (uno de paso no cuenta salvo con `moving: true`). Pasar por delante vale; quedarse
  encima, no: elige otro sitio, o apártate si al llegar resulta que ya había otro. El asistente sin código lo hace solo.
- **Variedad** (`Variety<T>`, SDK 1.10): para no hacer siempre lo mismo (entradas, reacciones, animaciones de reposo,
  frases). Cada variante con su peso (`Add(x, weight: 2)` sale el doble que una de 1; 0 = nunca) y su enfriamiento en
  segundos (`cooldown: 120`: no vuelve a salir en dos minutos). `Pick()` elige al azar por peso, nunca la misma dos veces
  seguidas mientras haya otra (`NoRepeat`), y si todas descansan da la que antes esté lista: siempre sale algo.
  `TryPick` no elige nada si todas descansan. `Pick(x => …)` elige solo entre las que encajan ahora (en la barra de
  tareas, encima de una ventana…). `Use(x)` la marca como hecha aunque no saliera de ahí, `CooldownLeft`, `Reset`. Con
  `new Variety<T>(seed: 1)` la secuencia es siempre la misma (pruebas); `clock` cambia el reloj de los enfriamientos.
- **Un solo sistema de dibujo** (SDK 1.6): `Present` con la API de WPF. El SDK 1.5 tenía también `PresentGpu` (shaders con
  los punteros de Direct3D del host); se quitó porque nadie lo necesita y salía caro: con el shader de Kobii el controlador
  de la GPU gastaba un 4–12 % de un núcleo todo el rato, frente a un 1 % dibujando lo mismo en vectorial. El último
  fotograma se queda en pantalla aunque se pierda la GPU o vuelva de un juego a pantalla completa (el host lo repite).
- **Música** (SDK 1.7): con `World.ListenToMusic(true)`, `World.Music` dice si suena música con ritmo (`Playing`; una voz o
  ruido no cuentan), a cuántos pulsos por minuto (`Bpm`) y el pulso (`BeatAt(tick.Now)`: crece uno por pulso y la fracción
  es la fase, 0 = el golpe), para bailar a compás. El host lee el medidor de cada programa que suena (las barras del
  mezclador de volumen de Windows: nada se graba), sin los sonidos ni las voces de los asistentes; cien veces por segundo
  mientras suena algo y cuatro en silencio, y solo mientras algún asistente escucha: deja de escuchar
  (`ListenToMusic(false)`) cuando no se te ve. Tarda unos 3 s en reconocer el ritmo.
- **Papelera:** `SystemEvent.RecycleBinEmptied` trae dónde está su icono en el escritorio (o null si no está o los
  iconos están ocultos).

## Ajustes y panel

K-Desktop los dibuja con sus propios controles, sin cargar código del asistente (§11.8 del plan):

- **`settings.json`** → Opciones → Asistentes → *Ajustes* de cada uno (o el menú del clic derecho → *Ajustes*).
  Páginas (pestañas si hay varias), secciones y controles: `toggle`, `slider`, `number`, `choice`, `text`, `color`,
  `gallery`, `apps`, `hotkey` (llega con la etapa 2.6), `button` (lanza un extra o, con `resetData`, borra su carpeta
  de datos tras confirmar), `info` y `keys`. `enabledWhen` atenúa un control mientras un interruptor esté apagado.
  Lo que el usuario cambia se guarda en `Assistants\Settings\<id>.json` (solo lo que difiere del valor por defecto)
  y, si está en el escritorio, le llega al momento: `OnSettingsChanged` con **todos** los valores.
- **`panel.json`** → Panel → Asistente, debajo de su tarjeta: `stat` (en fila), `progress`, `list`, `achievements`,
  `buttons` y `text`. Los datos llegan con `PublishPanel`, que se **fusiona** con lo publicado antes: publica todo en
  `Enter` y después solo lo que cambie. Una galería con `source` toma sus imágenes de esos mismos datos
  (`[{ id, name, image, locked, lockText }]`).
- **Extras según el momento** (SDK 1.1, pon `"sdk": "1.1"`): `SetExtraState(id, Shown | Disabled | Hidden)`
  enseña, atenúa o esconde un extra en el menú del clic derecho (que dibuja K-Desktop, con el icono y el atajo de
  cada extra) y en los botones del panel; tras cada carga todos vuelven a `Shown`. `ExtraHotkey(id)` da el atajo del
  comando de K-Desktop que lo ejecuta («CTRL + ALT + L»), para decirle al usuario qué pulsar. Los comandos y la voz
  siguen llegando a `OnExtraAsync` aunque esté escondido: explica allí por qué no se puede.
- **Su voz:** lo que contesta lo dice en voz alta el host, con el perfil de `voice` de su manifiesto
  (`voice/<id>.json`: motor y cadena de efectos —`pitch`, `formant`, `ring`, `vocoder`, `bitcrush`, `radio`, `chorus`,
  `flanger`, `echo`, `glitch`— y `moods`, los efectos que se añaden en ciertas situaciones). `OnVoiceLevel` le dice lo
  fuerte que suena mientras habla (para luces o una boca). Con SDK 1.2 (`"sdk": "1.2"`), `SetVoiceMood("error")`,
  `SetVoiceMood("happy")` o `SetVoiceMood(null)` cambian el ánimo de lo que diga a partir de entonces (el host ya no pone
  `far` solo). Su personalidad, en `persona/{lang}.json`.
- **Lo que el usuario le cambia:** en Opciones → Asistentes → el tuyo → «Personalidad y voz» el usuario puede escribirle otra
  personalidad (tus frases fijas siguen), darle otro idioma y otra voz (motor, voz, perfil, velocidad). Con otro idioma,
  `host.Language` es ese y no el de K-Desktop: léelo en `Attach` (al cambiarlo, el host vuelve a cargar el asistente).
- **El modelo con el que piensa:** `"model": { "suggested": [...] }` en el manifiesto, modelos de Hugging Face
  («usuario/repo/archivo.gguf» o «usuario/repo:Q4_K_M»), el mejor primero. K-Desktop ofrece (desmarcado) el primero que cabe
  en la gráfica del usuario, con su proyector de visión si lo hay, y solo si no tiene ya ese archivo; al descargarlo, ese
  asistente piensa con él. Por ejemplo, el Lander: `["unsloth/Qwen3.5-9B-GGUF:Q4_K_M", "unsloth/Qwen3.5-4B-GGUF:Q4_K_M"]`.

## Reglas

- Todo se llama en el **hilo del motor** del host, y los `await` vuelven a él. No hace falta bloquear nada; nunca
  se bloquea ese hilo.
- `Tick` devuelve **cuánto puede dormir**: en reposo, segundos; animándose, `Rendering.FrameInterval`. Solo se
  dibuja (`Present`) cuando algo cambia: el último fotograma se queda en pantalla.
- Coordenadas en **píxeles físicos** de la pantalla virtual. Para el tamaño, el escalado del monitor
  (`MonitorInfo.DpiScale`).
- **No estás solo:** puede haber otros asistentes en el escritorio a la vez, en el mismo proceso y sobre la misma
  capa. No ocupes la pantalla entera ni des por hecho que eres el único; el mundo no te enseña a los demás (sus
  zonas de clic no cuentan como ventanas). Lo que te llega (gestos, órdenes, extras, ajustes) es solo tuyo: el host
  y K-Desktop deciden a quién va cada cosa.
- Un asistente que falla cinco veces seguidas se descarga (solo él; los demás siguen); uno que hace caer el proceso
  tres veces seguidas se quita del escritorio. Al descargarlo, el host retira lo que haya dejado (superficies, zonas
  de clic, suscripciones).
- El asistente no ve la IA, las herramientas, la tubería ni K-Desktop: solo el SDK.
