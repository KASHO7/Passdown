# Phase 23D — GitHub Releases updater & post-update What's New

Unreleased development after `0.1.0-rc.7`, intended for **`0.1.0-rc.8`** (not packaged here;
`Directory.Build.props` still says `0.1.0-rc.7`). Adds a user-approved, signature-verified in-app update
from GitHub Releases, an external Windows updater with rollback, and a one-time What's New after a
successful update.

> **Status in one line:** implemented and tested with synthetic data on the macOS build host.
> **Installation is enabled:** the production signing key `carowinds-release-1` is configured (commit
> `392d125`, see §4). Nothing has been executed on Windows.

## 1. Starting state

| | Verified |
|---|---|
| HEAD / branch | `707864e` on `feature/passdown-redesign` |
| Version | `0.1.0-rc.7` / FileVersion `0.1.0.7` (unchanged) |
| Deterministic tests | **4173/4173** (221 + 1100 + 874 + 463 + 1371 mixed + 26 + 105 + 2 + 7 + 4) |
| Release build | 0 warnings, 0 errors |
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 |
| Unrelated staged files | `.DS_Store` files and `.claude/skills/pdf-design/SKILL.md` — untouched, outside the commit |
| RC.6 / RC.7 artifacts | Frozen; not rebuilt, not repacked |

## 2. Architecture decision

**Findings (portable distribution):**

- The release is a multi-file, self-contained win-x64 folder (`Carowinds.Passdown.Desktop.exe` + its DLLs
  + the .NET runtime + native Skia/HarfBuzz/SQLite/QuestPDF libraries), zipped under one top-level folder
  `Carowinds-Operations-<version>-win-x64/`. No installer, no admin rights.
- While the app runs, Windows locks its `.exe`/`.dll` images against writing and deleting (renaming is
  still allowed — which is why a lock *probe* is needed, not just a rename).
- All user data lives in `%LOCALAPPDATA%\Carowinds Passdown` (database, drafts, notes, History PDFs,
  `sync-config.json`, `appearance.json`, template preference, logs). **Nothing user-specific lives in the
  installation folder**, so replacing the program files cannot touch user data.
- The same executable is also started headless by the 6 AM scheduled task (`--scheduled-refresh`) —
  a second process that can hold the files.
- The experimental single-file EXE has no updater beside it and is not updatable in place.

**Decision:** in-place update of the portable folder by an **external updater** that runs from the
*verified, staged copy of the new version* — never from the installation folder — so it locks nothing it
replaces.

```
App (old)                              Updater (from staged new version)            App (new)
─────────                              ─────────────────────────────────            ─────────
check GitHub ─► verify signed manifest
Install Update ─► download ─► SHA-256 ─► safe extract to staging
Restart and Install ─► save draft (23A)
  ─► write request ─► start updater ─► close
                                       wait for old PID to exit
                                       re-verify staged hashes
                                       refuse if files in use / not writable / no space
                                       copy into .carowinds-update\incoming-<id>
                                       swap: old → backup-<id>, new → place (journaled)
                                       start new exe ───────────────────────────►  window opens
                                       wait for confirmation  ◄───────────────────  confirm launch
                                       record Succeeded, delete backup             What's New (once)
                    (no confirmation in 3 min / crash / start failed)
                                       kill only that process, reverse journal,
                                       record RolledBack, start old version once
```

| Layer | Added |
|---|---|
| Domain | `Updates/SemanticVersion` (SemVer 2.0 precedence), `Updates/UpdateChannel` + `UpdateChannelPolicy` |
| Application | `Updates/`: `UpdateManifest`, `UpdateConfiguration(+Parser)`, `IUpdateReleaseSource` (provider boundary), `IUpdateManifestVerifier`, `UpdateDiscoveryService`, `ApplicationUpdateCoordinator`, staging/launch/inspection/outcome abstractions, `ReleaseNotesDocument`, `BundledReleaseNotesParser`, `WhatsNewPolicy`, `PostUpdateLaunch` |
| Infrastructure | `Updates/`: `GitHubReleaseSource`, `EcdsaUpdateManifestVerifier`, `UpdateManifestJson`, `SafeZipExtractor`, `UpdatePackageStager`, `InstallationInspector`, `FileUpdateStateStore`, `UpdatePaths`; `Updates/Installation/`: request/record contracts, `UpdateInstaller` (engine), `IUpdaterEnvironment`, `UpdaterProcessLauncher`, `UpdateInstallRecordStore` |
| Updater (new project) | `src/Carowinds.Passdown.Updater` — windowless `Carowinds.Passdown.Updater.exe`, `asInvoker` manifest, entry point only |
| Desktop | `ApplicationUpdatesViewModel`, `WhatsNewViewModel`, `WhatsNewView`, `ApplicationUpdatePresentation`, `UpdateRestartHost`/`EmbeddedReleaseNotesSource`, `ReleaseNotes/release-notes.md` (embedded), Settings section, gear indicator, startup/shutdown hooks |
| Tools (new, dev-only) | `tools/update-signing` — `carowinds-update-signing` (generate-key, create-manifest, sign, verify) |
| Documents | unchanged |

