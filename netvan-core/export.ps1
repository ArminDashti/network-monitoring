# Netvan Exporter
# Builds from netvan-core and publishes to ..\netvan

param(
    [Alias("path")]
    [string]$InstallDir,
    [switch]$AddToPath,
    [switch]$SetEnvVars,
    [switch]$SkipService,
    [int]$ServiceInterval = 1
)

$ErrorActionPreference = "Stop"

$NetvanServiceNames = @("Netvan", "NetM")

# Resolve install directory: -InstallDir, -path, --path=<dir>, or ..\netvan
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
        $InstallDir = Join-Path (Split-Path $PSScriptRoot -Parent) "netvan"
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

function Test-IsAdministrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-InstallDirectoryInUse {
    param([string]$InstallDirectory)

    $netvanExe = Join-Path $InstallDirectory "netvan.exe"
    if ((Test-Path $netvanExe) -and -not (Test-FileUnlocked -Path $netvanExe)) {
        return $true
    }

    return $false
}

function Wait-NetvanServiceStatus {
    param(
        [string]$ServiceName,
        [ValidateSet("Running", "Stopped")]
        [string]$DesiredStatus,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $service) {
            return $DesiredStatus -eq "Stopped"
        }

        if ($service.Status.ToString() -eq $DesiredStatus) {
            return $true
        }

        Start-Sleep -Milliseconds 250
    }

    return $false
}

function Get-NetvanWindowsServices {
    $services = @()
    foreach ($serviceName in $NetvanServiceNames) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service) {
            $services += $service
        }
    }

    return $services
}

function Test-NetvanServiceInstalled {
    return (Get-NetvanWindowsServices).Count -gt 0
}

function Invoke-ScCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$ScArguments,
        [string]$FailureContext
    )

    $output = & sc.exe @ScArguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        return $true
    }

    $detail = ($output | Out-String).Trim()
    $commandText = "sc.exe $($ScArguments -join ' ')"
    if ($detail) {
        Write-Host "[WARN] $commandText failed: $detail" -ForegroundColor Yellow
    }
    elseif ($FailureContext) {
        Write-Host "[WARN] $FailureContext" -ForegroundColor Yellow
    }

    return $false
}

function Stop-NetvanWindowsService {
    param(
        [string]$InstallDirectory,
        [switch]$FailIfNotStopped
    )

    $services = @(Get-NetvanWindowsServices)
    if ($services.Count -eq 0) {
        return $true
    }

    $netvanExe = Join-Path $InstallDirectory "netvan.exe"
    if (Test-Path $netvanExe) {
        & $netvanExe service stop 2>&1 | Out-Null
    }

    $allStopped = $true
    foreach ($service in $services) {
        $serviceName = $service.Name
        Write-Info "Stopping '$serviceName' Windows service (Netvan)..."

        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        $null = Invoke-ScCommand -ScArguments @("stop", $serviceName) -FailureContext "Could not stop '$serviceName' via sc.exe."

        if (Wait-NetvanServiceStatus -ServiceName $serviceName -DesiredStatus Stopped) {
            Write-Success "Stopped '$serviceName' service."
        }
        else {
            $allStopped = $false
            if ($FailIfNotStopped) {
                Write-Error-Exit "Could not stop '$serviceName'. Re-run export.ps1 in an elevated PowerShell window (Run as administrator)."
            }

            Write-Host "[WARN] '$serviceName' did not stop in time; files may stay locked." -ForegroundColor Yellow
        }
    }

    return $allStopped
}

