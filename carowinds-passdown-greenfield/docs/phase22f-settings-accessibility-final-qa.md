# Phase 22F — Settings, Accessibility & Final UI/UX Quality Assurance

**Status:** unreleased development after RC.6 (`0.1.0-rc.6`). This is the last Phase 22 polish and
verification phase. HEAD before this phase was `f685b72` (Phase 22E.1). The next packaged candidate is
**`0.1.0-rc.7`**, which is not packaged here; `Directory.Build.props` still says `0.1.0-rc.6`. RC.6
artifacts are untouched.

Scope: Settings organization; a Calendar state-consistency fix; Eastern-time display for operational
timestamps; a few accessibility, popup and responsive corrections found by rendering every screen; a
reusable synthetic screenshot harness. There are no retrieval, pricing, show-rule, persistence,
scheduling, sync-protocol or PDF-template changes. No schema changed (DB 16, Draft 4, Snapshot 2,
SyncSchemaVersion 1) and there is no migration.

## 1. Starting state

Verified before any change: branch `feature/passdown-redesign`, HEAD `f685b72`, version `0.1.0-rc.6`,
schemas DB 16 (`Migration0016…`) / Draft 4 / Snapshot 2 / Sync 1. Deterministic tests **3885/3885**:
221 Domain + 1089 Application + 852 Infrastructure + 451 Documents + 1176 Ui together + 26 + 57 + 2 + 7 + 4
in the five run-alone classes (`-c Release --filter "Category!=LiveSmoke"`). Release build 0 warnings,
0 errors. The pre-existing staged files (`.DS_Store`, `.claude/.DS_Store`, `.claude/skills/.DS_Store`,
`.claude/skills/pdf-design/SKILL.md`) and the unstaged `../.DS_Store` were left untouched and are not
part of the Phase 22F commit.

## 2. Final UI audit (before changes)

Every screen was rendered from synthetic fixtures at four sizes in both themes (§16–17) and the markup
was read. The findings, ordered by impact:

| # | Area | Finding | Impact |
|---|---|---|---|
| A1 | Calendar | After choosing a date, the panel kept the **previous** date's heading, hours, price, passdown status and notes until the load finished; the old cell stayed highlighted; Open History could be enabled from the previous date's Finalized status | data confidence |
| A2 | Timestamps | Eight presentation formatters used `ToLocalTime()` / `LocalDateTime` (PC zone), five used Eastern; none said which zone. "Last synced: 10:48 AM" had no date | data confidence |
| A3 | Settings | One long Personal Sync card with three "Status"/"Sync"/"Status" label-chip pairs; problems in the same neutral chip as "Ready"; Device ID as its own section; no word about appearance or automatic refresh | hierarchy, readability |
| A4 | Settings | Diagnostics failures looked like successes (plain text) | readability |
| A5 | Settings | The Phase 21C mixed-version risk (an older build drops show selections) was only in docs | data safety |
| A6 | Date picker | Adjacent-month days rendered as **filled gray tiles** (Fluent out-of-scope fill) in both themes, although the markup comment says they are muted | consistency |
| A7 | Calendar | At 960 px, "12 PM–9 PM" (WinterFest) truncated to "12 PM–9…" in day cells (also a 125–150% risk) | responsiveness |
| A8 | History | Frozen gate times rendered as bare "6:00 PM / 6:15 PM" lines under Park Hours with no labels | readability |
| A9 | Accessibility | The Associate section rail's current section was a style only (the app strip already announces "Current page"); progress bars had no names | accessibility |

Checked and found correct: top navigation (names, "Current page" item status, Tab order), all icon-only
buttons named (Previous/Next day, Settings gear, date button, Math picker), Calendar Shows disclosure
(ExpandCollapse), Price History overlay (Escape, Close name, fits 960×640), dark popup surfaces
(MenuFlyout, Flyout, ComboBox, ToolTip tokens), token contrast (§14), no horizontal overflow anywhere.

## 3. Settings organization

