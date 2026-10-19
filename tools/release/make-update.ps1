<#
  Prepares an update for K-Desktop's auto-updater from the portable build:
    artifacts\update\KDesktop-<version>.exe
    artifacts\update\KDesktop-<version>.apk   the Android app (it also travels inside the exe: phones update from the PC)
    artifacts\update\kdesktop.json          { version, date, url, sha256, size, notes, android }
  Upload the folder's files to where the "Update address" points (default https://kobayashi82.net/kdesktop/).
  Publishing the portable runs it by itself (target MakeUpdate in KDesktop.csproj).

  Usage:
    1. Raise <Version> in Directory.Build.props (e.g. 5.0.1) and build the portable:
         dotnet publish src\KDesktop\KDesktop.csproj -c Release -p:PublishProfile=SelfContained
    2. Notes for the update dialog (optional; kept while the version stays the same):
         powershell -ExecutionPolicy Bypass -File tools\release\make-update.ps1 -NotesEs "…" -NotesEn "…"
#>
param(
  [string]$Exe = "",
  [string]$Apk = "",
  [string]$OutDir = "",
  [string]$NotesEs = "",
  [string]$NotesEn = ""
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
if (-not $Exe) { $Exe = Join-Path $root "artifacts\portable\KDesktop.exe" }
if (-not $Apk) { $Apk = Join-Path $root "artifacts\android\KDesktop.apk" }
if (-not $OutDir) { $OutDir = Join-Path $root "artifacts\update" }
if (-not (Test-Path $Exe)) { throw "No existe ${Exe} (publica antes el portable)." }

$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Exe)
$version = "{0}.{1}.{2}" -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart
$name = "KDesktop-$version.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null

# The notes of this same version when none are given (the build runs this script without them).
$manifestPath = Join-Path $OutDir "kdesktop.json"
$previous = $null
if (Test-Path $manifestPath) { try { $previous = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json } catch { } }

# Only this version's files stay.
Get-ChildItem $OutDir -Filter "KDesktop-*" | Where-Object { $_.Name -notlike "KDesktop-$version.*" } | Remove-Item -Force
Copy-Item $Exe (Join-Path $OutDir $name) -Force

$sha = (Get-FileHash (Join-Path $OutDir $name) -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item (Join-Path $OutDir $name)).Length
$notes = [ordered]@{}
if ($NotesEs) { $notes.es = $NotesEs }
if ($NotesEn) { $notes.en = $NotesEn }
if ($notes.Count -eq 0 -and $previous -and $previous.version -eq $version -and $previous.notes) { $notes = $previous.notes }
$manifest = [ordered]@{
  version = $version
  date = (Get-Date -Format "yyyy-MM-dd")
  url = $name
  sha256 = $sha
  size = $size
  notes = $notes
}

if ((Test-Path $Apk) -and (Test-Path "$Apk.json")) {
  $apkInfo = [System.IO.File]::ReadAllText("$Apk.json") | ConvertFrom-Json
  $apkName = "KDesktop-$($apkInfo.version).apk"
  Copy-Item $Apk (Join-Path $OutDir $apkName) -Force
  $manifest.android = [ordered]@{
    version = $apkInfo.version
    code = $apkInfo.code
    url = $apkName
    sha256 = (Get-FileHash (Join-Path $OutDir $apkName) -Algorithm SHA256).Hash.ToLowerInvariant()
    size = (Get-Item (Join-Path $OutDir $apkName)).Length
  }
}

# UTF-8 without BOM (PowerShell 5.1's -Encoding utf8 adds one).
$json = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
# The zip to hand out (artifacts\release): the portable exe compressed, 170 MB → about 70 MB. Only this version's.
$releaseDir = Join-Path $root "artifacts\release"
New-Item -ItemType Directory -Force $releaseDir | Out-Null
$zip = Join-Path $releaseDir "K-Desktop-$version.zip"
Get-ChildItem $releaseDir -Filter "K-Desktop-*.zip" | Where-Object { $_.FullName -ne $zip } | Remove-Item -Force
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $Exe, "K-Desktop/KDesktop.exe", [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null }
finally { $archive.Dispose() }

"K-Desktop $version -> $OutDir"
"  release: $zip ($([Math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
"  $name ($([Math]::Round($size / 1MB, 1)) MB)"
if ($manifest.android) { "  $($manifest.android.url) ($([Math]::Round($manifest.android.size / 1KB)) KB)" }
"  kdesktop.json (sha256 $sha)"
