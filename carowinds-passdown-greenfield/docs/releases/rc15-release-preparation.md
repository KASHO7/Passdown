# RC.15 release preparation — 0.1.0-rc.15

Date: 2026-11-28 · Branch `feature/passdown-redesign` · **Release source commit `19cbf8b`**
(`19cbf8b1fae8201cd270006d077bb497c5aef308`). Built locally on the macOS build host. **Prepared, unsigned, not published.**

Publication authorization: **NOT AUTHORIZED.** The owner brief "Carowinds Operations — RC.15 Release Preparation"
(2026-11-28) authorized versioning, release notes, packaging, signing readiness and the updater plan, and explicitly
excluded push, tag, GitHub release, asset upload, public manifest change, key rotation and production signing.

Result values: **PASS** (automated or build-host check) · **OWNER-ACCEPTED ON WINDOWS** (owner statement) ·
**NOT EXECUTED** · **NOT AUTHORIZED** · **PENDING**. Claude ran no Windows test.

## 1. Executive summary

RC.15 = RC.14 + Phase 36 maintenance + Phase 37 Weather (Dashboard, Calendar, PDF Weather Outlook) + the pre-RC.15
post-closing pricing retention fix + the owner-approved D-1 Calendar wording. The version is `0.1.0-rc.15`
(FileVersion `0.1.0.15`). The production-mode portable ZIP was built from a clean detached worktree of `19cbf8b`:
307 files, SHA-256 `64b5d17b7921a48dbdf8c3eec9ebbbcb61a3cb509bfe9720079cbed4973981dc`. Content checks, the real-ZIP test
and an isolated throwaway-key signing dry run pass. The full deterministic suite passes (§9). No schema, sync, draft,
snapshot, updater, signing-tool, trusted-key, dependency or packaging-script change. No release blocker was found.
Not yet done, by design: production signing, manifest, publication, and the genuine RC.14 → RC.15 Windows update.

## 2. Scope of release (verified against Git history `3d603f0..19cbf8b` and the phase records)

| Area | Commits | User-facing? |
|---|---|---|
| Documentation governance and repository guards | `7c2027e` | No (repository only) |
| Phase 36A architecture and technical-debt audit | `da65b53` | No (documentation only) |
| Phase 36B test infrastructure reliability | `5384663` | No (tests and tools only) |
| Phase 36C Early Ride Time editor extraction; three view-model posts through `IUiDispatcher` | `07ae59b`, `15a31d2`, `18fac73`, `c7c79aa` | Same screen; internal reliability |
| Phase 37A/37B Weather plan, NWS retrieval, parsing, cache (not activated by itself) | `4c15115`, `2bcdd57`, `ad84590`, `927a1c7`, `ff1f94f` | Via 37C/37D/37F |
| Phase 37C Dashboard Weather card, weather monitor, manual refresh, NC/SC alert coverage | `b8676eb`, `c13c6e1`, `39bbb5b`, `e3353e4` | Yes |
| Phase 37F PDF Weather Outlook (operating-hours selection, Classic/Modern/Compact, one immutable capture, Include Weather Outlook preference, checkbox in Include in Passdown) | `9d5d282`…`a1c090d` | Yes |
| Phase 37D Calendar selected-date Weather with hourly detail; one shared preference shown in Settings | `bdda770`…`700d881` | Yes |
| Pre-RC.15 pricing retention (verified dated prices kept after the official calendar stops listing the date) | `1be060d`, `42f6a7b`, `e0ff3d9` | Yes |
| Pricing-retention Windows harness (test tool outside the solution; **not packaged**) | `780da37`, `f3966d1`, `efd6f63`, `2427b9d` | No |
| Phase 37E validation, NWS request-volume simulation tests, D-1 Calendar wording, D-2 decision | `8921324`, `4f17bed`, `69d70a7`, `c95da27`, `f65e239` | D-1 yes |
| RC.15 version and bundled What's New | `2d4ea2c` | Yes (About, What's New) |
| RC.15 packaged tester guide and portable README | `19cbf8b` | Packaged text |

**Discrepancy in the brief, resolved from evidence:** the brief lists Phase 35 (UI modernization) for RC.15. Phase
35A–35G shipped in **RC.14** (`rc14-release-readiness.md` §3; all 35 commits are ancestors of the RC.14 tag `3d603f0`).
The RC.15 notes therefore do not present Phase 35 as new. Audit-only work (36A) and tooling (36B, harness) are not
presented as user features.

Not in RC.15: Manager workflow (deferred), Morning Message (retired), alerts in PDFs, weather in Calendar day cells,
weather in Personal Sync/drafts/History, GPS or device location, automatic preview redraw on weather change (D-2),
printing-engine change, Phase 36E, seasonal themes, Personal Tasks/Issue Tracking.

## 3. Owner acceptance evidence

