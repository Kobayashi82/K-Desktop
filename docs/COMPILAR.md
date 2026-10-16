# Compilar, publicar y actualizar K-Desktop

Todos los comandos se escriben en una terminal (PowerShell) abierta en la carpeta del proyecto
(`D:\Programas\Utilidades\Backup\Mis Programas\K-Desktop 5.0`).

## Resumen rápido

| Quiero… | Comando |
|---|---|
| Compilar todo (programa, portable, APK y carpeta de actualización) | `dotnet build KDesktop.slnx -c Release` |
| Solo el portable (+ APK + carpeta de actualización) | `dotnet publish src\KDesktop\KDesktop.csproj -c Release -p:PublishProfile=SelfContained` |
| Solo la APK | `powershell -ExecutionPolicy Bypass -File tools\android\build-apk.ps1` |
| Compilar rápido para probar (Debug, sin portable) | `dotnet build KDesktop.slnx` |
| Cerrar el K-Desktop que está abierto | `artifacts\portable\KDesktop.exe --exit` |

Lo normal es usar la primera: hace todo de una vez.

## Lo que hay que tener instalado

| Qué | Dónde | Para qué |
|---|---|---|
| SDK de .NET 10 | instalado en Windows (`dotnet --version` → 10.x) | compilar K-Desktop |
| JDK 17 (portable) | `%LOCALAPPDATA%\Programs\jdk-17` | compilar la APK |
| SDK de Android (platform 34, build-tools 34.0.0, emulador) | `%LOCALAPPDATA%\Android\Sdk` | compilar la APK y probarla |

Si falta el JDK o el SDK de Android, K-Desktop compila igual, pero sin generar una APK nueva: mete la última que haya
en `artifacts\android`, o ninguna. También se pueden indicar otras rutas con las variables `JAVA_HOME` y
`ANDROID_HOME`.

## Qué genera cada cosa (carpeta `artifacts`)

| Carpeta | Contenido |
|---|---|
| `artifacts\portable\KDesktop.exe` | El programa en un solo `.exe` (no necesita .NET). Es el que usas a diario. |
| `artifacts\android\KDesktop.apk` | La app de Android (y `KDesktop.apk.json` con su versión). |
| `artifacts\update\` | Lo que se sube al servidor de actualizaciones (ver abajo). |
| `artifacts\build\` | Archivos intermedios; se pueden borrar sin problema. |

`artifacts` no se sube a GitHub (está en `.gitignore`).

Si K-Desktop está abierto mientras publicas, no pasa nada: el `.exe` en uso se renombra a `KDesktop.exe.<fecha>.old`
y el nuevo ocupa su sitio. Para usar el nuevo hay que cerrar el programa (`--exit`) y volver a abrirlo. Los `.old`
se borran solos en la siguiente publicación.

## Sacar una versión nueva (actualización)

1. Sube la versión en `Directory.Build.props`, por ejemplo `<Version>5.0.1</Version>`. Es la versión del programa
   y también la de la APK (5.0.1 → código 50001).
2. Compila: `dotnet build KDesktop.slnx -c Release`.
3. En `artifacts\update\` quedan:
   - `KDesktop-5.0.1.exe`: el programa.
   - `KDesktop-5.0.1.apk`: la app de Android, por si quieres instalarla a mano en otro móvil.
   - `latest.json`: lo que consulta K-Desktop para saber si hay versión nueva.
4. Opcional: notas que se ven en el aviso de actualización. Se conservan mientras no cambies la versión:
   ```
   powershell -ExecutionPolicy Bypass -File tools\release\make-update.ps1 -NotesEs "Qué hay de nuevo" -NotesEn "What's new"
   ```
5. Sube **los tres archivos** a la dirección de actualizaciones (por defecto `https://kobayashi82.net/kdesktop/`, se
   cambia en Opciones › Actualizaciones). Los K-Desktop instalados la ven en un máximo de 6 horas, o al momento con
   «Buscar ahora».

