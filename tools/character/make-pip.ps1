# Dibuja los fotogramas de Pip, el ejemplo de asistente sin código (samples/CharacterAssistant/Package), y su icono.
# powershell -File tools\character\make-pip.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\..\samples\CharacterAssistant\Package'
New-Item -ItemType Directory -Force (Join-Path $out 'frames') | Out-Null
$S = 160   # lado de cada fotograma

function New-Frame { $b = New-Object System.Drawing.Bitmap $S, $S; $g = [System.Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent); return @($b, $g) }

# Pip: una gota redonda con pies. $dy sube o baja el cuerpo, $squash lo aplasta, $eyes: open|closed|happy|sad|wide, $mouth: none|small|open|smile|frown|o
function Draw-Pip($g, [double]$dy = 0, [double]$squash = 0, [string]$eyes = 'open', [string]$mouth = 'small', [double]$feet = 0, [double]$tilt = 0, [switch]$NoFeet) {
    $cx = $S / 2; $base = $S - 14
    $w = 104 * (1 + $squash * 0.5); $h = 104 * (1 - $squash)
    $top = $base - 16 - $h + $dy
    $state = $g.Save()
    $g.TranslateTransform($cx, $base); $g.RotateTransform($tilt); $g.TranslateTransform(-$cx, -$base)
    # sombra
    $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(50, 0, 0, 0))
    $g.FillEllipse($shadow, $cx - 40, $base - 6, 80, 10)
    if (-not $NoFeet) {
        $foot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 96, 104))
        $g.FillEllipse($foot, $cx - 34 + $feet, $base - 16 + [Math]::Max(0, -$feet) * 0.3, 26, 16)
        $g.FillEllipse($foot, $cx + 8 - $feet, $base - 16 + [Math]::Max(0, $feet) * 0.3, 26, 16)
    }
    # cuerpo con degradado
    $rect = New-Object System.Drawing.RectangleF ($cx - $w / 2), $top, $w, $h
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($rect)
    $grad = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $grad.CenterPoint = New-Object System.Drawing.PointF ($cx - $w * 0.15), ($top + $h * 0.3)
    $grad.CenterColor = [System.Drawing.Color]::FromArgb(255, 120, 230, 220)
    $grad.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 24, 150, 160))
    $g.FillPath($grad, $path)
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 14, 84, 92)), 3
    $g.DrawEllipse($outline, $rect)
    # brillo
    $shine = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
    $g.FillEllipse($shine, $cx - $w * 0.3, $top + $h * 0.12, $w * 0.22, $h * 0.14)
    # ojos
    $ey = $top + $h * 0.42; $ex = $w * 0.18
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 30, 40))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 20, 30, 40)), 4
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($side in -1, 1) {
        $x = $cx + $side * $ex
        switch ($eyes) {
            'closed' { $g.DrawLine($pen, $x - 8, $ey + 2, $x + 8, $ey + 2) }
            'happy'  { $g.DrawArc($pen, $x - 9, $ey - 4, 18, 14, 200, 140) }
            'sad'    { $g.FillEllipse($dark, $x - 6, $ey - 4, 12, 14); $g.DrawLine($pen, $x - 9 * $side, $ey - 12, $x + 7 * $side, $ey - 8) }
            'wide'   { $g.FillEllipse($white, $x - 11, $ey - 12, 22, 24); $g.FillEllipse($dark, $x - 6, $ey - 6, 12, 12) }
            'up'     { $g.FillEllipse($dark, $x - 6, $ey - 12, 12, 14); $g.FillEllipse($white, $x - 3, $ey - 10, 4, 4) }
            default  { $g.FillEllipse($dark, $x - 7, $ey - 8, 14, 17); $g.FillEllipse($white, $x - 3, $ey - 5, 5, 5) }
        }
    }
    # mejillas
    $cheek = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 120, 140))
    $g.FillEllipse($cheek, $cx - $ex - 22, $ey + 10, 16, 9); $g.FillEllipse($cheek, $cx + $ex + 6, $ey + 10, 16, 9)
    # boca
    $my = $top + $h * 0.66
    switch ($mouth) {
        'small' { $g.DrawArc($pen, $cx - 7, $my - 6, 14, 10, 20, 140) }
        'smile' { $g.DrawArc($pen, $cx - 13, $my - 12, 26, 20, 15, 150) }
        'open'  { $g.FillEllipse($dark, $cx - 9, $my - 5, 18, 14) }
        'half'  { $g.FillEllipse($dark, $cx - 7, $my - 2, 14, 8) }
        'o'     { $g.FillEllipse($dark, $cx - 6, $my - 4, 12, 13) }
        'frown' { $g.DrawArc($pen, $cx - 10, $my, 20, 14, 200, 140) }
    }
    $g.Restore($state)
}

