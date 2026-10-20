# Pruebas de concepto (fase 0.5)

Hechas el 20 de octubre de 2026 en Windows 11 (tres monitores, pantalla virtual de 5360 × 1440, el izquierdo en
x = −1920), con programas desechables publicados igual que el portable de K-Desktop (un solo archivo,
autocontenido, sin comprimir). **Las cuatro funcionan; el plan no cambia.**

## a) Tubería entre niveles de integridad — ✅

Un servidor **elevado** (integridad alta) crea la tubería y un cliente **sin elevar** (integridad media) se conecta,
escribe y lee.

- La tubería se crea con `CreateNamedPipeW` (P/Invoke) y un descriptor de seguridad desde SDDL, porque
  `PipeSecurity` de .NET no deja poner la etiqueta de integridad sin privilegios de auditoría:

  ```
  D:P(A;;GA;;;SY)(A;;GA;;;<SID del usuario>)S:(ML;;NW;;;ME)
  ```

  Solo SYSTEM y tu usuario; etiqueta obligatoria **media** con *no-write-up*, para que un proceso medio pueda
  escribir. Banderas: `PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE` (nadie puede
  adelantarse creando otra instancia con el mismo nombre) y `PIPE_REJECT_REMOTE_CLIENTS`. El `SafePipeHandle` se
  envuelve en un `NamedPipeServerStream`.
- **Comprobación del cliente** con `GetNamedPipeClientProcessId` → `QueryFullProcessImageNameW` (misma ruta que
  `Environment.ProcessPath`), `ProcessIdToSessionId` (misma sesión), el usuario de su token (mismo SID) y su PID (el
  que se lanzó). Un **impostor** (el mismo programa copiado con otro nombre) queda **rechazado**.
- Ojo: las tuberías **no admiten `ReadTimeout`** (lanza `InvalidOperationException`). Los tiempos máximos van con
  `CancellationToken` en las lecturas asíncronas.

## b) Lanzar sin administrador y conocer su PID — ✅

`ProcessLauncher.CreateProcessAsShellUser` (token del Explorador + `CreateProcessWithTokenW`) ya recibe
`PROCESS_INFORMATION`: basta con devolver `dwProcessId` (y conservar `hProcess` para vigilar el proceso en vez de
cerrarlo). El proceso lanzado es **no elevado, integridad media**, encuentra la tubería por su nombre y se conecta.

## c) Cargar DLL externas en el ejecutable de un solo archivo — ✅

Host publicado en un solo archivo con el SDK y Vortice 3.8.3 dentro; asistente en una carpeta aparte con **su**
Vortice 3.8.2.

- Cada asistente en su `AssemblyLoadContext` **descargable**. `Load` devuelve `null` para
  `KDesktop.Assistant.Sdk` (así se usa el del host, que está en el contexto por defecto aunque vaya dentro del
  exe) y resuelve el resto con `AssemblyDependencyResolver` sobre el DLL de entrada (necesita su `.deps.json`).
  También `LoadUnmanagedDll`.
- El cast a `IAssistant` funciona (el tipo del SDK es el mismo para los dos), el asistente usa su propio Vortice
  (contexto `assistant-N`, ruta de su carpeta) y dibuja con WPF (`DrawingGroup`, pinceles congelados).
- **Descarga y recarga:** dos rondas cargar → usar → `Unload()`; el contexto se recoge (referencia débil muerta)
  en las dos. Sirve para cambiar de asistente y para `--assistant-dev`.
- A tener en cuenta en la fase 1:
  - `ProjectReference` con `Private=false` **no** evita que el SDK se copie a la salida del asistente
    (`EnableDynamicLoading`): el empaquetador del `.kdassistant` debe excluir `KDesktop.Assistant.Sdk.*`.
  - Un asistente que registre `DependencyProperty` o tipos de WPF globales podría impedir la descarga (WPF guarda
    esos tipos en tablas estáticas). Plan B si pasa: cambiar de asistente reiniciando el proceso del asistente,
    que es barato.

## d) La capa transparente en un proceso sin elevar — ✅

Al estilo de Kobii: **una** ventana sobre toda la pantalla virtual
(`WS_EX_NOREDIRECTIONBITMAP | LAYERED | TRANSPARENT | TOOLWINDOW | TOPMOST | NOACTIVATE`) con un árbol de
DirectComposition, y cada lienzo es una cadena de intercambio pequeña (premultiplicada) que se coloca con el
desplazamiento de su visual. Las zonas de clic son ventanas aparte, alfa 1/255 y región elíptica.

Comprobado sin mover el ratón:

| Comprobación | Cómo | Resultado |
|---|---|---|
| El cuerpo dibujado deja pasar el ratón | `WindowFromPoint` sobre el círculo (fuera de la zona de clic) devuelve la ventana de debajo (Chrome) | ✅ |
| La zona de clic recibe el ratón | `WindowFromPoint` en su centro devuelve la ventana de la zona | ✅ |
| No roba el foco | `GetForegroundWindow` igual antes y después | ✅ |
| El fotograma llega a la pantalla | `GetPixel` de la pantalla compuesta = naranja exacto (255, 127, 0) | ✅ |
