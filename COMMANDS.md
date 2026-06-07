# netm Command List

This file provides a quick reference for the `netm` CLI commands.

## Available Commands

| Command | Description |
|---|---|
| `netm start` | Start the background traffic collector. |
| `netm stop` | Stop the background traffic collector. |
| `netm status` | Show whether the collector is running (PID, uptime, DB stats). |
| `netm reset` | Remove the traffic database file, then restart the collector/service so in-memory counters start fresh. |
| `netm service install` | Install the NetM Windows service (Administrator required). |
| `netm service uninstall` | Remove the NetM Windows service (Administrator required). |
| `netm service start` | Start the NetM Windows service (daemon collection). |
| `netm service stop` | Stop the NetM Windows service. |
| `netm service status` | Show Windows service status. |
| `netm info` | Show database path, row counts, UTC time coverage, and app version. |
| `netm usage` | Show upload, download, and total bytes for the selected time window. |
| `netm usage download` | Show only download (received) bytes for the selected time window. |
| `netm usage upload` | Show only upload (sent) bytes for the selected time window. |
| `netm apps list` | List application names observed in the traffic database. |
| `netm rt` | Live per-app table (refreshes automatically) with download/upload plus daily, weekly, and monthly totals. Press Ctrl+C to exit. |
| `netm taskbar enable` | Show upload/download speeds in the Windows 11 taskbar (starts on login). |
| `netm taskbar disable` | Remove the taskbar speed widget and stop auto-start. |

## Common Options

| Option | Default | Description |
|---|---|---|
| `--target` | *(unset)* | Target scope: all apps, `apps`, `ip`, `host`, or a specific app/IP/hostname. |
| `--from-datetime` | `today T0000` | Local datetime start (`yyMMddTHHmm`). |
| `--to-datetime` | `now` | Local datetime end (`yyMMddTHHmm`, inclusive). |
| `--include-private` | `no` | Set `yes` to include RFC1918/link-local traffic. |
| `--db`, `-d` | `%LocalAppData%\NetM\traffic.db` | Path to SQLite database file. |
### `service install` options

| Option | Default | Description |
|---|---|---|
| `--interval`, `-i` | `5` | Seconds between TCP samples. |
| `--db`, `-d` | `%LocalAppData%\NetM\traffic.db` | Path to SQLite database file. |

## Examples

```powershell
netm start
netm status
netm stop
netm reset
# Daemon mode (required for collection)
netm service install
netm service start
netm service status
netm service stop
netm service uninstall
netm info
netm usage
netm usage --target=apps --from-datetime=260515T0900
netm usage --target=ip --include-private=no
netm usage --target=telegram --from-datetime=260514T0000
netm apps list --filter=edge
netm rt
netm taskbar enable
netm taskbar disable
```
