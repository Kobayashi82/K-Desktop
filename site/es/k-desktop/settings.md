# Opciones, datos y privacidad

*Opciones* está organizada en grupos plegables, con un buscador arriba (Ctrl+F): escribe «volumen» o «copia» y te lleva
allí.

![Opciones → Apariencia](../../images/es/settings-appearance.png)

## General

| Sección | Qué tiene |
|---|---|
| **Apariencia** | Tema claro, oscuro o el del sistema; idioma (el de Windows, o uno de los seis incluidos); iconos de las webs; traducir |
| **General** | Pausar K-Desktop (con su atajo y sus sonidos); iniciar con Windows, el icono de la bandeja y su imagen; los atajos para enseñar la ventana y para salir, los sonidos y el mensaje al arrancar |
| **OSD** | Los mensajes en pantalla de K-Desktop: dónde salen (pulsando en un dibujo de la pantalla), en qué monitor, su tamaño, cuánto duran, qué hacen con una película o un juego a pantalla completa y qué grupos salen (acciones, volumen y brillo, voz, avisos). Debajo, las **notificaciones** de los logros: si salen, con sonido, en qué monitor y esquina (también en un dibujo), cuánto duran y si esperan a que acabe lo que está a pantalla completa |
| **Lanzador** | Su atajo, qué encuentra (programas, comandos ocultos, cuentas, la web) y en qué orden |

## Voz y multimedia

| Sección | Qué tiene |
|---|---|
| **Voz** | Modelos de voz, micrófono, palabras de activación, atajos para hablar y para dictar. Ver [Voz](voice.md) |
| **Multimedia** | Órdenes de voz para la música, los vídeos y el volumen, y cuánto sube o baja cada vez |

## Conexiones

| Sección | Qué tiene |
|---|---|
| **Control remoto** | El servidor UDP. Ver [Móvil y control remoto](remote.md) |
| **Aplicación web** | El servidor web, emparejar y los dispositivos emparejados, touchpad y teclado |
| **Aplicación de Android** | Su versión y «Guardar la APK» |

## Sistema

| Sección | Qué tiene |
|---|---|
| **Por aplicación** | Programas donde los atajos de K-Desktop no hacen nada (juegos, escritorios remotos), con un atajo para añadir o quitar el de delante, y programas donde Mouse 4 y Mouse 5 no van atrás ni adelante |
| **Capturas y lupa** | La carpeta de las capturas; la **repetición de audio** (guarda en memoria los últimos segundos para que *Grabar audio → Guardar la repetición* los guarde cuando pase algo; no se escribe nada en el disco); el zoom, la forma y el tamaño de la lupa |
| **Datos** | Copias de seguridad, exportar, importar, volver a importar de K-Desktop 4 y el registro |
| **Actualizaciones** | La versión, buscar ahora y qué hacer cuando hay una nueva |

El **módulo Asistente** tiene su propio grupo: ver [Asistentes](../assistant/index.md).

## Copias de seguridad

- **Exportar** guarda todo lo tuyo en un `.zip`: opciones, comandos, QuickMenus, macros, perfiles de K-Board, logros y
  estadísticas, palabras de activación, y tus propios iconos, sonidos e idiomas (y los datos de los asistentes).
- **Copias automáticas**: la misma copia completa cada N días, guardando las últimas, en la carpeta que elijas (mejor en
  otro disco, OneDrive o un NAS). Además, las opciones se copian cada día.
- **Importar** restaura una copia. Antes se guarda la configuración actual en la carpeta de copias.

## Idiomas

Hay seis incluidos: español, inglés, francés, alemán, italiano y portugués. Los idiomas de más son archivos JSON en
`%APPDATA%\K-Desktop 5\Languages`. En *Traducir*, **Exportar plantilla** saca el idioma actual: traduce los textos,
guarda el archivo ahí con el código del idioma (`nl.json`) y elígelo en *Idioma*. Un archivo con solo algunos textos
(`es.json`) cambia solo esos.

## Logros y estadísticas

Más de 100 logros con avisos animados, y estadísticas de lo que más usas.

![Logros](../../images/es/achievements.png)

## Privacidad

El reconocimiento de voz, las palabras de activación, las voces y la IA de los asistentes van **en tu PC**. Nada de lo que
dices o escribes se manda a ninguna parte. K-Desktop solo sale a internet para lo que le pides:

- las peticiones web de tus comandos;
- las herramientas de internet, del tiempo y de monedas de los asistentes, y las herramientas y servidores MCP que añadas
  tú;
- buscar actualizaciones, y descargar voces o modelos.

El móvil habla con el PC por tu red local y necesita emparejarse, también desde el mismo PC.