function Remove-NetvanWindowsServiceRegistration {
    param(
        [string]$InstallDirectory,
        [switch]$FailIfPresent
    )

    if (-not (Test-NetvanServiceInstalled)) {
        return $true
    }

    $netvanExe = Join-Path $InstallDirectory "netvan.exe"
    if (Test-Path $netvanExe) {
        & $netvanExe service uninstall 2>&1 | Out-Null
    }

    $maxAttempts = 5
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        $services = @(Get-NetvanWindowsServices)
        if ($services.Count -eq 0) {
            Write-Success "Removed Netvan Windows service registration."
            return $true
        }

        foreach ($service in $services) {
            $serviceName = $service.Name
            Write-Info "Removing existing '$serviceName' Windows service registration..."

            $null = Invoke-ScCommand -ScArguments @("stop", $serviceName) -FailureContext "Could not stop '$serviceName' before delete."
            Start-Sleep -Seconds 1
            $null = Invoke-ScCommand -ScArguments @("delete", $serviceName) -FailureContext "Could not delete '$serviceName' service registration."
        }

        if (-not (Test-NetvanServiceInstalled)) {
            Write-Success "Removed Netvan Windows service registration."
            return $true
        }

        Start-Sleep -Seconds 1
    }

    if ($FailIfPresent) {
        Write-Error-Exit "Could not remove Netvan Windows service registration. Re-run export.ps1 in an elevated PowerShell window (Run as administrator)."
    }

    Write-Host "[WARN] Could not remove Netvan Windows service registration. Re-run export.ps1 as Administrator." -ForegroundColor Yellow
    return $false
}

function Test-FileUnlocked {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $true
    }

    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::None)
        $stream.Dispose()
        return $true
    }
    catch {
        return $false
    }
}

