# netm Command List

This file provides a quick reference for the `netm` CLI commands.

## Available Commands

| Command | Description |
|---|---|
| `netm start` | Start the background traffic collector. |
| `netm stop` | Stop the background traffic collector. |
| `netm status` | Show whether the collector is running (PID, uptime, DB stats). |
| `netm info` | Show database path, row counts, UTC time coverage, and app version. |
| `netm usage` | Show upload, download, and total bytes for the selected time window. |
| `netm usage download` | Show only download (received) bytes for the selected time window. |
| `netm usage upload` | Show only upload (sent) bytes for the selected time window. |
| `netm apps list` | List application names observed in the traffic database. |
| `netm rt` | Show per-app real-time table with download/upload plus daily, weekly, and monthly totals. |

## Common Options

| Option | Default | Description |
|---|---|---|
| `--target` | *(unset)* | Target scope: all apps, `apps`, `ip`, `host`, or a specific app/IP/hostname. |
| `--from-datetime` | `today T0000` | Local datetime start (`yyMMddTHHmm`). |
| `--to-datetime` | `now` | Local datetime end (`yyMMddTHHmm`, inclusive). |
| `--include-private` | `no` | Set `yes` to include RFC1918/link-local traffic. |
| `--db`, `-d` | `%LocalAppData%\NetM\traffic.db` | Path to SQLite database file. |

## Examples

```powershell
netm start
netm status
netm stop
netm info
netm usage
netm usage --target=apps --from-datetime=260515T0900
netm usage download --target=ip --include-private=no
netm usage upload --target=telegram --from-datetime=260514T0000
netm apps list --filter=edge
netm rt
```
