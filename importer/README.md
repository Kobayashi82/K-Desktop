# Importador de K-Desktop 4

Programa aparte que pasa a K-Desktop 5 los comandos, QuickMenus, opciones, sonidos e iconos de K-Desktop 4 (lo que
guarda en `HKCU\Software\KDesktop4` y `%APPDATA%\KDesktop 4.0`). Hasta la 5.0.262 lo hacía K-Desktop al arrancar por
primera vez; ahora va aquí, porque solo se usa una vez.

- `publish.ps1` hace `artifacts\importer\KDesktop.Importer.exe`: un solo archivo, con .NET dentro (no hace falta nada
  más; las DLL que salen al lado no las usa).
- Al abrirlo, ofrece cerrar K-Desktop 4 (respondería a los mismos atajos) y K-Desktop 5 (guardaría su configuración
  encima), avisa si ya hay configuración de K-Desktop 5 (se guarda una copia en `Backups`), importa y vuelve a abrir
  K-Desktop 5.
- `--data <carpeta>`: importa en otra carpeta de datos (entonces no toca el K-Desktop 5 abierto). `--si`: sin preguntas.
