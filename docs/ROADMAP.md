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
- [x] Editor visual de QuickMenus:
  - lienzo en vivo con los submenús en columnas;
  - arrastrar y soltar (elementos, comandos, archivos, carpetas y enlaces);
  - menú contextual, renombrar en el sitio, iconos;
  - cortar, copiar, pegar y duplicar; deshacer y rehacer;
  - comando rápido.
- [x] "Elegir ventana" para capturar el proceso de una aplicación con un clic.
- [x] Tema claro, oscuro o del sistema; idiomas español e inglés en JSON con cambio en caliente.
- [x] Instancia única, línea de comandos (`--menu`, `--run`, `--show`, `--exit`), bandeja e inicio con Windows (tarea programada).

## Hecho (sesión 2)

- [x] **Logros y estadísticas** (empiezan de cero en la 5.0, sin importar los de la 4.0):
  - 112 logros en 10 categorías, con niveles, progreso y logros secretos; nombres clásicos de la 4.0 y nuevos (macros, acordes, QuickMenus anidados, noctámbulo…);
  - estadísticas en `stats.json`: contadores, ejecuciones por comando y por origen (atajo, QuickMenu, remoto…);
  - notificación animada (insignia, destello, barra de tiempo, sonido `Logro`), en cola, clic para abrir la página;
  - página propia con pestañas Logros / Estadísticas, filtros, búsqueda y reinicio.
- [x] **Macros**:
  - grabación de teclado y ratón con tiempos (barra flotante con cuenta atrás, pausa y detener; Mayús+Esc termina);
  - editor de pasos: insertar, editar, arrastrar, eliminar, deshacer y simplificar (unir pulsaciones, quitar movimientos, cambiar tiempos);
  - acción "Reproducir macro" con repeticiones, bucle, velocidad y pausa entre repeticiones;
  - Mayús+Esc la detiene en cualquier momento y suelta las teclas y botones que hubiera pulsado.
- [x] Reordenar acciones (y pasos de macro) arrastrando, con animación.
- [x] `--data "carpeta"` (instancia independiente con otros datos) y `--page nombre`.
- [x] Correcciones: menú de la bandeja traducido; las acciones ya no guardan `kind`/`summary` en config.json; textos fijos pasados a los idiomas.

## Siguiente

- [ ] Probar a fondo en el uso diario: hotkeys, bloqueo, acordes, elevación y macros.
- [ ] Publicación: build Release self-contained / ReadyToRun, y un instalador o zip portable.
- [ ] Favicons para las acciones web.

## Ideas

- Salida de audio: cambiar el dispositivo predeterminado (existía en la 4.0).
- Atajos por aplicación con "capas" (sustituiría a K-Board si se vuelve a necesitar).
- Búsqueda rápida de comandos tipo lanzador (atajo → escribir → Enter).
- Cliente web/móvil para el control remoto.