### Cómo se actualiza la app del móvil

La app de Android **no** consulta el servidor: se la pasa el propio PC. Cada K-Desktop lleva dentro la APK de su
versión. Cuando el móvil se conecta a un PC con una APK más nueva, avisa una vez y en Ajustes aparece «Actualizar la
app». Al pulsarlo la descarga del PC y abre el instalador de Android. La primera vez Android pide permiso para
«instalar apps de este origen»; se activa y continúa solo.

Las apps instaladas antes del 16-10-2026 no saben actualizarse: hay que instalar una vez a mano la nueva
(`artifacts\update\KDesktop-<versión>.apk`). Play Protect avisará porque no viene de la tienda: hay que pulsar
«Instalar de todas formas».

## La clave de la APK (¡importante!)

Android solo acepta una actualización si está firmada con **la misma clave** que la app instalada. Si se pierde la
clave, habrá que desinstalar la app en cada móvil y volver a instalarla (y emparejar de nuevo).

La clave son dos archivos:

- `kdesktop-release.jks`: la clave.
- `kdesktop-release.jks.password.txt`: su contraseña.

El script de la APK los busca, por este orden, en:

1. `keys\` dentro del proyecto. Esta carpeta **no** se sube a GitHub (está en `.gitignore`), así que entra en tus
   copias de seguridad de la carpeta del proyecto pero nunca en el repositorio público.
2. `%USERPROFILE%\.android\` (`C:\Users\Kobay\.android\`). Ahí se creó la primera vez y ahí está ahora.

Si no la encuentra en ninguno de los dos sitios, crea una clave nueva en `keys\`. **No dejes que pase si ya hay
móviles con la app**: primero recupera la antigua.

Para tener una copia en el proyecto (recomendado), ejecuta una vez:

```
New-Item -ItemType Directory -Force keys
Copy-Item "$env:USERPROFILE\.android\kdesktop-release.jks*" keys\
```

Y guarda también una copia fuera del PC (un USB o tu nube privada). **Nunca** la subas a GitHub ni la mandes por
chat.

## Probar sin tocar tus datos

K-Desktop puede arrancar con otra carpeta de datos, sin atajos globales ni UDP, para hacer pruebas sin afectar al
que usas:

```
artifacts\portable\KDesktop.exe --data "C:\ruta\a\datos-de-prueba" --no-elevate --no-hooks --no-udp
```

Otras opciones de la línea de comandos:

| Opción | Qué hace |
|---|---|
| `--exit` | Cierra el K-Desktop abierto (si no hay ninguno, no hace nada). |
| `--show` | Muestra la ventana. |
| `--page <página>` | Abre la ventana en esa página. |
| `--run <comando>` / `--menu <menú>` | Ejecuta un comando / abre un QuickMenu. |
| `--no-web` | Arranca sin la app web. |

### Probar la APK en el emulador

```
& "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe" -avd kdtest
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install -r artifacts\android\KDesktop.apk
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" reverse tcp:2502 tcp:2502
```

Con el `reverse`, en la app se pone `http://localhost:2502` y entra como «este PC», sin emparejar.

En el móvil de verdad: copia la APK al móvil (o descárgala) e instálala.

## Iconos

El icono (exe, bandeja, web y Android) se dibuja con `tools\icon\make-icon.ps1`:

```
powershell -ExecutionPolicy Bypass -File tools\icon\make-icon.ps1
```

Genera el `.ico`, los PNG de la web y, en `tools\icon\out`, los de Android. Estos hay que copiarlos a
`android\res\mipmap-*`: `icon-48/72/96/144/192.png` → `ic_launcher.png` de `mdpi/hdpi/xhdpi/xxhdpi/xxxhdpi`, y
`adaptive-108/162/216/324/432.png` → `ic_launcher_foreground.png`. Además, `icon-192.png` → `android\assets\icon.png`.
