# Phase 22 — consolidated Windows validation checklist (RC.7 candidate)

One checklist for everything Phases 22A–22F changed. Run it on the packaged **`0.1.0-rc.7`** build on
Windows 11 x64. Use a test profile, or back up `%LOCALAPPDATA%\Carowinds Passdown` first.

Status key: PASS · FAIL · NOT EXECUTED. **Nothing below has been run on Windows.** Every development
check so far ran on macOS (headless Avalonia tests and Skia captures). Those cannot stand in for
Windows rendering, DPI scaling, Narrator, the native folder picker, Task Scheduler or OneDrive.

Before you start, note in the results the Windows build, display scaling, theme, and the About this
build version and path.

## 1. Shell, theme and navigation (22A–22C)

| # | Check | Status |
|---|---|---|
| 1.1 | Fury banner: compact, photo cropped around the crest, "CAROWINDS OPERATIONS" readable in Light and Dark | NOT EXECUTED |
| 1.2 | Light/Dark design system: neutral light surfaces, charcoal dark surfaces, blue accents, no Fluent light-gray panels in Dark | NOT EXECUTED |
| 1.3 | Live theme switch (Windows Settings › Personalization › Colors) while the app is open: every screen and open popup follows without a restart | NOT EXECUTED |
| 1.4 | Short navigation labels Dashboard · Calendar · Passdown · Notes · History. The active item is underlined, and the strip wraps rather than clipping at large text sizes | NOT EXECUTED |
| 1.5 | Settings gear: trailing, keyboard reachable (Tab after History), Enter/Space opens Settings, tooltip "Settings" | NOT EXECUTED |

## 2. Dashboard (22C)

| # | Check | Status |
|---|---|---|
| 2.1 | Operations Overview compact; park hours prominent; Exact vs From pricing correct for the ordinary Park Ticket | NOT EXECUTED |
| 2.2 | Operational Notes summary counts match the Notes screen | NOT EXECUTED |
| 2.3 | Data Status collapses and expands; errors and stale sources stay visible while collapsed | NOT EXECUTED |
| 2.4 | Today's Shows compact; only upcoming performances; Shows remain last in the PDF | NOT EXECUTED |

## 3. Associate Passdown (22D–22E)

| # | Check | Status |
|---|---|---|
| 3.1 | Date bar: previous/next, date picker, Today. Adjacent-month days in the picker appear as muted text, not gray tiles (22F) | NOT EXECUTED |
| 3.2 | Toolbar: Check Online, Create PDF · style, Copy ▾, More ▾. Nothing clips at 960 px or 150% | NOT EXECUTED |
| 3.3 | Section rail Overview / Products / Notes. Narrator says "Current section" for the active one (22F) | NOT EXECUTED |
| 3.4 | Attendance/pre-solds: grouped as 38,500 / 21,240 on screen, PDF and Teams Copy | NOT EXECUTED |
| 3.5 | Products: "From $x" for starting prices, exact prices plain, Military/First Responder rows, overrides "Prints as …" | NOT EXECUTED |
| 3.6 | Math picker opens, symbols insert, preview renders; Escape closes; focus returns to the editor | NOT EXECUTED |
| 3.7 | Spell check: red underlines only on misspellings, none inside math, underlines clear after navigation | NOT EXECUTED |
| 3.8 | Season Price History overlay: fits at 960×640, scrolls, Escape and Close dismiss, focus returns | NOT EXECUTED |
| 3.9 | Save indicator: "Saved on this computer at h:mm AM ET". After a failed save (for example a read-only profile) it shows "Unsaved changes" and retries | NOT EXECUTED |

## 4. Calendar (22E, 22E.1, 22F)

| # | Check | Status |
|---|---|---|
| 4.1 | Shows collapsed by default, "N scheduled shows · M performances", View schedule ⌄ expands and the choice persists across dates | NOT EXECUTED |
| 4.2 | Twelve-month future coverage: dates through about a year ahead show official hours or Not Published | NOT EXECUTED |
| 4.3 | WinterFest weekends (noon–9 PM) and New Year's Eve (3 PM–1 AM) show Verified hours. At 960 px the cell wraps "12 PM–" / "9 PM" rather than truncating | NOT EXECUTED |
| 4.4 | Date switching (22F): clicking another date updates the heading and the highlighted cell at once. The previous date's hours, pricing, Shows, passdown status and notes never appear under the new heading | NOT EXECUTED |
| 4.5 | Rapid switching (Oct 16 → Oct 17 → Dec 5; repeated Prev/Next Day) ends on the last date's details only | NOT EXECUTED |
| 4.6 | Historical dates: recorded hours only, nothing invented; backfill status line readable | NOT EXECUTED |
| 4.7 | Open Passdown / View Date Pricing open the selected date; Open History enabled only for a finalized date (never from the previous date while loading) | NOT EXECUTED |

## 5. Notes and History (22E, 22F)

