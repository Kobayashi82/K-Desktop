param([switch]$Stop)
# The demo desktop on the secondary monitor: a borderless window with the wallpaper over its work area (sent to the
# bottom), and two Edge windows in English with their own temporary profiles (Wikipedia's Apollo Lunar Module and Hacker
# News). Edge only keeps the size it is opened with: moving it later with SetWindowPos stretches it to 1460 px high.
. "$PSScriptRoot\_paths.ps1"
Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like "*$Work*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*backdrop.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if ($Stop) { 'parado'; exit 0 }
Start-Process powershell -ArgumentList '-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-File', "`"$PSScriptRoot\backdrop.ps1`"" | Out-Null
Start-Sleep 2
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$common = @('--no-first-run', '--no-default-browser-check', '--hide-crash-restore-bubble')
Start-Process $edge -ArgumentList (@("--user-data-dir=`"$Work\edge1`"") + $common + @('--window-position=-1830,340', '--window-size=1120,740', 'https://en.wikipedia.org/wiki/Apollo_Lunar_Module'))
Start-Sleep 4
Start-Process $edge -ArgumentList (@("--user-data-dir=`"$Work\edge2`"") + $common + @('--window-position=-800,440', '--window-size=740,600', 'https://news.ycombinator.com'))
Start-Sleep 4
'escritorio de demo listo'
