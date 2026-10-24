<#
  Publishes a new version of one of K-Desktop's own assistants (Lander, Kobii and Kumo, in assistants\: Pip is only the
  SDK's example) to the fixed release «assistants» of K-Desktop's repository, as <id>.kai. That address
  never changes: catalog.json (the official catalog) lists it and the assistant's «updates» points to it, so whoever has
  it installed is told about the new version.

  What it does:
    1. Builds it in Release. The .kai comes out signed (SDK .targets, AssistantSign=true).
    2. Checks that K-Desktop would say «Verificado», and that its «updates» is its address in the release.
    3. Keeps a copy in artifacts\assistants\<id>-<version>.kai.
    4. Uploads it as <id>.kai to the release «assistants» (made the first time), replacing the one before.
  Raise «version» (and «changes») in its assistant.json before. Needs GitHub CLI logged in (winget install GitHub.cli,
  then gh auth login). -DryRun does everything but upload.

  Usage:
    powershell -ExecutionPolicy Bypass -File tools\release\publish-assistant.ps1 lander [-DryRun]
#>
param(
    [Parameter(Mandatory)] [ValidateSet('lander', 'kobii', 'kumo')] [string]$Assistant,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$repo = 'Kobayashi82/K-Desktop'
$tag = 'assistants'
$kaisignProject = Join-Path $root 'tools\KaiSign\KaiSign.csproj'

function Step($text) { Write-Host "-- $text" -ForegroundColor Cyan }

# 1. Build it.
$name = (Get-Culture).TextInfo.ToTitleCase($Assistant)
$project = Join-Path $root "assistants\$name\src\$name.Assistant"
if (-not (Test-Path $project)) { throw "No está $project (¿falta clonar K-Desktop-Assistants en assistants\?)" }
$manifestFile = Join-Path $project 'Package\assistant.json'
Step "Compilando $name en Release (sale firmado)"
dotnet build $project -c Release -nologo -v q -p:AssistantSign=true
if ($LASTEXITCODE -ne 0) { throw 'La compilación ha fallado' }
$manifest = [System.IO.File]::ReadAllText($manifestFile) | ConvertFrom-Json
$id = $manifest.id
$version = $manifest.version
$kai = Get-ChildItem (Join-Path $project 'bin\Release') -Recurse -Filter "$id-$version.kai" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $kai) { throw "No se encuentra $id-$version.kai" }

# 2. Checks.
Step "Comprobando $id $version"
$address = "https://github.com/$repo/releases/download/$tag/$id.kai"
if ($manifest.updates -ne $address) { throw "«updates» de su assistant.json tiene que ser $address (ahora: $($manifest.updates))" }
$verify = dotnet run --project $kaisignProject -v q -- verify $kai
Write-Host $verify
if ($LASTEXITCODE -ne 0 -or $verify -notmatch 'verificado') { throw 'La firma no sale como «Verificado»' }

# 3. A copy of each version.
$archive = Join-Path $root "artifacts\assistants\$id-$version.kai"
New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
if ((Resolve-Path $kai).Path -ne $archive) { Copy-Item $kai $archive -Force }
Write-Host "Copia: $archive"

# 4. Upload.
$upload = Join-Path ([System.IO.Path]::GetTempPath()) "$id.kai"
Copy-Item $archive $upload -Force
if ($DryRun) {
    Write-Host "Prueba: no se sube. Se subiría como $address" -ForegroundColor Yellow
    Remove-Item $upload
    return
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Falta GitHub CLI: winget install GitHub.cli y luego gh auth login' }
gh release view $tag -R $repo *> $null
if ($LASTEXITCODE -ne 0) {
    Step "Creando la release «$tag»"
    gh release create $tag -R $repo --title 'Asistentes de K-Desktop' --latest=false `
        --notes 'La última versión de cada asistente de K-Desktop (<id>.kai). K-Desktop los encuentra en su catálogo y avisa cuando hay una nueva.'
    if ($LASTEXITCODE -ne 0) { throw 'No se ha podido crear la release' }
}
Step "Subiendo $id.kai ($version)"
gh release upload $tag $upload -R $repo --clobber
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido subir' }
Remove-Item $upload
Write-Host "Publicado: $address ($version)" -ForegroundColor Green
