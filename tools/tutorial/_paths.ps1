# Paths shared by the tutorial capture scripts (dot-source it: . "$PSScriptRoot\_paths.ps1").
# $Work holds everything temporary (demo data, raw captures, Edge profiles); KD_TUTORIAL overrides it.
# KD_TUTORIAL_LANG picks the language of the demo (en, the default, or es): its own work folder, documents and captures
# (docs\tutorial\captures\es), so both can be made without touching the other.
$Lang = if ($env:KD_TUTORIAL_LANG) { $env:KD_TUTORIAL_LANG } else { 'en' }
$Repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$Work = if ($env:KD_TUTORIAL) { $env:KD_TUTORIAL } else { Join-Path $env:TEMP ('kdesktop-tutorial' + $(if ($Lang -ne 'en') { "-$Lang" })) }
$Data = Join-Path $Work 'kddemo'
$Raw = Join-Path $Work 'out'
$Exe = Join-Path $Repo 'artifacts\portable\KDesktop.exe'
$Captures = Join-Path $Repo ('docs\tutorial\captures' + $(if ($Lang -ne 'en') { "\$Lang" }))
$Knowledge = Join-Path ([Environment]::GetFolderPath('MyDocuments')) $(if ($Lang -eq 'es') { 'Manuales' } else { 'Manuals' })
$McpScript = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MCP\servers.js'
$DemoModels = 'E:\AI Models'
$Ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $Ffmpeg) { $Ffmpeg = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }

# The demo instance (not the user's K-Desktop, not its assistant process).
function Get-DemoPid {
  Get-CimInstance Win32_Process -Filter "Name='KDesktop.exe'" |
    Where-Object { $_.CommandLine -like "*$Data*" -and $_.CommandLine -notlike '*--assistant*' -and $_.CommandLine -notlike '*--page*' -and $_.CommandLine -notlike '*--run*' } |
    Select-Object -First 1 -ExpandProperty ProcessId
}
function Get-DemoAssistantPid {
  Get-CimInstance Win32_Process -Filter "Name='KDesktop.exe'" | Where-Object { $_.CommandLine -like "*--assistant*" -and $_.CommandLine -like "*$Data*" } |
    Select-Object -First 1 -ExpandProperty ProcessId
}
# Sends arguments to the running demo instance (--page, --run, --menu, --launcher, --exit…).
function Send-Demo { & $Exe --data $Data @args }
# Minimizes the demo's main window: at 1920×1080 it covers the whole secondary monitor, and the assistants take that for
# a full-screen app and leave the monitor (also when they are put on the desktop).
function Hide-DemoWindow {
  Add-Type -Namespace KdTut -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);' -ErrorAction SilentlyContinue
  & "$PSScriptRoot\wins.ps1" -Pids (Get-DemoPid) | Where-Object { $_ -match "'K-Desktop' .* 1920x1080$" } |
    ForEach-Object { [KdTut.W]::ShowWindow([IntPtr][int64](($_ -split ' ')[1]), 6) | Out-Null }
}
