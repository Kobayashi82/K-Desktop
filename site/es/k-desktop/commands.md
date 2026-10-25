# Comandos

Un **comando** es una lista de acciones con un nombre. Se lanza con un atajo, desde un QuickMenu, el lanzador, la voz, el
móvil, la red, un evento, otro comando o un asistente.

![Editando un comando](../../images/es/action-window.png)

## Atajos

Un atajo es cualquier combinación de teclas, botones del ratón y la rueda: `CTRL + ALT + O`, `F13`, `Mouse 5`,
`Mouse 4 + Mouse 1`, `CTRL + Wheel Up`… Para grabarlo, haz clic en la casilla del atajo y púlsalo. Un comando puede tener
varios.

- **Solo para un programa**: limita el comando a algunos programas (por el nombre de su proceso). Si el mismo atajo existe
  en general y para un programa, dentro de ese programa gana el específico y en todos los demás el general.
- **Desactivar en**: en *Opciones → Desactivar y bloquear* pones los programas (juegos, escritorios remotos…) donde los atajos de
  K-Desktop no hacen nada. La acción *Desactivar en la app actual* lo cambia para el programa que está delante.
- K-Desktop avisa cuando un atajo ya lo usa otro comando, el propio K-Desktop o un asistente.

## Acciones

Cada comando tiene una lista de acciones que se ejecutan en orden: abrir un programa, colocar una ventana, enviar teclas,
cambiar el volumen, esperar, ejecutar otro comando… Ver la [lista completa de acciones](actions.md).

![Elegir una acción](../../images/es/action-picker.png)

- Cada acción se puede apagar sin borrarla, y su orden se cambia arrastrando.
- **Ejecutar** en el editor prueba el comando al momento.
- Una acción que abre algo elige si va **como administrador o como usuario normal**.

## Si… (condiciones)

La acción **Si…** comprueba algo y, si no se cumple, para el comando o se salta las acciones siguientes. Puede mirar:

| Condición | Ejemplo |
|---|---|
| Un programa está abierto / está delante | «Si Steam está abierto» |
| Una hora y unos días | «Si es de lunes a viernes de 9:00 a 18:00» |
| Un dispositivo está conectado | «Si el mando de Xbox está conectado» |
| El sonido sale por un dispositivo / algo está sonando | «Si el sonido sale por los auriculares» |
| Una red | «Si está conectado a WiFiCasa», «Si no hay red» |
| Batería o corriente | «Si funciona con batería» |
| Un equipo responde | «Si 192.168.1.10 responde» |
| Un archivo existe | «Si existe D:\copia.lock» |
| El portapapeles | «Si el portapapeles contiene «http»» |
| Uso del procesador, la gráfica o la memoria | «Si el procesador pasa del 80 %» |

Todas las condiciones se pueden negar («no está», «no responde»…).

## Registro

*Registro* enseña todo lo que se ha ejecutado: qué comando, cuándo, desde dónde (atajo, menú, lanzador, voz, móvil, red,
evento, asistente…) y si fue bien.

![Registro](../../images/es/history.png)

## Organizar

- **Busca** por nombre, atajo o acción, y **filtra** la lista: todos los comandos, los que están fuera de los QuickMenus o
  los de un QuickMenu.
- **Habilita o deshabilita** un comando, **ocúltalo** en la lista (sigue funcionando), **duplícalo** o bórralo (sale
  también de los QuickMenus en los que estaba).
- **Disponible por control remoto**: si el móvil y la red lo pueden ver y ejecutar.
- **Icono**: automático (el de su primera acción), un icono vectorial incluido en cualquier color, una biblioteca de iconos
  de Windows, cualquier imagen o el favicon de una web.
- En el editor, **Probar solo esta acción** ejecuta una acción sola.
