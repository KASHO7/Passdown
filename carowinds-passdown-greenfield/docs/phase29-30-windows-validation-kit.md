# Phase 29 + 30 — combined Windows validation kit

**Status: PREPARED, NOT VALIDATED.** Every Windows and Teams check is **NOT EXECUTED** until it is run on the
Windows 11 test VM. Nothing was pushed, published, uploaded or released; no GitHub Release exists for this
build; production updater settings are unchanged; no RC.10 was packaged.

## 1. Baseline

| Item | Value |
| --- | --- |
| Branch / source commit | `feature/passdown-redesign` @ `984d55c` (Phase 30; contains Phase 29 `fb6cc67`, 29A `086b90a`, the RC.9 price-history fold fix `780ceac`) |
| Version | `0.1.0-rc.9` / file version `0.1.0.9` (unchanged) |
| Database / sync schema | DB 17, `SyncSchemaVersion` 1 (unchanged) |
| Working-tree changes for this kit | `packaging/windows/Build-PortableRelease.ps1` (test-build mode + guard), two tests, this document, `tools/phase29-30-windows-kit/`. **No application source changed**: the packaged binaries are exactly `984d55c` |

## 2. Release-script overwrite risk (Phase 29A finding F2) — fixed

`Build-PortableRelease.ps1`:

- **Frozen-release guard.** Without `-LocalTestLabel`, the script stops with an error when
  `artifacts/releases/Carowinds-Operations-<Version>-win-x64.zip` already exists — before publishing and
  before any `Remove-Item`. At the current `0.1.0-rc.9` it therefore refuses to run instead of wiping the
  frozen RC.9 staging folder and ZIP.
- **`-LocalTestLabel <label>`.** Publish output, staging folder and ZIP all go under
  `artifacts/local-test/<label>/`, named `LOCAL-ONLY-<label>-win-x64`, with a `LOCAL-ONLY-TEST-BUILD.txt`
  marker (label, version, commit, "not a release"). `artifacts/releases`, `artifacts/release-staging` and
  `artifacts/publish/win-x64` are never read, written or deleted. The label is restricted to
  `[A-Za-z0-9][A-Za-z0-9._-]{0,63}`; `-SkipPublish` cannot be combined with it. The ZIP's SHA-256 is printed.

Verification: two new static-contract tests in `PortablePackagingDataSafetyTests`; the guard was run with
PowerShell 7.6.8 (local tool in the session scratchpad) against a scratch copy of the repository with a fake
"frozen" RC.9 ZIP and staging folder: it refused, the fake publish script never ran, both fake files were
unchanged; a path-like label and `-SkipPublish` with a label were both refused. The real test package below was
then built by the script itself in `-LocalTestLabel` mode.

## 3. Test build

