# Dot-source this file to use the user-local .NET SDK when no machine-wide SDK is installed:
#   . .\build\dotnet-env.ps1
$local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
if (Test-Path (Join-Path $local 'dotnet.exe')) {
    $env:DOTNET_ROOT = $local
    $env:PATH = "$local;$env:PATH"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
