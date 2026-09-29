# Stacks the topmost windows for captures, bottom to top: stage, demo windows (titles given), dock plate, dock, menu bar.
param([string[]]$Titles = @())
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class RZ {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cx, uint f);
  public static List<IntPtr> ByTitle(string title, bool startsWith) {
    var l = new List<IntPtr>();
    EnumWindows((h, x) => { var t = new StringBuilder(256); GetWindowText(h, t, 256); var s = t.ToString();
      if ((startsWith ? s.StartsWith(title) : s == title) && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero);
    return l;
  }
  public static void Top(IntPtr h) { SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
}
"@
foreach ($t in $Titles) { foreach ($h in [RZ]::ByTitle($t, $true)) { [RZ]::Top($h) } }
foreach ($t in 'Gravitone Backdrop', 'Gravitone', 'Gravitone Menu Bar Glass', 'Gravitone Menu Bar') { foreach ($h in [RZ]::ByTitle($t, $false)) { [RZ]::Top($h) } }
