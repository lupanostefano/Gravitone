param([string]$Out = "shot.png", [int]$Height = 260, [int]$MoveX = -1, [int]$MoveY = -1, [int]$WaitMs = 600, [switch]$Click, [switch]$RightClick, [switch]$Direct, [switch]$Top)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
}
"@
[W]::SetProcessDPIAware() | Out-Null
$w = [W]::GetSystemMetrics(0); $h = [W]::GetSystemMetrics(1)
if ($MoveX -ge 0) {
  if ($Direct) { [W]::SetCursorPos($MoveX, $MoveY) | Out-Null; Start-Sleep -Milliseconds 60 }
  else {
    # glide in so the dock sees real mouse-move events
    [W]::SetCursorPos($MoveX, $MoveY - 150) | Out-Null; Start-Sleep -Milliseconds 80
    for ($i = 5; $i -ge 0; $i--) { [W]::SetCursorPos($MoveX, $MoveY - $i * 5) | Out-Null; Start-Sleep -Milliseconds 30 }
  }
  if ($Click) { [W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
  if ($RightClick) { [W]::mouse_event(8,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(0x10,0,0,0,[UIntPtr]::Zero) }
}
Start-Sleep -Milliseconds $WaitMs
$bmp = New-Object System.Drawing.Bitmap $w, $Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
# -Top captures the menu bar strip instead of the dock strip
$y = if ($Top) { 0 } else { $h - $Height }
$g.CopyFromScreen(0, $y, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"screen=${w}x${h}"
