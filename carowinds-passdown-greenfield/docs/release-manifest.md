# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Rebuilt for Phase 9F (release closeout) after the full Phase 9A–9E finalization/History/frozen-PDF
work landed — see `docs/release-strategy.md` "Phase 9F rebuild" for the narrative. This is the
single source of truth for exactly what was released; `docs/release-strategy.md` has the full
narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, the Post-Phase-8 rebuild, and this
rebuild).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild) |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4` |
| Compressed size | 61,303,566 bytes (58.5 MiB) |
| Extracted size | 139,519,155 bytes (133.1 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-25 |
| Current schema version | 9 (`Migration0008CreateFinalizedPassdownsTable` + `Migration0009AddFinalizedPassdownPdfColumns`, additive atop Migrations 1–7) |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| Automatic finalization | New this rebuild (Phase 9B): every eligible passdown finalizes automatically at/after 6:00 AM Eastern the following day, always before that invocation's scheduled/catch-up refresh. No manual finalization control exists. |
| Passdown History | New this rebuild (Phase 9D): top-level, read-only "History" section listing every finalized passdown; opens/saves the exact frozen PDF (Phase 9C) via stored bytes only, never regenerated |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database, all draft/note content, and all finalized-passdown snapshot/PDF content |
| Non-live automated test count | 858/858 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — up from 725/725 at the prior (Post-Phase-8) artifact; 66 Domain + 221 Application + 202 Ui + 82 Documents + 287 Infrastructure. Phase 9A–9E added finalization/History/integration tests; none were removed, skipped, or weakened |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E's full manual pass ran against the original Phase 7B/7C/7D/7E artifact, not this one (see "Known limitations" in `docs/release-strategy.md` for its accepted confidence gaps). This rebuild's Phase 9 changes (automatic finalization, frozen PDF storage, the History UI) are covered by the automated suite above (including real-SQLite end-to-end integration tests, Phase 9E), but have **not been run on a real Windows machine** — no Windows host was available this session. A focused Windows smoke check of History/finalization (not a full Phase 7E rerun) is the recommended next step; see `docs/release-strategy.md` "Phase 9F rebuild" → "Focused Windows smoke check" |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Phase 9F Windows-validation and visual-validation gaps noted in "Phase 9F rebuild" |

## How this artifact was produced

Built manually on this (non-Windows, macOS) dev host, mirroring exactly what
`packaging/windows/Publish-Windows.ps1` + `packaging/windows/Build-PortableRelease.ps1` do — no
`pwsh` is available on this dev host (same limitation recorded in every prior Phase 7 sub-phase and
rebuild):

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64`
2. Delete stray `.pdb` files from the publish output.
3. Stage into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/`: the publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, `Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`, `README.txt` (from `README-portable-release.md`), and `TESTER-GUIDE.txt`.
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`, preview/screenshot-harness artifacts) — clean.
5. `zip -r -X -9` the staging folder into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.
7. The ZIP was extracted fresh to a scratch directory and re-inspected independently of the staging folder (not just trusted from the staging step).

Before rebuilding, the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll`/
`.Infrastructure.dll`/`.Documents.dll` were checked directly (a byte-string search of the actual
built assemblies, not just timestamps) for symbols unique to Phase 9A–9D source that did not exist
in the Post-Phase-8 artifact: `PassdownHistoryViewModel`, `ShowHistorySectionCommand`,
`OpenFinalPdfCommand`/`SavePdfCopyCommand`, `TempFileFinalizedPdfLauncher` (Desktop);
`ScheduledPassdownFinalizer`, `FinalizedPassdownFinalizationService`, `IPassdownHistoryService`,
`FinalizationEligibility` (Application); `SqliteFinalizedPassdownRepository`,
`Migration0008CreateFinalizedPassdownsTable`, `Migration0009AddFinalizedPassdownPdfColumns`
(Infrastructure); `IFinalizedPassdownSnapshotBuilder`/`BuildProjection` (Documents) — all present —
proving this publish actually contains the Phase 9 code, not a stale rebuild.

`README.txt`/`TESTER-GUIDE.txt` were also updated this rebuild (Phase 9F Parts P/Q) to briefly
describe the new Passdown History section before being staged — see `docs/release-strategy.md`
"Phase 9F rebuild" for the exact wording rationale.

This supersedes the Post-Phase-8 artifact (SHA-256
`eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31`, still recorded in
`docs/release-strategy.md` "Post-Phase-8 rebuild" as the historical artifact produced after the
Phase 8A/8B Associate Daily Feature/section-visibility/PDF-redesign pass). The functional change
between the two: the entire Phase 9A–9E finalization/History/frozen-PDF pipeline — automatic 6:00 AM
Eastern finalization (always before that invocation's scheduled refresh), an immutable
`finalized_passdowns` table (schema version 9), exact frozen PDF bytes stored alongside every
finalized snapshot, and a new top-level, read-only Passdown History screen. No retrieval,
business-rule, or Associate-editing behavior changed; no Manager workflow was added; no manual
finalization or delete-history control was added. The Post-Phase-7F artifact (SHA-256
`ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`), the Phase 7F artifact (SHA-256
`01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`), and the original Phase
7B/7C/7D/7E artifact (SHA-256 `94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`)
remain further superseded, retained only as historical validation information (the last of these is
what Phase 7E's manual Windows pass actually validated).
