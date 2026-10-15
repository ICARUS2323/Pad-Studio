param([string]$BuildDirectory = 'artifacts/package-build/PadStudio', [string]$OutputArchive = 'release/PadStudio-win-x64.zip')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    $taskBuild = [IO.Path]::GetFullPath((Join-Path $taskRoot $BuildDirectory))
    $taskArchive = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputArchive))
    $taskStage = Join-Path $taskRoot ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    $taskApp = Join-Path $taskStage 'PadStudio'
    New-Item -ItemType Directory -Path $taskApp -Force | Out-Null
    # Copy only distribution files; never copy saved assignments, drafts or diagnostics.
    foreach ($taskFile in @('PadStudio.exe','PadStudio.dll','PadStudio.deps.json','PadStudio.runtimeconfig.json','interception.dll','KULLANIM.md')) {
        Copy-Item -LiteralPath (Join-Path $taskBuild $taskFile) -Destination $taskApp
    }
    foreach ($taskDirectory in @('driver','licenses')) {
        Copy-Item -LiteralPath (Join-Path $taskBuild $taskDirectory) -Destination $taskApp -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY.md') -Destination $taskApp
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskArchive) -Force | Out-Null
    Compress-Archive -LiteralPath $taskApp -DestinationPath $taskArchive -Force
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($taskArchive + '.sha256'), ($taskHash + '  ' + [IO.Path]::GetFileName($taskArchive) + "`n"))
    Write-Output $taskArchive
    Write-Output ('SHA256: ' + $taskHash)
} finally { Pop-Location }
