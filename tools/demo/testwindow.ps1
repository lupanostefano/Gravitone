# A plain colourful window for testing dock effects (title "GenieTest").
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = "GenieTest"
$f.StartPosition = "Manual"
$f.Location = New-Object System.Drawing.Point(620, 140)
$f.Size = New-Object System.Drawing.Size(620, 460)
$f.BackColor = [System.Drawing.Color]::FromArgb(36, 84, 200)
$l = New-Object System.Windows.Forms.Label
$l.Text = "GENIE"
$l.Font = New-Object System.Drawing.Font("Segoe UI", 66, [System.Drawing.FontStyle]::Bold)
$l.ForeColor = [System.Drawing.Color]::White
$l.AutoSize = $true
$l.Location = New-Object System.Drawing.Point(60, 110)
$f.Controls.Add($l)
[System.Windows.Forms.Application]::Run($f)
