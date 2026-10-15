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

## Hecho (sesión 3)

- [x] **Salida de audio**: acción que cambia el dispositivo de sonido predeterminado (también el de comunicaciones si se quiere); con varios elegidos, cada ejecución pasa al siguiente conectado, con mensaje en pantalla y sonido de prueba opcional. Lista ordenable arrastrando; sin dispositivos elegidos, rota por todos los conectados. Se importa de la 4.0.
- [x] **Energía → Cambiar de usuario** (también se importa así de la 4.0, antes se convertía en «Bloquear»).
- [x] **Selector de iconos** nuevo:
  - colección propia de K-Desktop 5: mosaicos vectoriales con degradado y cualquier símbolo de Windows 11 (más de 9.000, con buscador), 15 colores y estilo mosaico o símbolo;
  - iconos de las bibliotecas de Windows (imageres, shell32, ddores… o cualquier dll/exe) con su índice;
  - desde archivo, o volver al icono automático;
  - los iconos por defecto de las acciones y de las ubicaciones especiales usan la colección nueva; se han quitado los de la 4.0.
- [x] **Macros → paso «Desplazar ratón»** (relativo), exacto en píxeles o «en bruto» para juegos.
- [x] **¿Sabías que…?** en Acerca de: 134 trucos (Windows, Explorador, navegadores, texto y K-Desktop) en un carrusel animado con filtros, avance automático y logros.
- [x] **K-Board** rehecho desde cero:
  - perfiles con reasignaciones de teclas, botones del ratón y rueda → teclas o combinaciones, texto, nada o un comando;
  - capas con modificadores (Ctrl + J → ↓ suelta Ctrl mientras tanto) y comodín (Shift + Bloq Mayús usa la regla de Bloq Mayús);
  - se mantienen pulsadas como la original y se sueltan exactamente al soltarla, aunque se apague el perfil a medias;
  - por aplicaciones (o en todas, respetando «Desactivar en»), activables a mano, desde un comando (acción K-Board), desde la bandeja;
  - editor visual: teclado ISO español completo con bloque numérico y ratón dibujados, capas, captura de teclas y lista de reasignaciones;
  - estadísticas y logros propios.

## Pendiente

- [ ] Probar el K-Board en el uso diario (sobre todo juegos y combinaciones con modificadores).
- [ ] Probar la salida de audio con varios dispositivos reales.
- [ ] Probar a fondo en el uso diario: hotkeys, bloqueo, acordes, elevación y macros.
- [ ] Publicación: build Release self-contained / ReadyToRun, y un instalador o zip portable.
- [ ] Favicons para las acciones web.

## Ideas

- K-Link (crear enlaces duros / uniones de carpeta) y Zona Wi-Fi móvil (sustituta de la Red Ad-Hoc): sin decidir.
- Captura de pantalla, grabación de pantalla y de audio: descartadas por ahora.
- Búsqueda rápida de comandos tipo lanzador (atajo → escribir → Enter).
- Cliente web/móvil para el control remoto.