function Wait-ForFileUnlocked {
    param(
        [string]$Path,
        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-FileUnlocked -Path $Path) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Stop-NetvanProcessesInDirectory {
    param([string]$InstallDirectory)

    $netvanExe = Join-Path $InstallDirectory "netvan.exe"
    $netmExe = Join-Path $InstallDirectory "netm.exe"
    $resolvedExe = $null

    if (Test-Path $netvanExe) {
        $resolvedExe = [System.IO.Path]::GetFullPath($netvanExe)
        Write-Info "Stopping netvan processes using $resolvedExe..."
        & $resolvedExe stop 2>$null | Out-Null
        & $resolvedExe service stop 2>$null | Out-Null
    }
    elseif (Test-Path $netmExe) {
        $resolvedExe = [System.IO.Path]::GetFullPath($netmExe)
        Write-Info "Stopping legacy netm processes using $resolvedExe..."
    }
    else {
        return
    }

    $stoppedAny = $false
    $processes = @(
        Get-CimInstance Win32_Process -Filter "Name = 'netvan.exe'" -ErrorAction SilentlyContinue
        Get-CimInstance Win32_Process -Filter "Name = 'netm.exe'" -ErrorAction SilentlyContinue
    )
    foreach ($proc in $processes) {
        $shouldStop = $false

        if ($proc.ExecutablePath) {
            try {
                $procPath = [System.IO.Path]::GetFullPath($proc.ExecutablePath)
                $shouldStop = $procPath -ieq $resolvedExe
            }
            catch {
                $shouldStop = $false
            }
        }
        elseif ($proc.CommandLine -and $proc.CommandLine -like "*$resolvedExe*") {
            $shouldStop = $true
        }

        if ($shouldStop) {
            Write-Info "Stopping netvan.exe (PID $($proc.ProcessId))..."
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
            $stoppedAny = $true
        }
    }

    foreach ($proc in @(
            @(Get-Process -Name netvan -ErrorAction SilentlyContinue)
            @(Get-Process -Name netm -ErrorAction SilentlyContinue)
        )) {
        $procPath = $null
        try {
            $procPath = $proc.Path
        }
        catch {
            # Path is unavailable without elevation; handled by fallback below.
        }

        if ($procPath) {
            try {
                if ([System.IO.Path]::GetFullPath($procPath) -ieq $resolvedExe) {
                    Write-Info "Stopping netvan.exe (PID $($proc.Id))..."
                    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                    $stoppedAny = $true
                }
            }
            catch {
                continue
            }
        }
    }

    if (-not $stoppedAny -and (Test-Path $netvanExe) -and -not (Test-FileUnlocked -Path $netvanExe)) {
        $running = @(Get-Process -Name netvan -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) {
            Write-Host "[WARN] Stopping $($running.Count) netvan process(es) so install files can be replaced..." -ForegroundColor Yellow
            foreach ($proc in $running) {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                if (Test-IsAdministrator) {
                    $null = & taskkill.exe /F /PID $proc.Id 2>&1
                }
            }
        }
    }

    Start-Sleep -Milliseconds 500
}

function Assert-CanReplaceInstallDirectory {
    param([string]$InstallDirectory)

    if (-not (Test-InstallDirectoryInUse -InstallDirectory $InstallDirectory)) {
        return
    }

    if (Test-IsAdministrator) {
        return
    }

    $netvanExe = Join-Path $InstallDirectory "netvan.exe"
    $services = @(Get-NetvanWindowsServices)
    $runningCount = @(Get-Process -Name netvan -ErrorAction SilentlyContinue).Count

    $reason = if ($services | Where-Object { $_.Status -ne "Stopped" }) {
        "The Netvan Windows service is installed and must be stopped before export can replace files."
    }
    elseif ($runningCount -gt 0) {
        "A netvan.exe process (PID may require elevation to see) is still running and has $netvanExe open."
    }
    else {
        "Install files in $InstallDirectory are locked."
    }

    Write-Error-Exit "$reason Re-run export.ps1 in an elevated PowerShell window (Run as administrator)."
}

function Remove-NetvanServiceIfPresent {
    param(
        [string]$InstallDirectory,
        [switch]$FailIfServiceRemains
    )

    $netvanExe = Join-Path $InstallDirectory "netvan.exe"

    if (Test-NetvanServiceInstalled) {
        $null = Stop-NetvanWindowsService -InstallDirectory $InstallDirectory -FailIfNotStopped:$FailIfServiceRemains
        $null = Remove-NetvanWindowsServiceRegistration -InstallDirectory $InstallDirectory -FailIfPresent:$FailIfServiceRemains
    }

    Stop-NetvanProcessesInDirectory -InstallDirectory $InstallDirectory

    $repoRoot = Split-Path $PSScriptRoot -Parent
    $guiSource = Join-Path $repoRoot "netvan-gui"
    if (Test-Path $guiSource) {
        Stop-NetvanGuiProcesses -InstallDirectory $InstallDirectory -GuiSourceDirectory $guiSource
    }

    if ((Test-Path $netvanExe) -and -not (Wait-ForFileUnlocked -Path $netvanExe)) {
        if ($FailIfServiceRemains) {
            Write-Error-Exit "$netvanExe is still in use. Re-run export.ps1 in an elevated PowerShell window (Run as administrator)."
        }

        Write-Host "[WARN] $netvanExe is still in use. Re-run export.ps1 as Administrator or stop Netvan manually." -ForegroundColor Yellow
    }
}

function Clear-InstallDirectory {
    param([string]$Dir)

    if (-not (Test-Path $Dir)) {
        return
    }

    $netvanExe = Join-Path $Dir "netvan.exe"
    $maxAttempts = 5

    Write-Info "Deleting all files in $Dir..."
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            Get-ChildItem -Path $Dir -Force | Remove-Item -Recurse -Force -ErrorAction Stop
            Write-Success "Cleared install directory."
            return
        }
        catch {
            if ($attempt -eq $maxAttempts) {
                Assert-CanReplaceInstallDirectory -InstallDirectory $Dir
                throw
            }

            Write-Host "[WARN] Could not delete all files (attempt $attempt/$maxAttempts): $($_.Exception.Message)" -ForegroundColor Yellow
            Stop-NetvanProcessesInDirectory -InstallDirectory $Dir
            if (Test-Path $netvanExe) {
                $null = Wait-ForFileUnlocked -Path $netvanExe -TimeoutSeconds 5
            }

            Start-Sleep -Seconds 1
        }
    }
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

    [Environment]::SetEnvironmentVariable("NETVAN_HOME", $Dir, "User")
    Write-Success "Set NETVAN_HOME environment variable to $Dir."

    $env:Path = if ($env:Path -like "*$Dir*") { $env:Path } else { "$env:Path;$Dir" }
    $env:NETVAN_HOME = $Dir

    Write-Info "Restart your terminal for PATH/NETVAN_HOME to apply in new sessions."
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
# Netvan Configuration

