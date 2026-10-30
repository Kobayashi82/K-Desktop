param([string[]]$Extra = @())
# Starts the demo K-Desktop (the published portable) with the demo data: hooks on (no «hooks off» banner), not elevated (so
# UI Automation can drive it) and never the real microphone: KDESKTOP_VOICE_FAKE gives the wake word wizard the takes of
# fakevoice\ and KDESKTOP_VOICE_FAKE_SESSION is what any listening «hears».
. "$PSScriptRoot\_paths.ps1"
$env:KDESKTOP_VOICE_FAKE = "$Work\fakevoice"
$env:KDESKTOP_VOICE_FAKE_SESSION = "$Work\session.wav"
$arguments = @('--data', $Data, '--no-elevate') + $Extra
$p = Start-Process -FilePath $Exe -ArgumentList ($arguments | ForEach-Object { if ($_ -match ' ') { '"' + $_ + '"' } else { $_ } }) -PassThru
"pid $($p.Id)"
