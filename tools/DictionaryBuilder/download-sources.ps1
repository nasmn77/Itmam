<#
.SYNOPSIS
    Downloads the pinned source word lists used to build the shipped dictionaries.

.DESCRIPTION
    Every source is pinned to an exact commit and verified by SHA-256 so the dictionary can be
    rebuilt byte-for-byte. Files land in data/raw/ (git-ignored). See data/README.md for licenses.
    This is the only network access in the whole project; the app itself never goes online.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\data\raw')
)

$ErrorActionPreference = 'Stop'

$sources = @(
    @{
        Name   = 'frequencywords'
        # hermitdave/FrequencyWords, OpenSubtitles 2018, content licensed CC-BY-SA-4.0.
        Url    = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/525f9b560de45753a5ea01069454e72e9aa541c6/content/2018/ar/ar_full.txt'
        File   = 'frequencywords-ar_full.txt'
        Sha256 = 'EBB36AEAE78E609ADC552B32EC91FF2490E1170F8B244D22EEF9952D2096D610'
    }
    @{
        Name   = 'frequencywords-en'
        # The same project and commit, English list (2.0), content licensed CC-BY-SA-4.0.
        Url    = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/525f9b560de45753a5ea01069454e72e9aa541c6/content/2018/en/en_full.txt'
        File   = 'frequencywords-en_full.txt'
        Sha256 = '7FEA67AB954E2C01DF6C608C9826E594CF36F8823B3243554F88245FB75DC506'
    }
    @{
        Name   = 'ldnoobw-en'
        # LDNOOBW English list, CC-BY-4.0 (Shutterstock): the candidates the English blocklist was hand-picked from.
        Url    = 'https://raw.githubusercontent.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words/5faf2ba42d7b1c0977169ec3611df25a3c08eb13/en'
        File   = 'ldnoobw-en.txt'
        Sha256 = 'AF851ECEF1D5F212CABA17339B12AC39CC2FEF7D78C74876F67237644FCEE8BD'
    }
)

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

foreach ($source in $sources) {
    $target = Join-Path $OutputDirectory $source.File
    Write-Host "Downloading $($source.Name) -> $target"
    Invoke-WebRequest -Uri $source.Url -OutFile $target -UseBasicParsing

    $actual = (Get-FileHash -Algorithm SHA256 -Path $target).Hash
    if ($source.Sha256 -and $actual -ne $source.Sha256) {
        Remove-Item $target
        throw "SHA-256 mismatch for $($source.Name): expected $($source.Sha256), got $actual"
    }
    Write-Host "  SHA-256 $actual"
}