| Item | Evidence | Result |
|---|---|---|
| Integrated Phase 37E2 Windows build `LOCAL-ONLY-RC15-Phase37E2-LOCALTEST-win-x64.zip` (SHA-256 `15608e47…6532a1`, About `0.1.0-rc.15-LOCALTEST.phase37e2`, from `69d70a7`) | Owner: "everything works" (RC.15 preparation brief, 2026-11-28); the local ZIP's hash re-checked today and equal | **OWNER-ACCEPTED ON WINDOWS** (overall; no per-item results, screenshots, timings or telemetry reported) |
| Pricing-retention harness on Windows | `seed` PASS and `verify` PASS (Phase 37E §3) | OWNER-ACCEPTED ON WINDOWS |
| RC.13 → RC.14 in-app update | Owner-reported success (RC.14 record §15) | Historical support only — **not** proof of RC.14 → RC.15 |

Released vs. accepted: `69d70a7..19cbf8b` changes only `Directory.Build.props` (version), the bundled
`release-notes.md`, two Domain version-ordering test files, `TESTER-GUIDE.txt`, `README-portable-release.md` and
documentation. No code, XAML, dependency or packaging-script change. The release ZIP itself has not been run on Windows.

### Phase 37E decisions carried forward

| Decision | Status |
|---|---|
| D-1 Calendar retained price ("$49.00 · last verified Nov 27") | Resolved, implemented (`69d70a7`), covered by the 37E2 acceptance. Kept unchanged |
| D-2 Weather preview timing | Accepted as designed; disclosure included verbatim in the bundled What's New, the GitHub notes draft and the manifest notes draft. No preview redraw implemented |
| D-3 High-DPI printing | Checklist item E.11 (peak memory during printing and one minute later) has **no reported figures**. The owner's overall "everything works" includes printing but is not a measurement. Recorded as: no high-DPI printing defect reported; **no memory figure and no high-DPI certification claimed**; no printing-engine change (none authorized). The 225 MB transient band allocation per 600-DPI page is a known, unchanged Mac benchmark finding (37E §13) |
| D-4 Integrated Windows acceptance | Recorded as owner acceptance of the 37E2 build (above) |

## 4. Source branch and commits

| | |
|---|---|
| Starting HEAD | `f65e239` (Phase 37E-5), clean tree; 46 commits ahead of the local `origin/feature/passdown-redesign` ref (remote not fetched) |
| RC.14 tag | `v0.1.0-rc.14` → `3d603f0`, ancestor of HEAD; 48 commits `3d603f0..f65e239` |
| `2d4ea2c` | Version `0.1.0-rc.15` / `0.1.0.15`; bundled What's New; 9 version-ordering test cases — **in the ZIP** |
| `19cbf8b` | Tester guide and portable README for RC.15 — **release source commit**, in the ZIP |
| this commit | `AGENT_BUILD_SPEC.md` status lines, `docs/architecture.md` status, this record, the GitHub notes draft, index updates, Phase 37E sign-off — documentation only, **not in the ZIP** |

## 5. Version changes

