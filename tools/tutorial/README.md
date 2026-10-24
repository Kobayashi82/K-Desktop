# Capturas del vídeo tutorial

Scripts para volver a hacer las capturas de `docs/tutorial/captures` (la primera tanda se hizo el 24/10/2026 con el
portable 5.0.188). Todo se hace con una **instancia de demo** de K-Desktop en inglés y tema oscuro, con datos inventados
(un usuario «Alex»), sin tocar los datos reales ni el K-Desktop que está abierto.

## Qué hace falta

- El portable publicado en `artifacts\portable\KDesktop.exe` (la demo lo usa tal cual; no hace falta compilar).
- Node, ffmpeg (para los clips) y las voces SAPI en inglés de Windows (Zira).
- Monitor secundario de 1920×1080 al 100 % a la **izquierda** del principal (en `-1920,278`). Si cambia, ajustar las
  coordenadas de `desk.ps1`, `clip.ps1`, `windowBounds`/`launcherPosition` de `mkdemo.js` y los `grab.ps1` de abajo.
- Los paquetes firmados en `artifacts\assistants\` (Lander, Kobii, Pip).

## Qué queda fuera de los datos reales

`mkdemo.js` crea `config.json` desde cero (nunca copia el real: una carpeta sin config importa la v4 y ejecuta su evento
de inicio). De `%APPDATA%\K-Desktop 5` solo toma el progreso y el aspecto del Lander, los contadores de logros, las
palabras de activación (solo se listan; el micrófono es siempre simulado) y enlaces duros a los modelos de voz. Los modelos
del cerebro salen de `E:\AI Models` (enlaces duros a cuatro con nombre presentable). Las acciones de los comandos son
reales pero solo se ejecutan si se pulsan sus atajos (elegidos para no chocar con los tuyos); los eventos y el K-Board solo
saltan con `eldenring.exe`, que no está.

## Pasos

```powershell
cd tools\tutorial
.\prepare.ps1                      # datos en %TEMP%\kdesktop-tutorial (KD_TUTORIAL para otra carpeta)
.\run.ps1 -Extra --install-assistant,"..\..\artifacts\assistants\lander-0.16.1.kai"
```

En PowerShell, cargar las rutas para usar las funciones (`Get-DemoPid`, `Send-Demo`, `Hide-DemoWindow`):
`. .\_paths.ps1`. `ui.ps1` maneja la interfaz por UI Automation (`-List` enseña lo que hay, `-Name` pulsa, `-Value` escribe,
`-Up 1` actúa sobre el padre de un texto, `-Type ListItem -Select` elige en una lista).

1. **assistant-install**: la ficha de instalación sale sola. `.\wshot.ps1 -ProcessId (Get-DemoPid) -Out …`; desmarcar el
   atajo (`CTRL + ALT + L` coincide con uno tuyo) e `ui.ps1 -Name Install`. Instalar igual Kobii y Pip con
   `Send-Demo --install-assistant <kai>` (la casilla de grabar la palabra de Pip abre el asistente de la palabra: Cancel).
2. **Páginas de la ventana** (PrintWindow, 1920×1080): `.\cap.ps1 -Page <página> -Out <nombre> -Save`.
   | Nombre | Página |
   |---|---|
   | commands | `commands` |
   | quickmenu-editor | `quickmenus` |
   | events, macros, kboard, history | `events`, `macros`, `kboard`, `history` |
   | achievements / stats | `achievements` (Statistics: `ui.ps1 -Name Statistics -Up 1`) |
   | settings-voice, settings-web, settings-launcher, settings-appearance | `settings/voice`, `settings/web`, `settings/launcher`, `settings/appearance` |
   | assistant-list, assistant-settings | `settings/assistants`, `settings/assistant` |
   | brain, tools, tools-builtin, tools-mcp-server | `settings/brain`, `settings/tools`, `settings/tools/builtin`, `settings/tools/servers/github` |
   | assistant-voice, memory, notes, knowledge | `settings/speech`, `settings/memory`, `settings/notes`, `settings/knowledge` |
   | lander-settings, lander-try | `settings/assistants/lander`, `settings/assistants/lander/try` |
   | assistant-panel(-achievements) | `assistant/lander` (antes, tres mensajes en su chat: «What time is it?», «Remember that my sister's birthday is on 12 November», «What should the boiler pressure be?»; el tercero carga el cerebro: liberarlo al acabar) |
3. **Diálogos** (`wshot.ps1` = la ventana «K-Desktop» más pequeña):
   - action-picker: en Commands, `ui.ps1 -Name 'Add action'`.
   - action-window: buscar «Snap left» (`ui.ps1 -Name '#edit' -Value 'Snap left'`), elegir su acción
     (`-Name KDesktop.Core.Model.WindowAction -Type ListItem -Select`), desplegar «Where» por UI Automation (el ComboBox de
     la derecha) y pegar el desplegable sobre la captura con `grab.ps1 -Base` (es otra ventana).
   - pair-qr: `settings/web` → `ui.ps1 -Name 'Pair a device'`.
   - wake-wizard: `settings/voice` → `Add word`, escribir «Computer», Next, Record (las seis tomas sintéticas de
     `fakevoice\` dan «Computer»; con «Pip» Parakeet entendía «P» y «Head»).
4. **Escritorio** (monitor secundario entero con `grab.ps1 -X -1920 -Y 278 -W 1920 -H 1080`; no sale el cursor):
   `.\desk.ps1`, `Hide-DemoWindow` y llevar el ratón al secundario sobre el fondo, no sobre una ventana (si no, salen
   los avisos de enlace de Edge): `.\mouse.ps1 -X -560 -Y 1150`.
   - quickmenu: `Send-Demo --menu 'Main menu'` y pasar el ratón por «Windows» (posición según donde se abrió).
   - launcher / launcher-calc: `Send-Demo --launcher`; texto con `ui.ps1 -Name 'Search commands and programs…' -Value st`
     («st» da Steam, Visual Studio, VS Code, Store y comandos; otras búsquedas sacan programas con nombre en español)
     y luego `128*12+40`.
   - desktop-assistants y achievement-toast: los asistentes aparecen junto al cursor al ponerlos (Panel → Hide y
     Put on the desktop, con la ventana minimizada). Con el ratón sobre el fondo, `.\clip.ps1 -Command 'Lander: say hello'
     -Name say -Frames 16`: el Lander vuela al cursor y habla; como es el comando nº 1000, salta el logro «Commander».
   - voice-osd / voice-heard: mantener pulsado el clic sobre Kobii o Pip (`mouse.ps1 -X … -Y … -Down`, capturas,
     `-Up`). La tecla de voz no sirve: K-Desktop ignora las teclas inyectadas. La frase simulada es «Open the
     calculator.» (abre la Calculadora: cerrarla después).
5. **Clips**: `clip.ps1`. Los comandos ocultos `Clip: …` piden al Lander mover solo las ventanas de Edge de la demo.
   **Nunca** pedirle «esta ventana» ni «Snap left»: actúa sobre la ventana de delante, que puede ser una tuya.
6. Cerrar todo: `Send-Demo --exit`, `.\desk.ps1 -Stop`, y `.\cleanup.ps1` (borra la carpeta de trabajo, `Documents\Manuals`,
   `Documents\MCP` y `E:\AI Models`; los enlaces duros no tocan los modelos).

## Trampas vistas

- PowerShell no distingue mayúsculas: `$c` pisa a `$C`, `$s` a `$S`.
- Edge abierto con `--window-size` respeta el tamaño; moverlo después con SetWindowPos lo estira a 1460 px de alto.
  Y al remolcar una ventana de Edge, una de cada dos capturas (gdigrab y CopyFromScreen) la enseña en su sitio antiguo
  aunque ya esté quieta: el clip del remolque no sirvió.
- Hacer clic en el fondo lo trae delante de las ventanas: `winctl.ps1 -Title Desktop -Bottom`.
- Antes de la 5.0.189, los comandos de «Asistente: preguntar/decir/cerebro» no tienen icono (en 2026-10 se les puso a
  mano en la configuración de la demo).
