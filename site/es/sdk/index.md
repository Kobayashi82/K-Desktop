# Crear asistentes

Un asistente es un paquete `.kai`: un `.zip` con su manifiesto (`assistant.json`), sus imágenes y ajustes y —si tiene
código— sus ensamblados en `lib/`. Hay dos tipos:

| | Qué es | Para |
|---|---|---|
| **[Sin código](no-code.md)** | Imágenes (fotogramas PNG) y un `character.json`. Lo mueve K-Desktop: anda por la barra de tareas y por encima de las ventanas, se cae, duerme y hace una animación para cada cosa | Artistas: un personaje en una tarde, sin programar |
| **[Con código](getting-started.md)** | Una biblioteca de clases en C# contra el **SDK** (`KDesktop.Assistant.Sdk`), dibujada con la API de dibujo de WPF | Cualquier cosa: volar, física, juegos, maniobras sobre las ventanas, ajustes y panel propios |

En los dos casos el asistente habla, piensa y usa herramientas como cualquier otro: la IA, las herramientas, la voz y el
reconocimiento de voz son de K-Desktop. Un asistente **nunca ve la IA, las herramientas ni K-Desktop**: recibe el mundo
(ventanas, monitores, barra de tareas, los demás asistentes), sus servicios y **gestos** que hacer (pulsa el botón de
minimizar de esta ventana, lleva aquella ventana allí, di esto, reacciona a cómo ha ido). Cada asistente decide cómo los
hace.

