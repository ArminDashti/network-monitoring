# Network Monitor (`netm`)

A **Windows** console application that tracks **TCP** network usage over time: which applications send and receive data, which remote IPs and hostnames they talk to, and which network adapter (NIC) carries the traffic. Samples are stored in a local **SQLite** database. Start the background collector with `netm start` and query history with `netm usage`, `netm apps list`, and `netm rt`.

## Installation

### From GitHub Releases (Recommended)

Download and run the installer script from an elevated PowerShell terminal:

```powershell
# Install latest version to %LocalAppData%\NetM with configs.toml
iwr -useb https://raw.githubusercontent.com/ArminDashti/network-monitoring/main/install.ps1 | iex

# Install specific version
iwr -useb https://raw.githubusercontent.com/ArminDashti/network-monitoring/main/install.ps1 | iex -Args "-Version v1.0.0"

# Install and add to PATH, set NETM_HOME environment variable
iwr -useb https://raw.githubusercontent.com/ArminDashti/network-monitoring/main/install.ps1 | iex -Args "-SetEnvVars"
```

The installer will:
- Create `%LocalAppData%\NetM` folder
- Download `netm.exe` to that folder
- Download `sqlite3.exe` (SQLite CLI tool) to that folder
- Create a default `configs.toml` configuration file
- Optionally add the folder to your PATH and set `NETM_HOME` environment variable

After installation with `-SetEnvVars`, you can use:
- `netm` command directly in PowerShell
- `sqlite3.exe` to manage the database directly
- `$env:NETM_HOME` to access the installation directory

### Build from Source

```powershell
# Clone and build from source
iwr -useb https://raw.githubusercontent.com/ArminDashti/network-monitoring/main/install.ps1 | iex -Args "-BuildFromSource"

# Build from source and configure environment variables
iwr -useb https://raw.githubusercontent.com/ArminDashti/network-monitoring/main/install.ps1 | iex -Args "-BuildFromSource", "-SetEnvVars"
```

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/ArminDashti/network-monitoring/releases)
2. Extract `netm.exe` and `configs.toml` to `%LocalAppData%\NetM`
3. Optionally add the installation directory to your PATH and set `NETM_HOME` environment variable

## Commands

Only these subcommands are exposed:

| Command | Purpose |
|---------|---------|
| `netm start` | Start background traffic collector |
| `netm stop` | Stop background collector |
| `netm status` | Collector PID, uptime, database stats |
| `netm service` | Install/start/stop the NetM Windows service (daemon mode — required for collection) |
| `netm info` | Database path, row counts, UTC coverage, version |
| `netm usage` | Upload, download, and total bytes in a time range |
| `netm apps list` | Application names seen in the database |

### Options (usage)

| Option | Default | Description |
|--------|---------|-------------|
| `--target` | *(omit)* | What to measure (see targets below) |
| `--from-datetime` | today `T0000` | Range start (local) |
| `--to-datetime` | now | Range end (local), inclusive |
| `--include-private` | `no` | `yes` to include RFC1918/link-local traffic |
| `--db` / `-d` | `%LocalAppData%\NetM\traffic.db` (from `configs.toml`) | SQLite path |

### Datetime format

Local time in compact form: **`yyMMddTHHmm`** (example: `260515T1430` = 2026-05-15 14:30).

- Omit the time portion (`yyMMdd` only) → **`T0000`** (midnight).
- `260515T` with no digits after `T` → **`T0000`**.

### `--target` values

| Target | Behavior |
|--------|----------|
| *(not set)* | Combined totals for **all apps** |
| `apps` | Breakdown by application (all apps, sorted by usage) |
| `ip` | Top **100** remote IPs by usage |
| `host` | Top **100** hostnames by usage |
| `<app>` | One process name, e.g. `telegram` |
| `<x.x.x.x>` | One remote IP |
| `<hostname>` | One host, e.g. `example.com` or `sub.example.com` |

### `apps list`

```powershell
netm apps list
netm apps list --filter=chrom
```

