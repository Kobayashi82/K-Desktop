# Correo

K-Desktop vigila tus cuentas de correo y te avisa de lo que importa, lanza [eventos](events.md) cuando llega un correo,
manda correos desde tus comandos y deja que los asistentes busquen, resuman y preparen borradores. No es un cliente de
correo: lo sigues leyendo donde siempre.

## Cuentas

*Opciones › Correo › Añadir cuenta*: la dirección, un nombre para la cuenta (*Personal*, *Trabajo*…; así la entiende el
asistente) y **Cómo entra**:

- **Con contraseña (de aplicación)**. Gmail, iCloud y Yahoo piden una *contraseña de aplicación* (*Cómo conseguir una
  contraseña de aplicación* dice dónde); los demás, tu contraseña. **Detectar los servidores** encuentra IMAP y SMTP solo;
  si no, escríbelos.
- **Con la cuenta conectada**: Gmail sin contraseña de aplicación, después de conectar la cuenta en *Opciones › Cuentas ›
  Google* marcando *Correo de Gmail*.

Outlook y Hotmail no se pueden conectar: Microsoft ya no deja entrar a los programas con contraseña. **Probar** comprueba
que entra en IMAP y en SMTP y, si no, dice por qué. La contraseña se guarda cifrada, solo para tu usuario en este PC.

Cada cuenta tiene además:

- **Para qué**: *Todo* (avisar, eventos, asistente y enviar) o *Solo enviar*, una cuenta solo para los informes y avisos de
  K-Desktop, que solo llega a las **Direcciones permitidas** (vacío: su propia dirección).
- **Avisar de**: *Solo los importantes*, *Todos los correos nuevos* o *Nada* (solo eventos y asistente).
- **Marcar como leídos los correos que lee el asistente**.

## Correos importantes y avisos

**Correos importantes** dice qué correos importan: **Remitentes** (direcciones, dominios como `@banco.es` o nombres) y
**Palabras en el asunto** (*factura, urgente*). Son los que se avisan con *Solo los importantes* y los que el asistente
destaca.

**Avisos** elige cómo te avisa de un correo nuevo: una **notificación** (un clic abre el correo en la web: Gmail, iCloud,
Yahoo…), un **mensaje en pantalla (OSD)** o **lo dice un asistente** (si está en el escritorio; si no, un OSD). **Probar un
aviso** enseña uno. Un correo con una invitación de calendario llega siempre como notificación: al pulsarla se añade la cita
al [calendario](calendar.md). Los importantes también te pueden llegar por [Telegram](telegram.md).

## Acciones y eventos

| | |
|---|---|
| **Enviar correo** (acción) | Desde una de tus cuentas, con texto con variables y archivos adjuntos (una captura, un informe…), o **Dejarlo como borrador** |
| **Llega un correo** (evento) | Cuando llega un correo a una cuenta: de alguien, con palabras en el asunto, solo con adjuntos o solo los importantes. Puede **guardar sus adjuntos** en una carpeta (solo de un tipo: `*.pdf`). Los comandos reciben `{correo_asunto}`, `{correo_de}`, `{correo_fragmento}`, `{correo_adjuntos}`, `{correo_rutas}` (los archivos guardados) y `{correo_cuenta}` |

*Cuando llegue la factura de la luz, guarda su PDF en la carpeta Facturas y avísame.*

## Los asistentes

Con el [módulo Asistente](../assistant/index.md): **Mirar el correo** y **Leer un correo** («¿algo del banco?», «resúmeme el
último de Ana»), **Dejar un borrador**, **Enviar un correo**, **Ordenar un correo** (marcarlo como leído o no leído,
archivarlo o mandarlo a la papelera; nunca se borra del todo) y **Añadir una invitación al calendario**. Leer nunca pregunta;
enviar pregunta siempre; lo demás pregunta hasta que digas «no volver a preguntar» y se puede deshacer. Un correo nunca da
órdenes: después de leer uno, el asistente pregunta antes de hacer nada.

En *El asistente y el correo*: **Los asistentes pueden enviar** (apagado, solo dejan borradores) y **Días de correo
guardados** (solo las cabeceras y un fragmento, para buscar sin preguntar al servidor).
