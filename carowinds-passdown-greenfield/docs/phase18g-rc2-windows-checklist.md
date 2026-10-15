# Phase 18G — RC.2 Windows validation checklist

Run this on the Windows PC that previously showed the Dashboard problem. For each step, write
**PASS / FAIL / PARTIAL / NOT EXECUTED / NOT APPLICABLE** and a short note in the Result column.
Send the filled-in table back (paste it or attach this file) and the release manifest will be
completed from it. Step numbers match the Phase 18G task (9–35).

RC.2 supersedes RC.1. It adds the Phase 18F work: the **Copy ▾** menu (Teams text) and the empty
Daily Feature omission in PDFs. Don't test RC.1 any more.

**Rules for the whole session**

- Sign in as your normal (standard) Windows user. Don't use "Run as administrator" anywhere.
- Never delete `%LOCALAPPDATA%\Carowinds Passdown`. It holds your real data.
- Don't delete any old program folders during this test. Only record where they are.
- If any PC uses Personal Sync, run only RC.2 on it during the test. Older builds may drop newer
  draft fields when they save a draft.
- In Teams, **paste only — don't send** unless you mean to. A paste into the compose box is enough.
- If something fails, stop, write down what you saw (a screenshot helps), and report it. Don't try
  workarounds. A code fix found here becomes `0.1.0-rc.3`; RC.2 is never patched in place.

## Artifacts under test

| | File | Bytes | SHA-256 |
|---|---|---|---|
| Primary (recommended) | `Carowinds-Operations-0.1.0-rc.2-win-x64.zip` | 73,461,483 | `3b8fdb219755991ab3a401d58218d8c626faf35d696da3974a6cd0df73ce2d22` |
| Experimental | `Carowinds-Operations-0.1.0-rc.2-win-x64.exe` | 186,155,852 | `156da1bad68bcbca983718682d606b2c1f74c6dcfa8276d77095685590a30578` |

Built from commit `dbaa753`, the version commit on top of Phase 18F `8020edb`. About this build
should show **`0.1.0-rc.2 (dbaa753)`**.

## 10. Hash check (before extracting)

After copying both files to the PC, open PowerShell (normal, not admin) in that folder and run:

```powershell
Get-FileHash .\Carowinds-Operations-0.1.0-rc.2-win-x64.zip, .\Carowinds-Operations-0.1.0-rc.2-win-x64.exe -Algorithm SHA256
```

Both hashes must match the table above. If either differs, stop: you are not testing RC.2.

| # | Step | Windows-calculated hash | Result |
|---|---|---|---|
| 10.1 | ZIP hash matches | | |
| 10.2 | EXE hash matches | | |

## 9. Machine record

Use Settings → System → About, and Settings → Time & language → Language & region.

| Item | Value |
|---|---|
| Windows edition (Home / Pro / …) | |
| Version and OS build (e.g. 24H2, 26100.xxxx) | |
| System type (x64, or ARM64 running x64 emulation) | |
| Signed in as standard user or admin? | |
| Folder you extract RC.2 into | |
| Regional format (e.g. English (United States)) | |
| Display scale currently set (Settings → System → Display → Scale) | |
| OneDrive signed in and available? | |
| Teams desktop app installed and signed in? | |
| Did this PC show the Dashboard problem before? | |
| SmartScreen / Defender prompt on first launch? (what it said) | |

## 11. Windows Search before RC.2 (do this before extracting)

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

Was the earlier Dashboard confusion caused by a stale search result? ☐ yes ☐ no ☐ can't tell — why: ____

## 12. Direct RC.2 launch

