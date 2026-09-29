# Mouse helpers for captures (physical pixels).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class In {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
}
"@
[In]::SetProcessDPIAware() | Out-Null
function Move-Mouse([int]$X, [int]$Y) { [In]::SetCursorPos($X, $Y) | Out-Null }
function Glide([int]$X0, [int]$Y0, [int]$X1, [int]$Y1, [int]$Steps = 20, [int]$Ms = 12) {
    for ($i = 1; $i -le $Steps; $i++) {
        $k = $i / $Steps; $k = $k * $k * (3 - 2 * $k)
        [In]::SetCursorPos([int]($X0 + ($X1 - $X0) * $k), [int]($Y0 + ($Y1 - $Y0) * $k)) | Out-Null
        Start-Sleep -Milliseconds $Ms
    }
}
function Click-Mouse { [In]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [In]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
function Right-Click { [In]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [In]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero) }
