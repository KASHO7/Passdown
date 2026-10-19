# Phase 22E — Calendar, Operational Notes & History Refinement

**Status:** unreleased development after RC.6 (`0.1.0-rc.6`). Builds on Phases 22A–22D (HEAD before
this phase: `7e7ca9b`). Next packaged candidate: **`0.1.0-rc.7`** (not packaged here;
`Directory.Build.props` still says `0.1.0-rc.6`). RC.6 artifacts are untouched.

Scope: presentation of the Operations Calendar's selected-date panel (compact, expandable Shows),
Operational Notes list/detail, History list/detail, and one pricing-presentation correction (Associate →
Products labels advertised starting prices "From"). No retrieval, refresh, scheduling, pricing,
persistence, finalization, Personal Sync or PDF-template change. No schema changed (DB 16, Draft 4,
Snapshot 2, SyncSchemaVersion 1); no migration.

Files:

- `src/Carowinds.Passdown.Domain/Catalog/AdvertisedPriceText.cs` — new shared "From $x" rule (§14–15).
- `src/Carowinds.Passdown.Documents/Associate/AssociatePassdownDocumentModelBuilder.cs` — its private
  `FormatPrice` now delegates to `AdvertisedPriceText` (identical output).
- `src/Carowinds.Passdown.Desktop/Presentation/CalendarShowsPresentation.cs` — new (§3–6).
- `src/Carowinds.Passdown.Desktop/Presentation/ShowSchedulePresentation.cs` — freshness builder shared.
- `src/Carowinds.Passdown.Desktop/ViewModels/OperationsCalendarViewModel.cs` — `CalendarShows` (replaces
  the Calendar's `ShowsPresentation`), `IsShowScheduleExpanded`, `ShowScheduleToggleText`.
- `src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml` — selected-date panel (§2–7).
- `src/Carowinds.Passdown.Desktop/Presentation/OperationalNotePresentation.cs`,
  `ViewModels/OperationalNotesViewModel.cs`, `Views/OperationalNotesView.axaml` — Notes (§8–10).
- `src/Carowinds.Passdown.Desktop/ViewModels/MainViewModel.cs`, `Views/DashboardView.axaml` — Dashboard
  Open Notes opens today (§11).
- `src/Carowinds.Passdown.Desktop/Presentation/PassdownHistorySummaryPresentation.cs`,
  `Views/HistoryView.axaml` — History (§12–13).
- `src/Carowinds.Passdown.Desktop/ViewModels/ProductRowViewModel.cs`, `Views/AssociateView.axaml` —
  Products "From" (§14).
- Tests: `AdvertisedPriceTextTests` (Domain), `Phase22ECalendarShowsTests`, `Phase22ENotesHistoryTests`,
  `Phase22EProductPriceLabelTests` (Ui, mixed partition), +10 in `Phase15ERealWindowLayoutTests` (run-alone);
  intentional updates listed in §19.

## 1. Starting audit (at `7e7ca9b`)

Baseline verified: branch `feature/passdown-redesign`, HEAD `7e7ca9b`, **3718/3718** deterministic tests
(one known-intermittent Documents byte-compare — `DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender`
— failed once in the baseline run and passed 3/3 on rerun), Release build 0 warnings / 0 errors, version
`0.1.0-rc.6`, schemas DB 16 / Draft 4 / Snapshot 2 / Sync 1. Pre-existing staged files (`.DS_Store`,
`.claude/.DS_Store`, `.claude/skills/.DS_Store`, `.claude/skills/pdf-design/SKILL.md`) and the unstaged
`../.DS_Store` were left untouched and are not part of the Phase 22E commit.

**Calendar.** Month grid of fixed-height (132 px) day cells: day number, state label (CLOSED / NOT
PUBLISHED / …), hours, Carolina Harbor line, Stale tag, passdown status, notes count; today = outlined,
selected = filled + outlined. No legend exists (states are words in the cells). Selected-date panel
(`heroPanel`, 320 px): "Selected Date", date, Today chip, Prev/Next Day, then Park Status, Effective
Hours, verification/stale lines, Carolina Harbor, Daily Ticket, **Shows** (heading "Shows — October 24",
then every show as name / times / venue, then freshness), Passdown Status, Active Operational Notes, and
four routing buttons. Every element was a direct child of one 10 px-spaced stack, so headings floated
as far from their values as from the previous section. A busy day (6 shows, 22 performances) made the
Shows block **360 px** and the panel **978 px** — taller than the month grid, so the page scrolled
336 px at 1280×860. For today the Calendar reused the Dashboard's "upcoming only" presentation.
Calendar → Associate ("Open Passdown") and View Date Pricing carry the selected date; Dashboard → Open
Passdown opens today.

**Operational Notes.** Lifecycle tabs (All/Active/Upcoming/Expired/Resolved), Category and Priority
filters, Clear filters, New Note. Each list row: bold title, "Category · Priority" (Important in the
accent `priorityText.important` style), date range, carry-forward line (often repeating the range:
"Oct 17 – Oct 24" then "Through Oct 24"), and a lifecycle chip — five lines, **105–118 px** per row.
Detail: title, chips, Effective From/Through, carry-forward, Resolved time, body, Edit/Resolve (inline
confirm). No created/updated time shown. Empty states: "No operational notes yet." and per-filter
messages. Explicit Save/Cancel editor; no delete (by design).

**History.** List (280 px): "October 16, 2026", "Finalized Oct 17, 2026 at 6:02 AM", then two chips
(origin, PDF availability) — **69 px** per row. No filters. Detail: title, finalized time, chips,
read-only badge, then four separate cards (Park Hours, Attendance, Daily Feature, Early Ride), a card per
product category, Shows, Operational Notes, and the PDF actions at the very bottom.

**Associate Products.** The OFFICIAL column printed `PriceFormatting.FormatOrNull(amount)` — "$40.00"
even for a `PriceKind.StartingFrom` row; the "Starting From" qualifier sat in italics under the name.
PDFs (`AssociatePassdownDocumentModelBuilder.FormatPrice`) and Teams Copy (which prints the PDF row
text) already printed "From $40.00". The evidence (`AssociateProduct.PriceKind`) was available to the
view model but unused for the label.

Smallest necessary changes: a dedicated compact Calendar Shows presentation + disclosure; grouping the
Calendar panel's sections; denser Notes and History row templates with the same facts; promoting the
History PDF actions; one shared price formatter used by both the PDF model and the Products row.

## 2. Calendar visual refinements

- Each information group (Selected Date, Park Status, Daily Ticket, Shows, Passdown Status, Active
  Operational Notes) is its own stack — small uppercase `subsectionTitle` label, 2 px to its values,
  14 px between groups. Hairlines separate identity/day-stepping, information, and routing (unchanged).
- The Today chip sits beside the date; "Effective Hours" is no longer a separate label line (the hours
  are the line under the state, with the full "Effective hours: …" accessible name); Carolina Harbor is
  one line ("Carolina Harbor · 11:00 AM – 7:00 PM").
- Prev/Next Day moved from a horizontal StackPanel to a WrapPanel: under headless (fake) font metrics
  the pair overflowed the panel by 21 px — a pre-existing risk at 125–150% scaling; now it wraps.
- No new colors: all Phase 22A tokens. Day cells, their geometry, today/selected treatments and the month
  navigation are unchanged; no show times were added to cells; no legend was added (none existed and the
  cells already state their status in words).

## 3. Shows compact summary

Collapsed (default):

    SHOWS
    6 scheduled shows · 22 performances
    View schedule ⌄
    Official schedule checked 10:32 AM

Built by `CalendarShowsPresentation.Build(view, nowUtc)` from the selected date's own
`ShowScheduleView` (the persisted last-known-good record). Measured: **86 px** collapsed vs **360 px**
before for the same busy day.

## 4. Expansion behavior

- The disclosure is the shared Phase 22C `Expander.disclosure` ("View schedule ⌄" / "Hide schedule ⌃"):
  a real ToggleButton header (Tab, Enter/Space, Fluent focus adorner) and the Expander's ExpandCollapse
  automation pattern. Accessible name "Show schedule".
- `OperationsCalendarViewModel.IsShowScheduleExpanded` — false when the Calendar view model is created,
  kept across date changes, day stepping, month navigation, Go to Today and refreshes for the rest of the
  app session; never persisted (a new app run starts collapsed).
- Toggling is pure presentation: no service call, no reload, no change to `SelectedDate`, no Associate
  or draft interaction (the Calendar view model has no draft dependency at all).
- `CalendarShows` is rebuilt whenever the snapshot or the selected date changes and is **null while the
  snapshot is for another date** — between a date change and its load completing the Shows group is
  hidden, so a previous date's lineup is never shown under the new date (asserted with a gated load).
- The disclosure is visible only when there is a verified schedule (`HasSchedule`); a date without one
  shows its status sentence and no empty "View schedule".

## 5. Show/performance counting

- Shows merge by official `ShowId` (case-insensitive; the name only if an id were blank), so a duplicated
  record counts once and contributes the union of its times; two different ids stay two shows even with
  the same name.
- Performances = each merged show's `ShowTimes.Normalize` set (distinct, operating-day order — times
  before 4:00 AM belong to the end of the night). Shows with no start time are never counted.
