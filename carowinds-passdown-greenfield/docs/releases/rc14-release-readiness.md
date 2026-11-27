# RC.14 release readiness — 0.1.0-rc.14

Date: 2026-11-26 · Branch `feature/passdown-redesign` · **Release source commit `3d603f0`**
(`3d603f05a1f617723ea0af4ffb790592a021ce9b`). Built and signed locally on the macOS build host.
**Not pushed, not tagged, not published.**

**Publication authorization: NOT AUTHORIZED — awaiting separate user approval.**

Result values: **PASS** · **USER-VERIFIED ON WINDOWS** · **NOT EXECUTED** · **BLOCKED**. "PASS" is an automated or
build-host check; **USER-VERIFIED ON WINDOWS** means the product owner ran it by hand on Windows. Claude ran no Windows
test.

## Commits

| Commit | Content | In the ZIP |
|---|---|---|
| `369fc81` | `Directory.Build.props` Version `0.1.0-rc.14` / FileVersion `0.1.0.14`; bundled What's New (`release-notes.md` — kept with the version because `BundledReleaseNotes_MatchTheBuiltVersion` ties them); 5 version-ordering test cases | yes |
| `6f89c68` | Packaged `TESTER-GUIDE.txt` and `README-portable-release.md` for the RC.14 interface | yes |
| `3d603f0` | Tester-guide wording so the package contains no `LOCALTEST` / `LOCAL-ONLY` text — **release source commit** | yes |
| this commit | `docs/releases/rc14-release-notes.md` (GitHub draft) and this record — documentation only, built after the ZIP | **no** |

The ZIP, manifest and signature correspond to `3d603f0`, not to the documentation commit. A first production build
from `6f89c68` was discarded unsigned (its tester guide contained the word "LOCALTEST"); it was never copied to
`artifacts/releases`.

## Release files

