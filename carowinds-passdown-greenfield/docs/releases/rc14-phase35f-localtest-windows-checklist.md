# RC14 local Windows test candidate — Phase 35F (Operational Notes, History, Settings)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`, tag → `d784c60`) and every frozen file under `artifacts/releases` are unchanged.

**Dashboard is still not redesigned.** Its approved reference has not been supplied, so the Dashboard looks exactly as in
35D/35E. Phase 35E stays open until it is.

| | RC14-Phase35F-LOCALTEST |
|---|---|
| Source commit | `8967fcc` (Phase 35F-1 on top of 35E record `8de9453`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35f` |
| Package | `artifacts/local-test/RC14-Phase35F-LOCALTEST/LOCAL-ONLY-RC14-Phase35F-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,197,042 bytes (72.7 MB); 308 files |
| ZIP SHA-256 | `e067899ff87995bba39889228497fccdf7b23772040ed980148ff9b43409133b` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35F-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,826 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker file names `8967fcc`; no database, draft, history, key or update-manifest file in the ZIP; all 10,720 earlier release and local-test files re-hashed byte-identical |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). The test identity sorts above RC.13, so the updater never offers
a release over it. Database schema 18 (no migration). Personal Sync format unchanged. No new local preference files.

## What changed (what to look for)

1. **Operational Notes**: white page header with **+ New Note**; a grey filter bar with **Search notes**, the status
   segments (Active · All · Upcoming · Expired · Resolved), Category and Priority; a list card ("Active notes · 7 notes",
   each row: title + status pill, a one-line preview of the note text, category · priority and dates) and a detail card
   (OPERATIONAL NOTE, title, tags, **Edit** / **Resolve** at the top right, Effective from / through, Note details, a
   "Persistent operational note" explanation, created/updated times). **New Note** and **Edit** open a centred dialog over
   a dimmed page (Title, Category, Priority, Effective from / through + Until resolved, Details, Cancel / **Save Note**).
2. **Passdown History**: page header; a filter bar with **Search** (dates, record type, PDF status), **All records ·
   Scheduled · Recovery** (and Manual when such records exist) and **Year**; a list card ("Finalized records · n records")
   and a read-only detail card: FINALIZED ASSOCIATE PASSDOWN, the date, finalized time and type, badges, **Open Final PDF**
   / **Save PDF Copy** at the top right, a read-only note, then Operating day (park hours, North gate, Avenue entrance,
   Early Ride Time, attendance), Daily feature, Products included (with the stored prices), Shows, Operational notes.
3. **Settings**: a section list on the left (Appearance, Passdown Defaults, Calendar, Data Refresh, Personal Sync,
   Diagnostics, Application Updates, About) beside one scrolling page of white cards. Theme options sit side by side;
   **Interface animations**, **Show Pass Perks on Calendar** and **Enable Personal Sync** are On/Off switches (the same
   saved settings as before). Everything else (folder, status, Sync Now, conflicts, Export Diagnostics, updates, About)
   is unchanged in behaviour.

Not offered (see `docs/phase35f-operational-notes-history-settings.md` §3): **Reopen** for a resolved note (the app has
no reopen operation); searching inside a History record's products, shows or notes (search reads record metadata only);
a Check Online button or "Official-source details" in Settings.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with synthetic data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`. Do not run
`Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder (use a new empty test folder such as
`C:\Users\Public\CarowindsSyncTest`), and do not install any update from this build. Settings → About shows
`0.1.0-rc.14-LOCALTEST.phase35f`.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows build
and display scaling. Reference images (ignore their sample values): `carowinds_operational_notes_preview.png`,
`carowinds_operational_notes_editor_preview.png`, `carowinds_operational_notes_compact_preview.png`,
`carowinds_history_preview.png`, `carowinds_history_pdf_action_preview.png`, `carowinds_history_compact_preview.png`,
`carowinds_settings_motion_preview.png`, `carowinds_settings_original_icon_preview.png`, `carowinds_settings_dark_preview.png`,
`carowinds_settings_compact_preview.png`, `carowinds_settings_sync_preview.png`.

### A. Operational Notes

1. **Appearance**: compare with `carowinds_operational_notes_preview.png` — header, filter bar, list card, detail card.
2. **New note**: + New Note → the dialog opens with the cursor in Title. Fill every field (a date range, a Details text
   with two paragraphs), **Save Note**: the dialog closes, the note is selected and its details match exactly.
3. **Validation**: Save with an empty title → "Enter a title."; Effective through before Effective from → the existing
   message; nothing is saved until fixed.
4. **Cancel never saves**: Edit a note, change the title, then press **Escape** (and separately **×** and **Cancel**):
   the note is unchanged; focus returns to Edit (or New Note).
