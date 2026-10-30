param([string]$Name, [string]$Type, [switch]$CloseOthers)
# Opens a modal window of the demo without waiting (a UI Automation Invoke on what opens a modal does not return until it
# closes), or with -CloseOthers closes every demo window but the main one (WM_CLOSE: never waits either).
. "$PSScriptRoot\_paths.ps1"
$demo = Get-DemoPid
if ($CloseOthers) {
  Add-Type -Namespace KdTut -Name C -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);' -ErrorAction SilentlyContinue
  & "$PSScriptRoot\wins.ps1" -Pids $demo | Where-Object { $_ -match "'K-Desktop' " -and $_ -notmatch ' 1920x1080$' } |
    ForEach-Object { [KdTut.C]::PostMessage([IntPtr][int64](($_ -split ' ')[1]), 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; 'closed' }
  exit 0
}
$args2 = @('-NoProfile', '-File', "`"$PSScriptRoot\ui.ps1`"", '-ProcessId', $demo, '-Name', "`"$Name`"")
if ($Type) { $args2 += @('-Type', $Type) }
Start-Process powershell -ArgumentList $args2 -WindowStyle Hidden | Out-Null
Start-Sleep 3
'opened'