| File (`artifacts/releases/`, read-only) | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.14-win-x64.zip` | 76,138,292 | **`15f07778e3c6d74e25da6534feb8f859a523fb99a344ee2b7189b8f29fdaa278`** |
| `Carowinds-Operations-0.1.0-rc.14-win-x64.update.json` | 53,211 | `9fd0f69399220e69cab2f90f3f83509f893f9e95f3414301d394387994af8db8` |
| `Carowinds-Operations-0.1.0-rc.14-win-x64.update.json.sig` | 216 | `91979b41ac730d83d773d5e1aa271cc719928df55db218b10ea9568939c0693f` |

ZIP: 307 files, 169,472,674 bytes uncompressed, one top-level folder `Carowinds-Operations-0.1.0-rc.14-win-x64`.
First-party informational version `0.1.0-rc.14+3d603f05a1f617723ea0af4ffb790592a021ce9b`; EXE FileVersion `0.1.0.14`,
ProductVersion `0.1.0-rc.14+3d603f0…`, ProductName "Carowinds Operations". About this build should read
`0.1.0-rc.14 (3d603f0)`. No single-file EXE was built.

## 1. Preflight

| Check | Result |
|---|---|
| Start: branch `feature/passdown-redesign`, HEAD `18f1ce1` (Phase 35G-2), preceded by `c4fe167` (Phase 35G-1); 4 commits ahead of the local `origin/feature/passdown-redesign` ref `256a1c9` (remote not re-fetched) | PASS |
| Phase 35 commits are ancestors of the release source: 35A `296807e`…`beba3f8`, 35B `1677781`/`bad86da`, 35C `051cc4e`/`bfbdd18`, 35D `65c230a`/`d7d6848`/`7066e08`, **35D compact date selector `01d3553`/`62d2aca`**, 35E Calendar `5ea4d0f`/`8de9453`, 35F `8967fcc`/`256a1c9`, **35E Dashboard `2ec6261`/`cfc1bbd`**, 35G `c4fe167`/`18f1ce1` | PASS |
| Unrelated work left untouched and in no RC.14 commit: staged `.DS_Store` files and `.claude/skills/pdf-design/SKILL.md`; unstaged `AGENT_BUILD_SPEC.md`, `CLAUDE.md`, `docs/architecture.md`, `.claude/skills/ui-polish/SKILL.md`; untracked `.claude/rules/`, `docs/architecture/`. Commits used `git commit --only` with explicit paths; the build ran in a separate detached worktree of `3d603f0`, so none of this reached the package | PASS |
| RC.13: annotated tag `v0.1.0-rc.13` (object `c7a36fc`) → `d784c60`, local and on the remote; GitHub release prerelease, published 2026-11-22T17:39:54Z, same three assets, sizes and digests as `rc13-release-readiness.md` | PASS |
| No `v0.1.0-rc.14` tag locally or on the remote; no RC.14 GitHub release (public API listing shows RC.13 first) | PASS |
| Phase 35G Windows test candidate on disk: `LOCAL-ONLY-RC14-Phase35G-LOCALTEST-win-x64.zip`, 76,140,069 bytes, SHA-256 `e599cbdf133c16af12feb31ed339b7602e6e704f9079cee34d8eb3064b4ed079` — matches the 35G record | PASS |

## 2. Frozen release integrity

| Check | Result |
|---|---|
| All **7,393** files under `artifacts/releases` and `artifacts/local-test` hashed before any change and again after the build and signing: byte-identical (36 earlier release files RC.7–RC.13 included). The 35G LOCALTEST ZIP was neither reused nor renamed | PASS |
| `src/Carowinds.Passdown.Desktop/appsettings.json` (trusted keys, repository, update rules) unchanged since `d784c60` | PASS |
| Updater discovery code unchanged since RC.13: no file under `src/Carowinds.Passdown.Updater`, `Infrastructure/Updates` or `Application/Updates` differs from `d784c60` | PASS |
| No historical release notes or records rewritten | PASS |

## 3. Scope (Phase 35, verified against commits and phase records)

RC.14 = RC.13 + the Phase 35 UI/UX modernization: shared shell, sidebar, tokens, original icon, Light/Dark, Interface
animations and reduced motion (35A); Passdown toolbar, date navigation, section tabs, Overview, persistent PDF companion
(35B); Products & Pricing with separate official/display prices and the anchored price popover (35C); Shows, Passdown
Notes, dropdown chevrons, navy title bar and the compact date selector (35D); Calendar and the Admissions Overview
Dashboard (35E); Operational Notes, History and Settings (35F); final polish — Notes placeholder, Data Refresh wording,
tester guide, removal of the unused banner photo and `ShowColumnsPanel`, performance and memory review (35G). Details:
`docs/phase35-unified-ui-modernization.md` and `docs/phase35b…phase35g-*.md`.

Not in RC.14: Weather, seasonal themes, Manager workflow, Morning Message, text price overrides, new official sources,
pricing-rule changes, PDF engine or print pipeline changes.

Source changes since RC.13 outside `Desktop`: Dashboard snapshot/pricing summary (Application), animation and sidebar
preferences, window caption styler and Windows animation-effects reader (Infrastructure) — all additive. **No change**
under `Infrastructure/Storage`, `Infrastructure/PersonalSync`, `Application/PersonalSync`, `Infrastructure/Retrieval`,
`Documents` or `Updater`.

## 4. Windows validation

**USER-VERIFIED ON WINDOWS (overall):** the product owner tested the final Phase 35G local candidate
`LOCAL-ONLY-RC14-Phase35G-LOCALTEST-win-x64.zip` (76,140,069 bytes, SHA-256 `e599cbdf…4ed079`, built from `c4fe167`,
shown as `0.1.0-rc.14-LOCALTEST.phase35g`) and reported that it looks great and works as expected. This is recorded as
user-confirmed Windows acceptance of the final local candidate.

Only the overall result was reported. Per-item results of `rc14-phase35g-localtest-windows-checklist.md` were not
reported, so these are **not claimed**: 100/125/150 % scaling, high contrast, Narrator, Windows Animation effects,
physical printing, Personal Sync with a real OneDrive folder, long-session memory (checklist 20), and Windows
performance figures.

**Released vs. tested:** `c4fe167..3d603f0` changes only version metadata, `release-notes.md`, `TESTER-GUIDE.txt`,
`README-portable-release.md`, two Domain test files, documentation and the visual-fixtures README. No code, XAML, dependency or packaging-script
change. Against RC.13 the file list is identical (307 files; LOCALTEST had 308 including its marker); every
third-party file, `pdfium.dll`, all licenses and the two `.cmd` helpers are byte-identical to RC.13. The signed RC.14
ZIP itself has not been run on Windows.

## 5. Automated tests and build

| Check | Result |
|---|---|
| Clean Release build (`bin`/`obj` deleted, `--no-incremental`) on the version commit: **0 warnings, 0 errors**; `tools/update-signing` Release build 0/0 | PASS |
| Deterministic suite (Release, `Category!=LiveSmoke`, six run-alone real-window classes): **5,893 / 5,893, 0 failed, 0 skipped** — Domain 467, Application 1,396, Documents 484, Infrastructure 1,319, Ui mixed 1,986, OperationsCalendarView 30, Phase15ERealWindowLayout 144, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 54. Memory guard (kill above 3 GB) never triggered | PASS |
| Baseline 5,888 (Phase 35G) + 5 new `InlineData` cases: rc.13 < rc.14, LOCALTEST identity > its release, rc.14 is a valid release-candidate shape, rc.13 → rc.14 installable, rc.14 → rc.13 not installable. No test removed, skipped or weakened | PASS |
| After the `3d603f0` tester-guide change: Infrastructure packaging/updates/`RealReleaseZip` (193, with `CAROWINDS_RELEASE_ZIP` = the signed ZIP), Ui tester-guide/release (59), Application updates (84) — all passed, 0 skipped | PASS |
| Focused areas inside the suite: version/About and `BundledReleaseNotes_MatchTheBuiltVersion`; semantic-version ordering and update discovery; manifest and signature validation; PDF generation (Classic/Compact/Modern) and preview; Windows printing view models; saved-draft compatibility; migrations and backups; Personal Sync; finalized History immutability; official pricing; Pass Perks; Phase 35 UI tests | PASS |
| Live-website tests (`LiveSmoke`) | NOT EXECUTED (opt-in by design) |

## 6. Data, compatibility and PDF

| Check | Result |
|---|---|
| Database schema **18** (latest migration `Migration0018CreateOfficialReferenceDocumentsTable`; storage code unchanged since RC.13). Coming from RC.11–RC.13 nothing is upgraded; from RC.10 or older the RC.11 upgrade runs with its backup | PASS |
| Personal Sync: `CurrentSyncSchemaVersion` 1; sync code unchanged since RC.13 | PASS |
| PDF: `Documents` unchanged since RC.13; finalized History serves stored PDFs (Open Final PDF / Save PDF Copy), never regenerated | PASS |
| Rollback: no schema change, so returning to the RC.13 ZIP with the same data folder works; the updater's `pre-update-…` backup remains the recovery path | PASS (by design, not run on Windows) |

## 7. Performance

Phase 34 protections present in source and unchanged in RC.14 (no file changed since the 35G measurements): leading-edge
live preview scheduling (`PassdownPdfPreviewViewModel`), PDF engine warm-up (`PdfEngineWarmUp`), Price History snapshot
reader, Accesso request coalescing (`CoalescingRetrievalHttpClient`), PDFium direct-to-bitmap rendering
(`PdfiumPdfRasterizer`). The Phase 35G benchmark run (Mac, `docs/phase35g-final-uiux-polish-readiness.md` §8) found no
regression against Phase 34C. No benchmark or screenshot run was repeated for release preparation (no code changed
since `c4fe167`). Mac figures are not Windows measurements.

## 8. Package, manifest and signature

| Check | Result |
|---|---|
| Built with `Build-PortableRelease.ps1` in production mode (no `-LocalTestLabel`; frozen-release guard active) from a detached worktree of `3d603f0` with a clean tree; `Test-ReleasePackage.ps1` passed on the staging folder and the ZIP (307 files); `unzip -t` clean; no duplicate entry names; one top-level folder | PASS |
| No `LOCALTEST`, `LOCAL-ONLY` or `phase35g` text in any packaged file or in the manifest | PASS |
| First-party DLLs, both EXEs and both `deps.json` carry `0.1.0-rc.14+3d603f0…`; the only `rc.13` text is the What's New compatibility sentence | PASS |
| x64 PE: `Carowinds.Passdown.Desktop.exe` PE32+ GUI x86-64; `pdfium.dll`, `e_sqlite3.dll`, `libSkiaSharp`, `libHarfBuzzSharp`, `av_libglesv2.dll` present (same bytes as RC.13) | PASS |
| Licenses: `THIRD-PARTY-NOTICES.txt`, `licenses/` (PDFium, pdfium-binaries, 17 PDFium dependency notices, QuestPDF and its dependencies, Inter, STIX Two Math, SkiaSharp/HarfBuzzSharp, .NET, ANGLE, CommunityToolkit), `LatoFont/OFL.txt` — identical to RC.13 | PASS |
| Original app icon: all 9 images of `Assets/logo.ico` (SHA-256 `e866bc82…61bdb4f`) embedded in the EXE | PASS |
| `TESTER-GUIDE.txt` and `README.txt` byte-identical to `packaging/windows/` at `3d603f0` | PASS |
| No PDB, database, WAL/SHM, log, draft, History, `.pem`, `.update.json`/`.sig` or `.DS_Store` in the package; 16-byte-window scan for private-key bytes: 0 hits in 307 files. The only absolute-path strings are inside third-party SQLitePCLRaw DLLs, byte-identical to RC.13 | PASS |
| Manifest: format 1, `carowinds-operations`, `0.1.0-rc.14`, `release-candidate`, `win-x64`, title "A redesigned Carowinds Operations", `publishedAtUtc` 2026-11-27T00:49:24Z (signing time, **not** a publication time); package name, size and SHA-256 equal the ZIP; 307 entries, each size + SHA-256 recomputed from a fresh extraction, 0 mismatches | PASS |
| Signature: key ID `carowinds-release-1`, ECDSA P-256 (established key outside the repository, never printed or copied); `verify` against the current `appsettings.json` **and against RC.13's own `appsettings.json` from `d784c60`** = **VERIFIED** | PASS |
| Tamper: version text changed → `InvalidSignature`; package SHA-256 changed → `InvalidSignature` | PASS |
| Asset names follow `UpdateAssetNames` (`Carowinds-Operations-<version>-win-x64.zip`, `.update.json`, `.update.json.sig`), as for RC.13. Expected download URL (from `GitHubReleaseSource`, not stored in the manifest): `https://github.com/KASHO7/Passdown/releases/download/v0.1.0-rc.14/Carowinds-Operations-0.1.0-rc.14-win-x64.zip` — **remote availability PENDING; not fetched (the release does not exist)** | PASS (local) / PENDING (remote) |
| RC.13 recognises RC.14: the signing identity is in RC.13's trusted keys (verified above), and the version-ordering tests show `0.1.0-rc.14` > `0.1.0-rc.13`, same channel, installable | PASS |

