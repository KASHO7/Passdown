# Phase 7E — Windows 11 manual validation checklist (fillable)

Run this on a real Windows 11 machine/VM. Fill in every `Result:` line as you go — write what
actually happened, not what you expect. If a step fails, keep going through the rest unless it
blocks a later step; note the failure and move on. Bring this file (or your filled-in copy) back
to Claude afterward so results can be recorded in `docs/release-strategy.md` and any real defect
can be fixed.

**Do not delete real user data. Use a disposable/test Windows profile or VM snapshot wherever this
checklist says "disposable."**

## Artifact under test

- File: `Carowinds-Passdown-0.1.0-dev-win-x64.zip`
- SHA-256: `94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`
- Size: 61 MB compressed, 279 zip entries, ~132.9 MB uncompressed
- Location in repo: `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`

Copy **only this ZIP** to the Windows machine (e.g. via USB, network share, or cloud transfer —
not the whole repo). After copying, verify integrity in PowerShell:

```powershell
Get-FileHash "C:\path\to\Carowinds-Passdown-0.1.0-dev-win-x64.zip" -Algorithm SHA256
```

Result must match the hash above exactly. Record: 94064EEE63A2CB7BC0D30E83D45AFDE946FF02DBB3B5D3B9AEAA9F7D16871AB0

---

## Part A — Test environment

Record:

- Windows edition/version/build (`winver` or `Get-ComputerInfo | Select WindowsProductName,WindowsVersion,OsBuildNumber`):PS C:\Users\Kishan\Downloads> Get-ComputerInfo | Select WindowsProductName,WindowsVersion,OsBuildNumber

WindowsProductName WindowsVersion OsBuildNumber
------------------ -------------- -------------
Windows 10 Pro     2009           26200



- Architecture (`$env:PROCESSOR_ARCHITECTURE`): ARM64
- Display scaling (Settings → System → Display): ______________________
- Was .NET SDK/runtime already installed before this test? (`dotnet --info`, or note if `dotnet` isn't found at all): PS C:\Users\Kishan\Downloads> dotnet --info
.NET SDK:
 Version:           10.0.400
 Commit:            14fbf8d527
 Workload version:  10.0.400-manifests.330ea142
 MSBuild version:   18.9.6+14fbf8d52

Runtime Environment:
 OS Name:     Windows
 OS Version:  10.0.26200
 OS Platform: Windows
 RID:         win-arm64
 Base Path:   C:\Program Files\dotnet\sdk\10.0.400\

.NET workloads installed:
There are no installed workloads to display.
Configured to use workload sets when installing new manifests.
No workload sets are installed. Run "dotnet workload restore" to install a workload set.

Host:
  Version:      10.0.11
  Architecture: arm64
  Commit:       e2f47b0110

.NET SDKs installed:
  10.0.400 [C:\Program Files\dotnet\sdk]

.NET runtimes installed:
  Microsoft.AspNetCore.App 10.0.11 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
  Microsoft.NETCore.App 10.0.11 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
  Microsoft.WindowsDesktop.App 10.0.11 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]

Other architectures found:
  None

Environment variables:
  Not set

global.json file:
  Not found

Learn more:
  https://aka.ms/dotnet/info

Download .NET:
  https://aka.ms/dotnet/download

If .NET was already present, note that clearly — it does **not** prove the self-contained
requirement. Prefer a clean VM/snapshot without a separately installed .NET runtime if practical.

**Result:** PASS / PARTIAL / NOT EXECUTED — PASS

---

## Part B — Extract the real ZIP

Extract into a normal user-writable path **with spaces** in it, e.g.:

```
C:\Users\<You>\Downloads\Carowinds Passdown Test\
```

Record:

- Extracted folder path: Dowloads folder
- Approx. extracted size (`(Get-ChildItem -Recurse | Measure-Object Length -Sum).Sum / 1MB`): ______________________

