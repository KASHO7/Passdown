# Release manifest — Carowinds Operations 0.1.0-dev (win-x64)

Rebuilt for Phase 13H (final Phase 13 integration audit and release closeout) after the full Phase
13A–13G hardening/rename work landed — see `docs/release-strategy.md` "Phase 13H rebuild" for the
narrative. This is the single source of truth for exactly what was released; `docs/release-strategy.md`
has the full narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, the Post-Phase-8 rebuild, the
Phase 9F rebuild, the Phase 10F rebuild, the Phase 11F rebuild, the Phase 12F rebuild, and this rebuild).

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild per the Phase 13H brief — "do not change version unless release strategy requires it") |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.zip` (new naming convention — supersedes the stale `Carowinds-Passdown-0.1.0-dev-win-x64.zip` name used through Phase 12F, per the Phase 13H brief) |
| SHA-256 | `dde45055424d140fe0a16eae079f24cddfbc9a699c7a06dd7e50f0c65f4a8f0d` |
| Compressed size | 62,586,155 bytes (59.7 MiB) |
| Extracted size | 141,557,778 bytes (135.0 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-27 |
| Current schema version | 11 (`Migration0011CreateDeepRefreshStatusTable`, introduced in Phase 13D for Deep Refresh status persistence, additive atop Migrations 1–10 — unchanged since 13D; Phase 13E–13G introduced no migration) |
| AssociateDraftPayload schema version | 3 (unchanged since Phase 11D) |
| Snapshot schema version | 1 (unchanged since Phase 1) |
| Executable | `Carowinds.Passdown.Desktop.exe` (internal identity intentionally retained — see "Branding vs. internal identity" below) |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task named `"Carowinds Passdown Daily Refresh"` — legacy name intentionally retained, see below; no admin, no permanent execution-policy change) |
| Automatic finalization | Unchanged: every eligible passdown finalizes automatically at/after 6:00 AM Eastern the following day, always before that invocation's scheduled/catch-up refresh. No manual finalization control exists. |
| Passdown History | Unchanged: top-level, read-only "History" section listing every finalized passdown; opens/saves the exact frozen PDF via stored bytes only, never regenerated. |
| Operations Dashboard | Unchanged |
| Operational Notes | Unchanged |
| Operations Calendar | Unchanged since Phase 12A–12F |
| Manual Hours / Private Buyout | **Phase 13E**: typable free-text time entry (e.g. "9", "900", "9:00a") replacing the prior spinner-only `TimePicker`, normalized/echoed on blur, with a blocking inline validation error replacing the old soft warning-text-only guard |
| Early Ride Time (ERT) | **Phase 13F**: derived start time = effective park opening − 30 minutes (North Gate opening), shown alongside the existing free-text Details/attractions fields; purely additive, no schema change |
| Refresh model | **Phase 13B**: explicit `RefreshMode` (`Scheduled` / `StartupCatchUp` / `UserForced`); both "Check Online" surfaces (Dashboard, Associate) now force-refresh every catalog family (Season Passes, Season Pass Add-Ons, General Add-Ons, Promotions) in addition to the four dated families — previously catalog families only refreshed on their rolling scheduled cadence |
| Commercial coverage / pricing | **Phase 13C**: daily commercial-refresh cadence and pricing-semantics vocabulary (Exact Dated / Starting From / Comparison) hardened; known limitation carried forward — see below |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted — legacy folder name intentionally retained, see below) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen) — produces `Carowinds-Operations-Diagnostics-YYYYMMDD-HHmm.zip` (renamed this rebuild's predecessor, Phase 13G) at a user-chosen location; local-only, excludes the database, all draft/note content, and all finalized-passdown snapshot/PDF content |
| Non-live automated test count | 1600/1600 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — unchanged from the Phase 13G baseline (Domain 105, Application 453, Documents 106, Infrastructure 352, Ui 584). No code changed this phase beyond two packaging-script text edits (see below), so no test count change. Reconfirmed clean on this rebuild's own runs — see "Test suite stability" below |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | **NOT EXECUTED.** No Windows host or VM was available this session — the same limitation recorded in every prior Phase 7/9/10/11/12/13G rebuild performed from this (macOS) dev host. 100%/125%/150% Windows display-scaling validation, live icon/taskbar rendering, and the full manual click-through checklist are all **NOT EXECUTED**. Confidence instead comes from: the full non-live automated suite (including real-XAML-loader construction checks and `Measure`/`Arrange` layout passes at 1280×860/960×640 for representative states across Dashboard/Calendar/Associate/History), a direct byte-string search of the freshly published `.exe`/`.dll`s confirming Phase 13 symbols and the `Carowinds Operations` Product attribute are actually present (not a stale reuse), and a byte-for-byte diff between the staged release folder and a fresh extraction of the shipped ZIP |
| Known limitations | Ordinary Bring-A-Friend exact dated pricing is not anonymously available behind passholder authentication (no credential/login automation was added, per policy) — carried forward from Phase 13C. See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Phase 9F, 10F, 11F, 12F, and 13H Windows-validation/visual-validation gaps noted in their respective rebuild sections. Manager workflow, Alerts, and any Phase 14/15 feature remain out of scope and were not started |

## Branding vs. internal identity

Phase 13G renamed every user-visible surface to **"Carowinds Operations"** (window title, brand band,
PDF header, diagnostic summary heading, three export-filename conventions, nontechnical script/doc
prose) while intentionally retaining four internal compatibility identifiers, unchanged since before
Phase 13:

- AppData folder: `%LOCALAPPDATA%\Carowinds Passdown`
- Database filename: `carowinds-passdown.db`
- Executable filename: `Carowinds.Passdown.Desktop.exe`
- Task Scheduler task name: `"Carowinds Passdown Daily Refresh"`

This Phase 13H rebuild changed **only** the release ZIP's own naming convention
(`Carowinds-Operations-<version>-win-x64.zip`, previously deferred from 13G) — it did not touch any of
the four identifiers above. A dedicated regression test
(`Phase13GAppDataRenameCompatibilityTests.ExistingLegacyAppData_DraftHistoryAndDeepRefreshState_AllLoadUnchanged_UnderTheRenamedApp`,
Infrastructure) proves drafts/History/Deep-Refresh state all reload correctly from a legacy-named data
directory under the renamed app, with no second `Carowinds Operations` directory ever created.

## How this artifact was produced

Built manually on this (non-Windows, macOS) dev host, mirroring exactly what
`packaging/windows/Publish-Windows.ps1` + `packaging/windows/Build-PortableRelease.ps1` do — no
`pwsh` is available on this dev host (same limitation recorded in every prior Phase 7/9/10/11/12/13G
rebuild):

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` — clean publish, 271 files after `.pdb` removal (135 MB), matching the Phase 7B–13G baseline.
2. A `strings` byte-search of the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll` confirmed Phase 13 symbols present: `RunUserForcedRefreshAsync` (13B, ×4), `NorthGateOpening` (13F, ×4), `ManualHoursWarningText`/`TimeText` (13E), `OperationsCalendarViewModel` (×51) — and the `AssemblyProductAttribute` embedded in the built DLL reads `"Carowinds Operations"` (13G) — proving this publish is not a stale reuse of the Phase 12F output.
3. Staged into `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/` — 277 files: the publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, both `.cmd` helpers, `README.txt` (from `README-portable-release.md`), `TESTER-GUIDE.txt` — the same layout Phase 7C–13G established, under the new `Carowinds-Operations` folder name.
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`) — clean (an initial naive substring check flagged `Tmds.DBus.Protocol.dll` and `System.Net.Sockets.dll` as false positives against the `.db`/`.so` patterns; re-run with extension-anchored globs confirmed both are normal .NET framework DLLs, not forbidden content).
5. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.
7. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the staging folder reported byte-for-byte identical content (277 extracted files matching 277 staged files), and `Carowinds.Passdown.Desktop.exe`'s SHA-256 inside the extraction independently matched the staged copy.

