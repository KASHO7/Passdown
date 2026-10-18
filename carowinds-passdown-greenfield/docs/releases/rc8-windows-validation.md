# Carowinds Operations 0.1.0-rc.8 — Windows validation record

Execution record for the frozen RC.8 artifacts. It combines the release checks (extraction, version,
packaging, upgrade, sync, scheduler) with the Phase 23A–23D feature checks
(`docs/phase23a-save-failure-freeze-fix.md` §12, `docs/phase23b-dashboard-shows-branding.md` §19,
`docs/phase23c-appearance-preferences.md` §16, `docs/phase23d-github-updater.md` §12) and the RC.7
regression areas (`docs/releases/rc7-windows-validation.md`).

**Status: NOT EXECUTED.** RC.8 was built on macOS. No Windows 11 environment was available to the build
session. Every item stays **NOT EXECUTED** until someone runs it on Windows and records the outcome.
Automated build-host results (`docs/release-manifest.md` "0.1.0-rc.8") do not replace any check here.

Result values: **PASS** · **PASS AFTER FIX** · **FAIL** · **PARTIAL** · **NOT EXECUTED** ·
**NOT APPLICABLE**. A failure that needs a new binary ships as **0.1.0-rc.9**. The RC.8 files are never
rebuilt or replaced in place.

## 0. Artifacts under test

Check hashes before extracting (PowerShell: `Get-FileHash .\<file> -Algorithm SHA256`). Built from commit
`a651e26`; About this build should read `0.1.0-rc.8 (a651e26)`.

