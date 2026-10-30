param([string]$Title, [string]$Class, [int]$X = -9999, [int]$Y, [int]$W, [int]$H, [switch]$Minimize, [switch]$Restore, [switch]$Front, [switch]$Close, [switch]$Bottom)
# Finds a visible top-level window by title (contains) and/or class and moves, minimizes, restores, raises or closes it.
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class WC {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  public static IntPtr Find(string title, string cls) { IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { if (!IsWindowVisible(h)) return true; var s = new StringBuilder(256); GetWindowText(h, s, 256); var c = new StringBuilder(256); GetClassName(h, c, 256);
      if ((string.IsNullOrEmpty(title) || s.ToString().Contains(title)) && (string.IsNullOrEmpty(cls) || c.ToString() == cls)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; } }
'@
$h = [WC]::Find($Title, $Class)
if ($h -eq [IntPtr]::Zero) { 'not found'; exit 1 }
if ($Close) { [WC]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; 'closed'; exit 0 }
if ($Minimize) { [WC]::ShowWindow($h, 6) | Out-Null }
if ($Restore) { [WC]::ShowWindow($h, 9) | Out-Null }
if ($X -ne -9999) { [WC]::SetWindowPos($h, [IntPtr]::Zero, $X, $Y, $W, $H, 0x0044) | Out-Null }
if ($Bottom) { [WC]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x0013) | Out-Null }
if ($Front) { [WC]::keybd_event(0x87, 0, 0, [UIntPtr]::Zero); [WC]::keybd_event(0x87, 0, 2, [UIntPtr]::Zero); [WC]::SetForegroundWindow($h) | Out-Null }
"ok $h"
