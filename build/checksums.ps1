<#
.SYNOPSIS
  Writes SHA256SUMS.txt for every file in the artifacts directory (sha256sum-compatible format).
#>
[CmdletBinding()]
param(
    [string]$ArtifactsDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $ArtifactsDirectory) {
    # $PSScriptRoot is not available inside parameter defaults under Windows PowerShell -File, so resolve it here.
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ArtifactsDirectory = Join-Path $scriptDir '..\artifacts'
}
$files = Get-ChildItem $ArtifactsDirectory -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name
$lines = foreach ($f in $files) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $f.FullName).Hash.ToLowerInvariant()
    "$hash  $($f.Name)"
}
# LF line endings and no BOM so `sha256sum -c SHA256SUMS.txt` works on Linux, macOS and Git Bash as well as Windows.
$content = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText((Join-Path $ArtifactsDirectory 'SHA256SUMS.txt'), $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote SHA256SUMS.txt ($($files.Count) files)"
$lines | ForEach-Object { Write-Host "  $_" }
