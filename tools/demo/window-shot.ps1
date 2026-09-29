# Saves a PNG of one window's own pixels (PrintWindow, so nothing behind it is captured).
param([Parameter(Mandatory)][string]$Title, [Parameter(Mandatory)][string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class WS {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s);
}
"@
[WS]::SetProcessDPIAware() | Out-Null
$h = [WS]::FindWindow([NullString]::Value, $Title)
if ($h -eq [IntPtr]::Zero) { throw "window '$Title' not found" }
$r = New-Object WS+R; [WS]::GetWindowRect($h, [ref]$r) | Out-Null
$f = New-Object WS+R; [void][WS]::DwmGetWindowAttribute($h, 9, [ref]$f, 16)
$bmp = New-Object System.Drawing.Bitmap ($r.Ri - $r.L), ($r.B - $r.T), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[void][WS]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
$crop = New-Object System.Drawing.Rectangle ([Math]::Max(0, $f.L - $r.L)), ([Math]::Max(0, $f.T - $r.T)), ($f.Ri - $f.L), ($f.B - $f.T)
$bmp.Clone($crop, $bmp.PixelFormat).Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
