# Launch the Netvan Electron GUI
# Requires: Node.js, npm install in netvan-gui (already done once)

$ErrorActionPreference = "Stop"
$guiDir = $PSScriptRoot

if (-not $env:NETVAN_HOME) {
    $installCandidates = @(
        (Join-Path (Split-Path $guiDir -Parent) "netvan"),
        (Join-Path (Split-Path $guiDir -Parent) "Netvan")
    )
    foreach ($candidate in $installCandidates) {
        if (Test-Path (Join-Path $candidate "netvan.exe")) {
            $env:NETVAN_HOME = [System.IO.Path]::GetFullPath($candidate)
            Write-Host "[INFO] Using NETVAN_HOME=$($env:NETVAN_HOME)" -ForegroundColor Cyan
            break
        }
    }
}

if (-not (Test-Path (Join-Path $guiDir "node_modules"))) {
    Write-Host "[INFO] Installing GUI dependencies..." -ForegroundColor Cyan
    Push-Location $guiDir
    npm install
    Pop-Location
}

Push-Location $guiDir
npm start
Pop-Location
