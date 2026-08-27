# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Rebuilt for Phase 12F (Phase 12 Operations Calendar release closeout) after the full Phase 12A–12E
Operations Calendar work landed — see `docs/release-strategy.md` "Phase 12F rebuild" for the
narrative. This is the single source of truth for exactly what was released; `docs/release-strategy.md`
has the full narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, the Post-Phase-8 rebuild, the
Phase 9F rebuild, the Phase 10F rebuild, the Phase 11F rebuild, and this rebuild).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild — the app rename to "Carowinds Operations" is Phase 13 scope, not done here) |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `dcceafee15be7f30ba410d3bb68032bd3eba057833f4a4c910fe5ecbbc4d4946` |
| Compressed size | 61,409,645 bytes (58.6 MiB) |
| Extracted size | 139,980,429 bytes (133.5 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-26 |
| Current schema version | 10 (`Migration0010CreateOperationalNotesTable`, additive atop Migrations 1–9 — unchanged since Phase 11A; Phase 12A–12F introduced no migration) |
| AssociateDraftPayload schema version | 3 (adds `SelectedOperationalNotes` — unchanged since Phase 11D) |
| Snapshot schema version | 1 (unchanged since Phase 1) |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| Automatic finalization | Unchanged from the prior (Phase 11F) artifact: every eligible passdown finalizes automatically at/after 6:00 AM Eastern the following day, always before that invocation's scheduled/catch-up refresh. No manual finalization control exists. |
| Passdown History | Unchanged from the prior (Phase 11F) artifact: top-level, read-only "History" section listing every finalized passdown; opens/saves the exact frozen PDF via stored bytes only, never regenerated. Phase 12D added a clean date-based preselection seam (`PassdownHistoryViewModel.SelectByDateAsync`) so Operations Calendar's "Open History" lands on the matching finalized record — generic top-level "Open History" navigation (from Dashboard) is unaffected and still preserves whatever was already selected. |
| Operations Dashboard | Unchanged from the prior (Phase 11F) artifact |
| Operational Notes | Unchanged from the prior (Phase 11F) artifact: a supervisor-facing "Operational Notes" top-level section for persistent, multi-day notes, a read-only "Active Operational Notes" summary on the Dashboard, and an explicit, per-date inclusion workflow on Associate Passdown's Notes area. Persistent notes are never automatically added to a daily passdown. |
| Operations Calendar | **New this rebuild (Phase 12A–12E)**: a fifth top-level "Operations Calendar" section (`Dashboard \| Operations Calendar \| Associate Passdown \| Operational Notes \| History`) — a month-grid planning/routing surface, local-only and non-mutating. Shows, per day cell: park operating-state/hours (Open/Closed/Custom Hours/Private Buyout/Not Published/Unavailable, plus a stale/last-known-good indicator), passdown lifecycle (No Draft/Draft/Finalized, with Finalized always taking precedence over an existing draft), and relevant/Important persistent Operational Notes counts. Supports Previous/Next Month (day-of-month preserved with clamping), Previous/Next Day, Go to Today, and off-month-cell selection (moves the displayed month) — always exactly one bounded local load per navigation action, never a per-cell query. "Open Passdown" routes to Associate Passdown at the selected date (flushing any pending autosave first); "Open History" is enabled only once the selected date is Finalized and preselects the matching History record; "Open Operational Notes" is generic navigation. No editing control of any kind exists on this screen. |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database, all draft/note content, the Dashboard's own live source-status detail text, and all finalized-passdown snapshot/PDF content (unchanged by Phase 12 — Operations Calendar introduces no new diagnostic surface; it has no note-body, draft-content, or source-status text of its own to leak) |
| Non-live automated test count | 1395/1395 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — unchanged from the Phase 12E baseline (1242/1242 at the start of Phase 12A); 66 Domain + 400 Application + 96 Documents + 317 Infrastructure + 516 Ui. Phase 12F's audit found no genuine defect, so no regression test was added this rebuild — the count is the accumulated Phase 12A–12E total (153 new tests across 12A–12E), reconfirmed green |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E's full manual pass ran against the original Phase 7B/7C/7D/7E artifact (an ARM64-emulated Windows 10 host, not native x64), not this one. Operations Calendar is covered by the automated suite above (including real-XAML-loader construction checks and `Measure`/`Arrange` layout passes at 1280×860 and 960×640 for every representative state), but has **not been run on a real Windows machine** — no Windows host was available this session. 100%/125%/150% Windows display-scaling validation is **NOT EXECUTED**. See `docs/release-strategy.md` "Phase 12F rebuild" for the full gap list |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Phase 9F, Phase 10F, Phase 11F, and Phase 12F Windows-validation and visual-validation gaps noted in their respective rebuild sections |

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

Before rebuilding, the freshly published `Carowinds.Passdown.Application.dll`/`.Desktop.dll` were
checked directly (a byte-string search of the actual built assemblies via `strings`, not just
timestamps) for symbols unique to Phase 12A–12E Operations Calendar source: `OperationsCalendarViewModel`
(48 occurrences), `OperationsCalendarService` (5), `OperationsCalendarOperatingState` (3),
`OperationsCalendarPassdownStatus` (2), `SelectByDateAsync` (1), `PreviousDayCommand` (7),
`NextDayCommand` (7), `OpenHistoryHelpText` (5), `SetDisplayedMonthAndSelectedDate` (1),
`OperationsCalendarNotesPresentation` (1) — all present in the freshly published output, proving this
publish actually contains the Phase 12 Operations Calendar code, not a stale reuse of the Phase 11F
artifact.

`TESTER-GUIDE.txt`/`README.txt` were updated this rebuild (Phase 12F) to add an "Operations Calendar"
step/section describing month navigation, Today, Previous/Next Day, off-month-date selection, park-hours
overlays, passdown-lifecycle indicators, Operational Notes counts, and the Open Passdown/Open
History/Open Operational Notes routing actions. Steps 1–7 above were run once against a first
staging pass, then re-run for steps 3/5/6/7 alone (re-copying the two updated doc files into staging,
re-zipping, re-hashing, and re-verifying the extraction) after those two files were edited — the
SHA-256/sizes recorded in the table above are from this final pass, the one actually shipped.

This supersedes the Phase 11F artifact (SHA-256
`96e6c3b48be265cac444eb96c1aba6ea6c7214b0561a97a05dd039a474f0767b`, 277 files / 280 entries, 61,385,910
compressed / 139,878,942 extracted bytes, recorded above in the prior revision of this table as the
artifact produced after the Phase 11A–11E persistent Operational Notes work). The functional change
between the two: the entire Phase 12A–12E Operations Calendar feature — a new fifth top-level
"Operations Calendar" section (month-grid planning/routing surface), park-hours/operating-state
overlays reusing the existing hours engine, passdown-lifecycle and persistent-notes-count overlays with
Finalized-over-Draft precedence, month/day navigation with day-of-month-preserving clamping, and a
History date-preselection seam. No retrieval, business-rule, finalization/History-immutability,
Operational Notes, or existing Associate-editing behavior changed; no schema migration was introduced —
database schema stays 10, `AssociateDraftPayload` schema stays 3, `FinalizedPassdownSnapshot` schema
stays 1 (all confirmed directly in code, not merely carried forward from documentation). No Manager
workflow or Alerts was added; the app rename to "Carowinds Operations" remains Phase 13 scope.

The Phase 10F artifact (SHA-256 `279693228b16ba201b702b188139f6dc5fdde23579e0eeae7e02bb085cae0f98`),
the Phase 9F artifact (SHA-256 `ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`),
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
instruction — it applies to release identity, not just retrieved park data. For the Phase 11F rebuild,
the pre-existing `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip` file on disk was
overwritten by that rebuild without first independently re-hashing it — `artifacts/` is gitignored and
not tracked in version control, so that specific prior file could not be recovered or re-verified at
that time; this document's prior revision remained the authoritative record of what that Phase 10F hash
was. This (Phase 12F) rebuild's own SHA-256 above was computed directly against the final ZIP this
rebuild actually shipped (after the doc-update re-stage/re-zip pass described above), and independently
re-confirmed after a fresh extraction (see "How this artifact was produced" step 7) — no discrepancy was
found for this rebuild.
