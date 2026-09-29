# Records a region of the screen with ffmpeg while a script block runs, then turns it into frames or a GIF.
# Usage: . .\record.ps1; Record -Out clip.mp4 -X 0 -Y 700 -W 1920 -H 380 -Seconds 3 -Action { ... }
function Record([string]$Out, [int]$X, [int]$Y, [int]$W, [int]$H, [double]$Seconds, [scriptblock]$Action, [int]$Fps = 30) {
    if (Test-Path $Out) { Remove-Item $Out -Force }
    $args = @('-y', '-f', 'gdigrab', '-framerate', "$Fps", '-draw_mouse', '1', '-offset_x', "$X", '-offset_y', "$Y",
        '-video_size', "${W}x${H}", '-i', 'desktop', '-t', "$Seconds", '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '14', '-pix_fmt', 'yuv420p', $Out)
    $p = Start-Process ffmpeg -ArgumentList $args -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 700   # ffmpeg needs a moment to start grabbing
    & $Action
    $null = $p.WaitForExit(([int]($Seconds * 1000) + 15000))
}
