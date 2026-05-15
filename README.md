# Network Monitor (`netm`)

A **Windows** console application that tracks **TCP** network usage over time: which applications send and receive data, which remote IPs and hostnames they talk to, and which network adapter (NIC) carries the traffic. Samples are stored in a local **SQLite** database so you can query totals, time ranges, and breakdowns without keeping the collector running.

## What it can do

### Monitor live TCP traffic

Run the **`collect`** command to sample active TCP connections on a fixed interval (default: every 5 seconds). For each connection, the tool:

- Reads per-connection byte counters from the Windows **IP Helper** API (`GetExtendedTcpTable` and `GetPerTcpConnectionEStats`)
- Attributes traffic to the **owning process** (application name)
- Maps the local IP to a **NIC** name (e.g. Ethernet, Wi‑Fi)
- Resolves the remote IP to a **hostname** when possible (cached reverse DNS)
- Computes **deltas** since the last sample and writes them into SQLite, bucketed by UTC minute

While collecting, the console prints a short status line each interval (connection count and volume in that interval). Press **Ctrl+C** to stop.

### Store history in SQLite

All collected data goes to a single database file (default):

`%LocalAppData%\NetworkMonitor\traffic.db`

You can override the path with `--db` / `-d`. The schema keeps per-minute aggregates keyed by application, NIC, remote IP, port, and hostname, so reports and usage queries work offline after collection stops.

### Lifetime reports (all time in the database)

The **`report`** command prints **cumulative** upload, download, and total bytes since you started collecting (not limited to a date range):

| Mode | What you see |
|------|----------------|
| `ip` | Traffic grouped by **remote IP** (optional filter) |
| `nic` | Traffic grouped by **network adapter** (optional filter) |
| `host` | Traffic grouped by **hostname** / site label (optional filter; aliases: `website`, `site`) |

Use `--top` / `-n` to limit how many rows are printed (default: 25).

### Time-range usage queries

The **`usage`** command answers “how much was used between these times?” using the minute buckets in the database. Date/time boundaries are **local time** in `yyyy-MM-dd'T'HH:mm:ss` format.

| Command | Purpose |
|---------|---------|
| `usage` | Total upload, download, and combined bytes in the range |
| `usage download` | Download (received) bytes only |
| `usage upload` | Upload (sent) bytes only |
| `usage app` | Breakdown by **application** (process name); filter one app with `--app` |
| `usage ip` | Breakdown by **remote IP**; optional `--app` filter |

Defaults if you omit `--from-datetime` / `--to-datetime`: from **today at 00:00:00** through **now** (local).

## Requirements

- **Windows 11** (or compatible Windows with the same IP Helper APIs)
- **.NET 10** SDK to build from source, or use a published **`netm.exe`** artifact
- **Administrator / elevated** prompt recommended for **`collect`**: some connections may not expose ESTATS counters without elevation

## Limitations (important)

Understanding what is **not** measured helps interpret results:

| Topic | Behavior |
|-------|----------|
| **Protocol** | **TCP only** (IPv4 and IPv6). **UDP** and traffic that never appears as TCP (e.g. some **QUIC-only** flows) are **not** counted. |
| **Byte type** | Application **TCP payload** bytes per connection, not full Ethernet/Wi‑Fi frame sizes. |
| **“Websites”** | Hostnames come from **reverse DNS** on the remote IP, not from TLS **SNI** or HTTP Host headers. Many IPs will show as raw addresses or generic labels. |
| **Reports vs usage** | `report` is **all-time** totals in the DB; `usage` is **time-ranged** from collected samples. You must run `collect` first to populate data. |

## Quick start

### Build

```powershell
dotnet build NetworkMonitor\NetworkMonitor.csproj -c Release
```

### Collect

```powershell
# Default DB under %LocalAppData%\NetworkMonitor\traffic.db
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- collect --interval 5

# Custom database path
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- collect -i 10 --db .\traffic.db
```

### Reports (lifetime)

```powershell
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- report ip --top 20
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- report nic ["Wi-Fi"]
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- report host [google]
```

### Usage (time range)

```powershell
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- usage
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- usage download --from-datetime 2026-05-15T09:00:00
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- usage app --app chrome
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- usage ip --db .\traffic.db
```

### Published executable

CI builds a self-contained **`netm.exe`** for **win-x64** on pushes that touch the project (see `.github/workflows/publish-windows-exe.yml`). Download the **`netm-win-x64`** artifact from the workflow run and run the same subcommands (`collect`, `report`, `usage`, …) without installing the SDK.

Example:

```powershell
.\netm.exe collect -i 5
.\netm.exe report host --top 15
.\netm.exe usage --from-datetime 2026-05-15T00:00:00
```

## Project layout

| Path | Role |
|------|------|
| `NetworkMonitor/Program.cs` | CLI commands and handlers |
| `NetworkMonitor/Services/TrafficCollector.cs` | TCP sampling and delta calculation |
| `NetworkMonitor/Native/IpHelperApi.cs` | P/Invoke to Windows IP Helper |
| `NetworkMonitor/Storage/TrafficStore.cs` | SQLite schema and queries |
| `NetworkMonitor.sln` | Solution file |

## NuGet restore

Repository `nuget.config` clears disabled package feeds so **`nuget.org`** restores correctly in restricted or corporate environments.

## License

See repository defaults; add a `LICENSE` file if you distribute binaries publicly.
