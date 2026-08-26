# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Rebuilt for Phase 11F (Phase 11 release closeout) after the full Phase 11A–11E persistent Operational
Notes work landed — see `docs/release-strategy.md` "Phase 11F rebuild" for the narrative. This is the
single source of truth for exactly what was released; `docs/release-strategy.md` has the full
narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, the Post-Phase-8 rebuild, the Phase 9F
rebuild, the Phase 10F rebuild, and this rebuild).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild) |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `96e6c3b48be265cac444eb96c1aba6ea6c7214b0561a97a05dd039a474f0767b` |
| Compressed size | 61,385,910 bytes (58.5 MiB) |
| Extracted size | 139,878,942 bytes (133.4 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-26 |
| Current schema version | 10 (`Migration0010CreateOperationalNotesTable`, additive atop Migrations 1–9 — unchanged since Phase 11A) |
| AssociateDraftPayload schema version | 3 (adds `SelectedOperationalNotes` — unchanged since Phase 11D) |
| Snapshot schema version | 1 (unchanged by Phase 11 — a selected persistent note is frozen into the existing generic `OperationalNotes` string list, not a new structured field) |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| Automatic finalization | Unchanged from the prior (Phase 10F) artifact: every eligible passdown finalizes automatically at/after 6:00 AM Eastern the following day, always before that invocation's scheduled/catch-up refresh. No manual finalization control exists. |
| Passdown History | Unchanged from the prior (Phase 10F) artifact: top-level, read-only "History" section listing every finalized passdown; opens/saves the exact frozen PDF via stored bytes only, never regenerated |
| Operations Dashboard | Unchanged from the prior (Phase 10F) artifact, except its "Active Operational Notes" panel now surfaces real persistent-note data (see below) |
| Operational Notes | **New this rebuild (Phase 11A–11E)**: a supervisor-facing "Operational Notes" top-level section for persistent, multi-day notes (create/edit/resolve, lifecycle/category/priority filters), a read-only "Active Operational Notes" summary on the Dashboard, and an explicit, per-date inclusion workflow on Associate Passdown's Notes area. Persistent notes are **never automatically added to a daily passdown** — a supervisor must explicitly select one for a given date; the selected copy is frozen at that moment and does not silently resync if the source note changes later, except via an explicit "Replace with Current Version" action. Finalization and PDF generation use only the frozen selected copy, never a live query back to the persistent note. |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database, all draft/note content (including persistent Operational Notes titles/bodies and selected-copy content), the Dashboard's own live source-status detail text, and all finalized-passdown snapshot/PDF content (unchanged by Phase 11 — `Application/Diagnostics/DiagnosticReportBuilder.cs`/`Infrastructure/Diagnostics/DiagnosticExportService.cs` were not touched) |
| Non-live automated test count | 1242/1242 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — up from 1240/1240 at the start of this rebuild (982/982 at the prior, Phase 10F, artifact); 66 Domain + 351 Application + 96 Documents + 317 Infrastructure + 412 Ui. This rebuild added 2 Documents regression tests closing a Part M audit gap (explicit "select → edit source without replacing → finalize → snapshot shows OLD text" / "select → explicitly Replace → finalize → snapshot shows NEW text" proof); none were removed, skipped, or weakened |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E's full manual pass ran against the original Phase 7B/7C/7D/7E artifact (an ARM64-emulated Windows 10 host, not native x64), not this one. This rebuild's Phase 11 Operational Notes work is covered by the automated suite above (including real-XAML-loader construction checks for every representative Notes Manager/Associate-inclusion/Dashboard state), but has **not been run on a real Windows machine** — no Windows host was available this session. 100%/125%/150% Windows display-scaling validation for Operational Notes is **NOT EXECUTED**. See `docs/release-strategy.md` "Phase 11F rebuild" for the full gap list |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Phase 9F, Phase 10F, and Phase 11F Windows-validation and visual-validation gaps noted in their respective rebuild sections |

## How this artifact was produced

Built manually on this (non-Windows, macOS) dev host, mirroring exactly what
`packaging/windows/Publish-Windows.ps1` + `packaging/windows/Build-PortableRelease.ps1` do — no
`pwsh` is available on this dev host (same limitation recorded in every prior Phase 7/9/10 sub-phase
and rebuild):

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64`
2. Delete stray `.pdb` files from the publish output.
3. Stage into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/`: the publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, `Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`, `README.txt` (from `README-portable-release.md`), and `TESTER-GUIDE.txt`.
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`, preview/screenshot-harness artifacts) — clean.
5. `zip -r -X -9` the staging folder into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.
7. The ZIP was extracted fresh to a scratch directory and re-inspected independently of the staging folder — a `diff -rq` against the staging folder confirmed byte-for-byte identical content, and `Carowinds.Passdown.Desktop.dll`'s SHA-256 inside the extracted ZIP was independently confirmed to match the staged copy.

Before rebuilding, the freshly published `Carowinds.Passdown.Application.dll`/`.Infrastructure.dll`/
`.Desktop.dll` were checked directly (a byte-string search of the actual built assemblies via
`strings`, not just timestamps) for symbols unique to Phase 11A–11E source that did not exist in the
Phase 10F artifact: `OperationalNotesService` (7 occurrences), `OperationalNotesViewModel` (158
occurrences), `DashboardOperationalNotesSummary` (2), `AssociateOperationalNoteSelection` (2),
`ReplaceOperationalNoteWithCurrentAsync` (6), `OperationalNoteSourceComparison` (3),
`Migration0010CreateOperationalNotesTable` (2) — all present in the freshly published output, proving
this publish actually contains the Phase 11 Operational Notes code, not a stale reuse of the Phase 10F
artifact.

`README.txt`/`TESTER-GUIDE.txt` were updated this rebuild (Phase 11F) to add an "Operational Notes"
section/step describing the Notes Manager, the Dashboard's "Active Operational Notes" summary, and the
explicit per-date inclusion workflow (available suggestions → Add → frozen copy → source-changed
indicator → Replace with Current Version → Remove), before being staged.

This supersedes the Phase 10F artifact (SHA-256
`279693228b16ba201b702b188139f6dc5fdde23579e0eeae7e02bb085cae0f98`, 277 files / 280 entries, 61,331,017
compressed / 139,629,103 extracted bytes, recorded above in the prior revision of this table as the
artifact produced after the Phase 10A–10E Operations Dashboard work). The functional change between
the two: the entire Phase 11A–11E persistent Operational Notes feature — a new "Operational Notes"
top-level section and Notes Manager UI, a read-only Dashboard summary, an explicit per-date passdown
inclusion workflow with frozen selection copies, and category/priority filtering plus source-change
awareness with an explicit "Replace with Current Version" action. No retrieval, business-rule,
finalization/History-immutability, or existing Associate-editing behavior changed; the database schema
moved from 9 to 10 (`Migration0010CreateOperationalNotesTable`, additive only) and
`AssociateDraftPayload`'s schema moved from an earlier version to 3 (adds `SelectedOperationalNotes`,
additive only, pre-Phase-11 drafts load with zero selections) — both already shipped as part of the
Phase 11A/11D work landing on `main`, not new to this rebuild; `FinalizedPassdownSnapshot`'s schema
stays at 1. No Manager workflow, Alerts, or Operations Calendar was added.

The Phase 9F artifact (SHA-256 `ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`),
the Post-Phase-8 artifact (SHA-256 `eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31`),
the Post-Phase-7F artifact (SHA-256 `ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`),
the Phase 7F artifact (SHA-256 `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`), and
the original Phase 7B/7C/7D/7E artifact (SHA-256
`94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) remain further superseded, retained
only as historical validation information (the last of these is what Phase 7E's manual Windows pass
actually validated).

**Note on a discrepancy in the Phase 10F task brief (retained for history):** the task instructions
that requested the Phase 10F rebuild stated the artifact being superseded had SHA-256
`1684f9146534a7ed7f58cf9e4124cd558aabc6f5f8121dc91135ff46a426ba71`. That hash did not match any
artifact recorded in this repository's history — the actual prior-current artifact at that time was
the Phase 9F artifact, SHA-256 `ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`. That
document then correctly superseded the real prior hash, not the one named in that task brief; the
discrepancy was flagged rather than silently reconciled, per CLAUDE.md's "never invent data"
instruction — it applies to release identity, not just retrieved park data. For this (Phase 11F)
rebuild, the pre-existing `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip` file on disk
(timestamped 2026-08-26, matching this document's own pre-rebuild "Phase 10F" hash record) was
overwritten by the new build without first independently re-hashing it — `artifacts/` is gitignored
and not tracked in version control, so that specific prior file cannot be recovered or re-verified
now. This document's own prior revision is the authoritative record of what that Phase 10F hash was;
no discrepancy with the task brief was noted for this rebuild.
