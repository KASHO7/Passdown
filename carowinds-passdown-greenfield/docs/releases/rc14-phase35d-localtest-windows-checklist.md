# RC14 local Windows test candidate — Phase 35D (Shows, Passdown Notes, navy title bar, dropdown arrows)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`, tag → `d784c60`) and every frozen file under `artifacts/releases` are unchanged
(all 890 earlier release and local-test files re-hashed byte-identical after this build).

| | RC14-Phase35D-LOCALTEST |
|---|---|
| Source commit | `d7d6848` (Phase 35D on top of Phase 35C `bfbdd18`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35d` |
| Package | `artifacts/local-test/RC14-Phase35D-LOCALTEST/LOCAL-ONLY-RC14-Phase35D-LOCALTEST-win-x64.zip` |
| ZIP size | 76,103,090 bytes (72.6 MB); 308 files |
| ZIP SHA-256 | `6b67cc89a87df5cad0324c68caa8f5117f3f6d2af7c14e2daf5f6ebd8a6b890b` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35D-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,737 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker file names the clean source commit; no database, draft, history, key or update-manifest file in the ZIP |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). The test identity sorts above RC.13, so the updater never
offers a release over it. Database schema 18 (no migration). Personal Sync format unchanged. No new local files.

## What changed (what to look for)

1. **Title bar** (every screen): dark navy with the original Carowinds icon and white "Carowinds Operations", Windows'
   own minimize / maximize / close buttons on the right. (Windows 11 only; Windows 10 keeps the system title bar.)
2. **Dropdown arrows**: the small ˅ on **Actions** and on the date button now sit level with their text.
3. **Shows & Entertainment**: one white sheet — title, date and when the official schedule was checked, "N of M shows
   selected", Select all / Clear, then columns for show and location · scheduled performances · details. Long
   schedules show three times and "+N more"; **View times** lists every time.
4. **Passdown Notes**: "Daily notes" (the same text box, bigger) and "Persistent operational notes" as two separate
   parts; persistent notes are ticked to include them ("Included in this Passdown" / "Available for <date>");
   **Manage notes** opens Operational Notes.

Mac results: `docs/phase35d-shows-notes-shared-polish.md`. They are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with synthetic data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`. Do not run
`Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder, and do not install any update
from this build. Settings → About shows `0.1.0-rc.14-LOCALTEST.phase35d`.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows
build (Settings → System → About) and display scaling.

### A. Navy title bar (priority 1–4)

1. The title bar is dark navy across the full width, like `carowinds_settings_compact_preview.png`; the text
   "Carowinds Operations" is white and nothing else is written there.
2. The icon at the left is the **original** Carowinds icon (same as the taskbar icon), not "CW" or a different image.
3. Minimize, Maximize, Restore and Close work. The caption buttons are light on the navy and visible on hover (Close
   turns red on hover — that is Windows' own style).
4. Drag the window by the title bar; double-click the title bar to maximize and again to restore; drag the window
   edges and corners to resize; hover the Maximize button for **Snap layouts** and snap to a half; Win+← / Win+→ /
   Win+↑. When maximized nothing is cut off at the top or behind the taskbar.
5. Switch Settings → Appearance between **Light**, **Dark** and **Use Windows setting**: the title bar stays navy with
   white text in all three (check after each switch).
6. Click another window so Carowinds is inactive: the bar stays navy and readable.
7. (Optional) Turn on a Windows **contrast theme** (Settings → Accessibility → Contrast themes): the title bar uses the
   contrast theme's colours, not navy. Turn it off again: navy returns after switching away from and back to the app
   (or on the next launch).
8. At 100 %, 125 % and 150 % display scaling: icon and title are sharp, not overlapping, the bar's height looks normal.

### B. Dropdown arrows (priority 5–6)

9. Passdown toolbar → **Actions**: the ˅ is vertically centred on the word "Actions" with a small even gap. Same for the
   ˅ on the date button. Check while hovering, while pressed, after Tab focus, with the menu open, in Light and Dark,
   and at 100 / 125 / 150 %.
10. Products & Pricing: each category heading's ˅ / › arrow is centred on its heading text.
11. **Actions** still lists, in order: Print… (Ctrl+P), Save PDF…, Copy for Teams: Operations Summary / Today's Pricing /
    Operations + Pricing, Show/Hide Preview, PDF Style: Classic / Compact / Modern, Check Online, Season Price History…
    — each works as before. Ctrl+P still prints.

### C. Shows (priority 7)

12. Passdown → **Shows**: compare with `carowinds_shows_preview.png` (ignore its sample shows). Title, date and
    "Official schedule checked …" are correct for the date; each show has a checkbox, name, location (or "Location not
    listed"), and its times.
13. Tick and untick shows: "N of M shows selected" changes at once; the preview's Shows section updates; the draft
    saves (Saved). **Select all** / **Clear** work.
14. A show with many times shows three and "+N more"; **View times** lists **every** time in a grid (count them against
    the official schedule); **Less** closes it. Expanding/closing never ticks or unticks the show and does not refresh
    the preview. Tab to View times and press Enter/Space: it toggles.
15. If Shows is off under Overview ▸ Include in Passdown, the page says so (selection still possible).
16. Change the date with ‹ ›: the other date's shows appear (or its "not published" / "couldn't check" note), never the
    previous date's. Return: your ticks for the first date are still there.
17. A long show name wraps without overlapping the times column.

### D. Passdown Notes (priority 8–9)

18. Passdown → **Notes**: compare with `carowinds_notes_preview.png` / `carowinds_notes_included_preview.png`.
    "Daily notes" text box is roomy; "N daily notes · N persistent included" at the top is right.
19. Type several lines, including blank lines: the count under the box ("N notes") counts only non-empty lines; the
    preview shows one note per non-empty line; Saved appears; spelling underline still works.
20. Switch to Shows and back: the text is exactly as typed. Close and reopen the app: still there.
21. Type 40+ lines: the box grows to a limit and then scrolls inside itself; the page stays usable.
22. With a persistent note available for the date (create one in Operational Notes if needed): it appears under
    "Available for <date>" unticked. Tick it: it moves to "Included in this Passdown (1)", the preview shows it, Saved
    appears. Untick it: it goes back to Available and leaves the preview.
23. Open **Operational Notes** (or click **Manage notes**): the note itself is unchanged — still Active, same text, not
    resolved or deleted — after ticking and unticking. Manage notes keeps the Passdown's date when you come back.
24. Notes are never ticked automatically on a new date; a note ticked for one date is not ticked on another.

### E. Preview, PDF and printing (priority 10–11)

25. The preview keeps up with ticking shows, ticking notes and typing (no new lag). **Create PDF**, **Save PDF** and
    **Print** contain exactly the shows and notes shown, for the selected date.

### F. Appearance, motion, scaling, keyboard (priority 12–13)

26. Light, Dark and "Use Windows setting": Shows and Notes are readable; checkboxes, links, tiles and counts visible.
27. Interface animations On / Off and Windows Animation effects off: nothing new animates; View times opens instantly.
28. At ≈1920×1080, 1536×960, 1280×720 and the 960×640 minimum, sidebar expanded and collapsed, preview shown and
    hidden: no clipped checkboxes, times or links, no sideways scrolling, time tiles never overlap.
29. Keyboard only: Tab reaches each show checkbox and View times, the daily notes box, Manage notes and each persistent
    note checkbox in order; Space toggles checkboxes. Narrator reads "Include <show> on the passdown, <times>",
    "Show all N performance times for <show>", "Include <note> in this passdown".

### G. Memory and regression (priority 14)

30. Optionally watch Task Manager while switching Overview ↔ Shows ↔ Notes ~50 times and opening/closing View times
    ~50 times: memory should level off.
31. Overview, Products & Pricing (price popover), Calendar, History, Settings and Operational Notes behave as in the
    Phase 35C build.

### H. Follow-up — compact date selector (FOLLOW-UP build only; NOT YET TESTED)

The product owner confirmed items 1–31 on the first 35D build. The follow-up is a separate package; the first one is
unchanged and kept:

| | RC14-Phase35D1-LOCALTEST (follow-up) |
|---|---|
| Source commit | `01d3553` |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35d1` |
| Package | `artifacts/local-test/RC14-Phase35D1-LOCALTEST/LOCAL-ONLY-RC14-Phase35D1-LOCALTEST-win-x64.zip` |
| ZIP size | 76,101,701 bytes (72.6 MB); 308 files |
| ZIP SHA-256 | `d0bc89d33c37a6b19a6c68f785a7d575797b9df9ce0abd7fa0cbf167191fdeb7` |
| Mac verification | Deterministic suite 5,761 passed / 0 skipped; clean Release build 0 warnings / 0 errors; content validation (308 files); `RealReleaseZip` passed |

Extract it to a new folder in `CarowindsTest` the same way. Items 1–31 need only a quick regression look with it.

32. Passdown toolbar on today: ‹ [calendar icon **Nov 26, 2026** ˅] › — no "Today" badge and **no Today button**; Create
    PDF and Actions sit closer. Hovering the date shows the full date with "(Today)".
33. Press ‹ (or ›, or pick another day in the calendar): **Today** appears after ›; click it (and separately: Tab to it
    and press Enter) — today's date returns, Today disappears, and the keyboard focus is on the date button.
34. On another date with an unsaved edit (type in Daily notes), press Today: the edit is saved to the date it was typed
    on, not to today. Then **Print** / **Save PDF** right away: the document is today's.
35. Leave the app open across **midnight Eastern** (or set it running at 11:5x PM on a test PC, or let it sleep past
    midnight and wake it): within about five minutes — or as soon as you click back into the window — **Today** appears,
    the date shown does **not** change by itself, and nothing is reloaded or lost.
36. If the PC's time zone is not Eastern (e.g. set it to Pacific or Tokyo in the test account): "today" still follows
    Carowinds (Eastern) time — the Today button hides only on the Eastern date.
37. Dec 31 / Jan 1: stepping across the year shows "Dec 31, 2026" / "Jan 1, 2027" correctly.
38. At 1280×720, the 960×640 minimum, 125 % and 150 % scaling, Light and Dark: the date control is compact, nothing is
    clipped or overlaps Create PDF / Actions, the ˅ stays centred, Today (when shown) is readable.
39. Narrator on the date button: "Operating date" and the full date; on Today: "Today".

Report each item as **Pass / Fail / Not executed**. Phase 35D is not Windows-validated until this is done.
