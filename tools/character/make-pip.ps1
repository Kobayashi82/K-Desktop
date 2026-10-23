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
            'left'   { $g.FillEllipse($dark, $x - 11, $ey - 8, 14, 17); $g.FillEllipse($white, $x - 8, $ey - 5, 5, 5) }
            'right'  { $g.FillEllipse($dark, $x - 3, $ey - 8, 14, 17); $g.FillEllipse($white, $x + 1, $ey - 5, 5, 5) }
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
        'wide'  { $g.FillEllipse($dark, $cx - 14, $my - 10, 28, 22) }
    }
    $g.Restore($state)
}

# Lo que le sale de la cabeza: $kind = '!' | '?' | 'note' | 'sparkle' | 'blush'; $lift sube la marca.
function Draw-Mark($g, [string]$kind, [double]$lift = 0, [double]$alpha = 255) {
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb([int]$alpha, 230, 90, 60))
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb([int]$alpha, 40, 110, 190))
    $x = $S / 2 + 34; $y = 18 - $lift
    switch ($kind) {
        '!' {
            $g.FillRectangle($ink, $x - 3, $y, 7, 18)
            $g.FillEllipse($ink, $x - 4, $y + 22, 9, 9)
        }
        '?' {
            $font = New-Object System.Drawing.Font 'Segoe UI', 30, ([System.Drawing.FontStyle]::Bold)
            $g.DrawString('?', $font, $blue, $x - 16, $y - 14)
        }
        'note' {
            $font = New-Object System.Drawing.Font 'Segoe UI Symbol', 30, ([System.Drawing.FontStyle]::Bold)
            $g.DrawString([string][char]0x266A, $font, $blue, $x - 16, $y - 12)
        }
        'sparkle' {
            $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb([int]$alpha, 255, 200, 40))
            foreach ($p in @(@(-60, 26, 7), @(54, 10, 9), @(62, 60, 6), @(-52, 70, 5))) {
                $cx = $S / 2 + $p[0]; $cy = $p[1] - $lift; $r = $p[2]
                $pts = @(
                    (New-Object System.Drawing.PointF $cx, ($cy - $r)), (New-Object System.Drawing.PointF ($cx + $r * 0.3), ($cy - $r * 0.3)),
                    (New-Object System.Drawing.PointF ($cx + $r), $cy), (New-Object System.Drawing.PointF ($cx + $r * 0.3), ($cy + $r * 0.3)),
                    (New-Object System.Drawing.PointF $cx, ($cy + $r)), (New-Object System.Drawing.PointF ($cx - $r * 0.3), ($cy + $r * 0.3)),
                    (New-Object System.Drawing.PointF ($cx - $r), $cy), (New-Object System.Drawing.PointF ($cx - $r * 0.3), ($cy - $r * 0.3)))
                $g.FillPolygon($gold, $pts)
            }
        }
    }
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
# Variantes (etapa 13.11)
# idle: mira a un lado y al otro (4)
$look = 'left', 'left', 'right', 'right'
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -eyes $look[$i] -tilt $(if ($i -lt 2) { -3 } else { 3 }); Save-Frame $f "look$i" }
# happy: se menea (4)
$wiggle = -12, 12, -12, 0
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy -4 -eyes 'happy' -mouth 'smile' -tilt $wiggle[$i] -feet ($wiggle[$i] / 2); Save-Frame $f "wiggle$i" }
# enter al arrancar: se despierta y se estira (4)
$wake = @(@(6, 0.12, 'closed', 'none'), @(2, 0.04, 'closed', 'small'), @(-8, -0.1, 'wide', 'o'), @(0, 0, 'open', 'smile'))
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy $wake[$i][0] -squash $wake[$i][1] -eyes $wake[$i][2] -mouth $wake[$i][3]; Save-Frame $f "wake$i" }
# enter al ponerlo: cae del cielo y aterriza (4)
$drop = @(@(-120, -0.12, 'wide', 'o'), @(-50, -0.1, 'wide', 'o'), @(4, 0.2, 'happy', 'smile'), @(0, 0, 'open', 'smile'))
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy $drop[$i][0] -squash $drop[$i][1] -eyes $drop[$i][2] -mouth $drop[$i][3] -NoFeet:($i -lt 2); Save-Frame $f "drop$i" }
# exit al esconderlo: se despide (4)
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -eyes 'happy' -mouth 'smile' -tilt ((($i % 2) * 2 - 1) * 9); Save-Frame $f "bye$i" }
# exit al quitarlo: triste, se hunde (4)
for ($i = 0; $i -lt 4; $i++) { $f = New-Frame; Draw-Pip $f[1] -dy (6 + $i * 14) -squash (0.08 + $i * 0.1) -eyes 'sad' -mouth 'frown' -NoFeet:($i -ge 2); Save-Frame $f "sink$i" }

