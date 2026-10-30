param([string]$Command, [string]$Name, [int]$Seconds = 20, [int]$Frames = 0)
# Records the secondary monitor with ffmpeg (no cursor) while the demo K-Desktop runs a command (--run), into
# $Work\out\<Name>.mp4; -Frames also takes a still every 0.7 s into $Work\out\<Name>\.
. "$PSScriptRoot\_paths.ps1"
if (-not $Ffmpeg) { 'ffmpeg no encontrado'; exit 1 }
$rec = Start-Process $Ffmpeg -ArgumentList '-y', '-f', 'gdigrab', '-framerate', '30', '-offset_x', '-1920', '-offset_y', '278', '-video_size', '1920x1080',
  '-draw_mouse', '0', '-t', "$Seconds", '-i', 'desktop', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '18', '-pix_fmt', 'yuv420p', "`"$Raw\$Name.mp4`"" -PassThru -WindowStyle Hidden
Start-Sleep 1.5
if ($Command) { Send-Demo --run $Command }
if ($Frames) {
  New-Item -ItemType Directory -Force "$Raw\$Name" | Out-Null
  foreach ($i in 1..$Frames) { Start-Sleep -Milliseconds 700; & "$PSScriptRoot\grab.ps1" -X -1920 -Y 278 -W 1920 -H 1080 -Out "$Raw\$Name\$i.png" | Out-Null }
}
$rec.WaitForExit(($Seconds + 10) * 1000) | Out-Null
"$Raw\$Name.mp4"
