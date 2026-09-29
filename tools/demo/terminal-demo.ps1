# Prints something pleasant in a Windows Terminal window for captures.
$e = [char]27
function global:prompt { "$([char]27)[38;2;255;143;196m❯$([char]27)[0m " }
Clear-Host
$lines = @(
  "",
  "  $e[38;2;255;143;196m●$e[0m $e[38;2;123;108;255m●$e[0m $e[38;2;60;200;240m●$e[0m   $e[1mgravitone$e[0m  $e[2m0.2.0$e[0m",
  "",
  "  $e[2m$('─' * 46)$e[0m",
  "  $e[38;2;60;200;240mdock$e[0m         bottom · 48 px · wave magnification",
  "  $e[38;2;60;200;240mgenie$e[0m        Direct3D 11 · 1 frame per refresh",
  "  $e[38;2;60;200;240mtaskbar$e[0m      hidden, restored on exit ✓",
  "  $e[38;2;60;200;240mguard$e[0m        GravitoneGuard.exe watching ✓",
  "  $e[38;2;60;200;240mmenu bar$e[0m     1 display",
  "  $e[2m$('─' * 46)$e[0m",
  "",
  "  $e[38;2;255;143;196m$e[1mready.$e[0m"
)
$lines | ForEach-Object { Write-Host $_ }
