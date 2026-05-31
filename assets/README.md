# SQLite CLI bundles

Pre-downloaded [SQLite command-line tools](https://www.sqlite.org/download.html) for offline installs.

| File | Architecture |
|------|----------------|
| `sqlite-tools-win-x64-3530100.zip` | Windows x64 |
| `sqlite-tools-win-arm64-3530100.zip` | Windows ARM64 |

`export.ps1` picks `assets/sqlite-tools-win-<arch>*.zip` matching the host CPU and extracts `sqlite3.exe` into the install directory.

To refresh after a new SQLite release, download the matching zip from https://www.sqlite.org/download.html into this folder (keep the `sqlite-tools-win-<arch>-<version>.zip` naming).
