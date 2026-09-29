<#
.SYNOPSIS
  Builds the Microsoft Store package (MSIX) of Gravitone: self-contained publish, assets, manifest, makeappx.

.DESCRIPTION
  The Store package cannot depend on the .NET Desktop Runtime being installed, so the app is published
  self-contained. Windows SDK (makeappx, signtool) is needed; it is on GitHub's windows-latest runners.

  For the Store upload the package does not need to be signed: the Store signs it. Pass -Sign to make a
  package signed with a throw-away certificate, to try it on a PC (see the message printed at the end).

.EXAMPLE
  # Store package, with the identity Partner Center shows under "Product identity":
  ./tools/build-msix.ps1 -Version 0.2.2 -IdentityName 12345Publisher.Gravitone -Publisher 'CN=AAAAAAAA-...' -PublisherDisplayName 'Stefano Lupano'

.EXAMPLE
  # A test package for sideloading (test identity, signed with a self-made certificate):
  ./tools/build-msix.ps1 -Sign
#>
param(
    [string]$Version = '0.2.1',
    [string]$IdentityName = 'Gravitone.Test',
    [string]$Publisher = 'CN=Gravitone Test',
    [string]$PublisherDisplayName = 'Stefano Lupano',
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$work = Join-Path $root 'build\msix'
$layout = Join-Path $work 'layout'
$dist = Join-Path $root 'dist'

# MSIX versions have four numbers, and the Store wants the last one to be 0: "0.3.0-beta" becomes 0.3.0.0.
$numeric = ($Version -split '-')[0]
$parts = @($numeric -split '\.')
while ($parts.Count -lt 3) { $parts += '0' }
$msixVersion = ($parts[0..2] -join '.') + '.0'

function Find-SdkTool([string]$name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found: install the Windows 10/11 SDK." }
    $tool.FullName
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $layout, $dist -ItemType Directory -Force | Out-Null

Write-Host "Publishing (self-contained)..."
dotnet publish "$root\Gravitone.csproj" -c Release -r win-x64 --self-contained true -o $layout "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Write-Host "Assets..."
Add-Type -AssemblyName System.Drawing
$assets = New-Item (Join-Path $layout 'Assets') -ItemType Directory -Force
$icon = [System.Drawing.Icon]::new((Join-Path $root 'Gravitone.ico'), 256, 256).ToBitmap()
function New-Asset([string]$name, [int]$width, [int]$height) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'HighQuality'
    $side = [int]([Math]::Min($width, $height) * 0.8)
    $g.DrawImage($icon, [int](($width - $side) / 2), [int](($height - $side) / 2), $side, $side)
    $g.Dispose()
    $bitmap.Save((Join-Path $assets "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}
New-Asset 'StoreLogo' 50 50
New-Asset 'Square44x44Logo' 44 44
New-Asset 'Square150x150Logo' 150 150
New-Asset 'Wide310x150Logo' 310 150
$icon.Dispose()

Write-Host "Manifest..."
$manifest = Get-Content (Join-Path $root 'packaging\msix\AppxManifest.xml') -Raw
$manifest = $manifest.Replace('@IDENTITY_NAME@', $IdentityName).Replace('@PUBLISHER@', $Publisher).
    Replace('@PUBLISHER_DISPLAY_NAME@', $PublisherDisplayName).Replace('@VERSION@', $msixVersion)
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding UTF8

$msix = Join-Path $dist "Gravitone-$msixVersion-x64.msix"
Write-Host "Packing $msix..."
& (Find-SdkTool 'makeappx.exe') pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }

if ($Sign) {
    Write-Host "Signing with a test certificate..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
        -FriendlyName 'Gravitone test package' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $cer = Join-Path $dist 'Gravitone-test.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    & (Find-SdkTool 'signtool.exe') sign /fd SHA256 /sha1 $cert.Thumbprint $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
    Write-Host @"

To try it on a PC (PowerShell as administrator, once):
  Import-Certificate -FilePath Gravitone-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Then, as the user:
  Add-AppxPackage .\$([IO.Path]::GetFileName($msix))
Remove it with:
  Get-AppxPackage $IdentityName | Remove-AppxPackage
"@
}

Write-Host "Done: $msix"