- Counts are exact: six shows × four times → "6 scheduled shows · 24 performances". Singular/plural:
  "1 scheduled show · 1 performance", "2 scheduled shows · 2 performances" (counts ≥ 1,000 grouped via
  `OperationalCount`). No duration is ever inferred.
- Expanded rows: name (SemiBold, wraps), then "7:00, 8:00, 9:30, 10:30 PM · Carowinds Theatre"
  (`ShowTimes.FormatCompactList`, every time with exact minutes; venue muted, omitted when not
  published), hairline-divided, ordered by first performance then name. Each row's accessible name:
  "Wake the Dead, 4 performances: 7:00, 8:00, 9:30, 10:30 PM, Carowinds Theatre".
- The Calendar is a planning view: even for today it lists every verified time (the Dashboard keeps its
  "upcoming only" Today's Shows).

## 6. Schedule status presentation

| `ShowScheduleState` | Calendar wording | Count / disclosure | Problem style |
|---|---|---|---|
| Scheduled | "N scheduled show(s) · M performance(s)" | yes | only if the latest check failed |
| NoShows (verified empty) | "No shows scheduled for this date." | no | no |
| NotPublished | "Show schedule not published." | no | no |
| ParkClosed | "No shows — park closed." | no | no |
| CouldNotCheck (only failures) | "Couldn't check the show schedule." | no | yes (`statusError`) |
| HistoricalNotAvailable | "Show schedule not available for this past date." | no | no |
| NotChecked (no record yet) | "Show schedule unavailable — not checked yet." | no | no |

Last-known-good after a failed check keeps the verified schedule and its count, with
"Official schedule checked Oct 15, 10:32 AM · latest check failed" (the Phase 21C freshness builder, now
shared). "0 scheduled shows" is never printed; a confirmed empty schedule is said in words. Status is
always text; color only reinforces.

## 7. Historical Calendar behavior

Unchanged rules: past dates show only what was recorded for that exact date (`ShowScheduleView.Compose`
→ HistoricalNotAvailable when nothing was recorded); no past show schedule is requested or backfilled; no
early-January hours invented; park-hours history, current-year backfill status line and its priority are
untouched; no current product or price is projected onto a past date. A past date with a saved record
shows that record's count and times.

## 8. Notes list refinements

Rows are three lines in divided rows (hairline under each, 8 px vertical padding) instead of five lines
plus a chip:

    South Gate scanner #3 out of service
    General · Important
    Active · Oct 15 – Oct 20

- `ScheduleText` = lifecycle word · date range, plus the carry-forward phrase only when it adds a
  relative fact: "· Expires today", "· Expires tomorrow", "(tomorrow)" for an upcoming note. The redundant
  "Through Oct 24" / "Until resolved" / "Expired Oct 14" repeats are gone; every fact is still on the row.
- Important (the supervisor's explicit priority — never inferred from text): a 3 px accent rule at the
  row's left edge plus the existing bold "Important" word. No warning boxes; never color alone.
- Row accessible name: "Scanner down, Important, General, Active · Oct 17 – Oct 19".
- Measured: average row **109 → 66 px**; fully visible rows at 1280×860 **5 → 8**, at 960×640 **3 → 6**.

Detail: the stored created/updated times are shown under the effective dates — "Created Oct 12 at
11:00 AM · Updated Oct 17 at 8:00 AM" (Updated only when edited ≥ 1 minute after creation). They use the
screen's existing timestamp formatter (the one Resolved already used: en-US, `ToLocalTime()` — the
Eastern wall clock on the target PCs). Stored values and conversion logic are unchanged.

## 9. Notes filtering and editing

All lifecycle tabs, Category/Priority filters, Clear filters, New Note, the editor (title, category,
priority, effective dates, Until resolved, body, spell-check), Save/Cancel, Resolve with confirmation, the
unsaved-editor navigation guard and Personal Sync are untouched (asserted in markup; filter result sets
asserted unchanged). There is no delete in this product (unchanged).

## 10. Notes empty states

- No notes at all: "No operational notes yet." + New Note (unchanged, compact).
- Filtered empty: unchanged per-tab sentences, plus: with Priority = Important the sentence says
  "important" — "No active important operational notes. Try clearing filters." / "No important
  operational notes. Try clearing filters." No illustration panels, no sample notes.

## 11. Dashboard → Notes navigation decision

- **View Notes →** (Operational notes tile) opens the Operational Notes manager. That screen is not
  date-filtered: lifecycle is re-derived against today's Eastern date on every load. Behavior preserved:
  filters and selection are kept, Associate's date is not touched (asserted).
- **Open Notes** (Passdown tile) opens Associate → Notes, which *is* date-scoped, and the tile reports
  today's draft note lines. Phase 22C recorded that it inherited whichever date Associate last showed;
  it now opens **today's Eastern date** through the same flush-then-load path as Open Passdown and View
  Pricing (`AssociateViewModel.SelectEasternToday`). Its help text says "today's". Calendar and History
  routes keep their explicit dates. Two existing tests that pinned the old behavior were updated (§19).

## 12. History refinements

- List rows: two lines — "Friday, October 16, 2026" (weekday added) with a compact **PDF** / **No PDF**
  tag at the right (No PDF in the stale/amber token; tooltip and accessible name carry the full "PDF
  available" / "Exact historical PDF unavailable"), then "Finalized Oct 17, 6:02 AM · Scheduled" (the
  finalized year only across a year boundary). Divided rows, no chips. Measured **69 → 46 px**; all nine
  rows visible at 960×640 (was six).
- Detail: identity block (date, finalized time, origin/PDF chips, read-only badge) followed immediately by
  **Open Final PDF** (now the screen's primary action) and Save PDF Copy; then one "Finalized summary"
  panel holding Park Hours | Attendance and Daily Feature | Early Ride Time as labelled groups (four
  cards → one); one Products panel with category bands (a card per category → one panel); Shows (venue
  on its own muted line); Operational Notes, which now says "No operational notes were recorded." when the
  frozen list is empty instead of showing a bare heading.
- With no records at all, the detail pane no longer says "Select a finalized passdown from the list"
  (there is nothing to select); the list's own empty state remains.
- No filters exist; none added. Selection, on-demand detail loading, `SelectByDateAsync` (Calendar → Open
  History) are unchanged.

## 13. Frozen History preservation

Only XAML and display-only presentation properties changed. The detail binds the same frozen snapshot
fields (all asserted present); prices print the frozen `EffectivePriceText` exactly as finalized ("From
$40.00" stays as stored; nothing is re-priced). Open Final PDF / Save PDF Copy still go only through
`IFinalizedPassdownPdfRetrievalService` and hand the launcher the retrieved byte array itself (asserted
`Same`). The History seams expose only `ListAsync`/`GetAsync`/`GetPdfAsync` (asserted by reflection) — no
code path here can regenerate a PDF, reformat stored numbers, reorder stored Shows, re-finalize or
rewrite a snapshot. `AdvertisedPriceText` produces byte-identical PDF text, so newly generated PDFs are
unchanged too; stored PDFs are never touched.

## 14. Associate "From" correction

| Evidence (`AssociateProduct.PriceKind`) | OFFICIAL column | Override placeholder | Notes |
|---|---|---|---|
| ExactDated (incl. Military/First Responder dated exact) | `$59.00` | Use official ($59.00) | |
| StartingFrom (advertised minimum, incl. Military/First Responder fallback, Bring-A-Friend catalog) | `From $40.00` | Use official (From $40.00) | the "Starting From" qualifier line is not repeated (it is in the price); other qualifiers stay |
| StandardCatalog / Sale / Bundle / Unknown | `$129.99` | | never prefixed — an amount's presence never implies "From" or exactness |
| No amount | `Not priced` (or the source's own display string, unchanged) | | never a bare "From" |

Overrides: the stored override is still the number only (`DisplayPriceOverride`, unchanged storage and
parsing). Because the PDF keeps the row's `PriceKind`, an override on a starting-price row prints
"From $35.00"; the Products row now says so under the box — "Prints as From $35.00" — and
`EffectivePriceText` matches. Stale rows keep their "From" plus the existing stale note; historical dates
keep the evidence recorded for that date. Custom/internal items are unchanged (no official evidence).

## 15. Shared formatting behavior

`Carowinds.Passdown.Domain.Catalog.AdvertisedPriceText.Format(amount, kind)` is the one rule: "From " +
`PresentationCulture.FormatUsd(amount)` for `PriceKind.StartingFrom`, else the plain amount; host culture
ignored. Used by the document model (so Classic, Compact and Modern PDFs and Teams Copy, which prints the
PDF row text) and by `ProductRowViewModel`. A parity test builds the real document model from the same
workspace and asserts every Products row's `EffectivePriceText` equals the PDF row's, across exact, From,
dated Military/First Responder, catalog, sale and overridden rows, and checks the Teams pricing lines.
No PDF template changed. Intentional remaining difference: the Calendar's Daily Ticket line keeps its
existing "Starting from $x" wording (Phase 13D; not part of Associate Products).

## 16. Accessibility

- Calendar Shows: named disclosure ("Show schedule"), ExpandCollapse state, Enter/Space, focus kept on
  the header after toggling, Fluent focus adorner; summary has an accessible name ("Shows: 6 scheduled
  shows · 22 performances"); rows announce name, performance count, times and venue; rows are not
  realized while collapsed and are present to assistive technology once expanded (real-window test).
- Notes rows and History rows have composed accessible names (importance, category, schedule; weekday,
  finalized time, origin, PDF availability). The PDF tag has a tooltip with the full wording.
- Status is always words (show states, Important, PDF / No PDF, Stale); color only reinforces.
- Tab order unchanged on all three screens; Open Final PDF/Save PDF Copy keep their names and help text.
- All colors are Phase 22A tokens; Light and Dark captured. **Narrator not validated** (no Windows host).

## 17. Screenshot results

Isolated Skia harness (session scratchpad, not committed): a console app compiled against the Desktop
project and the Ui.Tests fakes, `UseSkia().UseHeadless(UseHeadlessDrawing=false)` +
`CaptureRenderedFrame()`, no desktop lifetime, no database, synthetic data only (busy Saturday with six
shows / 22 performances incl. a long wrapping name, a no-venue show and a 12:30 AM performance; a
two-show Sunday; NoShows, CouldNotCheck, NotPublished, last-known-good-after-failure, past-without-record
and not-checked dates; eight notes incl. two Important; nine finalized records incl. a manual one, one
without a PDF and one with no operational notes; exact, From and overridden prices). A second copy
compiled against `7e7ca9b` (`git archive`, no repository change) gave the "before" numbers. The real
profile (`~/Library/Application Support/Carowinds Passdown`) listing was identical before and after
every run.

104 after-PNGs (Light/Dark × 1280×860, 960×640, 1280×690, 1920×1000): Calendar busy collapsed, expanded,
expanded-scrolled, date switched while expanded, and the six status dates (at 1280×860 and 960×640);
Notes with records; Notes empty; History with records; History record without notes; History empty;
Associate Products. Reviewed: collapsed/expanded Shows, date switch (new date's two shows, still
expanded), error wording in the error color, wrapping long show names and time lists inside the panel at
960 px, Notes rows with the Important rule in both themes, History two-line rows with the amber "No PDF",
History empty state, Products "From $40.00" and "Use official (From $40.00)".

## 18. Density measurements

Measured from rendered Skia layouts (light theme; dark identical geometry):

| Measure | Before (`7e7ca9b`) | After |
|---|---|---|
| Busy-day Shows section (heading → next group) | 360 px | **86 px** collapsed · 416 px expanded |
| Busy-day selected-date panel content | 978 px | **619 px** collapsed (−359 px, −37%) · 949 px expanded |
| Quiet-day panel content (NoShows / NotPublished) | 692 px | 593 px |
| Page scroll, busy day, 1280×860 | 336 px | **73 px** (the month grid is now the tallest element) |
| Page scroll, busy day, 960×640 | 556 px | 293 px |
| Page scroll, busy day, 1920×1000 | 196 px | 0 px |
| Notes row height (avg) | 109 px | **66 px** |
| Notes rows fully visible, 1280×860 / 960×640 | 5 / 3 | 8 / 6 |
| History row height | 69 px | **46 px** |
| History rows fully visible, 960×640 / 1280×690 | 6 / 7 | 9 / 9 (all) |

Narrow windows: at 960×640 every Calendar/Notes/History/Products control stays inside the window (real-
window tests at 960×640, 1280×690, 1280×860, 1920×1000 in both themes); show names and time lists wrap
inside the 320 px panel; Prev/Next Day wrap if needed. Fewer pixels was not the goal on its own — no
hit target was shrunk and every fact is still visible (or one click away in the schedule).

## 19. Tests added

| Class | Partition | Count | Covers |
|---|---|---|---|
| `AdvertisedPriceTextTests` (Domain) | normal | 15 | From only for StartingFrom, every other kind plain, missing → null, host cultures |
| `Phase22ECalendarShowsTests` (Ui) | mixed | 40 | 6×4 = 24, duplicate times, duplicate records, distinct ids, no-time shows, singular/plural, late-night order, row order, expanded times/venue/accessible text, no-venue, today lists all, every state's wording + no count + no disclosure, never "0", freshness, last-known-good + failure, past with/without record; VM: collapsed by default, toggle never loads or changes date, busy summary, date switch while expanded (gated: no previous-date carryover), date without shows keeps preference, month navigation keeps preference, local refresh recomputes, failed refresh keeps LKG, load error hides Shows, new session collapsed; markup: shared disclosure, bindings, divided wrapping rows, every existing detail/route kept, no show times in day cells |
| `Phase22ENotesHistoryTests` (Ui) | mixed | 26 | note schedule line (range, expires today/tomorrow, until resolved, upcoming, expired, resolved), Important from priority only, created/updated, US format under de-DE, edit shows Updated and keeps Created, Important empty wording, other empty states unchanged, filter result sets unchanged, View Notes keeps filters/date, Open Notes → today, notes markup (all facts, no chips, all filters/editor/actions); History row title/detail/year boundary/No PDF/accessible text, select + open serves the stored bytes (same array), read-only seams, list/detail markup incl. empty-history prompt |
| `Phase22EProductPriceLabelTests` (Ui) | mixed | 27 | exact, From, qualifier folding, missing (4 kinds), retired display string, non-From kinds never prefixed, stale From/exact, Military/First Responder dated exact and minimum fallback (3 names each), historical, override print note/storage, edit/clear note, PDF + Teams parity, real `AssociateViewModel` with no online call, markup |
| `Phase15ERealWindowLayoutTests` (Ui) | run-alone | +10 | Calendar disclosure by keyboard (Space/Enter), ExpandCollapse state, rows realized only when expanded, no reload; collapsed ≤ 110 px and expanded > 3× at five size/theme combinations with every show line inside the panel and no control clipped; Notes rows three blocks + no extra padding, inside the list; History rows two lines, Open Final PDF reachable |

Intentional updates: `Phase21CShowsMarkupTests` (the Calendar binds `CalendarShows`, the Dashboard still
binds the shared `ShowsPresentation`); `MainViewModelNavigationTests.DashboardOpenNotes_OpensTodaysEasternDate_LikeOpenPassdown`
(was `…NeverChangesAssociateSelectedDate`) and `Phase21ADashboardOpenPassdownTests.AssociateOwnDateSelection_StaysStableAcrossOrdinaryNavigation`
(no longer treats Dashboard Open Notes as ordinary navigation) — §11.

Deterministic total: **3836/3836** (baseline 3718) = 221 Domain + 1059 Application + 833 Infrastructure +
451 Documents + 1176 Ui together + 26 `OperationsCalendarViewTests` + 57 `Phase15ERealWindowLayoutTests` +
2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests`
alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes; no new
run-alone class). Release build 0 warnings, 0 errors.

## 20. Known limitations

- No Windows run, DPI scaling or Narrator check (§21); Mac Skia captures are not DPI validation.
- The expansion preference lives in the Calendar view model for the app session (collapsed on next
  launch) — by design, no persistence.
- While a newly selected date loads (normally a few milliseconds of local SQLite), the rest of the
  selected-date panel still shows the previous snapshot as before; only the Shows group is withheld.
- Notes timestamps use the screen's existing `ToLocalTime()` formatter (Eastern on the target PCs; a PC
  set to another time zone would show local time, as Resolved already did).
- The Calendar's Daily Ticket keeps "Starting from $x" (Phase 13D wording) rather than "From $x".
- Headless unit tests use fake font metrics; pixel densities come from the Skia harness only.
- **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.**
  Neither is affected or cleared by this phase.

## 21. Windows validation checklist

All **NOT EXECUTED** (no Windows host). Run on the RC.7 package.

| Check | Status |
|---|---|
| Calendar month navigation (Prev/Today/Next), day selection, Prev/Next Day | NOT EXECUTED |
| Selected-date panel: hours, closed, not published, unavailable, stale note, Carolina Harbor, Daily Ticket | NOT EXECUTED |
| Busy date: Shows collapsed by default with the correct "N scheduled shows · M performances" | NOT EXECUTED |
| View schedule: every time and venue, long names wrap, late-night times last | NOT EXECUTED |
| Change dates while expanded: stays expanded, new date's shows only, no flash of the old list | NOT EXECUTED |
| Official schedule freshness; "latest check failed" after a failed check | NOT EXECUTED |
| No shows / not published / closed / couldn't check / past date wording; no empty View schedule | NOT EXECUTED |
| Historical dates: no invented hours, shows or prices; backfill status line readable | NOT EXECUTED |
| Open Passdown / View Date Pricing open the selected date; Dashboard Open Passdown opens today | NOT EXECUTED |
| Notes list scanning, Important rule, schedule line, created/updated in detail | NOT EXECUTED |
| Notes filters, Clear filters, empty states (incl. Important filter) | NOT EXECUTED |
| Notes create / edit / resolve / cancel; unsaved-editor guard; Personal Sync round trip | NOT EXECUTED |
| Dashboard Open Notes opens today's passdown notes; View Notes keeps the manager's filters | NOT EXECUTED |
| History browsing, weekday titles, PDF / No PDF tag, empty History | NOT EXECUTED |
| History Open Final PDF / Save PDF Copy on a record finalized before RC.7 — opens unchanged | NOT EXECUTED |
| Associate Products: "From $x" for starting prices, exact prices plain, "Prints as From $x" under an override | NOT EXECUTED |
| PDFs (Classic/Compact/Modern) and Teams Copy print the same price text as Products | NOT EXECUTED |
| Light and Dark themes, live theme switch on Calendar/Notes/History | NOT EXECUTED |
| 100% / 125% / 150% scaling (incl. 960×640) — no clipping, no horizontal scroll | NOT EXECUTED |
| Keyboard-only: Tab to View schedule, Enter/Space, visible focus | NOT EXECUTED |
| Narrator: Show schedule expanded/collapsed, row announcements, Notes/History row names | NOT EXECUTED |
| No unexpected network requests when expanding Shows or browsing History | NOT EXECUTED |
| No data loss: drafts, notes, History intact after upgrade from RC.6 | NOT EXECUTED |
