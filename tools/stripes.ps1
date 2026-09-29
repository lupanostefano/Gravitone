Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type 'using System.Runtime.InteropServices; public static class D { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[D]::SetProcessDPIAware() | Out-Null
$f = New-Object System.Windows.Forms.Form
$f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'; $f.ShowInTaskbar = $false
$f.Bounds = New-Object System.Drawing.Rectangle 600, 880, 720, 160
$f.Add_Paint({ param($s, $e)
  $cols = 'Red','Orange','Gold','LimeGreen','DeepSkyBlue','MediumBlue','DarkViolet','White','Black'
  for ($i = 0; $i -lt 36; $i++) { $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromName($cols[$i % $cols.Count])); $e.Graphics.FillRectangle($b, $i * 20, 0, 20, 160) }
})
$t = New-Object System.Windows.Forms.Timer; $t.Interval = 6000; $t.Add_Tick({ $f.Close() }); $t.Start()
[System.Windows.Forms.Application]::Run($f)
