## Learned User Preferences

- Prefer local `dotnet publish` via `netvan-core\export.ps1` over GitHub release downloads; CI/workflows were removed from this repo.
- Install and redeploy through `netvan-core\export.ps1` (successor to `install.ps1`); default output is `.\netvan`, override with `--path=<dir>` or `-InstallDir`.
- When narrowing scope ("we only need X" or "remove all codes belonging to"), delete obsolete commands and code paths instead of leaving them in place.
- Interactive CLI views such as `netvan rt` must refresh in real time, not print one-shot snapshots.
- Keep `COMMANDS.md` as the maintained canonical command reference.
- Ask the user to install external software manually rather than installing it in the agent environment.

## Learned Workspace Facts

- Windows TCP usage monitor (`netvan`): IP Helper eStats collection, SQLite storage, Spectre.Console CLI (`info`, `usage`, `apps`, `rt`, `service` commands).
- Traffic collection runs only via the Netvan Windows service (`netvan service install` / `start`).
- Storage buckets are fixed at 1 second (`TrafficStore.BucketIntervalSeconds`); default sampling interval is 1s.
- Build targets are `net9.0` (portable queries) and `net9.0-windows` (TCP collection).
- Data directory defaults to the install dir via `NETVAN_HOME`; legacy fallback is `%LocalAppData%\Netvan\`.
- Deep implementation notes live in `.cursor/key-points.mdc`; avoid duplicating that file in agent responses.
