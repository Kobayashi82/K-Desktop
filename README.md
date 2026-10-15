# K-Desktop 5.0

Atajos de teclado y ratón, QuickMenus y control remoto por UDP para Windows. Es la reescritura completa de K-Desktop 4 (VB.NET / WinForms) en **C# / WPF / .NET 10**.

## Funciones

- **Comandos**: secuencias de acciones que se lanzan con atajos de teclado o ratón (`CTRL + ALT + O`, `Mouse 5`, acordes como `Mouse 4 + Mouse 1`, `CTRL + Wheel Up`…), desde un QuickMenu, por UDP o por línea de comandos.
- **Ejecución con o sin administrador**: K-Desktop corre elevado, para que sus atajos también funcionen sobre ventanas elevadas, pero cada acción elige si su programa arranca como administrador o como usuario normal.
- **QuickMenu**: menú en cascada en la posición del ratón, con fondo acrílico, iconos y navegación con teclado:
  - Clic izquierdo ejecuta y cierra.
  - Clic derecho ejecuta y deja el menú abierto.
  - Clic central edita el elemento.
- **Lanzador**: una acción que abre una caja de búsqueda en el centro de la pantalla. Escribe parte del nombre (vale con las iniciales o letras sueltas) y Enter ejecuta el comando, abre el programa del menú Inicio o busca en la web; también hace cuentas. Los más usados salen primero.
- **Macros**: graba teclado y ratón con sus tiempos, edita los pasos (también desplazamientos relativos del ratón, en bruto para juegos) y reprodúcelas desde cualquier comando (repeticiones, bucle, velocidad). Mayús + Esc las detiene.
- **K-Board**: perfiles que reasignan teclas, botones del ratón y la rueda a otras teclas o combinaciones, a un texto, a un comando o a nada; en todas las aplicaciones o solo en las que elijas. Se mantienen pulsadas como las originales (Bloq Mayús → Ctrl, botón lateral → tecla en un juego…), admiten capas con modificadores (Ctrl + J → ↓) y se editan sobre un teclado y un ratón dibujados. Se activan a mano, desde un comando o desde la bandeja.
- **Iconos**: colección propia de K-Desktop 5 (mosaicos vectoriales con cualquier símbolo de Windows 11, 15 colores y dos estilos), los iconos de las bibliotecas de Windows (shell32, imageres… con su índice), cualquier imagen o, en las acciones web, el icono de la propia página (favicon, descargado una vez de la web).
- **Logros y estadísticas**: más de 100 logros con notificación animada, y estadísticas de uso (comandos más usados, cómo los ejecutas…).
- **Control remoto UDP**: compatible con el protocolo de K-Desktop 4 (puerto 2501: `lista`, número, nombre, `sk …`, `tasks`…).
- **Interfaz moderna** con tema claro/oscuro (o el del sistema) y animaciones.
- **¿Sabías que…?**: más de 130 trucos de atajos de Windows, el Explorador, los navegadores, la edición de texto y K-Desktop, en «Acerca de».
- **Multiidioma** (español e inglés, incluidos en el ejecutable). Para añadir un idioma o cambiar textos basta con dejar un JSON en `%APPDATA%\K-Desktop 5\Languages`.
- **Importación automática** de los datos de K-Desktop 4 (comandos, QuickMenus, opciones e iconos) la primera vez que se ejecuta.

## Acciones

| Aplicaciones | Entrada y sonido | Ventanas y sistema | K-Desktop |
|---|---|---|---|
| Ejecutar aplicación | Enviar pulsación / texto | Siempre visible | Abrir QuickMenu |
| Abrir carpeta / ubicación especial | Volumen | Archivos ocultos | Comandos (ejecutar / habilitar…) |
| Abrir página web | Salida de audio (rotar dispositivos) | Desactivar en la app actual | Esperar |
| Controlar aplicación (activar / cerrar / esperar) | Reproducir sonido | Mostrar mensaje | K-Board (activar / desactivar) |
| Script CMD / PowerShell | Reproducir macro | Energía (apagar, reiniciar, cambiar de usuario…) | |

## Línea de comandos

```
KDesktop.exe --menu "Principal"     abre un QuickMenu (en la instancia en ejecución)
KDesktop.exe --run "Nombre"         ejecuta un comando
KDesktop.exe --show | --exit
KDesktop.exe --page macros          abre K-Desktop en una página (commands, quickmenus, macros, kboard, achievements, stats, settings, about)
KDesktop.exe --no-hooks --no-udp --no-elevate   diagnóstico
KDesktop.exe --data "C:\Carpeta"    usa otra carpeta de datos (instancia independiente: pruebas, copia portátil)
```

## Estructura

```
src/KDesktop.Core   núcleo sin UI: modelo, almacenamiento JSON, hooks, motor de acciones, UDP, importador de K-Desktop 4
src/KDesktop        aplicación WPF: ventana principal, QuickMenu, OSD, bandeja, temas, idiomas
```

Los datos se guardan en `%APPDATA%\K-Desktop 5\config.json` (comandos, QuickMenus, macros, perfiles de K-Board y opciones), con copias de seguridad diarias en `Backups`. Los logros y estadísticas van aparte, en `stats.json`.

## Compilar

Requiere el SDK de .NET 10.

```
dotnet build KDesktop.slnx
```

En Release (`dotnet build KDesktop.slnx -c Release`) se genera además el portable en `artifacts\portable\KDesktop.exe`: un solo ejecutable que no necesita .NET instalado. Idiomas (español e inglés), sonidos e iconos van dentro del ejecutable. Para saltarse el portable: `-p:BuildPortable=false`.

Los archivos de `%APPDATA%\K-Desktop 5\Languages` (`<código>.json`) y `%APPDATA%\K-Desktop 5\Sounds` (`*.wav`) se suman a los incluidos; si coinciden, gana el de la carpeta (en los idiomas, clave a clave).
