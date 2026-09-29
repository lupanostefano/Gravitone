# Drags with the left button from (FromX,FromY) to (ToX,ToY) in steps and saves a screenshot of the dock strip
# while the button is still down (-Out) and another after release (-OutAfter).
param([int]$FromX, [int]$FromY, [int]$ToX, [int]$ToY, [string]$Out = "drag.png", [string]$OutAfter = "", [int]$Height = 300)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class D {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
}
"@
[D]::SetProcessDPIAware() | Out-Null
$w = [D]::GetSystemMetrics(0); $h = [D]::GetSystemMetrics(1)
function Shot($path) {
  $bmp = New-Object System.Drawing.Bitmap $w, $Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen(0, $h - $Height, 0, 0, $bmp.Size)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}
# glide in so the dock sees real mouse moves
[D]::SetCursorPos($FromX, $FromY - 120) | Out-Null; Start-Sleep -Milliseconds 80
for ($i = 5; $i -ge 0; $i--) { [D]::SetCursorPos($FromX, $FromY - $i * 20) | Out-Null; Start-Sleep -Milliseconds 30 }
Start-Sleep -Milliseconds 300
[D]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 100
$steps = 25
for ($i = 1; $i -le $steps; $i++) {
  [D]::SetCursorPos([int]($FromX + ($ToX - $FromX) * $i / $steps), [int]($FromY + ($ToY - $FromY) * $i / $steps)) | Out-Null
  Start-Sleep -Milliseconds 25
}
Start-Sleep -Milliseconds 500
Shot $Out
[D]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
if ($OutAfter) { Shot $OutAfter; Start-Sleep -Milliseconds 700; Shot ($OutAfter -replace '\.png$', '2.png') }
"screen=${w}x${h}"
