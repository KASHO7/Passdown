# RC14 local Windows test candidate — Phase 35E Dashboard follow-up

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`) and every earlier file under `artifacts/` — including the Calendar-only
`RC14-Phase35E-LOCALTEST` and the `RC14-Phase35F-LOCALTEST` ZIPs — are unchanged (all 10,625 earlier artifact files
re-hashed byte-identical after this build).

This build contains everything in the 35F build plus the redesigned **Dashboard**. The Calendar (35E), Passdown,
Products, Shows, Notes, History and Settings screens are unchanged from the 35F build.

| | RC14-Phase35E-DASHBOARD-LOCALTEST |
|---|---|
| Source commit | `2ec6261` (Phase 35E-3 Dashboard on top of 35F-2 `256a1c9`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35e-dashboard` |
| Package | `artifacts/local-test/RC14-Phase35E-DASHBOARD-LOCALTEST/LOCAL-ONLY-RC14-Phase35E-DASHBOARD-LOCALTEST-win-x64.zip` |
| ZIP size | 76,286,420 bytes (72.8 MB); 308 files |
| ZIP SHA-256 | `0702c01fa6e116abd6fb0530b9f2a5f16c9ec21aea82ee3adcbdddf6a8dbd2ee` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35E-DASHBOARD-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,885 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker file names the source commit; no database, draft, history, key or update-manifest file in the ZIP |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). Database schema 18 (no migration). Personal Sync format
unchanged. No new local files.

## What changed (what to look for)

1. **Header**: the date over **Admissions Overview**; on the right the official-data line and **Check Online**.
2. **Gate card** (blue top line): PARK OPERATING HOURS, AVENUE ENTRANCE, NORTH GATE, EARLY RIDE TIME in one row on
   wide windows, 2 × 2 on narrower ones; a footer with the hours' verification line, the operating date and
   **View in Calendar**.
3. **Left column**: *Today's Associate Passdown* (status, "Last saved locally …", attendance / pre-solds / daily
   feature, Passdown notes count, Latest finalized + Open History, **Open Passdown** and **Review Notes**) and
   *Shows & Entertainment* (counts, upcoming times with "+N more", venue on the right, **View Shows**).
4. **Right column**: *Daily Ticket Pricing* (today's official dated prices, **View Pricing**), *Operational Notes*
   (**View Notes**), *<Month> Pass Perks* (labelled qualifying / booking / redemption dates, **Details**).
5. **Official Data** card at the bottom with **View Source Details** (source list and **Run Deep Refresh**).

Reference: `carowinds_dashboard_phase35_reference.html` and its PNG. Ignore their sample values (Nov 22 hours, $50,
6,000, 300, the show list, "Carowinds B SyncTest", 7:44 AM). Mac results: `docs/phase35e-dashboard-calendar-redesign.md`
§14–§24 — they are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with synthetic data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`. Do not run
`Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder, and do not install any update
from this build. Settings → About shows `0.1.0-rc.14-LOCALTEST.phase35e-dashboard`.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows
build and display scaling.

1. **Visual match**: compare with the approved HTML/PNG — header, gate card, two columns, card title bands and coloured
   top lines, spacing and type sizes. Note anything that looks off or crowded.
2. **Light/Dark**: Settings → Appearance Light, Dark and Use Windows setting: every card, top line, link and warning
   text readable.
3. **Sidebar expanded/collapsed**: both states; cards never overlap or clip; the gate card switches between one row and
   2 × 2 cleanly.
4. **Resizing and scaling**: ≈1920×1080, 1536×960, 1280×720 and the 960×640 minimum, maximized and restored; 100 %,
   125 %, 150 % scaling (and large text if available): no sideways scrolling, no cut-off buttons or links, no text on top
   of other text.
5. **Park hours and gates**: on a day with published hours the times match Passdown → Overview (incl. a manual North
   Gate / Avenue time: it says "Manual time for today"); on an unpublished or closed day nothing is invented.
6. **Freshness**: header line, gate footer, Shows, Pricing and Pass Perks "checked / verified" times are plausible and
   in Eastern time; nothing claims "verified" for data that was never checked.
7. **Draft vs finalized**: no draft → "No Draft"; after typing in the Passdown → "Draft In Progress" with "Last saved
   locally …" (plain grey, not a warning); a finalized date (if available) → "Finalized", no Open Passdown.
8. **Missing / unavailable states**: after a failed Check Online (e.g. network off in the test account) the last
   values stay with "could not be rechecked / may be out of date" wording; pricing with no dated prices shows the
   honest "Price not available …" or "From …" line.
9. **Dashboard navigation**: every link/button does what it says and nothing else.
10. **Correct operating date**: late evening (after 8 PM Eastern) and, if possible, with the PC set to another time
    zone, the header and every action still use the Carowinds (Eastern) date.
11. **View in Calendar**: opens the Calendar with today's operating date selected; the Passdown date is unchanged.
12. **Open Passdown / Review Notes**: open today's Passdown on Overview / Notes, even if another date was selected
    before.
13. **View Pricing / View Shows**: open today's Passdown on Products / Shows.
14. **View Notes / Open History**: Operational Notes opens with its previous filter; History opens read-only; viewing a
    note on the Dashboard never changes it or its Passdown inclusion.
15. **Pass Perks**: qualifying, booking and redemption dates appear as separate labelled lines that match the official
    Perks & Play page; "Today, <date>" only on the real date; the note says these are public perks, not a pass's
    rewards; **Details** opens Products with the Pass Perks panel.
16. **Keyboard and screen reader**: Tab moves Check Online → View in Calendar → Open History → Open Passdown → Review
    Notes → show "+N more" → View Shows → View Pricing → View Notes → Details → View Source Details with a visible focus
    ring; Enter/Space activate; Narrator reads each card name, prices ("may be out of date" where shown), notes
    ("Important") and perk dates. High contrast mode: everything visible.
17. **No unexpected online refresh**: opening the Dashboard, switching sections, focusing the window, resizing and
    expanding Source details or show times never start Check Online or an "Updating" indicator.
18. **Performance**: opening the Dashboard and moving to Passdown / Calendar feels as quick as the 35F build.
19. **Memory**: switch Dashboard ↔ another section about 100 times; Task Manager memory settles and does not keep
    climbing.
20. **PDF preview after entering Passdown**: Dashboard → Open Passdown: the preview shows today's date; Save PDF / Print
    produce today's date.

Record results in a reply or a copy of this file; do not mark anything passed that was not run.