One scrollable page (no tabs, no expanders), max 720 px wide, in five groups. Each group has a section
heading over one panel; label/value rows share one `Grid.settingsRows` (140 px label column, flexible
value column):

1. **Appearance** — information only (see §4).
2. **Data Refresh** — information only (see §5).
3. **Personal Sync** — enable, Folder (path or "No folder chosen yet.", detected OneDrive, Choose
   Folder…), Folder status, Automatic sync (status, detail, Last synced), Sync Now + result, conflict
   callout, mixed-version caution (see §6). Conflict list and conflict detail follow when opened.
4. **Diagnostics** — see §7.
5. **About this build** — App, Version, Running from, the stale-copy hint, Device ID (moved from its own
   section) and its privacy note.

Every existing binding, command and accessible name is kept (asserted). Spacing: heading → panel 8 px,
panel → next heading 20 px, rows 8 px. The header line says "Settings for this computer. Nothing here
changes official park data."

## 4. Appearance

The app has no theme setting: `App.axaml` uses `RequestedThemeVariant="Default"`, so it follows Windows.
No control was invented. The Appearance row says "Theme: Follows Windows — Light or Dark" and points to
Windows Settings › Personalization › Colors. Live switching is the existing behavior (real-window tests
switch Light → Dark → Light). The Fury banner is theme-invariant, with its own scrim (Phase 22B).

## 5. Refresh presentation

Information only, with no control. The intro says so: "There is nothing to set here."

| Row | Text (matches existing behavior) |
|---|---|
| When the app opens | Checks official data, unless it was checked within the last hour (Phase 19A 60-minute grace) |
| While it is open | Checks again every four hours |
| Daily at 6 AM | Windows scheduled task, full maintenance including park hours about 12 months ahead (22E.1); "set up with the app's installation scripts, not from this screen" |
| Past dates | Missing park hours from earlier this season are filled in while Carowinds still publishes them (20C) |

## 6. Personal Sync presentation

- Folder status and Automatic sync are labelled rows with `statusBadge` chips. They turn to the error
  style only for real problems: `PersonalSyncStatusPresentation.IsProblem` (Folder unavailable,
  Configuration error) and `PersonalSyncAutomationStatusPresentation.IsProblem` (Error, Conflict, History
  conflict). Offline is **not** a problem (the Phase 16F "safe locally" rule). The words always carry the
  meaning.
- The automatic-sync detail (for example "Couldn't write to the sync folder (access denied).") uses the
  error color when it is a problem.
- "Last synced: Oct 17 at 10:48 AM ET". It was a bare time.
- Conflicts sit in one inset callout with a warning badge. The duplicate "Status" label is gone.
- Long sync paths are selectable and wrap (real-window test at 960 px).
- **Mixed-version caution** (visible, amber `fieldWarning`): "Use the same app version on every computer
  that shares this folder. An older version can drop show selections when it re-saves a synced passdown
  draft." This is information only. Nothing claims automatic protection, because none exists. Sync
  schema, payloads and algorithms are unchanged.

## 7. Diagnostics

Same privacy-safe export service, ZIP and save picker, still in Settings. The button is on its own line.
The result line uses `statusSuccess` ("Diagnostics saved.") or `statusError` (the picker's reason or
"Diagnostics could not be created. Try again."), driven by the new
`DiagnosticsExportViewModel.IsDiagnosticsExportProblem`. A cancel shows nothing. It is announced politely.
The description is unchanged in substance: never notes, Daily Feature, drafts, sync data, PDFs or the
database.

## 8. About

Same `BuildIdentity` logic: product name, `0.1.0-rc.6 (<commit>)` during development (never labelled
RC.7), running path (selectable, wraps) and the stale-copy hint. Device ID moved here as a row.

## 9. Calendar date-loading correction

`OperationsCalendarViewModel` (UI state only; the existing `_loadVersion` generation token is unchanged):

