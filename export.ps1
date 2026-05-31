# Network Monitor Exporter
# Builds from the local repository and publishes to .\NetworkMonitoringExport

param(
    [Alias("path")]
    [string]$InstallDir,
    [switch]$AddToPath,
    [switch]$SetEnvVars,
    [switch]$SkipService,
    [int]$ServiceInterval = 5
)

$ErrorActionPreference = "Stop"

# Resolve install directory: -InstallDir, -path, --path=<dir>, or .\NetworkMonitoringExport
$PathFromArgs = $null
foreach ($arg in $args) {
    if ($arg -match '^(?:--|-)path=(.+)$') {
        $PathFromArgs = $Matches[1].Trim('"', "'")
        break
    }
}
if (-not $InstallDir) {
    if ($PathFromArgs) {
        $InstallDir = $PathFromArgs
    }
    else {
        $InstallDir = Join-Path (Get-Location).Path "NetworkMonitoringExport"
    }
}
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[SUCCESS] $Message" -ForegroundColor Green
}

function Write-Error-Exit {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    exit 1
}

function Remove-NetMServiceIfPresent {
    param([string]$InstallDirectory)

    $serviceName = "NetM"
    $netmExe = Join-Path $InstallDirectory "netm.exe"
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $service) {
        return
    }

    if (Test-Path $netmExe) {
        if ($service.Status -eq "Running") {
            Write-Info "Stopping '$serviceName' Windows service (Network Monitor)..."
            & $netmExe service stop | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[WARN] netm service stop returned exit code $LASTEXITCODE; trying Stop-Service..." -ForegroundColor Yellow
                Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            }
            else {
                Write-Success "Stopped '$serviceName' service."
            }
        }

        Write-Info "Removing existing '$serviceName' Windows service registration..."
        & $netmExe service uninstall | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Removed '$serviceName' service."
            return
        }

        Write-Host "[WARN] netm service uninstall returned exit code $LASTEXITCODE; trying sc.exe delete..." -ForegroundColor Yellow
    }
    elseif ($service.Status -eq "Running") {
        Write-Info "Stopping '$serviceName' Windows service..."
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }

    $null = & sc.exe delete $serviceName 2>&1
    if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
        Write-Success "Removed '$serviceName' service."
    }
    else {
        Write-Host "[WARN] Could not remove '$serviceName' service. Re-run as Administrator if install fails." -ForegroundColor Yellow
    }
}

function Clear-InstallDirectory {
    param([string]$Dir)

    if (-not (Test-Path $Dir)) {
        return
    }

    Write-Info "Deleting all files in $Dir..."
    Get-ChildItem -Path $Dir -Force | Remove-Item -Recurse -Force -ErrorAction Stop
    Write-Success "Cleared install directory."
}

function Update-InstallEnvironmentVariables {
    param([string]$Dir)

    $currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($currentUserPath -notlike "*$Dir*") {
        $newPath = if ([string]::IsNullOrWhiteSpace($currentUserPath)) { $Dir } else { "$currentUserPath;$Dir" }
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        Write-Success "Added $Dir to user PATH."
    }
    else {
        Write-Info "$Dir is already in PATH."
    }

    [Environment]::SetEnvironmentVariable("NETM_HOME", $Dir, "User")
    Write-Success "Set NETM_HOME environment variable to $Dir."

    $env:Path = if ($env:Path -like "*$Dir*") { $env:Path } else { "$env:Path;$Dir" }
    $env:NETM_HOME = $Dir

    Write-Info "Restart your terminal for PATH/NETM_HOME to apply in new sessions."
}

function Ensure-DefaultConfig {
    param([string]$ConfigPath)

    if (Test-Path $ConfigPath) {
        return
    }

    $SourceConfig = Join-Path $PSScriptRoot "configs.toml"
    if (Test-Path $SourceConfig) {
        Copy-Item -Path $SourceConfig -Destination $ConfigPath -Force
        Write-Success "Copied configuration file to $ConfigPath"
        return
    }

    Write-Info "Creating default configuration file..."
    $DefaultConfig = @"
# Network Monitor Configuration

# Database path (default: %LocalAppData%\NetM\traffic.db)
database_path = "%NETM_HOME%\\traffic.db"

# Monitoring settings
[monitoring]
# Enable/disable monitoring
enabled = true
# Sampling interval in seconds
sampling_interval = 5

# Storage settings
[storage]
# Maximum database size in MB
max_size_mb = 500
# Retention period in days
retention_days = 30

# Logging settings
[logging]
# Log level: Debug, Info, Warning, Error
level = "Info"
# Log file path
log_file = "%NETM_HOME%\\netm.log"
"@
    Set-Content -Path $ConfigPath -Value $DefaultConfig
    Write-Success "Created default configuration file at $ConfigPath"
}

function Get-AssetsDirectory {
    $assetsDir = Join-Path $PSScriptRoot "assets"
    if (-not (Test-Path $assetsDir)) {
        New-Item -ItemType Directory -Path $assetsDir | Out-Null
        Write-Info "Created assets directory: $assetsDir"
    }
    return $assetsDir
}

