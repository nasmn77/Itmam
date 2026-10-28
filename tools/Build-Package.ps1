<#
.SYNOPSIS
  Builds Itmam's MSIX (x64 + ARM64) with Visual Studio's MSBuild.

.DESCRIPTION
  -Mode Store (default): artifacts\msix\*.msixupload for Partner Center (unsigned: the Store signs it).
  -Mode Test: a .msixbundle signed with the local test certificate (tools/New-DevCertificate.ps1),
              ready to install on this PC by double-clicking it or with Add-AppxPackage.

  Requires Visual Studio with "MSIX Packaging Tools" and a Windows 11 SDK (see src/Itmam.Package/README.md).

.EXAMPLE
  ./tools/Build-Package.ps1 -Mode Test
#>
param(
    [ValidateSet('Store', 'Test')] [string] $Mode = 'Store'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts\msix'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -requires Microsoft.VisualStudio.ComponentGroup.MSIX.Packaging `
    -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio with "MSIX Packaging Tools" was not found.' }

Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
& $msbuild (Join-Path $root 'src\Itmam.Package\Itmam.Package.wapproj') /restore /m /nologo /v:m `
    /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Always '/p:AppxBundlePlatforms=x64|arm64' `
    "/p:UapAppxPackageBuildMode=$(if ($Mode -eq 'Store') { 'StoreUpload' } else { 'SideloadOnly' })" `
    "/p:AppxPackageDir=$output\"
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed ($LASTEXITCODE)." }

if ($Mode -eq 'Test') {
    $bundle = Get-ChildItem $output -Recurse -Filter *.msixbundle | Select-Object -First 1
    $thumbprint = & (Join-Path $PSScriptRoot 'New-DevCertificate.ps1') | Select-Object -Last 1
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
        Sort-Object FullName | Select-Object -Last 1
    & $signtool.FullName sign /fd SHA256 /sha1 $thumbprint /s My $bundle.FullName
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)." }
    Write-Host "Signed test bundle: $($bundle.FullName)"
} else {
    Get-ChildItem $output -Filter *.msixupload | ForEach-Object { Write-Host "Store upload: $($_.FullName)" }
}