# Más animaciones y variantes (etapa 15.3)
# Un fotograma con su marca encima: $marks = lista de @(kind, lift) o $null.
function Save-Pip([string]$name, [hashtable]$pip, $mark = $null) {
    $f = New-Frame
    if ($pip) { Draw-Pip $f[1] @pip }
    if ($mark) { Draw-Mark $f[1] $mark[0] $mark[1] }
    Save-Frame $f $name
}
# click: se aplasta y se ríe (4) / se menea con la risa (4)
$squish = @(@{ squash = 0.26; dy = 4; eyes = 'happy'; mouth = 'small' }, @{ squash = 0.1; eyes = 'happy'; mouth = 'smile' }, @{ squash = -0.06; dy = -6; eyes = 'happy'; mouth = 'smile' }, @{ eyes = 'open'; mouth = 'smile' })
for ($i = 0; $i -lt 4; $i++) { Save-Pip "squish$i" $squish[$i] }
$giggle = -7, 7, -7, 0
for ($i = 0; $i -lt 4; $i++) { Save-Pip "giggle$i" @{ eyes = 'happy'; mouth = 'open'; tilt = $giggle[$i]; dy = -($i % 2) * 3 } }
# announce: brinca con una exclamación (4) / te llama con la boca abierta (4)
$hop = @(@{ squash = 0.16; dy = 4; eyes = 'wide'; mouth = 'o' }, @{ dy = -18; squash = -0.06; eyes = 'wide'; mouth = 'open'; NoFeet = $true }, @{ dy = -24; squash = -0.08; eyes = 'happy'; mouth = 'open'; NoFeet = $true }, @{ eyes = 'open'; mouth = 'smile' })
for ($i = 0; $i -lt 4; $i++) { Save-Pip "hey$i" $hop[$i] @('!', ($i * 3)) }
for ($i = 0; $i -lt 4; $i++) { Save-Pip "call$i" @{ eyes = 'open'; mouth = $(if ($i % 2) { 'o' } else { 'open' }); tilt = (4 - ($i % 2) * 8); dy = -($i % 2) * 4 } @('!', ($i % 2) * 6) }
# catch: abre la boca, se lo traga y se alegra (4)
$gulp = @(@{ eyes = 'wide'; mouth = 'wide'; dy = -4 }, @{ eyes = 'wide'; mouth = 'wide'; dy = -8; squash = -0.06 }, @{ eyes = 'closed'; mouth = 'none'; squash = 0.16; dy = 4 }, @{ eyes = 'happy'; mouth = 'smile' })
for ($i = 0; $i -lt 4; $i++) { Save-Pip "gulp$i" $gulp[$i] $(if ($i -eq 3) { @('sparkle', 0) } else { $null }) }
# sad: se derrite un poco (4)
for ($i = 0; $i -lt 4; $i++) { Save-Pip "droop$i" @{ eyes = 'sad'; mouth = 'frown'; squash = (0.12 + [Math]::Min($i, 2) * 0.08); dy = (6 + [Math]::Min($i, 2) * 6) } }
# surprised: da un respingo hacia atrás (3)
$jolt = @(@{ eyes = 'wide'; mouth = 'o'; dy = -14; tilt = -10; NoFeet = $true }, @{ eyes = 'wide'; mouth = 'o'; dy = -6; tilt = -6 }, @{ eyes = 'wide'; mouth = 'small' })
for ($i = 0; $i -lt 3; $i++) { Save-Pip "jolt$i" $jolt[$i] @('!', 4) }
# think: mira a un lado y a otro con una interrogación (4)
$ponder = 'left', 'up', 'right', 'up'
for ($i = 0; $i -lt 4; $i++) { Save-Pip "ponder$i" @{ eyes = $ponder[$i]; mouth = 'small'; tilt = (-3 + ($i % 2) * 6) } @('?', ($i % 2) * 4) }
# idle: bosteza (4) / canturrea (4)
$yawn = @(@{ eyes = 'closed'; mouth = 'o'; squash = -0.04; dy = -2 }, @{ eyes = 'closed'; mouth = 'wide'; squash = -0.08; dy = -4 }, @{ eyes = 'closed'; mouth = 'wide'; squash = -0.08; dy = -4 }, @{ eyes = 'closed'; mouth = 'small'; squash = 0.04 })
for ($i = 0; $i -lt 4; $i++) { Save-Pip "yawn$i" $yawn[$i] }
for ($i = 0; $i -lt 4; $i++) { Save-Pip "hum$i" @{ eyes = 'happy'; mouth = $(if ($i % 2) { 'small' } else { 'o' }); tilt = (-4 + ($i % 2) * 8) } @('note', $i * 5) }
# happy: salta entre destellos (4)
for ($i = 0; $i -lt 4; $i++) { Save-Pip "shine$i" @{ dy = $jump[$i][0]; squash = $jump[$i][1]; eyes = 'happy'; mouth = 'open'; NoFeet = ($i -in 1, 2) } @('sparkle', ($i * 4)) }
# enter al arrancar: sale de un charquito (4)
$rise = @(@{ dy = 50; squash = 0.4; eyes = 'closed'; mouth = 'none'; NoFeet = $true }, @{ dy = 34; squash = 0.28; eyes = 'closed'; mouth = 'small'; NoFeet = $true }, @{ dy = -6; squash = -0.08; eyes = 'wide'; mouth = 'o' }, @{ eyes = 'open'; mouth = 'smile' })
for ($i = 0; $i -lt 4; $i++) { Save-Pip "rise$i" $rise[$i] }
# enter al ponerlo: aparece entre destellos (4)
Save-Pip 'pop0' $null @('sparkle', 0)
Save-Pip 'pop1' @{ squash = 0.32; dy = 10; eyes = 'closed'; mouth = 'none' } @('sparkle', 6)
Save-Pip 'pop2' @{ squash = -0.1; dy = -8; eyes = 'wide'; mouth = 'open' } @('sparkle', 12)
Save-Pip 'pop3' @{ eyes = 'happy'; mouth = 'smile' }
# exit al esconderlo: se va de un salto hacia arriba (4)
$away = @(@{ eyes = 'happy'; mouth = 'smile' }, @{ squash = 0.18; dy = 4; eyes = 'happy'; mouth = 'smile' }, @{ dy = -50; squash = -0.12; eyes = 'happy'; mouth = 'open'; NoFeet = $true }, @{ dy = -130; squash = -0.12; eyes = 'happy'; mouth = 'open'; NoFeet = $true })
for ($i = 0; $i -lt 4; $i++) { Save-Pip "away$i" $away[$i] }

