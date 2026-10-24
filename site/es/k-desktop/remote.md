# Móvil y control remoto

## Aplicación web (móvil, tableta, cualquier navegador)

*Opciones → Aplicación web* enciende un pequeño servidor web en tu PC (el puerto 2502 por defecto). Desde un móvil en la
misma red tienes tus comandos y menús, el volumen y la salida de audio, un touchpad y un teclado, órdenes de voz y el chat
de los asistentes. Se puede instalar en el móvil como aplicación (PWA).

![Emparejar un dispositivo](../../images/es/pair-qr.png)

1. Enciende **Servidor web**.
2. **Emparejar dispositivo**: escanea el código QR con el móvil (o abre el enlace que enseña).
3. Ya está: el dispositivo queda emparejado hasta que lo **revoques** en *Dispositivos emparejados*.

Las pestañas de la aplicación:

| Pestaña | Qué tiene |
|---|---|
| **Menús** | Tus QuickMenus |
| **Comandos** | Todos los comandos marcados como *Disponible por control remoto* |
| **Audio** | Volumen, silencio, reproducir/pausa, siguiente, anterior y la salida de audio |
| **Touchpad** | Mover el ratón, hacer clic, desplazar y escribir en el PC (solo con *Touchpad y teclado* encendido) |
| **Asistente** | El chat con los asistentes, por escrito o hablando |
| **Ajustes** | Tema, idioma y las opciones de la aplicación |

A tener en cuenta:

- Nada funciona hasta emparejar un dispositivo, **también desde el mismo PC**.
- Para escuchar en la red, K-Desktop tiene que ir como administrador (como va por defecto); si no, el servidor solo
  contesta a este PC.
- *Dirección de internet* es opcional, solo si entras desde fuera a través de un proxy inverso.

## Aplicación de Android

La aplicación de Android es la misma aplicación web con micrófono propio. *Opciones → Aplicación de Android → Guardar la
APK…*, cópiala al móvil y ábrela (Android pide permitir instalar aplicaciones de ese origen). No necesita tienda: cuando se
conecta un móvil con una versión más antigua, la aplicación ofrece actualizarse desde el PC.

## UDP

*Opciones → Control remoto → Servidor UDP* (puerto 2501) acepta mensajes de texto sencillos, compatibles con K-Desktop 4:

| Mensaje | Qué hace |
|---|---|
| `list` | Contesta con la lista de comandos remotos |
| Un número o un nombre | Ejecuta ese comando remoto |
| `event <nombre>` | Ejecuta los eventos con *Llamada desde fuera* y ese nombre |
| `play`, `pause`, `next`, `previous`, `subir volumen`, `bajar volumen`, `silenciar volumen` | Teclas multimedia |
| `sk <teclas>` / `sendkey <teclas>` | Envía una combinación de teclas |
| `show kdesktop` | Enseña la ventana de K-Desktop |

*Aceptar desde otros dispositivos* deja que los manden otros equipos; apagado, solo este PC. Lo puede usar Home Assistant,
un script, otro PC o una aplicación del móvil.

## API web

Los dispositivos emparejados también pueden llamar directamente a la API, por ejemplo para lanzar un evento:

```
POST http://<PC>:2502/api/event
X-KD: 1
Authorization: Bearer <token>

{"name": "puerta"}
```
