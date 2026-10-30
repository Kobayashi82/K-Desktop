# Sends a file of the PC to the user's own Telegram bot (sendDocument). The token and the chat come in the environment
# (TELEGRAM_TOKEN, TELEGRAM_CHAT), never as arguments: they would be seen in the list of processes.
param([string]$Path, [string]$Caption = "")
$ErrorActionPreference = "Stop"
$Path = $Path.Trim().Trim('"')
if (-not $Path -or -not [System.IO.Path]::IsPathRooted($Path)) { Write-Output "Give the full path of the file (its folder and its name, as find_files says)."; exit 1 }
if (Test-Path -LiteralPath $Path -PathType Container) { Write-Output "$Path is a folder: send its files one by one."; exit 1 }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Write-Output "There is no file $Path."; exit 1 }
$file = Get-Item -LiteralPath $Path
# What a bot may send at most.
if ($file.Length -gt 50MB) { Write-Output ("{0} is {1:0} MB: Telegram bots can only send files up to 50 MB." -f $file.Name, ($file.Length / 1MB)); exit 1 }
if (-not $env:TELEGRAM_TOKEN -or -not $env:TELEGRAM_CHAT) { Write-Output "The secrets telegram_token and telegram_chat are missing (Options > Tools > Secrets)."; exit 1 }

Add-Type -AssemblyName System.Net.Http
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(110)
$form = New-Object System.Net.Http.MultipartFormDataContent
$stream = [System.IO.File]::OpenRead($file.FullName)
try {
    $form.Add((New-Object System.Net.Http.StringContent($env:TELEGRAM_CHAT)), "chat_id")
    if ($Caption) { $form.Add((New-Object System.Net.Http.StringContent($Caption)), "caption") }
    $content = New-Object System.Net.Http.StreamContent($stream)
    $content.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream")
    $form.Add($content, "document", $file.Name)
    $response = $client.PostAsync("https://api.telegram.org/bot$($env:TELEGRAM_TOKEN)/sendDocument", $form).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
} catch {
    Write-Output "Telegram could not be reached: $($_.Exception.InnerException.Message)"
    exit 1
} finally {
    $stream.Dispose(); $form.Dispose(); $client.Dispose()
}
if (-not $response.IsSuccessStatusCode) {
    $why = $body
    try { $why = ($body | ConvertFrom-Json).description } catch { }
    # «Forbidden: bot can't initiate conversation with a user»: the user never pressed Start in the bot.
    Write-Output "Telegram did not accept it ($([int]$response.StatusCode)): $why"
    exit 1
}
Write-Output ("Sent {0} ({1:0.0} MB) to the user's Telegram." -f $file.Name, ($file.Length / 1MB))
