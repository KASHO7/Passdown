# RC14 local Windows test candidate — Phase 35E (Operations Calendar redesign)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`, tag → `d784c60`) and every frozen file under `artifacts/releases` are unchanged
(all 10,154 earlier release and local-test files re-hashed byte-identical after this build).

**Dashboard is not in this build's changes.** Its approved reference was not supplied, so the Dashboard looks exactly as
in 35D. Section A below therefore only checks that the Dashboard did not change or break.

| | RC14-Phase35E-LOCALTEST |
|---|---|
| Source commit | `5ea4d0f` (Phase 35E Calendar on top of 35D follow-up `62d2aca`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35e` |
| Package | `artifacts/local-test/RC14-Phase35E-LOCALTEST/LOCAL-ONLY-RC14-Phase35E-LOCALTEST-win-x64.zip` |
| ZIP size | 76,122,198 bytes (72.6 MB); 308 files |
| ZIP SHA-256 | `b2e7f6fc074cb958865d7ea1230a7d8a6b58caabc92b10d9ad85319d15186aca` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35E-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,803 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker file names the clean source commit; no database, draft, history, key or update-manifest file in the ZIP |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). The test identity sorts above RC.13, so the updater never offers
a release over it. Database schema 18 (no migration). Personal Sync format unchanged. No new local files (the Calendar's
Show Perks box uses the existing `calendar-display.json`).

## What changed (what to look for)

1. **Calendar page**: white toolbar "Operations Calendar"; "November 2026" with ‹ Previous · Today · Next ›; one white
   month card whose days touch (thin lines, no gaps); today's number in a small blue tile; the selected day tinted with
   a blue frame.
2. **Day cells**: hours or CLOSED, then "Draft in progress" or "Finalized" only when that date has one (the old
   "No Draft" on every day is gone), then notes.
3. **Pass Perks**: the check / gift / clock markers now have their own colours (blue qualifying visit, teal
   redemption / use, amber booking deadline), the legend below the month has a **Show Perks** box (the same switch as
   Settings → Calendar), and a note that markers are public offers, not rewards earned by a guest.
4. **Selected-date panel**: "SELECTED DATE", the date with small ‹ › day buttons, labelled rows (Park information,
   Pass Perks, Passdown and notes) and four buttons fixed at the bottom: **Open Passdown**, **View Pricing**,
   **Operational Notes**, **View Finalized History**.
5. The month heading stays put; on a short window only the month scrolls, and the panel scrolls inside itself.

Mac results: `docs/phase35e-dashboard-calendar-redesign.md`. They are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with synthetic data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`. Do not run
`Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder, and do not install any update
from this build. Settings → About shows `0.1.0-rc.14-LOCALTEST.phase35e`.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows
build and display scaling. Reference images: `carowinds_calendar_preview.png`, `carowinds_calendar_deadline_preview.png`,
`carowinds_calendar_monthly_perks_preview.png`, `carowinds_calendar_compact_preview.png` (ignore their sample hours,
prices and perks).

### A. Dashboard (unchanged in this build)

1. **Dashboard visual fidelity**: looks as in the 35D build (no redesign yet) — nothing missing or overlapping.
2. **Dashboard section content**: hours, pricing, shows, notes and Passdown status show as before.
3. **Source freshness and accuracy**: Dashboard "checked …" lines and stale/unavailable wording unchanged.
4. **Passdown navigation**: Dashboard → Open Passdown opens today's Passdown.

### B. Calendar

5. **Calendar appearance**: compare with `carowinds_calendar_preview.png`: toolbar, month heading, white month card with
   touching cells, weekday band, legend, panel on the right.
6. **Month navigation**: ‹ Previous / Next › change month (including December ↔ January); **Today** returns to today's
   month and date. Each click is instant; the page does not jump.
7. **Date selection**: click several days, including a grey day of the next/previous month (the Calendar moves to that
   month). The panel heading changes at once; details follow ("Loading details…" may flash). Never the previous date's
   details under the new date.
8. **Draft/finalized indicators**: a date with a saved draft says "Draft in progress"; a finalized date says
   "Finalized"; other dates say nothing. The panel's Passdown row agrees (No Draft / Draft In Progress / Finalized).
9. **Pass Perks qualifying markers**: blue check on the last qualifying-visit date; tooltip names it.
10. **Pass Perks redemption markers**: teal gift on redemption start/end dates; a small count when two perks share it.
11. **Pass Perks booking-deadline markers**: amber clock on booking deadlines (`carowinds_calendar_deadline_preview.png`).
12. **Month-long perk summary**: "Valid all <month> (n)" in the panel opens the list; no daily marker for those perks
    (`carowinds_calendar_monthly_perks_preview.png`).
13. **Selected-date panel**: park status/hours/verification, Carolina Harbor (when published), daily ticket, Shows
    (View schedule), perks with coloured left lines and View details, Passdown and notes. The four buttons stay at the
    bottom; long content scrolls inside the panel. View Finalized History is greyed out unless the date is finalized.
14. **Empty and dense months**: a month with no published hours (e.g. far future) shows "NOT PUBLISHED" or nothing
    without errors; a six-row month (e.g. August 2026, January 2027) fits with no overlapping text.
15. **Light/Dark**: Settings → Appearance Light, Dark, Use Windows setting: cells, today tile, selected frame, marker
    colours and legend readable in each.
16. **Sidebar expansion/collapse**: both states; the panel and month never overlap or clip.
17. **Window resizing**: ≈1920×1080, 1536×960, 1280×720 and the 960×640 minimum, maximized and restored: no sideways
    scrolling, no cut-off ‹ Previous / Today / Next ›, panel always fully on screen with its buttons.
18. **DPI scaling**: 100 %, 125 %, 150 % (and large text if available): hours wrap to two lines at most, markers and
    counts readable, buttons not clipped ("Operational Notes" fits).
19. **Keyboard and focus**: Tab reaches ‹ Previous, Today, Next ›, each day, Show Perks, ‹ › day buttons, View schedule,
    View details, the four buttons; Enter/Space selects a day; focus ring visible. Narrator reads the day's hours,
    passdown status, notes and perk markers.
20. **Calendar-to-Passdown date correctness**: select a non-today date → **Open Passdown**: the Passdown opens on that
    date. **View Pricing** opens Products for that date. Today's date is not substituted.
21. **PDF preview and Save PDF after navigation**: after 20, the preview shows that date; Save PDF / Print produce that
    date. **View Finalized History** on a finalized date opens its stored record (no new PDF is generated).
22. **No unexpected refreshes or delays**: changing month, date, Show Perks and View schedule never starts Check Online
    or an "Updating" indicator; nothing waits on an animation (also with Interface animations Off and Windows
    Animation effects off).
23. **No suspicious memory growth**: click Next › / ‹ Previous about 100 times and select ~30 dates; Task Manager memory
    for Carowinds Operations settles and does not keep climbing.

### C. Show Perks setting

24. Untick **Show Perks** on the Calendar: markers, legend and panel perks disappear, the box stays. Settings → Calendar
    → "Show Pass Perks on Calendar" is unticked too. Tick either one: markers return. Restart the app: the choice is kept.
