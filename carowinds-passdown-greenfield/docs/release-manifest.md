# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Phase 7F final checkpoint. This is the single source of truth for exactly what was released;
`docs/release-strategy.md` has the full narrative history (Phases 7A–7F).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy") |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57` |
| Compressed size | 61,258,219 bytes (58.4 MiB) |
| Extracted size | 139,336,742 bytes (132.9 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database and all draft/note content |
| Non-live automated test count | 684/684 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E manual pass completed (see "Known limitations" below for accepted confidence gaps); Phase 7F's own code changes (diagnostic export) were reviewed and automated-tested on this dev host but **not independently re-run on a real Windows machine** (no Windows host available this phase) — see `docs/release-strategy.md` Phase 7F "Windows recheck status" |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps" |

## How this artifact was produced

Built manually on this (non-Windows, Fedora/macOS) dev host, mirroring exactly what
`packaging/windows/Publish-Windows.ps1` + `packaging/windows/Build-PortableRelease.ps1` do — no
`pwsh` is available on this dev host (same limitation recorded in every prior Phase 7 sub-phase):

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64`
2. Delete stray `.pdb` files from the publish output.
3. Stage into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/`: the publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, `Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`, `README.txt` (from `README-portable-release.md`), and `TESTER-GUIDE.txt` (new this phase).
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`) — clean.
5. `zip -r -X -9` the staging folder into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.

This supersedes the Phase 7B/7C/7D/7E artifact (SHA-256
`94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) — the only functional change
between the two is the addition of the diagnostic export feature (Application/Infrastructure/Desktop
code, one new "Export Diagnostics" button) and the new `TESTER-GUIDE.txt`. No retrieval, scheduling,
persistence, or PDF behavior changed.
