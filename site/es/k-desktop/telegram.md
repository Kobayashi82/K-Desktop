# Telegram

K-Desktop puede tener su propia identidad en Telegram: le escribes desde el móvil para lanzar comandos o hablar con el
asistente, te avisa de cosas (correos importantes, lo que mandan tus comandos) y tú decides quién más puede escribirle y qué
puede hacer cada uno. Funciona sin el módulo Asistente; con él, Telegram es una forma más de hablar con los asistentes.

Hay dos formas, y puedes tener las dos a la vez. Cada una tiene su página en *Opciones › Cuentas*, con su propio dueño, sus
contactos y sus opciones:

| | **Telegram Bot** | **Telegram** (una cuenta con número) |
|---|---|---|
| Qué es | Un bot que creas en dos minutos con @BotFather | Una cuenta normal de Telegram, mejor con un teléfono solo para K-Desktop |
| Escribe primero | Solo a quien le escribió antes | A cualquiera de sus contactos |
| Botones | Sí (comandos, Sí / No) | No: las opciones van numeradas y se contesta con el número o la palabra |
| Archivos | Hasta 50 MB de salida y 20 MB de entrada | Hasta 2 GB |

## El bot

1. **Cómo crear el bot** abre una guía paso a paso: en Telegram, abre @BotFather, mándale `/newbot`, dale un nombre y un
   usuario que acabe en «bot», y copia el **token** que te da.
2. *Opciones › Cuentas › Telegram Bot › Añadir un bot*, pega el token y **Conectar**. Se comprueba con Telegram antes de
   guardarlo, cifrado.
3. **Vincular mi cuenta**: manda al bot el código de 6 cifras que sale, o abre su enlace, o escanea el QR con el móvil.
   Desde entonces eres su **dueño**.

## La cuenta con número

1. Ten la cuenta en un teléfono (mejor una SIM de verdad que un número virtual).
2. **Cómo conseguir el api_id y el api_hash** abre la guía: Telegram pide una «aplicación» tuya, gratis, en my.telegram.org
   (*API development tools*); copias sus dos valores.
3. *Opciones › Cuentas › Telegram › Iniciar sesión*: el api_id, el api_hash y el número; después el código que Telegram
   manda a la app (o por SMS) y, si la cuenta tiene verificación en dos pasos, su contraseña. La sesión se guarda cifrada,
   solo para tu usuario en este PC. **Cerrar sesión** la cierra también en Telegram.
4. **Vincular mi cuenta**: desde tu Telegram, manda a la cuenta el código que sale (búscala por su número o su usuario).

Telegram vigila las cuentas que escriben a mucha gente. K-Desktop va despacio a propósito (un mensaje cada poco más de un
segundo y, de serie, como mucho 3 personas por hora que nunca le han escrito), nunca escribe en masa y avisa en la página
si Telegram limita la cuenta durante un tiempo.

## Quién puede escribirle

El dueño puede hacer de todo. Los demás hacen lo que permite su **nivel**, también en los grupos; cuenta el nivel de quien
escribe, no el del chat:

| Nivel | Qué puede hacer |
|---|---|
| **Ignorar** | Nada: sus mensajes se descartan |
| **Solo charla** | Hablar con el asistente, sin herramientas |
| **Comandos elegidos** | Lanzar los comandos que elijas, sin IA |
| **Herramientas que solo leen** | Charla y herramientas que no cambian nada (el tiempo, la hora, buscar…) |
| **Herramientas elegidas** | Charla y las herramientas y comandos que elijas |
| **Como tú** | Lo mismo que tú en el PC |

Lo que pide confirmación te lo pregunta siempre **a ti**, nunca a quien lo pidió: en el PC si estás delante y, si no, por
Telegram con *Sí* / *No*, que caducan a los 5 minutos sin hacer nada.

