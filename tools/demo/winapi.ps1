# Small window helpers for captures: find a window by title, minimize, restore, ask whether it is minimized.
if (-not ('WA' -as [type])) {
    Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WA {
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cx, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
}
function Move-Window([IntPtr]$H, [int]$X, [int]$Y, [int]$W, [int]$Ht) { [void][WA]::SetWindowPos($H, [IntPtr]::Zero, $X, $Y, $W, $Ht, 0x44) }
function Focus-Window([IntPtr]$H) { [void][WA]::SetForegroundWindow($H) }
function Find-Window([string]$Title) { [WA]::FindWindow([NullString]::Value, $Title) }
function Minimize-Window([IntPtr]$H) { [void][WA]::ShowWindowAsync($H, 6) }
function Restore-Window([IntPtr]$H) { [void][WA]::ShowWindowAsync($H, 9) }
function Test-Minimized([IntPtr]$H) { [WA]::IsIconic($H) }
