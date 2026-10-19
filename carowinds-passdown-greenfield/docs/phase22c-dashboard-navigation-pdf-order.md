# Phase 22C — Dashboard Information Hierarchy, Navigation Labels & PDF Shows Ordering

**Status:** unreleased development after RC.6 (`0.1.0-rc.6`). Builds on Phase 22A (`d12ec88`) and
Phase 22B (`9dec1cb`). Next packaged candidate: **`0.1.0-rc.7`** (not packaged here;
`Directory.Build.props` still says `0.1.0-rc.6`). RC.6 artifacts are untouched.

Scope: the Dashboard's information hierarchy, the shortened navigation labels with a Settings gear,
and Shows as the last section of newly generated PDFs. No retrieval, refresh, persistence, sync,
finalization or pricing-integrity rule changed. No schema changed (DB 16, Draft 4, Snapshot 2,
SyncSchemaVersion 1); no migration.

Files:

- `src/Carowinds.Passdown.Application/Dashboard/DashboardAdmissionSummary.cs` — new: admission
  summary record + `DashboardAdmissionSelector` (the product policy, §5).
- `src/Carowinds.Passdown.Application/Dashboard/OperationsDashboardSnapshot.cs` / `OperationsDashboardService.cs`
  — `Admission` init property, projected from the already-composed workspace.
- `src/Carowinds.Passdown.Desktop/Presentation/DashboardAdmissionPresentation.cs` — new.
- `src/Carowinds.Passdown.Desktop/Presentation/OperationsDashboardPresentation.cs`,
  `DashboardDataHealthPresentation.cs`, `DashboardOperationalNotesPresentation.cs` — overview strings.
- `src/Carowinds.Passdown.Desktop/ViewModels/OperationsDashboardViewModel.cs` — `IsSourceDetailsExpanded`,
  `SourceDetailsToggleText`, `OpenPricingCommand`/`OpenPricingRequested`.
- `src/Carowinds.Passdown.Desktop/ViewModels/MainViewModel.cs` — routes View Pricing to Associate →
  Products for today's Eastern date.
- `src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml` — rewritten layout.
- `src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml` — short labels, trailing Settings gear.
- `src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml` — `Expander.disclosure` /
  `ToggleButton.disclosureHeader` styles, `Button.appSectionItem.iconItem`.
- `src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml` — `PassdownSettingsIconGeometry`,
  `PassdownChevronDownGeometry`, `PassdownNavigationIconSize`.
- `src/Carowinds.Passdown.Documents/Associate/Templates/{Classic,Compact,Modern}AssociatePassdownTemplateRenderer.cs`
  — Shows composed last.
- Tests: `Phase22CDashboardAdmissionTests` (Application), `Phase22CPdfShowsOrderTests` (Documents),
  `Phase22CDashboardOverviewTests` (Ui, mixed partition), `TestSupport/Phase22CDashboardFixtures`,
  +8 in `Phase15ERealWindowLayoutTests` (run-alone); `Phase22BShellNavigationTests`,
  `DashboardViewTests`, `ProductBrandingTests` updated for the agreed labels.

## 1. Previous Dashboard layout (at `9dec1cb`)

Measured in the isolated Skia harness (§18), top to bottom:

| Region | Content |
|---|---|
| Header (73 px) | "TODAY" + "Wednesday, August 26, 2026" (page title); right: shared official-data freshness line |
| Row 1: two equal hero cards | **Today's Operation** (hours headline, Open chip, verification) · **Passdown Status** (state, last saved, Open Passdown) |
| Today at a Glance | four stat tiles: attendance, pre-solds, Early Ride Time, Daily Feature |
| Today's Shows | compact rows + freshness line |
| **Data Status** (275 px, always expanded) | headline, detail, last verified, **primary** Check Online, six count chips, two-column per-source rows (timestamps only in tooltips), Deep Refresh block |
| Row: Passdown Notes · Active Operational Notes | draft note lines + Open Notes · up to 3 relevant persistent notes + Open Operational Notes |
| Latest Finalized | full-width card + Open History |

