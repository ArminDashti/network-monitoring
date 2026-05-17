# Network Monitor (`netm`)

A **Windows** console application that tracks **TCP** network usage over time: which applications send and receive data, which remote IPs and hostnames they talk to, and which network adapter (NIC) carries the traffic. Samples are stored in a local **SQLite** database so you can query totals and time ranges without keeping a collector running.

## Installation

### From GitHub Releases (Recommended)

Download and run the installer script from an elevated PowerShell terminal:

```powershell
# Install latest version to %LocalAppData%\NetM with configs.toml
iwr -useb https://raw.githubusercontent.com/OWNER/NetworkMonitor/main/install.ps1 | iex

# Install specific version
iwr -useb https://raw.githubusercontent.com/OWNER/NetworkMonitor/main/install.ps1 | iex -Args "-Version v1.0.0"

# Install and add to PATH, set NETM_HOME environment variable
iwr -useb https://raw.githubusercontent.com/OWNER/NetworkMonitor/main/install.ps1 | iex -Args "-SetEnvVars"
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
iwr -useb https://raw.githubusercontent.com/OWNER/NetworkMonitor/main/install.ps1 | iex -Args "-BuildFromSource"

# Build from source and configure environment variables
iwr -useb https://raw.githubusercontent.com/OWNER/NetworkMonitor/main/install.ps1 | iex -Args "-BuildFromSource", "-SetEnvVars"
```

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/OWNER/NetworkMonitor/releases)
2. Extract `netm.exe` and `configs.toml` to `%LocalAppData%\NetM`
3. Optionally add the installation directory to your PATH and set `NETM_HOME` environment variable

## Commands

Only these subcommands are exposed:

| Command | Purpose |
|---------|---------|
| `netm info` | Database path, row counts, UTC coverage, version |
| `netm usage` | Upload, download, and total bytes in a time range |
| `netm usage download` | Download (received) bytes only |
| `netm usage upload` | Upload (sent) bytes only |
| `netm apps list` | Application names seen in the database |

### Options (usage)

| Option | Default | Description |
|--------|---------|-------------|
| `--target` | *(omit)* | What to measure (see targets below) |
| `--from-datetime` | today `T0000` | Range start (local) |
| `--to-datetime` | now | Range end (local), inclusive |
| `--include-private` | `no` | `yes` to include RFC1918/link-local traffic |
| `--db` / `-d` | `%LocalAppData%\NetM\traffic.db` or custom path | SQLite path |

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

## Examples

```powershell
netm info

netm usage
netm usage --from-datetime=260515 --to-datetime=260515T2359
netm usage --target=apps --from-datetime=260515T0900
netm usage download --target=ip --include-private=no
netm usage upload --target=telegram --from-datetime=260514T0000

netm apps list --filter=edge
```

## Data collection

Traffic must be recorded into the SQLite database before `usage` or `apps list` return data. Collection uses the same TCP/IP Helper pipeline as before (`TrafficCollector` writing minute buckets). Run your collector process or integration that populates `%LocalAppData%\NetM\traffic.db` (or a custom `--db` path).

## Requirements

- **Windows 11** (or compatible Windows with IP Helper ESTATS APIs)
- **.NET 10** SDK to build from source, or a published **`netm.exe`** artifact

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
| `NetworkMonitor/Program.cs` | CLI (`info`, `usage`, `apps`) |
| `NetworkMonitor/Cli/` | Target parsing and datetime helpers |
| `NetworkMonitor/Services/TrafficCollector.cs` | TCP sampling and deltas |
| `NetworkMonitor/Storage/TrafficStore.cs` | SQLite schema and queries |

## NuGet restore

Repository `nuget.config` clears disabled package feeds so **`nuget.org`** restores correctly in restricted environments.
