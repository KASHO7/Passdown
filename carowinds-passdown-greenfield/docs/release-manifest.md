# Release manifest — Carowinds Passdown 0.1.0-dev (win-x64)

Rebuilt after the Phase 8A/8B Associate Passdown work (Daily Feature, section visibility, and the
redesigned one-page PDF) — see `docs/release-strategy.md` "Post-Phase-8 rebuild" for the narrative.
This is the single source of truth for exactly what was released; `docs/release-strategy.md` has the
full narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, and this rebuild).

| | |
|---|---|
| Product | Carowinds Passdown |
| Version | `0.1.0-dev` (explicit pre-release development version — not an assigned product release number; see `docs/release-strategy.md` "Versioning strategy"; unchanged by this rebuild) |
| Target | Windows 11 x64 (`win-x64`), self-contained, multi-file publish, no trimming, no AOT |
| Artifact | `Carowinds-Passdown-0.1.0-dev-win-x64.zip` |
| SHA-256 | `eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31` |
| Compressed size | 61,265,642 bytes (58.4 MiB) |
| Extracted size | 139,364,390 bytes (132.9 MiB) |
| File count | 277 files (280 zip entries: 277 files + 3 directory entries — the release root, `LatoFont\`, `tools\`) |
| Release build date | 2026-08-25 |
| Executable | `Carowinds.Passdown.Desktop.exe` |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (registers/removes a per-user Windows Task Scheduler task; no admin, no permanent execution-policy change) |
| User data location | `%LOCALAPPDATA%\Carowinds Passdown` (independent of wherever the ZIP is extracted) |
| Diagnostic export | In-app "Export Diagnostics" button (Associate screen, next to "Create PDF") — produces `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` at a user-chosen location; local-only, excludes the database and all draft/note content |
| Non-live automated test count | 725/725 (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`) — up from 684/684 at the prior (Post-Phase-7F) artifact; 66 Domain + 179 Application + 154 Ui + 60 Documents + 266 Infrastructure. The Phase 8A/8B Associate Daily Feature/section-visibility/PDF-redesign work added tests; none were removed, skipped, or weakened |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| Windows validation | Phase 7E's full manual pass ran against the original Phase 7B/7C/7D/7E artifact, not this one (see "Known limitations" in `docs/release-strategy.md` for its accepted confidence gaps). This rebuild's own changes (Daily Feature, section-level "Include in Passdown" visibility, redesigned one-page Associate PDF, SCarowinds/Haunt visibility) are Associate UI/PDF-focused and covered by the automated suite above, but have **not been run on a real Windows machine** — no Windows host was available this session. A focused Windows smoke check (not a full Phase 7E rerun) is the recommended next step; see `docs/release-strategy.md` "Post-Phase-8 rebuild" → "Focused Windows smoke check" |
| Known limitations | See `docs/release-strategy.md` Phase 7F "Known limitations / confidence gaps," plus the Windows-validation gap noted above |

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

Before rebuilding, the freshly published `Carowinds.Passdown.Desktop.dll` was checked directly (a
byte-string search of the actual built assembly, not just timestamps) for strings unique to the
Phase 8A/8B source: `DailyFeatureTypeOption`, `SelectedDateForPicker`, and all seven section-visibility
handlers (`OnIncludeDailyAdmissionChanged`, `OnIncludeFastLaneChanged`, `OnIncludePromotionsChanged`,
`OnIncludeSeasonPassesChanged`, `OnIncludeAddOnsChanged`, `OnIncludeHauntedAttractionsChanged`,
`OnIncludeOperationalNotesChanged`) — all present — to prove this publish actually contains the new
code, not a stale rebuild.

This supersedes the Post-Phase-7F artifact (SHA-256
`ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`, still recorded in
`docs/release-strategy.md` "Post-Phase-7F rebuild" as the historical artifact produced after the
Associate app-shell/date-picker/PDF-polish pass). The functional change between the two: `Daily Fun`
replaced by `Daily Feature` (restricted to Useless Fact of the Day / Question of the Day / Quote of
the Day), section-level "Include in Passdown" controls and persisted visibility for Daily Admission,
Fast Lane, Promotions, Season Passes, Add-Ons, SCarowinds/Haunt, and Operational Notes, and a
redesigned one-page Associate Passdown PDF (operational-summary hierarchy, a distinct Daily Feature
panel, adaptive price tiles/Promotion cards, dynamic reflow when sections are hidden, strict
one-page enforcement, removal of the `1 of 1` footer). No retrieval, scheduling, persistence, or
business-rule behavior changed. The Phase 7F artifact (SHA-256
`01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`) and the original Phase
7B/7C/7D/7E artifact (SHA-256 `94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`)
remain further superseded, retained only as historical validation information (the latter is what
Phase 7E's manual Windows pass actually validated).
