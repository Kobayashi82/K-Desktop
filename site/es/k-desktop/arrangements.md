# Disposiciones

Una disposición parte tus monitores en **zonas** y dice qué programas van en cada una. Luego los pone así cuando quieras:
con un atajo, desde un QuickMenu o sola, con un evento. Una disposición abarca todos los monitores; uno sin zonas en ella
no se toca.

1. Coloca tus programas como te gustan (el navegador a la izquierda, el editor a la derecha, la música en el otro monitor…).
2. *Disposiciones → Nueva disposición*: ya trae las ventanas abiertas ahora, cada una como una zona con su programa. Ponle
   nombre («Trabajo»).
3. **Probar** la pone ahora. **Crear un comando que la ponga** te deja un comando listo para darle un atajo.

**Guardar cómo están ahora** cambia las zonas de todos los monitores por las ventanas abiertas en ese momento: cada monitor
se recorta según sus ventanas y cada zona se queda con los programas de las suyas (una maximizada, toda la pantalla; las
minimizadas no cuentan). Las zonas nunca se pisan: las ventanas que están una encima de otra comparten zona, y un hueco
libre grande queda como zona vacía. Además, cada programa recuerda el sitio y el tamaño exactos de su ventana, así que al
ponerla todo vuelve tal cual estaba.

## Zonas

Arriba de **Zonas** hay una píldora por monitor: los conectados y, atenuados, los que han estado conectados alguna vez
(K-Desktop los recuerda), para preparar la disposición de la tele aunque no esté puesta. Un punto en la píldora indica que
ese monitor tiene zonas en esta disposición. Debajo está el dibujo del monitor elegido:

- **Clic** en una zona la marca (un clic fuera de las zonas la desmarca); **otro clic** en la marcada la parte en dos por donde marca la línea de puntos (en
  vertical; con **Mayús**, en horizontal).
- **Arrastra** las líneas entre zonas para cambiar su tamaño (se quedan a la mitad, un tercio o un cuarto si pasas cerca).
- La **✕** de una línea junta sus dos zonas, con sus programas.
- **Plantillas**: toda la pantalla, 2 o 3 columnas, cuadrícula, una grande y dos pequeñas o 2 filas. Los programas que
  tenía cada zona pasan a la zona nueva donde caía su centro. **Quitar zonas** deja el monitor sin zonas.

Debajo del dibujo están los **programas de la zona marcada**, como en *Solo en estas aplicaciones* de K-Board: escribe el
programa o elígelo de la lista y pulsa **Añadir**, o pulsa el botón del cursor y haz clic en su ventana. Una zona sin
programas solo sirve para encajar ventanas.

Los programas guardados con **Guardar cómo están ahora** llevan una chincheta: van al sitio exacto donde estaba su ventana,
que se ve con línea discontinua en la zona marcada. Con **clic derecho → Llenar la zona** olvida ese sitio y el programa se
estira a su zona, como los que añades a mano.

## Al ponerla

- **Si el programa está abierto**, su ventana va a su zona (o a su sitio guardado, si lleva chincheta); no abre otra. Una zona de toda la pantalla la deja maximizada.
  Con varias ventanas del mismo programa (dos de Chrome), las reconoce por su título y, si no, por orden.
- **Si no está abierto**, se salta, salvo con *Opciones → Abrir los programas que no estén abiertos*: entonces lo abre y lo
  coloca en cuanto sale su ventana. Las aplicaciones de la Tienda de Windows no se pueden abrir así. El aviso dice cuáles no
  estaban abiertos.
- **Los monitores** se reconocen por su nombre, aunque Windows les cambie el número. Si el de una zona no está conectado,
  sus programas van al principal, en el mismo sitio.

## Usarla

- La acción **Disposición de ventanas** pone una disposición desde cualquier comando.
- Con un [evento](events.md) **Pantalla conectada o desconectada** se pone sola: por ejemplo, una disposición «Película»
  al conectar la tele y otra «Trabajo» al desconectarla.
