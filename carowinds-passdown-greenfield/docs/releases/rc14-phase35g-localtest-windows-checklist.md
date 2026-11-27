# RC14 local Windows test candidate — Phase 35G final UI/UX acceptance

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`) and every earlier file under `artifacts/` are unchanged (re-hashed after this build — see
the package table).

This is the **final combined Phase 35 candidate**: the redesigned shell, Passdown workspace, Products & Pricing,
Shows, Passdown Notes, Calendar, Dashboard, Operational Notes, History and Settings (35A–35F and the 35E Dashboard
follow-up) plus the 35G polish fixes. It is the build to accept before RC.14 is prepared. Record:
`docs/phase35g-final-uiux-polish-readiness.md`.

| | RC14-Phase35G-LOCALTEST |
|---|---|
| Source commit | `c4fe167` |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35g` |
| Package | `artifacts/local-test/RC14-Phase35G-LOCALTEST/LOCAL-ONLY-RC14-Phase35G-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,140,069 bytes (72.6 MB); 308 files |
| ZIP SHA-256 | `e599cbdf133c16af12feb31ed339b7602e6e704f9079cee34d8eb3064b4ed079` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35G-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,888 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker names `c4fe167`; no database, draft, history, key or update-manifest file in the ZIP; all 6,827 earlier artifact files re-hashed byte-identical |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). Database schema 18 (no migration). Personal Sync format
unchanged. No new local files.

## What changed since the 35E Dashboard candidate (`RC14-Phase35E-DASHBOARD-LOCALTEST`)

1. **Operational Notes editor**: the empty *Effective through* box now reads "Until resolved" (when that box is
   ticked) or "Choose a date" instead of the raw `<MMM d, yyyy>` pattern; *Effective from* likewise.
2. **Settings → Data Refresh**: the "Status and Check Online" line now points to Official Data on the Dashboard and
   the Passdown's **Actions** menu (it still named a Passdown "official source details" area removed in 35B).
3. **Tester guide** (`TESTER-GUIDE.txt` in the ZIP): the "Look around", Check Online, PDF, keyboard and Settings
   steps describe the redesigned screens (sidebar, Actions menu, price editor, Passdown Defaults, Interface
   animations).
4. **Clean-up (no visible change)**: the unused Fury 325 banner photo and the unused Dashboard multi-column shows
   panel were removed.

Everything else is identical to the earlier Phase 35 candidates.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest` (item 17).
- Do not run `Enable Automatic Refresh.cmd` and do not install any update from this build.
- Settings → About shows `0.1.0-rc.14-LOCALTEST.phase35g`.

## Manual checklist

Mark each item **PASS / FAIL / NOT EXECUTED** with a note or screenshot (Windows+Shift+S) for anything odd. Record the
PC model, Windows 11 build, display scaling and monitor resolution once at the top of the report.

**Status of every item below: NOT EXECUTED** — no Windows run of this candidate has been reported. Earlier
confirmations cover 35A–35D only (see the record §2); they are not a substitute for this run.

### 1. Startup and native Windows title bar
1.1 Starts in a few seconds without an error; Dashboard first.
1.2 Title bar is **navy** with white text "Carowinds Operations" and the **original app icon** (same icon on the
    taskbar and in Alt+Tab).
1.3 Minimize, maximize/restore, close, drag, double-click to maximize and Snap Layouts (hover the maximize button) work.
1.4 Settings → Appearance → Dark: title bar stays navy/readable. Windows **high contrast** (Settings → Accessibility →
    Contrast themes): the title bar uses the high-contrast system colours, not navy.

### 2. Sidebar and navigation
2.1 Dashboard, Calendar, Passdown, Operational Notes, History; Settings at the bottom. The current screen has a bar at
    its left edge and bold text.
2.2 Collapse/expand button: icons only ↔ icons + labels; the page you are on is not reset; the choice survives a
    restart.
2.3 Below about 1,100 px window width the sidebar shows icons only (toggle unavailable); widening restores your choice.
2.4 While a note is being edited, navigating away is blocked as before.

