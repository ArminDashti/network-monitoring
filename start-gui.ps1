# Launch the NetM Electron GUI
# Requires: Node.js, npm install in NetMGui (already done once)

$ErrorActionPreference = "Stop"
$guiDir = Join-Path $PSScriptRoot "NetMGui"

if (-not (Test-Path (Join-Path $guiDir "node_modules"))) {
    Write-Host "[INFO] Installing GUI dependencies..." -ForegroundColor Cyan
    Push-Location $guiDir
    npm install
    Pop-Location
}

Push-Location $guiDir
npm start
Pop-Location
