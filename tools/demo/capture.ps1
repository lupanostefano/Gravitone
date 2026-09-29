# Captures the README / site media on the demo stage (see stage.ps1, raise.ps1): stills, and clips as MP4 (60 fps)
# plus GIF. The stage, the demo windows and the dock must be up. Moves the mouse: do not touch the PC meanwhile.
param([string]$Out = "$PSScriptRoot\..\..\docs\assets", [string]$Work = $env:TEMP)
. "$PSScriptRoot\input.ps1"
. "$PSScriptRoot\winapi.ps1"
$ErrorActionPreference = 'Stop'
$Out = (Resolve-Path $Out).Path

function Grab([string]$File, [switch]$Mouse) {
    ffmpeg -y -loglevel error -f gdigrab -framerate 1 -draw_mouse ([int][bool]$Mouse) -i desktop -frames:v 1 $File
}

# Records the whole screen (gdigrab, up to 60 fps) while $Action runs, then crops it.
function Clip([string]$Name, [double]$Seconds, [string]$Crop, [scriptblock]$Action) {
    $raw = Join-Path $Work "$Name-raw.mkv"
    if (Test-Path $raw) { Remove-Item $raw }
    $p = Start-Process ffmpeg -PassThru -WindowStyle Hidden -ArgumentList @('-y', '-f', 'gdigrab', '-framerate', '60', '-draw_mouse', '1', '-i', 'desktop',
        '-t', "$Seconds", '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '12', '-pix_fmt', 'yuv420p', $raw)
    Start-Sleep -Milliseconds 900
    & $Action
    [void]$p.WaitForExit(([int]($Seconds * 1000) + 15000))
    $script:lastRaw = $raw
}

function Mp4AndGif([string]$Raw, [string]$Name, [double]$Start, [double]$Length, [string]$Crop, [int]$GifWidth, [int]$GifFps) {
    ffmpeg -y -loglevel error -ss $Start -i $Raw -t $Length -vf "crop=$Crop,scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v libx264 -crf 20 -preset slow -pix_fmt yuv420p -movflags +faststart -an "$Out\$Name.mp4"
    ffmpeg -y -loglevel error -ss $Start -i $Raw -t $Length -vf "fps=$GifFps,crop=$Crop,scale=${GifWidth}:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" "$Out\$Name.gif"
}

$chrome = Find-Window "Gravitone: a real Dock for Windows 11"

# 1. Hero still: Chrome in front (the menu bar names it), the pointer resting on the dock.
Move-Mouse 700 135; Click-Mouse; Start-Sleep -Milliseconds 400
Move-Mouse 960 800; Glide 960 800 830 1050 14 14; Start-Sleep -Milliseconds 900
Grab "$Out\desktop.png"
Grab "$Work\full-menubar.png"

# 2. Magnification sweep.
Clip 'magnify' 6.2 '' {
    Glide 470 900 500 1052 10 12
    Glide 500 1052 1420 1052 90 18
    Glide 1420 1052 500 1052 90 18
    Glide 500 1052 500 700 10 12
}
Mp4AndGif $script:lastRaw 'magnify' 0.9 4.9 '1160:200:380:880' 900 24

# 3. Genie: minimize the Chrome window into its dock icon, and back.
Move-Mouse 700 135; Click-Mouse; Start-Sleep -Milliseconds 500
Move-Mouse 1300 900
Clip 'genie' 6.4 '' {
    Glide 1300 900 1240 1052 14 14
    Start-Sleep -Milliseconds 700
    Click-Mouse
    Start-Sleep -Milliseconds 1700
    Click-Mouse
    Start-Sleep -Milliseconds 1700
    Glide 1240 1052 1300 760 12 14
}
Mp4AndGif $script:lastRaw 'genie' 0.8 5.4 '1400:980:240:100' 900 30

# 4. Settings: right-click the dock's own padding (left of the first icon), first item of the menu.
Move-Mouse 400 1050; Start-Sleep -Milliseconds 300; Glide 400 1050 541 1050 8 14; Start-Sleep -Milliseconds 400
Right-Click; Start-Sleep -Milliseconds 800
Grab "$Work\dockmenu-full.png" -Mouse
$k = Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);' -Name K -Namespace W -PassThru
$k::keybd_event(0x28, 0, 0, [UIntPtr]::Zero); $k::keybd_event(0x28, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200
$k::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero); $k::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1600
Grab "$Work\settings-full.png"
"captured"