| # | Check | Status |
|---|---|---|
| 5.1 | Notes rows: three lines, Important accent rule, filters, empty states | NOT EXECUTED |
| 5.2 | Notes detail: "Created Oct 12 at 11:00 AM ET · Updated …" | NOT EXECUTED |
| 5.3 | History rows: weekday title, "Finalized Oct 17, 6:02 AM ET · Scheduled", PDF / No PDF tag | NOT EXECUTED |
| 5.4 | History detail: North Gate / Avenue labels on the frozen gate times (22F); Open Final PDF opens a pre-RC.7 PDF unchanged | NOT EXECUTED |

## 6. Settings (22F)

| # | Check | Status |
|---|---|---|
| 6.1 | One page: Appearance, Data Refresh, Personal Sync, Diagnostics, About this build; scrolls at 960×640; nothing clips | NOT EXECUTED |
| 6.2 | Appearance and Data Refresh are information only; the text matches actual behavior (four-hour check, 6 AM task set up outside the app) | NOT EXECUTED |
| 6.3 | Personal Sync: enable, Choose Folder (native picker), long OneDrive paths wrap, Sync Now, Last synced "… ET", conflicts review | NOT EXECUTED |
| 6.4 | Folder unavailable / sync error show in the error color with words; Offline is not shown as an error | NOT EXECUTED |
| 6.5 | Mixed-version caution visible. With an RC.5 build on a second PC, confirm its re-save drops show selections (known risk) | NOT EXECUTED |
| 6.6 | Export Diagnostics: success "Diagnostics saved." (success color); a failure shows its reason in the error color; the ZIP has no notes, drafts, sync data, PDFs or database | NOT EXECUTED |
| 6.7 | About: product, version `0.1.0-rc.7 (…)`, running path, device ID | NOT EXECUTED |

## 7. Eastern timestamps (22F)

| # | Check | Status |
|---|---|---|
| 7.1 | With Windows set to Eastern, timestamps on Dashboard, Calendar, Notes, History, Associate save, Personal Sync and conflict details show "… ET" with the correct times | NOT EXECUTED |
| 7.2 | Set Windows to Pacific (or UTC) and restart: the same stored times still show the Eastern wall clock with "ET". The PDF footer stamp is Eastern | NOT EXECUTED |
| 7.3 | Park hours, show times and plain dates carry no "ET" | NOT EXECUTED |

## 8. Popups and overlays

| # | Check | Status |
|---|---|---|
| 8.1 | Date picker, Copy, More, Math picker, Notes Category/Priority drop-downs, tooltips: popup surface, border and radius in both themes; Escape and outside-click dismiss; focus returns | NOT EXECUTED |
| 8.2 | Season Price History and confirmation prompts fit small windows and scroll where needed | NOT EXECUTED |

## 9. PDFs and Teams Copy

| # | Check | Status |
|---|---|---|
| 9.1 | Classic / Compact / Modern: Shows last, correct pricing, grouped counts, Daily Feature math, no unexpected whitespace or extra pages | NOT EXECUTED |
| 9.2 | Teams Copy excludes Shows and the Daily Feature | NOT EXECUTED |
| 9.3 | Frozen History PDFs open byte-identical to how they were finalized | NOT EXECUTED |

## 10. Scaling, keyboard and Narrator

| # | Check | Status |
|---|---|---|
| 10.1 | 100% scaling at 960×640, 1280×690, 1280×860, 1920×1000: no clipping, no horizontal scroll | NOT EXECUTED |
| 10.2 | 125% scaling, same screens | NOT EXECUTED |
| 10.3 | 150% scaling, same screens; Calendar day cells, Settings rows, toolbar wrap | NOT EXECUTED |
| 10.4 | Keyboard only: Tab order is predictable on every screen; focus is always visible; Enter/Space activates; Escape closes popups | NOT EXECUTED |
| 10.5 | Narrator: navigation reads "Current page" / "Current section"; Calendar day cells read "Selected"; Show schedule reads expanded/collapsed; the Calendar "Loading details…" line is announced; icon-only buttons (previous/next day, Settings, date) have names; progress bars have names | NOT EXECUTED |

## 11. Data, refresh and offline

| # | Check | Status |
|---|---|---|
| 11.1 | Upgrade from RC.6: drafts, notes, History, sync settings intact; no migration prompt (DB 16) | NOT EXECUTED |
| 11.2 | Scheduled refresh (6 AM task) runs; startup catch-up skips when checked within the hour; four-hour check | NOT EXECUTED |
| 11.3 | Offline: every screen opens from local data; failures keep last-known-good values; nothing shows Closed or Not Published because of a network failure | NOT EXECUTED |
| 11.4 | Changing the selected date (Calendar or Associate) makes no network request | NOT EXECUTED |

Licensing is separate from this checklist. **QuestPDF distribution license decision pending.**
**Fury photograph distribution rights pending.**
