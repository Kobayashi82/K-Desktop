# Firmar y publicar

## Firmar el paquete

Un paquete firmado lleva `signature.json` con el SHA-256 de **todos** sus archivos, quién lo publica, su clave pública
(ECDSA P-256) y la firma. No hay que pagar a nadie: la clave es tuya. K-Desktop la comprueba al abrir la ficha de
instalación:

| Lo que encuentra | Lo que enseña |
|---|---|
| Una firma buena de una clave de su lista de editores de confianza | **Verificado · nombre**, también en *Opciones → Asistentes* |
| Una firma buena de otra clave | «Firmado por «nombre»», y el aviso de siempre: ese nombre es solo lo que dice el paquete |
| Ninguna firma | «Sin firma» y el aviso de siempre |
| Una firma que no cuadra (un archivo cambiado, añadido o quitado después de firmar) | **No se instala** |

Si la versión instalada estaba firmada y la nueva no la firma la misma clave, la ficha avisa en rojo.

Haz tu clave una vez con `tools/KaiSign` (en el repositorio de K-Desktop); desde entonces **cada compilación firma el `.kai`**
sola, justo después de empaquetarlo:

```powershell
dotnet run --project tools\KaiSign -- keygen "Tu nombre"      # una vez: la clave se queda en tu usuario de Windows (DPAPI)
dotnet run --project tools\KaiSign -- export copia.pem         # una copia de la clave con contraseña, para guardarla aparte
dotnet run --project tools\KaiSign -- verify miasistente.kai   # lo que diría K-Desktop
```

| Propiedad de compilación | |
|---|---|
| (nada) | Se firma si tu usuario de Windows tiene clave; si no, un mensaje dice que va sin firmar |
| `-p:AssistantSign=false` | Nunca se firma |
| `-p:AssistantSign=true` | Error si no se puede firmar (para las compilaciones que publicas) |
| `KaiSignProject` / `KaiSignExe` | Dónde está kaisign: su `.csproj` (se compila desde las fuentes) o un `kaisign.exe` ya compilado |

Un paquete firmado a mano (`kaisign sign miasistente.kai`) vale igual. Firma **lo último**: cualquier cambio en el paquete
después rompe la firma. Para que tus paquetes salgan como *Verificado*, tu clave pública (`kaisign public`) tiene que estar
en la lista de K-Desktop.

## Versiones nuevas

Si el manifiesto dice dónde está siempre el último paquete, K-Desktop busca versiones nuevas junto con las suyas (al
arrancar, cada 6 horas y con *Buscar ahora* en *Opciones → Actualizaciones*) y las **ofrece**: un aviso, una barra en la
ventana, la fila del asistente en *Opciones → Asistentes* y *Opciones → Actualizaciones*, con *Actualizar…* (lo descarga y
abre su ficha) y *Omitir esta versión*. Nunca actualiza un asistente sin preguntar.

```json
"updates": "https://github.com/yo/mis-asistentes/releases/latest/download/miasistente.kai",
"changes": { "es": "Salta más alto y habla más rápido.", "en": "Jumps higher and talks faster." }
```

- `updates` es una dirección fija que da siempre el último `.kai` (en GitHub, la de `releases/latest/download/…`). https;
  `http://localhost` y `http://127.0.0.1` valen para probar con tu propio servidor.
- Para comprobarla, K-Desktop **solo lee el `assistant.json`** del paquete publicado, con peticiones por rangos (unos KB); un
  servidor que no las admite manda el paquete entero cada vez.
- Se ofrece si la versión es mayor y el id es el mismo. Si necesita un SDK más nuevo que el de ese K-Desktop, al usuario se
  le dice que actualice antes K-Desktop.
- `changes` (opcional, en uno o varios idiomas) es lo que trae esa versión: sale en el aviso y en la ficha.

## Ponerlo en un catálogo

*Opciones → Módulo Asistente → Catálogo* enseña los asistentes de los catálogos que lee (el de K-Desktop y los que añada el
usuario) con *Instalar…* y *Actualizar…*. Un catálogo es un JSON publicado en una dirección fija
([esquema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/catalog.schema.json)) que solo dice
dónde está cada paquete:

```json
{
  "catalog": 1,
  "assistants": [
    { "id": "miasistente", "package": "https://github.com/yo/mis-asistentes/releases/latest/download/miasistente.kai" }
  ]
}
```

- `package` es la misma dirección que `updates`: la que da siempre el último `.kai`.
- De cada paquete K-Desktop solo lee `assistant.json`, el icono y los vídeos que abra el usuario, por rangos: el nombre, la
  descripción, la versión, el autor y si funciona con ese K-Desktop salen del propio paquete. Si el `id` no coincide con el
  del paquete, no se ofrece.
- Estar en un catálogo no hace que un paquete sea de fiar: la ficha dice si está **verificado** y qué sabe hacer antes de
  instalarlo.
