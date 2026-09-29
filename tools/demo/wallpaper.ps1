# Draws the abstract wallpaper used in the README and site captures (original artwork, 3840x2160 PNG).
param([string]$Out = "$PSScriptRoot\..\..\docs\assets\demo-wallpaper.png")
Add-Type -AssemblyName System.Drawing
$w = 480; $h = 270   # painted small, then scaled up: smooth gradients without banding
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::FromArgb(255, 12, 14, 34))

function Blob($cx, $cy, $rx, $ry, $r, $gr, $b, $a) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $rx, $cy - $ry, $rx * 2, $ry * 2)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $brush.CenterColor = [System.Drawing.Color]::FromArgb($a, $r, $gr, $b)
    $brush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $r, $gr, $b))
    $g.FillPath($brush, $path)
}
Blob 90  40  260 200  86  52 255 235    # violet, top left
Blob 400 60  240 170  255 84 160 210    # magenta, top right
Blob 250 250 300 150  20 170 230 200    # cyan, bottom
Blob 40  230 190 130  40 90 255 190     # blue, bottom left
Blob 440 240 170 120  255 150 70 150    # amber glow, bottom right
Blob 250 120 130 90   180 120 255 90    # soft lilac centre
$g.Dispose()

$big = New-Object System.Drawing.Bitmap 3840, 2160
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = 'HighQualityBicubic'
$g2.PixelOffsetMode = 'Half'
$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # no pale edge from the upscale
$g2.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, 3840, 2160), 0, 0, $w, $h, [System.Drawing.GraphicsUnit]::Pixel, $attr)
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$big.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"$Out ($((Get-Item $Out).Length / 1MB) MB)"



