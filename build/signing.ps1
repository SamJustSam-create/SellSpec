<#
.SYNOPSIS
  Stages SellSpec executables for code signing and applies the signed copies afterwards.

.DESCRIPTION
  Used by .github/workflows/release.yml around the SignPath signing request (see CODE_SIGNING_POLICY.md).

  -Stage copies the executables that need an Authenticode signature into a flat folder with fixed names, so
  SignPath's artifact configuration does not depend on the version number:

    SellSpec-singlefile.exe      <- SellSpec-<version>-win-x64-singlefile.exe
    SellSpec-selfcontained.exe   <- SellSpec-<version>-win-x64-selfcontained.exe   (when present)
    portable\SellSpec.exe        <- SellSpec-<version>-win-x64-portable\SellSpec.exe

  -Apply takes the folder the signing service returned, checks that every staged file came back with a valid,
  timestamped Authenticode signature from the expected signer, copies the signed files over the unsigned ones,
  rebuilds the portable zip and regenerates SHA256SUMS.txt. A missing or unverified file is an error, so a
  release cannot silently ship half-signed.

.PARAMETER ArtifactsDirectory
  The folder build\publish.ps1 wrote to (artifacts\ by default).

.PARAMETER StagingDirectory
  -Stage: destination folder; created empty.

.PARAMETER SignedDirectory
  -Apply: the folder holding the signed files in the staging layout.

.PARAMETER ExpectedSigner
  -Apply: text that must appear in the signing certificate's subject. Default "SignPath Foundation".

.PARAMETER SkipSignatureCheck
  -Apply: copy the files without verifying signatures. For local dry runs of the packaging steps only; the
  release workflow never passes it, and the output of a dry run is not a signed build.
#>
[CmdletBinding(DefaultParameterSetName = 'Stage')]
param(
    [Parameter(ParameterSetName = 'Stage', Mandatory)] [switch]$Stage,
    [Parameter(ParameterSetName = 'Apply', Mandatory)] [switch]$Apply,
    [string]$ArtifactsDirectory,
    [Parameter(ParameterSetName = 'Stage')] [string]$StagingDirectory,
    [Parameter(ParameterSetName = 'Apply')] [string]$SignedDirectory,
    [Parameter(ParameterSetName = 'Apply')] [string]$ExpectedSigner = 'SignPath Foundation',
    [Parameter(ParameterSetName = 'Apply')] [switch]$SkipSignatureCheck
)
$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty inside parameter defaults when the script runs through -File, so resolve it here.
if (-not $ArtifactsDirectory) { $ArtifactsDirectory = Join-Path $PSScriptRoot '..\artifacts' }
$artifacts = (Resolve-Path $ArtifactsDirectory).Path

# Locate the published layout. Exactly one portable folder is expected; its name carries the version.
$portableDirs = @(Get-ChildItem $artifacts -Directory -Filter 'SellSpec-*-win-x64-portable')
if ($portableDirs.Count -ne 1) {
    throw "Expected exactly one SellSpec-<version>-win-x64-portable folder in $artifacts, found $($portableDirs.Count). Run build\publish.ps1 first."
}
$portableDir = $portableDirs[0].FullName
$version = $portableDirs[0].Name -replace '^SellSpec-(.+)-win-x64-portable$', '$1'

# Staged name -> published path. The self-contained build is optional (publish.ps1 -SkipSelfContained).
$layout = [ordered]@{
    'SellSpec-singlefile.exe'    = Join-Path $artifacts "SellSpec-$version-win-x64-singlefile.exe"
    'SellSpec-selfcontained.exe' = Join-Path $artifacts "SellSpec-$version-win-x64-selfcontained.exe"
    'portable\SellSpec.exe'      = Join-Path $portableDir 'SellSpec.exe'
}
foreach ($name in 'SellSpec-singlefile.exe', 'portable\SellSpec.exe') {
    if (-not (Test-Path $layout[$name])) { throw "Missing $($layout[$name])." }
}
$present = [ordered]@{}
foreach ($entry in $layout.GetEnumerator()) {
    if (Test-Path $entry.Value) { $present[$entry.Key] = $entry.Value }
}

