# RC15 local Windows test candidate — Phase 36C Early Ride Time editor extraction

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged: all 11,762
were re-hashed after this build.

Phase 36C is an **internal refactor with no intended visible change**:

- The Early Ride Time (ERT) editor's logic moved out of the Passdown workspace view model into its own child view
  model.
- Three background-notification hand-offs (Passdown and Operational Notes reload after Personal Sync, and the
  Settings sync status) now go through the app's existing UI-thread helper.

The point of this checklist is to confirm that **nothing behaves differently**. Record:
[phase36c-ert-editor-maintainability.md](../phase36c-ert-editor-maintainability.md).

| | RC15-Phase36C-LOCALTEST |
|---|---|
| Source commit | `18fac73` |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase36c` |
| Package | `artifacts/local-test/RC15-Phase36C-LOCALTEST/LOCAL-ONLY-RC15-Phase36C-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,143,312 bytes (72.6 MB); 308 files |
| ZIP SHA-256 | `d0d947e48a2d3ab5e8264c321287fb4f509221d171501b42bf8637c9aa3a726f` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase36C-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,957 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation (308 files); `RealReleaseZip` passed; marker names `18fac73`; no database, draft, history, key, signature or update-manifest file in the ZIP; all 11,762 earlier artifact files re-hashed byte-identical |

The source `<Version>` is still `0.1.0-rc.14` (not bumped). Database schema 18, with no migration. Personal Sync
format and PDF output are unchanged. No new local files.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up data. Setup is in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation". Extract the ZIP to a **new** folder in that
account (right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest`.
- Do not run `Enable Automatic Refresh.cmd`, and do not install any update from this build.
- Settings → About shows `0.1.0-rc.15-LOCALTEST.phase36c`.

## Manual checklist

Mark each item **PASS / FAIL / NOT EXECUTED**, with a note or screenshot (Windows+Shift+S) for anything odd.
Record once at the top of the report:

- PC model
- Windows 11 build
- display scaling
- monitor resolution

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

### 1. Opening the editor and date correctness
1.1 Open **Passdown**. On today's date, **Early Ride Time** shows the same status line as RC.14:
- the rides;
- "None";
- or "Not configured for <Month Year>".

1.2 Select **Edit Attractions**. The compact editor opens under it with **MONTHLY DEFAULT** (the month name of
the selected date), the "No ERT this month" box, the rides box, Note, **Save Monthly**, the **DATE OVERRIDE**
section and **Close**. The layout matches RC.14.

1.3 The rides and note already saved for this month appear in the boxes, one ride per line.

1.4 Move to a date in **another month** with ‹ › or the date picker, then select **Edit Attractions** again. The
month label and the boxes show *that* month's values, not the previous month's.

### 2. Monthly Default — save and cancel
2.1 Type two rides (one per line) and a note, then **Save Monthly**. The editor stays open, and the status line
above shows the new rides at once.

2.2 Select **Close**. Reopen with **Edit Attractions**: the saved rides and note are shown.

2.3 Type a different ride and select **Close** *without* saving. Reopen: the previously saved rides are back (the
unsaved text is discarded, as in RC.14).

2.4 Tick **No ERT this month** and **Save Monthly**. The status shows "None". Untick it, enter rides, save, and
the rides return.

### 3. Date override
3.1 Tick **Use override for <date>**; the override rides and reason boxes appear. Enter a ride and a reason, then
**Save Override**. The status shows the override ride, and **Remove Override** appears.

3.2 Move to the next day. The status returns to the monthly rides; the override applies to its own date only.

3.3 Go back, open the editor, and **Remove Override**. The status returns to the monthly default, and the override
boxes clear.

### 4. Date switching while editing
4.1 Open the editor, type rides **without saving**, then change the date. The editor closes. The new date shows
its own status, and nothing was saved for either date (reopen both to check).

4.2 Rapidly click › five or six times, then select **Edit Attractions**. The editor shows the values for the date
you landed on.

4.3 Open the editor, select **Save Monthly**, and immediately change the date. Afterwards the original month has
the saved rides. The new date's screen is not replaced by the old month's values.

### 5. Draft autosave independence
5.1 Type an attendance number, a daily note or a Daily Feature, then use the ERT editor (save, close, reopen).
The Passdown edits stay exactly as typed and save as usual. The saving indicator behaves as before.

5.2 Using only the ERT editor never shows "Unsaved" or "Saving" for the Passdown itself.

### 6. PDF preview, Print and Save PDF
6.1 With the preview pane shown, save a Monthly ERT change. The preview updates and shows the new Early Ride
rides.

6.2 Save an override. The preview shows the override rides.

6.3 **Actions → Print**: the printed page shows the current Early Ride rides. **Actions → Save PDF**: the saved
file shows them too.

### 7. Conflict handling (optional — needs two devices or two Windows accounts with a disposable sync folder)
7.1 Open the ERT editor on device A. On device B, change the same month's ERT and sync. On A, save a different
value. The existing message appears: "Early Ride Time was changed on another device while this editor was open…".
Both versions are kept under Settings → Personal Sync conflicts.

7.2 With the editor open and **unchanged** on A, a sync of B's ERT change refills the editor with B's values.

### 8. Keyboard and accessibility
8.1 With the editor open, press Tab from "No ERT this month". Focus moves to the rides box, then Note, then **Save
Monthly**, then the override checkbox, then **Close**, with the visible focus ring each time.

8.2 Space toggles the checkboxes, and Enter activates the buttons. Narrator (optional) reads the same names as
RC.14, for example "Monthly Early Ride Time rides" and "Save Monthly Early Ride Time".

8.3 After **Close**, keyboard focus is not lost; the next Tab continues in the Passdown. Note where focus goes; no
change from RC.14 is expected.

### 9. Light / Dark, scaling, motion
9.1 Settings → Appearance → Light, then Dark. The editor's borders, text, hints and error text are readable in
both, and look the same as RC.14.

9.2 At 125 % and 150 % display scaling, the editor fits the Overview column without clipping.

9.3 With Interface animations off, or Windows **Animation effects** off, the editor opens and closes instantly, as
before.

### 10. Changed UI-thread hand-offs
10.1 With Personal Sync set to the disposable folder, make an Operational Notes change on a second device or
account and sync. The Operational Notes list and the Passdown's persistent-note suggestions update on their own,
as before.

10.2 Settings → Personal Sync: during and after **Sync Now** and an automatic sync, the status line ("Syncing…",
"Last synced …") updates as before, with no freeze or error.

### 11. Memory and responsiveness
11.1 Open and close the ERT editor about 30 times and switch dates about 20 times. The app stays responsive.

11.2 Optional: Task Manager memory stays roughly flat, with no steady climb of more than a few MB per minute while
idle.

11.3 No error dialog and no unexpected entry in `%LOCALAPPDATA%\Carowinds Passdown\logs\app-*.log` (search for
"Early Ride Time" or "error").

## Reporting

Report PASS / FAIL / NOT EXECUTED per item, with notes. Windows validation is recorded only after the owner reports
it. This candidate does not authorize RC.15 preparation.