5. **Resolve**: Resolve → confirmation → **Yes, Resolve**: the note leaves Active and appears under Resolved with its
   resolved time; no Edit/Resolve buttons on it. Cancel on the confirmation leaves it Active.
6. **Search and filters**: type part of a title, a word from a note's details, and a category name ("Staffing"): the list
   narrows as you type; the count updates; "Clear filters" resets search, Category and Priority. Status segments,
   Category and Priority still select the same notes as before. Nothing is changed by searching.
7. **Passdown independence**: select a note here, then open Passdown → Notes: it is **not** included automatically.
   Include it there, return here: it is still Active and unchanged. Resolve it here: an already finalized Passdown in
   History still shows it as printed.
8. **Persistence after navigation**: create/edit, go to Calendar and back, restart the app: the change is there.
9. **Large / long content**: 20+ notes, one with a very long title and long text: rows wrap, nothing overlaps, the list
   scrolls.
10. **Keyboard**: Tab reaches New Note, Search, each status segment, Category, Priority, the list, Edit, Resolve; arrow
    keys move in the list; in the dialog Tab cycles the fields, Escape cancels; focus ring always visible. Narrator reads
    a row's title, priority and dates.

### B. Passdown History

11. **Appearance**: compare with `carowinds_history_preview.png`.
12. **Search and filters**: search a date ("Oct 16", "10/16/2026", a weekday), "Recovery", "PDF unavailable"; use
    Scheduled / Recovery and Year: the list narrows; "No finalized records match…" when nothing does; Clear filters.
13. **Selected record**: date, finalized time, type, badges and every stored section show; prices are the stored ones.
14. **Open Final PDF / byte preservation**: Open Final PDF opens the original. **Save PDF Copy** to the Desktop, then in
    PowerShell compare with the stored copy: `Get-FileHash <saved file>` twice from two saves → identical hashes; open it
    and confirm it shows the original finalization (not today's prices, shows or notes).
15. **Missing PDF**: a record marked "No PDF" shows "Exact historical PDF unavailable" and no PDF buttons; nothing is
    generated.
16. **Read-only**: there is no way to edit, regenerate or refinalize; selecting records and filtering do not change any
    Passdown draft (Passdown page unchanged afterwards).
17. **Calendar route**: with a History filter that hides a finalized date, open that date's **View Finalized History**
    from the Calendar: History clears its filters and shows that record.

### C. Settings

18. **Appearance**: compare with `carowinds_settings_motion_preview.png` / `_original_icon_preview.png`. Click each
    section-list entry: the page scrolls that card to the top and the entry turns blue; scrolling the page by hand moves
    the highlight.
19. **Theme**: Use Windows setting / Light / Dark apply at once (`carowinds_settings_dark_preview.png`); arrow keys
    (Left/Right and Up/Down) move the choice; restart keeps it.
20. **Interface animations**: switch Off → transitions stop, restart keeps Off; switch On. With Windows Settings →
    Accessibility → Visual effects → Animation effects **Off**, return to the app: the switch still says On, motion is
    off, and the note explains Windows is overriding it.
21. **Passdown Defaults**: untick Season Passes; a **new** date's Passdown starts without it; an already started date keeps
    its own choices.
22. **Calendar switch**: turn Show Pass Perks off here → the Calendar's Show Perks box is unticked and markers hidden;
    tick it on the Calendar → the switch here is On.
23. **Data Refresh**: information only; opening Settings starts no Check Online or "Updating" indicator.
24. **Personal Sync (test folder only)**: Enable switch, Choose Folder (a new empty test folder), Folder status and
    Automatic sync badges reflect the real state (never "Connected" just because a folder is chosen), Sync Now,
    Review Conflicts if any. Turning it off stops sync. Database location is not in the sync folder.
25. **Diagnostics / Updates / About**: Export Diagnostics saves a ZIP; Check for Updates behaves as in 35E (do not
    install); About shows `0.1.0-rc.14-LOCALTEST.phase35f` and the running folder.

### D. All three screens

26. **Light / Dark** for each screen: text and tags readable, no white boxes in Dark.
27. **Sidebar expanded / collapsed** on each screen.
28. **Window sizes**: ≈1920×1080, 1536×960, 1280×720, the 960×640 minimum, maximized and restored: no sideways
    scrolling; New Note, Open Final PDF / Save PDF Copy and Settings switches stay reachable (History's PDF buttons move
    under the date in a narrow window).
29. **DPI**: 100 %, 125 %, 150 %: nothing clipped; body text not shrunk.
30. **High contrast** (Windows contrast theme): every control visible and labelled.
31. **No delays / memory**: switching filters and selecting ~50 notes / records is instant; Task Manager memory settles.