if ($Stage) {
    if (-not $StagingDirectory) { throw 'StagingDirectory is required with -Stage.' }
    if (Test-Path $StagingDirectory) { Remove-Item -Recurse -Force $StagingDirectory }
    New-Item -ItemType Directory -Force $StagingDirectory | Out-Null
    foreach ($entry in $present.GetEnumerator()) {
        $destination = Join-Path $StagingDirectory $entry.Key
        New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
        Copy-Item $entry.Value $destination
        Write-Host ('  staged   {0,-28} <- {1}' -f $entry.Key, (Split-Path $entry.Value -Leaf))
    }
    Write-Host "==> $($present.Count) file(s) staged for signing in $StagingDirectory (SellSpec $version)" -ForegroundColor Cyan
    return
}

# -Apply
if (-not $SignedDirectory) { throw 'SignedDirectory is required with -Apply.' }
if (-not (Test-Path $SignedDirectory)) { throw "Signed folder not found: $SignedDirectory" }
$signedRoot = (Resolve-Path $SignedDirectory).Path

# The signing service returns the staged zip re-packed and the workflow extracts it. Accept the files at the
# root of the folder or one level down by locating the single-file executable.
$anchors = @(Get-ChildItem $signedRoot -Recurse -File -Filter 'SellSpec-singlefile.exe')
if ($anchors.Count -ne 1) {
    throw "Expected exactly one SellSpec-singlefile.exe under $signedRoot, found $($anchors.Count)."
}
$signedBase = $anchors[0].DirectoryName

$failures = New-Object System.Collections.Generic.List[string]
$verified = [ordered]@{}
foreach ($entry in $present.GetEnumerator()) {
    $signedFile = Join-Path $signedBase $entry.Key
    if (-not (Test-Path $signedFile)) {
        $failures.Add("$($entry.Key): not returned by the signing service")
        continue
    }
    if ($SkipSignatureCheck) {
        $verified[$entry.Key] = $signedFile
        continue
    }
    $signature = Get-AuthenticodeSignature -FilePath $signedFile
    if ($signature.Status -ne 'Valid') {
        # Keep the first sentence only; the rest of the message is execution-policy boilerplate.
        $reason = ([string]$signature.StatusMessage -split '(?<=\.)\s+')[0]
        $failures.Add("$($entry.Key): signature status $($signature.Status) ($reason)")
        continue
    }
    $subject = $signature.SignerCertificate.Subject
    if ($subject -notlike "*$ExpectedSigner*") {
        $failures.Add("$($entry.Key): signed by '$subject', expected a subject containing '$ExpectedSigner'")
        continue
    }
    if (-not $signature.TimeStamperCertificate) {
        $failures.Add("$($entry.Key): signature is not timestamped")
        continue
    }
    $verified[$entry.Key] = $signedFile
    Write-Host ('  verified {0,-28} {1}' -f $entry.Key, $subject)
}
if ($failures.Count -gt 0) {
    throw ("Signed files failed verification:`n  " + ($failures -join "`n  "))
}

foreach ($entry in $verified.GetEnumerator()) {
    Copy-Item $entry.Value $present[$entry.Key] -Force
}

# Rebuild the portable zip from the (now signed) folder exactly as publish.ps1 does, then the checksums.
Compress-Archive -Path "$portableDir\*" -DestinationPath "$portableDir.zip" -Force
& (Join-Path $PSScriptRoot 'checksums.ps1') -ArtifactsDirectory $artifacts

$outcome = if ($SkipSignatureCheck) { 'copied WITHOUT signature verification (dry run; not a signed build)' } else { "verified as signed by $ExpectedSigner" }
Write-Host "==> $($verified.Count) executable(s) $outcome; portable zip and SHA256SUMS.txt regenerated." -ForegroundColor Cyan
