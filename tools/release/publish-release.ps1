<#
  Publishes the portable already built (artifacts\update, made by make-update.ps1 when the portable is published) as
  the GitHub release v<version> of K-Desktop's repository, marked as the latest, and deletes the K-Desktop release that
  was there before (and its tag): there is only ever one. The release «assistants» is never touched.

  Its files are KDesktop.exe, KDesktop.apk and kdesktop.json: the README's download links and K-Desktop's auto-updater
  (https://github.com/Kobayashi82/K-Desktop/releases/latest/download/kdesktop.json) read them from there.

  Before: raise <Version> in Directory.Build.props, publish the portable, and commit and push. Needs GitHub CLI logged
  in (gh auth login). -DryRun does everything but publish.

  Usage:
    powershell -ExecutionPolicy Bypass -File tools\release\publish-release.ps1 [-DryRun]
#>
param([switch]$DryRun)
# PowerShell 5.1 stops the script when a program writes to stderr while ErrorActionPreference is Stop (gh does).
$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$repo = 'Kobayashi82/K-Desktop'
$update = Join-Path $root 'artifacts\update'

function Step($text) { Write-Host "-- $text" -ForegroundColor Cyan }

# 1. What there is to publish.
$manifestFile = Join-Path $update 'kdesktop.json'
if (-not (Test-Path $manifestFile)) { throw "No está $manifestFile: publica antes el portable" }
$manifest = [System.IO.File]::ReadAllText($manifestFile) | ConvertFrom-Json
$version = $manifest.version
$tag = "v$version"
$exe = Join-Path $update $manifest.url
$apk = if ($manifest.android) { Join-Path $update $manifest.android.url } else { $null }
foreach ($file in @($exe, $apk)) { if ($file -and -not (Test-Path $file)) { throw "Falta $file" } }
$exeVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if (("{0}.{1}.{2}" -f $exeVersion.FileMajorPart, $exeVersion.FileMinorPart, $exeVersion.FileBuildPart) -ne $version) { throw "$exe no es la $version" }
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sha -ne $manifest.sha256) { throw 'El SHA-256 de KDesktop.exe no coincide con el de kdesktop.json' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { $env:PATH += ';C:\Program Files\GitHub CLI' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Falta GitHub CLI: winget install GitHub.cli y luego gh auth login' }

# 2. The notes.
$android = if ($manifest.android) { "`n| **KDesktop.apk** | App de Android $($manifest.android.version) para controlar el PC desde el móvil (Android 5.0 o superior). |" } else { '' }
$apkSha = if ($manifest.android) { "`nKDesktop.apk  $($manifest.android.sha256)" } else { '' }
$notes = @"
## Descargas

| Archivo | Qué es |
|---|---|
| **KDesktop.exe** | K-Desktop $version para Windows 10/11 (64 bits). Un único ejecutable portátil: no necesita instalar .NET. |$android

Versión preliminar: K-Desktop 5 sigue en desarrollo y las cosas cambian de una versión a otra. K-Desktop se actualiza
solo desde aquí (*Opciones → Actualizaciones*).

**Documentación:** https://kobayashi82.github.io/K-Desktop ([español](https://kobayashi82.github.io/K-Desktop/es/) · [English](https://kobayashi82.github.io/K-Desktop/en/)).
Los asistentes (Lander, Kobii, Kumo) se instalan desde K-Desktop: *Opciones → Módulo Asistente → Catálogo*.

## Primeros pasos

1. Descarga ``KDesktop.exe``, ponlo en la carpeta que quieras y ábrelo. La primera vez se registra para arrancar con
   Windows (se puede cambiar en Opciones). Los datos se guardan en ``%APPDATA%\K-Desktop 5``.
2. Para el móvil, instala ``KDesktop.apk``, activa la aplicación web en Opciones y empareja el móvil con el código QR.
   K-Desktop actualiza la app solo a partir de ahí.

## Avisos

- El ejecutable no está firmado digitalmente, así que Windows SmartScreen puede mostrar «Windows protegió su PC»:
  pulsa «Más información» › «Ejecutar de todas formas».
- Al instalar la APK fuera de Google Play, Play Protect puede avisar: pulsa «Instalar de todas formas».

**SHA-256**

``````
KDesktop.exe  $sha$apkSha
``````
"@
$notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "kdesktop-release-$PID.md"
[System.IO.File]::WriteAllText($notesFile, $notes, (New-Object System.Text.UTF8Encoding($false)))

# 3. The release before (only K-Desktop's: «v…»).
$before = @(gh release list -R $repo --limit 100 --json tagName -q '.[].tagName' 2>$null | Where-Object { $_ -like 'v*' -and $_ -ne $tag })
Step "Publicar $tag$(if ($before) { " y quitar $($before -join ', ')" })"
if ($DryRun) {
    Write-Host "Prueba: no se publica nada. Notas en $notesFile" -ForegroundColor Yellow
    return
}

# 4. Publish, then remove the one before.
$files = @($exe, $manifestFile) + @($apk | Where-Object { $_ })
gh release view $tag -R $repo *> $null
if ($LASTEXITCODE -eq 0) {
    gh release upload $tag @files -R $repo --clobber
    gh release edit $tag -R $repo --notes-file $notesFile --latest
} else {
    gh release create $tag @files -R $repo --target main --title "K-Desktop $version" --notes-file $notesFile --latest
}
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido publicar' }
Remove-Item $notesFile
foreach ($old in $before) {
    Step "Quitando $old"
    gh release delete $old -R $repo --cleanup-tag --yes
    git -C $root tag -d $old 2>$null | Out-Null
}
Write-Host "Publicada: https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
