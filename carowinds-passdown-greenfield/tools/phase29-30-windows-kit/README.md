# Phase 29 + 30 Windows test kit

Combined Windows 11 validation kit for the Phase 29 Morning Message / Teams copy and the Phase 30 optional
Morning Message sync, on the test VM with two Windows accounts (`Kishan` = Computer A, `CarowindsTestB` =
Computer B). Record: `docs/phase29-30-windows-validation-kit.md`.

| Path | What |
| --- | --- |
| `WINDOWS-TEST-GUIDE.txt` | The tester's step-by-step guide and results table (plain text, ASCII, Notepad-friendly). Every check starts NOT EXECUTED |
| `scripts/Phase30-SyncTest.ps1` | Sync-test helper. `Prepare` (elevated): verified backups of both accounts' app data and the existing shared sync folder, fingerprint of that folder, Personal Sync paused in both accounts, Deny-rule check, new empty `C:\Users\Public\CarowindsSyncTest\Phase30-MorningMessage-Test` with Modify for both accounts on that folder only. `CheckAccess`: per-account write/delete probe in every test sync subfolder. `Inspect`: read-only view (text lengths only, never text). `VerifyOld`: read-only check that the existing shared sync folder still matches the fingerprint |
| `scripts/5`-`8 *.cmd` | Double-click launchers for the four modes (`-ExecutionPolicy Bypass` for that process only) |

The Phase 29A clipboard materials are reused unchanged from `tools/phase29a-windows-kit/` (`scripts/1`-`4`,
`Inspect-TeamsClipboard.ps1`, `Hold-Clipboard.ps1`, `synthetic-input/`, `expected/`) and copied into the kit
folder when it is assembled. The Phase 29A checklist itself is not shipped in this kit: `WINDOWS-TEST-GUIDE.txt`
supersedes it (different build, backups instead of renaming the profile, Personal Sync used in Parts C-E).

Scripts are ASCII-only (Windows PowerShell 5.1 reads a `.ps1` without a BOM as the ANSI code page).

## Building the test package

```bash
pwsh -NoProfile -File packaging/windows/Build-PortableRelease.ps1 -LocalTestLabel phase29-30-morning-message-sync
```

`-LocalTestLabel` keeps publish output, staging and ZIP under `artifacts/local-test/<label>/`
(`LOCAL-ONLY-<label>-win-x64.zip`, with a `LOCAL-ONLY-TEST-BUILD.txt` marker) and never touches
`artifacts/releases`, `artifacts/release-staging` or `artifacts/publish/win-x64`. Without it the script now
refuses to run when `artifacts/releases/Carowinds-Operations-<Version>-win-x64.zip` already exists.

## Assembling the kit folder

Copy into `artifacts/local-test/phase29-30-morning-message-sync/LOCAL-ONLY-phase29-30-test-kit/`: the test ZIP,
`SHA256SUMS.txt`, `WINDOWS-TEST-GUIDE.txt`, this kit's `scripts/` plus the Phase 29A `scripts/`,
`synthetic-input/` and `expected/`.
