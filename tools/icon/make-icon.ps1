<#
  Generates K-Desktop's icon from vector shapes (WPF), so every size is drawn sharp instead of scaled:
    src\KDesktop\Assets\Icons\KDesktop.ico      exe, windows and tray (16…256 px)
    src\KDesktop.Core\Web\icons\*.png          web app / PWA
    tools\icon\out\*.png                       other sizes (Android launcher icons, previews)
  Design: a gold squircle (the app's accent) with a dark geometric "K".
  Run: powershell -ExecutionPolicy Bypass -File tools\icon\make-icon.ps1
#>
param([string]$Root = (Resolve-Path "$PSScriptRoot\..\.."))
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = "Stop"

function Color($hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

# Draws the icon on a 256-unit canvas. $full: square background to the edges (maskable / apple-touch), $small: bolder
# strokes and less margin for 16-24 px.
function Draw-Icon([System.Windows.Media.DrawingContext]$dc, [bool]$full, [bool]$small, [double]$glyphScale = 1.0, [bool]$round = $false) {
  $m = if ($full) { 0 } elseif ($round) { 6 } elseif ($small) { 4 } else { 12 }
  # Round (Telegram, avatars): a circle, its corner radius half its side.
  $r = if ($full) { 0 } elseif ($round) { 128 - $m } elseif ($small) { 52 } else { 58 }
  $rect = New-Object System.Windows.Rect($m, $m, (256 - 2 * $m), (256 - 2 * $m))

  $bg = New-Object System.Windows.Media.LinearGradientBrush
  $bg.StartPoint = New-Object System.Windows.Point(0.15, 0); $bg.EndPoint = New-Object System.Windows.Point(0.85, 1)
  $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFFFD25A"), 0)))
  $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFF2B21C"), 0.5)))
  $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#FFD98A00"), 1)))
  $dc.DrawRoundedRectangle($bg, $null, $rect, $r, $r)

  if (-not $small) {
    # Soft light on the upper half and a thin darker rim: depth without looking skeuomorphic.
    $gloss = New-Object System.Windows.Media.LinearGradientBrush
    $gloss.StartPoint = New-Object System.Windows.Point(0.5, 0); $gloss.EndPoint = New-Object System.Windows.Point(0.5, 0.55)
    $gloss.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#55FFFFFF"), 0)))
    $gloss.GradientStops.Add((New-Object System.Windows.Media.GradientStop((Color "#00FFFFFF"), 1)))
    $dc.DrawRoundedRectangle($gloss, $null, $rect, $r, $r)
    if (-not $full) {
      $rim = New-Object System.Windows.Media.Pen((New-Object System.Windows.Media.SolidColorBrush((Color "#40995C00"))), 3)
      $inner = New-Object System.Windows.Rect(($m + 1.5), ($m + 1.5), (256 - 2 * $m - 3), (256 - 2 * $m - 3))
      $dc.DrawRoundedRectangle($null, $rim, $inner, ($r - 1.5), ($r - 1.5))
    }
  }

  # The K, centred; scaled down for maskable icons (safe zone).
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($glyphScale, $glyphScale, 128, 128)))
  $ink = New-Object System.Windows.Media.SolidColorBrush((Color "#FF221A0C"))
  $stroke = if ($small) { 38 } else { 30 }
  $pen = New-Object System.Windows.Media.Pen($ink, $stroke)
  $pen.StartLineCap = "Round"; $pen.EndLineCap = "Round"; $pen.LineJoin = "Round"
  # stem
  $dc.DrawLine($pen, (New-Object System.Windows.Point(88, 70)), (New-Object System.Windows.Point(88, 186)))
  # upper arm, from the stem up to the right; the lower leg leaves from a third of the way along it
  $dc.DrawLine($pen, (New-Object System.Windows.Point(88, 150)), (New-Object System.Windows.Point(166, 74)))
  $dc.DrawLine($pen, (New-Object System.Windows.Point(116, 123)), (New-Object System.Windows.Point(170, 186)))
  $dc.Pop()
}

function Render([int]$size, [bool]$full = $false, [double]$glyphScale = 1.0, [bool]$round = $false) {
  $small = $size -le 24 -and -not $full
  $visual = New-Object System.Windows.Media.DrawingVisual
  $dc = $visual.RenderOpen()
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))
  Draw-Icon $dc $full $small $glyphScale $round
  $dc.Pop(); $dc.Close()
  [System.Windows.Media.RenderOptions]::SetEdgeMode($visual, "Unspecified")
  $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($visual)
  $bmp
}

function Png-Bytes($bmp) {
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = New-Object System.IO.MemoryStream
  $enc.Save($ms); return , $ms.ToArray()
}

function Save-Png($bmp, $path) {
  New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
  [System.IO.File]::WriteAllBytes($path, (Png-Bytes $bmp))
}

# 32-bpp DIB entry (BITMAPINFOHEADER + bottom-up BGRA, straight alpha + AND mask): what every Windows API reads.
function Dib-Bytes($bmp) {
  $s = $bmp.PixelWidth
  $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($bmp, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
  $px = New-Object byte[] ($s * $s * 4); $conv.CopyPixels($px, $s * 4, 0)
  $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
  $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $s * 4, $s * 4) }
  $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
  $bw.Write((New-Object byte[] ($maskRow * $s)))
  $bw.Flush(); return , $ms.ToArray()
}

function Save-Ico([int[]]$sizes, $path) {
  $images = New-Object System.Collections.Generic.List[byte[]]
  foreach ($s in $sizes) { $b = Render $s; if ($s -ge 128) { $images.Add((Png-Bytes $b)) } else { $images.Add((Dib-Bytes $b)) } }
  $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$images[$i].Length); $bw.Write([int]$offset)
    $offset += $images[$i].Length
  }
  foreach ($img in $images) { $bw.Write($img) }
  $bw.Flush(); [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
}

$icons = Join-Path $Root "src\KDesktop\Assets\Icons"
$web = Join-Path $Root "src\KDesktop.Core\Web\icons"
$out = Join-Path $Root "tools\icon\out"

Save-Ico @(16, 20, 24, 32, 40, 48, 64, 128, 256) (Join-Path $icons "KDesktop.ico")
Save-Png (Render 64) (Join-Path $web "favicon.png")
Save-Png (Render 192) (Join-Path $web "icon-192.png")
Save-Png (Render 512) (Join-Path $web "icon-512.png")
Save-Png (Render 512 $true 0.8) (Join-Path $web "maskable-512.png")
Save-Png (Render 180 $true 0.9) (Join-Path $web "apple-touch-icon.png")
foreach ($s in 48, 72, 96, 144, 192, 512) { Save-Png (Render $s) (Join-Path $out "icon-$s.png") }
# Android adaptive icon layers (108 dp canvas, content inside the central 72 dp)
foreach ($s in 108, 162, 216, 324, 432) { Save-Png (Render $s $true 0.62) (Join-Path $out "adaptive-$s.png") }
# Round ones (avatars: Telegram, Discord, GitHub…): a circle on transparent corners, and for a profile picture that the
# site crops to a circle itself, the square to the edges with the K a little smaller (it must stay inside the circle).
$round = Join-Path $out "round"
foreach ($s in 32, 48, 64, 96, 128, 192, 256, 512, 1024) { Save-Png (Render $s $false 1.0 $true) (Join-Path $round "round-$s.png") }
foreach ($s in 512, 640, 1024) { Save-Png (Render $s $true 0.85) (Join-Path $round "avatar-$s.png") }
"done"