| Location | Value |
|---|---|
| `Directory.Build.props` `<Version>` / `<FileVersion>` | `0.1.0-rc.15` / `0.1.0.15` (AssemblyVersion stays `0.1.0.0` by design) |
| First-party DLLs, both EXEs, both `deps.json` (from the ZIP) | `0.1.0-rc.15+19cbf8b1fae8201cd270006d077bb497c5aef308` |
| EXE resources (Desktop and Updater) | FileVersion `0.1.0.15`, ProductVersion `0.1.0-rc.15+19cbf8b…`, ProductName "Carowinds Operations" |
| About this build (by `BuildIdentity`) | should read `0.1.0-rc.15 (19cbf8b)` — confirmed on Windows only after install (§13) |
| Bundled What's New | `Version: 0.1.0-rc.15`, title "Weather and pricing accuracy", date 2026-11-28 (embedded in `Carowinds.Passdown.Desktop.dll`; `BundledReleaseNotes_MatchTheBuiltVersion` passes) |
| Asset names (`UpdateAssetNames`) | `Carowinds-Operations-0.1.0-rc.15-win-x64.zip`, `.update.json`, `.update.json.sig` |
| Tag (when authorized) | `v0.1.0-rc.15` |
| Not changed | `appsettings.json` (identical to RC.14's), updater, signing tool, migrations, sync, manifest format 1, the LiveSmoke user-agent string in one opt-in test |

**Version ordering.** `0.1.0-rc.15-LOCALTEST.phase37e2` sorts **above** `0.1.0-rc.15` (SemVer: a longer prerelease
with equal leading identifiers ranks higher) and is not a release-candidate channel version, so a 37E2 installation is
never offered RC.15. New tests pin this; the ordering was not changed. The updater test must start from a genuine RC.14.

## 6. Release notes draft

- Bundled What's New (shown in-app after an in-app update, and from About): `src/Carowinds.Passdown.Desktop/ReleaseNotes/release-notes.md`.
- GitHub prerelease body draft: [`rc15-release-notes.md`](rc15-release-notes.md) (not posted).
- Manifest notes draft (the `--notes` file for `create-manifest`; shown before installing):

> **Before you install** — RC.15 does not change the database (RC.11–RC.14: nothing upgraded; RC.10 or older: the RC.11
> update runs first with its own backup). The updater makes and checks a database backup before changing any file. With
> Personal Sync on several computers, update one first, check it, then the others.
>
> **What's new** — National Weather Service weather for Carowinds' fixed location (no device location): a Dashboard
> Weather card with official NWS alerts, and a selected-date forecast with hourly details in the Calendar (never in the
> month grid). An optional Weather Outlook for park hours in Classic, Compact and Modern Passdown PDFs (no alerts in
> PDFs); the setting is in Overview › Include in Passdown and Settings › Passdown Defaults and stays on this computer.
> Weather in printed Passdowns: Print, Save PDF and Create PDF always use the latest weather the app has. If new weather
> arrives while the preview is open, the printed or saved PDF can show newer weather than the preview did; the preview
> updates to match right after. Daily Admission, Fast Lane and other dated prices already verified for a day are kept
> after the park closes, labelled as previously verified with their original time (the Calendar shows "last verified"
> with the date). This is not confirmation that tickets are still on sale. Weather is not synchronized and is not stored
> in drafts or finalized History.

The exact Markdown used in the dry run (§11) is reproduced in the publication sequence (§19, step 4).

Claims checked against the specification (§6.6, §7.5, §9.2, §17.3), the 37C/37D/37F/37E and pricing records and source:
no GPS claim; no PDF alert claim; offline wording limited to "last weather received, marked when it may be out of date";
no NWS allowance claim; previously verified prices described as historical verification, not availability; no claim of
testing every Windows or printer configuration; no deferred feature listed.

## 7. File changes (this milestone)

`Directory.Build.props`; `src/Carowinds.Passdown.Desktop/ReleaseNotes/release-notes.md`;
`tests/Carowinds.Passdown.Domain.Tests/Updates/SemanticVersionTests.cs` (+2 cases);
`tests/Carowinds.Passdown.Domain.Tests/Updates/UpdateChannelPolicyTests.cs` (+7 cases);
`packaging/windows/TESTER-GUIDE.txt`; `packaging/windows/README-portable-release.md`; `AGENT_BUILD_SPEC.md` (two status
sentences); `docs/architecture.md` (status paragraphs); `docs/architecture/updates-and-release.md` (record link);
`docs/README.md` (index); `docs/phase37e-integrated-weather-pricing-release-readiness.md` (§26 later sign-off);
this record; `docs/releases/rc15-release-notes.md`.

## 8. Compatibility review

| Contract | Verified value | Result |
|---|---|---|
| SQLite schema | 18; latest migration `Migration0018CreateOfficialReferenceDocumentsTable` (last in `App.axaml.cs` migration list); no file under `Storage/Migrations` changed since RC.14 | PASS — no migration |
| Personal Sync envelope | `SyncEnvelopeSerializer.CurrentSyncSchemaVersion` = 1; channel payloads 1; no change under `Application/PersonalSync`, `Infrastructure/PersonalSync` since RC.14 | PASS |
| Draft payload | `AssociateDraftPayload.CurrentSchemaVersion` = 4 | PASS |
| Finalized snapshot | `FinalizedPassdownSnapshot.CurrentSchemaVersion` = 2; History opens stored bytes | PASS |
| Storage code since RC.14 | only `SqliteDatedResultRepository` (+20 lines: the retention write of last-attempt columns, `1be060d`) — no schema change | PASS |
| Local data locations | unchanged (`%LOCALAPPDATA%\Carowinds Passdown`); new local-only files `weather-cache.json` and `passdown-weather-outlook.json` are created on demand, never in the sync folder | PASS |
| Existing settings, overrides, pricing records | unchanged formats; the retention fix only keeps rows that would previously have been erased | PASS |
| Updater, signing tool, trusted key, `appsettings.json` | no diff since RC.14; `appsettings.json` byte-identical to `3d603f0` | PASS |
| Dependencies | no `.csproj` or package change since RC.14 (diff); ZIP file list identical to RC.14 | PASS |

Disposable-data scenarios run by the automated suite (temporary folders and databases only): saved-draft compatibility,
migrations and backups, Personal Sync round trips and conflicts, finalized History immutability, pricing retention (55
tests), manual overrides, weather preference boundary (never in drafts, sync, SQLite or snapshots). A real RC.14 profile
upgraded by the RC.15 updater has **not** been tested (§13).

## 9. Automated validation

| Check | Result |
|---|---|
| Clean Release build (`bin`/`obj` deleted, `--no-incremental`) at the version commit content | **0 warnings, 0 errors** (includes `tools/update-signing`) |
| Deterministic partitions (Release, `Category!=LiveSmoke`, memory watchdog 3 GB, UI mixed with `--blame-hang`) — first run | Domain 504 (+9), Application 1,531, Infrastructure 1,469, Documents 640, Ui mixed 2,152, run alone `OperationsCalendarViewTests` 35, `Phase15ERealWindowLayoutTests` 162, `RealWindowStartupTimingTests` 2, `SpellCheckNavigationLifecycleTests` 7, `Phase21DMathEditorWindowTests` 4, `Phase33PreviewWindowTests` 63 = **6,569 run, 6,568 passed, 1 failed, 0 skipped**. The failure was the documentation guard `CurrentDocumentationReferenceTests.BacktickedRepositoryPaths_Exist`, correctly reporting that the uncommitted spec edit referenced this record before it existed; fixed by writing this record |
| Final run on the final tree (this record and all documentation edits present; `bin`/`obj` deleted, `--no-incremental` build 0 warnings / 0 errors, then every partition with `--no-build`) | Domain 504, Application 1,531, Infrastructure 1,469, Documents 640, Ui mixed 2,152, run alone 35 + 162 + 2 + 7 + 4 + 63 = **6,569 / 6,569 passed, 0 failed, 0 skipped** |
| Repository guards | **31 / 31** (also inside the Infrastructure partition) |
| Baseline | 6,560 + 9 new `InlineData` cases = 6,569. No test removed, skipped or weakened |
| Requested areas inside the partitions | pricing retention and Calendar retained-price (`PostClosingPricingRetentionTests`, `OperationsCalendarPresentationTests`), Dashboard Weather, Calendar Weather, PDF Weather (37F), PDF/History, Personal Sync, packaging (`ReleaseContent*`), version/About (`BundledReleaseNotes_MatchTheBuiltVersion`), version ordering (`SemanticVersionTests`, `UpdateChannelPolicyTests`), manifest and signature validation (Infrastructure updates) |
| `RealReleaseZip_WhenSupplied_PassesEveryRule` with `CAROWINDS_RELEASE_ZIP` = the staged RC.15 ZIP | **PASS** |
| Live tests (`LiveSmoke`, NWS and official sites) | NOT EXECUTED (opt-in; no live request made) |

Peak sampled test-host RSS: about 490 MB (first run) and 660 MB (final run); the 3 GB watchdog never fired.

## 10. Windows package details

| | |
|---|---|
| Kind | **Release-preparation (staging) package** — production mode, unsigned, no manifest, not published. Not a LOCALTEST build and not yet the final signed release |
| File | `artifacts/release-candidates/0.1.0-rc.15/Carowinds-Operations-0.1.0-rc.15-win-x64.zip` (git-ignored, read-only) |
| Source commit | `19cbf8b1fae8201cd270006d077bb497c5aef308` (clean detached worktree; frozen-release guard active, no `-LocalTestLabel`) |
| Command | `pwsh packaging/windows/Build-PortableRelease.ps1` (PowerShell 7.6.10 from an earlier session's scratch folder; nothing downloaded) |
| Version | `0.1.0-rc.15+19cbf8b…`; FileVersion `0.1.0.15`; target runtime `win-x64`, self-contained, ReadyToRun, not single-file |
| Files | 307; one top-level folder `Carowinds-Operations-0.1.0-rc.15-win-x64`; no duplicate entries |
| Bytes | **76,538,369** (ZIP); 170,506,363 uncompressed |
| SHA-256 | **`64b5d17b7921a48dbdf8c3eec9ebbbcb61a3cb509bfe9720079cbed4973981dc`** |
| Script validation | `Test-ReleasePackage.ps1` passed on the staging folder and on the ZIP (307 files) |
| `unzip -t` | no errors |
| Build output | 0 warnings, 0 errors |

Why not `artifacts/releases` yet: placing the ZIP there freezes the version (the build script then refuses to rebuild
it). Until the owner has reviewed the notes and authorized publication, the unsigned candidate stays in a separate
staging folder; publication copies the same bytes there (§19). No earlier artifact was touched.

**Content inspection (all 307 files of a fresh extraction):**

| Check | Result |
|---|---|
| File list identical to RC.14's 307 files | PASS |
| Files that differ from RC.14: the six first-party DLLs, both EXEs, both `deps.json`, `README.txt`, `TESTER-GUIDE.txt` (12). Every third-party, native (`pdfium.dll`, `e_sqlite3.dll`, `libSkiaSharp`, `libHarfBuzzSharp`, `av_libglesv2.dll`, `QuestPdfSkia.dll`), license and `.cmd`/`.ps1` file is byte-identical to RC.14 | PASS — no new native binary or dependency |
| No database, WAL/SHM, PDB, PEM/key, PFX, log, PDF, `.update.json`/`.sig`, weather cache, preference file, draft, fixture, `.DS_Store`, pricing-retention harness or other test tool by name | PASS |
| No `LOCALTEST`, `LOCAL-ONLY` or `phase37e` text in any file | PASS |
| `rc.14` text in first-party files: only the historical database paragraph of `README.txt` | PASS |
| Private-key scan (every 16-byte window of the signing key's DER and PEM body against every file; contents never printed) | **0 hits in 307 files** |
| `TESTER-GUIDE.txt` and `README.txt` byte-identical to `packaging/windows/` at `19cbf8b` | PASS |
| Bundled What's New embedded in `Carowinds.Passdown.Desktop.dll` (version line, title and the D-2 sentence present once) | PASS |
| `Carowinds.Passdown.Desktop.exe` and `Updater.exe` PE32+ GUI x86-64 | PASS |
| Earlier artifacts: all 17,031 files under `artifacts/` hashed before the build and after — byte-identical (RC.14 ZIP `15f07778…a278`, the 37E2 LOCALTEST ZIP `15608e47…6532a1` and every earlier package included) | PASS |

Executable permissions: the ZIP carries no Unix permission requirements for Windows; Windows SmartScreen may warn on an
unsigned EXE (unchanged since RC.7; Authenticode signing is not part of this product's release process).

## 11. Signing readiness

| Item | Established procedure (unchanged since RC.8/RC.14) |
|---|---|
| Tool | `tools/update-signing` (`carowinds-update-signing`; source unchanged since RC.14) |
| Commands | `create-manifest --zip … --version 0.1.0-rc.15 --channel release-candidate --title "…" --notes <notes.md>`; `sign --manifest … --key-id carowinds-release-1 --private-key <key>`; `verify --manifest … --signature … --appsettings …` |
| Private key | `~/.carowinds-signing/carowinds-release-1.pem`, outside the repository; present, mode `0600`, 240 bytes (checked by `stat` only — contents not read, printed, copied or used for signing). Alternatively `CAROWINDS_UPDATE_SIGNING_KEY_FILE` (unset in this session) |
| Trusted public key | `Updates:TrustedSigningKeys` key ID `carowinds-release-1` in `src/Carowinds.Passdown.Desktop/appsettings.json` — identical to RC.14's file, so RC.14 (and RC.13) trust the same key |
| Signature format | ECDSA P-256 over the exact manifest bytes, signature file format 1 with key ID |

**Isolated dry run (no production key, no public effect)** in the session scratch folder, on a copy of the staged ZIP:
a **throwaway** key pair was generated, the manifest created (format 1, `carowinds-operations`, `0.1.0-rc.15`,
`release-candidate`, `win-x64`, title "Weather and pricing accuracy", the notes of §6) and signed with key ID
`dryrun-throwaway`. Results: `verify` against a scratch trust list holding only the throwaway key → **VERIFIED** (307
files, package `64b5d17b…81dc`); against the real `appsettings.json` and RC.14's → **NOT VERIFIED: UnknownKey** (so the
dry-run files could never be accepted by any installation); version text altered → **InvalidSignature**; package SHA-256
altered → **InvalidSignature**; package name/size/SHA-256 equal to the ZIP; 307 manifest entries recomputed from the ZIP,
**0 mismatches**. The throwaway private key was deleted afterwards. **Production signing NOT EXECUTED (not authorized).**

## 12. Manifest readiness

| Field | Prepared value |
|---|---|
| `formatVersion` | 1 |
| `productId` / `version` / `channel` / `runtimeIdentifier` | `carowinds-operations` / `0.1.0-rc.15` / `release-candidate` / `win-x64` |
| `title` | "Weather and pricing accuracy" (same as the bundled What's New) |
| `publishedAtUtc` | set by `create-manifest` at signing time (a signing time, not a publication time — as for RC.14) |
| `releaseNotes` | §6 manifest notes |
| `package` | `fileName` `Carowinds-Operations-0.1.0-rc.15-win-x64.zip`, `size` 76,538,369, `sha256` `64b5d17b…81dc`, `rootFolder` `Carowinds-Operations-0.1.0-rc.15-win-x64` |
| `files` | 307 entries (path, size, SHA-256) |
| Download URL (derived by `GitHubReleaseSource`, not stored) | `https://github.com/KASHO7/Passdown/releases/download/v0.1.0-rc.15/Carowinds-Operations-0.1.0-rc.15-win-x64.zip` — **does not exist yet** |
| Integrity checks in the app | signature against trusted keys; channel/version shape; version greater than installed; package size + SHA-256; every file's size + SHA-256 after safe extraction (path validation); database backup before replacing files; recovery restores the previous program files on failure |

## 13. RC.14 → RC.15 updater acceptance plan

**Publication dependency.** The installed app discovers releases only through the public GitHub releases API for
`KASHO7/Passdown` and ignores drafts; there is no approved private or staging channel. A genuine in-app discovery and
install test therefore needs the RC.15 assets **published** (separately authorized). Nothing below has been run.

Starting state (disposable Windows account such as `CarowindsTestB`, or a VM snapshot — never the real installation or
a real Personal Sync folder):

1. Download the **published RC.14** ZIP (https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.14), verify SHA-256
   `15f07778e3c6d74e25da6534feb8f859a523fb99a344ee2b7189b8f29fdaa278`, extract to a new folder (e.g.
   `Documents\CarowindsRC14`). Do **not** start from any LOCALTEST build (it sorts above RC.15 and is not offered it).
2. Settings → About shows `0.1.0-rc.14 (3d603f0)`.
3. Create made-up data: a draft for a future date (attendance, a daily note, a Daily Feature), an Operational Note, a
   display-price override, Appearance Dark, PDF style Modern; open Season Price History once; enable Personal Sync with
   a new empty test folder and Sync Now. Close and reopen once.
4. Back up `%LOCALAPPDATA%\Carowinds Passdown` (and the test sync folder) to a dated copy outside it.

Upgrade (after publication):

| # | Step | Expected |
|---|---|---|
| 1 | Settings → Application Updates → Check for Updates | `0.1.0-rc.15` "Weather and pricing accuracy" offered |
| 2 | Version ordering | offered as newer than `0.1.0-rc.14`; no downgrade or same-version offer |
| 3 | Release notes | the §6 manifest notes, including the D-2 sentence |
| 4 | Signature | no signature warning (manifest verified with `carowinds-release-1`) |
| 5 | Release URL | release page/tag `v0.1.0-rc.15` |
| 6 | Package SHA-256 | equals §10 (`64b5d17b…81dc`) — compare with the published asset digest |
| 7–8 | Install Update | download completes; verification passes (no integrity error) |
| 9 | Restart and Install | the app exits cleanly |
| 10–11 | Updater | installs in place; RC.15 relaunches from the same folder; What's New shows the RC.15 notes |
| 12 | About | `0.1.0-rc.15 (19cbf8b)` |
| 13–17 | Data | the draft, finalized History (if any), Price History rows, the display-price override, Appearance, PDF style and Personal Sync settings are unchanged; `backups\` holds a new `pre-update-0.1.0-rc.14-to-0.1.0-rc.15…` copy |
| 18 | Weather | after ~10 s the Dashboard Weather card fills (or says weather is unavailable without internet); Calendar selected-date Weather appears; Settings → Passdown Defaults shows "Include Weather Outlook by default" (on, first time) |
| 19 | PDF | Create PDF, Save PDF and Print (Microsoft Print to PDF) work in Classic, Compact and Modern |
| 20 | No unexpected change | no database-upgrade message (schema 18 kept); Sync Now succeeds with the test folder |

Safe negative tests (disposable profile only):

| Test | How | Expected | Status |
|---|---|---|---|
| Tampered package / incorrect checksum | Automated: per-file and package hash refusal tests (Infrastructure updates) and the §11 dry run. On Windows, only with the established offline stager (`tools/updater-offline-stager`, `docs/releases/rc9-updater-test-harness.md`) against a disposable profile | refused; the installation untouched | Automated PASS / Windows NOT EXECUTED |
| Invalid signature | §11 dry run (`InvalidSignature`, `UnknownKey`); automated verifier tests | refused before download | PASS (build host) |
| Inaccessible download | disconnect the network after the offer appears, then Install Update | clear failure; RC.14 keeps running | NOT EXECUTED |
| Interrupted download | disconnect mid-download | failure reported; no partial install | NOT EXECUTED |
| Failed installation | only through the offline stager harness, never against real data | recovery restores the previous program files; `pre-update-…` backup present | NOT EXECUTED |
| Rollback by hand | close RC.15, run the RC.14 folder against the same data folder | opens (no schema change) | NOT EXECUTED |

Report each step PASS / FAIL. RC.13 → RC.15 and older paths are expected to work (same key, ordering tests) but are
not part of this plan.

## 14. Known limitations

- The RC.14 → RC.15 in-app update has not been run (needs publication). The release ZIP itself has not been run on Windows.
- Windows acceptance of the 37E2 build is overall only; per-item checklist results (DPI scaling, Narrator, high
  contrast, physical printers, real OneDrive sync, E.9, E.11 memory figures) were not reported and are not claimed.
- Weather depends on the public NWS service, which publishes no guaranteed allowance (about 31 requests per hour per
  running app in deterministic simulation, 37E §10 — a model, not a live measurement).
- A weather update does not redraw an already displayed preview (D-2, disclosed).
- The Calendar's `Stale` price wording is unchanged (plain amount, since Phase 13D).
- Printing is a high-resolution raster of each page; display-price overrides are numeric; the Dashboard shows the
  Daily Feature as entered.
- All performance figures are Mac measurements (37E §13); none were repeated (no code changed since `69d70a7`).
- Pass Perks are the public program, not individual guest rewards.

## 15. Risks

| Risk | Mitigation |
|---|---|
| Testers with a LOCALTEST install expect an in-app offer | Tester guide and notes say to extract the ZIP instead |
| NWS throttling with several devices on one IP | back-off, `Retry-After`, last-known weather shown; ~12/h per device while failing |
| Users read a retained price as "on sale" | qualified on every surface (D-1); release notes say so |
| Owner wants wording changes after reviewing the notes | the bundled notes are inside the ZIP: change them, rebuild from a new commit as `0.1.0-rc.15` (nothing is published or frozen yet) and re-run §9–§11 |

## 16. Deferred features

Manager workflow; optional UI refinements; Phase 36E performance investigation; TD-02 test-runner tooling; seasonal
themes; Personal Tasks/Issue Tracking; weather in finalized History; a shared cross-device weather cache; automatic
preview redraw on weather change. Morning Message stays retired.

## 17. Release blockers

None found. Required before calling RC.15 released (not blockers of preparation): owner authorization (§18), production
signing, publication, post-publication verification and the genuine RC.14 → RC.15 Windows update (§13).

## 18. Publication authorization boundary

**Prepared:** version `0.1.0-rc.15`, bundled What's New, packaged guides, GitHub notes draft, manifest notes, staged
unsigned ZIP with its hash, signing procedure validated by an isolated dry run, updater plan, this record — all local.

**Not yet authorized (not done):** push; tag `v0.1.0-rc.15`; GitHub release; asset upload; production signing; any
public manifest change; key rotation; merge to `main`. RC.14 and earlier tags, releases and assets were not touched.

**Not yet verified:** the genuine RC.14 → RC.15 in-app upgrade on Windows.

## 19. Exact next steps (publication sequence, only after separate owner authorization)

1. Confirm `git status` clean, HEAD contains `19cbf8b`; `git fetch` and confirm `origin/feature/passdown-redesign` is an
   ancestor (fast-forward only); confirm no `v0.1.0-rc.15` tag or release exists locally or remotely.
2. Re-hash the staged ZIP (must be `64b5d17b…81dc`, 76,538,369 bytes); re-run `RealReleaseZip`. Copy it unchanged to
   `artifacts/releases/Carowinds-Operations-0.1.0-rc.15-win-x64.zip` (read-only); re-hash all earlier artifacts.
3. Write the §6 manifest notes to a scratch `notes.md` (outside the repository).
4. `dotnet run --project tools/update-signing -- create-manifest --zip artifacts/releases/Carowinds-Operations-0.1.0-rc.15-win-x64.zip --version 0.1.0-rc.15 --channel release-candidate --title "Weather and pricing accuracy" --notes <scratch>/notes.md`
5. `dotnet run --project tools/update-signing -- sign --manifest artifacts/releases/Carowinds-Operations-0.1.0-rc.15-win-x64.update.json --key-id carowinds-release-1 --private-key ~/.carowinds-signing/carowinds-release-1.pem`
6. `verify` against `src/Carowinds.Passdown.Desktop/appsettings.json` **and** RC.14's (`git show 3d603f0:…/appsettings.json`)
   → `VERIFIED`; tamper checks (version, package SHA-256) → `InvalidSignature`; per-file recomputation, 0 mismatches;
   make the three files read-only and record their sizes and SHA-256.
7. Fill the hash and test-count lines of `rc15-release-notes.md`; commit the record update.
8. Push the branch (fast-forward); create the annotated tag `v0.1.0-rc.15` → **`19cbf8b`**; push only that tag.
9. Create the GitHub **prerelease** "Carowinds Operations v0.1.0-rc.15" (not latest) with exactly the three files and the
   notes body.
10. Anonymously download the three assets, compare bytes and hashes, verify the downloaded manifest against RC.14's
    trusted keys, run `RealReleaseZip` on the downloaded ZIP, and run a read-only discovery check (installed `0.1.0-rc.14`
    → `UpdateAvailable 0.1.0-rc.15`; `0.1.0-rc.15-LOCALTEST.phase37e2` → `UpToDate`).
11. Owner runs §13 on Windows; record the result as a later sign-off.

Published assets and the tag must never be replaced; any change after publication ships as `0.1.0-rc.16`.

## 20. Architecture and specification review

- **Product specification (`AGENT_BUILD_SPEC.md`): updated (status only).** §4.4 and §6.6 status sentences now record
  the 37E2 Windows acceptance and the prepared, unpublished RC.15 candidate (authority: the RC.15 preparation brief,
  2026-11-28). No requirement, rule, default or acceptance criterion changed; the "not yet released" labels stay true
  until publication.
- **Architecture documentation: updated (status only).** `docs/architecture.md` status paragraphs (Windows acceptance of
  the integrated build; RC.15 prepared, not published) and `docs/architecture/updates-and-release.md` (this record). No
  dependency, DI, state, data contract, retrieval, persistence, sync, PDF, lifecycle, threading, configuration, security,
  updater or packaging-script change: **reviewed — no change required** for those areas.

## 21. Publication (authorized 2026-11-28)

Added as a later record; §1–§20 are kept as prepared. Authority: owner brief "Carowinds Operations — Publish RC.15"
(2026-11-28), authorizing for this release only: final preflight, copying the tested staging ZIP to
`artifacts/releases`, manifest creation, production signing with `carowinds-release-1`, verification, release-metadata
commits, push, the tag `v0.1.0-rc.15` → `19cbf8b`, the GitHub prerelease with its three assets, and download
verification. Not authorized and not done: merge to `main`, force-push, history rewrite, key rotation, rebuild, changes
to RC.14 or earlier tags, releases or assets.

### 21.1 Preflight

| Check | Result |
|---|---|
| Starting HEAD `b20f629`, branch `feature/passdown-redesign`, clean tree; `19cbf8b` is an ancestor; `19cbf8b..b20f629` changes only the seven documentation files of §7 | PASS |
| `git fetch`: `origin/feature/passdown-redesign` = `9351c64` (RC.14 publication record), 0 remote-only commits, 49 local commits (fast-forward); `origin/main` = `06f48c4` | PASS |
| No `v0.1.0-rc.15` tag locally or on the remote; no RC.15 release (tag lookup 404) and no draft releases | PASS |
| RC.14: annotated tag `v0.1.0-rc.14` (object `6672214`) → `3d603f0` locally and remotely; release prerelease, published 2026-11-27T00:56:02Z; three assets with the sizes and digests of the RC.14 record | PASS |
| Staged ZIP: SHA-256 `64b5d17b…81dc`, 76,538,369 bytes, read-only, 307 files, one top-level folder, `unzip -t` clean; EXEs/DLL carry `0.1.0-rc.15+19cbf8b1fae8201cd270006d077bb497c5aef308`, FileVersion `0.1.0.15`; embedded What's New = `19cbf8b`'s `release-notes.md` (version line, title, D-2 sentence); no `LOCALTEST`/`LOCAL-ONLY`/`phase37e` text | PASS |
| Trust chain: `appsettings.json` byte-identical to RC.14's (`3d603f0`), single trusted key `carowinds-release-1`; no diff in updater, signing tool or update code since RC.14; private key file present outside the repository (mode `0600`, contents never read or printed) | PASS |

### 21.2 Final release gates (on HEAD `b20f629`; differs from `19cbf8b` by documentation only)

| Check | Result |
|---|---|
| Clean Release build (`bin`/`obj` deleted, `--no-incremental`) | **0 warnings, 0 errors** |
| Deterministic partitions (`--no-build`, `Category!=LiveSmoke`, 3 GB watchdog never fired) | Domain 504, Application 1,531, Infrastructure 1,469, Documents 640, Ui mixed 2,152, run alone 35 + 162 + 2 + 7 + 4 + 63 = **6,569 / 6,569 passed, 0 failed, 0 skipped** (equal to §9) |
| Repository guards | **31 / 31** |
| `RealReleaseZip_WhenSupplied_PassesEveryRule` on the staged ZIP | PASS |
| Live tests | NOT EXECUTED (opt-in) |

### 21.3 Release files, manifest and signature

| File (`artifacts/releases/`, read-only) | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.15-win-x64.zip` | 76,538,369 | **`64b5d17b7921a48dbdf8c3eec9ebbbcb61a3cb509bfe9720079cbed4973981dc`** |
| `Carowinds-Operations-0.1.0-rc.15-win-x64.update.json` | 52,630 | `d6f85f7f0477f7191a9d32d61d8f1fb1d1fe6f1db6d8f48b3766dc3f9db96868` |
| `Carowinds-Operations-0.1.0-rc.15-win-x64.update.json.sig` | 211 | `861aee99c01e092302fb4f5cc55c4b43ca9b1e286afbcd2a0b913244093b22fb` |

| Check | Result |
|---|---|
| ZIP copied with `cp -n` from `artifacts/release-candidates/0.1.0-rc.15/`; SHA-256 before and after equal, `cmp` identical; no existing file overwritten | PASS |
| `create-manifest` (§19 step 4): format 1, `carowinds-operations`, `0.1.0-rc.15`, `release-candidate`, `win-x64`, title "Weather and pricing accuracy", `publishedAtUtc` 2026-11-28T14:57:53Z (signing time, not publication time); package name/size/SHA-256 equal the ZIP; 307 entries recomputed from a fresh extraction, 0 mismatches; no LOCALTEST text | PASS |
| Manifest notes = §6 text, with one punctuation correction: "Weather in printed Passdowns." (period, the owner-approved D-2 wording and the bundled What's New) instead of the dry run's colon | PASS (deviation recorded) |
| `sign` with key ID `carowinds-release-1` (established key outside the repository; never printed or copied); manifest SHA-256 unchanged by signing | PASS |
| `verify` against the current `appsettings.json` and against RC.14's (`git show 3d603f0:…`) | **VERIFIED** (both) |
| Tamper on copies: version → `0.1.0-rc.16`, package SHA-256 one digit changed, package `fileName` changed (the download URL is derived from tag + `fileName`, not stored) | **InvalidSignature** (all three) |
| 16-byte-window private-key scan of the manifest, signature and all 307 extracted files | 0 hits |
| All 12,495 earlier files under `artifacts/releases`, `artifacts/local-test` and `artifacts/release-candidates` hashed before and after: byte-identical | PASS |

Asset set follows `UpdateAssetNames` and the RC.8–RC.14 procedure: ZIP, `.update.json`, `.update.json.sig`. The
update-screen release notes are the `releaseNotes` field inside the signed manifest; there is no separate notes asset
(the brief's "update-screen release notes file" is this manifest content, as the brief asked the established filenames
to be confirmed).