## Background collector

After install, start recording traffic:

```powershell
netm start    # spawn background collector
netm status   # PID, uptime, row counts
netm stop     # stop collector
```

Configuration lives in `%LocalAppData%\NetM\configs.toml` (or `%NETM_HOME%\configs.toml` when set). Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `monitoring.sampling_interval` | `5` | Seconds between TCP samples |
| `storage.retention_days` | `30` | Delete usage rows older than this |
| `storage.max_size_mb` | `500` | Prune when the database file exceeds this size |

Logs: `%NETM_HOME%\netm.log` (see `logging.log_file` in config).

## Examples

```powershell
netm start
netm status
netm info

netm usage
netm usage --from-datetime=260515 --to-datetime=260515T2359
netm usage --target=apps --from-datetime=260515T0900
netm usage --target=ip --include-private=no
netm usage --target=telegram --from-datetime=260514T0000

netm apps list --filter=edge
```

## Data collection (daemon mode)

Collection runs only as a **Windows service** in the background (no foreground collector).

```powershell
# Elevated PowerShell
netm service install
netm service start
netm service status
```

`install.ps1` installs and starts the service by default when run as Administrator. Use `-SkipService` to install binaries only.

```powershell
iwr -useb https://raw.githubusercontent.com/ArminDashti/network-monitoring/main/install.ps1 | iex -Args "-SetEnvVars"
```

Stop or remove the service:

```powershell
netm service stop
netm service uninstall
```

The service runs `netm run` internally. Logs are written to the Windows Event Log (source **NetM**).

Query traffic in any terminal: `netm usage`, `netm apps list`, `netm rt`.

Default database: `%LocalAppData%\NetM\traffic.db` (override with `--db` on `service install` or `NETM_HOME`).

## Quick start (from source)

```powershell
git clone https://github.com/ArminDashti/network-monitoring.git
cd network-monitoring
# Elevated PowerShell for daemon install:
.\install.ps1 -BuildFromSource -SetEnvVars
# Restart terminal, then:
netm service status
netm info
netm usage --target=apps
```

`netm start` remains available for local background collection (`netm collect`), while `netm service` is the recommended production daemon mode.

## Requirements

- **Windows 11** (or compatible Windows with IP Helper ESTATS APIs)
- **.NET 9** SDK to build from source, or a published **`netm.exe`** artifact
- **Administrator** (for `netm service install`): the Windows service runs as Local System and can read per-connection TCP byte counters for other processes

## Limitations

| Topic | Behavior |
|-------|----------|
| **Protocol** | **TCP only** (IPv4 and IPv6). UDP and QUIC-only flows are not counted. |
| **Byte type** | Application **TCP payload** bytes per connection, not full Ethernet frames. |
| **Hostnames** | From **reverse DNS** on the remote IP, not TLS SNI or HTTP Host. |
| **Private IPs** | Excluded by default (`--include-private=no`). |

## Build

```powershell
dotnet build NetworkMonitor\NetworkMonitor.csproj -c Release
```

Published **`netm-win-x64`** artifacts are built in CI (`.github/workflows/publish-windows-exe.yml`).

## Project layout

| Path | Role |
|------|------|
| `NetworkMonitor/Program.cs` | CLI (`start`, `stop`, `status`, `info`, `usage`, `apps`, `rt`) |
| `NetworkMonitor/Services/CollectionLoop.cs` | Background sampling loop |
| `NetworkMonitor/Services/DaemonManager.cs` | PID file and process control |
| `NetworkMonitor/Services/` | Background runner, Windows service host, and traffic collection |
| `NetworkMonitor/Cli/` | Target parsing and datetime helpers |
| `NetworkMonitor/Services/TrafficCollector.cs` | TCP sampling and deltas |
| `NetworkMonitor/Storage/TrafficStore.cs` | SQLite schema and queries |

## NuGet restore

Repository `nuget.config` clears disabled package feeds so **`nuget.org`** restores correctly in restricted environments.