> [!NOTE]
> El SDK va por la versión **1.0**: a partir de aquí solo crece sin romper nada. Un paquete hecho con el SDK 1.x funciona
> en cualquier K-Desktop con el SDK 1.y, y ≥ x. Está en NuGet:
> [KDesktop.Assistant.Sdk](https://www.nuget.org/packages/KDesktop.Assistant.Sdk), y su plantilla,
> [KDesktop.Assistant.Templates](https://www.nuget.org/packages/KDesktop.Assistant.Templates).

## Ejemplos

| | |
|---|---|
| [`templates/kdassistant`](https://github.com/Kobayashi82/K-Desktop/tree/main/templates/kdassistant) | La plantilla de `dotnet new`: el asistente con código más pequeño y completo. Se dibuja, tiene zona de clic, habla, reacciona, cambia de color con cada estado y enseña un arcoíris en su escaparate |
| [`samples/CharacterAssistant`](https://github.com/Kobayashi82/K-Desktop/tree/main/samples/CharacterAssistant) | **Pip**, un asistente sin código: imágenes y JSON |
| [K-Desktop-Assistants](https://github.com/Kobayashi82/K-Desktop-Assistants) | El Lander, Kobii y Kumo: asistentes completos con código |

## El paquete

```
miasistente-1.0.0.kai
  assistant.json        el manifiesto
  icon.png
  lib\MiAsistente.dll   su código (o character.json en uno sin código)
  settings.json         su página de ajustes (opcional)
  panel.json            su panel (opcional)
  persona\es.json …     su personalidad y sus frases (opcional)
  voice\miasistente.json  su perfil de voz (opcional)
  assets\ …             todo lo demás
  signature.json        si va firmado
```

## El manifiesto

`assistant.json` ([esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/assistant.schema.json)):

```json
{
  "$schema": "https://raw.githubusercontent.com/Kobayashi82/K-Desktop/main/src/KDesktop.Core/Assistant/Packages/Schemas/assistant.schema.json",
  "id": "miasistente",
  "name": "MiAsistente",
  "version": "1.0.0",
  "author": "Yo",
  "description": { "es": "Un asistente nuevo para K-Desktop.", "en": "A new assistant for K-Desktop." },
  "sdk": "1.0",
  "entry": "lib/MiAsistente.dll!MiAsistente.MiAsistente",
  "icon": "icon.png",
  "wakeWord": "MiAsistente",
  "common": ["size", "volume", "liveliness", "dragging"],
  "traits": {
    "travel": "fly",
    "canMoveWindows": true,
    "gestures": ["pressButton", "moveWindow", "communicate", "react", "announce"]
  }
}
```

| Campo | |
|---|---|
| `id`, `name`, `version`, `author`, `description`, `homepage`, `license` | Quién es. `description` puede ser un texto o uno por idioma (`{ "es": "…", "en": "…" }`). Instalar uno con el mismo `id` y una `version` mayor lo actualiza. Ids reservados: `data`, `settings`, `dev`, `kdesktop` |
| `sdk` | El SDK con el que se compiló (`"1.0"`) |
| `entry` | `lib/Archivo.dll!Espacio.De.Nombres.Tipo` (la clase que implementa `IAssistant`), o `character.json` en uno sin código |
| `icon`, `preview` | Su icono y una imagen para su ficha |
| `wakeWord` | Nombre sugerido para llamarlo: al instalarlo, K-Desktop ofrece grabarlo con la voz del usuario |
| `model` | Modelos de IA sugeridos (ver [Su modelo](getting-started.md#su-modelo)) |
| `voice`, `persona` | Su perfil de voz y su personalidad (ver [Voz y personalidad](getting-started.md#voz-y-personalidad)) |
| `settings`, `panel` | Su página de ajustes y su panel (`settings.json` y `panel.json` por defecto) |
| `traits` | Cómo viaja (`fly`, `walk`, `teleport`), si puede mover ventanas y qué [gestos](gestures.md) sabe hacer (`communicate` y `react` se dan siempre por sabidos) |
| `common` | Los [ajustes comunes](getting-started.md#ajustes-comunes) que usa |
| `speech` | `"own"` para dibujar él mismo lo que dice (ver [Su propio texto](host.md#su-propio-texto)) |
| `extras` | Sus acciones propias (ver [Extras](getting-started.md#extras)) |
| `updates`, `changes` | Dónde está su última versión y qué trae esta (ver [Publicar](publishing.md#versiones-nuevas)) |
| `showcase` | Vídeos de lo que sabe enseñar, para el catálogo (ver [Escaparate](showcase.md#vídeos-para-el-catálogo)) |

Los demás archivos también tienen su esquema:
[settings](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/settings.schema.json),
[panel](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/panel.schema.json),
[persona](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/persona.schema.json),
[voice](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/voice.schema.json),
[character](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/character.schema.json) y
[catalog](https://github.com/Kobayashi82/K-Desktop/blob/main/src/KDesktop.Core/Assistant/Packages/Schemas/catalog.schema.json).

## Instalarlo

Con el módulo Asistente encendido, un doble clic en el `.kai` (o `KDesktop.exe --install-assistant "archivo"`) abre su
ficha en K-Desktop. Antes de enseñarla, K-Desktop comprueba el paquete **sin cargar su código**: rutas y tamaños seguros; el
manifiesto, los ajustes, el panel, la personalidad y la voz contra sus esquemas; que estén la entrada, las imágenes y los
vídeos del escaparate; la versión del SDK; la [firma](publishing.md#firmar-el-paquete); y que el id no esté reservado. Si
algo está mal, la ficha dice qué.

Se instala en `Assistants\<id>\<versión>\` de la carpeta de datos de K-Desktop (`%APPDATA%\K-Desktop 5`). Una versión nueva
sustituye a la anterior —en caliente, si está en el escritorio— y conserva sus datos (`Assistants\Data\<id>\`) y sus ajustes.
Las copias de seguridad de K-Desktop guardan esos datos y la lista de asistentes instalados, no los paquetes.

## Cómo se ejecuta

- Los asistentes van en **su propio proceso** (`KDesktop.exe --assistant`, como usuario normal, nunca como administrador),
  cada uno en su propio `AssemblyLoadContext`. K-Desktop solo lee el manifiesto.
- Puede haber varios asistentes a la vez en el escritorio, en el mismo proceso y en la misma capa de dibujo.
- Un asistente que falla cinco veces seguidas se descarga (solo él; los demás siguen); uno que tira el proceso tres veces
  seguidas se quita del escritorio. Al descargarlo, el host quita todo lo que dejó (superficies, zonas de clic,
  suscripciones).
