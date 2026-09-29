# Installs a Gravitone setup like a user would (silently, for the current user), checks the installed copy, then
# uninstalls it and checks that nothing is left but the user's settings. Used by CI and by the release workflow.
# Do not run it on a PC where you use Gravitone: it replaces and then removes the installed copy.
param([Parameter(Mandatory)][string]$Setup)
$ErrorActionPreference = 'Stop'
$app = Join-Path $env:LOCALAPPDATA 'Programs\Gravitone'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$log = Join-Path ([IO.Path]::GetTempPath()) 'gravitone-install.log'

$p = Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/TASKS="autostart"', "/LOG=`"$log`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content $log -Tail 30; throw "setup failed ($($p.ExitCode))" }
foreach ($f in 'Gravitone.exe', 'GravitoneGuard.exe', 'unins000.exe') { if (-not (Test-Path "$app\$f")) { throw "missing $f" } }
if ((Get-ItemProperty $run).Gravitone -notlike "*$app\Gravitone.exe*") { throw 'autostart entry missing or wrong' }
$key = Get-ChildItem HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall | Get-ItemProperty | Where-Object DisplayName -eq 'Gravitone'
if (-not $key) { throw 'no uninstall entry' }
"installed $($key.DisplayVersion) in $app"

$st = Start-Process "$app\Gravitone.exe" -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow
if ($st.ExitCode -ne 0) { throw "installed self-test failed ($($st.ExitCode))" }

$u = Start-Process "$app\unins000.exe" -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
if ($u.ExitCode -ne 0) { throw "uninstall failed ($($u.ExitCode))" }
Start-Sleep -Seconds 3
if (Test-Path "$app\Gravitone.exe") { throw 'files left after uninstall' }
if ((Get-ItemProperty $run).Gravitone) { throw 'autostart entry left after uninstall' }
if (Get-Process Gravitone, GravitoneGuard -ErrorAction SilentlyContinue) { throw 'process left running after uninstall' }
'install / self-test / uninstall: ok'
