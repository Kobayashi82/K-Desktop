# Crear un asistente

Un asistente es un paquete `.kdassistant`: un `.zip` con su manifiesto (`assistant.json`), sus imágenes y ajustes, y
su código en `lib/`. Se escribe en C# contra el SDK (`src/KDesktop.Assistant.Sdk`), que K-Desktop pone siempre: el
paquete nunca lo lleva. Mientras el SDK no esté en NuGet se usa por referencia a una copia de este repositorio.

## Empezar

```powershell
dotnet new install "<K-Desktop>\templates\kdassistant"
dotnet new kdassistant -n MiAsistente --sdk "<K-Desktop>\src\KDesktop.Assistant.Sdk" --authorName "Yo"
cd MiAsistente
dotnet build          # → bin\Debug\…\miasistente-1.0.0.kdassistant
```

El proyecto tiene:

| | |
|---|---|
| `MiAsistente.cs` | La clase que implementa `IAssistant`: se dibuja, tiene una zona de clic y sabe `Communicate` y `React` |
| `Package\assistant.json` | El manifiesto (esquema en `docs/assistant/esquemas/assistant.schema.json`): id, versión, entrada, gestos… |
| `Package\…` | Todo lo demás que va en el paquete tal cual: `icon.png`, `settings.json`, `panel.json`, `persona\`, `voice\`, `assets\` |

Cada compilación deja en `bin\…\package\` el paquete tal y como se instala y, al lado, el `.kdassistant`.

## Probar sin instalar

```powershell
KDesktop.exe --assistant-dev "<carpeta del proyecto>"
```

Carga el `bin\…\package` más reciente (una copia, para que se pueda recompilar) y lo **recarga al recompilar**. Si
K-Desktop está abierto con la misma carpeta de datos, se conecta a él; si no, funciona solo. El registro está en
`Logs\assistant.log` de la carpeta de datos. `KDesktop.exe --assistant --exit` lo cierra.

## El ejemplo

`samples/DotAssistant` (Dot) es el asistente mínimo completo: un punto que se teletransporta, se puede arrastrar,
reacciona a la papelera, a los avisos y a la hora en punto, tiene tres ajustes y una tarjeta en el panel, y guarda
sus datos en su carpeta.

## Reglas

- Todo se llama en el **hilo del motor** del host, y los `await` vuelven a él. No hace falta bloquear nada; nunca
  se bloquea ese hilo.
- `Tick` devuelve **cuánto puede dormir**: en reposo, segundos; animándose, `Rendering.FrameInterval`. Solo se
  dibuja (`Present`) cuando algo cambia: el último fotograma se queda en pantalla.
- Coordenadas en **píxeles físicos** de la pantalla virtual. Para el tamaño, el escalado del monitor
  (`MonitorInfo.DpiScale`).
- Un asistente que falla cinco veces seguidas se descarga; uno que hace caer el proceso tres veces seguidas se
  desactiva. Al descargarlo, el host retira lo que haya dejado (superficies, zonas de clic, suscripciones).
- El asistente no ve la IA, las herramientas, la tubería ni K-Desktop: solo el SDK.