| | |
| --- | --- |
| File | **`LOCAL-ONLY-phase29-30-morning-message-sync-win-x64.zip`** |
| Location (build host, gitignored) | `artifacts/local-test/phase29-30-morning-message-sync/` (also inside the kit folder) |
| Bytes | **76,229,467** |
| SHA-256 | **`1898b3f930a6bc98548b8a39d2dc1d2112ae4c20cdf83c323157c59b10d6ea2b`** |
| Source commit | `984d55c11ccb46623dc88f71144f7dba47f370ae` |
| Application version | `0.1.0-rc.9+984d55c11ccb46623dc88f71144f7dba47f370ae` in all six packaged assemblies; About this build reads **`0.1.0-rc.9 (984d55c)`** (frozen RC.9: `e20d846`) |
| Shape | win-x64, self-contained, multi-file, same publish flags as RC.9; one top-level folder `LOCAL-ONLY-phase29-30-morning-message-sync-win-x64\`; 288 files = frozen RC.9 file list + `LOCAL-ONLY-TEST-BUILD.txt` |

Audit (build host): `Test-ReleasePackage.ps1` PASS on staging and ZIP (run by the script); C# validator
`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip` PASS (a bogus path fails, so it ran); `unzip -t` OK; fresh
extraction `diff -rq`-identical to staging; both executables `PE32+ (GUI) x86-64`; no `.pdb`, keys, databases,
logs, PDFs, `.DS_Store`, `__MACOSX`, `.dylib`/`.so`, `.update.json(.sig)`, `appearance.json`,
`sync-config.json`, `morning-message*.json` or `device-id.txt`; no `ghp_`/`github_pat_`/`PRIVATE KEY`; no
build-user name or checkout path in text or binaries; `appsettings.json` and `Infrastructure/Updates`
unchanged since `984d55c`.

## 4. Kit

| | |
| --- | --- |
| Kit folder | `artifacts/local-test/phase29-30-morning-message-sync/LOCAL-ONLY-phase29-30-test-kit/` |
| Kit as one file | `LOCAL-ONLY-phase29-30-test-kit.zip` — 75,994,677 bytes, SHA-256 `b59bce1fbc7eb45b44f684c9903712ab0380ab6a8fe0168ea3644a8861c02e10` |
| Contents | test ZIP, `SHA256SUMS.txt`, `WINDOWS-TEST-GUIDE.txt`, `scripts\` (Phase 29A clipboard checkers 1–4 + Phase 30 sync helpers 5–8), `synthetic-input\`, `expected\` |
| Committed sources | `tools/phase29-30-windows-kit/` (guide, sync helper, launchers, README); Phase 29A materials reused from `tools/phase29a-windows-kit/` |

`WINDOWS-TEST-GUIDE.txt` parts: A safety/set-up (hash, verified backups, Personal Sync paused, new empty
folder), B Morning Message composer + real clipboard + Teams desktop + Teams in Edge + single Copy options +
OD Notes separation, C enabling sync in both accounts (off by default, both can enable, folder access),
D sync scenarios (content/preferences follow, independent edits merge, same-field conflict with both
choices, composer-open hold, disable keeps data, re-enable reconciles offline edits, one account off, folder
unavailable, Teams after sync), E draft/note/official price-history sync regression, F finish (old shared
folder verified unchanged, no switching back to it).

### Sync folder and account safety

- Accounts: `Kishan` = Computer A, `CarowindsTestB` = Computer B (the existing VM test accounts).
- `Phase30-SyncTest.ps1 -Mode Prepare` (elevated, app closed in both accounts) stops at the first problem.
  In order: copies both `%LOCALAPPDATA%\Carowinds Passdown` folders and the existing
  `C:\Users\Public\CarowindsSyncTest\Carowinds Operations Sync` to
  `C:\CarowindsEvidence\Phase30-backup-<timestamp>\` and compares every file's SHA-256; writes
  `SHA256SUMS.csv` and a fingerprint of the existing shared folder; prints each account's sync settings;
  sets `PersonalSyncEnabled` to `false` in each `sync-config.json` (regex on that one value, atomic replace,
  folder/device id/everything else kept — otherwise the first launch of the test build would sync with the
  existing folder and could publish into it); refuses on Deny entries; creates the new empty
  `C:\Users\Public\CarowindsSyncTest\Phase30-MorningMessage-Test` (refuses if it exists and is non-empty)
  and grants both accounts Modify (inherited) on that folder only; verifies the grant.
- `CheckAccess` proves each account can write in every subfolder the other created; `Inspect` is read-only
  and prints text lengths only; `VerifyOld` re-hashes the existing shared folder against the fingerprint.
- The guide forbids pointing either account back at the old folder at the end (it would merge test edits into
  the earlier test data); restoring from the backup is left to the developer.
- The scripts were parse-checked with PowerShell 7.6.8 (0 errors), are ASCII-only, and the config pause
  replacement was exercised on a sample `sync-config.json`. **Their Windows-only parts (ACLs, cross-profile
  copies, process checks, clipboard) have not run anywhere yet.**

## 5. Tests and build

Deterministic suite (Release, `Category!=LiveSmoke`, five real-Window UI classes alone): **4,951 passed,
0 failed** (baseline 4,949; +2 new packaging-script tests). Domain 303 · Application 1,330 · Infrastructure
1,136 · Documents 525 · Ui mixed 1,505 · run-alone 26 + 113 + 2 + 7 + 4.

Clean Release build (`dotnet clean` + `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`):
**0 warnings, 0 errors.**

## 6. Frozen artifacts

SHA-256 of the RC.8 and RC.9 ZIP, single-file EXE, `.update.json` and `.sig` were recorded before any build and
re-verified after the package and kit were built: **unchanged** (RC.9 ZIP `e1ad521f…1558`, EXE `5fa0769f…faa9`,
manifest `936b196c…160f`, signature `af82eaf5…93e2`, matching `docs/release-manifest.md`). A tree hash of
`artifacts/release-staging/Carowinds-Operations-0.1.0-rc.9-win-x64` and `artifacts/publish/` is also unchanged.
The Phase 29A test ZIPs in `artifacts/local-test/phase29a/` were not touched.

## 7. Windows results

All rows of the guide's results table: **NOT EXECUTED**. Fill in from the tester's `RESULTS.txt`,
`Desktop\Phase29A-results` and `Desktop\Phase30-results` (both accounts).