function Ensure-SqliteCliAsset {
    param([string]$Architecture)

    $AssetsDir = Get-AssetsDirectory
    $SqliteVersion = "3530100"
    $ZipName = "sqlite-tools-win-$Architecture-$SqliteVersion.zip"
    $SqliteZipPath = Join-Path $AssetsDir $ZipName
    $SqliteExePath = Join-Path $AssetsDir "sqlite3-$Architecture.exe"

    if (Test-Path $SqliteExePath) {
        Write-Info "Using cached SQLite CLI from $SqliteExePath"
        return $SqliteExePath
    }

    try {
        if (-not (Test-Path $SqliteZipPath)) {
            $SqliteUrl = "https://www.sqlite.org/2026/$ZipName"
            Write-Info "Downloading SQLite CLI to ./assets (one-time)..."
            Invoke-WebRequest -Uri $SqliteUrl -OutFile $SqliteZipPath
            Write-Success "Saved $ZipName to ./assets"
        }
        else {
            Write-Info "Using cached SQLite archive: $SqliteZipPath"
        }

        $SqliteExtractDir = Join-Path $AssetsDir "sqlite-extract-$Architecture"
        if (-not (Test-Path $SqliteExtractDir)) {
            Expand-Archive -Path $SqliteZipPath -DestinationPath $SqliteExtractDir -Force
        }

        $SqliteExe = Get-ChildItem -Path $SqliteExtractDir -Filter "sqlite3.exe" -Recurse | Select-Object -First 1
        if (-not $SqliteExe) {
            Write-Host "[WARN] sqlite3.exe not found in $SqliteExtractDir" -ForegroundColor Yellow
            return $null
        }

        Copy-Item -Path $SqliteExe.FullName -Destination $SqliteExePath -Force
        Write-Success "Cached SQLite CLI at $SqliteExePath"
        return $SqliteExePath
    }
    catch {
        Write-Info "SQLite download failed, continuing without it..."
        return $null
    }
}

function Install-SqliteCli {
    param(
        [string]$TargetDir,
        [string]$Architecture
    )

    Write-Info "Installing SQLite CLI tool..."
    $SqliteExePath = Ensure-SqliteCliAsset -Architecture $Architecture
    if (-not $SqliteExePath) {
        return
    }

    $TargetExe = Join-Path $TargetDir "sqlite3.exe"
    Copy-Item -Path $SqliteExePath -Destination $TargetExe -Force
    Write-Success "SQLite CLI tool installed at $TargetExe"
}

$Arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
Write-Info "Install directory: $InstallDir"
Write-Info "Detected architecture: $Arch"

Remove-NetMServiceIfPresent -InstallDirectory $InstallDir
Clear-InstallDirectory -Dir $InstallDir

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Write-Info "Created install directory: $InstallDir"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error-Exit ".NET SDK not found. Install .NET 9 SDK, then run export.ps1 again."
}

$LocalProject = Join-Path $PSScriptRoot "NetworkMonitor\NetworkMonitor.csproj"
if (-not (Test-Path $LocalProject)) {
    Write-Error-Exit "Project not found at $LocalProject. Run export.ps1 from the repository root."
}

$OriginalLocation = Get-Location
try {
    Set-Location $PSScriptRoot

    Write-Info "Publishing release build to $InstallDir..."
    dotnet publish $LocalProject `
        -c Release `
        -f net9.0-windows `
        -r "win-$Arch" `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $InstallDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error-Exit "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Write-Success "Publish completed successfully"
}
finally {
    Set-Location $OriginalLocation
}

Ensure-DefaultConfig -ConfigPath (Join-Path $InstallDir "configs.toml")
Install-SqliteCli -TargetDir $InstallDir -Architecture $Arch
Update-InstallEnvironmentVariables -Dir $InstallDir

$NetmExe = Join-Path $InstallDir "netm.exe"
$SqliteExe = Join-Path $InstallDir "sqlite3.exe"
$ConfigFile = Join-Path $InstallDir "configs.toml"

if (-not $SkipService) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "[WARN] Daemon mode requires an elevated PowerShell session." -ForegroundColor Yellow
        Write-Host "[WARN] Re-run export.ps1 as Administrator, or run: netm service install && netm service start" -ForegroundColor Yellow
    }
    elseif (-not (Test-Path $NetmExe)) {
        Write-Error-Exit "Installation failed: netm.exe not found at $NetmExe"
    }
    else {
        $DbPath = Join-Path $InstallDir "traffic.db"
        Write-Info "Installing NetM Windows service (daemon mode, interval ${ServiceInterval}s)..."
        & $NetmExe service install --db $DbPath --interval $ServiceInterval
        if ($LASTEXITCODE -ne 0) {
            Write-Error-Exit "netm service install failed with exit code $LASTEXITCODE"
        }

        Write-Info "Starting NetM Windows service..."
        & $NetmExe service start
        if ($LASTEXITCODE -ne 0) {
            Write-Error-Exit "netm service start failed with exit code $LASTEXITCODE"
        }

        Write-Success "NetM Windows service installed and started (daemon mode)."
    }
}

if (Test-Path $NetmExe) {
    Write-Success "Installation complete! Files installed to: $InstallDir"
    Write-Info "Installed files:"
    Write-Info "  - netm.exe (Network Monitor CLI)"
    if (Test-Path $SqliteExe) {
        Write-Info "  - sqlite3.exe (SQLite CLI tool)"
    }
    if (Test-Path $ConfigFile) {
        Write-Info "  - configs.toml (Configuration file)"
    }
    Write-Info ""
    Write-Info "Run 'netm info' to verify the installation"
    Write-Info "Daemon mode: export.ps1 installs the Windows service by default (elevated). Use -SkipService to install binaries only."
    Write-Info "Optional local mode: run 'netm start' then 'netm status' if you prefer non-service collection."
    Write-Info "Use 'sqlite3.exe' to manage the SQLite database directly"
}
else {
    Write-Error-Exit "Installation failed: netm.exe not found at $NetmExe"
}
