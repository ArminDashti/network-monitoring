# Network Monitor (`netm`)

A **Windows** console application that tracks **TCP** network usage over time: which applications send and receive data, which remote IPs and hostnames they talk to, and which network adapter (NIC) carries the traffic. Samples are stored in a local **SQLite** database so you can query totals and time ranges without keeping a collector running.

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
| `--db` / `-d` | `%LocalAppData%\NetworkMonitor\traffic.db` | SQLite path |

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

Traffic must be recorded into the SQLite database before `usage` or `apps list` return data. Collection uses the same TCP/IP Helper pipeline as before (`TrafficCollector` writing minute buckets). Run your collector process or integration that populates `%LocalAppData%\NetworkMonitor\traffic.db` (or a custom `--db` path).

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
