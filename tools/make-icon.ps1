# Renders the Gravitone icon from art/icon.svg (large sizes) and art/icon-small.svg (16-32 px, simpler and bolder)
# with headless Chrome, then writes Gravitone.ico (PNG-compressed entries) and the PNGs used by the docs and the installer.
param([string]$Chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe')
$ErrorActionPreference = 'Stop'
# Chrome reports on stderr even when it succeeds: native stderr must not count as an error here.
$PSNativeCommandUseErrorActionPreference = $false
$root = Resolve-Path "$PSScriptRoot\.."
$work = Join-Path ([IO.Path]::GetTempPath()) 'gravitone-icon'
New-Item -ItemType Directory -Force $work | Out-Null
Add-Type -AssemblyName System.Drawing

function Render([string]$Svg, [int]$Size, [string]$Out) {
    $html = Join-Path $work "render-$Size.html"
    $svgUrl = 'file:///' + ((Join-Path $root $Svg) -replace '\\', '/')
    Set-Content $html "<!doctype html><html><head><style>html,body{margin:0;background:transparent}img{display:block;width:${Size}px;height:${Size}px}</style></head><body><img src='$svgUrl'></body></html>" -Encoding utf8
    & $Chrome --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --default-background-color=00000000 `
        "--window-size=$Size,$Size" --virtual-time-budget=3000 "--screenshot=$Out" ("file:///" + ($html -replace '\\', '/')) 2>$null | Out-Null
    if (-not (Test-Path $Out)) { throw "render failed: $Out" }
}

# 512 px master (a larger headless window crops the page), then every size downscaled from it (sharper than letting the browser render tiny SVGs).
$master = Join-Path $work 'master-512.png'
$masterSmall = Join-Path $work 'master-small-512.png'
Render 'art\icon.svg' 512 $master
Render 'art\icon-small.svg' 512 $masterSmall

function Scale([string]$Src, [int]$Size) {
    $img = [System.Drawing.Image]::FromFile($Src)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($img, 0, 0, $Size, $Size)
    $g.Dispose(); $img.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = foreach ($n in $sizes) {
    $bmp = Scale ($(if ($n -le 32) { $masterSmall } else { $master })) $n
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

# ICO: header, one directory entry per image, then the PNG data.
$ico = Join-Path $root 'Gravitone.ico'
$fs = [System.IO.File]::Create($ico)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]
    $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n }))); $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# PNGs for the docs, the site and the installer.
$assets = Join-Path $root 'docs\assets'
Copy-Item $master (Join-Path $assets 'icon-512.png') -Force
foreach ($n in 256, 128) { $b = Scale $master $n; $b.Save((Join-Path $assets "icon-$n.png"), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose() }
$b = Scale $masterSmall 64; $b.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
Copy-Item $ico (Join-Path $assets 'Gravitone.ico') -Force
"{0}: {1:N0} KB" -f $ico, ((Get-Item $ico).Length / 1KB)
