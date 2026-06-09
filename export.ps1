# Netvan export entry point
# Builds from netvan-core and publishes to .\netvan
#
# Usage (same flags as netvan-core\export.ps1):
#   .\export.ps1
#   .\export.ps1 --path=C:\Tools\Netvan
#   .\export.ps1 -SkipService

$ErrorActionPreference = "Stop"

$Exporter = Join-Path $PSScriptRoot "netvan-core\export.ps1"
if (-not (Test-Path $Exporter)) {
    Write-Host "[ERROR] Exporter not found at $Exporter" -ForegroundColor Red
    exit 1
}

& $Exporter @args
exit $LASTEXITCODE
