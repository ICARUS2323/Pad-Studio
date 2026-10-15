param([string]$OutputDirectory = 'release/PadStudio')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    if (-not (Test-Path -LiteralPath 'vendor/Interception/Interception/library/x64/interception.dll')) { & (Join-Path $PSScriptRoot 'prepare-dependencies.ps1') }
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot 'artifacts/dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:NUGET_PACKAGES = Join-Path $taskRoot 'artifacts/packages'
    $env:APPDATA = Join-Path $taskRoot 'artifacts/build-appdata'
    dotnet restore src/PadStudio.csproj --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Proje geri yüklenemedi.' }
    dotnet publish src/PadStudio.csproj -c Release --no-restore --no-self-contained -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'KULLANIM.md') -Destination (Join-Path $OutputDirectory 'KULLANIM.md')
} finally { Pop-Location }
