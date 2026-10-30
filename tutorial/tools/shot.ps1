param([int]$ProcessId, [string]$Out)
# PrintWindow de la ventana «K-Desktop» más grande de ese proceso (sin capturar la pantalla).
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class Shot
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    public struct RECT { public int L, T, R, B; }
    public static IntPtr Find(uint pid)
    {
        IntPtr best = IntPtr.Zero; long area = 0;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var s = new System.Text.StringBuilder(256); GetWindowText(h, s, 256);
            if (s.ToString() != "K-Desktop") return true;
            RECT r; GetWindowRect(h, out r); long a = (long)(r.R - r.L) * (r.B - r.T);
            if (a > area) { area = a; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }
}
'@
$h = [Shot]::Find([uint32]$ProcessId)
if ($h -eq [IntPtr]::Zero) { Write-Output 'sin ventana'; exit 1 }
$r = New-Object Shot+RECT
[Shot]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[Shot]::PrintWindow($h, $dc, 2) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "$Out $($bmp.Width)x$($bmp.Height)"
