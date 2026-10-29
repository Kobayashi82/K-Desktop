# Textos

En *Textos* guardas abreviaturas cortas que, al teclearlas en cualquier programa, K-Desktop cambia por un texto más
largo: tu firma, tu dirección, una respuesta que repites a menudo.

1. *Textos → Nuevo texto*.
2. Escribe la **abreviatura** (`firma`) y el **texto** que la sustituye.
3. En cualquier programa, teclea el prefijo y la abreviatura (`;firma`): se borra y aparece el texto.

Prueba cada uno en la caja **Pruébalo** de la misma página. Dentro de K-Desktop solo se escriben ahí, nunca mientras los
editas.

## Opciones

- **Escribir al teclear**: apaga o enciende todos los textos a la vez. Cada texto tiene además su interruptor
  **Activado**, en la lista y en su ficha.
- **Prefijo**: va delante de cada abreviatura (`;` de serie) para que no salte al escribir normal. Puedes cambiarlo o
  dejarlo vacío; sin prefijo, una abreviatura solo salta como palabra entera (`ok` no salta dentro de `book`).

En cada texto, **Texto**, **Pruébalo**, **Opciones** y **Notas** se pliegan, y se quedan como los dejes de un texto al
siguiente.

- **Solo en estas aplicaciones** (en *Opciones*): vacío, el texto vale en todas partes. Con aplicaciones, solo salta con
  una de ellas delante, también si están en *Desactivar en*. Así puedes tener la misma abreviatura con un texto para el
  correo y otro para el resto: el de la aplicación gana ahí. En la lista, debajo de cada texto, se ve dónde vale, como en K-Board.
- **Notas**: lo que quieras apuntar sobre el texto; no se escribe nunca.

## Cuándo salta

- En cuanto terminas de teclear la abreviatura.
- Si otra abreviatura empieza igual (`;fe` y `;fecha`), la corta espera a que pulses **espacio**, **Tab** o **Intro**.
  Con espacio, el espacio se queda detrás del texto. Debajo de la abreviatura se ve cómo salta la tuya.
- Si escribes la abreviatura en mayúsculas (`;FIRMA`), el texto sale en mayúsculas; con la primera en mayúscula
  (`;Firma`), el texto empieza con mayúscula.
- **No salta** en las contraseñas, en los juegos o películas a pantalla completa, en los programas de *Desactivar en*
  ni con K-Desktop en pausa.

## Lo que puede llevar el texto

Escribe `{` en el texto para elegir:

| Variable | Qué pone |
|---|---|
| `{cursor}` | Nada: deja el cursor ahí al acabar (por ejemplo `<b>{cursor}</b>`) |
| `{fecha}` | La fecha de hoy (2026-10-29) |
| `{hora}` | La hora (09:30:05) |
| `{portapapeles}` | El texto copiado |
| `{portapapeles:2}` | Lo copiado antes de lo último (`:3`, `:4`… más atrás) |

Los textos cortos se teclean. Los largos o con varias líneas se pegan (así un salto de línea no envía el mensaje en un
chat) y lo que tenías copiado vuelve a su sitio.
