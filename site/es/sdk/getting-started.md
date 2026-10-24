# Con código

Un asistente con código es una biblioteca de clases de .NET (`net10.0-windows`, WPF) que implementa `IAssistant` del SDK.
El SDK lo pone siempre K-Desktop: un paquete nunca lo lleva.

## Empezar con la plantilla

Hace falta el SDK de .NET 10. La plantilla y el SDK vienen de NuGet
([KDesktop.Assistant.Templates](https://www.nuget.org/packages/KDesktop.Assistant.Templates),
[KDesktop.Assistant.Sdk](https://www.nuget.org/packages/KDesktop.Assistant.Sdk)):

```powershell
dotnet new install KDesktop.Assistant.Templates
dotnet new kdassistant -n MiAsistente --authorName "Yo"
cd MiAsistente
dotnet build          # → bin\Debug\…\miasistente-1.0.0.kai
```

Para compilar contra una copia del [repositorio de K-Desktop](https://github.com/Kobayashi82/K-Desktop) (para probar un
cambio en el SDK), añade `--sdk "<K-Desktop>\src\KDesktop.Assistant.Sdk"`.

| | |
|---|---|
| `MiAsistente.cs` | La clase que implementa `IAssistant`: se dibuja, tiene una zona de clic y sabe `Communicate`, `React` y `Announce` |
| `Package\assistant.json` | El [manifiesto](index.md#el-manifiesto) |
| `Package\…` | Todo lo demás que va en el paquete tal cual: `icon.png`, `settings.json`, `panel.json`, `persona\`, `voice\`, `assets\` |

Cada compilación deja el paquete en `bin\…\package\` tal como se instala y, al lado, el `.kai` (firmado, si tienes clave:
ver [Firmar](publishing.md#firmar-el-paquete)).

## Probarlo sin instalarlo

```powershell
KDesktop.exe --assistant-dev "<carpeta del proyecto>"
```

Carga el `bin\…\package` más nuevo (una copia, para que puedas seguir compilando) y **lo vuelve a cargar en cada
compilación**. Si K-Desktop está abierto con la misma carpeta de datos, se conecta a él; si no, va solo. El registro es
`Logs\assistant.log` de la carpeta de datos. `KDesktop.exe --assistant --exit` lo cierra.

## El asistente más pequeño

Solo unos pocos miembros son obligatorios; todos los demás tienen un valor por defecto que no hace nada (o dice que no).

```csharp
public sealed class MiAsistente : IAssistant
{
    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private Point _at;
    private bool _dirty = true;

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - 20);

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
    }

    public void Enter(EntranceKind how)
    {
        var work = _host.World.Current.PrimaryMonitor?.WorkArea ?? new PixelRect(0, 0, 800, 600);
        _at = new Point(work.Right - 200, work.Bottom - 24);
    }

    public Task ExitAsync(ExitKind why, CancellationToken ct) => Task.CompletedTask;

    public double Tick(TickInfo tick)
    {
        if (_dirty)
        {
            _dirty = false;
            var area = new PixelRect((int)_at.X - 24, (int)_at.Y - 24, (int)_at.X + 24, (int)_at.Y + 24);
            _surface.Present(area, dc => dc.DrawEllipse(Brushes.MediumPurple, null, _at, 18, 18));
            _hit.Place(new HitShape.Ellipse(area));
        }
        return 30;   // nada se mueve: a dormir (lo que importa llama a RequestTick)
    }

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (gesture is Gesture.Communicate)
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        return GestureOutcome.Committed;
    }

    public void SetState(AssistantState state, StateDetail detail) { }

    public void Dispose()
    {
        _hit.Dispose();
        _surface.Dispose();
    }
}
```

## Reglas

- Todo se llama en el **hilo del motor** del host, y los `await` vuelven a él. No hace falta bloquear nada; nunca bloquees
  ese hilo.
- `Tick` devuelve **cuánto puede dormir**: en reposo, segundos; mientras anima, `Rendering.FrameInterval`. Solo dibuja
  (`Present`) cuando algo cambia: el último fotograma se queda en pantalla, aunque se pierda la GPU o acabe un juego a
  pantalla completa.
- Las coordenadas son **píxeles físicos** de la pantalla virtual. Para los tamaños, el escalado del monitor
  (`MonitorInfo.DpiScale`).
- **No estás solo**: puede haber otros asistentes a la vez en el escritorio, en el mismo proceso y en la misma capa. No
  ocupes toda la pantalla; `World.Others` dice dónde están los demás. Lo que te llega (gestos, órdenes, extras, ajustes) es
  solo tuyo.
- Nunca lances una excepción para decir que no: devuelve `GestureOutcome.NotPerformed`. Una excepción se apunta en el
  registro; varias seguidas descargan el asistente.

## Entrar y salir

`Enter(how)` y `ExitAsync(why, ct)` dicen **por qué** llega o se va:

| `EntranceKind` | Cuándo | Qué hacer |
|---|---|---|
| `Startup` | Arranca K-Desktop o se enciende el módulo | Su entrada |
| `Summoned` | El usuario lo pone en el escritorio | Una entrada que diga «aquí estoy» |
| `Reloaded` | Estaba hace un momento y vuelve: una versión nueva, otro idioma, el proceso se reinició o se recuperó de una caída, se recompiló con `--assistant-dev` | Volver a salir donde estaba, sin espectáculo |

| `ExitKind` | Cuándo | Qué hacer |
|---|---|---|
| `Shutdown` | Se cierra K-Desktop o se apaga el módulo | Irse al momento |
| `Dismissed` | El usuario lo esconde (Opciones, la bandeja, su menú) | Su despedida |
| `Uninstalling` | Se está quitando (su carpeta de datos puede borrarse justo después) | La última despedida |
| `Reloading` | Vuelve enseguida con `Reloaded` | Irse al momento y guardar en `DataDirectory` dónde estaba: el nuevo puede ser otro proceso |

El host espera la salida 3 s como mucho (luego se cancela el token).

## Ajustes comunes

Lo que todos los asistentes tienen igual no va en su `settings.json`: K-Desktop lo enseña en la pestaña *Básico* de su
ficha, con las mismas unidades para todos. El manifiesto dice cuáles usa:

```json
"common": ["size", "volume", "liveliness", "dragging"]
```

| Nombre | Qué es | Quién lo hace |
|---|---|---|
| `size` | Tamaño, del 50 % al 200 % | El asistente: `host.Common.Size` (1 = el tamaño para el que está dibujado), por encima del escalado del monitor |
| `volume` | Volumen de sus sonidos, del 0 % al 100 % | El host, en todo lo que suena por `IAudio` (su voz va aparte) |
| `liveliness` | Lo movido que es, del 0 % (lo menos posible) al 200 % | El asistente: `host.Common.Liveliness` (1 = normal) en lo que hace por su cuenta |
| `dragging` | Cogerlo con el ratón | El host no pasa los arrastres de su zona de clic `body` si está apagado; `host.Common.Dragging` para lo demás |

Cuando cambian, llega `OnSettingsChanged` con `host.Common` ya al día.

## Ajustes y panel

K-Desktop los dibuja con sus propios controles, sin cargar nada del código del asistente:

- **`settings.json`** ([esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/settings.schema.json))
  → sus pestañas en su ficha de *Opciones → Asistentes* (o en su menú del botón derecho → *Ajustes*). Páginas (pestañas si
  hay varias), secciones y controles: `toggle`, `slider`, `number`, `choice`, `text`, `color`, `gallery`, `apps`, `hotkey`,
  `button` (lanza un extra o, con `resetData`, borra su carpeta de datos tras preguntar), `info` y `keys`. `enabledWhen`
  atenúa un control mientras un interruptor está apagado. Lo que cambia el usuario se guarda (solo lo que difiere del valor
  por defecto) y, si está en el escritorio, le llega al momento: `OnSettingsChanged` con **todos** los valores (también en
  `host.Settings`).
- **`panel.json`** ([esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/panel.schema.json))
  → *Panel → Asistente*, debajo de su tarjeta: `stat` (en fila), `progress`, `list`, `achievements`, `buttons` y `text`. Los
  datos llegan con `host.PublishPanel`, que se **fusiona** con lo publicado antes: publica todo en `Enter` y luego solo lo
  que cambie. Una galería con `source` toma sus imágenes de esos mismos datos
  (`[{ id, name, image, locked, lockText }]`).

## Extras

Los `extras` del manifiesto son sus acciones propias: salen en su menú del botón derecho, en su panel y como **Asistente:
acción** en los comandos de K-Desktop; con `hotkey`, la ficha de instalación ofrece crear un comando con ese atajo. Llegan
a `OnExtraAsync`.

- `host.SetExtraState(id, Shown | Disabled | Hidden)` enseña, atenúa o esconde un extra en el menú y en los botones del
  panel; tras cada carga vuelven todos a `Shown`. Los comandos y la voz siguen llegando a `OnExtraAsync` aunque esté
  escondido: explica ahí por qué no se puede.
- `host.ExtraHotkey(id)` da el atajo del comando de K-Desktop que lo lanza («CTRL + ALT + L»), para decirle al usuario qué
  pulsar.

## Voz y personalidad

- **Su voz**: lo que contesta lo dice el host en voz alta, con el perfil `voice` de su manifiesto (`voice/<id>.json`,
  [esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/voice.schema.json)): un motor y una
  cadena de efectos —`pitch`, `formant`, `ring`, `vocoder`, `bitcrush`, `radio`, `chorus`, `flanger`, `echo`, `glitch`— y
  `moods`, los efectos que se añaden en algunas situaciones. `OnVoiceLevel` dice lo fuerte que suena mientras habla (para
  luces o una boca); `host.SetVoiceMood("error")`, `"happy"` o `null` cambian el ánimo de lo que dice desde entonces.
- **Su personalidad**: `persona/{lang}.json`
  ([esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/persona.schema.json)), lo que le dice a
  la IA quién es y cómo habla, y sus frases fijas. Sin el idioma del usuario, el inglés o el primero que haya.
- **Lo que cambia el usuario**: en *Personalidad y voz* el usuario puede escribirle otra personalidad (tus frases fijas se
  quedan) y darle otro idioma y otra voz. Con otro idioma, `host.Language` es ese y no el de K-Desktop: léelo en `Attach`
  (cuando cambia, el host vuelve a cargar el asistente).

## Su modelo

`"model": { "suggested": [...] }` en el manifiesto: modelos de Hugging Face («dueño/repo/archivo.gguf» o
«dueño/repo:Q4_K_M»), el mejor primero. K-Desktop ofrece (desmarcado) el primero que cabe en la gráfica del usuario, con su
proyector de visión si lo tiene, y solo si ese archivo no está ya; una vez descargado, ese asistente piensa con él. El
Lander, por ejemplo:

```json
"model": { "suggested": ["unsloth/Qwen3.5-9B-GGUF:Q4_K_M", "unsloth/Qwen3.5-4B-GGUF:Q4_K_M"] }
```

`"think"` (también uno o varios) sugiere el modelo con el que **piensa más**: se ofrece aparte, también desmarcado. Si es el
mismo archivo que `suggested`, no se ofrece dos veces; sin él, piensa más con su modelo de siempre.

## Lo siguiente

- [Lo que da el host](host.md): bocadillo, estados, sonido, zonas de clic, archivos soltados, teclado, ventanas, música…
- [Gestos](gestures.md): hacer los comandos del usuario y las herramientas de la IA.
- [Escaparate](showcase.md): la pestaña *Probar* y los vídeos del catálogo.
- [Referencia de la API](../../api/KDesktop.Assistant.Sdk.yml) (en inglés).
