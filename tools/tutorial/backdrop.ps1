# A borderless window with the user's wallpaper over the secondary monitor's work area: a clean desktop for the
# screenshots (the taskbar stays real). It does not cover the whole monitor, so the assistants do not take it for a
# full-screen app. A click on it brings it to the front: send it back with winctl.ps1 -Title Desktop -Bottom.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace B -Name D -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[B.D]::SetProcessDPIAware() | Out-Null
$screen = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1
$wallpaper = (Get-ItemProperty 'HKCU:\Control Panel\Desktop').WallPaper
$f = New-Object System.Windows.Forms.Form
$f.Text = 'Desktop'
$f.FormBorderStyle = 'None'
$f.ShowInTaskbar = $false
$f.StartPosition = 'Manual'
$f.Bounds = $screen.WorkingArea
$f.BackColor = [System.Drawing.Color]::Black
if ($wallpaper -and (Test-Path $wallpaper)) {
  # Fill the whole monitor like the real wallpaper, then keep the work-area part.
  $img = [System.Drawing.Image]::FromFile($wallpaper)
  $full = New-Object System.Drawing.Bitmap $screen.Bounds.Width, $screen.Bounds.Height
  $g = [System.Drawing.Graphics]::FromImage($full)
  $g.InterpolationMode = 'HighQualityBicubic'
  $scale = [Math]::Max($screen.Bounds.Width / $img.Width, $screen.Bounds.Height / $img.Height)
  $w = $img.Width * $scale; $h = $img.Height * $scale
  $g.DrawImage($img, ($screen.Bounds.Width - $w) / 2, ($screen.Bounds.Height - $h) / 2, $w, $h)
  $g.Dispose()
  $f.BackgroundImage = $full
  $f.BackgroundImageLayout = 'None'
}
$f.Add_Shown({ $f.SendToBack() })
[System.Windows.Forms.Application]::Run($f)
