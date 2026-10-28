# Acciones

Todas las acciones que puede ejecutar un comando. Se añaden con *Añadir acción* en el editor del comando y se ejecutan en
orden.

## Abrir y ejecutar

| Acción | Qué hace |
|---|---|
| **Ejecutar aplicación** | Abre un programa, archivo, acceso directo o script, como usuario normal o como administrador. |
| **Abrir carpeta** | Abre una carpeta o una ubicación especial de Windows. |
| **Abrir página web** | Abre una dirección en el navegador que elijas. |
| **Controlar aplicación** | Activa, cierra, pone siempre visible o espera a una aplicación por el nombre de su proceso. |
| **Ejecutar script** | Ejecuta un script de CMD (BAT), PowerShell o VBScript escrito ahí mismo. |

## Ventanas

| Acción | Qué hace |
|---|---|
| **Ventana** | Maximiza, minimiza, coloca (mitades, cuartos, tercios), centra, pasa a otro monitor o hace transparente una ventana. |
| **Siempre visible (on/off)** | Deja la ventana activa por encima de las demás, o la suelta. |

## Teclado y ratón

| Acción | Qué hace |
|---|---|
| **Enviar pulsación / texto** | Envía una combinación de teclas, escribe un texto o mueve el ratón. |
| **Portapapeles** | Copia un texto (con fecha, hora…) o cambia el que hay: texto plano, mayúsculas, minúsculas, enlaces limpios… y lo pega. |
| **Reproducir macro** | Reproduce una macro grabada (teclado y ratón con sus tiempos), una vez o varias. |
| **K-Board** | Activa, desactiva o alterna un perfil de K-Board (teclas y botones cambiados). |
| **Dictar** | Escribe lo que dices donde está el cursor, sin IA. Acaba tras unos segundos de silencio, con Esc o diciendo «deja de dictar». |

## Sonido

| Acción | Qué hace |
|---|---|
| **Volumen** | Fija, sube, baja o silencia el volumen: de los altavoces o el micrófono (los predeterminados o uno que elijas), de un programa o del que está delante. |
| **Salida de audio** | Cambia el dispositivo de sonido predeterminado. Con varios elegidos, cada vez pasa al siguiente. |
| **Entrada de audio** | Cambia el micrófono predeterminado. Con varios elegidos, cada vez pasa al siguiente. |
| **Reproducir sonido** | Reproduce un sonido elegido en un panel por tipos (avisos, alarmas, llamadas, sistema y los tuyos) con los de K-Desktop y los de Windows, que suenan al pulsarlos, u otro archivo (.wav, .mp3, .wma, .m4a, .aac, .flac). Los que pongas en la carpeta de tus sonidos salen en «Tuyos». |
| **Grabar audio** | Graba lo que suena en el PC, el micrófono o las dos cosas, o guarda lo que acaba de sonar (*Opciones → Grabación de audio*). |

## Pantalla

| Acción | Qué hace |
|---|---|
| **Captura de pantalla** | Captura una zona que eliges con el ratón, la ventana activa, un monitor o todos; la copia, la guarda y, si quieres, la abre. |
| **Copiar texto de la pantalla** | Lee el texto de una zona que eliges con el ratón, de la ventana activa o de un monitor, y lo copia. Sin IA: con el reconocimiento de texto de Windows (OCR). |
| **Lupa** | Una lente (rectangular o redonda) que sigue al ratón y amplía lo que tiene alrededor, o toda la pantalla en la que está. |
| **Pantalla** | Apaga las pantallas (todas o las que elijas), cambia la proyección (Win + P), el brillo, o la resolución y la frecuencia de un monitor. |
| **Tema de Windows** | Pasa Windows y las aplicaciones a modo claro u oscuro, o alterna entre los dos. |

## Red e internet

| Acción | Qué hace |
|---|---|
| **Petición web** | Envía una petición HTTP (GET, POST…) sin abrir el navegador: Home Assistant, APIs, avisos al móvil… |
| **Despertar equipo** | Enciende otro PC o un servidor por la red (Wake-on-LAN) y, si quieres, espera a que responda. |
| **Wi-Fi y Bluetooth** | Enciende, apaga o alterna el Wi-Fi o el Bluetooth. |

## Sistema

