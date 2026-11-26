<#
.SYNOPSIS
  Writes SHA256SUMS.txt for every file in the artifacts directory (sha256sum-compatible format).
#>
[CmdletBinding()]
param(
    [string]$ArtifactsDirectory = (Join-Path $PSScriptRoot '..\artifacts')
)
$ErrorActionPreference = 'Stop'
$files = Get-ChildItem $ArtifactsDirectory -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name
$lines = foreach ($f in $files) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $f.FullName).Hash.ToLowerInvariant()
    "$hash  $($f.Name)"
}
Set-Content -Path (Join-Path $ArtifactsDirectory 'SHA256SUMS.txt') -Value $lines -Encoding ascii
Write-Host "Wrote SHA256SUMS.txt ($($files.Count) files)"
$lines | ForEach-Object { Write-Host "  $_" }
