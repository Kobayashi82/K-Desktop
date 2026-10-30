param([int]$X, [int]$Y, [int]$W, [int]$H, [string]$Out, [string]$Base, [int]$BaseX, [int]$BaseY)
# Copies a rectangle of the screen (virtual coordinates) into $Out. With -Base, pastes it onto that picture instead,
# at the rectangle's place relative to (BaseX, BaseY): a popup over a PrintWindow capture of its window.
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace G -Name D -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[G.D]::SetProcessDPIAware() | Out-Null
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, $bmp.Size, [System.Drawing.CopyPixelOperation]::SourceCopy)
$g.Dispose()
if ($Base) {
  $b = [System.Drawing.Image]::FromFile($Base)
  $canvas = New-Object System.Drawing.Bitmap $b
  $b.Dispose()
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  $g.DrawImage($bmp, $X - $BaseX, $Y - $BaseY, $W, $H)
  $g.Dispose()
  $canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $canvas.Dispose()
} else { $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png) }
$bmp.Dispose()
"$Out"