Right-click the ZIP and choose **Extract All…**. Extract to a new, empty folder whose path contains
a space, e.g. `C:\Users\<you>\Desktop\RC Test\`. Open the `Carowinds-Operations-0.1.0-rc.2-win-x64`
folder and double-click `Carowinds.Passdown.Desktop.exe`.

| # | Check | Result / notes |
|---|---|---|
| 12.1 | Extracted folder holds exactly one folder, `Carowinds-Operations-0.1.0-rc.2-win-x64` | |
| 12.2 | App opens, no missing-DLL or .NET error, window isn't blank | |
| 12.3 | Dashboard loads | |
| 12.4 | Each navigation item opens its screen | |
| 12.5 | Close the app and confirm it's gone from Task Manager | |
| 12.6 | Second launch works | |
| 12.7 | Settings → About this build → **Version** = `0.1.0-rc.2 (dbaa753)` | |
| 12.8 | **Running from** = the RC.2 folder you just extracted | |
| 12.9 | Right-click the exe → Properties → Details: Product version `0.1.0-rc.2+dbaa753…`, File version `0.1.0.2` | |

## 13. Affected-PC Dashboard (use your existing data)

| # | Check | Result / notes |
|---|---|---|
| 13.1 | Dashboard loads with no generic error | |
| 13.2 | Rides-only Early Ride Time displays correctly | |
| 13.3 | **Refresh** works | |
| 13.4 | Navigate away, come back, Dashboard still fine | |
| 13.5 | Close and reopen, Dashboard still fine | |
| 13.6 | If step 11 found a stale copy, launch it. Does it show the old Dashboard problem while RC.2 doesn't? Write both folder paths and versions | |

## 14. Conjuring (only on an SCarowinds operating date the official site lists)

| # | Check | Result / notes |
|---|---|---|
| 14.1 | Test date used | |
| 14.2 | Fresh retrieval (Dashboard → **Refresh**, or **Run Deep Refresh**) | |
| 14.3 | "The Conjuring: Beyond Fear" appears under **SCarowinds / Haunt** with the official nightly price | |
| 14.4 | It appears only once | |
| 14.5 | SCarowinds selected: PDF includes it, and **Copy → Today's Pricing** includes it | |
| 14.6 | SCarowinds deselected: PDF excludes it, and copied pricing excludes it | |

If no such date exists on the official site right now, write **NOT EXECUTED** (automated tests stay
as the evidence).

## 15–17. Gate overrides (Associate screen)

Use a test date, not one you'll actually use for a real passdown. Paste copies into Notepad or the
Teams compose box to read them.

| # | Check | Result / notes |
|---|---|---|
| 15.1 | Default North Gate time (write it down) | |
| 15.2 | Set North Gate only. North Gate changes; Avenue and Park Hours stay the same; an "Override" indicator appears | |
| 15.3 | Wait a few seconds for autosave. Switch to another date and back. Override still there | |
| 15.4 | Close and reopen the app. Override still there | |
| 15.5 | **Copy → Operations Summary**: the `North Gate:` line shows the override time | |
| 15.6 | **Reset**. North Gate returns to the default from 15.1 | |
| 16.1 | Default Avenue time | |
| 16.2 | Set Avenue only. Avenue changes; North Gate and Park Hours stay the same | |
| 16.3 | Switch dates and back, then close and reopen. Override still there | |
| 16.4 | **Copy → Operations Summary**: the `Avenue:` line shows the override time | |
| 16.5 | **Reset**. Avenue returns to its default | |
| 17.1 | Set both. Each shows its own value and both survive close/reopen | |
| 17.2 | **Copy → Operations Summary** shows both override times | |
| 17.3 | **Copy → Operations + Pricing** shows both override times | |
| 17.4 | Preview PDF (any template) shows both override times, and the word "Override" is not in the PDF | |
| 17.5 | Reset one. The other stays (no coupling) | |
| 17.6 | History: once a test date with overrides has been finalized (automatic after the 6:00 AM cutoff the next day), History shows the overridden gate times. Write NOT EXECUTED if you can't wait or don't want a test passdown in History | |

## 18. Empty Daily Feature in PDFs

Pick a test date. Choose a feature type if the screen offers one, and leave the Daily Feature text
empty (or only spaces). Generate a Preview PDF in each template (template picker in the Associate
screen).

| # | Check | Classic | Compact | Modern |
|---|---|---|---|---|
| 18.1 | PDF opens in the Windows PDF viewer | | | |
| 18.2 | No Daily Feature heading | | | |
| 18.3 | No divider line or placeholder text where it would have been | | | |
| 18.4 | No blank gap: the next section moves up | | | |
| 18.5 | Now type Daily Feature text and regenerate: heading and text are back | | | |

## 19. Copy ▾ menu basics

| # | Check | Result / notes |
|---|---|---|
| 19.1 | **Copy ▾** sits next to **Create PDF** | |
| 19.2 | Tab reaches it (visible focus outline) | |
| 19.3 | Enter opens the menu | |
| 19.4 | Space opens the menu | |
| 19.5 | Up/Down arrows move between the three items | |
| 19.6 | Items read: Operations Summary, Today's Pricing, Operations + Pricing | |
| 19.7 | After a copy, a short confirmation line appears under the toolbar | |
| 19.8 | (Optional) Narrator reads a name for the button and each item | |

## 20–22. Teams paste — Today's Pricing

Select some pricing sections and products, then **Copy → Today's Pricing** and paste into the
Teams desktop compose box. **Don't send.** If Teams desktop isn't available, write NOT EXECUTED.

| # | Check | Result / notes |
|---|---|---|
| 20.1 | Line breaks survive | |
| 20.2 | Blank line between sections; readable | |
| 20.3 | Prices correct | |
| 20.4 | No table/Markdown symbols (`|`, `**`, `#`) and no HTML | |
| 20.5 | No hidden or unselected products or sections | |
| 20.6 | No unexpected bold/fonts/colors | |
| 21.1 | Select only Daily Admission and Fast Lane (deselect Promotions, Season Passes, SCarowinds, Add-Ons). PDF and copied pricing list exactly the same products | |
| 21.2 | Deselect one individual product. It disappears from both the PDF and the copy | |
| 22.1 | Prices show as `$59.00` / `From $37.00`. Is that readable in Teams? Your opinion: | |
| 23.1 | A product with no price: the PDF shows "-"; the copy leaves it out. A section with nothing priced is left out of the copy entirely. Clean? (This is intended) | |

