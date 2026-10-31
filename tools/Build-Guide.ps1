<#
.SYNOPSIS
  Builds the user guide PDFs (docs/guide/itmam-guide-ar.pdf and -en.pdf) from docs/guide/guide.*.html.

.DESCRIPTION
  Prints each page with Microsoft Edge (headless, already on every Windows PC): Arabic shaping and right-to-left
  layout come out right, and nothing is downloaded. The PDFs are shipped in the package (Settings → General →
  User guide, and the tray menu) and copied to the website (site/assets/guide/).
  The screenshots in docs/guide/img come from the Debug build with sample data (ITMAM_DEMO_DATA).

.EXAMPLE
  ./tools/Build-Guide.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$guide = Join-Path $root 'docs\guide'
$site = Join-Path $root 'site\assets\guide'

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found.' }

# A separate, temporary profile: the user's own browser profile is never touched.
$profile = Join-Path ([IO.Path]::GetTempPath()) "itmam-guide-$PID"
New-Item -ItemType Directory -Force $site | Out-Null
try {
    foreach ($lang in 'ar', 'en') {
        $html = Join-Path $guide "guide.$lang.html"
        $pdf = Join-Path $guide "itmam-guide-$lang.pdf"
        Remove-Item $pdf -ErrorAction SilentlyContinue
        $url = 'file:///' + ($html -replace '\\', '/')
        $arguments = @('--headless=new', '--disable-gpu', '--no-pdf-header-footer', "--user-data-dir=$profile",
            "--print-to-pdf=$pdf", $url)
        Start-Process -FilePath $edge -ArgumentList $arguments -Wait -WindowStyle Hidden
        if (-not (Test-Path $pdf)) { throw "Edge did not write $pdf" }
        Copy-Item $pdf $site -Force
        '{0}: {1:N0} KB' -f $pdf, ((Get-Item $pdf).Length / 1KB)
    }
}
finally {
    Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
}
