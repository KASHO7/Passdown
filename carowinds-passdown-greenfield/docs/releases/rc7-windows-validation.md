# Carowinds Operations 0.1.0-rc.7 — Windows validation record

Execution record for the frozen RC.7 artifacts. It consolidates
`docs/phase22-windows-validation-checklist.md` (Phases 22A–22F) with the release checks
(extraction, version, packaging, upgrade, sync, scheduler) from Phase 23.

**Status: NOT EXECUTED.** No Windows 11 environment was available to the build session (macOS
host). Every item below stays **NOT EXECUTED** until someone runs it on Windows and records the
outcome. Nothing here was pre-marked as passing. Build-host test results are in
`docs/release-manifest.md` "0.1.0-rc.7". They do not replace these checks.

Result values: **PASS** · **PASS AFTER FIX** · **FAIL** · **PARTIAL** · **NOT EXECUTED** ·
**NOT APPLICABLE**. A failure that needs a new binary is fixed in **0.1.0-rc.8**. The RC.7 files
below are never rebuilt or replaced in place.

## 0. Artifacts under test

Check the hashes before extracting (PowerShell:
`Get-FileHash .\<file> -Algorithm SHA256`). Built from commit `6dbdce8`; About this build should read
`0.1.0-rc.7 (6dbdce8)`.