## 24. Nothing priced selected

Deselect every pricing section (or every priced product).

| # | Check | Result / notes |
|---|---|---|
| 24.1 | **Copy → Today's Pricing**: message says "No pricing selected"; pasting gives no empty "Ticket Prices:" block | |
| 24.2 | **Copy → Operations + Pricing**: operations part pastes, no pricing part; message says "Operations summary copied (no pricing selected)" | |

## 25–26. Teams paste — Operations

| # | Check | Result / notes |
|---|---|---|
| 25.1 | **Copy → Operations Summary**, paste in Teams: lines `Park Hours`, `Avenue`, `North Gate`, `Projected Attendance`, `Pre-solds`, `ERT`, one per line | |
| 25.2 | Times are 12-hour with AM/PM; counts have commas (e.g. `18,000`) | |
| 25.3 | ERT lists rides only | |
| 25.4 | (If practical) a closed date shows `Park Hours: Closed` and no gate lines | |
| 26.1 | **Copy → Operations + Pricing**, paste in Teams: operations first, then exactly one blank line, then pricing | |
| 26.2 | No duplicated headings; wrapping acceptable; no unselected pricing | |

## 27. Clipboard failure (optional)

Only if you know a safe way to make the clipboard briefly unavailable (for example a clipboard
manager that locks it). Don't do anything that could destabilize Windows. Otherwise write
NOT EXECUTED.

| # | Check | Result / notes |
|---|---|---|
| 27.1 | App doesn't crash; message "Couldn't copy to the clipboard. Try again." | |
| 27.2 | Draft unchanged | |
| 27.3 | App log has a technical error line (but no copied text) | |

## 28. Display scaling

Settings → System → Display → **Scale**. For each level, open the Associate screen at the window
size you normally use (and once maximized).

| # | Check | 100% | 125% | 150% |
|---|---|---|---|---|
| 28.1 | Copy ▾ button visible | | | |
| 28.2 | Copy menu not cut off; items readable | | | |
| 28.3 | Create PDF usable | | | |
| 28.4 | North Gate / Avenue override controls usable | | | |
| 28.5 | No overlapping or cut-off text | | | |
| 28.6 | No sideways scrolling unless the window is unreasonably small | | | |
| | **Overall** | | | |

Restore your usual scale afterwards.

## 29. US formatting under a non-US region (optional, restore afterwards)

Change Settings → Time & language → Language & region → **Regional format** to English (United
Kingdom) or German. Leave the time zone alone. Restart the app.

| # | Check | Result / notes |
|---|---|---|
| 29.1 | Copied pricing uses `$` | |
| 29.2 | Copied operations use AM/PM | |
| 29.3 | Month/day names English everywhere | |
| 29.4 | One PDF: US date, AM/PM, `$` | |
| 29.5 | Dashboard, Calendar, History US formatted | |
| 29.6 | Regional format restored afterwards | |

## 30. App log

