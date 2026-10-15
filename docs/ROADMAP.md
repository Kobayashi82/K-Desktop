# Hoja de ruta

## Hecho (sesión 1)

- [x] Solución .NET 10: `KDesktop.Core` (lógica) + `KDesktop` (WPF + WPF-UI).
- [x] Modelo de datos en JSON con guardado atómico, debounce y copias de seguridad diarias.
- [x] Importador de K-Desktop 4: comandos, acciones, QuickMenus, opciones, sonidos e iconos.
- [x] Hooks de teclado y ratón en un hilo dedicado: acordes de ratón, rueda, bloqueo de teclas y supresión del menú Inicio/Alt; se reinstalan periódicamente.
- [x] Motor de comandos asíncrono y cancelable (Esc), con filtros por proceso y la lista "Desactivar en".
- [x] Lanzamiento elevado / no elevado sin KPS.exe (el propio ejecutable hace de auxiliar con el token del Explorador).
- [x] 16 tipos de acción.
- [x] QuickMenu acrílico en cascada: teclado, clic derecho que no cierra, clic central para editar.
- [x] OSD animado.
- [x] Servidor UDP compatible con K-Desktop 4.
- [x] Ventana principal: comandos (editor completo), opciones y acerca de.
- [x] Tema claro, oscuro o del sistema; idiomas español e inglés en JSON con cambio en caliente.
- [x] Instancia única, línea de comandos (`--menu`, `--run`…), bandeja e inicio con Windows (tarea programada).

## Siguiente

- [ ] **Editor visual de QuickMenus**: lienzo en vivo, arrastrar y soltar (comandos, archivos, URLs), menú contextual, renombrar en el sitio, iconos, deshacer y rehacer.
- [ ] Reordenar acciones arrastrando.
- [ ] Probar a fondo con K-Desktop 4 cerrado: hotkeys, bloqueo y acordes.
- [ ] Publicación: build Release self-contained / ReadyToRun e instalador o zip portable.
- [ ] Favicons para las acciones web.

## Ideas

- Salida de audio: cambiar el dispositivo predeterminado (existía en la 4.0).
- Atajos por aplicación con "capas" (sustituiría a K-Board si se vuelve a necesitar).
- Búsqueda rápida de comandos tipo lanzador (Ctrl + Espacio → escribir → Enter).
- Cliente web/móvil para el control remoto.