### 3. Dashboard
3.1 Gate card (hours, Avenue Entrance, North Gate, Early Ride Time) in one row when wide, 2 × 2 when narrower.
3.2 Passdown card, Shows (+N more / View Shows), Daily Ticket Pricing (only today's exact dated prices), Operational
    Notes, Pass Perks, Official Data (View Source Details). Blank values say "Not entered" / "Not available" — no
    invented numbers, no coloured dots standing in for words.
3.3 View in Calendar, Open Passdown, Review Notes, View Shows, View Pricing, View Notes, Details, Open History each go to
    the right screen and date. None of them starts an online check; only Check Online / Run Deep Refresh do.

### 4. Calendar
4.1 Month grid: open hours / CLOSED / NOT PUBLISHED; Draft in progress / Finalized lines; today marked.
4.2 Pass Perks markers (qualifying visit, redemption/use, booking deadline) are distinguishable by **shape** as well as
    colour; legend under the grid; **Show Perks** hides/restores them and the choice survives a restart.
4.3 Click several dates quickly: the right panel always shows the last date clicked. ‹ › in the panel step one day.
4.4 Open Passdown opens that date. Months up to a year ahead show official data or "Not Published", nothing invented.

### 5. Passdown Overview
5.1 Toolbar: Associate Passdown, ‹ date ›, compact date ("Nov 26, 2026"), **Today only when not on today's Eastern
    date**, Draft/Saved state, **Create PDF**, **Actions ▾**. Nothing overflows at 960 × 640.
5.2 Date picker opens; choosing a date changes the page instantly without going online.
5.3 Overview sheet: hours, gates (overrides, private buyout), attendance (typing 38500 → 38,500 on leaving the box, cursor
    never jumps), pre-solds, Daily Feature (∑ Math), Early Ride Time, Include in Passdown.

### 6. Products & Pricing
6.1 Grouped rows; Official and Display price columns; Inc checkboxes; Details; custom items; Current Deals; Pass Perks;
    Season pass price history.
6.2 Price editor: opens next to the price; **one at a time**; Enter saves a valid number; **Esc cancels**; "Use official
    price" removes the override; an invalid entry is announced and not saved; focus returns to the edit button.
6.3 Near the bottom of the window the editor repositions to stay visible. Changing the date or scrolling closes it
    without saving.

### 7. Shows
7.1 Live count, Select all / Clear, checkboxes; a long schedule expands with **View times** and collapses with Less.
7.2 Unticking Shows under Include in Passdown keeps the picks but stops them printing.

### 8. Daily and persistent Notes (Passdown)
8.1 Daily notes: one printed note per non-empty line.
8.2 Ticking a persistent Operational Note includes it in this passdown only — it is never edited or resolved.
    "Manage notes" opens Operational Notes.

### 9. Operational Notes
9.1 Search, lifecycle segments (Active, All, Upcoming, Expired, Resolved), Category, Priority, Clear filters.
9.2 New Note: the dialog opens with focus in Title; **the empty Effective through box reads "Until resolved"** (and
    "Choose a date" after unticking Until resolved). Esc / × / Cancel close without saving and focus returns to New Note.
9.3 Save Note, Edit, Resolve (asks to confirm; Cancel leaves the note active). No accidental resolve.

### 10. History
10.1 List, search (dates, origin, PDF status), All/Scheduled/Recovery, Year filter. Filters never change records.
10.2 **Open Final PDF** opens the stored PDF; **Save PDF Copy** saves an identical copy (compare file size/hash with
     the stored one if convenient). Neither regenerates the PDF. The record is read-only.
10.3 Records from before this build are unchanged.

### 11. Settings
11.1 Section list on the left (Appearance, Passdown Defaults, Calendar, Data Refresh, Personal Sync, Diagnostics,
     Application Updates, About); clicking scrolls to the group.
11.2 Switches (Interface animations, Show Pass Perks on Calendar, Enable Personal Sync) and the theme radios look and
     behave consistently.
11.3 Data Refresh "Status and Check Online" now names the Dashboard's Official Data and the Passdown's Actions menu.
11.4 A long Personal Sync folder path wraps without a sideways scroll bar.

### 12. Theme and animation preferences
12.1 Use Windows setting / Light / Dark switch at once; the choice survives a restart with no flash of the other theme.
12.2 Interface animations **On** (default for a fresh profile): subtle sidebar width and hover transitions only.
12.3 **Off**: no transitions; everything still works.
12.4 Windows Settings → Accessibility → Visual effects → **Animation effects Off**, then return to the app: motion
     stops even with On selected, the switch still shows your saved choice, and Settings explains the override.

### 13. Keyboard and accessibility
13.1 Tab through every screen: visible focus, sensible order; Enter/Space activate; Esc closes popovers, menus and
     dialogs; focus returns to the control that opened them.
13.2 Narrator (Ctrl+Windows+Enter): sidebar items, Calendar day cells (date, hours, Draft/Finalized, perks, selected),
     Dashboard buttons, the price editor, the Notes dialog. Note anything read as just "button".
13.3 High contrast theme: all text readable, focus visible.

### 14. Responsive layouts and Windows DPI
14.1 At 100 %, 125 % and 150 % scaling: ≈1920 × 1080, 1536 × 960, 1280 × 720 and the 960 × 640 minimum, maximized and
     restored, on Dashboard, Calendar, Passdown (each tab, preview shown and hidden), Notes, History and Settings.
14.2 No clipped primary buttons, overlapping text, sideways scrolling or unreachable controls; popovers and dialogs
     fully visible.

### 15. Live PDF preview
15.1 A checkbox or show change appears in the preview almost at once; continuous typing refreshes shortly after a
     pause; switching dates shows the new date's preview (never the previous date's).
15.2 Divider drag, page arrows, zoom −/+, Fit width, Fit page; layout and zoom remembered after a restart.

### 16. Save PDF and printing
16.1 Create PDF and Save PDF in Classic, Compact and Modern (Actions → PDF Style): content matches the preview; Shows at
     the end; nothing cut off.
16.2 Print (Actions → Print, Ctrl+P, preview toolbar) opens the Windows print dialog; "Microsoft Print to PDF" output
     matches the preview; Cancel prints nothing.
16.3 Change the date, then print immediately: the printout is for the **new** date.
16.4 The three Copy for Teams options paste plain text without Shows or Daily Feature.

### 17. Personal Sync (disposable test folder only)
17.1 Enable Personal Sync on a **disposable** folder; status shows the coordinator's real state; drafts and notes sync
     between two `CarowindsTest` profiles/PCs if available. Never the real shared folder.

### 18. Draft recovery
18.1 Type into a draft, close the app from the taskbar, reopen: the draft is intact. A save failure (if tested) is
     reported in words and nothing is lost.

### 19. Official-source behaviour
19.1 Check Online (Dashboard; Passdown Actions) refreshes; without internet it fails politely and keeps the last good
     data marked as stale. Selecting dates or navigating never goes online.

### 20. Memory and long-session performance
20.1 Use the app for 20–30 minutes across all screens (many date changes, previews, popovers, show expansions). Task
     Manager: memory may rise then settle; it should not keep climbing steadily. Note the value at start and end.
20.2 Typing, navigation and previews stay responsive; nothing freezes.

## Not covered by this checklist

- RC.13 → RC.14 in-app update (needs a signed RC.14 manifest; see the record's release procedure).
- Installer/updater validation of the final RC.14 package.