function Save-Frame($pair, [string]$name) { $pair[1].Dispose(); $pair[0].Save((Join-Path $out "frames\$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $pair[0].Dispose() }

# idle: respira y parpadea (4)
$idle = @(@(0, 0, 'open'), @(-2, 0.02, 'open'), @(0, 0, 'open'), @(0, 0, 'closed'))
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy $idle[$i][0] -squash $idle[$i][1] -eyes $idle[$i][2]; Save-Frame $f "idle$i" }
# walk: una tira de 6 (se dibuja cada fotograma y se pegan)
$strip = New-Object System.Drawing.Bitmap ($S * 6), $S
$sg = [System.Drawing.Graphics]::FromImage($strip); $sg.Clear([System.Drawing.Color]::Transparent)
for ($i = 0; $i -lt 6; $i++) {
    $f = New-Frame; $phase = [Math]::Sin($i / 6 * 2 * [Math]::PI)
    Draw-Pip $f[1] -dy (-[Math]::Abs($phase) * 6) -feet ($phase * 10) -tilt ($phase * 3)
    $f[1].Dispose(); $sg.DrawImage($f[0], $i * $S, 0); $f[0].Dispose()
}
$sg.Dispose(); $strip.Save((Join-Path $out 'frames\walk.png'), [System.Drawing.Imaging.ImageFormat]::Png); $strip.Dispose()
# talk (4), listen (2), think (2)
$talk = 'open', 'half', 'o', 'small'
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -mouth $talk[$i] -dy (-($i % 2) * 2); Save-Frame $f "talk$i" }
for ($i = 0; $i -lt 2; $i++) { $f = New-Frame; Draw-Pip $f[1] -eyes 'wide' -mouth 'o' -tilt (6 - $i * 2); Save-Frame $f "listen$i" }
for ($i = 0; $i -lt 2; $i++) { $f = New-Frame; Draw-Pip $f[1] -eyes 'up' -mouth 'small' -tilt (-4 - $i * 2); Save-Frame $f "think$i" }
# happy: salta (4), sad (2), surprised (2)
$jump = @(@(0, 0.15), @(-16, -0.05), @(-22, -0.06), @(-8, 0))
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy $jump[$i][0] -squash $jump[$i][1] -eyes 'happy' -mouth 'smile' -NoFeet:($i -in 1, 2); Save-Frame $f "happy$i" }
for ($i = 0; $i -lt 2; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy (4 + $i * 2) -squash 0.06 -eyes 'sad' -mouth 'frown'; Save-Frame $f "sad$i" }
for ($i = 0; $i -lt 2; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy (-8 + $i * 4) -squash (-0.08) -eyes 'wide' -mouth 'o'; Save-Frame $f "surprised$i" }
# drag (2): cuelga sin pies; fall (2); sleep (2)
for ($i = 0; $i -lt 2; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy -6 -squash (-0.1) -eyes 'wide' -mouth 'open' -tilt (($i * 2 - 1) * 8) -NoFeet; Save-Frame $f "drag$i" }
for ($i = 0; $i -lt 2; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy -10 -squash (-0.12) -eyes 'wide' -mouth 'o' -feet (($i * 2 - 1) * 8); Save-Frame $f "fall$i" }
for ($i = 0; $i -lt 2; $i++) {
    $f = New-Frame; Draw-Pip $f[1] -dy (6 + $i * 2) -squash (0.12 + $i * 0.02) -eyes 'closed' -mouth 'none'
    $font = New-Object System.Drawing.Font 'Segoe UI', (14 + $i * 4), ([System.Drawing.FontStyle]::Bold)
    $f[1].DrawString('z', $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 60, 90, 140))), (112 + $i * 8), (28 - $i * 10))
    Save-Frame $f "sleep$i"
}
# icono 128x128: el primer fotograma
$icon = New-Object System.Drawing.Bitmap 128, 128
$ig = [System.Drawing.Graphics]::FromImage($icon); $ig.SmoothingMode = 'AntiAlias'; $ig.InterpolationMode = 'HighQualityBicubic'
$first = [System.Drawing.Image]::FromFile((Join-Path $out 'frames\idle0.png'))
$ig.DrawImage($first, -16, -20, 160, 160); $first.Dispose(); $ig.Dispose()
$icon.Save((Join-Path $out 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png); $icon.Dispose()
'hecho'
