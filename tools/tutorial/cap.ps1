param([string]$Page, [string]$Out, [int]$Wait = 2000, [switch]$Save)
# Opens a page of the demo K-Desktop (--page commands, settings/brain, assistant/lander…) and captures its main window with
# PrintWindow into $Work\out\<Out>.png; -Save also copies it to docs\tutorial\captures\<Out>.png.
. "$PSScriptRoot\_paths.ps1"
if ($Page) { Send-Demo --page $Page; Start-Sleep -Milliseconds $Wait }
& "$PSScriptRoot\shot.ps1" -ProcessId (Get-DemoPid) -Out "$Raw\$Out.png"
if ($Save) { Copy-Item "$Raw\$Out.png" "$Captures\$Out.png" }
