# Prints the foreground window's title and whether a process's main window is minimized (test helper).
param([string]$Process = "mspaint")
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class FG {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
"@
$fg = [FG]::GetForegroundWindow()
$p = Get-Process $Process -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
"foreground: " + [FG]::Title($fg)
if ($p) { "$Process minimized: " + [FG]::IsIconic($p.MainWindowHandle) }
