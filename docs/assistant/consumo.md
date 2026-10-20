# Consumo del módulo Asistente (etapa 1.10)

Medido el 20 de octubre de 2026 con la 5.0.55 (compilación Release dependiente del marco) en un Ryzen 7 7800X3D
(8 núcleos, 4,2 GHz), RTX 4070 SUPER, 64 GB y dos monitores (3440 × 1440 y 1920 × 1080). Referencia: la 5.0.42
(rama `5.0`, antes del módulo).

## Resultado

Sin usar el PC, tras 3 minutos encendido, midiendo 2 minutos (CPU en % de **un** núcleo):

| Caso | K-Desktop CPU | K-Desktop memoria (WS / privada) | Proceso del asistente CPU | Proceso del asistente memoria |
|---|---|---|---|---|
| 5.0.42 | 0,002 % | 199 / 164 MB | — | — |
| Módulo apagado | 0,001 % | 202 / 167 MB | — | — |
| Módulo encendido, Dot quieto | 0,028 % | 206 / 167 MB | 0,067 % | 104 / 78 MB |
| Módulo encendido, Dot paseando | 0,073 % | 205 / 168 MB | 0,233 % | 109 / 82 MB |

Comprobación de §2.2 del plan:

- **Apagado, K-Desktop igual que la 5.0: ✅.** Misma CPU, misma memoria, mismos hilos. No carga ningún ensamblado
  del asistente (`KDesktop.Assistant.*` no aparece entre los módulos del proceso).
- **Encendido y en reposo, el proceso del asistente en el orden del Lander (~0,05 % de un núcleo posado): ✅.**
  Dot quieto se queda en 0,067 %. Paseando (salta cada 8–20 s y anima medio segundo a la frecuencia de la pantalla)
  sube a 0,233 %.
- **K-Desktop con el módulo encendido** sube a 0,03 % (la tubería, su latido y la vigilancia del proceso), y con Dot
  paseando a 0,07 % (recibe su panel en cada salto). Unos 4 MB más de memoria.
- **La IA y la voz se cargan al usarlas:** todavía no existen (fases 3–5); se medirán entonces.
- **Pantalla completa exclusiva → se libera la GPU:** está hecho (`HostRendering.Suspend`), pero no se ha medido; hace
  falta un juego DirectX a pantalla completa.

Memoria del proceso del asistente: unos 105 MB de conjunto de trabajo y 80 MB privados, casi todo el runtime de
.NET, WPF (para que los asistentes dibujen con `DrawingContext`) y Direct3D/DirectComposition. De sus ~75 hilos, 55
son del controlador de NVIDIA (`nvwgf2umx.dll`, Direct3D 11) y no gastan nada en reposo.

## Con el PC en uso

Mientras se usa el PC, Windows avisa al asistente de todo lo que se mueve (eventos de ventana, incluido el cursor) y
el motor despierta para descartarlos. Es lo mismo que hace el Lander independiente. Medido por partes con el motor
instrumentado (con uso normal del PC):

| | Antes (5.0.54) | Ahora |
|---|---|---|
| Reescaneos de todas las ventanas | 4–8 por segundo | 0,5 por segundo (el de seguridad, cada 2 s) |
| Trabajo del motor | 3–4 ms por segundo | 0,3–0,4 ms por segundo |

## Qué se corrigió en esta etapa

- **Filtro de eventos de ventana** (`WorldWatcher.OnWindowEvent`). Al pasar el código del Lander al host se perdió su
  filtro, y cualquier tooltip, menú o ventana hija que apareciera, desapareciera o se moviera hacía reescanear todo
  el escritorio. Ahora, como en el Lander, solo cuentan las ventanas de primer nivel; un movimiento solo si es de una
  ventana del mundo y su rectángulo cambió de verdad; ocultarse, minimizarse o cerrarse solo si estaba en el mundo.
  Una ventana nueva que aún no cuenta (sin tamaño o sin título) se vuelve a mirar a los 0,4 s, sin reescanear.
  Sin usar el PC, el proceso del asistente bajó de 0,114 % a 0,078 % y K-Desktop de 0,065 % a 0,031 %.
- **Latido de la tubería** (`PipeConnection`). Los dos lados despertaban cada segundo para ver si el otro seguía
  vivo. Ahora duermen hasta el momento en que el silencio podría llegar al límite (5 s para el `ping`, 15 s para
  darla por muerta). El protocolo no cambia. Ojo al hacerlo: `Environment.TickCount64` va a saltos de 15,6 ms y el
  `pong` suele llegar en el mismo salto que el `ping`; tomarlo por «sin respuesta» hacía caer la tubería a los 15 s
  (lo cubre `PipeTests.OnePingingSideKeepsItselfAliveWithThePongs`).
- **Icono de la bandeja con otra carpeta de datos.** El identificador del icono sale del nombre y Windows lo ata a la
  ruta del ejecutable: dos K-Desktop del mismo `.exe` con carpetas de datos distintas chocaban y el segundo se caía al
  arrancar (`TryCreate failed`). Ahora una carpeta de datos que no es la normal usa un nombre propio, y si aun así
  falla, K-Desktop sigue sin icono.

## Cómo se mide

`tools/assistant/measure-idle.ps1` arranca varias instancias aisladas **a la vez** (cada una con una copia de una
carpeta de datos de prueba, sin ganchos, web ni UDP; su `config.json` decide si el módulo está encendido), espera a
que se asienten y mide los dos procesos de cada una:

```powershell
.\tools\assistant\measure-idle.ps1 -Case `
  "5.0.42|<worktree de la rama 5.0>\src\KDesktop\bin\Release\net10.0-windows\win-x64\KDesktop.exe|<datos apagado>", `
  "apagado|src\KDesktop\bin\Release\net10.0-windows\win-x64\KDesktop.exe|<datos apagado>", `
  "quieto|src\KDesktop\bin\Release\net10.0-windows\win-x64\KDesktop.exe|<datos con Dot>"
```

La carpeta de datos de prueba solo lleva un `config.json` sin comandos (con `showWindowOnStart` y `autoBackup`
desactivados, y `assistantEnabled` / `assistantActive` según el caso) y, para los casos encendidos,
`Assistants\dot\<versión>` y `Assistants\Settings\dot.json` (`{"wander": false}` para Dot quieto).

Tres trampas que hacen salir cifras falsas:

1. **El tiempo de CPU de Windows no sirve a esta escala.** Se cobra en saltos del reloj (15,6 ms), y un hilo que
   despierta con un temporizador está justo en marcha cuando llega el salto: los procesos en reposo parecen de 3 a 10
   veces más ocupados. El script cuenta **ciclos** (`QueryProcessCycleTime`).
2. **El arranque deja cola.** Durante un par de minutos terminan hilos del arranque (compilación por niveles,
   inicializaciones en segundo plano) y suman ciclos. Por eso se esperan 3 minutos antes de medir.
3. **Usar el PC despierta al asistente.** El script anota qué parte del tiempo hubo teclado o ratón (`In use %`); las
   cifras de la tabla son con 0 %.

Para ver qué hilo gasta, sirve contar ciclos por hilo (`QueryThreadCycleTime`) y agruparlos por el módulo de su
dirección de inicio y su nombre (`GetThreadDescription`): el motor del asistente se llama
«K-Desktop assistant engine».