Verify root contains: `Carowinds.Passdown.Desktop.exe`, `README.txt`,
`Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`, `appsettings.json`, runtime DLLs,
`tools\`.

**Result:** PASS / FAIL — PASS

---

## Part C — Clean first launch

Before launching, confirm a clean state for this test profile:

```powershell
Test-Path "$env:LOCALAPPDATA\Carowinds Passdown"
```

Should be `False` (or intentionally isolated — a fresh test profile/VM). If `True` and this isn't
a disposable profile, back up/rename it first rather than deleting it.

Double-click `Carowinds.Passdown.Desktop.exe`. Verify:

- [ ] App opens
- [ ] No terminal/console window required or left open
- [ ] No ".NET install" prompt
- [ ] No missing-DLL/runtime error
- [ ] No UAC/admin prompt
- [ ] Main Associate UI renders correctly

After launch:

```powershell
Test-Path "$env:LOCALAPPDATA\Carowinds Passdown"
Get-ChildItem "$env:LOCALAPPDATA\Carowinds Passdown"
```

Should now be `True`, with `carowinds-passdown.db` (+ possibly `-wal`/`-shm`) present.

**Result:** PASS / FAIL — PASS

---

## Part D — Basic UI smoke

Check:

- [ ] Main window opens at a sensible size
- [ ] Carowinds blue/white/red theme renders correctly
- [ ] Controls are readable
- [ ] Date selector works
- [ ] Changing the selected date does **not** trigger a network refresh (no spinner/online activity)
- [ ] Main sections render
- [ ] Editable fields accept input
- [ ] Product sections render
- [ ] No Manager workflow/nav appears anywhere

Exercise: change date, edit a few text/numeric fields, try include/exclude product, try custom
product flow if data is available.

**Result:** PASS / PARTIAL / FAIL — PASS

---

## Part E — Display scaling

Test at 100%, 125%, 150% (Settings → System → Display → Scale). Reload/relaunch app at each.

| Scale | Clipped controls? | Inaccessible buttons? | Horizontal overflow? | Text readable? | Notes |
|---|-------------------|-----------------------|----------------------|----------------|-------|
| 100% | Pass              | Pass                  | Pass                 | Pass           | None  |
| 125% | Pass              | Pass                  | Pass                 | Pass           | None  |
| 150% | Pass              | Pass                  | Pass                 | Pass           | None  |

Separate cosmetic issues from release blockers.

**Result:** PASS / PARTIAL / FAIL — PASS

---

## Part F — First-run persistence

1. Enter a recognizable test draft (projected attendance, an operational note, Daily Fun text, one
   product override or custom product). Value(s) used: Carti Pass $69.42
2. Wait for autosave, or trigger a save-flushing action.
3. Close the app normally.
4. Reopen `Carowinds.Passdown.Desktop.exe`.
5. Confirm the same draft values persist.

**Result:** PASS / FAIL — PASS

---

## Part G — Check Online

Use the app's "Check Online" action. Verify:

- [ ] Refresh starts only from the explicit action (not automatically)
- [ ] Progress/status behaves normally, UI stays usable
- [ ] Successful data appears where expected
- [ ] A failure (if one occurs) does not wipe previously-good data
- [ ] No browser/Playwright installation prompt appears
- [ ] No missing-Playwright/runtime error (Playwright was removed in Phase 7B — this is the key
      regression to watch for)

If internet/source behavior is temporarily unavailable, record that honestly and separate it from
a product failure.

**Result:** PASS / PARTIAL / FAIL — PASS

---

## Part H — PDF generation

Generate an Associate passdown PDF. Verify:

- [ ] Save dialog opens
- [ ] PDF created successfully, no developer path needed
- [ ] Opens in a normal Windows PDF viewer
- [ ] Branding/layout correct
- [ ] Content matches entered data
- [ ] No obvious clipping/truncation
- [ ] (Optional) Deliberately add enough content to test overflow behavior

**Result:** PASS / FAIL — PASS

---

## Part I — Enable Automatic Refresh

Double-click `Enable Automatic Refresh.cmd` from the extracted folder. Verify:

- [ ] No manual PowerShell typing required
- [ ] No permanent execution-policy change
- [ ] No UAC/admin prompt
- [ ] Resolves the extracted exe automatically
- [ ] Understandable success output; window stays open long enough to read

Then inspect:

```powershell
Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh"
Get-ScheduledTaskInfo -TaskName "Carowinds Passdown Daily Refresh"
(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Actions
(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Triggers
(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Settings
(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Principal
```

Verify:

- [ ] Task exists
- [ ] Executable path points into THIS extracted release
- [ ] Argument is `--scheduled-refresh`
- [ ] Working directory correct
- [ ] Daily 06:00 local time
- [ ] `StartWhenAvailable = True`
- [ ] `MultipleInstances = IgnoreNew`
- [ ] No network-required precondition
- [ ] Allowed on battery
- [ ] Current-user, limited privilege (not SYSTEM, not elevated)

**Result:** PASS / FAIL — PASS

---

## Part J — Manual scheduled refresh

```powershell
Start-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh"
```

Verify:

- [ ] The normal desktop app window does NOT appear
- [ ] Task completes
- [ ] Scheduled-refresh log updates (see Part K)

```powershell
Get-ScheduledTaskInfo -TaskName "Carowinds Passdown Daily Refresh"
```

Record: LastRunTime  8/22/2026 10:30:58 AM LastTaskResult 0 NextRunTime  8/23/2026 6:00:00 AM

A genuine remote-source partial failure (e.g. a source site down) is not a packaging failure if
the app reports/logs it correctly — note that distinction if it happens.

**Result:** PASS / FAIL — PASS

---

## Part K — Scheduled log location

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Carowinds Passdown\logs"
Get-Content "$env:LOCALAPPDATA\Carowinds Passdown\logs\scheduled-refresh-$(Get-Date -Format yyyyMMdd).log" -Tail 40
```

Verify:

- [ ] `scheduled-refresh-yyyyMMdd.log` exists after the Part J run
- [ ] Contains useful start/completion info
- [ ] No secrets/full payload dumps
- [ ] No developer-machine path leakage

**Result:** PASS / FAIL — PASS

---

## Part L — Disable Automatic Refresh

Double-click `Disable Automatic Refresh.cmd`. Verify:

- [ ] No admin/UAC prompt
- [ ] Understandable result text
- [ ] Task removed

```powershell
Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh" -ErrorAction SilentlyContinue
```

Expected: no task returned.

- [ ] LocalAppData still present (db/drafts/logs untouched)

**Result:** PASS / FAIL — PASS

---

## Part M — Re-enable / idempotency

1. Double-click `Enable Automatic Refresh.cmd` again — task recreates cleanly.
2. Run `Enable Automatic Refresh.cmd` a **second time without disabling first**.
3. Verify no duplicate task, and only one `Carowinds Passdown Daily Refresh` task exists:

```powershell
(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Count
```

Should be a single task object (not an array of 2).

**Result:** PASS / FAIL — PASS

---

## Part N — Portable move test

1. Close the app.
2. Copy/move the extracted folder to a new user-writable path **with spaces**, e.g.
   `C:\Users\<You>\Desktop\Admissions Tools\Carowinds Passdown\`. Do **not** move LocalAppData.
3. Launch the app from the new location. Verify:
   - [ ] App opens
   - [ ] Existing draft/data still present
   - [ ] No path-related startup failure
   - [ ] `appsettings.json` resolves correctly
   - [ ] PDF still works
4. **Before** re-enabling, inspect the scheduled task — it should still point at the OLD path:

```powershell
(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Actions
```

5. Double-click `Enable Automatic Refresh.cmd` from the NEW folder. Verify:
   - [ ] Same task updated (still exactly one task)
   - [ ] Executable path now points to the NEW folder
   - [ ] No duplicate task
   - [ ] `Start-ScheduledTask` still works from here

**Result:** PASS / FAIL — PASS

---

## Part O — Re-extract / update test

1. Close the app (keep LocalAppData untouched).
2. Extract a **fresh copy** of the same ZIP into a new folder.
3. Launch the fresh copy.
4. Verify the previously-created draft/data is still there.
5. Verify no duplication/reset of data.
6. Re-enable automatic refresh from the fresh path if needed.

**Result:** PASS / FAIL — PASS

---

## Part P — Portable delete semantics

On a disposable extracted copy:

1. Disable automatic refresh if it points at that copy.
2. Close the app.
3. Delete the extracted application folder.
4. Verify `%LOCALAPPDATA%\Carowinds Passdown` still exists with database, drafts/data, logs.
5. Re-extract the ZIP elsewhere and launch again — verify data returns.

**Result:** PASS / FAIL — PASS

---

## Part Q — Unknown legacy file coexistence

```powershell
"legacy test content — do not delete me" | Out-File "$env:LOCALAPPDATA\Carowinds Passdown\legacy-test-file.txt"
"legacy log — do not delete me" | Out-File "$env:LOCALAPPDATA\Carowinds Passdown\logs\legacy-test.log"
```

Launch/use/close the app, run a manual scheduled refresh (Part J again is fine). Then:

```powershell
Get-Content "$env:LOCALAPPDATA\Carowinds Passdown\legacy-test-file.txt"
Get-Content "$env:LOCALAPPDATA\Carowinds Passdown\logs\legacy-test.log"
```

Verify both files still exist, unchanged.

**Result:** PASS / FAIL — PASS

---

## Part R — Corrupt database safety

**Disposable test profile only. Back up first.**

```powershell
$dbPath = "$env:LOCALAPPDATA\Carowinds Passdown\carowinds-passdown.db"
Copy-Item $dbPath "$dbPath.backup"

# Close the app first, then corrupt the live copy (overwrites the SQLite header magic string,
# which is enough to make it fail to open as a database without needing a SQLite CLI tool):
$bytes = [System.Text.Encoding]::ASCII.GetBytes("THIS-IS-NOT-A-VALID-SQLITE-DATABASE-FILE-TEST")
$fs = [System.IO.File]::Open($dbPath, 'Open', 'Write')
$fs.Write($bytes, 0, $bytes.Length)
$fs.Close()
```

1. Launch the packaged app (interactive). Verify:
   - [ ] The corrupt file is not silently deleted/replaced
   - [ ] Failure occurs (crash is acceptable per spec — "loud, not silent" — just must not destroy
         the file)
2. Test headless:

```powershell
& "C:\path\to\Carowinds.Passdown.Desktop.exe" --scheduled-refresh
echo $LASTEXITCODE
```

   - [ ] No UI appears
   - [ ] Fatal/nonzero exit
   - [ ] Log reflects the failure

3. Restore the known-good DB afterward:

```powershell
Copy-Item "$dbPath.backup" $dbPath -Force
```

**Result:** PASS / FAIL — PASS

---

## Part S — Newer-schema guard

**Disposable DB copy only.**

This sets `PRAGMA user_version` past the current highest migration without needing a SQLite CLI —
SQLite stores that value as a big-endian 32-bit integer at byte offset 60 of the file header:

```powershell
$dbPath = "$env:LOCALAPPDATA\Carowinds Passdown\carowinds-passdown.db"
$testCopy = "$env:TEMP\newer-schema-test.db"
Copy-Item $dbPath $testCopy -Force

$bytes = [System.IO.File]::ReadAllBytes($testCopy)
$verBytes = [BitConverter]::GetBytes([int32]9999)
[Array]::Reverse($verBytes)   # SQLite stores this big-endian
[Array]::Copy($verBytes, 0, $bytes, 60, 4)
[System.IO.File]::WriteAllBytes($testCopy, $bytes)

# Point the app at this test copy instead of the real one, e.g. by temporarily swapping it in
# under a disposable LocalAppData profile, then launch:
Copy-Item $testCopy $dbPath -Force   # ONLY if $dbPath is already a disposable test copy, not real data
```

Launch the packaged app against that DB. Verify:

- [ ] `UnsupportedSchemaVersionException` path triggers (app fails to open the DB normally rather
      than silently proceeding)
- [ ] Database is not downgraded or reset — its bytes are unchanged after the failed attempt
- [ ] Headless (`--scheduled-refresh`) also returns fatal behavior against this DB

Restore the normal DB afterward. If this manual mutation proves impractical in your environment,
mark this **NOT EXECUTED** rather than guessing — the automated `MigrationRunnerTests` coverage
already exercises this at the unit level.

**Result:** PASS / FAIL / NOT EXECUTED — PASS

---

## Part T — Startup catch-up coexistence (best effort)

With automatic refresh enabled: start a scheduled refresh manually (Part J) and, around the same
time, launch the normal app. Verify:

- [ ] UI opens
- [ ] Only one refresh actually proceeds (shared lock)
- [ ] No corruption, no fatal overlap error

If the refresh finishes too fast to reproduce reliably, mark **NOT EXECUTED** — do not add
artificial delays to production code for this. Existing automated Phase 6 coverage may stand.

**Result:** PASS / NOT EXECUTED — PASS

---

## Part U — No .NET/dev dependency check

Confirm normal use never required: `dotnet run`, .NET SDK, the repository, Visual Studio, Git,
Python, Node, a Playwright browser install, or admin rights. Normal use was: extract, double-click.
Scheduled setup was: double-click helper.

**Result:** PASS / FAIL — PASS

---

## Part V — Release content inspection on Windows

In the extracted folder, confirm absence of: `.pdb`, any `Playwright`/`.playwright` folder, any
`*.Tests.dll`, source files, `.git`/`.claude`, a real user database, developer logs, macOS/Linux
native binaries (`.dylib`/`.so`), developer-specific absolute paths in `README.txt`/
`appsettings.json`.

```powershell
Get-ChildItem -Recurse | Where-Object { $_.Extension -in '.pdb','.dylib','.so' -or $_.Name -like '*playwright*' -or $_.Name -like '*.Tests.dll' }
```

Should return nothing.

**Result:** PASS / FAIL — PASS

---

## Part W — Build scripts on real PowerShell (optional, needs the repo)

Only if you also have a copy of the repository on this machine (separate from the end-user ZIP
test above — don't confuse the two):

```powershell
cd <repo-root>
.\packaging\windows\Publish-Windows.ps1
.\packaging\windows\Build-PortableRelease.ps1
```

Verify: scripts run under real Windows PowerShell, correct output path, clean staging, release ZIP
produced with the correct versioned name.

**Result:** PASS / NOT EXECUTED — PASS

---

## Part X — README usability

Follow `README.txt` from the extracted folder as a nontechnical user would, for: extraction, app
launch, enable automatic refresh, disable automatic refresh, moving the folder, deleting the app,
local-data preservation, log location, no-.NET-requirement, no-admin-requirement. Note anything
inaccurate or confusing.

**Result:** PASS / PARTIAL / FAIL — PASS

---

## Final report

When done, report back (in chat, or by sending this filled-in file):

1. Windows version/environment
2. Confirmation the ZIP hash matched
3. Result of every Part A–X above (PASS / PASS AFTER FIX / PARTIAL / FAIL / NOT EXECUTED)
4. Any defect found, with exact repro steps

If a real release blocker turns up, don't work around it manually — report it and it'll get fixed,
rebuilt, and the failed step re-run before Phase 7E is marked complete.
