# RC.12 release record — 0.1.0-rc.12

Date: 2026-11-22 · Branch `feature/passdown-redesign` · Release source commit **`7763c7e`** (tag
`v0.1.0-rc.12`). Built and signed on macOS. **Published 2026-11-22 as a GitHub prerelease (§7).** Not merged to
`main`.

Result values: **PASS** · **USER-VERIFIED ON WINDOWS** · **FAIL** · **NOT EXECUTED** · **BLOCKED**. "PASS" means an
automated or build-host check; **USER-VERIFIED ON WINDOWS** means the product owner ran the step by hand on Windows.

## Release files

| File (`artifacts/releases/`, read-only) | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.12-win-x64.zip` | 80,955,888 | **`9a36f20bdd1ca08a5c681837755186b24f683bcba849c165fd9647f0a387372c`** |
| `Carowinds-Operations-0.1.0-rc.12-win-x64.update.json` | 52,571 | `ac286af9a4f84585a5fa95c6adc543301e9a60ebe68e63c21c04af9eb75eeb91` |
| `Carowinds-Operations-0.1.0-rc.12-win-x64.update.json.sig` | 206 | `56e45a0fe10a506c1e460ccea212582b966ef018571b90a898b429cae37676ce` |

Informational version in all six first-party assemblies: `0.1.0-rc.12+7763c7e30f28d9c096341fe8a508d1d761b40a37`;
EXE file version `0.1.0.12`. About this build reads `0.1.0-rc.12 (7763c7e)`. No single-file EXE was built.

What RC.12 contains since RC.11 (`573d17c`): Phase 32 (official Pass Perks, `0e115f9`), the `-LocalTestVersion`
packaging option (`a409290`), Phase 32A (typed Calendar markers, `9de3569`), Phase 33 (live PDF preview and direct
Windows printing, `c2718b2`), the LOCALTEST records (docs only) and the RC.12 build commit `7763c7e` (version,
What's New, tester guide, portable README).

## 1. Baseline

| Check | Result |
|---|---|
| Start: branch `feature/passdown-redesign`, HEAD `3ee777f`, 7 commits ahead of `origin` (`cffcbdc`, the RC.11 record), all committed | PASS |
| Phase 32 (`0e115f9`), Phase 32A (`9de3569`) and Phase 33 (`c2718b2`) are ancestors of `7763c7e` | PASS |
| Working tree: only the pre-existing unrelated `.DS_Store` / `.claude/skills/pdf-design/SKILL.md` changes, left staged and untouched (not in any RC.12 commit) | PASS |
| No Morning Message code in `src/` or in any packaged file | PASS |
| RC.10 (3 files) and RC.11 (3 files) SHA-256 unchanged before and after the RC.12 build, signing and publication | PASS |

## 2. Released source vs. the Windows-tested candidate

The product owner tested `RC12-Phase33-LOCALTEST` (built from **`c2718b2`**, ZIP SHA-256 `1f98bac6…df0d2ab3`; see
`rc12-phase33-localtest-windows-checklist.md`).

| Check | Result |
|---|---|
| `c2718b2..7763c7e`: one docs file (`3ee777f`), then `Directory.Build.props` (Version/FileVersion), the bundled `release-notes.md`, `TESTER-GUIDE.txt` and `README-portable-release.md`. No code, XAML, test, packaging-script or dependency change | PASS |
| Package diff against the tested LOCALTEST ZIP: same 307 files (LOCALTEST also had its `LOCAL-ONLY-TEST-BUILD.txt` marker). Only the six first-party DLLs, two EXEs, two `deps.json` (version strings) and the two text guides differ; `pdfium.dll`, all license files and every third-party file are byte-identical | PASS |
| Conclusion: functionally identical to the tested candidate apart from version and release text. No further Windows test is required for the change itself | PASS |

## 3. Regression suite and build

| Check | Result |
|---|---|
| Clean Release build (bin/obj deleted, `--no-incremental`) with the RC.12 version: 0 warnings, 0 errors | PASS |
| Deterministic suite (Release, `Category!=LiveSmoke`, Ui run-alone partition): **5,347 / 5,347**, 0 skipped — Domain 462, Application 1369, Infrastructure 1184, Documents 479, Ui mixed 1688, OperationsCalendarView 26, Phase15ERealWindowLayout 117, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 9 | PASS |
| Same total as the Phase 33 baseline (the RC.12 commit changes no tests). `BundledReleaseNotes_MatchTheBuiltVersion` passes with the rc.12 notes | PASS |
| Weakened tests since RC.11: no test files deleted, no `Skip`; 6 test lines changed (source count 14 → 15, View details links 2 → 3) | PASS |
| Classic, Compact and Modern PDF generation (Documents suite) | PASS |
| Packaging/release tests: Infrastructure 107/107 (incl. `RealReleaseZip_WhenSupplied_PassesEveryRule` against the final ZIP), Ui `Release` 55/55 | PASS |
| Live-website tests (LiveSmoke) | NOT EXECUTED (opt-in by design) |

## 4. Data and compatibility

All automated, on disposable data. The real operational profile on the build Mac was not opened; the app was not
launched.

| Check | Result |
|---|---|
| Database schema stays **18** (no migration added since RC.11; Desktop registers migrations 1–18) | PASS |
| Drafts, Notes, Price History: no payload/schema change; covered by the Application/Infrastructure suites | PASS |
| Pass Perks cache uses the existing schema-18 tables; a failed check keeps the last good data (`Phase32PassPerksStorageTests`) and touches no personal data | PASS |
| Personal Sync format unchanged (`SyncSchemaVersion` 1; the only Personal Sync file changes are the Settings → Calendar UI) | PASS |
| ERT synchronization: no source change since RC.11 | PASS |
| New local, never-synced preference files: `calendar-display.json` (Phase 32), `pdf-preview-layout.json` (Phase 33), in `%LOCALAPPDATA%\Carowinds Passdown`, which an update does not replace | PASS |

## 5. Package, manifest and signature

| Check | Result |
|---|---|
| Built with `Build-PortableRelease.ps1` (production mode, not `-LocalTestLabel`; frozen-release guard active); `Test-ReleasePackage.ps1` passed on staging and ZIP (307 files); `unzip -t` clean; one top-level folder `Carowinds-Operations-0.1.0-rc.12-win-x64` | PASS |
| No LOCALTEST identity in any assembly; no LOCAL-ONLY marker | PASS |
| `pdfium.dll` (PDFium `bblanchon.PDFium.Win32` 159.0.8138, chromium/8138) present, plus `licenses/PDFium-LICENSE.txt`, `licenses/pdfium-binaries-LICENSE.txt` and 17 `licenses/PDFium-dependencies/*.txt`; `THIRD-PARTY-NOTICES.txt` included | PASS |
| No PDBs, databases, logs, fixtures, screenshots, `.update.json`/`.sig`, developer paths or Morning Message text in the package; 16-byte-window scan for private-key bytes: 0 hits in 307 files | PASS |
| Bundled What's New = `0.1.0-rc.12`, "Pass Perks, live PDF preview and direct printing"; `TESTER-GUIDE.txt` / `README.txt` updated for rc.12 | PASS |
| Manifest: format 1, `carowinds-operations`, `0.1.0-rc.12`, `release-candidate`, `win-x64`; package name, size 80,955,888 and SHA-256 = the ZIP; 307 file entries, each size + SHA-256 recomputed from a fresh extraction, 0 mismatches | PASS |
| Download URL: not stored in the manifest by design; the updater uses `https://github.com/KASHO7/Passdown/releases/download/v0.1.0-rc.12/Carowinds-Operations-0.1.0-rc.12-win-x64.zip` (HTTP 200, byte-identical, §7) | PASS |
| Signature: `carowinds-release-1` (ECDSA P-256), established key outside the repository, never printed or copied; `verify` with the app's `appsettings.json` = **VERIFIED**; `appsettings.json` unchanged since RC.11 | PASS |
| Tamper: version text changed → `InvalidSignature`; package SHA-256 changed → `InvalidSignature` | PASS |

## 6. Windows validation

**Reported by the product owner on 2026-11-22: the Phase 33 local candidate (`RC12-Phase33-LOCALTEST`, from
`c2718b2`) passed all tests on Windows** (checklist sections A–J of `rc12-phase33-localtest-windows-checklist.md`:
launch and isolation, preview generation, live updates, resizing/show-hide, templates, pages and zoom, Save PDF,
printing through the Windows print dialog, appearance/scaling/Narrator, restart and preservation, and Calendar /
Pass Perks behaviour). Recorded as **USER-VERIFIED ON WINDOWS**. Claude did not run these tests.

Only the overall result was reported, without per-item notes. Not claimed from this report: whether a physical
printer (H11) or an offline printer (H12) was available, which display scales were tried, or whether Narrator
was used. The signed RC.12 ZIP itself has not been run on Windows; §2 shows it differs from the tested build only in
version and release text.

## 7. Publication and post-publication

| Check | Result |
|---|---|
| Branch pushed `cffcbdc..7763c7e` (fast-forward, no force, not merged to `main`) | PASS |
| Annotated tag `v0.1.0-rc.12` (object `d111b68`) → **`7763c7e`**, verified through the public API | PASS |
| Release https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.12 — "Carowinds Operations v0.1.0-rc.12", prerelease, not a draft, not marked latest, published 2026-11-22T11:27:35Z; page HTTP 200 without authentication | PASS |
| Assets: exactly the three files above, original names; anonymous downloads byte-identical; GitHub digests match | PASS |
| Downloaded manifest: signature VERIFIED (`carowinds-release-1`), advertises `0.1.0-rc.12` / `release-candidate` / package `9a36f20b…7372c` | PASS |
| The releases listing the updater reads (`GET /repos/KASHO7/Passdown/releases`) lists `v0.1.0-rc.12` first | PASS |
| RC.11 release unchanged (prerelease, same three assets and digests; tag → `573d17c`); RC.10 still a prerelease with 3 assets; local RC.10/RC.11 files byte-identical | PASS |

## 8. RC.11 → RC.12 in-app update

**NOT EXECUTED.** No Windows machine is available to the build host, so no real in-app upgrade has been run. The
manifest, signature, asset names and listing checks above show that RC.11's updater should be offered RC.12; they
do not prove an end-to-end update.

Manual steps (separate local Windows account such as `CarowindsTest`, or a VM snapshot — never the real
installation):

1. In the test account, download and extract RC.11 (https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.11)
   to a writable folder such as `Documents\CarowindsRC11`, and run it.
2. Create representative made-up data: save a draft (attendance, a Daily Feature), add an Operational Note, run
   Check Online, open More ▾ → Season Price History once. Close and reopen once.
3. Settings → Application Updates → Check for Updates: RC.12 (`0.1.0-rc.12`, "Pass Perks, live PDF preview and
   direct printing") is offered, with no signature warning.
4. Install Update → download completes → Restart and Install.
5. The app restarts; What's New shows the RC.12 notes.
6. Settings → About this build: `0.1.0-rc.12 (7763c7e)`, same folder.
7. The draft, note and Price History rows from step 2 are unchanged; `backups\` holds a new `pre-update-0.1.0-rc.11-to-0.1.0-rc.12…` copy.
8. Passdown shows the live PDF preview; Print opens the Windows print dialog (Cancel is fine); Save PDF works.
9. Calendar shows Pass Perks markers after Check Online; Settings → Calendar toggles them.

Report each step as PASS / FAIL.

## 9. Known limitations

- The RC.11 → RC.12 in-app update has not been run on Windows (§8).
- The per-item scope of the owner's Windows test (physical printer, display scales, Narrator) was not reported (§6).
- Printed output is a high-resolution image of each page (PDFium rasterization at the printer's resolution, up to
  600 DPI), not vector text.
- On macOS/Linux development hosts printing is not available (Windows only).
- The Pass Perks list is the public program; individual guest reward wallets are out of scope.
- Not covered automatically: Windows DPI scaling, Narrator, real OneDrive behaviour and live Carowinds pages
  (LiveSmoke).

Published assets and tag must never be replaced; any change ships as `0.1.0-rc.13`.
