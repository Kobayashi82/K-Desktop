# Empaqueta una carpeta de asistente (assistant.json y lo demás) en <id>-<version>.kai, junto a la carpeta o en -Out.
# powershell -File tools\character\pack.ps1 samples\CharacterAssistant\Package [-Out carpeta]
param([Parameter(Mandatory)] [string]$Folder, [string]$Out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$Folder = (Resolve-Path $Folder).Path
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $Folder 'assistant.json') | ConvertFrom-Json
if (-not $Out) { $Out = Split-Path $Folder -Parent }
New-Item -ItemType Directory -Force $Out | Out-Null
$file = Join-Path $Out ("{0}-{1}.kai" -f $manifest.id, $manifest.version)
if (Test-Path $file) { Remove-Item $file }
$zip = [System.IO.Compression.ZipFile]::Open($file, 'Create')
try {
    foreach ($item in Get-ChildItem $Folder -Recurse -File) {
        # Dentro del paquete, siempre con «/».
        $name = $item.FullName.Substring($Folder.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $item.FullName, $name, 'Optimal') | Out-Null
    }
} finally { $zip.Dispose() }
$file
