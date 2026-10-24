param([int[]]$Pids)
# Lists the visible windows of those processes: pid, handle, title, position and size.
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class WL {
  delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  struct R { public int L,T,Ri,B; }
  public static List<string> List(uint[] pids) {
    var o = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (Array.IndexOf(pids, p) >= 0 && IsWindowVisible(h)) { var s = new StringBuilder(256); GetWindowText(h, s, 256); var c = new StringBuilder(256); GetClassName(h, c, 256); R r; GetWindowRect(h, out r);
        o.Add(p + " " + h + " '" + s + "' [" + c + "] " + r.L + "," + r.T + " " + (r.Ri - r.L) + "x" + (r.B - r.T)); }
      return true; }, IntPtr.Zero);
    return o; } }
'@
if (-not $Pids) { $Pids = (Get-Process KDesktop | Where-Object { $_.Path }).Id }
[WL]::List([uint32[]]$Pids)
