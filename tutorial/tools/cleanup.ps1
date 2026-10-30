# Undoes prepare.ps1 once the demo is closed: the demo data and raw captures, the demo documents and MCP servers, and the
# folder of model hard links (deleting a hard link never touches the original model).
. "$PSScriptRoot\_paths.ps1"
if (Get-DemoPid) { & $Exe --data $Data --exit; Start-Sleep 6 }
Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like "*$Work*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*backdrop.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
foreach ($dir in $Knowledge, (Split-Path $McpScript), $DemoModels, $Work) { if (Test-Path $dir) { cmd /c rmdir /s /q "$dir"; "borrado: $dir" } }