- `SelectedDateHeadingText` / `SelectedDateIsToday` / `DisplayedMonthText` come from `SelectedDate` /
  `DisplayedMonth`, so the heading, Today chip and month title change the moment a date is chosen.
- `IsSelectedDetailCurrent` is true only while `Snapshot.Selected.Date == SelectedDate`. The view wraps
  Park Status, Daily Ticket, Shows, Passdown Status and Active Operational Notes in `SelectedDateDetails`,
  which is shown only then. Otherwise one compact line (`SelectedDetailPlaceholderText`, polite live
  region) says "Loading details…" or, after a failed read, "Details for this date are unavailable."
- Day cells: `OperationsCalendarDayViewModel.IsSelected` is now observable and updated on selection,
  so the new cell is highlighted and its accessible name says "Selected" at once.
- `CanOpenHistory` additionally requires the current detail, so Open History is never enabled from the
  previous date's Finalized status.
- **Rapid switching:** each change bumps `_loadVersion`, and only the latest load's result is applied.
  Tested: Oct 16 → Oct 17 → Dec 5 with completions in the order Dec 5, Oct 16, Oct 17. Dec 5 stays.
  Seven day steps make seven local reads and end on the last date.
- **No extra retrieval:** the Calendar view model still depends only on `IOperationsCalendarService` (local)
  and `IClock`. A date change makes exactly one local read, as before. Shows expansion, View Date
  Pricing, Calendar → Passdown, caching, park-hours data and the 22E.1 coverage are unchanged.
- **Flash avoidance:** local reads take milliseconds. The placeholder is one muted line, not a spinner
  or panel.

## 10. Failure behavior

