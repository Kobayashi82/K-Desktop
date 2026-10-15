# K-Desktop 5.0

Atajos de teclado y ratón, QuickMenus y control remoto por UDP para Windows. Es la reescritura completa de K-Desktop 4 (VB.NET / WinForms) en **C# / WPF / .NET 10**.

## Funciones

- **Comandos**: secuencias de acciones que se lanzan con atajos de teclado o ratón (`CTRL + ALT + O`, `Mouse 5`, acordes como `Mouse 4 + Mouse 1`, `CTRL + Wheel Up`…), desde un QuickMenu, por UDP o por línea de comandos.
- **Ejecución con o sin administrador**: K-Desktop corre elevado, para que sus atajos también funcionen sobre ventanas elevadas, pero cada acción elige si su programa arranca como administrador o como usuario normal.
- **QuickMenu**: menú en cascada en la posición del ratón, con fondo acrílico, iconos y navegación con teclado:
  - Clic izquierdo ejecuta y cierra.
  - Clic derecho ejecuta y deja el menú abierto.
  - Clic central edita el elemento.
- **Control remoto UDP**: compatible con el protocolo de K-Desktop 4 (puerto 2501: `lista`, número, nombre, `sk …`, `tasks`…).
- **Interfaz moderna** con tema claro/oscuro (o el del sistema) y animaciones.
- **Multiidioma** (español e inglés). Para añadir un idioma basta con dejar un JSON en `Languages`.
- **Importación automática** de los datos de K-Desktop 4 (comandos, QuickMenus, opciones e iconos) la primera vez que se ejecuta.

## Acciones

| Aplicaciones | Entrada y sonido | Ventanas y sistema | K-Desktop |
|---|---|---|---|
| Ejecutar aplicación | Enviar pulsación / texto | Siempre visible | Abrir QuickMenu |
| Abrir carpeta / ubicación especial | Volumen | Archivos ocultos | Comandos (ejecutar / habilitar…) |
| Abrir página web | Reproducir sonido | Desactivar en la app actual | Esperar |
| Controlar aplicación (activar / cerrar / esperar) | | Mostrar mensaje | |
| Script CMD / PowerShell | | Energía (apagar, reiniciar…) | |

## Línea de comandos

```
KDesktop.exe --menu "Principal"     abre un QuickMenu (en la instancia en ejecución)
KDesktop.exe --run "Nombre"         ejecuta un comando
KDesktop.exe --show | --exit
KDesktop.exe --no-hooks --no-udp --no-elevate   diagnóstico
```

## Estructura

```
src/KDesktop.Core   núcleo sin UI: modelo, almacenamiento JSON, hooks, motor de acciones, UDP, importador de K-Desktop 4
src/KDesktop        aplicación WPF: ventana principal, QuickMenu, OSD, bandeja, temas, idiomas
```

Los datos se guardan en `%APPDATA%\K-Desktop 5\config.json`, con copias de seguridad diarias en `Backups`.

## Compilar

Requiere el SDK de .NET 10.

```
dotnet build KDesktop.slnx
```
