# Netvan (`netvan`)

A **Windows** console application that tracks **TCP** network usage over time: which applications send and receive data, which remote IPs and hostnames they talk to, and which network adapter (NIC) carries the traffic. Samples are stored in a local **SQLite** database. Traffic is collected by the Netvan Windows service; query history with `netvan usage`, `netvan apps list`, and `netvan rt`.

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
- `netvan` command directly in PowerShell
- `sqlite3.exe` to manage the database directly
- `$env:NETVAN_HOME` to access the installation directory

## Commands

Only these subcommands are exposed:

| Command | Purpose |
|---------|---------|
| `netvan service` | Install/start/stop the Netvan Windows service (required for collection) |
| `netvan reset` | Remove the traffic database and restart the service (fresh in-memory counters) |
| `netvan info` | Database path, row counts, time coverage, version |
| `netvan usage` | Upload, download, and total bytes in a time range |
| `netvan apps list` | Application names seen in the database |
| `netvan rt` | Real-time usage table by app |

### Options (usage)

| Option | Default | Description |
|--------|---------|-------------|
| `--target` | *(omit)* | What to measure (see targets below) |
| `--from-datetime` | today `T0000` | Range start (local) |
| `--to-datetime` | now | Range end (local), inclusive |
| `--include-private` | `no` | `yes` to include RFC1918/link-local traffic |
| `--db` / `-d` | `%LocalAppData%\Netvan\traffic.db` (from `configs.toml`) | SQLite path |

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
netvan apps list
netvan apps list --filter=chrom
```

## Configuration

Configuration lives in `%NETVAN_HOME%\configs.toml` (or `%LocalAppData%\Netvan\configs.toml` when `NETVAN_HOME` is not set). Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `monitoring.sampling_interval` | `1` | Seconds between TCP samples |
| `storage.retention_days` | `30` | Delete usage rows older than this |
| `storage.max_size_mb` | `500` | Prune when the database file exceeds this size |

Logs: `%NETVAN_HOME%\netvan.log` (see `logging.log_file` in config).

## Examples

```powershell
netvan service status
netvan info

netvan usage
netvan usage --from-datetime=260515 --to-datetime=260515T2359
netvan usage --target=apps --from-datetime=260515T0900
netvan usage --target=ip --include-private=no
netvan usage --target=telegram --from-datetime=260514T0000

netvan apps list --filter=edge
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

Query traffic in any terminal: `netvan usage`, `netvan apps list`, `netvan rt`.

Default database: `%LocalAppData%\Netvan\traffic.db` (override with `--db` on `service install` or `NETVAN_HOME`).

## Quick start

```powershell
git clone https://github.com/ArminDashti/netvan.git
cd network-monitoring
# Elevated PowerShell for service install:
.\netvan-core\export.ps1 -SetEnvVars
# Restart terminal, then:
netvan service status
netvan info
netvan usage --target=apps
```

## Requirements

- **Windows 11** (or compatible Windows with IP Helper ESTATS APIs)
- **.NET 9** SDK to build from source, or a published **`netvan.exe`** artifact
- **Administrator** (for `netvan service install`): the Windows service runs as Local System and can read per-connection TCP byte counters for other processes

## Limitations

| Topic | Behavior |
|-------|----------|
| **Protocol** | **TCP only** (IPv4 and IPv6). UDP and QUIC-only flows are not counted. |
| **Byte type** | Application **TCP payload** bytes per connection, not full Ethernet frames. |
| **Hostnames** | From **reverse DNS** on the remote IP, not TLS SNI or HTTP Host. |
| **Private IPs** | Excluded by default (`--include-private=no`). |

## Build

```powershell
dotnet build netvan-core\Netvan.csproj -c Release
```

To publish a self-contained Windows executable:

```powershell
dotnet publish netvan-core\Netvan.csproj -c Release -f net9.0-windows -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

## Project layout

| Path | Role |
|------|------|
| `netvan-core/` | C# CLI source, build scripts, and `export.ps1` |
| `netvan-gui/` | Electron GUI (`start-gui.ps1`) |
| `netvan/` | Published binaries ready to use (output of `export.ps1`) |
| `netvan-core/Program.cs` | CLI (`service`, `reset`, `info`, `usage`, `apps`, `rt`) |
| `netvan-core/Services/CollectionLoop.cs` | Background sampling loop |
| `netvan-core/Services/` | Windows service host and traffic collection |
| `netvan-core/Cli/` | Target parsing and datetime helpers |
| `netvan-core/Services/TrafficCollector.cs` | TCP sampling and deltas |
| `netvan-core/Storage/TrafficStore.cs` | SQLite schema and queries |

## NuGet restore

Repository `nuget.config` clears disabled package feeds so **`nuget.org`** restores correctly in restricted environments.
