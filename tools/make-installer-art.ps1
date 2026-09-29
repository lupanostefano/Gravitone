# Renders the installer images from art/wizard-large.html and art/icon.svg and writes the BMPs Inno Setup wants,
# one per display scale (100-250%): installer/wizard-large-*.bmp (side image, 164x314 at 100%) and
# installer/wizard-small-*.bmp (top-right image, 55x55 at 100%, with alpha).
param([string]$Chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe')
$ErrorActionPreference = 'Stop'
# Chrome reports on stderr even when it succeeds: native stderr must not count as an error here.
$PSNativeCommandUseErrorActionPreference = $false
$root = Resolve-Path "$PSScriptRoot\.."
$out = Join-Path $root 'installer'
$work = Join-Path ([IO.Path]::GetTempPath()) 'gravitone-installer-art'
New-Item -ItemType Directory -Force $work | Out-Null
Add-Type -AssemblyName System.Drawing

function Shot([string]$Url, [int]$W, [int]$H, [string]$File, [switch]$Transparent) {
    $args = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', "--window-size=$W,$H", '--virtual-time-budget=6000', "--screenshot=$File")
    if ($Transparent) { $args += '--default-background-color=00000000' }
    & $Chrome @args $Url 2>$null | Out-Null
    if (-not (Test-Path $File)) { throw "render failed: $File" }
}

function SaveBmp([string]$Src, [int]$W, [int]$H, [string]$Dest, [switch]$Alpha) {
    $img = [System.Drawing.Image]::FromFile($Src)
    $format = if ($Alpha) { [System.Drawing.Imaging.PixelFormat]::Format32bppArgb } else { [System.Drawing.Imaging.PixelFormat]::Format24bppRgb }
    $bmp = New-Object System.Drawing.Bitmap $W, $H, $format
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($img, 0, 0, $W, $H)
    $g.Dispose(); $img.Dispose()
    $bmp.Save($Dest, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

$fileUrl = { param($p) 'file:///' + ((Join-Path $root $p) -replace '\\', '/') }

$large = Join-Path $work 'wizard-large.png'
Shot (& $fileUrl 'art\wizard-large.html') 410 785 $large
$scales = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 250 = 2.5 }
foreach ($pct in $scales.Keys) {
    $k = $scales[$pct]
    SaveBmp $large ([int][Math]::Round(164 * $k)) ([int][Math]::Round(314 * $k)) (Join-Path $out "wizard-large-$pct.bmp")
}

# Small image: the icon alone, transparent around it (32-bit BMP with alpha).
$iconHtml = Join-Path $work 'icon.html'
Set-Content $iconHtml "<!doctype html><html><head><style>html,body{margin:0;background:transparent}img{display:block;width:512px;height:512px}</style></head><body><img src='$(& $fileUrl 'art\icon.svg')'></body></html>" -Encoding utf8
$small = Join-Path $work 'wizard-small.png'
Shot ('file:///' + ($iconHtml -replace '\\', '/')) 512 512 $small -Transparent
foreach ($pct in $scales.Keys) {
    $n = [int][Math]::Round(55 * $scales[$pct])
    SaveBmp $small $n $n (Join-Path $out "wizard-small-$pct.bmp") -Alpha
}
Copy-Item $large (Join-Path $root 'docs\assets\installer-side.png') -Force
Get-ChildItem $out -Filter 'wizard-*.bmp' | Select-Object Name, Length
