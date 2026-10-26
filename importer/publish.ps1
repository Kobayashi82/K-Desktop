# Makes artifacts\importer\KDesktop.Importer.exe: one file, with .NET inside (whoever runs it needs nothing installed).
# The native libraries of K-Desktop's voice come out next to it, but the importer never loads them: only the .exe is needed.
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts\importer'
if (Test-Path $out) { cmd /c rmdir /s /q "$out" }
dotnet publish (Join-Path $PSScriptRoot 'KDesktop.Importer.csproj') -c Release -o $out `
    -p:SelfContained=true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { Write-Host 'No se pudo publicar el importador' -ForegroundColor Red; exit 1 }
$exe = Join-Path $out 'KDesktop.Importer.exe'
Write-Host ("Listo: {0} ({1:N0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)) -ForegroundColor Green
