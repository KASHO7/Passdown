# Phase 35E — Dashboard and Operations Calendar redesign

Status: **Calendar implemented locally — awaiting Windows review. Dashboard NOT started: its approved reference was not
supplied (§1). Phase 35E is therefore incomplete and is not marked complete.** Not pushed, tagged or published.
Phase 35F has not been started.

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
