param([int]$Passes = 6)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[M]::SetProcessDPIAware() | Out-Null
$p = Get-Process Gravitone
$c0 = $p.TotalProcessorTime.TotalMilliseconds; $t0 = Get-Date
for ($k = 0; $k -lt $Passes; $k++) {
  for ($x = 600; $x -le 1320; $x += 6) { [M]::SetCursorPos($x, 1045) | Out-Null; Start-Sleep -Milliseconds 10 }
  for ($x = 1320; $x -ge 600; $x -= 6) { [M]::SetCursorPos($x, 1045) | Out-Null; Start-Sleep -Milliseconds 10 }
}
[M]::SetCursorPos(960, 500) | Out-Null
$p.Refresh(); $secs = ((Get-Date) - $t0).TotalSeconds
"animazione: {0:N1}% di un core per {1:N1} s" -f (($p.TotalProcessorTime.TotalMilliseconds - $c0) / ($secs * 10)), $secs
