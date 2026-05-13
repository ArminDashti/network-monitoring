# network-monitoring

Console tool for Windows that samples **TCP** per-connection byte counters (via `GetExtendedTcpTable` + `GetPerTcpConnectionEStats`), maps local IPs to NIC names, resolves remote IPs to hostnames (best-effort / reverse DNS), and stores rolling totals in **SQLite**.

## Build

```powershell
dotnet build NetworkMonitor\NetworkMonitor.csproj -c Release
```

## Usage

```powershell
# Collect (default DB: %LocalAppData%\NetworkMonitor\traffic.db)
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- collect --interval 5

# Reports (optional filter in brackets)
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- report ip [1.2.3.4] --db .\traffic.db
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- report nic ["Ethernet"]
dotnet run --project NetworkMonitor\NetworkMonitor.csproj -- report host [google] --top 20
```

Run `collect` from an elevated prompt if some connections return no ESTATS data. **UDP** and traffic that never appears as TCP (for example some **QUIC**) are not counted. “Websites” are inferred from resolved hostnames, not TLS SNI.

Repository `nuget.config` clears disabled feeds so `nuget.org` restores correctly in restricted environments.
