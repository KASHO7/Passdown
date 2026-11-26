# RC13 local Windows test candidate — Phase 34B-1 (live PDF preview responsiveness)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.12 (published
prerelease `v0.1.0-rc.12`, tag → `7763c7e`) and its frozen files are unchanged (re-hashed after this build).

| | RC13-Phase34B1-LOCALTEST |
|---|---|
| Source commit | `87b265e` (Phase 34B-1 on top of `5cb71f5`) |
| Version shown in the app | `0.1.0-rc.13-LOCALTEST.phase34b1` |
| Package | `artifacts/local-test/RC13-Phase34B1-LOCALTEST/LOCAL-ONLY-RC13-Phase34B1-LOCALTEST-win-x64.zip` |
| ZIP size | 80,961,162 bytes (77.2 MB); 308 files (unchanged file set vs. the Phase 33 candidate) |
| ZIP SHA-256 | `b4a040cc71cfd625aa0083410b09407ff81f91527826de30e175de21abdda547` |
| EXE inside the ZIP | `LOCAL-ONLY-RC13-Phase34B1-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Full deterministic suite 5,378 passed / 0 skipped (5,347 + 31 new); clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP; `RealReleaseZip` passed; stamped version `0.1.0-rc.13-LOCALTEST.phase34b1+87b265e…` read from the DLL; no manifest/signature in the package; all files under `artifacts/releases` (incl. RC.10/RC.11/RC.12) re-hashed byte-identical |

Source `<Version>` is still `0.1.0-rc.12` (not bumped); the test identity is stamped only into this build and
sorts above RC.12 and a future RC.13, so the updater never offers a release over it. Database schema 18 (no
migration). No new local files. Personal Sync format unchanged.

## What changed (what to look for)

Only the timing of the PDF preview. A single click (checkbox, product, show, template, date) should update the
preview **almost immediately** instead of after about half a second. While typing, the preview updates on the
first key and again just after you stop (about ¼ second). The "Updating…" chip should rarely appear. PDF
content, Print, Save PDF and Create PDF should look exactly as in RC.12.

## Data isolation — use the separate Windows account

The app always uses the signed-in user's `%LOCALAPPDATA%\Carowinds Passdown`, so test only in the separate local
account **`CarowindsTest`** (setup in `rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract
the ZIP to a **new** folder in that account (right-click → Extract All), then double-click
`Carowinds.Passdown.Desktop.exe`. Do not run `Enable Automatic Refresh.cmd`, do not point Personal Sync at your
real shared folder, and do not install any update from this build. Your installed RC.12 in your normal account
is not touched.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note for anything odd. Where a time is asked for, a rough
impression ("instant", "about ½ s") is enough; a phone slow-motion video is better.

### A. Startup and first preview
- [ ] A1 The EXE launches from Explorer in CarowindsTest; Settings → About shows `0.1.0-rc.13-LOCALTEST.phase34b1 (87b265e…)`.
- [ ] A2 Open Passdown: the preview appears without a noticeable extra wait (RC.12 waited ~½ s after loading). Time to first page: ___
- [ ] A3 Test data is only under `C:\Users\CarowindsTest\AppData\Local\Carowinds Passdown`.

### B. Checkbox and selection responsiveness (preview visible)
- [ ] B1 Products → toggle one product's Include: the page changes almost immediately (RC.12 ≈ ½ s). Impression: ___
- [ ] B2 Overview / Include in Passdown switches (e.g. Fast Lane, Add-Ons): same.
- [ ] B3 Shows → select / deselect a show: same.
- [ ] B4 Click several checkboxes quickly in a row: the final preview matches the final checkbox states.
- [ ] B5 The "Updating…" chip does not flash on ordinary clicks.

### C. Typing
- [ ] C1 Notes → type a sentence at normal speed: typing never lags (letters never appear late).
- [ ] C2 The preview shows the first letter quickly, then settles on the full text about ¼ s after you stop. Time after last key: ___
- [ ] C3 The preview does not flicker on every keystroke.
- [ ] C4 Attendance and Daily Feature edits behave the same way.

### D. Long multi-page notes
- [ ] D1 Paste ~80 lines of notes so the Passdown reaches 2+ pages: page indicator shows "1 / N".
- [ ] D2 Go to page 2, then edit one word: the preview stays on page 2 and updates quickly. Impression: ___
- [ ] D3 Remove notes back to one page while on page 2: lands on page 1, no error.

### E. Date changes
- [ ] E1 Change the date (date picker, ‹ ›, Today): the old page disappears at once ("Loading this date's preview…"), then the new date's page appears with no extra wait.
- [ ] E2 The caption always shows the selected date; never the previous date's PDF, even when clicking dates quickly.
- [ ] E3 Unsaved edits on the previous date are still there when you return (draft protections unchanged).

### F. Section switching
- [ ] F1 Overview → Products → Shows → Notes: the preview stays exactly as it is (no flash, no reload, same page and zoom).

### G. Templates
- [ ] G1 More → PDF Style: Classic, Compact, Modern each update the preview almost immediately; layout identical to RC.12.

### H. Printing and Save PDF
- [ ] H1 Print → **Microsoft Print to PDF**: output matches the preview (layout, page breaks, margins).
- [ ] H2 Type an edit and press Print **immediately**: the printed PDF includes the edit.
- [ ] H3 Type an edit and press Save PDF immediately: the saved PDF includes the edit and matches the preview.
- [ ] H4 Change the date and try Print while "Loading this date's preview…" shows: Print is unavailable or says to try again; nothing from the previous date is printed.
- [ ] H5 Create PDF still works exactly as before.

### I. Preview show / hide, resize and zoom
- [ ] I1 Hide Preview, make edits, Show Preview: the latest edits appear.
- [ ] I2 Drag the divider and resize the window: smooth, no overlap; zoom (− / +, Fit width, Fit page) still works and stays sharp.

### J. Display scaling
- [ ] J1 100 %: nothing clipped, page sharp, updates quick.
- [ ] J2 125 %: same.
- [ ] J3 150 %: same.

### K. Narrator (Ctrl+Win+Enter), where available
- [ ] K1 While typing in Notes, Narrator does **not** repeatedly announce "Updating…".
- [ ] K2 Preview toolbar buttons are still announced by name; date-change loading text is still announced.

### L. Restart and preferences
- [ ] L1 Hide the preview, move the divider, choose Fit page, pick a PDF style; close and reopen: all restored.
- [ ] L2 Draft edits made before closing are still there after reopening.

### M. Afterwards
- [ ] M1 In your normal account, RC.12 still opens with your Passdowns unchanged.

## Results

| Item | Result | Notes |
|---|---|---|
| A–M (all) | NOT YET TESTED | Windows observations go here, separately from the Mac benchmark numbers in `docs/phase34b1-preview-responsiveness.md`. |
