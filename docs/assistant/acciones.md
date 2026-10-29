# Acciones de K-Desktop → gestos y riesgo

Tabla del coreógrafo general (§11.3 del plan) y del riesgo de cada acción (§6.1). La fuente de verdad es el código:
`src/KDesktop.Core/Assistant/ActionGestures.cs` y `ActionRisk.cs`. Las pruebas (`AssistantMapTests`) fallan si
aparece un `ActionKind` sin gesto o sin riesgo.

- **Gestos:** lo que se pide al asistente mientras corre el paso. El marcado con ★ es el que **hace** la acción
  (el asistente llama a `Commit` en el momento del efecto, o la hace él y devuelve `PerformedSelf`); los demás solo
  llevan hasta él. Lo que el asistente no sepa hacer se cambia por `communicate` y K-Desktop hace la acción.
- **Riesgo:** 🟢 sin preguntar · 🟡 pregunta la primera vez · 🔴 pregunta siempre. Un **comando** tiene el riesgo
  más alto de sus acciones activas, de los comandos que ejecuta y de las dos ramas de cada «Si…», y nunca menos de
  🟡 (se confirma la primera vez), salvo que lo marques como de confianza.

| Acción | Variante | Gestos | Riesgo |
|---|---|---|---|
| Ventana (`Window`) | Minimizar / Maximizar / Restaurar | `pressButton` ★ (su botón) | 🟢 |
| | Alternar maximizar | `pressButton` ★ (restaurar si está maximizada, si no maximizar) | 🟢 |
| | Ajustar, Centrar, Mover al monitor | `moveWindow` ★ — si está maximizada, antes `pressButton(restaurar)` | 🟢 |
| | Transparencia | `pointAt` ★ | 🟢 |
| Controlar aplicación (`AppControl`) | Activar | `activateWindow` ★ | 🟢 |
| | Cerrar | `pressButton(cerrar)` ★ | 🟡 |
| | Esperar | `waitForWindow` ★ | 🟢 |
| Abrir programa, carpeta, web (`RunApp`, `OpenFolder`, `OpenUrl`) | | `waitForWindow` ★ (se posa en la ventana nueva) | 🟢 |
| Volumen, Salida de audio, Radio, Plan de energía, Mantener despierto, Tema de Windows, Pantalla | | `goTo(bandeja)` → `communicate` ★ (icono de cada una) | 🟢 (apagar pantalla o radio: 🟡) |
| Siempre visible (`TopMost`) | | `mark` ★ (la «chincheta») | 🟢 |
| Captura (`Screenshot`) | | `scan` ★ | 🟢 |
| Lupa (`Magnifier`) | | `pointAt(punto)` ★ | 🟢 |
| Enviar pulsación (`SendInput`) | Teclas, texto | `typing` ★ | 🔴 |
| | Mover el ratón | `goTo(punto)` ★ | 🔴 |
| Macro (`PlayMacro`) | | `typing` ★ | 🔴 |
| Energía (`Power`) | | `communicate` ★ (cuenta atrás) | 🔴 (bloquear: 🟢, desde la 5.0.352: no se pierde nada) |
| Script (`RunScript`) | | `communicate` ★ | 🔴 |
| Petición web (`HttpRequest`) | | `communicate` ★ | 🔴 |
| Esperar (`Wait`) | | `communicate` ★ (cuenta atrás) | 🟢 |
| Comandos (`CommandControl`) | Ejecutar | `communicate` ★ (sus pasos llegan con `depth` + 1) | el del comando llamado |
| | Activar / desactivar | `communicate` ★ | 🟡 |
| Portapapeles, Wake on LAN, Sonido, Multimedia, Grabar audio, Mensaje, Notificación, Enviar al móvil, Archivos ocultos, «Desactivar en», K-Board, QuickMenu, Lanzador | | `communicate` ★ (icono de cada una) | 🟢 |
| Archivos y carpetas (`Files`) | Copiar, renombrar, crear, comprimir, copia de seguridad | `communicate` ★ (icono `folder`) | 🟢 (sincronizar quitando lo que ya no está: 🟡) |
| | Mover, borrar | `communicate` ★ (icono `folder`) | 🔴 |
| Asistente: acción (`AssistantExtra`) | | `communicate` ★ (icono `assistant`); el extra lo hace el asistente que lo declara | 🟢 |
| Asistente: decir (`AssistantSay`) | | `communicate` ★ (icono `assistant`): el asistente lo dice (`say`) | 🟢 |
| Asistente: cerebro (`AssistantBrain`) | | `communicate` (icono `assistant`): saca el modelo de la gráfica o lo carga (`brain` `unload` / `load`) | 🟢 |
| Asistente: preguntar (`AssistantAsk`) | | `communicate` ★ (icono `assistant`); la orden la contesta el asistente (`heard` con `source: "command"`); sin texto, abre la caja para escribirla | 🟢 |
| «Si…» (`Condition`) | | ninguno: solo decide qué acciones corren | 🟢 |
