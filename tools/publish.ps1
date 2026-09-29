# Local release build, the same steps as .github/workflows/release.yml: tests, publish, installer, portable zip, checksums.
param([string]$Version = ([xml](Get-Content "$PSScriptRoot\..\Gravitone.csproj")).Project.PropertyGroup.Version)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
Push-Location $root
try {
    dotnet test Gravitone.slnx -c Release
    if (Test-Path publish) { Remove-Item publish -Recurse -Force }
    dotnet publish Gravitone.csproj -c Release -r win-x64 --self-contained false -o publish -p:Version=$Version
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 is needed for the installer (winget install JRSoftware.InnoSetup).' }
    & $iscc /Qp "/DAppVersion=$Version" '/DSourceDir=..\publish' installer\Gravitone.iss
    Compress-Archive -Path publish\* -DestinationPath "dist\Gravitone-$Version-win-x64-portable.zip" -Force
    Get-ChildItem dist -File | Where-Object Extension -in '.exe', '.zip' | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
    } | Set-Content dist\SHA256SUMS.txt
    Get-ChildItem dist | Select-Object Name, @{ n = 'MB'; e = { [Math]::Round($_.Length / 1MB, 1) } }
}
finally {
    Pop-Location
}
