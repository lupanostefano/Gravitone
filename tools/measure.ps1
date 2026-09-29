param([int]$Seconds = 60, [string]$Name = 'Gravitone')
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class GR { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr h, uint f); }
"@
function Snap($p) {
  $p.Refresh()
  [pscustomobject]@{
    CPU = $p.TotalProcessorTime.TotalMilliseconds
    WS = [math]::Round($p.WorkingSet64 / 1MB, 1)
    Private = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
    Handles = $p.HandleCount
    Threads = $p.Threads.Count
    GDI = [GR]::GetGuiResources($p.Handle, 0)
    USER = [GR]::GetGuiResources($p.Handle, 1)
  }
}
$p = Get-Process $Name | Select-Object -First 1
$a = Snap $p
Start-Sleep -Seconds $Seconds
$b = Snap $p
"Prima: $($a | Out-String)"
"Dopo:  $($b | Out-String)"
"CPU media: {0:N3}% di un core ({1:N0} ms in {2} s)" -f (($b.CPU - $a.CPU) / ($Seconds * 10)), ($b.CPU - $a.CPU), $Seconds
