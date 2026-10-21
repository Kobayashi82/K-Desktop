# Crear un asistente

Un asistente es un paquete `.kai`: un `.zip` con su manifiesto (`assistant.json`), sus imágenes y ajustes, y
su código en `lib/`. Se escribe en C# contra el SDK (`src/KDesktop.Assistant.Sdk`), que K-Desktop pone siempre: el
paquete nunca lo lleva. Mientras el SDK no esté en NuGet se usa por referencia a una copia de este repositorio.

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

## El ejemplo

`samples/DotAssistant` (Dot) es el asistente mínimo completo: un punto que se teletransporta, se puede arrastrar,
reacciona a la papelera, a los avisos, a la hora en punto y al scroll fuerte, suena al saltar (un sonido hecho en
código), tiene un extra (`dot.jump`), ajustes con un botón para ese extra y otro para «Poner a cero los saltos», una
tarjeta y un botón en el panel, y guarda sus datos en su carpeta.

## Lo que da el host

- **Bocadillo:** cuando el asistente dice algo (`Communicate`), el host dibuja el bocadillo con el texto, apuntando a
  `BubbleAnchor` (lo normal, lo alto de la cabeza; null si no se ve) y siguiéndolo mientras se mueve, el tiempo que
  se tarda en leerlo. El asistente solo hace su gesto de «hablar» (o nada: el bocadillo sale igual). El clic central
  sobre el personaje abre la caja para escribirle (y su menú del clic derecho, *Escribir*): el clic y el doble clic
  izquierdos son tuyos. Mantener pulsado el botón izquierdo sin moverlo (450 ms) es hablarle: recibes `Down` y `Up`,
  pero no `Click` ni arrastre.

- **Estado** (`SetState`): K-Desktop te dice si te está escuchando (`Listening`: su palabra, la tecla para hablar o el
  clic mantenido), si piensas (`Thinking`), si hablas (`Speaking`) o si vuelves a reposo (`Idle`). Enséñalo a tu
  manera (el Lander enciende la luz de su antena; Dot se rodea de un anillo blanco al escuchar y late al pensar).

- **Sonido** (`IAudio`): `Load` (muestras en coma flotante, mono o estéreo, a cualquier frecuencia) o `LoadWav` (del
  paquete), `Play` con ganancia y panorama, y `Loop` con ganancia, tono y panorama en vivo (para un motor). El host lo
  mezcla todo bajo el volumen de Opciones → Asistente → General; el dispositivo solo se abre mientras suena algo. Con el
  sonido apagado, `Enabled` es falso y todo suena a silencio.
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
  `SetVoiceMood("happy")` o `SetVoiceMood(null)` cambian el ánimo de lo que diga a partir de entonces; `far` lo pone el host
  solo cuando habla desde otro monitor que el del ratón. Su personalidad, en `persona/{lang}.json`.

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
