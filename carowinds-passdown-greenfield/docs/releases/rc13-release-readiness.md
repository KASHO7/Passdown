# RC.13 release record — 0.1.0-rc.13

Date: 2026-11-22 · Branch `feature/passdown-redesign` · Release source commit **`d784c60`** (annotated tag
`v0.1.0-rc.13`, tag object `c7a36fc`). Built and signed on macOS. **Published 2026-11-22T17:39:54Z as a GitHub
prerelease (§7).** Not merged to `main`.

Result values: **PASS** · **USER-VERIFIED ON WINDOWS** · **FAIL** · **NOT EXECUTED** · **BLOCKED**. "PASS" means an
automated or build-host check; **USER-VERIFIED ON WINDOWS** means the product owner ran the step by hand on Windows.

## Release files

| File (`artifacts/releases/`, read-only) | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.13-win-x64.zip` | 75,951,435 | **`c4e1fd291b84c8c93458de4c893d889143c6d9d7be2010e805f01840b7c11d5d`** |
| `Carowinds-Operations-0.1.0-rc.13-win-x64.update.json` | 52,748 | `be3ddaec3f5ce75102ff76e79982359460db5346a41167ca2de4009b8f36724c` |
| `Carowinds-Operations-0.1.0-rc.13-win-x64.update.json.sig` | 211 | `566de4d65f0c1093885fdca16e22eabf1dd0eeffbe24f876bb5d54485b1c8ce0` |

Informational version in the first-party assemblies: `0.1.0-rc.13+d784c605fe3d984ee88636b415686f9ed85353b9`;
EXE file version `0.1.0.13`. About this build reads `0.1.0-rc.13 (d784c60)`. No single-file EXE was built.

What RC.13 contains since RC.12 (`7763c7e`): Phase 34A audit (docs, `5cb71f5`), Phase 34B-1 live preview
responsiveness (`87b265e`), Phase 34B-2 general performance (`5cf716e`), Phase 34C cleanup (`146902c`, `ec4cdb5`,
`a205d85`, `a569401`), the LOCALTEST records and Phase 34C report (docs only) and the RC.13 build commit `d784c60`
(version, What's New, tester guide, portable README). No Weather, Seasonal Appearance or task-tracking feature.

## 1. Baseline

| Check | Result |
|---|---|
| Start: branch `feature/passdown-redesign`, HEAD `f8ed1c5`, 10 commits ahead of `origin` (`b3773e1`, the RC.12 record) | PASS |
| Phase 34B-1 (`87b265e`), 34B-2 (`5cf716e`) and 34C (`a569401`) are ancestors of `d784c60` | PASS |
| Working tree: only the pre-existing unrelated `.DS_Store` / `.claude/skills/pdf-design/SKILL.md` changes, left staged and untouched (not in any RC.13 commit) | PASS |
| No `v0.1.0-rc.13` tag or release existed before publication | PASS |
| RC.10, RC.11 and RC.12 files (9) and every earlier local-test ZIP (45) — 54 files — SHA-256 unchanged after the RC.13 build, signing and publication | PASS |

## 2. Released source vs. the Windows-tested candidate

The product owner tested `RC13-Phase34C-LOCALTEST` (built from **`a569401`**, ZIP SHA-256
`cb43a1fd025aba3af03ab53da814b72858b03463c5858bbe3f06836ce768821d`; see `rc13-phase34c-localtest-windows-checklist.md`).

| Check | Result |
|---|---|
| `a569401..d784c60`: two docs files (`f8ed1c5`), then `Directory.Build.props` (Version/FileVersion), the bundled `release-notes.md`, `TESTER-GUIDE.txt` and `README-portable-release.md`. No code, XAML, test, packaging-script or dependency change | PASS |
| Package diff against the tested LOCALTEST staging folder: same 307 files (LOCALTEST also had its `LOCAL-ONLY-TEST-BUILD.txt` marker). Only the six first-party DLLs, two EXEs, two `deps.json` (version strings), `README.txt` and `TESTER-GUIDE.txt` differ; `pdfium.dll`, every license and every third-party file are byte-identical | PASS |
| Conclusion: functionally identical to the tested candidate apart from version and release text. No further Windows test is required for the change itself | PASS |

## 3. Regression suite and build

| Check | Result |
|---|---|
| Clean Release build (bin/obj deleted, `--no-incremental`) with the RC.13 version: 0 warnings, 0 errors | PASS |
| Deterministic suite (Release, `Category!=LiveSmoke`, Ui run-alone partition): **5,512 / 5,512**, 0 skipped — Domain 462, Application 1,369, Documents 484, Infrastructure 1,281, Ui mixed 1,751, OperationsCalendarView 26, Phase15ERealWindowLayout 117, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 9 | PASS |
| Same total as the Phase 34C baseline (the RC.13 commit changes no tests). `BundledReleaseNotes_MatchTheBuiltVersion` passes with the rc.13 notes | PASS |
| Weakened tests since RC.12: none deleted or skipped; the only removed test lines are the Phase 34C rename / injectable-timeout lines documented in `docs/phase34c-repository-cleanup.md` §11 | PASS |
| Phase 34B-1 scheduling, 34B-2 coalescing (`Phase34B2PostCoalescingTests`), direct-render and Price History reader tests; all six accesso source suites and the 66-case `Phase34CAccessoDatedSourceConsolidationTests` | PASS |
| PDF generation (Classic, Compact, Modern; long notes, math, special characters, multi-page), preview, Print and Save PDF view-model tests; `Phase34CEmbeddedFontTests` | PASS |
| Personal Sync, ERT, migration and backup, updater verification and packaging tests; `RealReleaseZip_WhenSupplied_PassesEveryRule` against the final ZIP; semantic-version / update-discovery tests (24) | PASS |
| Live-website tests (LiveSmoke) | NOT EXECUTED (opt-in by design) |

## 4. Data, compatibility and PDF output

| Check | Result |
|---|---|
| Database schema stays **18** (no migration since RC.11) | PASS |
| Personal Sync format unchanged (`SyncSchemaVersion` 1); Phase 34C only removed an unused, never-serialized method | PASS |
| Drafts, Notes, Price History, ERT: no payload or schema change | PASS |
| PDF output: Phase 34C gate — 45 PDFs (15 documents × Classic/Compact/Modern, long notes, math, special characters, 1–6 pages) byte-identical after timestamp normalization before and after the Lato reduction (`docs/phase34c-repository-cleanup.md` §7). No font or template change since | PASS |
| Performance: Phase 34B improvements intact on the Mac benchmarks (preview, warm-up, Price History, request coalescing 21 → 9, preview allocations); Mac numbers, not Windows results (`phase34c-repository-cleanup.md` §10) | PASS |

## 5. Package, manifest and signature

| Check | Result |
|---|---|
| Built with `Build-PortableRelease.ps1` (production mode, not `-LocalTestLabel`; frozen-release guard active) from a clean `d784c60` tree; `Test-ReleasePackage.ps1` passed on staging and ZIP (307 files); `unzip -t` clean; one top-level folder `Carowinds-Operations-0.1.0-rc.13-win-x64`. The LOCALTEST ZIP was not reused | PASS |
| Size: 75,951,435 bytes, 4,994,453 bytes (≈5.0 MB) smaller than RC.12's 80,955,888 | PASS |
| No LOCALTEST / LOCAL-ONLY string in any packaged file or in the manifest | PASS |
| `pdfium.dll` present, plus `licenses/PDFium-LICENSE.txt`, `licenses/pdfium-binaries-LICENSE.txt`, 17 `licenses/PDFium-dependencies/*.txt`, `THIRD-PARTY-NOTICES.txt`, `LatoFont/OFL.txt`; QuestPDF dependencies unchanged | PASS |
| `Carowinds.Passdown.Documents.dll` embeds the six Lato faces (Regular, Bold, SemiBold, Italic, BoldItalic, SemiBoldItalic) and STIX Two Math | PASS |
| No PDBs, databases, logs, `.pem`, `.update.json`/`.sig` in the package; 16-byte-window scan for private-key bytes: 0 hits in 307 files | PASS |
| Bundled What's New = `0.1.0-rc.13`, "Faster preview, faster PDFs and a smaller download"; keeps the Pass Perks public-program statement | PASS |
| Manifest: format 1, `carowinds-operations`, `0.1.0-rc.13`, `release-candidate`, `win-x64`; package name, size and SHA-256 = the ZIP; 307 file entries, each size + SHA-256 recomputed from a fresh extraction, 0 mismatches | PASS |
| Download URL (not stored in the manifest by design): `https://github.com/KASHO7/Passdown/releases/download/v0.1.0-rc.13/Carowinds-Operations-0.1.0-rc.13-win-x64.zip` — HTTP 200, byte-identical (§7) | PASS |
| Signature: `carowinds-release-1` (ECDSA P-256), established key outside the repository, never printed or copied; `verify` with the app's `appsettings.json` (unchanged since RC.12) = **VERIFIED** | PASS |
| Tamper: version text changed → `InvalidSignature`; package SHA-256 changed → `InvalidSignature` | PASS |

## 6. Windows validation

**Reported by the product owner: the Phase 34C local candidate (`RC13-Phase34C-LOCALTEST`, from `a569401`) was
tested on Windows and the requested checks work.** The owner also reported earlier that the Phase 34B-1 and
34B-2 local builds work. Recorded as **USER-VERIFIED ON WINDOWS**. Claude did not run these tests.

Only the overall result was reported, without per-item notes; per-item scope (physical printer, display scales,
Personal Sync with OneDrive) is not claimed. The signed RC.13 ZIP itself has not been run on Windows; §2 shows it
differs from the tested build only in version and release text.

## 7. Publication and post-publication

| Check | Result |
|---|---|
| Branch pushed `b3773e1..d784c60` (fast-forward, no force, not merged to `main`) | PASS |
| Annotated tag `v0.1.0-rc.13` (object `c7a36fc`) → **`d784c60`**, verified through the public API | PASS |
| Release https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.13 — "Carowinds Operations v0.1.0-rc.13", prerelease, not a draft, not marked latest, published 2026-11-22T17:39:54Z; page HTTP 200 without authentication | PASS |
| Assets: exactly the three files above, original names; anonymous downloads byte-identical; GitHub digests match | PASS |
| Downloaded manifest: signature VERIFIED (`carowinds-release-1`), advertises `0.1.0-rc.13` / `release-candidate` / package `c4e1fd29…11d5d` | PASS |
| The releases listing the updater reads (`GET /repos/KASHO7/Passdown/releases`) lists `v0.1.0-rc.13` first, prerelease, 3 assets | PASS |
| RC.12 release unchanged (prerelease, same three assets and digests, tag → `7763c7e`); RC.11 and RC.10 still prereleases with 3 assets; local RC.10–RC.12 files byte-identical | PASS |

## 8. RC.12 → RC.13 in-app update

**NOT EXECUTED.** No Windows machine is available to the build host, so no real in-app upgrade has been run. The
manifest, signature, asset names, version ordering tests and listing checks above show that RC.12's updater
should be offered RC.13; they do not prove an end-to-end update.

Manual steps (local Windows account `CarowindsTest` or a VM snapshot — never the real installation):

1. In the test account, download and extract RC.12 (https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.12)
   to a writable folder such as `Documents\CarowindsRC12`, and run it.
2. Create made-up data: save a draft (attendance, a Daily Feature), add an Operational Note, run Check Online,
   open More ▾ → Season Price History once. Close and reopen once.
3. Settings → Application Updates → Check for Updates: RC.13 (`0.1.0-rc.13`, "Faster preview, faster PDFs and a
   smaller download") is offered, with no signature warning.
4. Install Update → download completes → Restart and Install.
5. The app restarts; What's New shows the RC.13 notes.
6. Settings → About this build: `0.1.0-rc.13 (d784c60)`, same folder.
7. The draft, note and Price History rows from step 2 are unchanged; `backups\` holds a new
   `pre-update-0.1.0-rc.12-to-0.1.0-rc.13…` copy.
8. Passdown shows the live PDF preview and it updates almost immediately after a checkbox change.
9. Products, Shows, Calendar and Price History work; Print opens the Windows print dialog (Cancel is fine);
   Create PDF / Save PDF work.

Report each step as PASS / FAIL.

## 9. Known limitations

- The RC.12 → RC.13 in-app update has not been run on Windows (§8).
- The per-item scope of the owner's Windows test was not reported (§6).
- Performance figures in the Phase 34 reports are Mac measurements, not Windows results.
- Printed output is a high-resolution image of each page (PDFium rasterization), not vector text; printing is
  Windows only.
- The Pass Perks list is the public program; individual guest reward wallets are out of scope.
- Not covered automatically: Windows DPI scaling, Narrator, real OneDrive behaviour and live Carowinds pages
  (LiveSmoke).

Published assets and tag must never be replaced; any change ships as `0.1.0-rc.14`.