Eight bordered panels; two equally dominant blue buttons (Open Passdown, Check Online); no admission
price anywhere on the Dashboard (pricing lived only in Associate → Products). Refresh: the Dashboard
reloads its local projection on every `IDataChangeNotifier` change (Phase 19A) — unchanged.

## 2. New Operations Overview

```
WEDNESDAY, AUGUST 26, 2026                                   Official data checked 6:05 AM
Operations Overview
┌───────────────────────────────────────────────────────────────────────────────────────┐
│ PARK HOURS                              DAILY ADMISSION                               │
│ 10:00 AM – 10:00 PM                     Park Ticket                                   │
│ [Open]                                  $64.99                                        │
│ Verified · Last checked Aug 26 6:05 AM  Official price for today · Verified 6:05 AM   │
│                                         View Pricing →                                │
│ PASSDOWN                                OPERATIONAL NOTES                             │
│ Draft In Progress                       2 active notes · 1 important                  │
│ Last saved … · Passdown notes: 1 line   Important · Synthetic: Fury 325 queue …       │
│ [Open Passdown]  Open Notes             View Notes →                                  │
│ ───────────────────────────────────────────────────────────────────────────────────── │
│ OFFICIAL DATA                                                          [Check Online] │
│ Current · 6 current · 1 not published                                                 │
│ Last verified Aug 26 at 6:05 AM                                                       │
│ View Source Details ▾                                                                 │
└───────────────────────────────────────────────────────────────────────────────────────┘
Today's Shows                                              Official schedule checked 6:05 AM
Today at a Glance (2×2)                    │ Latest Finalized
```

(Amounts and names above are the synthetic screenshot fixture, never hard-coded in the view.)

## 3. Information hierarchy

Reading/automation order matches the requested priority: park hours → daily admission → passdown →
operational notes → official data → Today's Shows → secondary passdown figures and Latest Finalized.
One page heading ("Operations Overview") with a small uppercase date caption; one strong outline
(`heroPanel`) for the overview; tiles separated by whitespace, not nested borders; only one accent
button on the page (Open Passdown); Check Online and Run Deep Refresh are secondary buttons; View
Pricing / View Notes / Open Notes / Open History are link actions. Panels 8 → 4. All colors, sizes and
radii are Phase 22A tokens; no literal colors in the view.

Responsive: the 2×2 grid holds at every supported width (window `MinWidth` is 960, giving each column
≈ 420 px of text at 960×640); text wraps rather than clips; no horizontal scroll (real-window test
at 960×640, 1280×690, 1280×860, 1920×1000, collapsed and expanded). The 72 px banner and the shell
are unchanged in height.

## 4. Park hours

Shared `ParkHoursPresentation` (Eastern operating day, midnight closings, overrides, private buyout)
unchanged. The overview adds one Dashboard-only property, `ParkHoursHeadlineText`: open/closed use
the shared headline; otherwise the headline states the real `DashboardParkStatus` — "Hours not
published", "Hours unavailable", or "Hours not checked yet" — instead of the shared formatter's generic
"Hours not checked yet" (which previously appeared on the Dashboard for a NotPublished day: a
conflation fixed here). Stale last-known-good shows the existing "Using information from … — may be out
of date" line plus the stale note in the stale status color (text, not color alone). A stale cached
schedule is never presented as freshly verified.

## 5. Daily admission product policy

`DashboardAdmissionSelector` (Application, pure, unit-tested) over `AssociateWorkspace.Products` —
local data only, no new retrieval:

