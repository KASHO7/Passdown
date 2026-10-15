# Phase 18E — RC.1 Windows validation checklist

Run this on the Windows PC that previously showed the Dashboard problem. For each step, write
**PASS / FAIL / PARTIAL / NOT EXECUTED / NOT APPLICABLE** and a short note in the Result column.
Send the filled-in table back (paste it or attach this file) and the release manifest will be
completed from it.

**Rules for the whole session**

- Sign in as your normal (standard) Windows user. Don't use "Run as administrator" anywhere.
- Never delete `%LOCALAPPDATA%\Carowinds Passdown`. It holds your real data.
- Don't delete any old program folders during this test. Only record where they are.
- If any PC uses Personal Sync, run only RC.1 on it during the test. Older builds may drop the new
  gate-override fields when they save a draft.
- If something fails, stop, write down what you saw (a screenshot helps), and report it. Don't try
  workarounds.

## Artifacts under test

| | File | Bytes | SHA-256 |
|---|---|---|---|
| Primary (recommended) | `Carowinds-Operations-0.1.0-rc.1-win-x64.zip` | 73,446,617 | `28fad2675f1b71ecbcdcaa71503352c55108cc432a9dd23120474c57887bec42` |
| Experimental | `Carowinds-Operations-0.1.0-rc.1-win-x64.exe` | 186,106,700 | `493087311ab65d416278f049c069bb2648f2e5871024c458768684c59b989ab2` |

Built from commit `3cace78`, the version commit on top of Phase 18D `096bc4a`. About this build
should show **`0.1.0-rc.1 (3cace78)`**.

**Step 0 — confirm you have the right files.** After copying both files to the PC, open PowerShell
(normal, not admin) in that folder and run:

```powershell
Get-FileHash .\Carowinds-Operations-0.1.0-rc.1-win-x64.zip, .\Carowinds-Operations-0.1.0-rc.1-win-x64.exe -Algorithm SHA256
```

Both hashes must match the table above. If either differs, stop: you are not testing RC.1.

| # | Step | Result / notes |
|---|---|---|
| 0 | Both hashes match | |

## 9. Machine record

Use Settings → System → About, and Settings → Time & language → Language & region.

| Item | Value |
|---|---|
| Windows edition (Home / Pro / …) | |
| Version and OS build (e.g. 24H2, 26100.xxxx) | |
| System type (x64, or ARM64 running x64 emulation) | |
| Signed in as standard user or admin? | |
| Folder you extract RC.1 into | |
| Regional format (e.g. English (United States)) | |
| OneDrive signed in and available? | |
| Did this PC show the Dashboard problem before? | |
| SmartScreen / Defender prompt on first launch? (what it said) | |

## 10. Windows Search before RC.1 (do this before extracting)

1. Press Start and type `Carowinds Passdown`.
2. For each app or file result, right-click and choose **Open file location**. For a shortcut,
   right-click it, choose **Properties**, and copy the **Target**.
3. If it's safe, launch that copy and read Settings → About this build. Older copies may not have
   this panel at all; if so, write "no About panel".
4. Close it, then repeat with `Carowinds Operations`.

| Search term | Result shown | Target path | Version/build shown | Notes |
|---|---|---|---|---|
| Carowinds Passdown | | | | |
| Carowinds Operations | | | | |

Conclusion: Windows Search found ☐ nothing useful ☐ an old extracted copy ☐ an old shortcut ☐ other: ____

## 11. Direct RC.1 launch

