$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVendor = Join-Path $taskRoot 'vendor'
$taskArchive = Join-Path $taskVendor 'Interception.zip'
New-Item -ItemType Directory -Path $taskVendor -Force | Out-Null
if (-not (Test-Path -LiteralPath $taskArchive)) {
    Invoke-WebRequest -Uri 'https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip' -OutFile $taskArchive
}
$taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash
if ($taskHash -ne 'AD038963D6413055765128B0B931F6E765147C9916DBA79E65D872B261F9AF10') { throw 'Interception arşivinin SHA256 değeri beklenen sürümle eşleşmiyor.' }
Expand-Archive -LiteralPath $taskArchive -DestinationPath (Join-Path $taskVendor 'Interception') -Force
Write-Output 'Interception v1.0.1 hazırlandı; sürücü kurulmadı.'
