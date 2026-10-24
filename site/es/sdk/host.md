# Lo que da el host

El host es el proceso de los asistentes. Con `IAssistantHost` le da al asistente todo lo que puede ver y usar; la IA, las
herramientas, la voz y K-Desktop quedan fuera de su vista.

| Miembro | |
|---|---|
| `World` | Ventanas, monitores, barra de tareas, los demás asistentes, la música, el estado del sistema (`IWorld`) |
| `Rendering` | Superficies de dibujo sobre todo el escritorio |
| `Pointer` | Zonas de clic y la rueda del ratón |
| `Keyboard` | Teclas, y capturarlas mientras juega |
| `Audio` | Sus sonidos |
| `Windows` | Mover y cambiar ventanas (`IWindowServices`) |
| `Settings`, `Common` | Sus ajustes y los [comunes](getting-started.md#ajustes-comunes) |
| `Language` | El idioma en el que habla |
| `DataDirectory`, `PackageDirectory` | Su carpeta de datos (se conserva entre versiones y en las copias) y su paquete |
| `PublishPanel`, `Notify`, `SetExtraState`, `ExtraHotkey`, `SetVoiceMood` | Su panel, un aviso, sus extras, el ánimo de su voz |
| `RequestTick`, `Post`, `Log` | Despertar antes, ejecutar algo en el hilo del motor, escribir en `assistant.log` |

## Dibujar

Un solo sistema de dibujo: superficies con la API de dibujo de WPF. `Rendering.CreateSurface()` da una superficie;
`Present(zona, dc => …)` dibuja un fotograma en ella, en píxeles de pantalla. Presenta solo cuando algo cambia: el último
fotograma se queda en pantalla (el host lo repite si se pierde la GPU o acaba un juego a pantalla completa).
`Rendering.FrameInterval` es lo que dura un fotograma del monitor.

## El bocadillo

Cuando el asistente dice algo (`Communicate`), el host dibuja el bocadillo con el texto, apuntando a `BubbleAnchor`
(normalmente lo alto de su cabeza; null si no se ve) y siguiéndolo mientras se mueve, el tiempo que se tarda en leer. El
asistente solo hace su gesto de «hablar», o nada: el bocadillo sale igual.

Un clic central sobre el personaje abre la caja para escribirle (y también *Escribir a …* en su menú del botón derecho):
los clics y dobles clics izquierdos son tuyos. Mantener pulsado el botón izquierdo sin moverlo (450 ms) es hablarle: te
llegan `Down` y `Up`, pero ni `Click` ni arrastre. El botón derecho nunca te llega: abre su menú.

### Su propio texto

Con `"speech": "own"` en el manifiesto, lo que dice lo dibujas tú (un bocadillo propio, un cartel, palabras dentro de una
nube…). El host llama a `ShowSpeech(SpeechText)` cada vez que la respuesta crece mientras se escribe (`Finished` false), con
toda cuando está lista (`Finished` true), y otra vez con un `Until` más tarde mientras tu voz la sigue diciendo; cada
llamada sustituye a la anterior. Enséñalo hasta que llegue `HideSpeech`: el host sigue llevando los tiempos, la voz y el
chat.

`Text` es texto plano y llega entero aunque sea largo: córtalo a lo que quepa (todo está en el chat). Si ahora no puedes
(estás escondido, en mitad de una maniobra, no cabe), devuelve `false`: el host enseña su bocadillo el resto de esa
respuesta. En su ficha el usuario puede elegir *Usar siempre el bocadillo de K-Desktop*, y entonces no te llega nada.

### Respuestas largas

Cuando el usuario pide algo largo («explícamelo bien», «hazme una lista») o la respuesta no cabe en un bocadillo, el
bocadillo, tu propio texto y la voz reciben solo un resumen corto; K-Desktop enseña el texto entero en una hoja de lectura
junto a tu `BubbleAnchor`. No tienes que hacer nada.

## Estados

`SetState(state, detail)` dice qué está haciendo el módulo, para que lo enseñes a tu manera (el Lander enciende su antena y
enseña un icono; la plantilla cambia de color):

| `AssistantState` | |
|---|---|
| `Idle` | De vuelta al reposo |
| `Listening` | Su palabra, su tecla para hablarle o el clic mantenido: está escuchando. Con `detail.Activity` = `Dictation`, K-Desktop está escribiendo lo que dice el usuario |
| `Thinking` | La IA está preparando una respuesta |
| `ThinkingHard` | La IA lo piensa más, despacio: puede tardar medio minuto |
| `LoadingBrain` | Se está cargando su modelo en memoria antes de poder pensar |
| `NoBrain` | La orden necesitaba la IA y no hay; llega justo antes de `Speaking`, que lo dice (un encogerse de hombros) |
| `Speaking` | Está contestando |
| `AwaitingConfirmation` | Una pregunta de sí o no espera al usuario (la ventanita que pide permiso) |
| `Busy` | Está en marcha una de sus herramientas o un comando: `detail.Activity` dice cuál (`SearchingWeb`, `ReadingWeb`, `Files`, `Notes`, `Memory`, `Weather`, `Music`, `Timer`, `Screen`, `Command`…; la de un servidor MCP o una del usuario: `Other` con `detail.Tool`) |
| `Sleeping` | |

`detail.Text` lo dice en su idioma («Buscando en internet»), por si lo escribes tú. Versiones posteriores pueden añadir
actividades: trata las que no conozcas como `Other`.

## Sonido

`IAudio`: `Load` (muestras en coma flotante, mono o estéreo, a cualquier frecuencia) o `LoadWav` (del paquete), `Play` con
ganancia y panorama, y `Loop` con ganancia, tono y panorama en vivo (para un motor). El host lo mezcla todo con el volumen
del usuario; el dispositivo solo está abierto mientras suena algo. Con el sonido apagado, `Enabled` es false y todo suena en
silencio.

## Zonas de clic

`Pointer.CreateHitArea(id)`, cada una con su `id`, que llega en `PointerEvent.Area` (con varias —el cuerpo y unos juguetes,
por ejemplo— dales nombres distintos; sin id es `body`). Colócala con una forma (`HitShape`) donde esté el personaje. Sobre
la zona, el cursor es el que digas (`Cursor`: flecha, mano, agarrar) o uno que dibujes: `SetCursorImage(imagen, punto)` con
un `BitmapSource` en píxeles del monitor (los cursores de Windows miden 32 px al 100 %; Kobii enseña un cepillo mientras lo
cepillan); `null` vuelve al de siempre. `OnPointer` recibe entrar, salir, clics y arrastres.

## Archivos que le sueltan

Las zonas de clic aceptan archivos arrastrados desde el Explorador. Llegan a `OnPointer` como `FilesOver` (están encima: el
momento de estirar los brazos), `FilesAway` (se fueron sin soltarlos, o el usuario pulsó Escape) y `FilesDropped` (el
momento de cogerlos), con las rutas completas en `PointerEvent.Files`. Es solo la animación: qué hacer con ellos lo decide
K-Desktop. Nunca se toman como «mover»: el archivo se queda donde estaba.

Mientras los archivos están encima de tu asistente, K-Desktop esconde la imagen que Windows dibuja bajo el cursor y deja
solo una etiqueta pequeña, «Dárselo a <nombre>». Esa imagen te llega en `PointerEvent.Preview` (un `BitmapSource` congelado
en píxeles de pantalla; null si el programa de origen no dio ninguna), para enseñarla a tu manera: más pequeña, en sus
manos, colgando de un rayo… El Lander la sujeta con su rayo tractor, Kobii la levanta sobre las manos y los asistentes sin
código la llevan sobre la cabeza. No enseñarla también vale.

## Teclado y rueda

- `IKeyboard`: `IsDown` siempre; `Capture` hace que K-Desktop se trague esas teclas (pulsadas sin Ctrl, Alt ni Win, para que
  los atajos sigan funcionando) mientras juega, y sus cambios llegan a `KeyChanged`. `Release` al acabar (también se sueltan
  si el asistente se va, el proceso se cae o K-Desktop se pone en pausa).
- `SystemEvent.FastScroll` llega cuando el usuario gira la rueda con fuerza (con la posición del cursor); cada muesca
  (`SystemEvent.WheelTurned`, con la ventana bajo el cursor) solo con `Pointer.WatchWheel = true`: cuesta un mensaje por
  muesca, así que solo mientras importe.

## Ventanas

`IWindowServices`: `TryMove` (la parte visible de una ventana, para arrastrarla fotograma a fotograma) y `TryCommand`
(minimizar, maximizar, restaurar, traer delante —las de un proceso de administrador, y traer delante, las hace K-Desktop por
ti—), `GetCaptionButtons` (de DWM), `Thumbnail` (una imagen congelada de la ventana) y `AppIcon` (el icono de su programa).

## El mundo

- `World.Current`: los monitores (límites, área de trabajo, escalado), las ventanas con sus límites y las barras de tareas;
  `Changed` cuando cambia. `World.System`: el cursor y su velocidad, los segundos sin teclado ni ratón, la ventana de delante,
  la pantalla completa… `OnSystemEvent` para lo que pasa: una ventana que se abre, se mueve, se activa o se cierra, una
  notificación, el usuario que se va o vuelve, la pantalla completa, la energía y la batería, el tema, la hora en punto, la
  papelera vaciada (con dónde está su icono, o null)…
- **Sitio para todos**: di dónde está tu cuerpo con `BodyBounds` (solo el cuerpo: sin partículas, bocadillos ni efectos;
  null si no se ve). `World.Others` dice dónde está cada uno de los demás y si va de camino a algún sitio (`Moving`). Antes de
  posarte, pararte o ir andando a un sitio, mira `World.TakenBy(rectángulo)`: da el que se queda ahí, o null si está libre.
  Pasar por delante vale; quedarse encima, no.
- **Música**: con `World.ListenToMusic(true)`, `World.Music` dice si suena música con ritmo (`Playing`; una voz o un ruido no
  cuentan), sus pulsaciones por minuto (`Bpm`) y el pulso (`BeatAt(tick.Now)`: sube uno por pulso y su parte decimal es la
  fase, 0 = el golpe), para bailar a tiempo. El host lee el medidor de volumen de cada programa (no se graba nada), sin los
  sonidos de los asistentes. Tarda unos 3 s en encontrar el pulso. Deja de escuchar cuando no se te ve.

## Ayudas

- **`Variety<T>`**: para no hacer siempre lo mismo (entradas, reacciones, animaciones de reposo). Cada variante con su peso
  (`Add(x, weight: 2)` sale el doble de veces; 0 = nunca) y su descanso en segundos. `Pick()` elige al azar por peso, nunca la
  misma dos veces seguidas mientras haya otra, y si todas descansan da la que antes esté lista; `TryPick` entonces no da nada.
  `Pick(x => …)` elige solo entre las que valen ahora. `Use`, `CooldownLeft`, `Reset`; con una `seed` la secuencia es siempre
  la misma (pruebas).
- **`Rope`**: una cuerda de Verlet en píxeles de pantalla para ganchos, cables y correas. Tú sujetas su `Start`; su final
  cuelga (`ReleaseEnd`, con la velocidad a la que lo sueltas) o va sujeto (`PinEnd`). Solo se resiste a estirarse, nunca pasa
  de `MaxLength` con el final suelto y avanza a 120 Hz fijos (`Step(tick.Elapsed)`), así se porta igual a cualquier ritmo de
  fotogramas. `Length` para recoger o soltar cable, `Gravity`, `Damping`, `IsTaut`, `Overstretch`, `Pull` (lo que sigue una
  ventana remolcada). `Draw(dc, pen)` la dibuja; `Bounds(margin)` da el tamaño de la superficie.

Todos los tipos, en la [referencia de la API](../../api/KDesktop.Assistant.Sdk.yml) (en inglés).