Right-click the ZIP and choose **Extract All…**. Extract to a new, empty folder whose path contains
a space, e.g. `C:\Users\<you>\Desktop\RC Test\`. Open the `Carowinds-Operations-0.1.0-rc.1-win-x64`
folder and double-click `Carowinds.Passdown.Desktop.exe`.

| # | Check | Result / notes |
|---|---|---|
| 11.1 | Extracted folder holds exactly one folder, `Carowinds-Operations-0.1.0-rc.1-win-x64` | |
| 11.2 | App opens, no missing-DLL or .NET error, window isn't blank | |
| 11.3 | Dashboard loads | |
| 11.4 | Each navigation item opens its screen | |
| 11.5 | Close the app and confirm it's gone from Task Manager | |
| 11.6 | Second launch works | |
| 11.7 | Settings → About this build → **Version** = `0.1.0-rc.1 (3cace78)` | |
| 11.8 | **Running from** = the RC.1 folder you just extracted | |
| 11.9 | Right-click the exe → Properties → Details: Product version `0.1.0-rc.1+3cace78…`, File version `0.1.0.1` | |

## 12. Affected-PC Dashboard (use your existing data)

| # | Check | Result / notes |
|---|---|---|
| 12.1 | Dashboard loads with no generic error | |
| 12.2 | Rides-only Early Ride Time displays correctly | |
| 12.3 | **Refresh** works | |
| 12.4 | Navigate away, come back, Dashboard still fine | |
| 12.5 | Close and reopen, Dashboard still fine | |
| 12.6 | Associate screen shows the expected ERT state | |
| 12.7 | If step 10 found a stale copy, launch it. Does it show the old Dashboard problem while RC.1 doesn't? | |

## 13. Conjuring (only on an SCarowinds operating date the official site lists)

| # | Check | Result / notes |
|---|---|---|
| 13.1 | Test date used | |
| 13.2 | Fresh retrieval (Dashboard → **Refresh**, or Dashboard → **Run Deep Refresh**) | |
| 13.3 | "The Conjuring: Beyond Fear" appears under **SCarowinds / Haunt** with the official nightly price | |
| 13.4 | It appears only once | |
| 13.5 | PDF includes it only when the SCarowinds / Haunt section is selected | |

If no such date exists on the official site right now, write **NOT EXECUTED** (automated tests stay
as the evidence).

## 14–16. Gate overrides (Associate screen)

Use a test date, not one you'll actually use for a real passdown.

| # | Check | Result / notes |
|---|---|---|
| 14.1 | Default North Gate time (write it down) | |
| 14.2 | Set North Gate only. North Gate changes; Avenue and Park Hours stay the same; an "Override" indicator appears | |
| 14.3 | Switch to another date and back. Override still there | |
| 14.4 | Close and reopen the app. Override still there | |
| 14.5 | **Reset**. North Gate returns to the default from 14.1 | |
| 15.1 | Default Avenue time | |
| 15.2 | Set Avenue only. Avenue changes; North Gate and Park Hours stay the same | |
| 15.3 | Switch dates and back, then close and reopen. Override still there | |
| 15.4 | **Reset**. Avenue returns to its default | |
| 16.1 | Set both. Each shows its own value | |
| 16.2 | Reset one. The other stays (no coupling) | |
| 16.3 | History: once a test date with overrides has been finalized (this happens automatically after the 6:00 AM cutoff the next day), History shows the overridden gate times. Write NOT EXECUTED if you can't wait or don't want a test passdown in History | |

## 17. PDFs (with at least one gate override active)

Generate a Preview PDF in each template (template picker in the Associate screen).

| # | Check | Classic | Compact | Modern |
|---|---|---|---|---|
| 17.1 | PDF opens in the Windows PDF viewer | | | |
| 17.2 | Overridden gate time printed | | | |
| 17.3 | No word "Override" in the PDF | | | |
| 17.4 | Park Hours unchanged | | | |
| 17.5 | US date, 12-hour AM/PM, `$` prices | | | |
| 17.6 | No comparison prices, no ERT routine start time, no app/"Associate" title | | | |

## 18. US formatting under a non-US region (optional, restore afterwards)

Change Settings → Time & language → Language & region → **Regional format** to English (United
Kingdom) or German. Leave the time zone alone. Restart the app.

| # | Screen | English names, US dates, AM/PM, `$` | Notes |
|---|---|---|---|
| 18.1 | Dashboard | | |
| 18.2 | Calendar | | |
| 18.3 | Associate | | |
| 18.4 | History | | |
| 18.5 | Sync conflict screen (only if you see one) | | |
| 18.6 | One PDF | | |
| 18.7 | Regional format restored afterwards | | |

## 19. App log

Paste `%LOCALAPPDATA%\Carowinds Passdown\logs` into the File Explorer address bar.

| # | Check | Result / notes |
|---|---|---|
| 19.1 | Today's `app-YYYYMMDD.log` exists | |
| 19.2 | Startup line shows `0.1.0-rc.1+3cace78…` | |
| 19.3 | Running folder is shown as `%USERPROFILE%\…`, not `C:\Users\<your name>\…` | |
| 19.4 | No draft or note text in the log | |
| 19.5 | No OneDrive path with your name in it | |

## 20. Diagnostics export

Associate screen → **Export Diagnostics**. Save the ZIP, then open it in File Explorer.

| # | Check | Result / notes |
|---|---|---|
| 20.1 | `summary.txt` shows `0.1.0-rc.1+3cace78…` and the sanitized RC.1 folder | |
| 20.2 | Recent app log excerpt included | |
| 20.3 | Scheduled-refresh log excerpt included (after step 21, if run) | |
| 20.4 | No `.db` file, no PDFs, no draft/note text, no sync file contents | |
| 20.5 | No `C:\Users\<your name>` anywhere | |

## 21. Task Scheduler

In the RC.1 folder, double-click **Enable Automatic Refresh.cmd**.

| # | Check | Result / notes |
|---|---|---|
| 21.1 | Ran without an admin prompt (UAC) | |
| 21.2 | In Task Scheduler, under Task Scheduler Library, **Carowinds Passdown Daily Refresh** points at the RC.1 exe (path with a space) | |
| 21.3 | Right-click the task → **Run**. Last Run Result = `(0x0)` | |
| 21.4 | `logs\scheduled-refresh-YYYYMMDD.log` records `0.1.0-rc.1+3cace78…` and the RC.1 path | |
| 21.5 | The app still opens normally afterwards | |
| 21.6 | **Disable Automatic Refresh.cmd** removes only that task; other tasks are untouched | |
| 21.7 | Re-enable it if you want automatic refresh to keep running from RC.1 | |

## 22. Windows Search after RC.1

After using RC.1 for a while (indexing can take minutes), repeat step 10.

| # | Check | Result / notes |
|---|---|---|
| 22.1 | Does RC.1 appear? | |
| 22.2 | Do stale copies still rank above it? | |
| 22.3 | Does the `…-0.1.0-rc.1-…` folder name make the right copy easy to pick? | |

## 23. One-PC Personal Sync (only if OneDrive is available)

Settings → Personal Sync → **Choose Folder…** (inside OneDrive) → **Enable Personal Sync** →
**Sync Now**.

| # | Check | Result / notes |
|---|---|---|
| 23.1 | `carowinds-passdown.db`, `-wal` and `-shm` are still only in `%LOCALAPPDATA%\Carowinds Passdown` | |
| 23.2 | Sync folder holds only sync records and temp structure: no `.db`, no logs, no PDFs | |
| 23.3 | Associate draft syncs, and its gate overrides are in it | |
| 23.4 | Operational Notes sync | |
| 23.5 | ERT syncs | |
| 23.6 | Finalized History syncs | |

## 24–26. Two-PC Personal Sync (only if a second Windows PC is available)

Install RC.1 on **both** PCs (verify hashes on both) before editing anything.

| # | Check | Result / notes |
|---|---|---|
| 24.1 | Clean editor: same date open on A and B. Edit and Sync Now on A, then Sync Now on B. B reloads automatically | |
| 24.2 | Dirty editor: unsaved edit on B. Change and sync on A, then sync B. B keeps its edit and shows a conflict; nothing is silently overwritten | |
| 24.3 | **Keep This Device** resolves as expected | |
| 24.4 | Repeat 24.2, then **Use Other Device** resolves as expected | |
| 25.1 | North Gate override set on A arrives on B | |
| 25.2 | Dirty draft on B, Avenue override changed on A, sync. Conflict shows both sides' gate values | |
| 25.3 | Final result after resolving is as expected | |
| 26.1 | Operational Notes two-device check | |
| 26.2 | Monthly ERT two-device check | |
| 26.3 | Date ERT override two-device check | |

## 27. Experimental single-file EXE

Copy `Carowinds-Operations-0.1.0-rc.1-win-x64.exe` into its own empty folder and double-click it.

| # | Check | Result / notes |
|---|---|---|
| 27.1 | Opens and UI loads | |
| 27.2 | About this build = `0.1.0-rc.1 (3cace78)`, Running from = that folder | |
| 27.3 | Uses the same data (`%LOCALAPPDATA%\Carowinds Passdown`) | |
| 27.4 | Nothing new appears in its folder besides the EXE after closing | |
| 27.5 | Closes cleanly | |

The single-file EXE stays experimental. It ships without the third-party notices, so don't hand it
out on its own.
