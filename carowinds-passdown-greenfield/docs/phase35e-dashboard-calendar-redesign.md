# Phase 35E — Dashboard and Operations Calendar redesign

Status: **Calendar implemented locally (35E-1/35E-2) and Dashboard implemented locally as a separate follow-up
(35E-3/35E-4, §14–§24). Both await Windows review. Phase 35E is not complete until the product owner confirms both on
Windows.** Not pushed, tagged or published. The Calendar part below (§1–§13) is the original record, left as written (its
"Dashboard not started / reference missing" notes describe the state at that time); the Dashboard is recorded on its own
in §14 onward.

### Calendar part (35E-1 / 35E-2)

| | |
|---|---|
| Starting commit | `62d2aca` (Phase 35D-5). 35A–35D were Windows-confirmed by the product owner; the 35D compact date-selector follow-up (`01d3553`) **is present** in the tree and is still awaiting its own Windows review |
| Implementation commit | 35E-1 (Calendar source and tests) |
| Record commit | 35E-2 (this document, the Windows checklist and the plan update) |
| Release status | RC.13 published and frozen; source `<Version>` still `0.1.0-rc.13`; binaries ship as rc.14 |
| Database schema | 18 (unchanged; no migration) |
| Personal Sync format | unchanged (SyncSchemaVersion 1, draft payload 4, finalized snapshot 2) |
| Drafts / autosave / finalized History / PDF | unchanged |
| Local test version | `0.1.0-rc.14-LOCALTEST.phase35e` (display only; source version unchanged) |

## 1. References

From `~/Downloads/carowinds-phase35-approved-design-references/`:

| Screen | Files | Used |
|---|---|---|
| Operations Calendar | `carowinds_calendar_refined_prototype.html`, `carowinds_calendar_preview.png`, `carowinds_calendar_deadline_preview.png`, `carowinds_calendar_monthly_perks_preview.png`, `carowinds_calendar_compact_preview.png` (also attached to the request) | yes |
| Dashboard | **none** | — |

**Missing: the approved Dashboard reference.** The bundle's own `carowinds-phase35-design-bundle-README.md` states that
"the latest approved Dashboard and Passdown Overview HTML prototypes/screenshots are not present", and that earlier
concept images and the generic `~/Downloads/carowinds_operations_prototype.html` must not be substituted. Nothing else
was found in the Downloads folder. Per the brief, **no Dashboard visual work was done** (DashboardView and its view model
are untouched). The Dashboard half of 35E starts once the approved Dashboard HTML/PNG is supplied.

