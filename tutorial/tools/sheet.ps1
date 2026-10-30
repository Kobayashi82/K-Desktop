param([string[]]$Names, [string]$Out)
# Four captures of $Work\out at half size in a 2×2 sheet (to review several at once).
. "$PSScriptRoot\_paths.ps1"
Add-Type -AssemblyName System.Drawing
$o = New-Object System.Drawing.Bitmap 1920, 1080
$g = [System.Drawing.Graphics]::FromImage($o)
$g.InterpolationMode = 'HighQualityBicubic'
$i = 0
foreach ($n in $Names) {
  $b = [System.Drawing.Image]::FromFile("$Raw\$n.png")
  $g.DrawImage($b, ($i % 2) * 960, [math]::Floor($i / 2) * 540, 960, 540)
  $b.Dispose(); $i++
}
$o.Save("$Raw\$Out.png"); $o.Dispose()
"$Raw\$Out.png"