This supersedes the Phase 12F artifact (SHA-256
`dcceafee15be7f30ba410d3bb68032bd3eba057833f4a4c910fe5ecbbc4d4946`, `Carowinds-Passdown-0.1.0-dev-win-x64.zip`,
277 files / 280 entries, 61,409,645 compressed / 139,980,429 extracted bytes) — the functional delta
between the two is all of Phase 13A–13H: the `RefreshMode`/`UserForced` catalog-refresh model (13B),
commercial-coverage/pricing-semantics hardening (13C), Operations Calendar/Deep Refresh corrections
(13D), typable Manual Hours/Private Buyout entry (13E), derived Early Ride Time (13F), the visible
rename to "Carowinds Operations" plus the new app icon (13G), and this rebuild's release-naming
convention update plus final integration audit (13H). Schema moved from 10 (at the Phase 12F artifact)
to 11 via `Migration0011CreateDeepRefreshStatusTable`, introduced in Phase 13D for Deep Refresh status
persistence — the first schema change since Phase 11A; `AssociateDraftPayload` schema stays 3,
`FinalizedPassdownSnapshot` schema stays 1 (all confirmed directly in code).

The Phase 11F artifact (SHA-256 `96e6c3b48be265cac444eb96c1aba6ea6c7214b0561a97a05dd039a474f0767b`),
the Phase 10F artifact (SHA-256 `279693228b16ba201b702b188139f6dc5fdde23579e0eeae7e02bb085cae0f98`),
the Phase 9F artifact (SHA-256 `ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`),
the Post-Phase-8 artifact (SHA-256 `eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31`),
the Post-Phase-7F artifact (SHA-256 `ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`),
the Phase 7F artifact (SHA-256 `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`), and
the original Phase 7B/7C/7D/7E artifact (SHA-256
`94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) remain further superseded, retained
only as historical validation information.

## Test suite stability (Phase 13H)

`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` was run five times this phase, with
zero source changes between runs: three runs passed 1600/1600 cleanly; two runs each hit exactly one
failure, both matching the pre-existing, previously-documented (Phase 6D) Avalonia headless
cross-assembly parallelism flake (`System.InvalidOperationException: Stack empty.` inside
`Avalonia.PropertyStore.AvaloniaPropertyDictionaryPool.Get()` during `XamlIlPopulate`) — once in
`DashboardViewTests.DashboardView_EachParkStatus_ConstructsWithoutError(status: Closed)`, once in
`AssociateViewAccessibilityTests.AssociateView_WithPopulatedWorkspace_ConstructsAndExposesCriticalAutomationNames`
— two different, unrelated View-construction tests, neither a Phase 13 file, both isolated to the same
documented non-deterministic signature. Not a regression; recorded honestly per the task brief's
instruction not to call a known flake a regression without evidence.

## Note on the discrepancy in the Phase 10F task brief (retained for history)

The task instructions that requested the Phase 10F rebuild stated the artifact being superseded had
SHA-256 `1684f9146534a7ed7f58cf9e4124cd558aabc6f5f8121dc91135ff46a426ba71`. That hash did not match any
artifact recorded in this repository's history — the actual prior-current artifact at that time was the
Phase 9F artifact. That document then correctly superseded the real prior hash, not the one named in
that task brief; the discrepancy was flagged rather than silently reconciled, per CLAUDE.md's "never
invent data" instruction — it applies to release identity, not just retrieved park data.
