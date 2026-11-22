# RC14 local Windows test candidate — Phase 35B (Passdown workspace and preview)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`, tag → `d784c60`) and every frozen file under `artifacts/releases` are unchanged
(all 55 earlier release and local-test files re-hashed byte-identical after this build).

| | RC14-Phase35B-LOCALTEST |
|---|---|
| Source commit | `1677781` (Phase 35B on top of Phase 35A `beba3f8`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35b` |
| Package | `artifacts/local-test/RC14-Phase35B-LOCALTEST/LOCAL-ONLY-RC14-Phase35B-LOCALTEST-win-x64.zip` |
| ZIP size | 75,999,023 bytes (72.5 MB); 308 files |
| ZIP SHA-256 | `c0e925c9fbde305d12962c51a47d87dffa5f9db9385810ee9cb3b5a88d25a84f` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35B-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,655 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker file names the clean source commit |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). The test identity sorts above RC.13, so the updater never
offers a release over it. Database schema 18 (no migration). Personal Sync format unchanged. No new local files.

## What changed (what to look for)

1. **Passdown toolbar**: "Associate Passdown", one grouped date control (‹ date › Today), a quiet "Draft" status, the
   blue **Create PDF** button and an **Actions ▾** menu. Check Online, Print, Save PDF, the three Copy for Teams
   options, PDF Style, Show/Hide Preview and Season Price History are all in Actions. **Ctrl+P** prints.
2. **Section tabs** (Overview · Products & Pricing · Shows · Notes) replace the left rail inside the Passdown page.
3. **Overview** is one white sheet: Park Hours & Gates · Attendance & Early Ride Time · Daily Feature · Include in
   Passdown, separated by thin lines. The official events note moved into Park Hours & Gates.
4. The **divider** between the editor and the preview is now visible with a small grip.
5. Products & Pricing, Shows and Notes content is unchanged (Products is redesigned in Phase 35C).

Mac results: `docs/phase35b-passdown-workspace-redesign.md`. They are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with synthetic data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`. Do not run
`Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder, and do not install any update
from this build.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows
build and display scaling.

### A. Overview appearance

1. Passdown opens on Overview: a white sheet on a light grey workspace, four headed groups with thin lines between
   them, no boxes inside boxes. Compare with `carowinds_passdown_overview_phase35b_reference.html` (ignore its sample
   prices, times and ride names).
2. "Official", "Entered here" and "Your setting" tags appear beside the groups; official park hours are plain text,
   not an input box.

### B. Real Passdown editing and date navigation

3. Type in Projected Attendance and Pre-solds: digits stay as typed; on leaving the box they show thousands separators.
4. The status changes from "Unsaved changes" → "Saving…" → "Saved on this computer at …" (never "synced").
5. ‹ and › change the date; the date control opens the calendar; Today returns to today. Each date shows its own data,
   and changing the date never shows "Checking online" by itself unless that date's data is missing or stale.
6. Close and reopen the app: the edits for that date are still there.

### C. Manual hours, buyout and gates

7. Tick **Manual hours override**: Open / Close boxes appear; enter times (Tab or Enter commits; invalid text shows a
   message); untick: the boxes disappear and the official hours return.
8. Tick **Private buyout / custom hours**: Open / Close / Details appear and the headline/badge update.
9. Type a North Gate or Avenue override: an "Override" tag and Reset appear; Reset restores the default.

### D. Attendance and Early Ride Time

10. **Edit Attractions** opens the Monthly Default and Date Override editor below the group; saving updates the
    rides line; Close hides the editor.

### E. Daily Feature and math

11. Change the feature type; type text; **∑ Math** inserts symbols/fractions at the cursor; text with `\( … \)`
    shows the rendered preview under the box and on the PDF.

### F. Include in Passdown

12. The checkboxes match your Settings → Passdown Preferences defaults for a new date; ticking/unticking each one adds
    or removes that section in the preview. SCarowinds / Haunt appears only in its season.

### G. Live PDF preview responsiveness

13. A checkbox change updates the preview almost immediately; typing updates it shortly after you pause; no repeated
    "Updating…" flicker during ordinary edits.
14. Switching tabs (Overview ↔ Products & Pricing ↔ Shows ↔ Notes) is instant, keeps each tab's scroll position and
    unsaved text, and does not reload the preview.

### H. Sidebar, tabs and divider

15. Collapse/expand the sidebar on Passdown: nothing resets; the tabs and toolbar re-fit.
16. Drag the divider; also Tab to it and use Left/Right arrows. The width is remembered after restarting.
17. The divider tints when hovered and when focused with the keyboard.

### I. Show/Hide Preview

18. Actions → Hide Preview (or the preview's Hide Preview): the editor takes the full width and a **Show Preview**
    button appears on the toolbar; it brings the preview back.
19. In a narrow window (≈1,000 px) only one pane shows; Show Preview brings the preview to the front and its Hide
    Preview returns to the editor.

### J. Save PDF, Print and Copy

20. Make an edit and immediately use Actions → **Save PDF…**: the saved file contains that edit.
21. Make an edit and immediately press **Ctrl+P** (and also try Actions → Print…): print to **Microsoft Print to
    PDF**; the printed file contains that edit. Cancel in the dialog is reported, not an error.
22. Change the date with › and print straight away: the printout is for the new date, never the previous one.
23. Actions → each **Copy for Teams** option pastes correctly into Teams (formatted as before).
24. **Create PDF** still asks where to save and produces the same PDF as before.

### K. PDF templates

25. Actions → PDF Style: Classic, Compact and Modern each update the preview, the "… PDF style" note beside the tabs
    and Create PDF / Save PDF / Print output.

### L. Appearance, motion and scaling

26. Light, Dark and "Use Windows setting": toolbar, tabs, sheet, divider, Actions menu and preview all restyle.
27. Interface animations **On**: the tab underline eases on hover; **Off** (or Windows Animation effects off): it
    changes instantly. Nothing waits in either mode.
28. At 100 %, 125 % and 150 % scaling and window sizes ≈1920×1080, 1536×960, 1280×720 and the 960×640 minimum: no
    hidden Create PDF / Actions, no overlapping toolbar buttons, no clipped labels, no sideways scrolling.

### M. Keyboard and Narrator

29. Tab order: date controls → Create PDF → Actions → the four tabs → the first Overview field; focus is clearly visible.
30. Narrator reads each tab name and "Current section" for the active one, and each Actions item by name.

### N. Persistence and regression

31. PDF style, preview visibility/width, sidebar state and Interface animations survive a restart.
32. Products & Pricing (prices, overrides, custom items), Shows selection, Notes (daily and persistent), Check
    Online, Calendar, History (open a stored PDF) and Settings behave as in RC.13.

Report each item as **Pass / Fail / Not executed**. Phase 35B is not Windows-validated until this is done.
