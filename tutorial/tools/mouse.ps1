param([int]$X, [int]$Y, [switch]$Click, [switch]$Right, [switch]$Down, [switch]$Up, [int]$Key = 0, [int]$Hold = 0)
# Moves the real cursor (virtual coordinates); -Click/-Right clicks there; -Down/-Up presses or releases the left button;
# -Key sends a virtual key (held -Hold ms).
Add-Type -Namespace M -Name I -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
'@
[M.I]::SetProcessDPIAware() | Out-Null
if ($PSBoundParameters.ContainsKey('X')) { [M.I]::SetCursorPos($X, $Y) | Out-Null }
if ($Click) { [M.I]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M.I]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
if ($Right) { [M.I]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M.I]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero) }
if ($Down) { [M.I]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero) }
if ($Up) { [M.I]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
if ($Key) { [M.I]::keybd_event([byte]$Key, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds $Hold; [M.I]::keybd_event([byte]$Key, 0, 2, [UIntPtr]::Zero) }
'ok'