Paste `%LOCALAPPDATA%\Carowinds Passdown\logs` into the File Explorer address bar.

| # | Check | Result / notes |
|---|---|---|
| 30.1 | Today's `app-YYYYMMDD.log` exists | |
| 30.2 | Startup line shows `0.1.0-rc.2+dbaa753…` | |
| 30.3 | Running folder is shown as `%USERPROFILE%\…`, not `C:\Users\<your name>\…` | |
| 30.4 | No draft, note, or copied pricing text in the log | |
| 30.5 | No OneDrive path with your name in it | |

## 31. Diagnostics export

Associate screen → **Export Diagnostics**. Save the ZIP, then open it in File Explorer.

| # | Check | Result / notes |
|---|---|---|
| 31.1 | `summary.txt` shows `0.1.0-rc.2+dbaa753…` and the sanitized RC.2 folder | |
| 31.2 | Recent app log excerpt included | |
| 31.3 | Scheduled-refresh log excerpt included (after step 32, if run) | |
| 31.4 | No `.db` file, no PDFs, no draft/note text, no clipboard text, no sync file contents | |
| 31.5 | No `C:\Users\<your name>` anywhere | |

## 32. Task Scheduler

In the RC.2 folder, double-click **Enable Automatic Refresh.cmd**.

| # | Check | Result / notes |
|---|---|---|
| 32.1 | Ran without an admin prompt (UAC) | |
| 32.2 | In Task Scheduler, **Carowinds Passdown Daily Refresh** points at the RC.2 exe (path with a space) | |
| 32.3 | Right-click the task → **Run**. Last Run Result = `(0x0)` | |
| 32.4 | `logs\scheduled-refresh-YYYYMMDD.log` records `0.1.0-rc.2+dbaa753…` and the RC.2 path | |
| 32.5 | The app still opens normally afterwards | |
| 32.6 | **Disable Automatic Refresh.cmd** removes only that task; other tasks are untouched | |
| 32.7 | Re-enable it if you want automatic refresh to keep running from RC.2 | |

## 33. One-PC Personal Sync (only if OneDrive is available)

Settings → Personal Sync → **Choose Folder…** (inside OneDrive) → **Enable Personal Sync** →
**Sync Now**. RC.2 only on this dataset.

| # | Check | Result / notes |
|---|---|---|
| 33.1 | `carowinds-passdown.db`, `-wal` and `-shm` and the logs are still only in `%LOCALAPPDATA%\Carowinds Passdown` | |
| 33.2 | Sync folder holds only sync records and temp structure: no `.db`, no logs, no PDFs | |
| 33.3 | Associate draft syncs, including gate overrides | |
| 33.4 | Operational Notes sync | |
| 33.5 | ERT syncs | |
| 33.6 | Finalized History syncs | |

## 34. Two-PC Personal Sync (only if a second Windows PC is available)

Install RC.2 on **both** PCs (verify hashes on both) before editing anything.

| # | Check | Result / notes |
|---|---|---|
| 34.1 | Clean editor: same date open on A and B. Edit and Sync Now on A, then Sync Now on B. B reloads automatically | |
| 34.2 | Dirty editor: unsaved edit on B. Change and sync on A, then sync B. B keeps its edit and shows a conflict; nothing is silently overwritten | |
| 34.3 | **Keep This Device** resolves as expected | |
| 34.4 | Repeat 34.2, then **Use Other Device** resolves as expected | |
| 34.5 | Gate override conflict: dirty draft on B, Avenue override changed on A, sync. Conflict shows both sides' gate values; result as expected | |
| 34.6 | Operational Notes two-device conflict | |
| 34.7 | ERT two-device conflict | |

## 35. Experimental single-file EXE

Copy `Carowinds-Operations-0.1.0-rc.2-win-x64.exe` into its own empty folder and double-click it.

| # | Check | Result / notes |
|---|---|---|
| 35.1 | Opens and UI loads | |
| 35.2 | About this build = `0.1.0-rc.2 (dbaa753)`, Running from = that folder | |
| 35.3 | Uses the same data (`%LOCALAPPDATA%\Carowinds Passdown`) | |
| 35.4 | Nothing new appears in its folder besides the EXE after closing | |
| 35.5 | Closes cleanly | |

The single-file EXE stays experimental. It ships without the third-party notices, so don't hand it
out on its own.
