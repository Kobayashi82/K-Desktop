# K-Desktop

K-Desktop convierte cualquier tecla, botón del ratón o combinación en una serie de acciones: abrir programas, colocar
ventanas, cambiar el volumen, hacer capturas, llamar a Home Assistant, escribir texto, leer el texto de la pantalla… Los
mismos comandos se lanzan desde un menú junto al ratón, una caja de búsqueda, la voz, el móvil, la red o solos, cuando pasa
algo en el PC.

Además, el **módulo Asistente** (opcional) pone en el escritorio personajes a los que se les habla. Piensan con un modelo
de IA local, ejecutan tus comandos con gestos que se ven y tienen herramientas para archivos, internet, la pantalla, la
música, temporizadores, notas y tus propios documentos.

![La ventana principal de K-Desktop](../images/k-desktop-es.png)

> [!WARNING]
> K-Desktop 5 se usa a diario, pero sigue en pleno desarrollo: las cosas cambian de una versión a otra.

## Qué hay en esta documentación

| | |
|---|---|
| **[K-Desktop](k-desktop/getting-started.md)** | Instalarlo, los comandos y sus acciones, QuickMenus, el lanzador, eventos, macros, K-Board, la voz, el móvil y las opciones |
| **[Módulo Asistente](assistant/index.md)** | Los asistentes del escritorio: instalarlos, su cerebro (la IA), sus herramientas, la memoria y los documentos, y sus voces |
| **[Crear asistentes](sdk/index.md)** | Para programadores y artistas: asistentes sin código (imágenes y JSON) y con código (C# y el SDK), firmarlos y publicarlos |
| **[Referencia de la API del SDK](../api/KDesktop.Assistant.Sdk.yml)** | Todos los tipos del SDK de asistentes (en inglés) |

## En pocas palabras

- **Comandos**: acciones con atajos (`CTRL + ALT + O`, `Mouse 5`, `CTRL + Wheel Up`…), en todas partes o solo en un
  programa.
- **QuickMenu y lanzador**: un menú junto al ratón y una caja de búsqueda que lo encuentran y lo ejecutan todo.
- **Eventos**: «cuando pase esto, ejecuta estos comandos» (una hora, abrir un programa, el PC sin usar, un USB…).
- **Macros y K-Board**: graba teclado y ratón, y cambia lo que hacen teclas y botones en cada programa.
- **Voz**: tu propia palabra de activación y reconocimiento en tu PC (sin internet), y dictado.
- **Móvil**: una aplicación web y otra de Android con tus comandos, un touchpad, un teclado y el chat de los asistentes.
- **Seis idiomas**: español, inglés, francés, alemán, italiano y portugués.
- **Privado**: el reconocimiento de voz, las voces y la IA van en tu PC. Ver [Privacidad](k-desktop/settings.md#privacidad).