### Update manifest notes (embedded in `.update.json`)

> **Before you install** — RC.14 does not change the database (RC.11–RC.13: nothing upgraded; RC.10 or older: the RC.11
> update runs first with its own backup). The updater makes and checks a database backup before changing any file. With
> Personal Sync on several computers, update one first, check it, then the others.
>
> **What's new** — redesigned interface (navy title bar, original icon, collapsible sidebar); Dashboard (Admissions
> Overview); Passdown workspace (compact date selector, Create PDF and one Actions menu, section tabs, live preview);
> Products & Pricing (official and display prices side by side, small price editor); Shows and Notes; Operations
> Calendar markers; Operational Notes and History search and filters (History still opens the original PDF); Settings
> sections, Light/Dark, Interface animations respecting Windows Animation effects; compatibility and Pass Perks
> statement.

## 9. RC.13 → RC.14 in-app update

**BLOCKED — requires published release assets.** The updater discovers releases through the public GitHub releases
API and downloads from the release-asset URL; the repository has no approved private or staging channel for a
production manifest, and none was invented. Local manifest, signature, version-ordering and ZIP checks (§8) show RC.13
should be offered RC.14; they do not prove an end-to-end update.

Run immediately after publication, in the disposable Windows account `CarowindsTest` (or a VM snapshot), never the real
installation or a real Personal Sync folder:

