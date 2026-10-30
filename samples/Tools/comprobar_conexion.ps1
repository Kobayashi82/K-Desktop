param([string]$Host_ = "192.168.1.1")
$ping = New-Object System.Net.NetworkInformation.Ping
$times = foreach ($i in 1..3) {
    try { $reply = $ping.Send($Host_, 1500); if ($reply.Status -eq "Success") { $reply.RoundtripTime } } catch { }
}
if (-not $times) { Write-Output "$Host_ does not answer"; exit 1 }
Write-Output ("{0} answers: {1} of 3 pings, {2} ms on average" -f $Host_, @($times).Count, [math]::Round((@($times) | Measure-Object -Average).Average))