1. **Product identity**: only the ordinary single-day ticket, by its official name — normalized
   "park ticket" (the dated API's package name, `docs/retrieval/dated-admission-source.md`) or
   "carowinds park ticket" (the checkout card title). Exact normalized match, not substring: "Park
   Ticket + Parking", "2-Day Park Ticket", "Military Park Ticket" never qualify.
2. **Never candidates**: custom rows; Military/First Responder (catalog and dated); Fast Lane; Add-Ons;
   Season Passes; bundles ("Ticket + Meal, Snack & Drink Bundle", "Ticket + Fast Lane Reserve Bundle");
   historical workspaces (Phase 21A).
3. **Exact first**: an official `DatedAdmissionTickets` row, `PriceKind.ExactDated`,
   `DateSpecific`, `ApplicableDate == today's Eastern operating date`.
4. **Else "From"**: the same product's official `PriceKind.StartingFrom` row.
5. **Ambiguity**: two or more distinct prices of the chosen kind → *not available* ("More than one
   Park Ticket price is listed for today"); the lowest is never picked.
6. **Official amount only**: `OfficialPriceAmount`, never a draft's `DisplayPriceOverride` — manual
   draft changes cannot appear as today's official price.

Audit note: the dated source can carry two same-named "Park Ticket" packages for future dates (the
adapter already keeps the lower as the exact price and the higher as `ComparisonPrice`, and on "today"
only one is offered), so the composed workspace normally has one exact Park Ticket row; rule 5 protects
against any future shape that is not.

When no price qualifies, the tile shows **Price not available** with the reason from the dated source's
own status (not published / no official dated price / could not be checked / not checked yet / not
offered) and **View Pricing →**, which opens Associate → Products for today's Eastern date
(`AssociateViewModel.SelectEasternToday`, the same flush-then-load path as Open Passdown).

## 6. Pricing freshness

Exact price: "Official price for today" + "Verified 6:05 AM" (Eastern; the date is added when not
today). Stale exact price: "Last verified Oct 15, 6:05 AM — may be out of date" in the stale color.
"From" price: "From $X" + "Advertised starting price" (never "for today"). No price: "Last checked …"
from the dated category's attempt-only timestamp — never labeled "verified". The price carries its own
timestamp; the page-level "Official data checked" line is not relied on to imply the price is current.
Technical source IDs stay in the source details.

## 7. Passdown status

Unchanged lifecycle (`DashboardPassdownStatus`): "No Draft", "Draft In Progress" (+ last saved),
"Finalized" (+ finalized time; Open Passdown hidden as before). The draft's note-line count moved into
the tile ("Passdown notes: N note lines entered") with the existing **Open Notes** deep link. Open
Passdown still always opens today's Eastern date (Phase 21A behavior and tests unchanged).

## 8. Operational notes summary

The existing model (`ListRelevantForDateAsync`: notes whose effective range covers today, Important
first) already defines "active" and an explicit Important priority, so the tile shows a truthful count:
"No active notes today" / "1 active note" / "5 active notes · 2 important" / "Unable to load operational
notes" (error color), then up to three titles ("Important ·" prefix only for notes a supervisor marked
Important), the existing "+ N more" overflow, and **View Notes →** (Operational Notes manager, filter
state preserved). No new fields, statuses or task semantics.

## 9. Official Data collapsible behavior

Collapsed (default): `OFFICIAL DATA` label; overall headline · one-line counts from the same tally as
the chips ("4 current · 1 stale · 1 unavailable · 1 not published"; "unverified" for verification
issues); a **Needs attention** line naming every stale, unavailable or unverifiable source ("Needs
attention: Fast Lane unavailable · Promotions stale") — visible while collapsed; last verified;
secondary **Check Online** (with its own busy bar and progress/result text); **View Source Details ▾**.

Expanded: the existing count chips, overall detail sentence, per-source rows for Today's Operation and
Products / Offers with their status and (now visible, not tooltip-only) "Last verified/Last checked"
timestamps, and the Deep Refresh block with **Run Deep Refresh**, its busy bar and messages. Nothing was
removed.

Mechanics: `Expander.disclosure` (shared style) bound to `OperationsDashboardViewModel.IsSourceDetailsExpanded`
(default `false`). Pure view state: toggling performs no read, no network call and no refresh-schedule
change (unit + real-window tests count service calls). The header is a ToggleButton (Tab-reachable,
Space/Enter toggle, Fluent focus adorner, focus kept on toggle); the Expander exposes the
ExpandCollapse automation pattern; the label reads "View Source Details" / "Hide Source Details" and
the chevron turns.

## 10. Source health semantics

Unchanged `DashboardSourceStatus`/`DashboardOverallDataStatus` precedence. NotPublished, NotChecked and
NotApplicable are informational (counted, never in the attention line); Unavailable, Stale and
VerificationIssue are named separately ("unavailable" / "stale" / "could not be verified"). "Official
data checked …" in the header is still the Phase 19A presentation, which only says "checked" for a
successful check.

## 11. Today's Shows

Same `ShowSchedulePresentation` (Phase 21C): upcoming performances per show, "+N earlier", venue when
published, "Started earlier today", only a confirmed empty schedule says "No shows scheduled";
NotPublished, ParkClosed, CouldNotCheck, last-known-good wording unchanged. Presentation tightened:
compact panel padding, freshness moved to the heading row, placed below the overview as a secondary
section. No new web request.

## 12. Shortened navigation labels

Visible: **Dashboard · Calendar · Passdown · Notes · History** and a trailing gear. Accessible names keep
the full section names, each containing its visible label (WCAG 2.5.3): "Dashboard section",
"Operations Calendar section", "Associate Passdown section", "Operational Notes section", "History
section", and "Settings". Same six destinations, commands, order, selection flags, page hosting and
lifetimes; `MainViewModel` routing unchanged except the new View Pricing route. Phase 22B styling
(underline + SemiBold + "Current page", neutral hover, width-stable labels) unchanged.

## 13. Settings gear behavior

`Button x:Name="SettingsNavigationItem" Classes="appSectionItem iconItem"` in the trailing `Auto`
column of the navigation Grid (which is now the "Application sections" landmark, so the gear is inside
it). Content: Avalonia's built-in `PathIcon` with an original 8-tooth gear geometry token (no icon
package, no new dependency), 16 px, inheriting the item foreground. Accessible name and tooltip
"Settings"; Tab order History → Settings; Space/Enter activate; Fluent focus ring; selected state =
the same 2 px accent underline + "Current page" item status. Right edge at the strip's 10 px padding;
item height ≤ a text item, so the 33 px strip height is unchanged (asserted).

## 14–16. PDF section order (Classic, Compact, Modern)

| # | Classic | Compact | Modern |
|---|---|---|---|
| 1 | Summary list (hours, gates, attendance, Early Ride) | Summary strip (Shows line removed from it) | Summary grid |
| 2 | Daily Feature (if entered) | Daily Feature | Daily Feature |
| 3 | Pricing categories, existing relative order (single column) | Pricing categories (master two-column layout) | Pricing categories (single column) |
| 4 | Operational Notes (if any) | Notes (if any) | Operational Notes (if any) |
| 5 | **Shows** (when enabled and printable) | **Shows** — the same compact wrapping "Name (times) • …" line, now under a small ruled "SHOWS" heading matching Notes | **Shows** |

Header/footer, fonts, product presentation, Daily Feature math, notes, branding and pagination rules
are unchanged. Shows still uses `KeepSection` (heading kept with its first rows, no forced page
break); empty/disabled/unpublished Shows prints nothing — no heading, gap or separator (proven by
identical word positions with and without). Show names, venues, start times, order, selected IDs,
per-date selections, changed/removed-time handling and offline fallback are untouched (only the call
site moved). Every sampled fixture (normal, busy-with-math, no-shows × three templates) has the same
page count before and after; the busy fixture is two pages in Classic and Modern both before and after.

## 17. Frozen History

History serves the stored PDF bytes (`FinalizedPassdownPdfRetrievalService`; covered by
`GetPdfAsync_FrozenPdfPresent_ReturnsStoredBytesAndMetadata`). No snapshot or stored PDF is
regenerated or rewritten. Newly finalized passdowns freeze the new order
(`NewlyFinalizedProjection_FreezesTheNewOrder`).

Teams Copy (Operations Summary / Today's Pricing / Operations + Pricing) never included Shows and is
byte-for-byte identical with or without selected shows (tested for all three).

## 18. Visual comparison

Isolated harness (session scratchpad, not committed): a console app compiled against the Desktop
project and the Ui.Tests fakes/fixtures, `UseSkia().UseHeadless(UseHeadlessDrawing=false)` +
`CaptureRenderedFrame()`, no desktop lifetime, no DB. A second copy compiled against the `9dec1cb`
source (`git archive`, no repository change) produced the "before" numbers. The real profile DB
(`~/Library/Application Support/Carowinds Passdown/carowinds-passdown.db`) had identical mtime/size
before and after every run.

| Measure (1280×860, light) | Before | After |
|---|---|---|
| Dashboard content height — typical verified day | 1115 px | 699 px (−37%) |
| Dashboard content height — busy day, partial failure | 1184 px | 728 px (−39%) |
| Busy day with source details expanded | (always expanded) 1184 px | 980 px |
| Official data block | 275 px | 87 px (healthy) / 102 px (with attention line) |
| Visibly bordered panels | 8 | 4 |
| Accent (primary) buttons | 2 | 1 |
| Scroll needed at 1280×860 (viewport 686/699) | 429–498 px | 0–29 px |
| Admission price on Dashboard | none | product-identified, own timestamp |
| Today's Shows top (in scroll content) | 294 px | 387–430 px (now deliberately secondary) |

Above the fold: before, 1280×690 showed hours, passdown and Today at a Glance; data status needed
scrolling. After, 1280×690 shows hours, admission, passdown, notes and the official-data summary,
with the top of Today's Shows still on the first screen. At 960×640 the whole Operations Overview (hours, admission, passdown,
notes, official data summary with Check Online) is visible without scrolling; Shows starts at the
fold. At 1920×1000 everything fits without scrolling.

97 after-PNGs were captured: five scenarios (verified, busy + partial failure, nothing published,
closed, stale after failure) × light/dark × 1280×860, 960×640, 1280×690, 1920×1000 (closed/stale at
the first two), collapsed, expanded and full-length renders, and the navigation with each of the six
destinations selected (light/dark, 1280×860 and 960×640). Plus 9 PDFs (normal, busy with math and a
16-performance show, no shows × three templates) rasterized for review.

Defects found and fixed during review:

1. Fluent's Expander minimum height left a ~45 px blank under "View Source Details" → `MinHeight=0`
   in the shared disclosure style.
2. Expanded source rows used only half the width → disclosure stretches its content.
3. A NotPublished day read "Hours not checked yet" → status-accurate overview headline (§4).
4. "1 issues" in the count line → "1 unverified".

## 19. Accessibility

- Order: automation/reading order equals the priority order; each overview area is a named group
  ("Park hours", "Daily admission", "Passdown Status", "Active operational notes", "Data status").
- Admission: the price TextBlock's accessible name is the full sentence ("Daily admission: Park Ticket,
  $64.99, Official price for today. Verified 6:05 AM."); View Pricing has a name and help text.
- Statuses are always words; color only reinforces (stale, warning, error).
- Disclosure: keyboard (Tab, Space, Enter), ExpandCollapse pattern, state-describing label (real-window
  test). Gear: name, tooltip, keyboard, focus ring, "Current page" (real-window test).
- Contrast: all text uses Phase 22A token pairs (≥ 4.5:1 measured in 22A, both themes).
- **Narrator not validated** (no Windows host).

## 20. Test coverage

| Class | Partition | Count | Covers |
|---|---|---|---|
| `Phase22CDashboardAdmissionTests` (Application) | normal | 24 | product identity, exact vs From, ambiguity, bundles, Military/Fast Lane/Add-On/Pass exclusion, other-date, override, custom, historical, stale, end-to-end through the real Associate composition (no online refresh) |
| `Phase22CPdfShowsOrderTests` (Documents) | normal | 19 | Shows last in all three templates by glyph position; after every price, Daily Feature and Notes; nothing else moved; empty/disabled omitted with identical layout; busy + math + 16 performances ≤ 2 pages, heading with first row, no clipping; show semantics; Teams Copy identical; new finalization freezes the new order; no network dependency |
| `Phase22CDashboardOverviewTests` (Ui) | mixed | 34 | admission strings/timestamps/Eastern, park states, passdown states, notes summary, official-data summary/attention, collapse never calls the service, local reload updates the overview, Today's Shows accuracy, View Pricing → today's Products, markup order/one primary action/collapse contents/no hard-coded data, disclosure style tokens |
| `Phase15ERealWindowLayoutTests` (Ui) | run-alone | +8 | gear trailing/keyboard/selected (2 sizes); overview no horizontal clipping and two-column geometry at 4 sizes collapsed/expanded, attention visible collapsed; disclosure keyboard + ExpandCollapse + no service calls |
| `Phase22BShellNavigationTests` (Ui) | mixed | +1 (updated) | short labels, accessible names, Settings gear markup, landmark |

Deterministic total: **3601/3601** (baseline 3515: +24 Application, +19 Documents, +35 Ui mixed, +8 run-alone) (see the release manifest for the per-project breakdown).

## 21. Windows validation checklist

All **NOT EXECUTED** (no Windows host). Run on the RC.7 package.

| Check | Status |
|---|---|
| Dashboard hierarchy reads hours → admission → passdown → notes → official data → shows | NOT EXECUTED |
| Admission shows "Park Ticket" with the correct official price for today (compare Associate → Products) | NOT EXECUTED |
| Admission/park-hours/shows timestamps are Eastern and distinct | NOT EXECUTED |
| View Source Details expands/collapses (mouse, Tab+Space, Enter); no network activity | NOT EXECUTED |
| Partial failure shows the Needs attention line while collapsed | NOT EXECUTED |
| Check Online and Run Deep Refresh still work and show progress | NOT EXECUTED |
| Today's Shows rows/empty states | NOT EXECUTED |
| Navigation labels Dashboard · Calendar · Passdown · Notes · History | NOT EXECUTED |
| Settings gear at the right edge; tooltip; keyboard; selected underline | NOT EXECUTED |
| New PDF: Shows last (Classic, Compact, Modern) | NOT EXECUTED |
| New PDF pagination with a busy day (no orphaned heading, no footer overlap) | NOT EXECUTED |
| Existing History PDFs unchanged (open one finalized before RC.7) | NOT EXECUTED |
| Light / dark mode | NOT EXECUTED |
| 100% / 125% / 150% scaling (incl. 960×640) | NOT EXECUTED |
| Keyboard-only navigation of the Dashboard | NOT EXECUTED |
| Narrator: gear "Settings", "Current page", expander state, admission sentence | NOT EXECUTED |

## 22. Known limitations

- No Windows run, DPI scaling or Narrator check (§21).
- The admission policy recognizes only the "Park Ticket" name the official sources use today; if the
  product is renamed, the tile honestly shows "Price not available" + View Pricing until the name list
  is updated.
- Attendance/pre-solds still print without thousands separators (existing presentation, out of scope).
- "Open Notes" keeps Associate's current date (existing Phase 10C behavior); only Open Passdown and View
  Pricing force today.
- Headless unit tests use fake font metrics; the one-row navigation fit and visual density are
  evidenced by the Skia harness only.
- The overview stays two columns at the 960 px minimum width; there is no single-column breakpoint
  because the window cannot be narrower.
- **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.**
  Neither is affected or cleared by this phase.