No database, draft, snapshot or sync schema change. No migration.

## 3. GitHub provider and release selection

- **Repository:** `KASHO7/Passdown` — taken from the project's git remote (`origin`), confirmed public via
  the unauthenticated GitHub API on 2026-10-18 (it had 0 releases). Configured in
  `src/Carowinds.Passdown.Desktop/appsettings.json` → `Updates:Repository` (validated `owner/name`;
  anything invalid disables updates). To change it, edit that value and rebuild.
- **Endpoint:** `GET https://api.github.com/repos/{owner}/{repo}/releases?per_page=30` (includes
  prereleases; GitHub's "latest release" is *not* used). No login, token or secret. `User-Agent:
  CarowindsOperations/<version>`, ETag revalidation (`If-None-Match`), 4 MB response cap, 20 s timeout.
- **Downloads:** URLs are *built* as
  `https://github.com/{owner}/{repo}/releases/download/{tag}/{exact asset name}` — `browser_download_url`
  and anything in release notes are ignored. Redirects are followed manually (max 5) and only to HTTPS,
  default port, no user-info, on `github.com`, `objects.githubusercontent.com`,
  `release-assets.githubusercontent.com` or `github-releases.githubusercontent.com`.
- **An approved release** must satisfy *all* of: not a draft (a release that does not say `draft:false` is
  treated as a draft); tag exactly `v<semver>`; version newer than installed; assets named exactly
  `Carowinds-Operations-<v>-win-x64.zip`, `…update.json`, `…update.json.sig`; signature verifies against a
  key bundled in the build; the signed manifest names this product, `win-x64`, the same version as the tag,
  the expected ZIP name/root folder and size, and a channel the installation accepts. Candidates are tried
  newest-first by SemVer precedence; at most 3 manifests are fetched per check. A public tag, GitHub's
  "Latest" marker or its "prerelease" flag authorizes nothing.
- **Channels** (`Updates:Channel`): `ReleaseCandidate` (current) accepts signed `release-candidate`
  releases whose prerelease part is exactly `rc.N` *and* signed `stable` releases (so RC users move to
  0.1.0 when it ships); `Stable` accepts only `stable` releases without a prerelease part. The channel is
  inside the signed manifest and must match the version's shape. An unknown channel value disables updates.
- **Version comparison:** full SemVer 2.0 precedence — `0.1.0-rc.9 < 0.1.0-rc.10 < 0.1.0`; never string
  order; never install an older or equal version.

**Future SharePoint provider:** implement `IUpdateReleaseSource` (list, small download, package download)
over a SharePoint folder. Signature verification, version policy, staging and installation are unchanged.
Not implemented.

## 4. Trust model

- **Authenticity:** ECDSA P-256 / SHA-256 over the exact manifest bytes (detached signature, IEEE P1363
  `r‖s`). Public keys (SubjectPublicKeyInfo, Base64) are bundled in `appsettings.json` →
  `Updates:TrustedSigningKeys` (`[{ "KeyId": "…", "PublicKey": "…" }]`, embedded in the app). The private
  key never exists in the repository, the app, the ZIP or test logs.
- **Integrity:** the signed manifest carries the ZIP's exact size and SHA-256 *and* every extracted file's
  path, size and SHA-256. A checksum published next to the ZIP is never trusted by itself.
- **Fail closed:** no trusted key → the app never contacts GitHub and Settings says *Manual update
  required*. Invalid/unknown-key signature, tampered manifest, wrong product/architecture/channel,
  unsupported format, unsafe paths, duplicate paths or a missing main executable/updater → rejected.
- **Configured key:** `carowinds-release-1` (ECDSA P-256), trusted since commit `392d125` at the owner's
  request. SHA-256 of its SubjectPublicKeyInfo:
  `8e0e108689653f027f92b0e91122cf3e49dda206636378723dc7941ed2c969d3`. The private key was generated on
  the build Mac at `~/.carowinds-signing/carowinds-release-1.pem` (folder `700`, file `600`; outside the
  repository and outside iCloud/OneDrive) and has never been committed, printed or uploaded. **Back it up
  to a second offline place** — without it, the next update must be distributed manually. Builds made
  before `392d125` (including the `6c10788` feature commit) trust no key and show "Manual update
  required".
- **Rotation:** ship a build that trusts old *and* new key → start signing with the new key → later ship a
  build that trusts only the new key. **Revocation:** ship (by manual ZIP if necessary) a build that no
  longer lists the compromised key; installations that still trust it remain exposed until updated, so
  keep the key offline and backed up. A lost key means the next update must be distributed manually.

## 5. Manifest format (format version 1)

`Carowinds-Operations-<version>-win-x64.update.json`:

```json
{
  "formatVersion": 1,
  "productId": "carowinds-operations",
  "version": "0.1.0-rc.8",
  "channel": "release-candidate",
  "runtimeIdentifier": "win-x64",
  "publishedAtUtc": "2026-10-20T15:00:00Z",
  "title": "…",
  "releaseNotes": "## Highlights\n- …",
  "package": { "fileName": "Carowinds-Operations-0.1.0-rc.8-win-x64.zip", "size": 94371840,
               "sha256": "<64 lowercase hex>", "rootFolder": "Carowinds-Operations-0.1.0-rc.8-win-x64" },
  "files": [ { "path": "Carowinds.Passdown.Desktop.exe", "size": 123, "sha256": "<hex>" }, … ]
}
```

`….update.json.sig`: `{ "formatVersion": 1, "algorithm": "ECDSA-P256-SHA256", "keyId": "…", "signature": "<Base64>" }`.

Limits: ≤ 1 MB manifest, ≤ 16 KB signature, ≤ 5,000 files, paths ≤ 240 chars, title ≤ 200, notes ≤ 20,000,
package ≤ 2 GB. Paths: `/`-separated, relative, no `..`/`.`/empty segments, no `\`, `:`, wildcard or control
characters, no trailing dot/space, no Windows device names, not under `.carowinds-update/`; case-insensitively
unique. Required files: `Carowinds.Passdown.Desktop.exe/.dll/.runtimeconfig.json`,
`Carowinds.Passdown.Updater.exe/.dll/.runtimeconfig.json`. Duplicate JSON properties are rejected.

## 6. Update state machine (Settings → Application Updates)

`ManualUpdateRequired` (no key / unsupported install) · `NotChecked` · `Checking` (button disabled) ·
`UpToDate` · `CheckFailed` (offline / timeout / rate-limited with next time / other; Retry) ·
`UpdateAvailable` (version, release date, download size, signed notes, readiness; **Install Update**,
**Remind Me Later**) · `Downloading` (progress bar + polite live text; **Cancel Download**) ·
`ReadyToInstall` (“will close, update and open again; data kept”; **Restart and Install**, **Not Now**) ·
`Restarting`. A background check never interrupts Downloading/Ready/Restarting. A previous attempt that did
not succeed is reported once (rolled back / folder in use / not writable / no space / recovery required).

**Check timing:** first check 90 s after startup (background), then every 6 h; Check for Updates joins a
running check and reuses a result < 60 s old. Failures back off 30 min; a rate limit backs off until
GitHub's reset time; the back-off is persisted, so restarts do not hammer the 60-requests/hour
unauthenticated limit shared by every PC behind one office address. One check is capped at 60 s. Entirely
separate from official-data retrieval (own `HttpClient`, no shared locks, no database). An unavailable
GitHub never affects startup.

**Notification:** a small accent dot on the Settings gear (decorative; the gear's help text says "Update
available") — never a popup. Remind Me Later hides it for that version for 7 days (persisted); a newer
version is a new reminder.

## 7. Download, verification and staging

Folders (per user, never cloud-synced, never inside the data folder):
`%LOCALAPPDATA%\Carowinds Operations Updates\{downloads,staging,requests,records}`.

1. Readiness check (Windows, portable layout with the updater beside the exe, real write probe).
2. Free-space check (ZIP + extracted size + 64 MB).
3. Stream to `downloads\<v>\…zip.partial` with exact-length enforcement and a 45 s stall timeout.
4. Exact size + SHA-256 against the signed manifest; rename to `.zip`.
5. `SafeZipExtractor` into a brand-new `staging\<v>-<id>`: every entry under the root folder, safe relative
   path, not a symlink (Unix mode bits), listed once in the manifest, exact size, SHA-256 match; extra and
   missing entries rejected; bytes capped per file; folder deleted on any failure.
6. The ZIP and partial files are deleted. Every attempt starts from a fresh download; stale staging is
   never reused, and cleanup removes folders older than 1 h that no unfinished install refers to.

## 8. Installation, rollback and recovery

**User approval:** nothing downloads before **Install Update** and nothing closes before **Restart and
Install**. Never silent.

**Phase 23A integration:** Restart and Install first runs `App.FlushPendingAutosaveBeforeShutdownAsync` —
the same bounded single-attempt flush as closing the window. If it returns "not saved", the update is
**postponed**: the updater is not started, the app stays open, the edits stay on screen with the Phase 23A
"Save failed" state and Retry Save, and Settings explains why; Restart and Install can be pressed again
later. No loop, no freeze, no false "saved". The close itself goes through the normal shutdown path
(which flushes again, stops sync/refresh/update checks within the existing bounds).

**Updater request:** `requests\install-<id>.json` (versions, install folder, staged folder, app PID + start
time, file list). The updater's command line is only `--request <that path>` (separate argument, no shell).
It refuses requests outside the requests folder, a staged folder other than the one it runs from, a
downgrade, another product, unsafe paths, or an install folder that is not a Carowinds installation, is a
link/junction, a drive root, or overlaps the updates folder. Only one updater runs at a time (lock file).

**Swap:** after the app exits (3 min max, PID reuse guarded), staged hashes are re-verified; any running
instance from the folder (by process path) or any file that cannot be opened for exclusive write
(another instance, the scheduled refresh, antivirus) blocks the install — **no process is ever stopped**
except the new version the updater itself started. Write probe and free-space check; copy to
`.carowinds-update\incoming-<id>` (verified); then per file: old → `backup-<id>`, new → place, journal
written after each step. Files of the *previous* version that the new one no longer ships are moved too
(from `installed-files.json`, written after each install); **files the app did not ship (user files) are
never moved**.

**Success condition:** the new version's window opens and writes `records\install-<id>.launched`
(version-matched, same folder). Only then is the record `Succeeded` and the backup deleted.

**Rollback:** swap failure → reverse the journal, record `NotInstalled/replace-failed`, nothing changed.
New version cannot start / exits / does not confirm within 3 min → stop that process, reverse the journal,
record `RolledBack`, start the previous version **once** (no restart loop). If reversing fails →
`RecoveryRequired`: the backup is kept, the record names it, and `UPDATE-RECOVERY.txt` in the app folder
explains the manual restore (copy the backup folder's contents back). The ultimate fallback is always
extracting a release ZIP to a new folder; user data is unaffected either way.

**Permissions:** in-place update only when the current user can actually write the folder (probe file).
No elevation is requested; the updater manifest is `asInvoker` (Windows' installer-detection heuristic
otherwise elevates executables named "…Updater"). A protected folder shows "Manual update required" with
instructions to extract the ZIP into a folder the user can change.

**Data preservation:** the database, drafts, notes, price history, frozen History PDFs, Personal Sync
settings, appearance and template preferences, logs and diagnostics are all under
`%LOCALAPPDATA%\Carowinds Passdown` and are never read or written by the updater. Paths are unchanged.

## 9. What's New

- Bundled per build: `src/Carowinds.Passdown.Desktop/ReleaseNotes/release-notes.md` (embedded resource;
  header `Version:` / `Title:` / `Date:` then `---` then notes). A test fails if its version differs from
  the built version, so the notes must be updated with every version bump. Works offline.
- **Automatically shown once** when the latest install record is a confirmed `Succeeded` for exactly the
  running version and `updates.json` → `LastAcknowledgedReleaseNotesVersion` is a different version.
  Closing records the acknowledgement. A clean install (no record), an ordinary restart, `NotInstalled`,
  `RolledBack`, `RecoveryRequired` or an unconfirmed attempt never opens it; acknowledging is per version,
  so the next successful update shows its own notes.
- **Reopen any time:** Settings → About this build → **What's New**.
- Rendering: headings, bullets and paragraphs as plain text; HTML tags removed, links reduced to their
  label, images dropped, control characters stripped, size-bounded. No HTML, scripts or clickable links.
  Missing notes show "Release notes aren't available for this version."
- Local only (`%LOCALAPPDATA%\Carowinds Passdown\updates.json`: last check, back-off, snooze,
  acknowledged notes). Not synced, not in the database, not exported.

**First installation:** a fresh extract (including RC.8 itself, the first updater-enabled build) does not
open What's New; notes remain available from Settings.

## 10. Release preparation guide (for the owner — no code or GitHub API knowledge needed)

Everything below runs from the repository folder on the build Mac/PC. Steps marked **once** are only for
the first release.

**A. Building**

1. Bump `<Version>`/`<FileVersion>` in `Directory.Build.props` (e.g. `0.1.0-rc.8` / `0.1.0.8`) and update
   `src/Carowinds.Passdown.Desktop/ReleaseNotes/release-notes.md` (same version, title, date, notes).
2. Run the tests and build (as for every RC), then `pwsh packaging/windows/Build-PortableRelease.ps1`.
   Result: `artifacts/releases/Carowinds-Operations-<version>-win-x64.zip` (now including
   `Carowinds.Passdown.Updater.exe`).

**B. Signing**

3. **Done (2026-10-18):** the signing key `carowinds-release-1` exists at
   `~/.carowinds-signing/carowinds-release-1.pem` and its public key is already in `appsettings.json`, so
   every build from now on can receive updates. Keep a backup of the `.pem` file in a second safe place;
   never email, commit or upload it. (For a future key rotation, `generate-key --private-key-out <file
   outside the repository>` creates a new pair; see §4.)
4. Write the notes users should see before installing into a text file (e.g. `notes.md`).
5. `dotnet run --project tools/update-signing -- create-manifest --zip artifacts/releases/Carowinds-Operations-0.1.0-rc.8-win-x64.zip --version 0.1.0-rc.8 --channel release-candidate --title "…" --notes notes.md`
6. `dotnet run --project tools/update-signing -- sign --manifest artifacts/releases/Carowinds-Operations-0.1.0-rc.8-win-x64.update.json --key-id carowinds-release-1 --private-key ~/.carowinds-signing/carowinds-release-1.pem`
7. `dotnet run --project tools/update-signing -- verify --manifest … .update.json --signature … .update.json.sig --appsettings src/Carowinds.Passdown.Desktop/appsettings.json`
   must print `VERIFIED`.

**C. Testing** — install the *previous* updater-enabled version on a Windows test PC, publish the new release
as a **draft** first if you want to rehearse (drafts are ignored by the app), then follow §12.

**D. Approving distribution** — before any upload to the public repository, the open questions in §14 must
be resolved in writing. Uploading makes the files public.

**E. Uploading** — on github.com → the repository → Releases → *Draft a new release*: tag
`v0.1.0-rc.8` (exactly `v` + version), title of your choice, attach the three files (`.zip`,
`.update.json`, `.update.json.sig`) with their names unchanged, tick "Set as a pre-release" for RCs, then
*Publish release*. Do not edit the files after publishing; to fix something, publish a new version.

**F. Installing** — users open Settings → Application Updates (or wait for the dot on the gear), read the
notes, choose **Install Update**, then **Restart and Install**.

*Optional future:* a GitHub Actions workflow could build and attach artifacts, but signing must stay with
the offline key holder. Not implemented (no unsolicited publishing workflow).

## 11. Tests

| Project | New | Covers |
|---|---|---|
| Domain | +49 | SemVer parsing/precedence (rc.9 < rc.10 < stable, not alphabetical), channel policy, no downgrade |
| Application | +67 | Discovery fixtures: no releases, newer, older, same, multiple RCs, stable vs prerelease, draft, missing assets, bad tag, invalid signature, replayed manifest, size mismatch, offline/timeout/HTTP/invalid/rate-limit, not configured (no network), ≤3 manifests; coordinator timing, coalescing, back-off persistence, snooze, acknowledgement; release-notes sanitizing/bounds; bundled notes parser; What's New policy (success, repeat, next update, clean install, every failure status); post-update confirmation; config parsing |
| Infrastructure | +102 | Real ECDSA with disposable keys: valid, tampered, untrusted/unknown key, malformed signature files, no keys, invalid configured keys, rotation/revocation, wrong product/architecture/channel/format, unsafe/duplicate paths, missing updater; ZIP traversal (`..`, `\`, absolute, drive, ADS), unexpected/duplicate/missing entries, symlinks, content mismatch, non-ZIP; staging success, corrupt download, interrupted download, disk space, fresh-download rule, cleanup; GitHub provider (public endpoint, no auth, ETag, rate-limit headers, Retry-After, HTTP errors, offline, cancellation, malformed/oversize, constructed URLs, redirect allowlist/HTTPS/port/user-info/loop, size caps, progress); installer engine (success + data preservation, obsolete-file removal sparing user files, no confirmation → kill + rollback + single restart, crash, start failure, mid-swap failure, recovery required + note, locked exe, other instance, app not closing, disk space, permission denied, tampered/extra staged files, request outside folder, wrong updater folder, downgrade/product/path in request, non-install folder, concurrent updater, confirmation binding); launcher (argument list, refusal, cleanup); inspector; signing tool (round trip, missing key fails closed, key in git tree refused, no overwrite, name/channel checks, invalid manifest); repo contains no private keys; local state file and path separation |
| Ui (mixed) | +42 | View-model states and wording, no double check, failures, available offer + safe notes, readiness explanations, download progress/cancel/errors, background check vs download, explicit restart, Not Now, launcher failures, unwritable at restart, **Phase 23A failed save postpones (no loop/freeze/close/false save) then succeeds after recovery**, previous-attempt messages, indicator + snooze + Narrator help text, What's New open/acknowledge/mismatched/missing, bundled notes = built version, markup names/live regions/order, theme tokens only |
| Ui (run-alone `Phase15ERealWindowLayoutTests`) | +4 | Real window: Settings update panel, gear dot and What's New in System-Light, System-Dark, Light, Dark — no wrong-theme brushes, no clipping/horizontal overflow, appearance preference never changed |

Existing tests changed: `PortablePackagingDataSafetyTests` allow-list (+ the updater's temporary publish
folder, asserted to be under `artifacts/`). The Settings gear kept its name, content and contract (the dot
is a separate decorative element).

**Totals:** **4437/4437** = 270 Domain + 1167 Application + 976 Infrastructure + 463 Documents + 1413 Ui
mixed + 26 `OperationsCalendarViewTests` + 109 `Phase15ERealWindowLayoutTests` + 2
`RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests`
(`-c Release`, `--filter "Category!=LiveSmoke"`; run-alone set unchanged). No test contacts GitHub.
Release build 0 warnings, 0 errors.

**Build-host packaging check:** `dotnet publish src/Carowinds.Passdown.Updater -c Release -r win-x64
--self-contained true` produces `PE32+ (GUI) x86-64` `Carowinds.Passdown.Updater.exe` with the `asInvoker`
manifest embedded and no build-machine path. `Publish-Windows.ps1` / `Build-PortableRelease.ps1` were
**not run** (no PowerShell on this host); `release-content-rules.json` now requires the four
`Carowinds.Passdown.Updater.*` files and forbids update/state artifacts.

## 12. Windows 11 x64 validation checklist — ALL NOT EXECUTED

Use signed **synthetic test releases** (test key, a private/fork repository or drafts promoted on a test
repository) — never a production release. Configure the test build's `Updates:Repository` and
`TrustedSigningKeys` accordingly.

| # | Check | Expected | Status |
|---|---|---|---|
| 1 | Extract to a user-writable folder (e.g. Documents) | Runs; Settings shows installed version | NOT EXECUTED |
| 2 | Fresh updater-enabled install | No What's New on first launch; About → What's New works | NOT EXECUTED |
| 3 | Update discovery | ~90 s after launch or Check for Updates finds the signed test release | NOT EXECUTED |
| 4 | Notification | Dot on gear; no popup; Remind Me Later hides it | NOT EXECUTED |
| 5 | Notes before install | Signed notes, date, size shown | NOT EXECUTED |
| 6 | Download progress | Bar + text; Cancel works and leaves nothing | NOT EXECUTED |
| 7 | Verification | Tampered ZIP / wrong key / edited manifest rejected | NOT EXECUTED |
| 8 | User confirmation | Nothing closes before Restart and Install | NOT EXECUTED |
| 9 | App shutdown | Draft saved; app closes | NOT EXECUTED |
| 10 | External updater | Starts from `%LOCALAPPDATA%\Carowinds Operations Updates\staging\…`; no window; no console | NOT EXECUTED |
| 11 | File replacement | Folder contains new files; user files kept | NOT EXECUTED |
| 12 | Restart | New version opens automatically | NOT EXECUTED |
| 13 | Version | About shows new version | NOT EXECUTED |
| 14 | What's New | Opens once automatically | NOT EXECUTED |
| 15 | Next launch | No What's New | NOT EXECUTED |
| 16 | Reopen | Settings → About → What's New | NOT EXECUTED |
| 17 | Data preserved | Drafts, notes, prices, History PDFs intact | NOT EXECUTED |
| 18 | Personal Sync | Still configured and syncing | NOT EXECUTED |
| 19 | Dark mode | Saved appearance unchanged | NOT EXECUTED |
| 20 | Failed update & recovery | Test release whose exe fails to start → previous version restored and reopened once; Settings explains; manual recovery note when rollback impossible | NOT EXECUTED |
| 21 | Locked installation | Second instance or scheduled refresh running → "not installed", nothing changed, nothing killed | NOT EXECUTED |
| 22 | Protected folder | `C:\Program Files\…` copy → Manual update required, no UAC | NOT EXECUTED |
| 23 | No unexpected UAC | Updater never prompts (asInvoker) | NOT EXECUTED |
| 24 | Failed save | Make the database read-only/locked, edit a draft, Restart and Install → postponed, edits kept, no freeze; fix and retry succeeds | NOT EXECUTED |
| 25 | Antivirus / SmartScreen | Note any SmartScreen or Defender prompt for the unsigned (Authenticode) updater | NOT EXECUTED |

## 13. macOS limitation

All of the above was built and tested on macOS. The engine was exercised against temporary folders with
a fake process environment and POSIX permissions; Windows file locking, ACLs, process start, restart,
rollback and the real updater executable have **not** been run on Windows. Nothing here claims a Windows
deployment.

## 14. Distribution approval requirements (blocking any public upload)

- **QuestPDF Community licence eligibility** for the intended organisational use — pending.
- **Fury 325 photograph redistribution rights** — pending.
- **Organisation policy** on distributing internal tooling through a personal public GitHub repository —
  pending.
- GitHub release assets in a public repository are downloadable by anyone. Implementing and testing the
  updater does not authorise publication. No release was created or uploaded.

## 15. Known limitations

- **One manual install:** RC.7 and earlier have no updater, so RC.8 (the first build that trusts
  `carowinds-release-1`) must be installed from its ZIP once; later versions can update in-app.
- **Single key, single location:** the private key currently exists only on the build Mac until the owner
  backs it up; losing it means distributing the next version manually and rotating keys (§4).
- Executables are not Authenticode-signed; SmartScreen/Defender behaviour unverified (§12 #25).
- The updater has no window of its own; during an update the user sees the app close and reopen
  (seconds to ~1 minute). A failure is reported when the previous version reopens.
- If the window's own close is held back by a *second* save failure after the updater started (rare race),
  the updater gives up after 3 minutes and records "app did not close"; nothing changes.
- A rename-capable lock or antivirus scan starting mid-swap is handled by rollback, not prevented.
- Obsolete files from versions installed by ZIP extraction (before the first in-app update) are not
  removed; harmless to the runtime.
- The GitHub listing reads the first 30 releases only.
- Mixed versions across PCs sharing a Personal Sync folder remain possible (existing caution); updating
  all PCs together is still recommended.
- The experimental single-file EXE is not updatable in place (shows Manual update required).