- If a read fails for a date in the **displayed month**, the grid stays (it is still that month's data),
  the chosen date stays selected and highlighted, and the panel shows the "unavailable" line. The error
  line says "Couldn't load October 16, 2026 from this computer's saved data. Choose the date again to
  retry." Choosing the date again retries.
- If a read fails for **another month**, the grid is cleared as before ("Unable to load the operations
  calendar right now."). The month title still names the month.
- No state is inferred from a failure. It is never shown as Closed or Not Published.

## 11. Eastern timestamp policy

`Desktop/Presentation/EasternTimestampFormat` is the one display formatter for operational timestamps.
It is built on the existing `Application.Time.EasternTimeZone` (IANA America/New_York) and is not a
second time-zone utility:

| Method | Example |
|---|---|
| `Time` | `6:05 AM ET` |
| `DateAndTime` | `Oct 17 at 6:05 AM ET` |
| `DateYearAndTime` | `Oct 17, 2026 at 6:05 AM ET` |
| `Relative(instant, now)` | `6:05 AM ET` on the same Eastern day, else `Oct 14, 6:05 AM ET` |
| `Format(instant, pattern)` | any US pattern + ` ET` |

Routed through it: park-hours verification/stale lines, Dashboard last saved/finalized, data health,
category status, Deep Refresh status, official-data status, admission freshness, show-schedule freshness
(Dashboard, Associate, Calendar), Calendar passdown status, Notes created/updated/resolved, History
finalized (row, detail, accessible text), Associate save status, Personal Sync last synced, and sync
conflict "Changed on another device on …". The Associate PDF footer stamp ("Oct 17 • 6:05 AM") now uses
the Eastern wall clock too, with no label and no layout change. Stored values, business conversions,
date-only fields, schemas and payloads are untouched.

Not labelled: park hours and show times (schedule values that are already Eastern operating-day wall
clock) and date-only values (Price History dates, note effective dates, operating dates). No timestamp
was found without an offset. Every one converted is a stored UTC instant (`DateTimeOffset`). The only
remaining host-zone read is Associate's "is it today on this PC?" highlight (Phase 21B), by design. A
test scans Desktop and Documents sources for any other `ToLocalTime` / `LocalDateTime` /
`TimeZoneInfo.Local` use.

## 12. DST handling

`TimeZoneInfo.ConvertTime` applies the real rules. There is no fixed offset anywhere. Tested: EDT (Oct)
and EST (Jan); 06:59Z / 07:00Z on Mar 8, 2026 → 1:59 AM / 3:00 AM; both 1:30 AMs on Nov 1, 2026 (05:30Z
EDT, 06:30Z EST); after midnight UTC still the previous Eastern day (00:30Z Oct 18 → Oct 17, 8:30 PM);
the UTC-only year boundary (04:30Z Jan 1 → Dec 31, 11:30 PM, History shows 2026); a close after
midnight (05:10Z → Oct 18, 1:10 AM). The instant's own offset (+09:00, −07:00) never changes the text,
and host culture (de-DE, ja-JP, ar-SA) never changes the format. This Mac runs in EDT, so it cannot
switch its own zone. Host independence is proven by construction (the source scan) and by the
offset-invariance tests. The real Windows-zone check is in the checklist (§20, 7.2).

## 13. Source-status consistency

Reviewed Verified / Closed / Not Published / Unavailable / Error / Stale / Checking / last-known-good
wording across Dashboard, Calendar, Associate and History. Status classification and wording were already
consistent and evidence-based (22C–22E.1). Phase 22F changed only the timestamp suffix. It made no
change to classification. The 22E.1 conservative Not Published decision is preserved: the fixture's
March 2027 date renders "Not Published", never Closed. Missing shows are never "No shows scheduled", a
failed show check says "Couldn't check the show schedule." in the error color, and stale values keep
"may be out of date". Calendar load failures add no status (§10).

## 14. Popup and overlay refinements

- **Date picker:** adjacent-month days (`CalendarDayButton:inactive`) are now transparent with
  `PassdownTextMutedBrush` text (5.65:1 light, 5.39:1 dark on the popup surface). Hover still wins. Today
  ring and selected fill are unchanged.
- Copy, More, Math picker, Notes drop-downs and tooltips were checked in both themes. They already use the
  popup tokens (no default light-gray surfaces in Dark). No template changes.
- Season Price History fits 960×640 and scrolls. Escape and Close still dismiss it. No size change.
- Menu content, action semantics, dismissal, focus restoration and autosave are unchanged.

Token contrast (WCAG, text on surface/app background/popup), from `PassdownTokens.axaml`:
primary ≥ 11:1, secondary ≥ 7.2:1, muted ≥ 5.1:1, warning ≥ 5.4:1, error ≥ 5.9:1, success ≥ 5.9:1, accent
link ≥ 6.2:1, stale ≥ 5.8:1 in both themes. Only disabled text is below 4.5:1, and WCAG exempts it.
No new tokens were added.

## 15. Accessibility and keyboard behavior

- Associate section rail: `Button.railItem.active` now sets `AutomationProperties.ItemStatus="Current
  section"`, the same style-setter convention as the app strip's "Current page" (Phase 22B). Real-window
  test: status follows navigation, and only the active item has one.
- Calendar: heading accessible name "Selected date: …"; day cells say "Selected" immediately; placeholder
  is a polite live region; Show schedule keeps ExpandCollapse.
- History: gate times read "North Gate: 6:00 PM" / "Avenue: 6:15 PM". The visible labels are display
  only, and the frozen text is unchanged.
- Progress bars are named (Loading dashboard / calendar / operational notes / history / finalized
  passdown, Working, Checking official data, Deep refresh in progress).
- Settings: problem chips and details are words plus color; conflict buttons are named "Review conflict:
  <item>"; Close and Cancel are named.
- Keyboard: Settings Tab order runs top to bottom (Enable → Choose Folder → … → Export Diagnostics), and
  Tab never toggles anything (real-window test). App strip and gear Tab order are unchanged (Phase 22B
  tests). Escape closes popups and Price History as before.
- A markup test parses every view and fails on any Button/ToggleButton without visible text, or any
  Expander/ProgressBar/ComboBox/ListBox/TextBox, that has no accessible name.
- Target sizes are unchanged: Calendar arrows and day buttons, date-picker days (38 px), Math symbols
  (about 32 px), checkboxes.

**Narrator: NOT EXECUTED** (no Windows host). These are automated property checks only. They are not a
screen-reader validation. The manual plan is checklist §10.5.

## 16. Synthetic fixture coverage

`tools/visual-fixtures/` (new, committed, **not** in the solution) is the earlier phases' scratch harness
promoted to a reusable tool. It is a console app that renders the real views with Avalonia Skia under
the headless platform, using the Ui.Tests fakes (linked source) and `Fixtures.cs`. It has no database, no
network and no desktop lifetime. Usage is in its `README.md`. The real profile listing was identical
before and after every run.

Fixtures (all synthetic): busy operating day (38,500 projected, 21,240 pre-solds), verified exact Park
Ticket, Military/First Responder From pricing, a stale Fast Lane with an override, two selected shows,
Daily Feature math, Important and normal notes, nine History records (one manual, one without a PDF, one
without notes), WinterFest weekends and New Year's Eve hours, historical September hours, Not
Published 2027 dates, stale October hours, a failed show check, partial source failure (Dashboard),
empty Notes/History, Personal Sync off / up to date / two conflicts / folder unavailable + sync error,
Diagnostics failure, autosave failure (the fake's new `SaveException`), the Season Price History
overlay, and the four popups.

## 17. Visual screenshot comparisons

Before images come from the same harness compiled against a `git archive` of `f685b72` (65 at
1280×860 + WinterFest at all sizes). After images: **232** (29 scenarios × Light/Dark × 960×640,
1280×690, 1280×860, 1920×1000). They are kept in the session scratchpad, not committed. Reviewed by
eye:

| Screen | Before → after |
|---|---|
| Calendar switching | old heading/details under a new click → new heading + "Loading details…", old cell un-highlighted |
| Calendar failed read | (n/a) → grid kept, honest one-line error, "unavailable" panel line |
| Calendar WinterFest 960 px | "12 PM–9…" → "12 PM–" / "9 PM" |
| Settings | one dense card with three status pairs → five labelled groups; error chips red; "Last synced … ET" |
| Date picker | gray tiles for adjacent days → muted text |
| History detail | unlabelled gate times → "North Gate · 6:00 PM", "Avenue · 6:15 PM" |
| Dashboard / Associate / Notes / Products / Price History / popups | unchanged layout; timestamps now "… ET" |

## 18. Responsive behavior

`<label>-geometry.txt` for all 232 after-captures: **no horizontal overflow and no button past the
window's right edge** at any size or theme. Settings scrolls vertically. It grew by about 240 px at
1280×860 for the two information groups, and the page already scrolled. Real-window tests:
Settings at 960×640 (Light/Dark), 1280×690, 1280×860, 1920×1000 (no clipping, long path wraps); Calendar
mid-switch at 960×640 and 1280×860. Mac Skia renders are **not** DPI validation (§20).

## 19. Test coverage

| Class | Partition | Count | Covers |
|---|---|---|---|
| `Phase22FCalendarAndTimeTests` (Ui) | mixed | 30 | heading/details/placeholder on a held change; cell highlight + "Selected"; Open History not from the previous date; month title; out-of-order completions; repeated stepping = 1 local read each; Shows expansion kept; same-month and other-month failures with retry; markup gating; Eastern format across EDT/EST/both DST transitions/midnight UTC/year boundary/after-midnight close; offset and culture invariance; host-zone source scan; History year boundary; no label on schedules/dates; Last synced |
| `Phase22FSettingsAccessibilityTests` (Ui) | mixed | 38 | five groups in order, single page; every binding/command/name kept; Appearance/Refresh information only and honest; mixed-version caution without a protection claim; wrapping paths; problem flags (5 + 8 cases); Diagnostics success/failure/cancel/exception and its styling; About identity; failed autosave stays "Unsaved changes" with the stored draft untouched; rail ItemStatus; accessible names in all eight views; Calendar placeholder/heading names; History gate labels; day-cell wrap; date-picker inactive style; popup tokens in both themes; Settings uses only shared status styles |
| `Phase22FGeneratedTimestampTests` (Documents) | normal | 6 | PDF footer Eastern across EDT/EST/DST/midnight UTC; offset invariance |
| `Phase15ERealWindowLayoutTests` (Ui) | run-alone | +9 | Settings at five size/theme combinations; Settings Tab order; strip + rail ItemStatus follow navigation; Calendar mid-switch panel at two sizes |

Intentional updates: 23 existing assertions gained " ET" (Phase19A status, 21C freshness, 22C admission/shows,
22E Calendar shows/Notes/History, `UsPresentationCultureTests`). The host-local expectations in
`UsPresentationCultureTests.LocalWallClockTimes…` and the 22E Notes `Stamp` helper now use Eastern. "Last
synced" expects a date. `Phase22ECalendarShowsTests.Markup_KeepsEveryExistingDetailAndRoute` expects
`SelectedDateHeadingText` instead of `Presentation.SelectedDateText`. No test was weakened.

Deterministic total: **3968/3968** (baseline 3885; +83) = 221 Domain + 1089 Application + 852
Infrastructure + 457 Documents + 1244 Ui together + 26 `OperationsCalendarViewTests` + 66
`Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests`
+ 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). The
run-alone set is unchanged (five classes). Known intermittent: `DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender`
failed once in the first full run and passed 5/5 on rerun (Documents 457/457), the same flake recorded in
22E and 22E.1. The final full run on the committed source passed 3968/3968 with no failure. Release build: 0 warnings, 0 errors.

Regression coverage kept: pricing, Military/First Responder, product lifecycle, historical pricing,
future park-hours discovery, backfill, shows, Associate selections, Daily Feature math, spell check,
draft persistence, Personal Sync, frozen History, PDFs, Teams Copy (Shows and Daily Feature excluded)
and scheduled refresh all pass unchanged. No PDF template changed. The only Documents change is the
footer's time zone, which is byte-identical on Eastern PCs.

## 20. Windows validation checklist

Consolidated for all of Phase 22: **`docs/phase22-windows-validation-checklist.md`** (shell, theme,
navigation, Dashboard, Associate, Calendar, Notes/History, Settings, Eastern timestamps, date switching,
popups, Math picker, spell-check underlines, Price History, PDFs/Teams Copy, 100/125/150% scaling,
keyboard, Narrator, Personal Sync, scheduled refresh, offline). Every item is **NOT EXECUTED**. Phase
22F-specific items: 3.1, 3.3, 3.9, 4.3–4.5, 4.7, 5.2–5.4, 6.1–6.7, 7.1–7.3, 10.5.

## 21. Remaining limitations

- No Windows run: no real DPI scaling, Narrator, native folder picker, Task Scheduler or OneDrive check.
- Host independence is shown structurally and by offset tests. This Mac cannot run in another zone in-process.
- Settings is taller by the two information groups. They are deliberately visible, not collapsed.
- A failed autosave still reads "Unsaved changes" and retries on the next change or flush (existing
  behavior; no new alerting).
- The Dashboard's "Today at a Glance" shows the Daily Feature source text (math markup is rendered only
  in Associate and PDFs, per Phase 21D scope).
- The mixed-version risk is a caution, not a protection.
- The visual harness depends on Ui.Tests fakes by linked source. It needs updating if those fakes change
  shape.
- **QuestPDF distribution license decision pending.** **Fury photograph distribution rights pending.**
  Neither is affected or cleared by this phase.

## 22. RC.7 readiness

Phase 22 (22A–22F) is complete in source. Ready to package **`0.1.0-rc.7`** in a separate step: bump
`Directory.Build.props` (version + file version), publish, then run the consolidated Windows checklist
on the package. Blocking before distribution: Windows validation (above) and the two licensing
decisions. Not done here: no version change, no packaging, no installer, no auto-update.