| Artifact | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.7-win-x64.zip` (primary) | 75,149,722 | `6473d1c59cdab360a054d59dafac3834c0184b39ee93ccb5816268c3c852b811` |
| `Carowinds-Operations-0.1.0-rc.7-win-x64-singlefile.exe` (experimental) | 191,013,708 | `caa32e3f4641f6d7cdb3010882247ba94360d4f0222742bc8b89df88e51802b0` |

## 1. Environment (fill in)

| Field | Value |
|---|---|
| Execution date | |
| Tester | |
| Windows edition and build (`winver`) | |
| CPU architecture | |
| Display(s), resolution, scaling | |
| Theme during the run | |
| Windows time zone | |
| .NET SDK/runtime installed on this PC? (`dotnet --info`, "not recognized" is expected) | |
| ZIP SHA-256 as computed on this PC | |
| About this build (version and running path) | |
| Profile used (disposable test profile / backed-up real profile) | |

## 2. Safety before testing

| # | Check | Result | Notes |
|---|---|---|---|
| 2.1 | `%LOCALAPPDATA%\Carowinds Passdown` copied to a dated backup outside that folder, with the app closed and no 6 AM task running | NOT EXECUTED | |
| 2.2 | Any shared sync folder used for testing is a new test folder, or the real one is backed up and testing it is approved | NOT EXECUTED | |
| 2.3 | Initial checks run against a disposable profile (for example a fresh Windows user, or the real profile renamed aside and restored afterward) | NOT EXECUTED | |

## 3. Extraction, launch and identity

| # | Check | Result | Notes |
|---|---|---|---|
| 3.1 | ZIP and EXE SHA-256 match the manifest | NOT EXECUTED | |
| 3.2 | "Extract All" into a new, empty folder; one top-level folder `Carowinds-Operations-0.1.0-rc.7-win-x64\` | NOT EXECUTED | |
| 3.3 | `Test-ReleasePackage.ps1 -Path <zip> -RepositoryRoot <checkout>` passes on Windows (if a checkout is available) | NOT EXECUTED | |
| 3.4 | `Carowinds.Passdown.Desktop.exe` starts with no .NET installed and no admin prompt | NOT EXECUTED | |
| 3.5 | Explorer › Properties › Details: Product name "Carowinds Operations", file version 0.1.0.7, product version 0.1.0-rc.7+… | NOT EXECUTED | |
| 3.6 | Settings › About this build: `0.1.0-rc.7 (<commit>)`, running path = the extracted folder | NOT EXECUTED | |
| 3.7 | Fury 325 banner renders (compact, crest visible, "CAROWINDS OPERATIONS" readable) in Light and Dark | NOT EXECUTED | |
| 3.8 | Data stays in `%LOCALAPPDATA%\Carowinds Passdown\carowinds-passdown.db` (no new folder, nothing written next to the exe) | NOT EXECUTED | |
| 3.9 | No migration on an RC.6 profile: Diagnostics export shows schema 16 before and after | NOT EXECUTED | |
| 3.10 | Close and launch again: opens normally with the same data | NOT EXECUTED | |
| 3.11 | Windows Search / Start does not launch an older copy (About path check) | NOT EXECUTED | |
| 3.12 | Experimental single-file EXE in its own empty folder: starts, About says RC.7 | NOT EXECUTED | |

## 4. Upgrade from RC.6 (backed-up or synthetic copy of an RC.6 profile)

| # | Check | Result | Notes |
|---|---|---|---|
| 4.1 | Drafts for past, today and future dates open with their values, Shows selections and math source intact | NOT EXECUTED | |
| 4.2 | Season Price History and historical price data intact | NOT EXECUTED | |
| 4.3 | Operational Notes (active, upcoming, resolved) intact | NOT EXECUTED | |
| 4.4 | User overrides (gate times, product include/price overrides, ERT overrides) intact; official cached values unchanged | NOT EXECUTED | |
| 4.5 | Personal Sync configuration (enabled, folder, device ID) intact | NOT EXECUTED | |
| 4.6 | History records all present; Open Final PDF of a pre-RC.7 record opens the original bytes | NOT EXECUTED | |
| 4.7 | Scheduled refresh task (if registered) still points at a valid exe; re-run "Enable Automatic Refresh.cmd" from the new folder if the folder moved | NOT EXECUTED | |

## 5. Shell, theme and navigation (22A–22C)

| # | Check | Result | Notes |
|---|---|---|---|
| 5.1 | Light/Dark: neutral light surfaces, charcoal dark surfaces, blue accents; no Fluent gray panels in Dark | NOT EXECUTED | |
| 5.2 | Live theme switch while open: every screen and open popup follows without restart | NOT EXECUTED | |
| 5.3 | Navigation Dashboard · Calendar · Passdown · Notes · History; active item underlined; wraps rather than clips at large text | NOT EXECUTED | |
| 5.4 | Settings gear: trailing, Tab after History, Enter/Space opens Settings, tooltip "Settings" | NOT EXECUTED | |

## 6. Dashboard (22C)

| # | Check | Result | Notes |
|---|---|---|---|
| 6.1 | Operations Overview compact; park hours prominent | NOT EXECUTED | |
| 6.2 | Official daily admission product shown; Exact vs "From" labeling correct | NOT EXECUTED | |
| 6.3 | Stale / unavailable states shown in words, never as Closed | NOT EXECUTED | |
| 6.4 | Attendance and pre-sold counts with commas | NOT EXECUTED | |
| 6.5 | Open Passdown → today's Passdown even if another date was selected | NOT EXECUTED | |
| 6.6 | Open Notes → Passdown Notes | NOT EXECUTED | |
| 6.7 | Data Status expands/collapses; errors stay visible collapsed | NOT EXECUTED | |
| 6.8 | Today's Shows: compact, upcoming performances only | NOT EXECUTED | |
| 6.9 | Operational Notes summary counts match the Notes screen | NOT EXECUTED | |

## 7. Associate Passdown (22D–22F)

| # | Check | Result | Notes |
|---|---|---|---|
| 7.1 | Date arrows, date popup (adjacent-month days muted), Today shortcut | NOT EXECUTED | |
| 7.2 | Changing the date makes no network request (Resource Monitor or offline test) | NOT EXECUTED | |
| 7.3 | Check Online for the selected date; failure keeps last-known-good values | NOT EXECUTED | |
| 7.4 | Products: official vs manual labels; exact prices plain; "From $x" for starting prices; Military/First Responder dated rows; overrides "Prints as …" | NOT EXECUTED | |
| 7.5 | Attendance / pre-sold editing: grouping on blur (38500 → 38,500), no cursor jumps while typing, invalid text left as typed with its error | NOT EXECUTED | |
| 7.6 | Autosave: "Saved on this computer at h:mm AM ET" after a pause | NOT EXECUTED | |
| 7.7 | **Phase 23 save failure**: with the profile folder made read-only, an edit shows "Save failed — changes not saved"; hovering shows the reason; restoring write access and editing again returns to "Saved …". **Restore write access before changing dates, Check Online or adding a note** (known freeze, see §17) | NOT EXECUTED | |
| 7.8 | Shows: off by default; per-performance ticks persist across dates and restart | NOT EXECUTED | |
| 7.9 | Daily Feature math: picker inserts symbols, preview renders, Escape closes, focus returns; spell check ignores math | NOT EXECUTED | |
| 7.10 | Create PDF (each style) | NOT EXECUTED | |
| 7.11 | Copy ▾ menu (three Teams Copy options) | NOT EXECUTED | |
| 7.12 | More ▾ menu (PDF style, Season Price History) | NOT EXECUTED | |
| 7.13 | Season Price History overlay fits 960×640, scrolls, Escape/Close dismiss, focus returns | NOT EXECUTED | |
| 7.14 | Section rail Overview / Products / Notes; Narrator says "Current section" | NOT EXECUTED | |
| 7.15 | Toolbar wraps without clipping at 960 px and 150% | NOT EXECUTED | |

## 8. Calendar (22E, 22E.1, 22F)

| # | Check | Result | Notes |
|---|---|---|---|
| 8.1 | Future months through about twelve months ahead: official hours or Not Published, nothing invented | NOT EXECUTED | |
| 8.2 | December WinterFest weekends (noon–9 PM) show Verified hours; cells wrap rather than truncate at 960 px | NOT EXECUTED | |
| 8.3 | New Year's Eve shows closing after midnight (3 PM–1 AM) | NOT EXECUTED | |
| 8.4 | Closed versus Not Published distinguished | NOT EXECUTED | |
| 8.5 | Selecting another date never shows the previous date's hours, pricing, Shows, status or notes | NOT EXECUTED | |
| 8.6 | Rapid switching (Oct 16 → Oct 17 → Dec 5; repeated Prev/Next Day) ends on the last date's details only | NOT EXECUTED | |
| 8.7 | Shows collapsed "N scheduled shows · M performances"; View schedule expands; choice persists across dates | NOT EXECUTED | |
| 8.8 | Historical dates: recorded data only; backfill status readable | NOT EXECUTED | |
| 8.9 | Open Passdown / View Date Pricing open the selected date; Open History only for a finalized date | NOT EXECUTED | |

## 9. Operational Notes (22E, 22F)

| # | Check | Result | Notes |
|---|---|---|---|
| 9.1 | List rows (three lines), Important accent | NOT EXECUTED | |
| 9.2 | Create / edit / resolve | NOT EXECUTED | |
| 9.3 | Filters and empty states | NOT EXECUTED | |
| 9.4 | Timestamps "Created … ET · Updated … ET" | NOT EXECUTED | |
| 9.5 | Note changes sync to a second PC (test folder) | NOT EXECUTED | |

## 10. History (22E, 22F)

| # | Check | Result | Notes |
|---|---|---|---|
| 10.1 | Finalized records listed with weekday title and "Finalized … ET · Scheduled" | NOT EXECUTED | |
| 10.2 | Detail record dates and North Gate / Avenue gate labels | NOT EXECUTED | |
| 10.3 | Open Final PDF / Save PDF Copy open the stored bytes; a saved copy's SHA-256 equals one saved before the upgrade | NOT EXECUTED | |
| 10.4 | Existing records survive the upgrade (count before = after) | NOT EXECUTED | |

## 11. Settings (22F)

| # | Check | Result | Notes |
|---|---|---|---|
| 11.1 | One page: Appearance, Data Refresh, Personal Sync, Diagnostics, About; scrolls at 960×640 | NOT EXECUTED | |
| 11.2 | Appearance follows Windows | NOT EXECUTED | |
| 11.3 | Refresh explanation matches behavior (four-hour check, 6 AM task set up outside the app) | NOT EXECUTED | |
| 11.4 | Personal Sync enable / configuration | NOT EXECUTED | |
| 11.5 | Choose Folder… opens the native Windows folder picker; long OneDrive paths wrap | NOT EXECUTED | |
| 11.6 | Sync Now; "Last synced … ET" | NOT EXECUTED | |
| 11.7 | Folder unavailable / sync error in the error color with words; Offline not shown as an error | NOT EXECUTED | |
| 11.8 | Mixed-version caution visible | NOT EXECUTED | |
| 11.9 | Export Diagnostics: "Diagnostics saved."; ZIP has no notes, drafts, sync data, PDFs or database | NOT EXECUTED | |
| 11.10 | About this build (see 3.6) | NOT EXECUTED | |
| 11.11 | Keyboard: every Settings control reachable and operable | NOT EXECUTED | |

## 12. PDFs and Teams Copy

Generate representative passdowns (products incl. "From" prices, Military/First Responder dated
pricing, attendance 38,500 / pre-solds 21,240, operational notes, two or three Shows, a math Daily
Feature).

| # | Check | Classic | Compact | Modern |
|---|---|---|---|---|
| 12.1 | Product prices and "From" labels | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 12.2 | Military/First Responder dated pricing | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 12.3 | Attendance separators | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 12.4 | Daily Feature math drawn (no raw LaTeX) | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 12.5 | Shows after Operational Notes (last section) | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 12.6 | Footer date/time (Eastern) and page indicator | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 12.7 | Pagination, no clipping, no unexpected extra page | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |

| # | Check | Result | Notes |
|---|---|---|---|
| 12.8 | Frozen pre-RC.7 History PDFs unchanged (see 10.3) | NOT EXECUTED | |
| 12.9 | Teams Copy option 1 excludes Shows and Daily Feature | NOT EXECUTED | |
| 12.10 | Teams Copy option 2 excludes Shows and Daily Feature | NOT EXECUTED | |
| 12.11 | Teams Copy option 3 excludes Shows and Daily Feature | NOT EXECUTED | |

## 13. Eastern timestamps (22F)

| # | Check | Result | Notes |
|---|---|---|---|
| 13.1 | Windows in Eastern: Dashboard, Calendar, Notes, History, save chip, Sync and conflict times show "… ET" correctly | NOT EXECUTED | |
| 13.2 | Windows set to Pacific (or UTC), restart: same stored times show Eastern wall clock with "ET"; PDF footer Eastern | NOT EXECUTED | |
| 13.3 | Park hours, show times and plain dates carry no "ET" | NOT EXECUTED | |

## 14. Popups and overlays

| # | Check | Result | Notes |
|---|---|---|---|
| 14.1 | Date picker, Copy, More, Math picker, Notes drop-downs, tooltips: themed surface, Escape/outside-click dismiss, focus returns | NOT EXECUTED | |
| 14.2 | Season Price History and confirmation prompts fit small windows and scroll | NOT EXECUTED | |

## 15. Scaling and window sizes

Screens: Dashboard, Calendar, Passdown (Overview/Products/Notes), Notes, History, Settings, overlays.

| Scaling | 960×640 | Standard (1280×690 / 1280×860) | Wide (1920×1000) |
|---|---|---|---|
| 100% | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 125% | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |
| 150% | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED |

Pass = no clipping, no overlap, no horizontal scroll; Calendar cells, Settings rows and toolbar wrap.
Attach screenshots for any non-PASS cell.

## 16. Keyboard and Narrator

| # | Check | Result | Notes |
|---|---|---|---|
| 16.1 | Tab order predictable on every screen | NOT EXECUTED | |
| 16.2 | Focus ring always visible | NOT EXECUTED | |
| 16.3 | Enter/Space activation; Escape closes popups | NOT EXECUTED | |
| 16.4 | Settings gear reachable and named | NOT EXECUTED | |
| 16.5 | Calendar Shows "View schedule" reads expanded/collapsed | NOT EXECUTED | |
| 16.6 | Navigation reads "Current page"; Associate rail "Current section"; Calendar day cells "Selected" | NOT EXECUTED | |
| 16.7 | Icon-only buttons (prev/next day, Settings, date) and progress bars have names; "Loading details…" announced | NOT EXECUTED | |
| 16.8 | Save chip: Narrator reads "Save status" and, after a failed save, the failure reason (help text) | NOT EXECUTED | |
| 16.9 | Anything Narrator cannot identify (list) | NOT EXECUTED | |

## 17. Personal Sync / OneDrive (test folder only)

| # | Check | Result | Notes |
|---|---|---|---|
| 17.1 | Draft changes sync to a second PC | NOT EXECUTED | |
| 17.2 | Notes sync | NOT EXECUTED | |
| 17.3 | Conflicting edits on two PCs produce a reviewable conflict, never a silent overwrite | NOT EXECUTED | |
| 17.4 | Restart and reload keep synced state | NOT EXECUTED | |
| 17.5 | Offline: app works from local data; sync resumes later | NOT EXECUTED | |
| 17.6 | OneDrive test folder (if available); database never placed in OneDrive | NOT EXECUTED | |
| 17.7 | Mixed versions (RC.6 + RC.7 on one test folder): RC.6 re-save drops Show selections (known risk, documentation check only) | NOT EXECUTED | |

## 18. Scheduled refresh (only with the user's instruction to register the task)

| # | Check | Result | Notes |
|---|---|---|---|
| 18.1 | "Enable Automatic Refresh.cmd" registers task "Carowinds Passdown Daily Refresh" (name unchanged), no admin prompt | NOT EXECUTED | |
| 18.2 | 6 AM maintenance run completes; Deep Refresh discovers about twelve months of park hours (about 20 requests) | NOT EXECUTED | |
| 18.3 | December published dates present after the run | NOT EXECUTED | |
| 18.4 | Startup catch-up after a missed 6 AM run; skipped when checked within the hour | NOT EXECUTED | |
| 18.5 | Four-hour in-app refresh unchanged; no excess requests | NOT EXECUTED | |
| 18.6 | "Disable Automatic Refresh.cmd" removes the task | NOT EXECUTED | |

## 19. Offline and data integrity

| # | Check | Result | Notes |
|---|---|---|---|
| 19.1 | Offline: every screen opens from local data | NOT EXECUTED | |
| 19.2 | Failed retrieval keeps last-known-good values; nothing shows Closed/Not Published because of a network failure | NOT EXECUTED | |
| 19.3 | Manual draft edits never change official cached values | NOT EXECUTED | |

## 20. Known limitations to observe, not fix, in RC.7

- **Persistent save failure + date change can freeze the app** (pre-existing since RC.1). While
  every save attempt fails, changing the date, Check Online, or adding/removing/replacing a note
  retries the save with no limit; Phase 23 reproduced 100% CPU with no progress on the build host.
  Closing the window is bounded (5 s timeout). Planned fix: RC.8. During 7.7, restore write access
  before any of those actions.
- Older versions may not understand the latest Show draft selections and may discard them on re-save.
  The app does not prevent mixed-version writes.
- Math Daily Feature paragraphs are drawn as shapes in PDFs (not selectable/searchable).

## 21. Failures found (fill in)

| # | Item | Result | Reproduction steps | Screenshot/diagnostics | Needs new binary? |
|---|---|---|---|---|---|
| | | | | | |

Licensing is separate from technical validation:
**QuestPDF distribution approval pending. Fury photograph redistribution rights pending.**
