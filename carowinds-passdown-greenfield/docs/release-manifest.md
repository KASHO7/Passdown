# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Rebuilt for Phase 10F (Phase 10 release closeout) after the full Phase 10A–10E Operations Dashboard
work landed — see `docs/release-strategy.md` "Phase 10F rebuild" for the narrative. This is the
single source of truth for exactly what was released; `docs/release-strategy.md` has the full
narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, the Post-Phase-8 rebuild, the Phase 9F
rebuild, and this rebuild).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild) |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `279693228b16ba201b702b188139f6dc5fdde23579e0eeae7e02bb085cae0f98` |
| Compressed size | 61,331,017 bytes (58.5 MiB) |
| Extracted size | 139,629,103 bytes (133.2 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-26 |
| Current schema version | 9 (unchanged by Phase 10 — `Migration0008CreateFinalizedPassdownsTable` + `Migration0009AddFinalizedPassdownPdfColumns`, additive atop Migrations 1–7) |
| Snapshot schema version | 1 (unchanged by Phase 10) |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| Automatic finalization | Unchanged from the prior (Phase 9F) artifact: every eligible passdown finalizes automatically at/after 6:00 AM Eastern the following day, always before that invocation's scheduled/catch-up refresh. No manual finalization control exists. |
| Passdown History | Unchanged from the prior (Phase 9F) artifact: top-level, read-only "History" section listing every finalized passdown; opens/saves the exact frozen PDF via stored bytes only, never regenerated |
| Operations Dashboard | **New this rebuild (Phase 10A–10E)**: a "Dashboard" top-level section, and the app's new default startup screen, summarizing today's (America/New_York) Park Status, Passdown Status, Today at a Glance (attendance/pre-solds/Early Ride Time/Daily Feature), an Operational Notes preview, the latest finalized passdown, and a Data Status card aggregating source freshness across all 7 categories with an explicit "Check Online" action. Dashboard load/Reload is local-only; only "Check Online" contacts official sources, reusing the existing Associate retrieval path. Dashboard's date is always "Today" and is independent of whatever date Associate Passdown has selected. "Open Passdown"/"Open Notes"/"Open History" route to the corresponding screen without disturbing Associate's in-progress state. |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database, all draft/note content, the Dashboard's own live source-status detail text, and all finalized-passdown snapshot/PDF content (unchanged by Phase 10 — `Application/Diagnostics/DiagnosticReportBuilder.cs`/`Infrastructure/Diagnostics/DiagnosticExportService.cs` were not touched) |
| Non-live automated test count | 982/982 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — up from 858/858 at the prior (Phase 9F) artifact; 66 Domain + 256 Application + 82 Documents + 287 Infrastructure + 291 Ui. Phase 10A–10E added the Dashboard's read model/service/view-model/view tests; none were removed, skipped, or weakened |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E's full manual pass ran against the original Phase 7B/7C/7D/7E artifact (an ARM64-emulated Windows 10 host, not native x64), not this one. This rebuild's Phase 10 Dashboard is covered by the automated suite above (including real-XAML-loader construction checks for every representative Dashboard state), but has **not been run on a real Windows machine** — no Windows host was available this session. 100%/125%/150% Windows display-scaling validation for the Dashboard is **NOT EXECUTED**. See `docs/release-strategy.md` "Phase 10F rebuild" for the full gap list |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Phase 9F and Phase 10F Windows-validation and visual-validation gaps noted in their respective rebuild sections |

## How this artifact was produced

Built manually on this (non-Windows, macOS) dev host, mirroring exactly what
`packaging/windows/Publish-Windows.ps1` + `packaging/windows/Build-PortableRelease.ps1` do — no
`pwsh` is available on this dev host (same limitation recorded in every prior Phase 7/9 sub-phase and
rebuild):

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64`
2. Delete stray `.pdb` files from the publish output.
3. Stage into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/`: the publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, `Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`, `README.txt` (from `README-portable-release.md`), and `TESTER-GUIDE.txt`.
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`, preview/screenshot-harness artifacts) — clean.
5. `zip -r -X -9` the staging folder into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.
7. The ZIP was extracted fresh to a scratch directory and re-inspected independently of the staging folder — a `diff -rq` against the staging folder confirmed byte-for-byte identical content, and `Carowinds.Passdown.Desktop.dll`'s SHA-256 inside the extracted ZIP was independently confirmed to match the staged copy.

Before rebuilding, the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll` were
checked directly (a byte-string search of the actual built assemblies, not just timestamps) for
symbols unique to Phase 10A–10E source that did not exist in the Phase 9F artifact:
`OperationsDashboardSnapshot`, `OperationsDashboardService`, `OperationsDashboardViewModel` (46
occurrences), `DashboardView` (49 occurrences), `DashboardDataHealthPresentation` (25 occurrences),
`ShowDashboardSection`, `CheckOnlineCommand` (Dashboard's own), `DashboardOverallDataStatus` — all
present in both the pre-zip publish output and the independently re-extracted ZIP — proving this
publish actually contains the Phase 10 Dashboard code, not a stale reuse of the Phase 9F artifact.

`README.txt`/`TESTER-GUIDE.txt` were updated this rebuild (Phase 10F Parts T/U) to describe the new
Dashboard as the default landing screen and its Open Passdown/Open Notes/Open History/Check Online
actions, before being staged.

This supersedes the Phase 9F artifact (SHA-256
`ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`, 277 files / 280 entries, 61,303,566
compressed / 139,519,155 extracted bytes, still recorded above in the prior revision of this table as
the artifact produced after the Phase 9A–9E finalization/History/frozen-PDF work). The functional
change between the two: the entire Phase 10A–10E Operations Dashboard — a new "Dashboard" top-level
section and default startup screen, Park Status/Passdown Status/Today at a Glance/Operational Notes
preview, latest-finalized History integration, a 5-state Data Status aggregate over all 7 source
categories with an explicit Check Online action, and the accompanying visual/accessibility polish. No
retrieval, business-rule, finalization/History-immutability, or Associate-editing behavior changed; no
schema migration was added (schema stays 9, snapshot schema stays 1); no Manager workflow, Alerts, or
Operations Calendar was added.

**Note on a discrepancy in the Phase 10F task brief:** the task instructions that requested this
rebuild stated the artifact being superseded had SHA-256
`1684f9146534a7ed7f58cf9e4124cd558aabc6f5f8121dc91135ff46a426ba71`. That hash does not match any
artifact recorded in this repository's history — the actual prior-current artifact (verified both from
this file's own pre-rebuild content and by hashing the checked-in
`artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip` directly before rebuilding it) was the
Phase 9F artifact, SHA-256 `ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`. This
document now correctly supersedes the real prior hash, not the one named in the task brief; the
discrepancy is flagged here rather than silently reconciled, per CLAUDE.md's "never invent data"
instruction — it applies to release identity, not just retrieved park data.

The Post-Phase-8 artifact (SHA-256 `eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31`),
the Post-Phase-7F artifact (SHA-256 `ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`),
the Phase 7F artifact (SHA-256 `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`), and
the original Phase 7B/7C/7D/7E artifact (SHA-256
`94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) remain further superseded, retained
only as historical validation information (the last of these is what Phase 7E's manual Windows pass
actually validated).
