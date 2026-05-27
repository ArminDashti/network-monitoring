# Network Monitor Installer
# Downloads and installs netm from GitHub releases or builds from source

param(
    [string]$Version = "latest",
    [string]$InstallDir = "$env:LocalAppData\NetM",
    [switch]$BuildFromSource,
    [switch]$AddToPath,
    [switch]$SetEnvVars,
    [switch]$SkipService,
    [int]$ServiceInterval = 5
)

$ErrorActionPreference = "Stop"

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

# Detect architecture
$Arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
Write-Info "Detected architecture: $Arch"

# Create install directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Write-Info "Created install directory: $InstallDir"
}

if ($BuildFromSource) {
    Write-Info "Building from source..."
    
    # Check for .NET SDK
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error-Exit ".NET SDK not found. Please install .NET 9 SDK first."
    }

    $LocalProject = Join-Path $PSScriptRoot "NetworkMonitor\NetworkMonitor.csproj"
    $BuildRoot = $PSScriptRoot
    $TempDir = $null

    if (-not (Test-Path $LocalProject)) {
        $TempDir = Join-Path $env:TEMP "netm-build-$((Get-Random))"
        New-Item -ItemType Directory -Path $TempDir | Out-Null
        $BuildRoot = $TempDir
        Write-Info "Cloning repository..."
        git clone --depth 1 https://github.com/ArminDashti/network-monitoring $TempDir 2>&1 | Out-Null
        $LocalProject = Join-Path $TempDir "NetworkMonitor\NetworkMonitor.csproj"
    }
    else {
        Write-Info "Building from local repository..."
    }

    try {
        Set-Location $BuildRoot

        Write-Info "Building release..."
        dotnet publish $LocalProject `
            -c Release `
            -f net9.0-windows `
            -r win-$Arch `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $InstallDir

        if ($LASTEXITCODE -ne 0) {
            Write-Error-Exit "dotnet publish failed with exit code $LASTEXITCODE"
        }

        Write-Success "Build completed successfully"
    }
    finally {
        Set-Location $PSScriptRoot
        if ($TempDir) {
            Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
        }
    }
    
    # Copy default config file if it doesn't exist
    $ConfigPath = Join-Path $InstallDir "configs.toml"
    if (-not (Test-Path $ConfigPath)) {
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
    
    # Download and install SQLite CLI tool
    Write-Info "Installing SQLite CLI tool..."
    $SqliteUrl = "https://www.sqlite.org/2024/sqlite-tools-win-$Arch-3480000.zip"
    $SqliteDownloadPath = Join-Path $env:TEMP "sqlite-tools.zip"
    
    try {
        Invoke-WebRequest -Uri $SqliteUrl -OutFile $SqliteDownloadPath
        $SqliteTempDir = Join-Path $env:TEMP "sqlite-extract-$((Get-Random))"
        Expand-Archive -Path $SqliteDownloadPath -DestinationPath $SqliteTempDir -Force
        
        # Find sqlite3.exe in the extracted folder
        $SqliteExe = Get-ChildItem -Path $SqliteTempDir -Filter "sqlite3.exe" -Recurse | Select-Object -First 1
        if ($SqliteExe) {
            Copy-Item -Path $SqliteExe.FullName -Destination (Join-Path $InstallDir "sqlite3.exe") -Force
            Write-Success "SQLite CLI tool installed at $(Join-Path $InstallDir 'sqlite3.exe')"
        }
        
        Remove-Item $SqliteDownloadPath -Force -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force $SqliteTempDir -ErrorAction SilentlyContinue
    }
    catch {
        Write-Info "SQLite download failed, continuing without it..."
    }
}
else {
    Write-Info "Installing from GitHub releases..."
    
    # Determine download URL
    if ($Version -eq "latest") {
        $ReleaseUrl = "https://api.github.com/repos/ArminDashti/network-monitoring/releases/latest"
    }
    else {
        $ReleaseUrl = "https://api.github.com/repos/ArminDashti/network-monitoring/releases/tags/$Version"
    }
    
    try {
        $Release = Invoke-RestMethod -Uri $ReleaseUrl -Headers @{ "Accept" = "application/vnd.github.v3+json" }
        $AssetName = "netm-win-$Arch.zip"
        $Asset = $Release.assets | Where-Object { $_.name -eq $AssetName }
        
        if (-not $Asset) {
            $Asset = $Release.assets | Where-Object { $_.name -like "*win*$Arch*" } | Select-Object -First 1
        }
        
        if (-not $Asset) {
            Write-Error-Exit "No suitable release asset found for $Arch architecture"
        }
        
        Write-Info "Downloading $($Asset.name)..."
        $DownloadPath = Join-Path $env:TEMP $Asset.name
        Invoke-WebRequest -Uri $Asset.browser_download_url -OutFile $DownloadPath
        
        Write-Info "Extracting to $InstallDir..."
        Expand-Archive -Path $DownloadPath -DestinationPath $InstallDir -Force
        Remove-Item $DownloadPath -Force
        
        Write-Success "Download and extraction completed"
        
        # Copy default config file if it doesn't exist
        $ConfigPath = Join-Path $InstallDir "configs.toml"
        if (-not (Test-Path $ConfigPath)) {
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
        
        # Download and install SQLite CLI tool
        Write-Info "Installing SQLite CLI tool..."
        $SqliteUrl = "https://www.sqlite.org/2024/sqlite-tools-win-$Arch-3480000.zip"
        $SqliteDownloadPath = Join-Path $env:TEMP "sqlite-tools.zip"
        
        try {
            Invoke-WebRequest -Uri $SqliteUrl -OutFile $SqliteDownloadPath
            $SqliteTempDir = Join-Path $env:TEMP "sqlite-extract-$((Get-Random))"
            Expand-Archive -Path $SqliteDownloadPath -DestinationPath $SqliteTempDir -Force
            
            # Find sqlite3.exe in the extracted folder
            $SqliteExe = Get-ChildItem -Path $SqliteTempDir -Filter "sqlite3.exe" -Recurse | Select-Object -First 1
            if ($SqliteExe) {
                Copy-Item -Path $SqliteExe.FullName -Destination (Join-Path $InstallDir "sqlite3.exe") -Force
                Write-Success "SQLite CLI tool installed at $(Join-Path $InstallDir 'sqlite3.exe')"
            }
            
            Remove-Item $SqliteDownloadPath -Force -ErrorAction SilentlyContinue
            Remove-Item -Recurse -Force $SqliteTempDir -ErrorAction SilentlyContinue
        }
        catch {
            Write-Info "SQLite download failed, continuing without it..."
        }
    }
    catch {
        Write-Info "Release download failed, falling back to source build..."
        $BuildFromSource = $true
        # Re-run the build logic
        & $PSCommandPath -BuildFromSource:$BuildFromSource -InstallDir $InstallDir -AddToPath:$AddToPath -SetEnvVars:$SetEnvVars -SkipService:$SkipService
        exit $LASTEXITCODE
    }
}

# Add to PATH and set environment variables if requested
if ($AddToPath -or $SetEnvVars) {
    $CurrentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($CurrentUserPath -notlike "*$InstallDir*") {
        [Environment]::SetEnvironmentVariable("Path", "$CurrentUserPath;$InstallDir", "User")
        Write-Success "Added $InstallDir to user PATH"
    }
    else {
        Write-Info "$InstallDir is already in PATH"
    }
    
    # Set NETM_HOME environment variable
    [Environment]::SetEnvironmentVariable("NETM_HOME", $InstallDir, "User")
    Write-Success "Set NETM_HOME environment variable to $InstallDir"
    
    Write-Info "Please restart your terminal or run:"
    Write-Info "`$env:Path += `";$InstallDir`""
    Write-Info "`$env:NETM_HOME = `"$InstallDir`""
}

# Verify installation
$NetmExe = Join-Path $InstallDir "netm.exe"
$SqliteExe = Join-Path $InstallDir "sqlite3.exe"
$ConfigFile = Join-Path $InstallDir "configs.toml"

if (-not $SkipService) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "[WARN] Daemon mode requires an elevated PowerShell session." -ForegroundColor Yellow
        Write-Host "[WARN] Re-run install.ps1 as Administrator, or run: netm service install && netm service start" -ForegroundColor Yellow
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
    Write-Info "Daemon mode: install.ps1 installs the Windows service by default (elevated). Use -SkipService to install binaries only."
    Write-Info "Use 'sqlite3.exe' to manage the SQLite database directly"
    if (-not ($AddToPath -or $SetEnvVars)) {
        Write-Info ""
        Write-Info "To add to PATH and set env vars, run with -SetEnvVars flag or manually add $InstallDir to your PATH"
    }
}
else {
    Write-Error-Exit "Installation failed: netm.exe not found at $NetmExe"
}
