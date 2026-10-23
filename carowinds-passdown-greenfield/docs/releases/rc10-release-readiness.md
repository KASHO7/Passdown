# RC.10 release readiness — 0.1.0-rc.10

Date: 2026-10-23 · Branch `feature/passdown-redesign` · Build commit **`a8a1d96`** (version bump on top of
`433bb29`, the Morning Message rollback). Built on macOS. **Nothing uploaded, tagged, pushed or merged.**

Result values: **PASS** · **FAIL** · **NOT EXECUTED** · **BLOCKED**. "PASS" below means an automated or
build-host check unless the row says Windows.

## Candidate

| File (`artifacts/releases/`) | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.10-win-x64.zip` | 75,900,751 | **`26f447e387de95c18d2894be9799a7c8ef72bdb2bf8262312f4af5b1e5d28fae`** |
| `Carowinds-Operations-0.1.0-rc.10-win-x64.update.json` | 48,435 | `ff8aa6d5b015815e8599efae9c02417a0fbb9b2117e361fec4b1bb4be29874e9` |
| `Carowinds-Operations-0.1.0-rc.10-win-x64.update.json.sig` | 201 | `8b147ef52bb3d54f207173130725dc15510c19e1b33cb1e208c5b6646b5eed09` |

All three are read-only. Informational version in all six first-party assemblies:
`0.1.0-rc.10+a8a1d96cdca1b00f361cfddff21c0d55e1e08777`; file version `0.1.0.10`. About this build should read
`0.1.0-rc.10 (a8a1d96)`. The experimental single-file EXE was **not built** for RC.10.

What RC.10 contains: RC.9 (`e20d846`) + the legacy Season Pass price-history fold fix (`780ceac`,
`ImportedPassIdentityFolding.cs`, `SeasonPriceHistoryService.cs`) + version/notes text. `src/` at `433bb29` is
byte-identical to `780ceac`; no Morning Message code. Package vs RC.9: same 287-file list; only the six
first-party DLL/EXE/deps files, `README.txt` and `TESTER-GUIDE.txt` differ; all 275 third-party/runtime files
byte-identical.

## 1. Baseline

| Check | Result |
|---|---|
| Branch `feature/passdown-redesign`, HEAD `433bb29` at start | PASS |
| Working tree: only the pre-existing unrelated `.DS_Store` / `.claude/skills/pdf-design/SKILL.md` changes, left staged and untouched (not in the RC.10 commit) | PASS |
| RC.8 (4 files) and RC.9 (4 files) SHA-256 equal `release-manifest.md`, before and after the RC.10 build | PASS |
| Schema versions: DB 17 (Migration0017), draft payload 4, finalized snapshot 2, sync envelope 1, sync payloads 1, official price-history file 1, update manifest 1 — unchanged since RC.9 | PASS |
| No Morning Message text in `src/`, `packaging/`, `tools/`; none (UTF-8 or UTF-16) in any packaged file | PASS |

## 2. Regression suite and build

| Check | Result |
|---|---|
| Clean Release build (bin/obj deleted, `--no-incremental`): 0 warnings, 0 errors — at `433bb29` and again at `a8a1d96` | PASS |
| Deterministic suite (Release, `Category!=LiveSmoke`, Ui run-alone partition): **4,752 / 4,752** — Domain 303, Application 1286, Infrastructure 1079, Documents 469, Ui mixed 1463, OperationsCalendarView 26, Phase15ERealWindowLayout 113, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4. Same result before and after the version bump | PASS |
| Regressions found / fixed | None found; no source or test changes |
| Live-website tests (LiveSmoke) | NOT EXECUTED (opt-in by design) |

## 3. Functional areas (automated coverage — not Windows verification)

Associate Passdown create/edit/persist; Operations Summary / Today's Pricing / Operations + Pricing copy
(plain text, `AssociateViewModelCopyForTeamsTests`, `AssociateTeamsTextFormatterTests`); official pricing
retrieval and last-known-good fallback; Season Pass / Membership offer identities; historical pricing and
legacy identity folding (`ImportedPassIdentityFoldingTests`); Official Price History Sync; draft and notes
Personal Sync; Dashboard; Calendar; Notes; History/finalization; ERT; PDF generation; Appearance and Settings
persistence; startup lifecycle; migrations, integrity and RC.8/RC.9 profile compatibility; retired Morning
Message files left untouched (`RetiredMorningMessageCompatibilityTests`, `MorningMessageRetiredTests`).

| Check | Result |
|---|---|
| All of the above, by the 4,752-test suite with disposable data | PASS |
| Real operational database untouched (`~/Library/Application Support/Carowinds Passdown` last modified 2026-10-15; app never launched) | PASS |
| Windows startup/shutdown, real clipboard paste, real PDF viewer, real sync folder | NOT EXECUTED (Windows) |

## 4. Updater and packaging

| Check | Result |
|---|---|
| Version advances to `0.1.0-rc.10` / `0.1.0.10`; bundled What's New matches the built version (`BundledReleaseNotes_MatchTheBuiltVersion…`); RC.9 orders rc.10 above rc.9 numerically (`Rc9_IsOfferedRc10_NotOrderedAsText`) | PASS |
| Packaging script cannot overwrite RC.8/RC.9: live run in scratch copies with `<Version>` = rc.8 and rc.9 beside an existing ZIP → refused at the guard before publishing; ZIP and staging untouched; plus `PortablePackagingDataSafetyTests` | PASS |
| `Test-ReleasePackage.ps1` on staging and ZIP (287 files); `RealReleaseZip_WhenSupplied_PassesEveryRule` with `CAROWINDS_RELEASE_ZIP`; `unzip -t` clean; one top-level folder; fresh extraction byte-identical to staging | PASS |
| Manifest: `0.1.0-rc.10`, `release-candidate`, `win-x64`, package name/root folder correct; ZIP size 75,900,751 and SHA-256 recomputed independently = manifest; 287 file entries, each size + SHA-256 recomputed from the extraction, 0 mismatches | PASS |
| Signature: `carowinds-release-1`, ECDSA P-256, key at the established path outside the repository (never printed or copied); `verify` = VERIFIED with the app's trust config. Same public key (SPKI SHA-256 `8e0e1086…c969d3`) is embedded in RC.9's and RC.10's `Desktop.dll`; `appsettings.json` unchanged since `e20d846` | PASS |
| Tamper: manifest version text changed → `InvalidSignature`; package SHA-256 changed → `InvalidSignature` | PASS |
| Release artifacts free of private-key bytes (16-byte window scan of all 287 files: 0), developer paths (0), databases, logs, PDBs, test data, `.update.json`/`.sig` | PASS |
| RC.9 → RC.10 compatibility: same DB schema (no migration), same sync formats, same trusted key, updater/verifier code unchanged since RC.9 | PASS (by analysis + tests) |
| Offline RC.9 → RC.10 in-app install on Windows (offline stager route) | NOT EXECUTED — no Windows result recorded for the RC.9 offline route either |
| GitHub release discovery and download (RC.9 finding RC.10 on GitHub) | **BLOCKED / NOT EXECUTED** — needs a real GitHub release; never exercised end to end |

## 5. Windows smoke test

All items **NOT EXECUTED** — checklist below.

## 6. Decision

Automated checks: **all PASS, nothing FAIL.** RC.10 is ready for the Windows smoke test, not for publication.
Still pending before publishing: Windows smoke test; GitHub discovery/download (blocked); QuestPDF Community
licence eligibility and Fury photograph rights (unchanged from RC.9, pending).

---

## Windows smoke-test checklist (RC.10)

Use a **separate Windows test account** (Settings → Accounts → Other users → Add account) or a VM snapshot,
so `%LOCALAPPDATA%\Carowinds Passdown` starts empty. Your real profile and the original shared sync folder are
never touched. Use made-up values only.

| # | Step | Expected | Result |
|---|---|---|---|
| 0 | Copy the ZIP over. Optional: in PowerShell, `Get-FileHash <zip>` | `26F447E387DE95C18D2894BE9799A7C8EF72BDB2BF8262312F4AF5B1E5D28FAE` | |
| 1 | Extract All… to a new folder in Documents; double-click `Carowinds.Passdown.Desktop.exe` | Opens within seconds; no .NET or admin prompt. Settings (gear) → About this build: `0.1.0-rc.10 (a8a1d96)` and this folder | |
| 2 | Passdown: type a test note in a few fields; wait for "Saved"; switch tabs and back | Text still there | |
| 3 | Copy ▾ → Operations Summary, paste into Notepad (and Teams if handy); repeat for Today's Pricing and Operations + Pricing | Three plain-text pastes with the right sections | |
| 4 | Products → Check Online; then More ▾ → Season Price History… | Silver, Gold, Prestige separate; Memberships separate (/month); history rows listed | |
| 5 | Dashboard, Calendar (pick another date — no "checking online"), Notes (add one note) | All open without errors; note saved | |
| 6 | Create PDF (try Classic and Modern) | PDF opens and looks right | |
| 7 | Close the app (X); reopen | Draft text and note still there | |
| 8 | Settings: Appearance → Dark, then Light; Personal Sync → Choose Folder… → a **new empty** folder → Sync Now | Theme changes at once and persists after restart; sync status OK; files appear only in the new folder | |
| 9 | Look through the navigation, Copy menu and Settings | No Morning Message anywhere | |
| 10 | Close the app; open `%LOCALAPPDATA%\Carowinds Passdown\logs` (Windows+R), open the newest log in Notepad, search for `Exception` | App closed fully (gone from taskbar / Task Manager); no unexpected exceptions | |

Report each line as PASS / FAIL. If anything fails, stop — do not publish; a fix ships as rc.11.