| Acción | Qué hace |
|---|---|
| **Archivos y carpetas** | Copiar, mover, renombrar, borrar (a la papelera o del todo), crear una carpeta, comprimir en ZIP, descomprimir o hacer una **copia de seguridad**: sincronizar (solo lo nuevo o cambiado, y si quieres quitar del destino lo que ya no está) o una copia con fecha cada vez guardando las últimas. El origen es una lista de archivos, carpetas o patrones (`C:\Música\*.mp3`) escritos a mano o añadidos con los botones. El destino puede estar dentro del origen: nunca se copia a sí mismo. Enseña el progreso en el OSD (Esc lo detiene) y la siguiente acción puede esperar a que termine. |
| **Archivos ocultos (on/off)** | Enseña o esconde los archivos ocultos en el Explorador. |
| **Plan de energía** | Cambia el plan de energía de Windows (Alto rendimiento, Equilibrado…) o pasa al siguiente. |
| **Energía** | Apaga, reinicia, suspende, bloquea o cierra la sesión, con una cuenta atrás que se puede cancelar. |

## Flujo

| Acción | Qué hace |
|---|---|
| **Si…** | Comprueba algo (un programa abierto, la hora, un dispositivo, la red, el uso del procesador, una variable…) y, si no se cumple, para el comando, se salta las acciones siguientes o ejecuta otro comando en su lugar. Ver [condiciones](commands.md#si-condiciones). |
| **Esperar** | Hace una pausa antes de la acción siguiente. |
| **Comandos** | Ejecuta, habilita o deshabilita otro comando. |
| **Abrir QuickMenu** | Enseña un QuickMenu donde está el ratón. |
| **Mostrar mensaje** | Enseña un mensaje en pantalla durante un rato. |
| **Mostrar notificación** | Una tarjeta en una esquina con tu título, texto, imagen, color y sonido, el tiempo que digas. Al pulsarla puede abrir algo (un archivo, una carpeta, una web) o ejecutar otro comando, y la siguiente acción puede esperar a que se cierre. Su sonido puede ser *Global* (el de *Opciones › Mensajes y notificaciones*). Con algo a pantalla completa hace lo que digan esas opciones. |
| **Desactivar en la app actual (on/off)** | Desactiva o vuelve a activar los atajos de K-Desktop en la aplicación activa (útil en juegos). |

## Asistente

Solo con el [módulo Asistente](../assistant/index.md) encendido.

| Acción | Qué hace |
|---|---|
| **Asistente: preguntar** | Le dice algo a un asistente, como si se lo dijeras en voz alta. Sin texto, abre la caja para escribirle. |
| **Asistente: decir** | El asistente dice algo en su bocadillo, sin la IA. Útil en eventos: «Ha terminado la copia». |
| **Asistente: cerebro** | Saca el modelo de IA de la gráfica (antes de un juego o un render) o lo carga ya para que la primera respuesta no espere. |
| **Asistente: acción** | Pone un asistente en el escritorio, lo esconde o lanza una acción propia suya, como el modo manual del Lander (si no está en el escritorio, sale para hacerla o no se hace nada, a elegir). |

## Variables: pasar datos de una acción a otra

En los textos de las acciones (un mensaje, una dirección, un texto que se escribe, un script…) puedes poner variables.
Escribe `{` en el campo y salen las que hay; con las flechas eliges una y con Enter la pones.

| Variable | Qué vale |
|---|---|
| `{resultado}` | Lo que dio la acción anterior que da algo (ver abajo). |
| `{seleccion}` | Lo seleccionado en la ventana donde ejecutaste el comando. Se copia una vez y el portapapeles queda como estaba. |
| `{portapapeles}` | El texto copiado. |
| `{fecha}` y `{hora}` | La fecha de hoy (2026-10-28) y la hora (09:30:05). |
| `{nombre}` | Lo que guardó una acción anterior con **Guardar en variable** y ese nombre. |

Dan algo: **Copiar texto de la pantalla** (el texto leído), **Petición web** (la respuesta), **Portapapeles** (el texto que
deja copiado), **Captura de pantalla** (el archivo guardado) y **Ejecutar script** (lo que escribe, si se marca *Esperar a
que termine* y la ventana es *Oculta*; no en VBScript). Estas acciones tienen el campo **Guardar en variable**: con un
nombre (letras, números y `_`), lo que dan sigue disponible hasta que acaba el comando, aunque después otra acción cambie
`{resultado}`.

Las variables duran lo que dura una ejecución del comando. Los comandos que ejecuta con *Comandos* y los comandos de un
mismo evento las comparten. También valen los nombres en inglés (`{result}`, `{selection}`, `{clipboard}`, `{date}`,
`{time}`). Lo que va entre llaves y no es una variable se deja como está.
