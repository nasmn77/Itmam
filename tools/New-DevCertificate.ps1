<#
.SYNOPSIS
  Creates the self-signed certificate used to sign Itmam's MSIX for local testing (once per PC).

.DESCRIPTION
  The certificate lives only in the current user's certificate store (Cert:\CurrentUser\My):
  no .pfx file and no password are written anywhere, so there is nothing to leak or commit.
  Its subject is the Partner Center publisher, so the same manifest is used locally and in the Store
  (the Store re-signs uploads with its own certificate; this one is never used there).

  To install a package signed with it, Windows must trust the certificate: the script exports the
  public part only (artifacts\ItmamDev.cer, no private key) and, with -Trust, adds it to the
  machine's Trusted People store, which asks for administrator approval.

.EXAMPLE
  ./tools/New-DevCertificate.ps1 -Trust
#>
param(
    [switch] $Trust
)

$ErrorActionPreference = 'Stop'
$subject = 'CN=9260B530-9E02-440E-8E99-B74323B1CE93' # Package/Identity/Publisher in Package.appxmanifest
$friendlyName = 'Itmam local test signing'

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.FriendlyName -eq $friendlyName -and $_.NotAfter -gt (Get-Date) } |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject -FriendlyName $friendlyName `
        -KeyUsage DigitalSignature -KeyExportPolicy NonExportable -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(2) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') # code signing; not a CA
    Write-Host "Created certificate $($certificate.Thumbprint) in Cert:\CurrentUser\My"
} else {
    Write-Host "Using existing certificate $($certificate.Thumbprint)"
}

$root = Split-Path $PSScriptRoot -Parent
$publicFile = Join-Path $root 'artifacts\ItmamDev.cer'
New-Item -ItemType Directory -Force (Split-Path $publicFile) | Out-Null
Export-Certificate -Cert $certificate -FilePath $publicFile -Type CERT | Out-Null
Write-Host "Public certificate (no private key): $publicFile"

if ($Trust) {
    # Machine-wide trust needs elevation: run only the import in an elevated PowerShell.
    $import = "Import-Certificate -FilePath '$publicFile' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', $import
    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $certificate.Thumbprint
    Write-Host ($trusted ? 'Trusted for installing test packages on this PC.' : 'Not trusted (the administrator prompt was declined).')
}

$certificate.Thumbprint