# Database path (default: %LocalAppData%\Netvan\traffic.db)
database_path = "%NETVAN_HOME%\\traffic.db"

# Monitoring settings
[monitoring]
# Enable/disable monitoring
enabled = true
# Sampling interval in seconds
sampling_interval = 1

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
log_file = "%NETVAN_HOME%\\netvan.log"
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

function Test-ProcessPathUnderDirectory {
    param(
        [string]$ProcessPath,
        [string]$DirectoryPath
    )

    if ([string]::IsNullOrWhiteSpace($ProcessPath) -or [string]::IsNullOrWhiteSpace($DirectoryPath)) {
        return $false
    }

    try {
        $resolvedProcessPath = [System.IO.Path]::GetFullPath($ProcessPath)
        $resolvedDirectory = [System.IO.Path]::GetFullPath($DirectoryPath).TrimEnd('\')
        return $resolvedProcessPath.StartsWith("$resolvedDirectory\", [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Stop-NetvanGuiProcesses {
    param(
        [string]$InstallDirectory,
        [string]$GuiSourceDirectory,
        [switch]$StopUnscopedElectronGui
    )

    $scopedDirectories = @()
    if ($InstallDirectory) {
        $scopedDirectories += (Join-Path $InstallDirectory "gui")
        $scopedDirectories += $InstallDirectory
    }
    if ($GuiSourceDirectory) {
        $scopedDirectories += $GuiSourceDirectory
        $scopedDirectories += (Join-Path $GuiSourceDirectory "dist" "win-unpacked")
    }

    foreach ($proc in @(Get-Process -Name Netvan -ErrorAction SilentlyContinue)) {
        $procPath = $null
        try {
            $procPath = $proc.Path
        }
        catch {
            # Path may be unavailable without elevation.
        }

        $shouldStop = $StopUnscopedElectronGui
        if (-not $shouldStop -and $procPath) {
            foreach ($directory in $scopedDirectories) {
                if (Test-ProcessPathUnderDirectory -ProcessPath $procPath -DirectoryPath $directory) {
                    $shouldStop = $true
                    break
                }
            }
        }

        if ($shouldStop) {
            Write-Info "Stopping Netvan GUI (PID $($proc.Id))..."
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            if (Test-IsAdministrator) {
                $null = & taskkill.exe /F /PID $proc.Id /T 2>&1
            }
        }
    }

    $electronProcesses = @(
        Get-CimInstance Win32_Process -Filter "Name = 'electron.exe'" -ErrorAction SilentlyContinue
    )
    foreach ($proc in $electronProcesses) {
        $commandLine = $proc.CommandLine
        $executablePath = $proc.ExecutablePath
        $shouldStop = $false

        if ($StopUnscopedElectronGui) {
            $shouldStop = $true
        }
        else {
            foreach ($directory in $scopedDirectories) {
                if (($commandLine -and $commandLine -like "*$directory*") -or
                    (Test-ProcessPathUnderDirectory -ProcessPath $executablePath -DirectoryPath $directory)) {
                    $shouldStop = $true
                    break
                }
            }
        }

        if ($shouldStop) {
            Write-Info "Stopping electron.exe (PID $($proc.ProcessId))..."
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
            if (Test-IsAdministrator) {
                $null = & taskkill.exe /F /PID $proc.ProcessId /T 2>&1
            }
        }
    }

    Start-Sleep -Milliseconds 500
}

function Clear-GuiBuildOutput {
    param(
        [string]$GuiSourceDirectory,
        [string]$InstallDirectory,
        [string]$OutputDirectoryName = "dist-build",
        [int]$MaxAttempts = 5
    )

    $outputDir = Join-Path $GuiSourceDirectory $OutputDirectoryName
    if (-not (Test-Path $outputDir)) {
        return
    }

    $appAsar = Join-Path $outputDir "win-unpacked" "resources" "app.asar"
    Write-Info "Clearing previous GUI build output..."

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if ((Test-Path $appAsar) -and -not (Test-FileUnlocked -Path $appAsar)) {
            Write-Host "[WARN] GUI build files are locked (attempt $attempt/$MaxAttempts); stopping Netvan GUI processes..." -ForegroundColor Yellow
            Stop-NetvanGuiProcesses -InstallDirectory $InstallDirectory -GuiSourceDirectory $GuiSourceDirectory -StopUnscopedElectronGui
            $null = Wait-ForFileUnlocked -Path $appAsar -TimeoutSeconds 5
        }

        try {
            Remove-Item -Path $outputDir -Recurse -Force -ErrorAction Stop
            Write-Success "Cleared previous GUI build output."
            return
        }
        catch {
            if ($attempt -eq $MaxAttempts) {
                Write-Error-Exit "Could not clear GUI build output at $outputDir. Close Netvan GUI, any dev Electron session, and Explorer windows showing that folder, then re-run export.ps1."
            }

            Stop-NetvanGuiProcesses -InstallDirectory $InstallDirectory -GuiSourceDirectory $GuiSourceDirectory -StopUnscopedElectronGui
            Start-Sleep -Seconds 1
        }
    }
}

function Install-NetvanGui {
    param([string]$TargetDir)

    $repoRoot = Split-Path $PSScriptRoot -Parent
    $guiSource = Join-Path $repoRoot "netvan-gui"
    if (-not (Test-Path $guiSource)) {
        Write-Host "[WARN] netvan-gui not found at $guiSource; skipping GUI installation." -ForegroundColor Yellow
        return
    }

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Host "[WARN] npm not found; skipping GUI installation. Install Node.js and re-run export.ps1 to include the GUI." -ForegroundColor Yellow
        return
    }

    Stop-NetvanGuiProcesses -InstallDirectory $TargetDir -GuiSourceDirectory $guiSource
    Clear-GuiBuildOutput -GuiSourceDirectory $guiSource -InstallDirectory $TargetDir

    Write-Info "Building Netvan GUI (Electron)..."
    $originalLocation = Get-Location
    try {
        Set-Location $guiSource

        if (-not (Test-Path "node_modules")) {
            Write-Info "Installing GUI dependencies..."
            npm install
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[WARN] npm install failed; skipping GUI installation." -ForegroundColor Yellow
                return
            }
        }

        npm run build
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[WARN] GUI build failed; skipping GUI installation." -ForegroundColor Yellow
            return
        }
    }
    finally {
        Set-Location $originalLocation
    }

    $unpackedDir = Join-Path $guiSource "dist-build" "win-unpacked"
    if (-not (Test-Path $unpackedDir)) {
        Write-Host "[WARN] GUI build output not found at $unpackedDir; skipping GUI installation." -ForegroundColor Yellow
        return
    }

    $guiTarget = Join-Path $TargetDir "gui"
    if (Test-Path $guiTarget) {
        Remove-Item -Path $guiTarget -Recurse -Force
    }

    Copy-Item -Path $unpackedDir -Destination $guiTarget -Recurse -Force
    Write-Success "GUI installed to $guiTarget"
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