Prototype content (Nov 2026 hours, "$50.00", "4 shows · 22 performances", the sample perks, "Official data checked Nov 22
· 7:44 AM ET", the toast messages) was **not** copied. Every value on the Calendar still comes from the existing
`OperationsCalendarSnapshot`, `PassPerksOverview` and presentation types.

## 2. Existing → approved mapping (Calendar)

| Prototype element | Production | Note |
|---|---|---|
| Toolbar "Operations Calendar" + subtitle | Same title (17 px, the 35B workspace title size) and subtitle "Park hours, Passdowns, and official Pass Perks dates" | Replaces the old "OPERATIONS CALENDAR" eyebrow + month as page title |
| Toolbar "Official data checked … · Check Online" | **Not added.** The right side carries the existing Phase 20C history-backfill line when it has text | Deviation: the Calendar owns no refresh, and a Check Online here would be a second, page-specific refresh. Official freshness is shown per date in the panel ("Verified · Last checked …"), which is the information the app actually has |
| "November 2026" + "Select an operating date …" + ‹ Previous · Today · Next › | `DisplayedMonthText` (19 px) + the line; the existing `PreviousMonth` / `GoToToday` / `NextMonth` commands and accessible names | The heading stays in place; only the month column scrolls (§3) |
| Month card, weekday band, flush hairline cells | One white card; weekday band on the secondary surface; cells share right/bottom hairlines, no gaps, no per-cell rounding | Week rows sit 1 px under the card edge so the last row/column hairline never doubles the border |
| Day number; today's number in a tinted tile | Same; the tile shows for today whether or not today is also selected | Prototype hides it when today is selected; kept so today stays identifiable |
| Selected cell: tint + 2 px accent frame | Same (padding shrinks by the extra border, so content never moves) | |
| Hours line; "CLOSED" | Existing compact state label / hours (wraps after the en dash, two lines max) / Carolina Harbor / Stale lines | Unchanged sources and rules |
| "Draft in progress" / "Finalized" | New `PassdownCellText`: shown only for a draft or a finalized date | The old "No Draft" line on every empty date is gone; the cell's accessible name still says "No draft" |
| "1 active note" | Existing notes summary ("2 Notes · 1 Important") | |
| One coloured perk icon (+ count) per date | The Phase 32A typed slots — check (qualifying visit), gift (redemption / use), clock (booking deadline), fixed order, count badge inside the slot — now each in its own colour | Shape **and** colour **and** words (legend, tooltip, name) |
| Legend + "Show Perks" | Same legend; the check box is the existing Settings → Calendar preference (`CalendarDisplaySettingsViewModel.ShowPassPerks`, one shared instance, saved to `calendar-display.json`) | Shown only when Pass Perks and the preference exist |
| "Markers indicate confirmed, date-specific public offers—not rewards earned by an individual guest." | "Markers show confirmed, date-specific public offers, not rewards earned by an individual guest." | Shown with the legend |
| Panel: SELECTED DATE / date / status | Eyebrow, `SelectedDateHeadingText` (changes the instant a date is chosen), Today chip, and the existing ‹ › Previous day / Next day as two compact icon buttons | Status moved into "Park information" so no previous-date value can appear under a new heading (Phase 22F gate) |
| Park information: Park hours, Daily ticket, Shows | Labelled rows: Park status, Park hours, verification / stale note, Carolina Harbor (when published), Daily ticket, Shows (summary, state wording, View schedule disclosure, freshness) | Existing presentation text only |
| Pass Perks: coloured left rule, title, why, details, View details; "Valid all November (4) ▾" | Same: title, marker icon + why, meta, notices (incl. inferred-year / discrepancy notices), View details; month-long perks in the existing collapsed disclosure | Dated entries get a 3 px rule in their marker colour; month-long rows are plain |
| Passdown and notes | Passdown status + last saved / finalized note; Operational notes count + Important | |
| Actions: Open Passdown · View Pricing · Operational Notes · View Finalized History | Same four, pinned to the bottom of the panel; existing commands/events | History stays disabled (not hidden) until the date is finalized, with help text saying why; it opens the stored record read-only |

## 3. Calendar behaviour

- **Workspace.** The month heading and navigation stay fixed; the month column scrolls on its own when the window is
  short; the selected-date panel (290 px) fills the available height, its body scrolls and its actions are always in
  view. At 1920×1080 nothing scrolls.
- **Selection** is immediate: the heading and the selected frame change on click/Enter; details follow when the local
  read completes, with the existing "Loading details…" placeholder in between. No animation, no debounce was added.
- **Keyboard.** Every cell is a Tab stop; Enter/Space select; ‹ › step the day; the disclosures keep ExpandCollapse.
- **Routing** is unchanged: Open Passdown and View Pricing raise the existing events with the Calendar's selected date
  (an adjacent-month cell first moves the Calendar to that month); MainViewModel still sets the Passdown date. The
  Calendar never changes the Passdown workspace date by itself.
- **No retrieval.** Month, date, Show Perks and disclosure changes are local reads or re-formatting only (tested).
- **Width.** One fixed panel width instead of a ContainerQuery: a query was measured at ~15 ms extra per month/date
  change (every day cell is re-templated on each change), so it was removed (§8).

## 4. Pass Perks marker rules (preserved)

Phase 32 / 32A semantics are unchanged: markers only on confirmed date-specific entries (qualifying visit, redemption /
use, booking deadline), one slot per kind in a fixed order, a count when several perks share a kind, month-long perks as
one "Valid all {month} (n)" summary instead of daily markers, inferred-year and source-discrepancy notices, inclusive
ranges, last-known-good cache, historical records. Storage, schema and sync untouched; no guest wallet. 35E adds only
colour per kind (tokens `PassdownPerkQualifyingBrush` / `RedemptionBrush` / `DeadlineBrush`, Light `#1E5AA8` /
`#1F7A7D` / `#8F5F14`, Dark `#8DB9EB` / `#6CC4C6` / `#DDB065`; each ≥ 4.5:1 on its surface) and the in-page Show Perks box.

## 5. Timezone

Unchanged: the Calendar's today is the `America/New_York` operating date (`EasternTimeZone`), never UTC or the
computer's zone. New tests pin the night before daylight saving ends and starts, the fall-back hour and New Year's Eve.

## 6. Accessibility and responsiveness

- Visible labels are the accessible names (Open History → "View Finalized History", View Date Pricing → "View Pricing";
  "Open Operational Notes" contains its label "Operational Notes"). Previous/Next day icon buttons have names and tooltips.
- Every marker kind is shape + colour + text; the cell name lists every status.
- Renders at 1920×1080, 1536×960, 1280×720 and 960×640, Light and Dark, sidebar expanded and collapsed: no horizontal
  overflow and no control past the window edge (136 captures, `tools/visual-fixtures`, scenarios `Calendar*`,
  `Shell-*-Calendar`, `LiveSwitch-Calendar`, `Settings-CalendarDisplay`).
- 125 % / 150 % Windows scaling and large text are Windows checklist items (Mac renders are not DPI evidence).

## 7. Preserved functionality

No change to: schema 18, Personal Sync, drafts/autosave, finalized History immutability, official prices and overrides,
products, Shows selection, Notes, ERT, refresh policies, Accesso coalescing, Pass Perks computation, QuestPDF, PDFium
preview, printing, release/update. Dashboard, Passdown, Products, Shows, Notes, the PDF companion and the date selector
were not touched.

Files: `Views/OperationsCalendarView.axaml` (rewritten layout, same bindings/commands), `OperationsCalendarDayViewModel`
(+`PassdownCellText`), `OperationsCalendarViewModel` (+`CalendarDisplay`, `CanTogglePassPerks`), `Themes/PassdownTokens.axaml`
(+3 perk brushes per theme).

## 8. Performance (Mac, headless Skia — development evidence only)

Scratch harness (not committed): the real `OperationsCalendarView` in a 1280×860 headless window with the synthetic
`tools/visual-fixtures` month and perks; each step = command → local load → layout → rendered frame; three runs each,
HEAD `62d2aca` vs 35E. Medians:

| | Before (HEAD) | After (35E) |
|---|---|---|
| First render (VM + window + first frame) | 441–471 ms | 471–505 ms |
| Month navigation | 63–94 ms | 67–81 ms |
| Selected-date change | 46–48 ms | 54 ms |
| Show Perks toggle | 35–46 ms | 48–50 ms |
| 200 month navigations, managed heap after GC | 33.9 → 33.9 MB | 43.6 → 43.6 MB |

Month navigation is within noise; a date change costs ≈ +7 ms (the new view realizes a few more elements per cell, and
the existing view model still re-creates all 42 cells on every change — unchanged behaviour). The ContainerQuery
version cost ≈ +20 ms and was dropped. No memory growth over 200 navigations in either build; the ~10 MB higher
steady-state heap is constant. No SQLite read, HTTP request or UI-thread wait was added. Windows responsiveness must be
checked on Windows (checklist).

## 9. Tests

New (42): `Phase35ECalendarTests` (33, mixed) — day-cell passdown line and unchanged labels; 4/5/6-row months; Eastern
today across DST and New Year's Eve; month navigation across the year boundary with one local read per action; adjacent-
month selection and routing with that date; History only for a finalized date; Show schedule never reads; markup
contracts (marker classes, Show Perks binding, panel scroll, fixed width, footnote, labels, no Check Online/Refresh, no
"No Draft" binding, no emoji); perk tokens in Light and Dark. `Phase32PassPerksUiTests` partial (5) — the Show Perks box
is the shared setting, hides/restores markers and saves locally without any read; offered only with perks and the
preference; each selected-date entry keeps its marker kind and month-long rows have none. `OperationsCalendarViewTests`
partial (4, run-alone) — at 1920×1080 Light, 1280×720 Dark and 960×640 Light on a six-row month: the
panel stays inside the window and ends with the month column, all four actions inside it, flush cells, heading never
scrolls away; a day chosen with the keyboard updates the heading at once.

Updated: `OperationsCalendarViewTests` critical names ("View Finalized History", "View Pricing").

**Results:** deterministic suite **5,803 / 5,803**, 0 skipped (Domain 462, Application 1,378, Documents 484,
Infrastructure 1,319, Ui mixed 1,929, OperationsCalendarView 30, Phase15ERealWindowLayout 134, RealWindowStartupTiming
2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 54) — baseline 5,761 re-run on
`62d2aca` first. Clean Release build (after `dotnet clean`): 0 warnings, 0 errors. Final layout run three times in full
(5,803 each time on two consecutive runs after the one below).

**Intermittent hang (recorded, not hidden).** One run of the mixed Ui partition stopped making progress (test host idle at
~0 % CPU, no memory growth) and was stopped after several minutes. The same partition then passed in 7 s with
`--blame-hang` (no hang to attribute) and in two further complete partition runs. This matches the documented,
pre-existing headless thread-affinity race of this assembly (`HeadlessTestSetup`, Phase 15E); nothing in 35E adds a
dispatcher wait to the mixed partition. If it recurs on the same tests, it should be investigated with a hang dump.

## 10. Windows test package

| | |
|---|---|
| Source commit | `5ea4d0f` (35E-1; `src`, `packaging` and `Directory.Build.props` clean) |
| Command | `Build-PortableRelease.ps1 -LocalTestLabel RC14-Phase35E-LOCALTEST -LocalTestVersion 0.1.0-rc.14-LOCALTEST.phase35e` |
| ZIP | `artifacts/local-test/RC14-Phase35E-LOCALTEST/LOCAL-ONLY-RC14-Phase35E-LOCALTEST-win-x64.zip` |
| Size | 76,122,198 bytes (72.6 MB); 308 files |
| SHA-256 | `b2e7f6fc074cb958865d7ea1230a7d8a6b58caabc92b10d9ad85319d15186aca` |
| Shown as | `0.1.0-rc.14-LOCALTEST.phase35e` |
| Validation | script content validation (folder and ZIP, 308 files) passed; `RealReleaseZip` passed; marker file names `5ea4d0f`; no database, draft, history, key or update manifest in the ZIP; all 10,154 earlier release and local-test files re-hashed byte-identical |

## 11. Known limitations / differences from the reference

- Dashboard not redesigned (reference missing).
- No toolbar "Official data checked · Check Online" on the Calendar (§2).
- Today's tile stays when today is also selected.
- Panel is 290 px at every width (prototype 305, 275 when narrow).
- Cells are a fixed 100 px tall (Phase 13D equal-geometry rule), so on very tall windows the month does not stretch to
  the bottom as the prototype's flexible rows do; the panel still fills the height.
- Mac renders and timings are not Windows evidence.

## 12. Windows validation status

**NOT EXECUTED.** Checklist: `docs/releases/rc14-phase35e-localtest-windows-checklist.md` (disposable `CarowindsTest`
account, synthetic data).

## 13. Phase 35F dependencies

35F (Operational Notes, History, Settings) needs: Windows sign-off of the 35D follow-up and of this Calendar; the
approved **Dashboard** reference to finish 35E first; the History / Operational Notes / Settings references (present in
the bundle). It can reuse the 35E toolbar, labelled-row and pinned-actions panel styles.


---

# Dashboard follow-up (35E-3 / 35E-4)

The Dashboard half of 35E was deferred in 35E-1/35E-2 because its approved reference was missing. The owner recovered
the reference on 2026-11-26; this is that follow-up, implemented separately from (and after) the Calendar and 35F.

## 14. Summary

| | |
|---|---|
| Branch | `feature/passdown-redesign` |
| Starting commit | `256a1c9` (Phase 35F-2). Present and verified: 35F (`8967fcc`, `256a1c9`), the 35E Calendar (`5ea4d0f`, `8de9453`), the 35D compact date selector (`01d3553`, `62d2aca`) |
| Implementation commit | `2ec6261` — Phase 35E-3 (source, tests, visual fixtures) |
| Record commit | 35E-4 (this section, the Windows checklist and the plan update) |
| Reviewed source baseline | `2ec6261`; `src`, `tests`, `packaging` and `Directory.Build.props` clean at build time. Unrelated uncommitted work in the tree (an in-progress documentation reorganization of `AGENT_BUILD_SPEC.md`, `CLAUDE.md`, `docs/architecture.md`, untracked `docs/architecture/` and `.claude/rules/`, `.claude/skills` edits, `.DS_Store` files) was left untouched and is not part of these commits |
| Release status | RC.13 published and frozen; source `<Version>` still `0.1.0-rc.13`; nothing pushed, tagged, signed or published |
| Database schema / Personal Sync | 18, unchanged / unchanged (SyncSchemaVersion 1, draft payload 4, finalized snapshot 2) |
| Drafts, autosave, finalized History, PDF pipeline, retrieval, refresh policy | unchanged |
| Local test version | `0.1.0-rc.14-LOCALTEST.phase35e-dashboard` (display only) |

## 15. Approved references

| File | Notes |
|---|---|
| `~/Downloads/carowinds_dashboard_phase35_reference.html` (71,158 bytes, SHA-256 `09bb20f5418830308d33a0f68e86ba0dbd58189ea07d7ba4c6042826e6a8ae2c`) | The interaction and layout reference: header, 4-column gate card, 1.88 : 1 columns, card title bands with 3 px top rules, breakpoints 1200 / 850 / 550 px |
| The Dashboard PNG attached to the 35E-follow-up request (1536 × 1236 px; not saved in the design folder, so it has no file name on disk) | The same design at a narrower width: 2 × 2 gate card, three price rows, simpler card contents |

The HTML and the PNG differ in detail (the PNG omits the header's Check Online, the gate support lines and the metric
helpers). Where they differ the HTML was followed and the PNG treated as its compact rendering. Neither file's
JavaScript, toast messages or sample values (Nov 22 hours, $50/$65/$45, "6,000", "300", "4 lines entered", the show
list, "Carowinds B SyncTest", "Verified 7:44 AM ET") was copied.

## 16. Design → Avalonia mapping

| Reference | Production (all values from the existing snapshot) |
|---|---|
| Header: date, "Admissions Overview", "Official data verified at … · Check Online" | `OperatingDayLabel` ("Sunday, November 22, 2026"), page title, the shared Phase 19A official-data line (`OfficialDataStatus.Presentation`), the existing **Check Online** command with its busy bar and result line. **No status dot** |
| Gate card: PARK OPERATING HOURS · AVENUE ENTRANCE · NORTH GATE · EARLY RIDE TIME | `ParkHoursHeadlineText` + "Published park schedule" / "Manual hours for today" / "Private buyout"; `ParkHours.AvenueText`/`NorthGateText` (the effective values, override included) + "45 / 30 min before opening" or "Manual time for today"; "Not available — Needs published park hours" without hours; `EarlyRideTimeText` (the ERT resolver's value) |
| Gate footer: verification · Operating date | `ParkHours.VerificationText` and stale note; "Operating date: Nov 22, 2026" + **View in Calendar** |
| Today's Associate Passdown: "Last saved locally …", status, "Autosaves on edit", three metrics, notes line, Open Passdown / Review Notes | Title band with "Last saved locally 8:16 AM ET" (draft only); `PassdownStatusText` (No Draft / Draft In Progress / Finalized — neutral text, never a warning); "Autosaves on this computer as you edit" or "Finalized record is read-only in History"; Projected attendance / Pre-solds ("Not entered" when blank) / Daily feature (type + text); "Passdown notes for print: n note lines entered"; **Latest finalized** date + **Open History** (moved from the old Latest Finalized panel); **Open Passdown** (primary) / **Review Notes** |
| Shows & Entertainment: "4 scheduled shows · 22 performances", Verified …, rows with +N more | `ShowsSummaryText` (counted from the recorded official schedule, only when Scheduled); the existing freshness text; the Phase 23B rows (upcoming times, "+N more" in place, "+N earlier") with the venue in a right-hand column; state lines; **View Shows** |
| Daily Ticket Pricing: rows + View Pricing | New `DashboardPricingSelector`: today's official **exact dated** ticket and Fast Lane prices (never a display override, never a "From" price, never another date or a historical workspace), Daily Admission first, max 5 + "n more in Products"; "Verified …" (oldest row) or "Some prices could not be rechecked — may be out of date"; with no dated rows, the Phase 22C admission reference ("From $…", "Price not available — Dated prices have not been checked yet" …) |
| Operational Notes (1 active) · View Notes | Existing relevant-today notes: summary, up to 3 titles with "Important ·" in words, date range and category, overflow line, **View Notes** |
| November Pass Perks · Details | New: the cached Pass Perks (`IPassPerksService`, local read only) for the operating month, up to 2 current/upcoming perks with separate labelled lines (Last qualifying visit · Book by · Redemption period — "Today, November 22" in words), status labels and source-discrepancy notices from Phase 32, "n more in Products", the official-page status line and "Publicly advertised perks — not what an individual pass has earned."; **Details** opens Products (the Pass Perks panel) |
| — | **Official Data** card (not in the reference): overall headline, attention line, last verified, the collapsed **Source details** (per-source rows, Deep Refresh). Kept so no existing function is lost |
| Sidebar, title bar | Unchanged Phase 35A sidebar, original icon and navy native title bar |

Visuals: 5 px cards, hairline borders, a 3 px top rule (blue gate/passdown/pricing, teal shows, slate notes, amber perks
— new tokens `PassdownCardRule{Blue,Teal,Amber,Slate}Brush`, Light from the reference, Dark lightened; decorative only),
title bands on the secondary surface, 11 px uppercase gate labels, 23 / 18 px values. No photo banner, no KPI tiles,
no role label, no duplicate app name.

## 17. Data, commands and navigation

- **Application:** `OperationsDashboardSnapshot` gains `Pricing` (`DashboardPricingSummary`) and `PassPerks` /
  `PassPerksLoadFailed`. `OperationsDashboardService` takes an optional `IPassPerksService` (already registered; DI
  supplies it — covered by `CompositionRootTests`) and reads it once per snapshot; a perks overview for another date is
  never shown; a read failure degrades only that card. No new repository, request or persistence.
- **Desktop:** new `OpenShowsCommand`, `OpenPassPerksCommand`, `OpenCalendarCommand` on `OperationsDashboardViewModel`
  (enabled once today's summary has loaded) raising events that `MainViewModel` routes: Shows → today's Eastern
  Passdown, Shows tab; Details → today's Passdown, Products; View in Calendar → `Calendar.SelectDateCommand` with the
  Dashboard's own operating date (one local Calendar read; the Passdown date is not touched). Existing Open Passdown /
  Review Notes (Notes tab) / View Pricing / View Notes / Open History / Check Online / Run Deep Refresh are unchanged.
- **No retrieval:** opening, reloading, resizing, expanding Source details or show times and every navigation action
  are local; only Check Online and Run Deep Refresh go online, as before (tested).
- **Operating date:** unchanged `OperationsDashboardService.TodayEastern()` (America/New_York). New tests pin the night
  daylight saving ends (both 1:30 AM hours), the night it starts, New Year's Eve 11:59 PM and midnight, and a UTC/computer
  date already one day ahead.
- **Layout:** `DashboardView.axaml.cs` (layout only) picks Wide (≥ 1000 px content: gate in one row, 1.88 : 1 columns,
  right column ≥ 330), Medium (≥ 680: gate 2 × 2, 1.4 : 1, ≥ 290) or Narrow (stacked). Breakpoints are on content
  width rather than window width because the sidebar width varies. No ContainerQuery (the Dashboard is not re-templated
  per date, but code-behind avoids the query cost measured on the Calendar).

## 18. Accessibility

Every card is a named landmark (Gate operations, Park hours, Avenue Entrance, North Gate, Early Ride Time, Passdown
Status, Passdown Notes, Latest finalized passdown, Today's shows, Daily admission, Active operational notes, Pass Perks,
Data status); every button's accessible name contains its visible label (tested for every button in the view — "Try
again" was renamed from "Try loading today's operations summary again" for this); help text explains each destination;
the Pass Perks card's name carries the public-listing note; prices, notes and perks have composed names including
"may be out of date", "Important" and labelled dates. Status is always in words (no dots, no colour-only meaning). Tab
order follows reading order (Check Online → View in Calendar → Open History → Open Passdown → Review Notes → show
toggles → View Shows → View Pricing → View Notes → Details → Source details; real-window test). Focus rings are the
shared Fluent ones. Windows Narrator, high contrast and large text are Windows checklist items.

## 19. Motion and performance

No new animation, timer or polling; the existing one-minute shows tick is unchanged. Scratch harness (not committed):
the real `MainWindow` + `MainViewModel` over the synthetic busy day (with price list and Pass Perks in the new build),
headless Skia, 1280 × 860, each step = action → local load → layout → rendered frame; 12 iterations × 3 runs, medians
of the warm iterations; HEAD `256a1c9` vs `2ec6261`. **Mac, headless — development evidence only.**

| | Before | After |
|---|---|---|
| Initial display (shell + Dashboard, first frame) | 114–139 ms | 145–163 ms |
| Return navigation to the Dashboard | 2.5–2.6 ms | 2.7–3.1 ms |
| Local reload from cached state, Dashboard visible (+frame) | 11–12 ms | 25–29 ms |
| Local reload while the Dashboard is hidden (the autosave case) | — | ≈ 4 ms |
| Presentation build alone | — | 0.1 ms |
| Dashboard → Passdown (first frame) | 42–50 ms | 37–47 ms |
| Dashboard → Calendar (first frame) | 79–91 ms | 69–76 ms |
| 200 History ↔ Dashboard round trips | 3.2–3.6 ms each; heap 128 → 128–131 MB | 3.6–3.7 ms each; heap 137–138 MB flat |

The higher first display (+≈ 20 ms) and visible-reload cost (+≈ 15 ms) come from realizing more content (seven cards,
price rows, perks); the presentation work itself is negligible and a hidden Dashboard reloads in ≈ 4 ms, so Passdown
autosaves are not slowed. No SQL query beyond the existing snapshot read plus one cached Pass Perks document read, no
HTTP, no UI-thread wait. No memory growth across 200 navigations (the ≈ 10 MB higher steady heap is constant). First
PDF preview after Open Passdown was not re-measured: the preview pipeline and its Phase 34B warm-up are untouched.
Windows numbers must come from the checklist.

## 20. Tests

Deterministic suite **5,826 → 5,885** (+59), 0 failed, 0 skipped — baseline re-run on `256a1c9` first (5,826).
Application 1,378 → 1,396; Ui mixed 1,949 → 1,983; Phase15ERealWindowLayout 137 → 144; others unchanged (Domain 462,
Documents 484, Infrastructure 1,319, OperationsCalendarView 30, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle
7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 54). Clean Release build: 0 warnings, 0 errors.

New: `Phase35EDashboardProjectionTests` (18, Application — price policy: exact dated only, today only, no override /
custom / "From" / other section / historical, order, duplicates, bound and true count, stale kept; perks wiring, one
local read, no retrieval, other-date guard, read failure; Eastern date at DST, New Year and UTC-ahead instants);
`Phase35EDashboardTests` (28 — gates incl. manual overrides and no-hours states, ERT, passdown states and "saved locally",
never zero for blank figures, shows counts, price presentation and fallback, routing for View in Calendar / View Shows /
Details with an Eastern date behind UTC, commands disabled until loaded, no Check Online from any action, layout
breakpoints, markup: card structure, tokens only, no fabricated telemetry or dots, button names, public-listing note,
neutral draft); `Phase32PassPerksUiTests` partial (5 — labelled dates, booking/month-long/upcoming, bound, expired left
to Products, not-checked / read-failure / failed-check wording); `Phase15ERealWindowLayoutTests` partial (7 — five sizes
Light/Dark incl. a stacked width: no clipping, no card overlap, no horizontal scroll; resize reflow without any read;
Tab order); `CompositionRootTests` (1 — the Pass Perks reader is wired into the Dashboard service).

Updated (superseded Operations Overview assertions replaced by equivalent contracts for the approved layout, none
removed): `DashboardViewTests` critical names; `Phase22CDashboardOverviewTests` reading order and title;
`Phase21CShowsMarkupTests` Shows title; `Phase23BDashboardShowsTests` row structure (venue column beside name/times, still
no unconstrained horizontal panel), one-column list instead of `ShowColumnsPanel`, style-token range;
`Phase15ERealWindowLayoutTests` gate arrangement (4 in a row / 2 × 2) and columns, venue beside the times, shows in one
list in schedule order.

## 21. Visual comparison and intentional differences

Renders of the real view (not of the HTML) with `tools/visual-fixtures`, scenarios `Dashboard35E-*` (typical, scrolled,
collapsed sidebar, nothing yet / no draft, stale + failed checks, long text + finalized, keyboard focus, source details)
at 1920 × 1080, 1536 × 960, 1280 × 720 and 960 × 640, Light and Dark (64 captures, no horizontal overflow, no button past
the window edge, no overlaps reported). Differences from the reference, all deliberate:

1. Header keeps the shared official-data wording ("Official data checked …"), no status dot; Check Online stays (HTML).
2. Gate support lines say how each time was set (calculated minutes or manual), not "ERT access" / "General admission" —
   the app does not record which guests use which entrance. Unknown hours show "Not available".
3. Gate footer shows the actual verification line, plus View in Calendar.
4. Passdown: no dot; existing title-case status; metric helper lines ("Supervisor projection", "Advance online
   purchases", "Mathematical support active") omitted — not recorded data; "Passdown notes for print" instead of
   "Operational Notes configured for print" (Phase 11C: the draft's notes and persistent Operational Notes never share a
   name); Latest finalized line added.
5. Shows lists only upcoming performances (Phase 21C) and adds View Shows; the freshness wording is the existing one.
6. Pricing lists only exact dated official prices for today (the reference's five rows are illustrative); a freshness
   line is added.
7. Operational Notes rows show the existing date range and category instead of "Active since … · Persistent".
8. Pass Perks adds a status line and the public-listing note; "Details" opens Products.
9. An Official Data card is added below (source breakdown and Deep Refresh had to remain).
10. Breakpoints use content width (1000 / 680) instead of window width (1200 / 850 / 550); the minimum window is 960.

## 22. Windows test package

| | |
|---|---|
| Source commit | `2ec6261` (marker file `LOCAL-ONLY-TEST-BUILD.txt` names it) |
| Command | `Build-PortableRelease.ps1 -LocalTestLabel RC14-Phase35E-DASHBOARD-LOCALTEST -LocalTestVersion 0.1.0-rc.14-LOCALTEST.phase35e-dashboard` (PowerShell 7.6.7 local tool in a scratch folder on the macOS build host) |
| ZIP | `artifacts/local-test/RC14-Phase35E-DASHBOARD-LOCALTEST/LOCAL-ONLY-RC14-Phase35E-DASHBOARD-LOCALTEST-win-x64.zip` |
| Size | 76,286,420 bytes (72.8 MB); 308 files (161.8 MB uncompressed) |
| SHA-256 | `0702c01fa6e116abd6fb0530b9f2a5f16c9ec21aea82ee3adcbdddf6a8dbd2ee` |
| Shown as | `0.1.0-rc.14-LOCALTEST.phase35e-dashboard` |
| Validation | script content validation on folder and ZIP (308 files) passed; `RealReleaseZip` passed; no database, WAL/SHM, draft, history, key or update manifest in the ZIP; all 10,625 earlier artifact files (incl. the Calendar-only `RC14-Phase35E-LOCALTEST` and `RC14-Phase35F-LOCALTEST` ZIPs and `artifacts/releases`) re-hashed byte-identical |

Checklist: `docs/releases/rc14-phase35e-dashboard-localtest-windows-checklist.md`.

## 23. Architecture and specification review

- **Product specification** (`AGENT_BUILD_SPEC.md` §8 Dashboard): **reviewed — no change required.** The Dashboard
  follows the explicitly approved reference, uses real application information only and triggers no retrieval on
  navigation; existing functionality is preserved. The spec's "Dashboard design status" text is satisfied as written.
- **Architecture documentation: discrepancy — not edited here.** The current-state architecture text in the working
  tree is part of an uncommitted documentation reorganization that is not this phase's work (`docs/architecture.md`
  rewritten, `docs/architecture/*.md` untracked). Two of its sentences become stale with this commit and should be
  corrected when that reorganization is committed:
  - `docs/architecture.md` review-baseline line: "Dashboard redesign remains blocked by its missing approved reference"
    → "Dashboard redesigned locally in Phase 35E-3 (`2ec6261`), awaiting Windows review".
  - `docs/architecture/desktop-ui.md`: "Dashboard redesign is not implemented in the recorded baseline." → "The
    Dashboard follows the approved Admissions Overview (Phase 35E-3): gate card, passdown, shows, dated pricing,
    operational notes, cached Pass Perks and Official Data; `DashboardView` code-behind switches the gate grid and
    columns by width (layout only)."
  Facts for the data/contract sections: `OperationsDashboardSnapshot.Pricing` / `PassPerks` / `PassPerksLoadFailed`;
  `DashboardPricingSelector` (Application/Dashboard); `OperationsDashboardService` optional `IPassPerksService`
  (cache-only); three new Dashboard navigation events routed by `MainViewModel`; four card-rule tokens per theme.
  No dependency-direction, persistence, sync, retrieval, PDF, lifecycle, threading, security or packaging change.
- `Controls/ShowColumnsPanel` is no longer used by any view (its own tests still pass); removal is a 35G cleanup item.

## 24. Status and limitations

- **Windows validation: NOT EXECUTED** (checklist above). Mac renders and timings are not Windows evidence; 125 % /
  150 % scaling, Narrator, high contrast and large text are Windows-only checks.
- Phase 35F (`8967fcc`, `RC14-Phase35F-LOCALTEST`) is also still awaiting Windows review; nothing here marks it approved.
- **Phase 35E overall: implemented locally (Calendar + Dashboard), not complete** — it closes after the owner confirms
  the Calendar and Dashboard on Windows and the architecture text above is corrected.
- Known limitations: the Daily Feature text is shown as entered (math markup unrendered, as before); the Pass Perks card
  shows at most two perks; a visible Dashboard reload costs ≈ 15 ms more than before (§19); `ShowColumnsPanel` unused.
- Phase 35G not started.