| Artifact | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.8-win-x64.zip` (primary) | 75,714,287 | `6d753211ec0cca7393aac162b15d01220f3ae12a05e489a5316c71489c7f83ea` |
| `Carowinds-Operations-0.1.0-rc.8-win-x64-singlefile.exe` (experimental) | 191,886,156 | `9026dc93944b0a9a0476858bb71acd9d997696ef2eb00a0ee6d741401753c654` |
| `Carowinds-Operations-0.1.0-rc.8-win-x64.update.json` (signed manifest, not published) | 49,119 | `8940cff5baa1d927cf5dfc28eae7826da9a9ce868325a1e2d4878b88f1a8d8df` |
| `Carowinds-Operations-0.1.0-rc.8-win-x64.update.json.sig` | 206 | `e8f1698ec7c3d999024db0cc438d46809de5a9cec5ed115033d10fe0e42f8aaa` |

## 1. Environment (fill in)

| Field | Value |
|---|---|
| Test date | |
| Tester | |
| Windows edition and build (`winver`) | |
| CPU architecture | |
| Screen resolution(s) | |
| Display scaling | |
| Theme (Windows Light/Dark) during the run | |
| Windows time zone | |
| .NET installed on this PC? (`dotnet --info`; "not recognized" is expected) | |
| ZIP SHA-256 computed on this PC | |
| App version shown in About this build | |
| Install folder | |
| Profile used (disposable / synthetic RC.7 copy / backed-up real) | |
| Screenshots folder | |

## 2. Safety before testing

| # | Check | Result | Notes |
|---|---|---|---|
| 2.1 | `%LOCALAPPDATA%\Carowinds Passdown` copied to a dated backup, app closed, no 6 AM task running | NOT EXECUTED | |
| 2.2 | Initial checks use a disposable profile (fresh Windows user, or real profile renamed aside and restored after) | NOT EXECUTED | |
| 2.3 | Any sync folder used is a new test folder (or the real one is backed up and testing it is approved) | NOT EXECUTED | |
| 2.4 | Save-failure and upgrade tests never run against an unbacked-up real profile | NOT EXECUTED | |

## 3. First launch (spec 31)

| # | Check | Result | Notes |
|---|---|---|---|
| 3.1 | ZIP/EXE SHA-256 match §0 | NOT EXECUTED | |
| 3.2 | Extract All to a user-writable folder; one top-level folder `Carowinds-Operations-0.1.0-rc.8-win-x64\` | NOT EXECUTED | |
| 3.3 | `Test-ReleasePackage.ps1 -Path <zip> -RepositoryRoot <checkout>` passes on Windows (if a checkout is available) | NOT EXECUTED | |
| 3.4 | `Carowinds.Passdown.Desktop.exe` starts with no SDK/runtime installed and no admin prompt | NOT EXECUTED | |
| 3.5 | Explorer › Properties › Details: Product "Carowinds Operations", file version 0.1.0.8, product version 0.1.0-rc.8+… | NOT EXECUTED | |
| 3.6 | Settings › About this build: `0.1.0-rc.8 (a651e26)`; running path = extracted folder | NOT EXECUTED | |
| 3.7 | `Carowinds.Passdown.Updater.exe` (+ `.dll`, `.runtimeconfig.json`, `.deps.json`) present next to the main exe | NOT EXECUTED | |
| 3.8 | Fury banner title-only (no bar-chart icon before "CAROWINDS OPERATIONS"), title aligned with content; Light and Dark | NOT EXECUTED | |
| 3.9 | Window and taskbar icon unchanged from RC.7 | NOT EXECUTED | |
| 3.10 | No What's New window on first launch (manual install); Settings › About › What's New opens RC.8 notes | NOT EXECUTED | |
| 3.11 | No unexpected migration: Diagnostics export shows schema 16 before and after; data stays in `%LOCALAPPDATA%\Carowinds Passdown\carowinds-passdown.db` | NOT EXECUTED | |
| 3.12 | Nothing written next to the exe during normal use (except during an update) | NOT EXECUTED | |
| 3.13 | SmartScreen / Defender prompts for the main exe noted (not Authenticode-signed) | NOT EXECUTED | |
| 3.14 | Experimental single-file EXE in its own empty folder: starts, About says RC.8, Application Updates says *Manual update required* | NOT EXECUTED | |

## 4. Save-failure fix — Phase 23A (spec 32; test profile only)

Make the profile's database read-only or locked (procedure: `docs/phase23a-save-failure-freeze-fix.md` §12).

| # | Check | Result | Notes |
|---|---|---|---|
| 4.1 | Edit a draft → chip "Save failed — changes not saved"; hover shows the reason; Retry Save visible | NOT EXECUTED | |
| 4.2 | Task Manager: no sustained 100% CPU at any point below | NOT EXECUTED | |
| 4.3 | Change date (arrows, date popup, Today) → refused with explanation; edits stay on screen | NOT EXECUTED | |
| 4.4 | Check Online → refused while the save is failing | NOT EXECUTED | |
| 4.5 | Add / edit / remove an operational note → does not overwrite the pending edits | NOT EXECUTED | |
| 4.6 | Personal Sync status is not shown as successful for unsaved changes | NOT EXECUTED | |
| 4.7 | Restore write access → Retry Save succeeds; chip "Saved on this computer at … ET"; edits retained after restart | NOT EXECUTED | |
| 4.8 | Close while failing: first close warns and stays open; second close exits (unsaved edits lost — documented) | NOT EXECUTED | |
| 4.9 | A locked database delays one save by up to ~5 s, then reports a result (expected limitation) | NOT EXECUTED | |

## 5. Dashboard Shows — Phase 23B (spec 33)

Use a busy scheduled show day (official schedule).

| # | Check | Result | Notes |
|---|---|---|---|
| 5.1 | Each show: name, times, venue stacked; no time/venue overlap | NOT EXECUTED | |
| 5.2 | More than five times → four plus "+N more"; expand shows all published times; collapse works | NOT EXECUTED | |
| 5.3 | Keyboard: Tab to "+N more", Enter/Space toggles; Narrator reads expanded/collapsed | NOT EXECUTED | |
| 5.4 | Long schedules wrap; missing venue handled in words, never invented | NOT EXECUTED | |
| 5.5 | Source states (no schedule / unavailable / stale) shown honestly | NOT EXECUTED | |
| 5.6 | 100% / 125% / 150% scaling: no overlap, no horizontal overflow; small and maximized window | NOT EXECUTED | |

## 6. Appearance — Phase 23C (spec 34)

| # | Check | Result | Notes |
|---|---|---|---|
| 6.1 | Default is Use Windows setting; follows Windows Light | NOT EXECUTED | |
| 6.2 | Follows Windows Dark | NOT EXECUTED | |
| 6.3 | Windows mode changed while the app is open → app follows | NOT EXECUTED | |
| 6.4 | Forced Light while Windows is Dark | NOT EXECUTED | |
| 6.5 | Forced Dark while Windows is Light | NOT EXECUTED | |
| 6.6 | Switch is immediate; open popups/overlays (date picker, Copy, More, What's New) restyle | NOT EXECUTED | |
| 6.7 | Restart: choice persists; no wrong-theme flash before the window appears | NOT EXECUTED | |
| 6.8 | `appearance.json` in `%LOCALAPPDATA%\Carowinds Passdown`; not in the sync folder; a second PC keeps its own choice | NOT EXECUTED | |
| 6.9 | Narrator and keyboard operate the three Appearance options | NOT EXECUTED | |

## 7. Updater — Phase 23D (spec 35; synthetic test releases only)

**Harness status: NOT PREPARED.** The production RC.8 build only trusts `carowinds-release-1` and only
reads `KASHO7/Passdown`; it must not be pointed at test data, and nothing test-related may be published to
that public repository. The real Windows updater test therefore needs two **test-configured** builds of
this same source — differing only in `appsettings.json` (`Updates:Repository` = a separate private test
repository, `Updates:TrustedSigningKeys` = a disposable test key; no code change, no verification bypass) —
and signed synthetic releases in that test repository. A test build carrying a newer version number is
labelled a test build and is never an approved release. Preparing it needs the owner to choose the test
repository (see `docs/release-manifest.md` "0.1.0-rc.8" outstanding issues).

| # | Check | Result | Notes |
|---|---|---|---|
| 7.1 | Check for Updates finds the signed synthetic release (or ~90 s after launch); gear dot, no popup | NOT EXECUTED | |
| 7.2 | Release notes, date and size shown before install; notes contain no clickable links or HTML | NOT EXECUTED | |
| 7.3 | Remind Me Later hides the dot for that version | NOT EXECUTED | |
| 7.4 | Nothing downloads before Install Update; progress bar + text; Cancel leaves nothing | NOT EXECUTED | |
| 7.5 | Tampered ZIP, edited manifest, wrong-key signature each rejected | NOT EXECUTED | |
| 7.6 | Nothing closes before Restart and Install; draft saved first | NOT EXECUTED | |
| 7.7 | Updater runs from `%LOCALAPPDATA%\Carowinds Operations Updates\staging\…`; no window, no console, no UAC | NOT EXECUTED | |
| 7.8 | Files replaced; user files in the folder kept | NOT EXECUTED | |
| 7.9 | New version restarts automatically; About shows the new version | NOT EXECUTED | |
| 7.10 | What's New opens once; not on the next launch; reopen from About | NOT EXECUTED | |
| 7.11 | Failed save before Restart and Install → update postponed, edits kept, no freeze; retry works after fixing | NOT EXECUTED | |
| 7.12 | Test release whose exe fails to start → previous version restored and reopened once; Settings explains | NOT EXECUTED | |
| 7.13 | Rollback impossible (simulated) → `UPDATE-RECOVERY.txt` and kept backup | NOT EXECUTED | |
| 7.14 | Second instance or scheduled refresh running → not installed, nothing changed, nothing killed | NOT EXECUTED | |
| 7.15 | Copy under `C:\Program Files\…` → Manual update required, no UAC | NOT EXECUTED | |
| 7.16 | Data, Personal Sync config and appearance unchanged after update | NOT EXECUTED | |
| 7.17 | SmartScreen / Defender behaviour for `Carowinds.Passdown.Updater.exe` noted | NOT EXECUTED | |
| 7.18 | Production RC.8 build: Check for Updates against the real repository reports up to date or a check failure — never offers anything unapproved | NOT EXECUTED | |

## 8. Upgrade from RC.7 (spec 37; backed-up or synthetic RC.7 profile)

RC.7 → RC.8 is a manual ZIP install. RC.7 cannot update itself.

| # | Check | Result | Notes |
|---|---|---|---|
| 8.1 | Drafts (past, today, future) open with values, Show selections and math source intact | NOT EXECUTED | |
| 8.2 | Operational Notes intact | NOT EXECUTED | |
| 8.3 | History records all present; Open Final PDF of a pre-RC.8 record returns the original bytes (SHA-256 equal to a copy saved before upgrading) | NOT EXECUTED | |
| 8.4 | Season Price History / price history intact | NOT EXECUTED | |
| 8.5 | Settings (template preference, overrides) intact; official cached values unchanged | NOT EXECUTED | |
| 8.6 | Personal Sync configuration (enabled, folder, device ID) intact | NOT EXECUTED | |
| 8.7 | No migration (schema 16 before and after); no `appearance.json` until a choice is made; follows Windows | NOT EXECUTED | |
| 8.8 | No frozen PDF regenerated | NOT EXECUTED | |
| 8.9 | Scheduled refresh task still valid, or re-run "Enable Automatic Refresh.cmd" from the new folder | NOT EXECUTED | |

## 9. Existing operational features (spec 36)

Source unavailability (official site down, not yet published) is not an application defect; record it as
such.

| # | Area | Result | Notes |
|---|---|---|---|
| 9.1 | Dashboard (overview, data status, notes summary, Open Passdown/Notes) | NOT EXECUTED | |
| 9.2 | Calendar (twelve months, WinterFest, NYE after midnight, rapid switching shows last date only) | NOT EXECUTED | |
| 9.3 | Associate Passdown (date bar no network, Check Online, products, attendance commas, autosave) | NOT EXECUTED | |
| 9.4 | Operational Notes (create/edit/resolve, filters, ET timestamps) | NOT EXECUTED | |
| 9.5 | History (read-only, Open Final PDF, Save PDF Copy) | NOT EXECUTED | |
| 9.6 | Official Prices (Exact vs "From", dated Military/First Responder) | NOT EXECUTED | |
| 9.7 | Future Park Hours (Published vs Not Published, nothing invented) | NOT EXECUTED | |
| 9.8 | WinterFest dates | NOT EXECUTED | |
| 9.9 | Shows (Calendar collapse/View schedule, Passdown ticks persist) | NOT EXECUTED | |
| 9.10 | Daily Feature math (picker, preview, PDF drawn) | NOT EXECUTED | |
| 9.11 | PDF templates Classic / Compact / Modern (Shows last, ET footer, no clipping) | NOT EXECUTED | |
| 9.12 | Teams Copy (three options exclude Shows and Daily Feature) | NOT EXECUTED | |
| 9.13 | Personal Sync on a test folder (two PCs, conflict review, offline) | NOT EXECUTED | |
| 9.14 | Scheduled Refresh (Enable/Disable cmd, 6 AM run, catch-up) — only if instructed | NOT EXECUTED | |
| 9.15 | Export Diagnostics (no drafts, notes, PDFs, database) | NOT EXECUTED | |
| 9.16 | Offline: every screen opens; failed retrieval keeps last-known-good | NOT EXECUTED | |

## 10. Scaling, keyboard and Narrator

| Scale | Dashboard / Calendar / Passdown | Notes / History / Settings | What's New / Updates panel |
|---|---|---|---|
| 100% | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 125% | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 150% | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |

| # | Check | Result | Notes |
|---|---|---|---|
| 10.1 | Tab order predictable; focus ring visible; Enter/Space/Escape behave | NOT EXECUTED | |
| 10.2 | Narrator names: Retry Save, "+N more", Appearance options, Check for Updates / Install Update, gear "Update available" help text, What's New | NOT EXECUTED | |
| 10.3 | Anything Narrator cannot identify (list) | NOT EXECUTED | |

## 11. Known limitations to observe, not fix, in RC.8

- One save attempt may wait up to ~5 s on a locked database.
- Closing twice after a persistent save failure discards unsaved changes (first close warns).
- Executables are not Authenticode-signed (SmartScreen may warn).
- Experimental single-file EXE cannot update in place.
- Mixed versions sharing a Personal Sync folder remain possible; upgrade together.

## 12. Failures found (fill in)

| # | Check | What happened | Screenshot | Recovery steps | Needs new binary? |
|---|---|---|---|---|---|
| | | | | | |
