# Gestos

El módulo le pide **gestos** al asistente: lo que quiere conseguir, nunca cómo. Cada asistente decide cómo los hace —el
Lander dispara al botón de minimizar, Kobii le tira una pelota, Kumo lo toca con el dedo—. Un asistente que solo sabe
`Communicate` y `React` ya funciona: para lo demás, el módulo enseña el bocadillo y hace él la acción.

```csharp
public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct) { … }
```

Pon los que sabe en `traits.gestures` de su manifiesto. `CanPerform` dice si puede hacer uno *ahora* (sí por defecto);
`GestureOutcome.NotPerformed` dice que no igual de bien. Nunca lances una excepción.

## Los gestos

| Gesto | Qué pide |
|---|---|
| `PointAt(Place)` | Señalar una ventana, un punto, una zona, la bandeja o el reloj (un foco, un brazo…) |
| `GoTo(Place)` | Ir a algún sitio (volando, andando, teletransportándose) |
| `PressButton(Window, Button, ButtonBounds)` | Pulsar un botón de la barra de título (minimizar, maximizar/restaurar, cerrar) |
| `MoveWindow(Window, Destination)` | Llevar una ventana allí (una maximizada llega ya restaurada por un `PressButton(Restore)` anterior) |
| `ActivateWindow(Window)` | Traer una ventana delante (posarse en ella, llamar a la puerta…) |
| `WaitForWindow(Process, Title)` | Se acaba de abrir algo: esperar su ventana. `ctx.Appeared` la da (o null) |
| `Mark(Place, Duration, Text)` | Dejar una marca en algún sitio un rato (una baliza, una banderita, un despertador), con una etiqueta opcional |
| `Scan(Area)` | Barrer una zona (una captura, leer la pantalla) |
| `Typing(Window)` | Hacer como que teclea en una ventana (se le están mandando teclas o texto) |
| `Communicate(Text, Icon)` | Quedarse a la vista mientras el host enseña el texto en el bocadillo. `Icon` dice de qué va cuando se sabe (`clock`, `folder`, `web`, `volume`, `power`…; enseña sin imagen los que no conozcas) |
| `React(Reaction)` | Una reacción corta: `Success`, `Warning`, `Failure`, `Celebration`, con su [contexto](#reacciones) |
| `Announce(Reason, Cursor, UserAway)` | [Llamar la atención](#llamar-la-atención) antes de decir algo por su cuenta |

## Los comandos del usuario

Cuando el usuario le pide a un asistente que ejecute un comando de K-Desktop, cada acción llega como uno o varios gestos:
minimizar → `PressButton(Minimize)`, colocar una ventana → `MoveWindow` con el destino exacto, una maximizada primero
`PressButton(Restore)`…

- Llama a **`ctx.Commit()` en el instante del efecto** (cuando el disparo da en el blanco): la acción pasa justo entonces, y
  `ctx.Committed` te dice si fue bien.
- O hazlo tú (`Windows.TryMove` fotograma a fotograma) y devuelve **`PerformedSelf`**: K-Desktop la deja luego al píxel.
- Lo que no sabes hacer (`CanPerform` false) lo enseña el host con el bocadillo mientras lo hace K-Desktop.
- Tienes **`ctx.TimeLeft`**: si te pasas, fallas o no contestas, K-Desktop lo hace igual y te pide un `React(Warning)`. Una
  orden nunca se queda a medias; el token salta cuando se acaba el tiempo o el usuario dice «para».
- **`ctx.Speed`** es la rapidez que eligió el usuario (`Showy`, `Normal`, `Fast`): en `Fast`, `Commit` antes de medio segundo.
- `ctx.Step` y `ctx.StepCount` dicen qué paso de la orden es.

```csharp
if (gesture is Gesture.PressButton press)
{
    // FlyNextToAsync y ShootAsync son animaciones propias del asistente.
    await FlyNextToAsync(press.ButtonBounds, ctx.TimeLeft, ct);
    await ShootAsync(press.ButtonBounds, ct);
    ctx.Commit();                       // la ventana se minimiza ahora, en el impacto
    return await ctx.Committed ? GestureOutcome.Committed : GestureOutcome.NotPerformed;
}
```

Con *Enseñar también los comandos que lanzas tú* encendido, los comandos lanzados de otras formas (un atajo, un QuickMenu,
el móvil) también te llegan como gestos, siempre en `Fast`: son solo adorno, nada los espera y su `Commit` no hace nada.

## Las herramientas de la IA

Lo que hacen las herramientas de la IA también te llega como gestos, con `ctx.Step` 0:

| Herramienta | Gesto |
|---|---|
| Un temporizador | `Mark` junto al reloj (`Place.Clock`) con la hora a la que suena; quitarlo → `PointAt(Place.Clock)` |
| Abrir una web, una búsqueda, un programa o un archivo | `WaitForWindow`, justo antes de abrirlo |
| Qué suena, controlar la música | `GoTo(Place.Tray)` |
| «Avísame cuando se cierre …» | `Mark` sobre esa ventana |
| Reescribir un texto y pegarlo | `Typing` en esa ventana |
| Leer la pantalla | `Scan` de lo leído |

Las herramientas JSON pueden pedir uno con `"gesture"`. Son solo adorno: la herramienta nunca te espera, `Commit` no hace
nada, un paso de una orden ocupa su lugar y uno nuevo sustituye al anterior. Lo que no sabes hacer no se enseña.

## Llamar la atención

Antes de decir algo por su cuenta (un temporizador que suena, un «avísame cuando» que se cumple, *Asistente: decir*) el host
pide `Announce`: acercarse al ratón, saludar, parpadear… `Reason` dice por qué (`Timer`, `Watch`, `Message`), `Cursor` dónde
estaba el ratón (o null) y `UserAway` cuántos segundos lleva sin teclado ni ratón (cuantos más, más grande puede ser: a lo
mejor no está mirando). El host te espera, `ctx.TimeLeft` como mucho (unos 2 s, 3 en `Showy` y medio segundo en `Fast`), y
luego habla con `Communicate`: acaba en cuanto hayas llamado la atención. No hay nada que confirmar.

## Reacciones

`Gesture.React` lleva `Context` (`ReactionContext`), que rellena el host:

| | |
|---|---|
| `Source` | `Order` (un comando o una acción de K-Desktop), `Tool` (un temporizador, una nota, internet…), `Notice` (algo que dice por su cuenta), `User` (lo que el usuario dice de él: «gracias», «bien hecho» → `Celebration`; «está mal» → `Failure`), o `Unknown` |
| `Streak` | Cuántas seguidas del mismo tipo (1 la primera; vuelve a 1 con otro tipo o tras media hora) |
| `FirstToday` | La primera de hoy |
| `What` | Qué era, cuando se sabe (`set_timer`, `run_command`, `timer`, `praise`…) |

El host reacciona una vez por orden, cuando la respuesta está lista: algo hecho y nada fallido → `Success`; hecho pero algo
falló → `Warning`; nada hecho y algo falló → `Failure`. Solo consultar algo (la hora, el tiempo) no reacciona. Úsalo con
`Variety<T>` para no reaccionar siempre igual: algo más grande la primera vez del día o en una racha larga, más alicaído al
tercer fallo seguido.
