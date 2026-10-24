<#
  Publishes the documentation website (site\, DocFX) to GitHub Pages: https://kobayashi82.github.io/K-Desktop

  What it does:
    1. Builds it (dotnet docfx site\docfx.json) and stops if there is any warning.
    2. Puts site\_site in a temporary repository with a single commit (and .nojekyll, so GitHub serves the folders that
       start with «_» as they are).
    3. Force-pushes it to the branch gh-pages of K-Desktop's repository: the branch only ever has the last version.
  No GitHub Actions: Pages serves the branch as it is. -DryRun does everything but push.

  Usage:
    powershell -ExecutionPolicy Bypass -File site\publish.ps1 [-DryRun]
#>
param([switch]$DryRun)
# PowerShell 5.1 stops the script when a program writes to stderr while ErrorActionPreference is Stop (docfx and git
# do): programs are checked with $LASTEXITCODE and cmdlets with -ErrorAction Stop.
$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$site = Join-Path $root 'site'
$output = Join-Path $site '_site'
$remote = 'https://github.com/Kobayashi82/K-Desktop.git'
$branch = 'gh-pages'

function Step($text) { Write-Host "-- $text" -ForegroundColor Cyan }
function Invoke-Git { & git.exe @args 2>&1 | ForEach-Object { "$_" }; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') ha fallado" } }

# 1. Build it.
Step 'Compilando la web'
Push-Location $root
try {
    & dotnet tool restore 2>&1 | Out-Null
    $log = & dotnet docfx (Join-Path $site 'docfx.json') 2>&1 | ForEach-Object { "$_" } | Out-String
    $built = $LASTEXITCODE
} finally { Pop-Location }
Write-Host $log.Trim()
if ($built -ne 0 -or $log -notmatch 'Build succeeded') { throw 'La compilación ha fallado' }
if ($log -notmatch '\b0 warning') { throw 'La compilación tiene avisos: arréglalos antes de publicar' }

# 2. A repository with only the website.
Step "Preparando la rama $branch"
$commit = (Invoke-Git -C $root rev-parse --short HEAD | Select-Object -Last 1).Trim()
$name = (Invoke-Git -C $root config user.name | Select-Object -Last 1).Trim()
$email = (Invoke-Git -C $root config user.email | Select-Object -Last 1).Trim()
$work = Join-Path ([System.IO.Path]::GetTempPath()) "kdesktop-pages-$PID"
if (Test-Path $work) { cmd /c rmdir /s /q "$work" }
New-Item -ItemType Directory $work -ErrorAction Stop | Out-Null
Copy-Item (Join-Path $output '*') $work -Recurse -Force -ErrorAction Stop
New-Item -ItemType File (Join-Path $work '.nojekyll') -Force -ErrorAction Stop | Out-Null
Invoke-Git -C $work init -q -b $branch | Out-Null
Invoke-Git -C $work add -A | Out-Null
Invoke-Git -C $work -c "user.name=$name" -c "user.email=$email" commit -q -m "Web de la documentación ($commit)" | Out-Null

# 3. Push.
if ($DryRun) {
    Write-Host "Prueba: no se sube nada. La web está en $work" -ForegroundColor Yellow
    return
}
Step "Subiendo a $branch"
Invoke-Git -C $work push -q -f $remote "${branch}:${branch}" | Out-Null
cmd /c rmdir /s /q "$work"
Write-Host 'Publicada: https://kobayashi82.github.io/K-Desktop (GitHub Pages tarda un minuto en ponerla al día)' -ForegroundColor Green