$isAdmin = Test-IsAdministrator
$hasExistingInstall = (Test-Path $InstallDir) -and ((Get-ChildItem -Path $InstallDir -Force -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)
$serviceInstalled = Test-NetvanServiceInstalled

if (-not $SkipService -and -not $isAdmin) {
    Write-Error-Exit "Export installs and starts the Netvan Windows service by default. Re-run export.ps1 in an elevated PowerShell window (Run as administrator), or pass -SkipService to publish binaries only."
}

if (-not $isAdmin -and ($hasExistingInstall -or $serviceInstalled)) {
    $runningServices = @(Get-NetvanWindowsServices | Where-Object { $_.Status -notin @("Stopped", "StopPending") })
    $runningProcesses = @(
        Get-Process -Name netvan -ErrorAction SilentlyContinue
        Get-Process -Name netm -ErrorAction SilentlyContinue
    ).Count -gt 0

    if ($runningServices.Count -gt 0 -or $runningProcesses -or (Test-InstallDirectoryInUse -InstallDirectory $InstallDir)) {
        Write-Error-Exit "An existing Netvan install or Windows service is still active. Re-run export.ps1 in an elevated PowerShell window (Run as administrator) so the service can be stopped, removed, and replaced."
    }
}

$failIfServiceRemains = -not $SkipService
Remove-NetvanServiceIfPresent -InstallDirectory $InstallDir -FailIfServiceRemains:$failIfServiceRemains
Assert-CanReplaceInstallDirectory -InstallDirectory $InstallDir
Clear-InstallDirectory -Dir $InstallDir

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Write-Info "Created install directory: $InstallDir"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error-Exit ".NET SDK not found. Install .NET 9 SDK, then run export.ps1 again."
}

