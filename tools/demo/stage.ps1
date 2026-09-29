# Puts a clean demo wallpaper on screen (a plain window behind everything, the real wallpaper is left alone)
# for README / site captures. Stop it by closing its process (its PID is printed).
# With -Topmost the stage stays above every other window (use raise.ps1 afterwards to bring the dock, the menu bar
# and the demo windows above it, in that order).
param([string]$Image = "$PSScriptRoot\..\..\docs\assets\demo-wallpaper.png", [int]$Trim = 2, [int]$Seconds = 240, [switch]$Topmost)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ST {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
}
"@
[ST]::SetProcessDPIAware() | Out-Null
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.ShowInTaskbar = $false
$form.TopMost = [bool]$Topmost
# A window covering the whole monitor counts as a full-screen app and hides the dock: stay a few pixels short.
$form.Bounds = New-Object System.Drawing.Rectangle $screen.X, $screen.Y, $screen.Width, ($screen.Height - $Trim)
$form.BackgroundImage = [System.Drawing.Image]::FromFile((Resolve-Path $Image))
$form.BackgroundImageLayout = 'Stretch'
$form.Text = 'Gravitone demo stage'
# A tool window: the dock does not list it as a running app.
$form.Add_Shown({
    $ex = [ST]::GetWindowLongPtr($form.Handle, -20).ToInt64()
    [ST]::SetWindowLongPtr($form.Handle, -20, [IntPtr]($ex -bor 0x80)) | Out-Null
})
# Safety net: the stage always closes by itself, so a forgotten one can never cover the screen for long.
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = [Math]::Max(10, $Seconds) * 1000
$timer.Add_Tick({ $form.Close() })
$timer.Start()
$form.Add_KeyDown({ if ($_.KeyCode -eq 'Escape') { $form.Close() } })
"PID $PID (closes in $Seconds s, or with Esc)"
[System.Windows.Forms.Application]::Run($form)

