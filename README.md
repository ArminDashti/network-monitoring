# Netvan (`netvan`)

A **Windows** application that tracks **TCP** network usage over time: which applications send and receive data, which remote IPs and hostnames they talk to, and which network adapter (NIC) carries the traffic. Samples are stored in a local **SQLite** database. Traffic is collected by the Netvan Windows service; view and query history in the **Electron GUI**.

## Installation

From the repository root, run the installer in an elevated PowerShell terminal (Administrator required for the Windows service):

```powershell
# Build and publish to .\netvan
.\netvan-core\export.ps1

# Custom install directory
.\netvan-core\export.ps1 --path=C:\Tools\Netvan

# Binaries only (skip Windows service install)
.\netvan-core\export.ps1 -SkipService
```

The installer will:
- Stop the Netvan Windows service if it is running
- Delete all files in the install directory (default `.\netvan`, or `--path=<dir>`)
- Build and publish `netvan.exe` from the local source into that directory
- Copy `configs.toml` from the repository (or create defaults)
- Extract `sqlite3.exe` from the bundled SQLite tools zip in `netvan-core/assets/`
- Add the folder to your user PATH and set `NETVAN_HOME`
- Install and start the Netvan Windows service by default when run as Administrator

After installation, you can use:
- `netvan` to launch the GUI (no arguments)
- `netvan service` to manage the Windows service
- `sqlite3.exe` to manage the database directly
- `$env:NETVAN_HOME` to access the installation directory

## Commands

| Command | Purpose |
|---------|---------|
| `netvan` | Launch the Netvan GUI |
| `netvan service` | Install/start/stop the Netvan Windows service (required for collection) |
| `netvan reset` | Remove the traffic database and restart the service (fresh in-memory counters) |
| `netvan taskbar` | Enable/disable the taskbar upload/download speed widget |

## Configuration

Configuration lives in `%NETVAN_HOME%\configs.toml` (or `%LocalAppData%\Netvan\configs.toml` when `NETVAN_HOME` is not set). Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `monitoring.disable_vpn_tracking` | `false` | Exclude traffic on VPN adapters from collection |
| `storage.retention_days` | `30` | Delete usage rows older than this |
| `storage.max_size_mb` | `500` | Prune when the database file exceeds this size |

Logs: `%NETVAN_HOME%\netvan.log` (see `logging.log_file` in config).

## Examples

```powershell
netvan
netvan service status
netvan service start
netvan reset
netvan taskbar enable
```

## Data collection

Collection runs only as a **Windows service** in the background.

```powershell
# Elevated PowerShell
netvan service install
netvan service start
netvan service status
```

`netvan-core\export.ps1` installs and starts the service by default when run as Administrator. Use `-SkipService` to install binaries only.

```powershell
.\netvan-core\export.ps1 -SetEnvVars
```

Stop or remove the service:

```powershell
netvan service stop
netvan service uninstall
```

The service runs `netvan run` internally. Logs are written to the Windows Event Log (source **Netvan**).

Use the GUI for live views, usage queries, and database info. Default database: `%LocalAppData%\Netvan\traffic.db` (override with `--db` on `service install` or `NETVAN_HOME`).

## Quick start

```powershell
git clone https://github.com/ArminDashti/netvan.git
cd network-monitoring
# Elevated PowerShell for service install:
.\netvan-core\export.ps1 -SetEnvVars
# Restart terminal, then:
netvan service status
netvan
```

## Requirements

- **Windows 11** (or compatible Windows with IP Helper ESTATS APIs)
- **.NET 9** SDK to build from source, or a published **`netvan.exe`** artifact
- **Administrator** (for `netvan service install`): the Windows service runs as Local System and can read per-connection TCP byte counters for other processes

## Limitations

| Topic | Behavior |
|-------|----------|
| **Protocol** | **TCP over IPv4 only**. |
| **Byte type** | Application **TCP payload** bytes per connection, not full Ethernet frames. |
| **Hostnames** | From **reverse DNS** on the remote IP, not TLS SNI or HTTP Host. |
| **Private IPs** | Excluded by default in GUI queries. |

## Build

```powershell
dotnet build netvan-core\Netvan.csproj -c Release
```

To publish a self-contained Windows executable:

```powershell
dotnet publish netvan-core\Netvan.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

## Project layout

| Path | Role |
|------|------|
| `netvan-core/` | C# service host, build scripts, and `export.ps1` |
| `netvan-gui/` | Electron GUI (`start-gui.ps1`) |
| `netvan/` | Published binaries ready to use (output of `export.ps1`) |
| `netvan-core/Program.cs` | Entry point (`service`, `reset`, `taskbar`, GUI launcher) |
| `netvan-core/Services/CollectionLoop.cs` | Background sampling loop |
| `netvan-core/Services/` | Windows service host and traffic collection |
| `netvan-core/Services/TrafficCollector.cs` | TCP sampling and deltas |
| `netvan-core/Storage/TrafficStore.cs` | SQLite schema and queries |

## NuGet restore

Repository `nuget.config` clears disabled package feeds so **`nuget.org`** restores correctly in restricted environments.
