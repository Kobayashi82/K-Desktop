# Sin código

Un asistente puede ser solo imágenes y JSON: en vez de un ensamblado, su `entry` es un `character.json`
([esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/character.schema.json)) y lo mueve
K-Desktop.

Se pone en la barra de tareas o encima de las ventanas, pasea de vez en cuando (y a veces se cae por el borde de una
ventana), sigue a la ventana sobre la que está, se puede coger y soltar, duerme cuando no estás y hace una animación para
cada cosa: escucharte, pensar, hablar en su bocadillo, alegrarse o apenarse de cómo ha ido, un clic. Habla, piensa y usa
herramientas como cualquier otro asistente; su voz y su personalidad se ponen en *Personalidad* y *Voz*. Con varios
asistentes en el escritorio, nunca se queda encima de otro.

Nada del paquete se ejecuta: K-Desktop solo lee sus imágenes y sonidos, y su ficha de instalación dice que no tiene código.
**Pip** ([`samples/CharacterAssistant`](https://github.com/Kobayashi82/K-Desktop/tree/main/samples/CharacterAssistant)) es un
ejemplo completo.

## Los archivos

```
MiPersonaje\
  assistant.json      "entry": "character.json", "traits": { "travel": "walk", "gestures": ["communicate", "react", "announce"] }
  character.json
  icon.png
  frames\idle0.png …  PNG con transparencia, todos de la misma altura
  sounds\hola.wav     opcional
```

```json
{
  "height": 80,
  "faces": "right",
  "walkSpeed": 60,
  "animations": {
    "idle": { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2 },
    "walk": { "strip": "frames/walk.png", "frames": 6, "fps": 10 },
    "talk": { "frames": ["frames/talk0.png", "frames/talk1.png"], "fps": 8, "sound": "sounds/hola.wav" }
  }
}
```

| Campo | |
|---|---|
| `height` | Su altura al 100 % de tamaño; todos los fotogramas se dibujan así de altos. Sus pies son el borde de abajo de la imagen, salvo que `ground` diga otra cosa |
| `ground` | Filas transparentes bajo sus pies en las imágenes (píxeles de las imágenes, 0 por defecto). Esa parte de cada imagen queda por debajo del suelo, así los pies lo tocan (se puede dejar ahí una sombra bajo los pies) |
| `faces` | Hacia dónde miran las imágenes; andando hacia el otro lado se ponen en espejo |
| `walkSpeed` | Lo rápido que anda |
| `animations` | Las animaciones, por nombre (abajo) |

## Animaciones

| Nombre | Cuándo |
|---|---|
| `idle` | En reposo (**la única obligatoria**) |
| `walk` | Andando |
| `talk`, `listen`, `think` | Hablando en su bocadillo, escuchándote, pensando |
| `happy`, `sad`, `surprised` | Cómo ha ido: bien, mal, o una sorpresa |
| `click` | Le has hecho clic |
| `drag`, `fall` | Cogido con el ratón, cayendo |
| `sleep` | No estás |
| `enter`, `exit` | Al llegar al escritorio y al irse |
| `announce` | Antes de decir algo por su cuenta (un temporizador, un aviso): primero anda hacia el ratón |
| `catch` | Le sueltan archivos encima (mientras están encima, lleva su imagen sobre la cabeza) |

Una que falta usa otra: `think` → `listen` → `idle`, `click` → `happy` → `idle`, `announce` y `catch` → `happy`,
`drag` → `fall` → `idle`…

Cada animación tiene:

- `frames` (una lista de imágenes) **o** `strip` + `frames` (una tira de N fotogramas del mismo ancho);
- `fps`;
- `loop`: por defecto se repiten todas salvo `happy`, `sad`, `surprised`, `click`, `enter`, `exit`, `announce` y `catch`;
- `sound`: un WAV que suena al empezar.

## Variantes

En vez de una animación, una lista. Cada vez que toca, K-Desktop elige una por su `weight` (el doble de peso, el doble de
veces; 1 por defecto), respetando su `cooldown` (los segundos que descansa después de salir), y sin repetir la última en
las que se hacen una vez; las que se repiten (`idle`, `walk`, `talk`…) vuelven a elegir al acabar cada vuelta.

En `enter` y `exit`, `when` dice para qué motivo es cada una: `startup` (arranca K-Desktop o se enciende el módulo) y
`summoned` (el usuario lo pone en el escritorio); `dismissed` (el usuario lo esconde) y `uninstalling` (se quita). Sin
ninguna para ese motivo, no hay entrada ni salida; volver tras una recarga (una versión nueva, otro idioma) nunca la tiene.

```json
"idle": [
  { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2, "weight": 6 },
  { "frames": ["frames/look0.png", "frames/look1.png"], "fps": 2, "cooldown": 20 }
],
"enter": [
  { "frames": ["frames/wake0.png", "frames/wake1.png"], "fps": 4, "when": ["startup"] },
  { "frames": ["frames/drop0.png", "frames/drop1.png"], "fps": 8, "when": ["summoned"] }
]
```

## Ajustes

El tamaño, el volumen, lo movido que es y si se puede coger con el ratón los pone el usuario en su ficha (los
[ajustes comunes](getting-started.md#ajustes-comunes)); un personaje los usa todos.

## Hacer el paquete

```powershell
tools\character\pack.ps1 <carpeta>
```

Comprime la carpeta (con `assistant.json` en su raíz) en `<id>-<versión>.kai` con las rutas como las quiere K-Desktop.
Ábrelo con K-Desktop para instalarlo. Para grabar sus vídeos para el catálogo, ver [Escaparate](showcase.md#grabarlos).
