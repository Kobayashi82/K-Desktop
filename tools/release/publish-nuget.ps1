<#
  Publishes the assistant SDK and its dotnet new template to nuget.org:
    KDesktop.Assistant.Sdk         the SDK assembly, build\KDesktop.Assistant.Sdk.targets (makes the .kai) and kaisign
    KDesktop.Assistant.Templates   the kdassistant template, which references that package

  What it does:
    1. Checks the versions: the SDK's PackageVersion and the template's sdkVersion have to be the same.
    2. Builds kaisign (framework-dependent) and packs both into artifacts\nuget.
    3. Tries them as a user would, in a temporary folder with its own NuGet cache: installs the template, makes a new
       assistant, builds it and checks the .kai (assistant.json, its dll, no SDK inside).
    4. Pushes them to nuget.org (skipped with -DryRun). The API key comes from $env:NUGET_API_KEY or is asked (never
       saved). A version already on nuget.org is skipped: raise PackageVersion to publish again.

  Usage:
    powershell -ExecutionPolicy Bypass -File tools\release\publish-nuget.ps1 [-DryRun]
#>
param([switch]$DryRun)
# PowerShell 5.1 stops the script when a program writes to stderr while ErrorActionPreference is Stop (dotnet does):
# programs are checked with $LASTEXITCODE and cmdlets with -ErrorAction Stop.
$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sdkProject = Join-Path $root 'src\KDesktop.Assistant.Sdk\KDesktop.Assistant.Sdk.csproj'
$templatesProject = Join-Path $root 'templates\KDesktop.Assistant.Templates.csproj'
$out = Join-Path $root 'artifacts\nuget'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "kdesktop-nuget-$PID"

function Step($text) { Write-Host "-- $text" -ForegroundColor Cyan }
function Run { & $args[0] @($args | Select-Object -Skip 1) 2>&1 | ForEach-Object { "$_" }; if ($LASTEXITCODE -ne 0) { throw "$($args -join ' ') ha fallado" } }
function VersionIn($file, $pattern) { ([regex]::Match([System.IO.File]::ReadAllText($file), $pattern)).Groups[1].Value }

# 1. Versions.
$sdkVersion = VersionIn $sdkProject '<PackageVersion>([^<]+)</PackageVersion>'
$templatesVersion = VersionIn $templatesProject '<PackageVersion>([^<]+)</PackageVersion>'
$templateSdk = VersionIn (Join-Path $root 'templates\kdassistant\.template.config\template.json') '"sdkVersion"[\s\S]*?"defaultValue":\s*"([^"]+)"'
Step "SDK $sdkVersion · plantilla $templatesVersion (usa el SDK $templateSdk)"
if ($templateSdk -ne $sdkVersion) { throw "La plantilla usa el SDK $templateSdk y el SDK es ${sdkVersion}: cambia sdkVersion en templates\kdassistant\.template.config\template.json" }

# 2. Pack.
if (Test-Path $work) { cmd /c rmdir /s /q "$work" }
New-Item -ItemType Directory -Force $work, $out -ErrorAction Stop | Out-Null
Get-ChildItem $out -Filter '*.nupkg' | Remove-Item -Force
Step 'Compilando kaisign'
Run dotnet publish (Join-Path $root 'tools\KaiSign\KaiSign.csproj') -c Release --self-contained false -o (Join-Path $work 'kaisign') -nologo -v q | Out-Null
Step 'Empaquetando'
Run dotnet pack $sdkProject -c Release -o $out -nologo -v q "-p:KaiSignDir=$(Join-Path $work 'kaisign')" | Out-Null
Run dotnet pack $templatesProject -c Release -o $out -nologo -v q | Out-Null
$packages = Get-ChildItem $out -Filter '*.nupkg'
$packages | ForEach-Object { Write-Host "   $($_.Name) ($([Math]::Round($_.Length / 1KB)) KB)" }

# 3. Try them as a user would (own NuGet cache: a repacked version must not come from the usual one).
Step 'Probando: plantilla nueva, compilar y mirar el .kai'
$env:NUGET_PACKAGES = Join-Path $work 'packages'
$try = Join-Path $work 'try'
New-Item -ItemType Directory -Force $try -ErrorAction Stop | Out-Null
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$out" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $try 'nuget.config') -Encoding UTF8
$hive = Join-Path $work 'hive'
$templatesPackage = ($packages | Where-Object Name -like 'KDesktop.Assistant.Templates.*').FullName
Run dotnet new install $templatesPackage --debug:custom-hive $hive | Out-Null
Push-Location $try
try {
    Run dotnet new kdassistant -n TryAssistant --authorName Test --debug:custom-hive $hive | Out-Null
    Run dotnet build TryAssistant -nologo -v q -p:AssistantSign=false | Out-Null
} finally { Pop-Location; Remove-Item Env:NUGET_PACKAGES }
$kai = Get-ChildItem (Join-Path $try 'TryAssistant\bin') -Recurse -Filter 'tryassistant-1.0.0.kai' | Select-Object -First 1
if (-not $kai) { throw 'La plantilla no ha dado un .kai' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($kai.FullName)
$entries = $zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
$zip.Dispose()
if ($entries -notcontains 'assistant.json' -or $entries -notcontains 'lib/TryAssistant.dll') { throw "Al .kai le falta algo: $($entries -join ', ')" }
if ($entries -match 'KDesktop\.Assistant\.Sdk') { throw 'El .kai lleva el SDK dentro (lo pone siempre K-Desktop)' }
Write-Host "   $($kai.Name): $($entries.Count) archivos, sin el SDK"

# 4. Push.
if ($DryRun) {
    Write-Host "Prueba: no se sube nada. Paquetes en $out" -ForegroundColor Yellow
    cmd /c rmdir /s /q "$work"
    return
}
$key = $env:NUGET_API_KEY
if (-not $key) {
    $secure = Read-Host 'Clave de API de nuget.org (no se guarda)' -AsSecureString
    $key = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if (-not $key) { throw 'Sin clave de API no se puede subir' }
foreach ($package in $packages) {
    Step "Subiendo $($package.Name)"
    & dotnet nuget push $package.FullName --api-key $key --source https://api.nuget.org/v3/index.json --skip-duplicate 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -notmatch [regex]::Escape($key) }
    if ($LASTEXITCODE -ne 0) { throw "No se ha podido subir $($package.Name)" }
}
cmd /c rmdir /s /q "$work"
Write-Host 'Subidos. nuget.org los valida y los enseña en unos minutos:' -ForegroundColor Green
Write-Host '  https://www.nuget.org/packages/KDesktop.Assistant.Sdk'
Write-Host '  https://www.nuget.org/packages/KDesktop.Assistant.Templates'