# Sonidos (mono, 22 050 Hz, 16 bits), sintetizados aquí: suaves y cortos.
New-Item -ItemType Directory -Force (Join-Path $out 'sounds') | Out-Null
function Save-Wav([string]$name, [double]$seconds, [scriptblock]$sample) {
    $rate = 22050; $n = [int]($rate * $seconds)
    $data = New-Object byte[] ($n * 2)
    for ($i = 0; $i -lt $n; $i++) {
        $t = $i / $rate
        $v = [Math]::Max(-1, [Math]::Min(1, (& $sample $t ($t / $seconds))))
        $s = [int16]([Math]::Round($v * 32000))
        $data[$i * 2] = [byte]($s -band 0xFF); $data[$i * 2 + 1] = [byte](($s -shr 8) -band 0xFF)
    }
    $file = [System.IO.File]::Create((Join-Path $out "sounds\$name.wav"))
    $w = New-Object System.IO.BinaryWriter $file
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $data.Length)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int]($rate * 2)); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]$data.Length); $w.Write($data); $w.Dispose()
}
$rnd = New-Object System.Random 7
# pop: un «plop» que sube
Save-Wav 'pop' 0.09 { param($t, $p) 0.4 * [Math]::Sin(2 * [Math]::PI * (500 * $t + 4000 * $t * $t)) * (1 - $p) * [Math]::Min(1, $t * 400) }
# boing: muelle que sube y tiembla
Save-Wav 'boing' 0.25 { param($t, $p) 0.32 * [Math]::Sin(2 * [Math]::PI * (260 * $t + 700 * $t * $t + 3 * [Math]::Sin(2 * [Math]::PI * 22 * $t))) * [Math]::Pow(1 - $p, 1.5) * [Math]::Min(1, $t * 300) }
# whoosh: aire que crece y se va
Save-Wav 'whoosh' 0.32 { param($t, $p) 0.22 * ($rnd.NextDouble() * 2 - 1) * [Math]::Sin([Math]::PI * $p) * (0.4 + 0.6 * [Math]::Sin(2 * [Math]::PI * 9 * $t)) }
# gulp: dos tragos graves
Save-Wav 'gulp' 0.26 { param($t, $p) $k = if ($t -lt 0.12) { $t / 0.12 } else { ($t - 0.13) / 0.13 }; if ($k -lt 0 -or $k -gt 1) { 0 } else { 0.42 * [Math]::Sin(2 * [Math]::PI * (210 - 90 * $k) * $t) * [Math]::Sin([Math]::PI * $k) } }
# ding: campanilla para llamar la atención
Save-Wav 'ding' 0.45 { param($t, $p) (0.28 * [Math]::Sin(2 * [Math]::PI * 1320 * $t) + 0.12 * [Math]::Sin(2 * [Math]::PI * 1980 * $t)) * [Math]::Exp(-7 * $t) * [Math]::Min(1, $t * 600) }

# icono 128x128: el primer fotograma
$icon = New-Object System.Drawing.Bitmap 128, 128
$ig = [System.Drawing.Graphics]::FromImage($icon); $ig.SmoothingMode = 'AntiAlias'; $ig.InterpolationMode = 'HighQualityBicubic'
$first = [System.Drawing.Image]::FromFile((Join-Path $out 'frames\idle0.png'))
$ig.DrawImage($first, -16, -20, 160, 160); $first.Dispose(); $ig.Dispose()
$icon.Save((Join-Path $out 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png); $icon.Dispose()
'hecho'