1. Download the public RC.13 ZIP (https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.13), extract to a new
   folder such as `Documents\CarowindsRC13` and run `Carowinds.Passdown.Desktop.exe`.
2. Create made-up data: a draft for a future date (attendance, a daily note, a Daily Feature), an Operational Note, a
   display-price override; open Season Price History once; set Appearance to Dark. Close and reopen once.
3. Settings → About this build shows `0.1.0-rc.13 (d784c60)`.
4. Settings → Application Updates → Check for Updates: `0.1.0-rc.14` "A redesigned Carowinds Operations" is offered,
   with no signature warning (manifest accepted).
5. Install Update → the download completes and is verified (no integrity error) → Restart and Install.
6. RC.14 launches from the same folder; What's New shows the RC.14 notes.
7. Settings → About shows `0.1.0-rc.14 (3d603f0)`.
8. The draft, note, override, Price History rows and the Dark appearance from step 2 are unchanged; `backups\` holds a
   new `pre-update-0.1.0-rc.13-to-0.1.0-rc.14…` copy.
9. Personal Sync with a new, empty disposable test folder: enable, Sync Now, "Last synced" updates, no error.
10. Dashboard and Calendar open and show local data; changing dates does not go online.
11. Passdown: edit a field; the live PDF preview updates almost at once.
12. Actions → Save PDF and Create PDF work; Actions → Print opens the Windows print dialog (Cancel is fine).
13. History: a finalized record (if one exists in the test profile) opens with Open Final PDF and is read-only.
14. Recovery: confirm the pre-update backup exists and that the RC.13 folder (if kept) still opens the same data folder
    (no schema change). If the update fails mid-way, the updater's recovery restores the previous program files —
    report what the app shows.

Report each step PASS / FAIL. Also note: an installation of any `0.1.0-rc.14-LOCALTEST.*` build is **not** offered
RC.14 (a LOCALTEST identity sorts above its release by design); replace it by extracting the RC.14 ZIP.

## 10. Draft GitHub release notes

`docs/releases/rc14-release-notes.md` — draft body in the RC.13 format. Not posted.

## 11. Architecture and specification review

- **Product specification (`AGENT_BUILD_SPEC.md`): reviewed — no changes required.** A version bump and release text
  change no business rule, workflow, default or acceptance criterion. The file is part of the owner's uncommitted
  reorganization and was not edited.
- **Architecture documentation: reviewed — no change required.** No dependency, DI, state, data contract, retrieval,
  persistence, sync, PDF, lifecycle, threading, configuration, security, updater or packaging-script change. The
  current-state documents (`docs/architecture.md`, `docs/architecture/`) are in the owner's uncommitted
  reorganization and were not edited. Suggested wording when that work is committed — `docs/architecture.md` review
  baseline: "RC.14 (`0.1.0-rc.14`) prepared and signed locally from `3d603f0`; not published." — and in
  `docs/architecture/updates-and-release.md`, the RC.14 asset names above.

## 12. Known limitations

- RC.13 → RC.14 in-app update not run (§9, BLOCKED until publication). RC.12/RC.11 → RC.14 in-app updates likewise
  not run.
- Per-item scope of the owner's Windows test not reported (§4); the signed ZIP itself not run on Windows.
- Performance figures are Mac measurements.
- Printing is a high-resolution raster of each page; Windows only.
- Display-price overrides remain numeric only; the Dashboard shows the Daily Feature as entered.
- Pre-existing headless-harness memory growth with focus changes and long navigation (same rate on RC.13).
- Pass Perks are the public program, not individual guest rewards.
- Not covered automatically: Windows DPI, Narrator, high contrast, real OneDrive, live Carowinds pages (LiveSmoke).

## 13. Remaining publication blockers

1. **Separate explicit owner authorization** to push the branch, create the annotated tag `v0.1.0-rc.14` → **`3d603f0`**
   and publish a GitHub prerelease (not latest) with exactly the three files above.
2. After publication: anonymous download hash check, manifest verification from the downloaded copy, releases listing
   check, then the RC.13 → RC.14 Windows update procedure (§9).

Published assets and tag must never be replaced; any later change ships as `0.1.0-rc.15`.
