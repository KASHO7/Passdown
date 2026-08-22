# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Rebuilt after the Associate UI/date-picker/PDF polish pass (see `docs/release-strategy.md`
"Post-Phase-7F rebuild" for the narrative). This is the single source of truth for exactly what was
released; `docs/release-strategy.md` has the full narrative history (Phases 7A–7F plus this rebuild).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild) |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d` |
| Compressed size | 61,260,188 bytes (58.4 MiB) |
| Extracted size | 139,345,446 bytes (132.9 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-22 |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database and all draft/note content |
| Non-live automated test count | 684/684 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — unchanged from Phase 7F; this rebuild's UI/PDF changes added no tests and broke none |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E's full manual pass ran against the earlier (Phase 7F) artifact, not this one (see "Known limitations" below for its accepted confidence gaps). This rebuild's own changes (Associate app-shell layout, section rail, `CalendarDatePicker`, denser Products layout, PDF grayscale/print refinements) are UI/PDF-focused and covered by the automated suite above, but have **not been run on a real Windows machine** — no Windows host was available this session. A focused Windows smoke check (not a full Phase 7E rerun) is the recommended next step; see `docs/release-strategy.md` "Post-Phase-7F rebuild" → "Focused Windows smoke check" |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Windows-validation gap noted above |

## How this artifact was produced

Built manually on this (non-Windows, macOS) dev host, mirroring exactly what
`packaging/windows/Publish-Windows.ps1` + `packaging/windows/Build-PortableRelease.ps1` do — no
`pwsh` is available on this dev host (same limitation recorded in every prior Phase 7 sub-phase):

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64`
2. Delete stray `.pdb` files from the publish output.
3. Stage into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/`: the publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, `Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`, `README.txt` (from `README-portable-release.md`), and `TESTER-GUIDE.txt`.
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`) — clean.
5. `zip -r -X -9` the staging folder into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.

Before rebuilding, the freshly published `Carowinds.Passdown.Desktop.dll` and
`Carowinds.Passdown.Documents.dll` were checked directly (not just by timestamp) for strings unique
to the new source — `SelectedDateForPicker`/`OnSectionButtonClick` (new Desktop view-model/code-behind
members) and the new PDF hairline/comparison-price colors `#B7C1CC`/`#5C6673` (with the prior
`#C9D3DD`/`#7C8794` confirmed absent) — to prove this publish actually contains the new code, not a
stale rebuild.

This supersedes the Phase 7F artifact (SHA-256
`01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`, still recorded in
`docs/release-strategy.md` Phase 7F as the historical artifact Phase 7E's Windows validation actually
ran against). The functional change between the two: the Associate screen's app-shell/section-rail
layout, a `CalendarDatePicker`-based date picker, a denser Products presentation, a "Verification &
Save Status" card, an "Operational Notes" summary card, and PDF print/grayscale typography
refinements (darker hairlines/comparison-price color, strikethrough on superseded comparison prices).
No retrieval, scheduling, persistence, or business-rule behavior changed. The Phase 7B/7C/7D/7E
artifact (SHA-256 `94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) remains further
superseded, retained only as historical validation information (it is what Phase 7E's manual Windows
pass actually validated, before the Phase 7F diagnostic-export addition).
