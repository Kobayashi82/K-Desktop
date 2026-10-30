# Calendario y agenda

K-Desktop junta tus citas en un sitio: calendarios propios, tus calendarios de Google y cualquier dirección `.ics`
(Outlook, festivos, deportes…). Te avisa antes de cada cita, lanza [eventos](events.md), tiene acciones para crear citas y
abrir reuniones, y los asistentes pueden mirar y cambiar tu agenda.

## La Agenda

**Agenda**, en el menú lateral encima de *Registro*, sale mientras algún calendario tenga **Mostrar en la agenda**. Enseña
las citas, cada una del color de su calendario, de dos formas:

- **Lista**: los días desde hoy, con hoy siempre arriba (aunque no tengas nada). La cita que está pasando lleva **Ahora**
  y las que ya pasaron se ven más apagadas.
- **Mes**: el mes entero; las citas de todo el día salen como una banda de su color y las demás con su hora. Las flechas
  (o la rueda del ratón) cambian de mes, **+N más** lleva ese día a la lista y un doble clic en un día crea una cita en él.

**Hoy** vuelve a hoy en las dos. Los botones de colores de arriba muestran u ocultan cada calendario (al menos uno tiene
que verse). Además:

- **Nueva cita**, o un clic en una para **Editar** (o **Ver**, si su calendario es de solo lectura); con el botón derecho:
  *Unirse*, *Copiar* y *Eliminar*.
- **Unirse** abre el enlace de Teams, Meet o Zoom de una reunión en línea.
- **Buscar citas…**, **Ver días anteriores** y **Ver más días**.
- **Actualizar ahora** vuelve a leer los calendarios de los servidores; **Importar .ics** y **Exportar a .ics** pasan citas
  entre calendarios y otros programas.

Una cita tiene título, calendario, inicio y fin (o *Todo el día*), **Se repite** (cada día, de lunes a viernes, cada semana,
cada mes un día o «el segundo martes», cada año, cada tantos; siempre, hasta un día o un número de veces), **Aviso**,
lugar, **Enlace de la reunión** (lo encuentra solo en el lugar o en las notas) y notas. Al cambiar o borrar una cita que
se repite pregunta: *Solo esta*, *Esta y las siguientes* o *Todas*. Una cita de un calendario de solo lectura solo se ve.

## Calendarios

*Opciones › Calendario* enseña todos los calendarios. Cada uno tiene su **nombre** (el asistente los conoce por él: «ponlo
en Trabajo»), **color**, **Mostrar en la agenda** y un **Aviso para las citas nuevas**. **Calendario por defecto** es donde
van las citas nuevas si no dices otro.

- **Nuevo calendario**: uno tuyo (Trabajo, Familia…), guardado en este PC y en sus copias.
- **Suscribirse a un .ics**: un calendario de solo lectura desde una dirección, que se lee cada cierto tiempo (15 minutos
  de serie). Es la forma de ver Outlook, o Google sin conectar la cuenta (*Dónde está la dirección .ics* lo explica). La
  dirección se guarda cifrada: la dirección secreta de un calendario deja leerlo a quien la tenga.
- **Google**: conecta la cuenta en *Opciones › Cuentas › Google* (solo ver el calendario, o verlo y editarlo) y luego
  **Mostrar sus calendarios** para elegir cuáles. Google pide una aplicación tuya, gratis (un *Client ID*); *Cómo conseguir
  el Client ID* te guía. K-Desktop nunca ve tu contraseña: inicias sesión en el navegador.

## Avisos

*Opciones › Calendario › Avisos* elige **Cómo avisa**: un **mensaje en pantalla (OSD)**, una **notificación** (con *Unirse*
si la cita tiene enlace de reunión) o **lo dice un asistente** (si está en el escritorio; si no, un OSD). **Avisos de Google
también aquí** viene apagado, porque el móvil ya los da. **Probar un aviso** enseña uno.

## Acciones y eventos

| | |
|---|---|
| **Crear cita** (acción) | Apunta una cita a una hora o dentro de un rato, con título, lugar y notas que pueden llevar variables |
| **Abrir la reunión** (acción) | Abre el enlace de la reunión de ahora (desde 10 minutos antes de empezar) o de la siguiente |
| **Cita del calendario** (evento) | Unos minutos antes de una cita, al empezar o al terminar: de un calendario, con una palabra en el título, solo reuniones en línea, con lugar… Los comandos reciben `{cita}`, `{cita_hora}`, `{cita_lugar}`, `{cita_notas}`, `{cita_enlace}` y `{cita_calendario}` |

*10 minutos antes de una reunión de Trabajo: no molestar y abrir Teams.*

## Los asistentes

Con el [módulo Asistente](../assistant/index.md), las herramientas **Mirar la agenda**, **Apuntar una cita**, **Cambiar una
cita** y **Borrar una cita**: «¿qué tengo mañana?», «pon el dentista el viernes a las 5», «pasa la reunión a las 11»,
«anula la cena del jueves». Apuntar nunca pregunta, cambiar pregunta hasta que digas «no volver a preguntar» y borrar
pregunta siempre; las tres se pueden deshacer.
