# netm Command List

This file provides a quick reference for the `netm` CLI commands.

## Available Commands

| Command | Description |
|---|---|
| `netm service install` | Install the NetM Windows service (Administrator required). |
| `netm service uninstall` | Remove the NetM Windows service (Administrator required). |
| `netm service start` | Start the NetM Windows service (daemon collection). |
| `netm service stop` | Stop the NetM Windows service. |
| `netm service status` | Show Windows service status. |
| `netm info` | Database path, coverage, and version |
| `netm usage` | Upload, download, and total bytes in a time range |
| `netm apps` | Application names from collected traffic. |
| `netm apps list` | List application names seen in the database |
| `netm rt` | Real-time usage table by app with daily/weekly/monthly totals |

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
```
