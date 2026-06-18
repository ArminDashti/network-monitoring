# Runs netvan-core/export.ps1 after the agent completes a prompt (stop hook),
# then launches the Netvan GUI from the published install directory.
# Project hooks execute with cwd at the repository root.

$ErrorActionPreference = "Continue"

# Consume hook JSON from stdin (required by Cursor hooks protocol).
$null = [Console]::In.ReadToEnd()

$exportScript = Join-Path $PSScriptRoot "..\..\netvan-core\export.ps1" | Resolve-Path -ErrorAction Stop

Write-Host "[netvan hook] Running export: $exportScript" -ForegroundColor Cyan

$logFile = Join-Path $env:TEMP "netvan-export-hook.log"
if (Test-Path $logFile) {
    Remove-Item $logFile -Force
}

# Hooks run without elevation; -SkipService publishes binaries to netvan/ only.
& $exportScript -SkipService *> $logFile
$exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
$combined = if (Test-Path $logFile) { Get-Content $logFile -Raw } else { "" }

if ($combined) {
    Write-Host $combined.TrimEnd()
}

if ($exitCode -ne 0) {
    if ($combined -match 'existing Netvan install or Windows service is still active') {
        Write-Host "[netvan hook] Skipped export: Netvan service or install is active (run export.ps1 elevated to replace files)." -ForegroundColor Yellow
        exit 0
    }

    Write-Host "[netvan hook] export.ps1 failed with exit code $exitCode" -ForegroundColor Red
    exit $exitCode
}

Write-Host "[netvan hook] export.ps1 completed successfully" -ForegroundColor Green

$installDir = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $exportScript -Parent) "..\netvan"))
$netvanExe = Join-Path $installDir "netvan.exe"
if (-not (Test-Path $netvanExe)) {
    Write-Host "[netvan hook] GUI not launched: netvan.exe not found at $netvanExe" -ForegroundColor Yellow
    exit 0
}

Write-Host "[netvan hook] Launching GUI: $netvanExe" -ForegroundColor Cyan
try {
    Start-Process -FilePath $netvanExe -WorkingDirectory $installDir | Out-Null
    Write-Host "[netvan hook] GUI launched" -ForegroundColor Green
}
catch {
    Write-Host "[netvan hook] Failed to launch GUI: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

exit 0
