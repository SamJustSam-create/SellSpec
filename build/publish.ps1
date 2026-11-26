<#
.SYNOPSIS
  Builds SellSpec release artifacts.

.DESCRIPTION
  Produces, under artifacts\:
    SellSpec-<version>-win-x64-portable\          framework-dependent folder (needs .NET Desktop Runtime 10)
    SellSpec-<version>-win-x64-portable.zip       the folder zipped, with portable.txt so settings stay beside the exe
    SellSpec-<version>-win-x64-singlefile.exe     framework-dependent single file (needs .NET Desktop Runtime 10)
    SellSpec-<version>-win-x64-selfcontained.exe  self-contained single file (no runtime needed, larger)
    SHA256SUMS.txt                                checksums of every artifact
  Run from any directory. Requires the .NET 10 SDK (dot-source build\dotnet-env.ps1 if it is installed user-locally).

.PARAMETER Configuration
  Build configuration; Release by default.

.PARAMETER SkipTests
  Skip running the automated tests before publishing.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipSelfContained
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'dotnet-env.ps1')

# Read the version through XPath InnerText so an empty <VersionSuffix></VersionSuffix> (a stable release)
# yields no suffix rather than a truthy XML element.
$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)
$prefixNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
$suffixNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionSuffix')
$versionPrefix = if ($prefixNode) { $prefixNode.InnerText.Trim() } else { '' }
$versionSuffix = if ($suffixNode) { $suffixNode.InnerText.Trim() } else { '' }
if (-not $versionPrefix) { throw 'VersionPrefix is missing from Directory.Build.props.' }
$version = if ($versionSuffix) { "$versionPrefix-$versionSuffix" } else { $versionPrefix }

$artifacts = Join-Path $root 'artifacts'
if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }
New-Item -ItemType Directory -Force $artifacts | Out-Null

$app = Join-Path $root 'src\SellSpec.App\SellSpec.App.csproj'
$common = @('-c', $Configuration, '-r', 'win-x64', '--nologo', '-p:ContinuousIntegrationBuild=true', '-p:DebugType=none', '-p:DebugSymbols=false')

Write-Host "==> SellSpec $version ($Configuration)" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host '==> Running tests' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'SellSpec.slnx') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; publish aborted.' }
}

# 1. Framework-dependent portable folder
$portableDir = Join-Path $artifacts "SellSpec-$version-win-x64-portable"
Write-Host '==> Publishing framework-dependent portable folder' -ForegroundColor Cyan
dotnet publish $app @common --self-contained false -o $portableDir
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
Set-Content -Path (Join-Path $portableDir 'portable.txt') -Value 'Delete this file to store settings in %LOCALAPPDATA%\SellSpec instead of the data folder beside SellSpec.exe.' -Encoding ascii
Copy-Item (Join-Path $root 'README.md') $portableDir
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') $portableDir
Compress-Archive -Path "$portableDir\*" -DestinationPath "$portableDir.zip" -Force

# 2. Framework-dependent single file
Write-Host '==> Publishing framework-dependent single file' -ForegroundColor Cyan
$fdTemp = Join-Path $artifacts 'tmp-fd'
dotnet publish $app @common --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $fdTemp
if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }
Move-Item (Join-Path $fdTemp 'SellSpec.exe') (Join-Path $artifacts "SellSpec-$version-win-x64-singlefile.exe") -Force
Remove-Item -Recurse -Force $fdTemp

# 3. Self-contained single file (no runtime required)
if (-not $SkipSelfContained) {
    Write-Host '==> Publishing self-contained single file' -ForegroundColor Cyan
    $scTemp = Join-Path $artifacts 'tmp-sc'
    dotnet publish $app @common --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $scTemp
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    Move-Item (Join-Path $scTemp 'SellSpec.exe') (Join-Path $artifacts "SellSpec-$version-win-x64-selfcontained.exe") -Force
    Remove-Item -Recurse -Force $scTemp
}

# 4. Checksums
& (Join-Path $PSScriptRoot 'checksums.ps1') -ArtifactsDirectory $artifacts

Write-Host '==> Artifacts' -ForegroundColor Cyan
Get-ChildItem $artifacts -File | ForEach-Object { '{0,12:N0} KB  {1}' -f ($_.Length / 1KB), $_.Name }
Write-Host 'Note: binaries are NOT code-signed. See DEVELOPMENT.md for signing instructions.' -ForegroundColor Yellow
