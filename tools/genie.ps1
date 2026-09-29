# Asks a dock started with --diag to minimize (-Restore: restore) a window through its genie path, as a
# click on its icon would, without touching the mouse.
param([Parameter(Mandatory)][IntPtr]$Window, [switch]$Restore)
if (-not ('GT' -as [type])) {
    Add-Type @"
using System; using System.Runtime.InteropServices;
public static class GT {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@
}
$dock = [GT]::FindWindow([NullString]::Value, "Gravitone")
[void][GT]::PostMessage($dock, [GT]::RegisterWindowMessage("Gravitone.GenieTest"), [IntPtr]([int][bool]$Restore), $Window)