- **Contactos**: cada uno con su nivel, **K-Desktop puede escribirle**, los comandos y herramientas elegidos, **Quién le
  contesta** y **Cómo le contesta**. En la cuenta, **Añadir contacto** lo trae de sus contactos de Telegram o lo añade
  **Con su número** (también a sus contactos de Telegram, para que pueda escribirle primero).
- **Alguien nuevo** que escribe espera tu decisión: te avisa en el PC y por Telegram, con **Permitir**, **Ignorar** y
  **Bloquear**.
- **Todos los demás**: el nivel de quien no es un contacto. Por encima de *Solo charla* hace falta **Entiendo el riesgo**:
  cualquiera que lo encuentre podría usar tu PC.
- **Límites**: mensajes por minuto de cada uno, **Solo yo mientras juego** (con algo a pantalla completa solo te contesta a
  ti) y cuántos mensajes por hora a quien nunca le ha escrito.
- **Grupos**: solo contesta si lo mencionan o le responden a un mensaje suyo; cada grupo se puede ignorar.
- **Bloqueados**: sus mensajes se descartan sin decir nada y K-Desktop nunca les escribe.

## Comandos desde Telegram

`/comandos` enseña los comandos que esa persona puede lanzar (como botones en el bot, numerados en la cuenta); escribir `/`
y el nombre también lo lanza (`/modo_juego` lanza «Modo juego»: dan igual las mayúsculas, los acentos y los espacios).
Contesta cómo fue («✓ Modo juego»). Las fotos, notas de voz y archivos que mandáis tú y tus contactos se guardan en
*Descargas*.

## Hablar con el asistente

Lo que se escribe sin `/` va al asistente (con el módulo Asistente encendido), con los límites de cada persona:

- **Quién contesta**: el que está en pantalla o uno en concreto (y cada contacto puede tener el suyo).
- **Cómo contesta**: *Como le escriben* (a una nota de voz, con voz; a un texto, con texto), *Con texto*, *Con su voz* (una
  nota de voz con la voz del asistente) o *Con texto y voz*.
- Las **notas de voz** se pasan a texto en tu PC (con los modelos de voz de *Opciones › Voz*); **Decir lo que entendió**
  contesta antes con ese texto. Una **foto** sin texto va al asistente para que la mire.
- **Decir lo que hizo** pone debajo de la respuesta lo que hizo («✓ Abrió Spotify»).

Los textos de otros son datos, no órdenes: en un grupo, con un mensaje reenviado o con un archivo de otra persona, lo que
actúa te pregunta antes. Quien no es el dueño tiene su propia conversación y no ve la memoria del asistente.

## Cuál escribe

- **Contesta por donde le escribieron**: al bot, el bot; a la cuenta, la cuenta.
- Para **escribir primero**, *Automático* usa la cuenta si esa persona es su dueño o uno de sus contactos y, si no, el bot.
  La acción **Enviar por Telegram** tiene **Con** (*Automático*, *El bot*, *La cuenta*), y al asistente le puedes decir «por
  el bot» o «por la cuenta».
- **Correos importantes**: en *Avisos por Telegram* de cada página; si los dos lo tienen, llega una vez, por la cuenta.

## Acciones, eventos y herramientas

| | |
|---|---|
| **Enviar por Telegram** (acción) | A ti o a un contacto: un texto con variables, un archivo, una captura de pantalla o lo que hay en el portapapeles. A quién le llegó queda en `{resultado}` |
| **Llega un mensaje de Telegram** (evento) | Solo tus mensajes o los de alguien, con unas palabras, de un tipo (texto, nota de voz, foto o archivo). Los comandos reciben `{telegram_texto}`, `{telegram_de}`, `{telegram_archivo}` y `{telegram_id}` |
| Herramientas del asistente | **Mandar un mensaje por Telegram**, **Mandar un archivo por Telegram** (un archivo, una captura o lo último que llegó) y **Leer Telegram** (los últimos mensajes de un chat). Mandarte algo a ti pregunta hasta que digas «no volver a preguntar»; a cualquier otro, siempre |

*Cuando me mande «nota …» desde el móvil, que la apunte en mis notas.*