$LocalProject = Join-Path $PSScriptRoot "Netvan.csproj"
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
Install-NetvanGui -TargetDir $InstallDir
Update-InstallEnvironmentVariables -Dir $InstallDir

$NetvanExe = Join-Path $InstallDir "netvan.exe"
$SqliteExe = Join-Path $InstallDir "sqlite3.exe"
$ConfigFile = Join-Path $InstallDir "configs.toml"

if (-not $SkipService) {
    if (-not (Test-Path $NetvanExe)) {
        Write-Error-Exit "Installation failed: netvan.exe not found at $NetvanExe"
    }

    if (Test-NetvanServiceInstalled) {
        Write-Info "Existing Netvan service registration detected after publish; removing before reinstall..."
        $null = Stop-NetvanWindowsService -InstallDirectory $InstallDir -FailIfNotStopped
        $null = Remove-NetvanWindowsServiceRegistration -InstallDirectory $InstallDir -FailIfPresent
    }

    $DbPath = Join-Path $InstallDir "traffic.db"
    Write-Info "Installing Netvan Windows service (interval ${ServiceInterval}s)..."
    & $NetvanExe service install --db $DbPath --interval $ServiceInterval
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Exit "netvan service install failed with exit code $LASTEXITCODE"
    }

    Write-Info "Starting Netvan Windows service..."
    & $NetvanExe service start
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Exit "netvan service start failed with exit code $LASTEXITCODE"
    }

    Write-Success "Netvan Windows service installed and started."
}

if (Test-Path $NetvanExe) {
    Write-Success "Installation complete! Files installed to: $InstallDir"
    Write-Info "Installed files:"
    Write-Info "  - netvan.exe (launches GUI when run with no arguments; CLI with subcommands)"
    $guiExe = Join-Path $InstallDir "gui\Netvan.exe"
    if (Test-Path $guiExe) {
        Write-Info "  - gui\Netvan.exe (Electron GUI)"
    }
    if (Test-Path $SqliteExe) {
        Write-Info "  - sqlite3.exe (SQLite CLI tool)"
    }
    if (Test-Path $ConfigFile) {
        Write-Info "  - configs.toml (Configuration file)"
    }
    Write-Info ""
    Write-Info "Run 'netvan info' to verify the installation"
    Write-Info "export.ps1 installs the Windows service by default (elevated). Use -SkipService to install binaries only."
    Write-Info "Use 'sqlite3.exe' to manage the SQLite database directly"
}
else {
    Write-Error-Exit "Installation failed: netvan.exe not found at $NetvanExe"
}
