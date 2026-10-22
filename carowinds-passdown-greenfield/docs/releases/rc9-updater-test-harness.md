# RC.9 updater test harness — synthetic releases and Windows procedure

Purpose: run `docs/releases/rc9-windows-validation.md` §8 (in-app RC.8 → RC.9 update) on real Windows, against
the **exact RC.9 updater bytes**, without publishing Carowinds Operations, the production key or real data.

**Status: PREPARED, NOT UPLOADED, NOT EXECUTED.** Nothing has been uploaded to GitHub. Every upload needs the
owner's explicit approval. Windows results go in `rc9-windows-validation.md` §8, and only after they actually run.

**Owner decision (2026-10-22): use the offline route in §8 first; no GitHub upload.** The packages contain the
compiled RC.9 Infrastructure/Application/Domain libraries, which would make the unreleased Phase 25–27 code
readable before it reaches public `KASHO7/Passdown` `main` (`5fa5aba`, everything up to RC.8/Phase 24). Sections
3 and 5 (GitHub upload and discovery) are **on hold**; GitHub discovery and download stay **BLOCKED**.

## 1. Design

| Piece | What it is | Where it goes |
|---|---|---|
| Test repository | `KASHO7/carowinds-updater-test-public` — public, separate from `KASHO7/Passdown` (the production app can't sign in to a private repository, see Phase 27 notes) | GitHub, synthetic releases only |
| Test signing key | `updater-test-only-1`, throwaway ECDSA P-256, generated outside the repository. An "impostor" key exists only to make an invalidly signed release. Delete both after testing | Build Mac scratch folder only |
| Starting app `0.0.900-rc.1` | RC.8 source (`a651e26`) built by `Build-PortableRelease.ps1` with **only** the version and `Updates` settings changed: repository = test repository, trusted key = test key only. It contains the real app (Fury photo, QuestPDF) | **LOCAL ONLY** — hand to the tester privately; never upload |
| New-version packages `0.0.900-rc.2…6` | 209 files: a synthetic test program named `Carowinds.Passdown.Desktop.exe` (`tools/updater-test-harness`) + **201 files byte-identical to the frozen RC.9 ZIP**: `Carowinds.Passdown.Updater.*`, the Infrastructure/Application/Domain libraries it loads, SQLite, the .NET runtime, three licence files | Upload candidates (one release per scenario) |

Versions `0.0.900-rc.N` sort **below** every real candidate, so a real installation could never treat a test
release as an update. Production builds trust neither the test key nor the test repository (CLI check:
`UnknownKey` for all five).

The synthetic program does what the real app does at startup that matters to the updater: it runs the **real
production migrations** (with the real verified pre-migration backup and integrity check), confirms the launch
through the **real** `PostUpdateLaunch`, and applies the **real** What's New policy. It prints each step in a
console window and saves `updater-test-report-<time>.txt` in the data folder. Its behaviour is fixed per
package at build time:

| Release | Behaviour | Expected result |
|---|---|---|
| `v0.0.900-rc.2` | Normal, but signed with the impostor key | Not offered (signature rejected) |
| `v0.0.900-rc.3` | Normal, correctly signed, one byte of the ZIP changed afterwards (same size) | Offered; download rejected by SHA-256; nothing changed |
| `v0.0.900-rc.4` | Exits with code 3 **before** touching the database | Rolled back; starting app reopened once; database still 16 |
| `v0.0.900-rc.5` | Upgrades the database to 17, then exits with code 4 without confirming | **Not** rolled back; nothing restarted; `UPDATE-RECOVERY.txt`; all backups kept |
| `v0.0.900-rc.6` | Upgrades to 17, confirms, What's New once | `Succeeded`; What's New shown once, not on the next start |

Each package's signed notes start with the RC.9 warning ("RC.9 updates the local database. A backup is required
before installation. Older versions cannot open the updated database.") so the pre-install display can be
checked.

## 2. Building (build Mac; already done once)

```bash
tools/updater-test-harness/build-updater-test-releases.sh <key folder outside repo> <work folder outside repo> KASHO7/carowinds-updater-test-public <pwsh>
```

Output (git-ignored, read-only): `artifacts/updater-test/` with `upload-candidates/v0.0.900-rc.N/`,
`local-only/`, `test-trust/appsettings-updater-test.json` (public key only) and `SHA256SUMS.txt`. The script
refuses `…/Passdown`, keys or work folders inside the repository, an existing output folder, and a frozen RC.9
ZIP whose hash isn't `e1ad521f…1558`. Check: the test program's `--verify-release` mode (signature + package
SHA-256 + the real `SafeZipExtractor`) gives rc.2 *signature rejected*, rc.3 *package rejected*, rc.4–rc.6
*verified and staged* (209 files).

## 3. Uploading (owner, one release at a time, only when that scenario runs)

This Mac has no GitHub credentials, so the owner uploads in the browser: github.com →
`KASHO7/carowinds-updater-test-public` → **Releases → Draft a new release** → tag exactly `v0.0.900-rc.N` →
title `UPDATER TEST ONLY — …` → attach the three files from `upload-candidates/v0.0.900-rc.N/` with their names
unchanged → tick **Set as a pre-release** → **Publish release**. Drafts are ignored by the app. Never edit a
published test release; never attach anything from `local-only/`.

## 4. Windows test environment

- Windows 11 x64 **virtual machine with snapshots** (preferred), or a **new local Windows user account** used
  only for this test. The app keeps data in that account's `%LOCALAPPDATA%\Carowinds Passdown`, so this is what
  isolates the test data. Before starting, confirm that folder does **not** exist.
- Install folder: `%USERPROFILE%\Documents\updater-test\app` (user-writable, not Program Files).
- Personal Sync (optional): a new empty folder, e.g. `%USERPROFILE%\Documents\updater-test\sync`.
- Copy `LOCAL-ONLY-updater-test-start-app-…-0.0.900-rc.1-win-x64.zip` to the machine privately (USB stick or
  private share). Check `Get-FileHash` against `SHA256SUMS.txt`.
- Useful locations: data `%LOCALAPPDATA%\Carowinds Passdown` (database, `backups\`, `updater-test-report-*.txt`);
  updater `%LOCALAPPDATA%\Carowinds Operations Updates\records\install-*.json`; app folder
  `UPDATE-RECOVERY.txt` and `.carowinds-update\backup-*`.

## 5. Procedure

Record every step in `rc9-windows-validation.md` §8 (PASS / FAIL / PARTIAL / BLOCKED / NOT EXECUTED), with
screenshots of each message.

**Stage 0 — starting point (schema 16).**
1. Extract the starting ZIP to the install folder and start `Carowinds.Passdown.Desktop.exe`.
2. Settings → About shows `0.0.900-rc.1`; Settings → Application Updates offers **Check for Updates** (not
   "Manual update required").
3. Create synthetic data: a draft with text "UPDATER TEST DRAFT", one note, Appearance **Dark**, optionally
   Personal Sync to the empty test folder and one PDF saved to the desktop.
4. Close the app. Note the database size and time. **Take VM snapshot S0** (or copy the data folder and the
   install folder aside).

**Stage 1 — untrusted signature (§8.4).** Upload `v0.0.900-rc.2`. Start the app → **Check for Updates** →
expected "up to date"; no dot on the gear; nothing downloaded.

**Stage 2 — tampered package (§8.4, §8.2, §8.3).** Upload `v0.0.900-rc.3`. **Check for Updates** → rc.3 is
offered; its notes start with the RC.9 database warning (screenshot). Nothing has downloaded yet. **Install
Update** → expected: "The downloaded update didn't pass verification and was deleted. Nothing was changed."; the
app stays open; the install folder
and data folder are unchanged; no `backups` folder.

**Stage 3 — failure before the database upgrade (§8.6, §8.12).** Upload `v0.0.900-rc.4`. **Check for Updates →
Install Update → Restart and Install.** Expected:
- the app closes (unsaved draft edits saved first); a console window may flash;
- the starting app reopens **once**, still `0.0.900-rc.1`, with all Stage 0 data; Settings says the update
  "didn't start correctly, so version 0.0.900-rc.1 was restored";
- `backups\carowinds-passdown-pre-update-0.0.900-rc.1-to-0.0.900-rc.4-…db` exists, timestamped **before** the
  app folder changed;
- record `status: rolledBack`, `databaseSchemaVersionBefore: 16`; the report file says "crash BEFORE".

**Stage 4 — failure after the database upgrade (§8.13, §8.15).** Upload `v0.0.900-rc.5`. Install as in Stage 3.
Expected:
- the console shows "Database before: schema 16 … after: schema 17", then exits; **nothing reopens**;
- `UPDATE-RECOVERY.txt` in the app folder names the database, the pre-update backup and the previous program
  files; record `status: recoveryRequired`, `failureCode: database-upgraded-not-confirmed`;
- the app folder holds the rc.5 files; `.carowinds-update\backup-<id>\` holds the starting app's files;
  `backups\` holds both the pre-update and the pre-migration copies.

Then the **recovery procedure** (installation guide §7 "Going back"):
1. Extract the starting ZIP again into a **new** folder and start it: it must refuse the schema-17 database
   (closes; log says schema 17 is newer). The database file is unchanged.
2. Close everything. Move `carowinds-passdown.db` (and any `-journal`/`-wal`/`-shm`) into a new
   `set aside` folder. Copy the `…pre-update-…rc.4…` or `…pre-update-…rc.5…` backup in as
   `carowinds-passdown.db`.
3. Start the starting app from the new folder: it opens with the Stage 0 draft, note and Dark appearance.

**Revert to snapshot S0** (or restore both folders) before Stage 5.

**Stage 5 — successful update (§8.1–8.11).** Upload `v0.0.900-rc.6`. Start the app; wait about 90 seconds
**without** clicking anything: the gear shows the update dot (discovery). Then **Install Update → Restart and
Install**. Expected console output, in order:
1. "Database before: schema 16", "Database after: schema 17 (integrity check passed)", and the pre-migration
   backup path;
2. "Update record: Succeeded 0.0.900-rc.1 -> 0.0.900-rc.6";
3. "WHAT'S NEW (shown once…)" with the test notes, then "acknowledged".

Then check:
- press Enter; start `Carowinds.Passdown.Desktop.exe` again from the app folder → "What's New: not shown";
- the record says `succeeded`; `.carowinds-update\backup-*` is gone; both database backups remain;
- no UAC prompt appeared at any point.

**Data check:** extract the frozen **real RC.9** ZIP (privately supplied, never via the updater) into another
folder and start it against this migrated profile. It opens normally with the Stage 0 draft, note, Dark
appearance and sync settings. It makes no new backup, because the database is already at 17.

**Optional.**
- §8.16: revert to S0; start a second copy of the starting app; try Restart and Install → expected "another copy
  … was using the app folder"; nothing killed.
- §8.14: revert to S0; fill the disk (e.g. `fsutil file createnew`) until free space is below the database size
  + 16 MB; Restart and Install → expected "wasn't installed … nothing was changed".

## 6. What this does not cover

- **The real RC.9 window after an in-app update.** The new version installed by the updater is the synthetic
  program, so the real What's New *window* and real RC.9 start-up after an in-app install aren't exercised. The
  What's New *decision* (the real policy) is, and real RC.9 opening the migrated database is checked separately.
- **The production RC.8 binary itself.** The starting app is RC.8 source with test settings; its update code is
  unchanged, but it isn't the frozen RC.8 file.
- **Discovery against `KASHO7/Passdown`** and the production key. Those can only be exercised by a real release.

## 7. Afterwards

Delete the test keys from the build Mac. Remove the test releases and tags from the test repository (owner's
choice). Keep `artifacts/updater-test/` read-only until the results are recorded.

## 8. Offline route (no GitHub) — the procedure to run now

Exercises the **real RC.9 updater bytes** on Windows without uploading anything. The only step replaced is the
installed app's "download and stage" step. A test tool, `carowinds-offline-update-stager`
(`tools/updater-offline-stager`), does it with the same production classes the app uses after a download
(`EcdsaUpdateManifestVerifier`, package size/SHA-256 check, `SafeZipExtractor`, `UpdaterProcessLauncher`). It then
exits, and the updater it started waits for it to close and installs as usual. It trusts only the throwaway test
key and has no network code.

### 8.1 Kit (build Mac → test machine, private transfer only)

Built by `tools/updater-offline-stager/build-offline-kit.sh` into `artifacts/updater-test-offline/kit/`
(git-ignored, read-only). Copy the **whole `kit` folder** by USB stick or private share; never upload it.

| File | Bytes | SHA-256 |
|---|---|---|
| `LOCAL-ONLY-updater-test-start-app-Carowinds-Operations-0.0.900-rc.1-win-x64.zip` | 75,715,702 | `43a43f142d03188b94ea15454b6678d45ed3db81506cefe4b94283d559e79709` |
| `LOCAL-ONLY-offline-update-stager-win-x64.zip` | 37,500,556 | `dc9446d7ca54ebf60f89ec30ce9d6db20c44f2c64fdb16a47721e8b1b18280bb` |
| `packages/v0.0.900-rc.2 … rc.6/` (ZIP + `.update.json` + `.sig` each) | as in `SHA256SUMS.txt` | same files as `artifacts/updater-test/upload-candidates/` |
| `SHA256SUMS.txt` | | hashes of everything above |

Build-host checks: kit hashes match; packages unchanged since they were built; stager is win-x64 console,
self-contained, built from `e20d846` source, embeds only the test public key (`updater-test-only-1`, no
production key), no `.pdb`, no private-key bytes (production/test/impostor), no Fury bytes, no developer paths.
**Dry run on macOS** (isolated updates folder, `--no-launch`): rc.2 *signature rejected*, rc.3 *package
rejected*, rc.4–rc.6 *verified and staged*; with launch, the request is written and removed again when the
(Windows-only) updater can't start — the expected macOS outcome.

### 8.2 Test machine

As §4: a Windows 11 x64 **VM with snapshots**, or a **new local Windows account** used only for this test.
`%LOCALAPPDATA%\Carowinds Passdown` must not exist before you start. Then:

1. Copy the `kit` folder to `%USERPROFILE%\Documents\updater-test\kit`. In PowerShell, in that folder, check
   the hashes: `Get-FileHash -Algorithm SHA256 <file>` against `SHA256SUMS.txt`.
2. Extract the start-app ZIP to `%USERPROFILE%\Documents\updater-test\app` and the stager ZIP to
   `%USERPROFILE%\Documents\updater-test\stager`.
3. The command used in every stage below (one line, in PowerShell), with `N` = 2…6:

   ```powershell
   & "$env:USERPROFILE\Documents\updater-test\stager\carowinds-offline-update-stager\carowinds-offline-update-stager.exe" --package "$env:USERPROFILE\Documents\updater-test\kit\packages\v0.0.900-rc.N\Carowinds-Operations-0.0.900-rc.N-win-x64.zip" --install "$env:USERPROFILE\Documents\updater-test\app\Carowinds-Operations-0.0.900-rc.1-win-x64"
   ```

**Always close the app normally first** (window close, which saves drafts — the same save "Restart and
Install" performs) and wait until it is gone from Task Manager.

### 8.3 Stages

Use §5's Stage 0 to create the synthetic starting data, then take **snapshot S0**. The starting app's own update
check finds nothing (the test repository has no releases) — expected.

| Stage | Command | Expected |
|---|---|---|
| A — untrusted signature | stager with `rc.2` | `1. Signature: REJECTED (InvalidSignature)`; nothing in `%LOCALAPPDATA%\Carowinds Operations Updates\staging`; app folder unchanged |
| B — tampered package | stager with `rc.3` | `2. Package: REJECTED`; nothing staged; app folder unchanged |
| C — failure before migration | close app → stager with `rc.4` | as §5 Stage 3: a console window may flash, the starting app reopens **once** at `0.0.900-rc.1` with the Stage 0 data and the "was restored" message; `backups\…pre-update-0.0.900-rc.1-to-0.0.900-rc.4-…db`; record `rolledBack`, `databaseSchemaVersionBefore: 16` |
| D — failure after migration | close app → stager with `rc.5` | as §5 Stage 4: console shows schema 16 → 17 and exits; **nothing reopens**; `UPDATE-RECOVERY.txt`; record `recoveryRequired` / `database-upgraded-not-confirmed`; then the recovery procedure in §5 Stage 4 |
| — | **revert to S0** | |
| E — success | close app → stager with `rc.6` | as §5 Stage 5 items 1–3 and checks: schema 16 → 17 with integrity check and pre-migration backup, `Update record: Succeeded 0.0.900-rc.1 -> 0.0.900-rc.6`, What's New once; a second manual start says "What's New: not shown"; then the real-RC.9 data check |
| F — app still open (optional) | revert to S0, leave the app **open** → stager with `rc.6` | the updater waits for the stager (not the app), finds the app folder in use and records `notInstalled` / `installation-in-use`; nothing killed or changed; on its next start the app explains it |
| G — no room for the backup (optional) | revert to S0, fill the disk as §5 → stager with `rc.6` | record `notInstalled` / `insufficient-disk-space`; nothing installed |

Stage F differs from the in-app path: there, the app closes itself before the updater looks. Here the tester
closes it.

### 8.4 What the offline route covers and what stays BLOCKED

| Validation item (`rc9-windows-validation.md` §8) | Offline route |
|---|---|
| 8.1 Detection, 8.2 notes preview, 8.3 install approval in the app | **BLOCKED** — need GitHub discovery |
| 8.4 Download + signature | signature, package hash and safe extraction on Windows: **testable** (Stages A, B, via the same classes); the app's download itself: **BLOCKED** |
| 8.5 Safe shutdown | the app's normal close (same save) only; the app-initiated close: **BLOCKED** |
| 8.6–8.15 Backup, replacement, restart, migration, health confirmation, What's New, data, both failure paths, recovery | **testable** — real updater executable, real migration code |
| 8.16 Locked installation | **testable** (Stage F, variant noted above) |
