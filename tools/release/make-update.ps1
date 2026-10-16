<#
  Prepares an update for K-Desktop's auto-updater from the portable build:
    artifacts\update\KDesktop-<version>.exe
    artifacts\update\latest.json          { version, date, url, sha256, size, notes }
  Upload both files to the folder the "Update address" points to (default https://kobayashi82.net/kdesktop/).

  Usage:
    1. Raise <Version> in Directory.Build.props (e.g. 5.0.1) and build the portable:
         dotnet publish src\KDesktop\KDesktop.csproj -c Release -p:PublishProfile=SelfContained
    2. powershell -ExecutionPolicy Bypass -File tools\release\make-update.ps1 -NotesEs "…" -NotesEn "…"
#>
param(
  [string]$Exe = "",
  [string]$OutDir = "",
  [string]$NotesEs = "",
  [string]$NotesEn = ""
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
if (-not $Exe) { $Exe = Join-Path $root "artifacts\portable\KDesktop.exe" }
if (-not $OutDir) { $OutDir = Join-Path $root "artifacts\update" }
if (-not (Test-Path $Exe)) { throw "No existe ${Exe} (publica antes el portable)." }

$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Exe)
$version = "{0}.{1}.{2}" -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart
$name = "KDesktop-$version.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null
Copy-Item $Exe (Join-Path $OutDir $name) -Force

$sha = (Get-FileHash (Join-Path $OutDir $name) -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item (Join-Path $OutDir $name)).Length
$notes = [ordered]@{}
if ($NotesEs) { $notes.es = $NotesEs }
if ($NotesEn) { $notes.en = $NotesEn }
$manifest = [ordered]@{
  version = $version
  date = (Get-Date -Format "yyyy-MM-dd")
  url = $name
  sha256 = $sha
  size = $size
  notes = $notes
}
# UTF-8 without BOM (PowerShell 5.1's -Encoding utf8 adds one).
$json = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $OutDir "latest.json"), $json, (New-Object System.Text.UTF8Encoding($false)))
"K-Desktop $version -> $OutDir"
"  $name ($([Math]::Round($size / 1MB, 1)) MB)"
"  latest.json (sha256 $sha)"
