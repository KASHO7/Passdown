# Phase 22D — Associate Workspace Refinement & Number Formatting

**Status:** unreleased development after RC.6 (`0.1.0-rc.6`). Builds on Phases 22A–22C (HEAD before
this phase: `d768056`). Next packaged candidate: **`0.1.0-rc.7`** (not packaged here;
`Directory.Build.props` still says `0.1.0-rc.6`). RC.6 artifacts are untouched.

Scope: the Associate Passdown workspace's layout and information hierarchy, the Season Price History
list surface, and en-US thousands separators for projected attendance and pre-solds wherever they are
displayed. No retrieval, refresh, pricing, Shows, Daily Feature parser, spell-check, finalization or
Personal Sync rule changed. No schema changed (DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1); no
migration.

Files:

- `src/Carowinds.Passdown.Domain/OperationalCount.cs` — new shared count formatter/parser (§13).
- `src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml` / `.axaml.cs` — layout (§1–12), blur hook.
- `src/Carowinds.Passdown.Desktop/ViewModels/AssociateViewModel.cs` — count editor text (§15),
  `HasOperationalNotesCount`.
- `src/Carowinds.Passdown.Desktop/Converters/OperationalCountTextConverter.cs` — new (History detail).
- `src/Carowinds.Passdown.Desktop/Views/HistoryView.axaml`, `PriceHistoryView.axaml`.
- `src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml` — `ListBox.popupList`, selected row states.
- `src/Carowinds.Passdown.Desktop/Presentation/OperationsDashboardPresentation.cs`,
  `src/Carowinds.Passdown.Application/PersonalSync/SyncConflictDetail.cs`,
  `src/Carowinds.Passdown.Documents/Associate/Templates/PdfFormatting.cs`,
  `src/Carowinds.Passdown.Documents/Associate/AssociateTeamsTextFormatter.cs` — route counts through
  `OperationalCount`.
- Tests: `OperationalCountTests` (Domain), `Phase22DCountFormattingTests` (Documents),
  `Phase22DAssociateWorkspaceTests` (Ui, mixed partition), +11 in `Phase15ERealWindowLayoutTests`
  (run-alone); intentional expectation updates listed in §21.

## 1. Initial Associate audit (at `d768056`)

Measured in the isolated Skia harness (§19) with a synthetic busy day:

| Region | What it held | Observation |
|---|---|---|
| Header (111 px at 1280 wide) | Title + save chip; date bar + actions; progress; multi-page warning; refresh status; official-data line + auto-check note; copy, load-error and sync-conflict messages | Check Online was the only accent (primary) button — the passdown outputs (Create PDF, Copy) looked secondary. Official-data line sat on its own row. |
| Rail (152 px) | Overview / Products / Notes | Tall items (13 px padding), square edges. |
| Overview "Day & Operations" panel | One label/value grid: Park Hours, North Gate / Avenue, verification, Attendance / Pre-solds (two wide inputs), Early Ride + Edit, Daily Feature (type, editor, ∑ Math, preview), boxed overrides inset | Official and typed values looked the same; attendance inputs stretched ≈ 430 px for a five-digit number; counts shown as `38500`. Math button and preview were separate rows below the editor. |
| Status strip panel | "Draft: … \| Verified · Last checked … \| Notes: N" + refresh failure + PDF export message | Duplicated the save chip and the park-hours verification line; failure/PDF messages only visible on Overview. |
| Shows panel | Checkbox header + Select all / Clear buttons; one-line rows "Name — times venue" | Two outlined buttons competing with the toolbar; rows ran together. |
| Include in Passdown panel | Seven checkboxes | Fine; unchanged. |
| Products | "Season pass price history" button alone on a row; per category: band, boxed status inset, ITEM / DISPLAY PRICE header, rows with "Official $x" under the name | Official price was secondary text under the name, so amounts didn't line up. |
| Notes | Daily notes editor; included and available persistent notes as boxed cards inside panels | Nested borders per note. |
| Price History overlay | Product ListBox kept Fluent's default fill (light gray in dark mode — the Phase 22A known limitation). |

What already worked and was kept as-is: the Phase 21B date navigator and calendar popup, the WrapPanel
action bar, Copy / More menus, the ERT editor, the overrides' typable time boxes, the Shows semantics,
the section-switching mechanism (`SelectedSection` + `active` class), every automation name.

## 2. Information hierarchy

Top to bottom: title row (page name · official-data freshness · Draft save state) → date navigation →
Check Online │ Create PDF · Copy · More → feedback stack → rail + section. Overview reads Park Hours
| Attendance → Early Ride → Daily Feature → Hours Overrides → Shows → Include in Passdown, matching the
task order (date, official info, entered info, feature, shows, output). No section, workflow, field or
entity was added or removed.

## 3. Date bar

Unchanged controls and behavior (Phase 21B: ‹ fixed-width date button with calendar popup › Today,
relative-day chip, keyboard, Escape/outside-click, Eastern dates). Only the header around it tightened
(padding 16 → 12, spacing 12 → 10, title row ≈ 12 px shorter).

## 4. Toolbar

Same commands, same order (asserted). Check Online is now a quiet outlined `toolbarButton` (always
visible, never automatic), followed by a thin divider, then **Create PDF · {style}** as the page's one
accent button, Copy ▾ (secondary) and More ▾ (PDF Style radio items, Season Price History…). Settings →
Diagnostics unchanged. Wrapping: at 960×640 (and 125–150% scaling) the action group drops to a second
line (action bar 34 → 76 px), never clipping or overlapping (real-window test at 960×640, 1280×690,
1280×860, 1920×1000).

## 5. Overview layout

One `heroPanel`, regions separated by whitespace and hairlines instead of nested boxes:

- **Park Hours** (left) — headline, Open/Closed chip, verification/stale/waterpark lines, then North
  Gate / Avenue (effective time, "Override" chip, override box, Reset) — identical bindings.
- **Attendance** (right, same row) — Projected / Pre-solds right-aligned count boxes (120–180 px).
  The row is capped at 1040 px so on a wide window attendance stays beside the hours.
- **Early Ride** — label, the rides on the same line, Edit at the right; the ERT editor still opens below.
- **Daily Feature** — type ComboBox with **∑ Math** beside it, the editor full width, the live preview
  attached to the editor's bottom edge (§10).
- **Hours Overrides** — the manual-hours / private-buyout checkboxes and time boxes, no longer boxed.

The old status strip was removed: its save text duplicated the Draft chip, its verification duplicated
the Park Hours line, its notes count moved to the rail ("Notes 1", hidden at 0), and its refresh-failure
and PDF messages moved to the header feedback stack (now visible on every section).

## 6. Official versus manually entered information

Small word tags beside each group heading: **Official** (accent chip — Park Hours, Shows "Official
schedule", Products "Prices for {date}"), **Entered here** (neutral chip — Attendance, Daily Feature,
Hours Overrides, Daily Passdown Notes, Custom & Internal Items) and **Your setting** (Early Ride
configuration). Official values are read-only text; entered values are input boxes; a gate override
keeps its "Override" chip. The word carries the meaning; color only reinforces it. Nothing manual is
labeled verified.

## 7. Products

A heading row "Prices for {selected date} · Official" with the Season Price History button. Category
status is now a compact two-line block (status + detail) instead of a bordered inset — still always
shown. Rows gained a right-aligned **OFFICIAL** column (the exact `OfficialPriceText` that was
previously "Official $x" under the name), so amounts line up; the DISPLAY PRICE override box stays in
the last column, with "Use official price" and "Price history" as link actions. Compare-at, qualifier,
bundle and stale/verification notes remain under the name. No pricing or retrieval logic changed
(Phase 21A Military/First Responder dating, Exact vs From, historical pricing, pass-year identity,
expired/retired products all untouched).

## 8. Notes

Same three panels. The daily notes editor (180 px min) carries an "Entered here" tag and a one-line
hint; included and available persistent notes are hairline-separated rows instead of a boxed card per
note; Remove is a secondary button (label/automation name unchanged: "Remove {title} from this
passdown"). Persistence, date association, spell-check, autosave, sync, History and PDF rules unchanged.

## 9. Shows

Header: Include checkbox + **Shows** + "Official schedule" tag; Select all / Clear as link actions.
Rows: checkbox, show name (SemiBold) over "times · venue" (venue muted, hidden when not published),
hairline-separated; changed/removed-time notes stay beneath the row in the warning style. Freshness and
status lines (verified, NotPublished, confirmed none, closed, unavailable, error, last-known-good)
unchanged. Default-off, date-specific selection, no auto-selection of new shows, offline fallback —
unchanged. PDFs keep Shows last; Teams Copy still excludes Shows.

## 10. Math editor appearance

The `DailyFeatureMathPreview` (unchanged control and renderer) gets the `attachedPreview` class: same
secondary surface, a 1 px border on three sides and bottom-rounded corners, inset 6 px — it reads as a
tray hanging off the editor. **∑ Math** sits beside the type selector but is declared after the editor,
so Tab order stays type → text → Math (asserted). Parser, templates, symbol picker and spell-check math
exclusion are untouched (Phase 21D tests pass unchanged).

## 11. Save / verification hierarchy

Title row, right side: official-data freshness (`OfficialDataStatus.Presentation`, Phase 19A wording,
with the auto-check note) │ **Draft** [save chip]. The chip is the only save indicator (Not saved yet /
Unsaved changes / Saving / Saved / Save failed / Conflict — same text and converter, debounce untouched).
The "Draft" label keeps a locally saved draft from being read as verified official data. Feedback
stack under the toolbar: multi-page PDF warning, refresh progress, Check Online failures, PDF export
result, copy result, load error and sync conflict (error color, full text). Nothing was suppressed.

## 12. Season Price History overlay

`ListBox.popupList` (shared style): `PassdownPopupSurfaceBrush` background, hairline frame, 10×6 item
padding, primary text. Shared `selectableRows` now also defines selected+hover/pressed and pressed fills
from Passdown tokens (Fluent's accent list brushes no longer leak through). Real-window test: the list's
background is the theme's popup brush in Light and Dark. History data and calculations untouched.

## 13. Number-formatting helper

`Carowinds.Passdown.Domain.OperationalCount` (Domain, so Application, Documents and Desktop share it
without new references):

- `Format(int)` → `count.ToString("N0", PresentationCulture.UnitedStates)` — 0 → "0", 250 → "250",
  1000 → "1,000", 38500 → "38,500", 1000000 → "1,000,000"; host culture ignored.
- `Format(int?, missingText)` — missing stays the caller's existing text ("—", "Not entered").
- `TryParseEntry(text)` — the pre-22D inputs (optional sign, trimmed, invariant digits) plus ignored
  commas; rejects decimals, letters, inner spaces, a lone comma, overflow.

## 14. Number formatting by surface

| Surface | Before | After |
|---|---|---|
| Dashboard (Today at a Glance) | `38500` | `38,500` (missing: "Not entered") |
| Associate Overview editors | `38500` | `38,500` after load/blur (§15) |
| History detail | `Projected attendance: 38500` | `Projected attendance: 38,500` (missing: "not recorded") |
| Personal Sync conflict comparison | `38500` | `38,500` (missing: "—") |
| Classic / Compact / Modern PDF | already `38,500` (`"N0"` since Phase 18B) | unchanged text, now via the shared helper |
| Teams Copy (Operations Summary, Operations + Pricing) | already `38,500` (Phase 18F) | unchanged |

Not touched: dates, years, times, IDs, versions, prices (`$59.00`, `From $40.00`), product counts,
Daily Feature text/math, free-form notes. (The Phase 22C doc's known-limitation line saying PDFs lacked
separators was inaccurate — they already grouped; only the on-screen surfaces above did not.)

## 15. Numeric editor behavior

- Bindings unchanged (per-keystroke, same validation messages, same autosave scheduling).
- While typing, the text is never rewritten — no caret jumps; autosave does not rewrite it either.
- On blur (`LostFocus` → `RegroupCountFields`), a valid value is shown grouped. This is display-only:
  the parsed value is unchanged, so no autosave is scheduled and the save state does not change.
- Invalid text (letters, decimals, negatives) is left as typed with its error.
- Editing a grouped value in place works ("38,5001" → 385001 → "385,001" on blur).
- On load/date switch the value shows grouped; when a reload carries the same number the field already
  holds (e.g. an official-data reload right after the associate's own autosave), the typed text is left
  exactly as is.
- Empty stays empty (missing ≠ 0); an entered 0 shows "0".

## 16. PDF formatting

No template change. All three templates already called `PdfFormatting.FormatCount` ("N0"); it now
delegates to `OperationalCount` with identical output. Verified: 38,500 / 21,240 in all three; 250 and
0 ungrouped; missing prints "—"; two renders differing only in counts have identical word sequences
apart from the count tokens; 1,000,000 / 250,000 stay on the page without overlapping; page count
unchanged; prices and Daily Feature numbers ("1973", "10000") unchanged; math renders identically.
Rasterized review: one page each, Shows last, no clipping or awkward wrapping.

## 17. Teams Copy formatting

Operations Summary already printed "Projected Attendance: 38,500" / "Pre-solds: 21,240"; now via the
shared helper. Tested: the only difference between two copies is the count text; missing counts add no
line; pricing copy is identical whatever the counts; menu options, Shows and Daily Feature exclusion
unchanged.

## 18. History compatibility

Finalized snapshots store `int?` counts (asserted: snapshot JSON contains `38500`, never `38,500`).
History serves stored PDF bytes unchanged (`FinalizedPassdownPdfRetrievalService`); no stored PDF is
regenerated. The History detail panel renders the stored integer through `OperationalCountTextConverter`
— improved display, unchanged snapshot. Newly generated/finalized PDFs use grouped counts (as before).

## 19. Screenshot comparisons

Isolated harness (session scratchpad, not committed): a console app compiled against the Desktop project
and the Ui.Tests fakes, `UseSkia().UseHeadless(UseHeadlessDrawing=false)` + `CaptureRenderedFrame()`, no
desktop lifetime, no database, synthetic data only (busy day: 38,500 / 21,240, products in four
categories incl. a stale Fast Lane and an override, four shows with two selected, math Daily Feature,
notes, unsaved state, Season Price History open). A second copy compiled against `d768056` (`git
archive`, no repository change) gave the "before" numbers. The real profile DB
(`~/Library/Application Support/Carowinds Passdown/carowinds-passdown.db`) had identical mtime/size before
and after every run.

48 after-PNGs: Dashboard, Overview, Products, Notes, math picker open, Season Price History × Light/Dark ×
1280×860, 960×640, 1280×690, 1920×1000; 24 before-PNGs; 3 PDFs rasterized.

| Measure (light) | Before | After |
|---|---|---|
| Header height, 1280 wide | 111 px | 99 px |
| Header height, 960 wide (toolbar wrapped) | 153 px | 141 px |
| Overview content height | 959 px | 924 px |
| Products content height | 1014 px | 957 px |
| Notes content height (960×640) | 470 px | 442 px |
| Overview panels | 4 (Day & Operations, status strip, Shows, Include) | 3 |
| Accent buttons in the header | 1 (Check Online) | 1 (Create PDF) |
| Above the fold at 1280×860 | through the Shows heading | hours, gates, attendance, Early Ride, Daily Feature + preview, overrides, Shows heading and first row |
| Attendance display | `38500` | `38,500` |

Defects found and fixed during review: attendance drifted to the far edge at 1920 (row capped at
1040 px); ∑ Math was disconnected from the editor at wide widths (moved beside the type selector);
Overview initially grew from new group headings (spacing 14 → 10, Early Ride on one line); show rows
were cramped (row padding 6, line spacing 2). Fewer pixels was not the goal: the count boxes are
narrower, but every editable control keeps its Fluent height and hit target.

## 20. Accessibility

- All automation names unchanged; new groups are named ("Park hours", "Attendance", "Early Ride",
  "Daily Feature", "Hours overrides", "Shows"); the Notes rail item's help text is "Operational notes: N".
- Source tags, save state and freshness are words; color only reinforces.
- Tab order: toolbar order unchanged; Daily Feature type → editor → Math (asserted); rail unchanged.
- Visible focus: Fluent focus adorners untouched (no control template replaced).
- Contrast: all colors are Phase 22A tokens (no literal colors, asserted).
- **Narrator not validated** (no Windows host).

## 21. Test coverage

| Class | Partition | Count | Covers |
|---|---|---|---|
| `OperationalCountTests` (Domain) | normal | 43 | 0 … int.MaxValue, missing text, seven host cultures, parse accept/reject, parse culture independence |
| `Phase22DCountFormattingTests` (Documents) | normal | 26 | all three PDFs: grouped, small/zero, missing "—", only count tokens differ, large values on page/no overlap/same pages, prices and feature numbers unchanged, math identical; helper; Teams grouped/only-difference/no added field/pricing identical; snapshot stores the integer |
| `Phase22DAssociateWorkspaceTests` (Ui) | mixed | 37 | editor load/typing/blur/no extra save/in-place edit/invalid/clear/zero; section switching keeps state with no online call; rail count; Dashboard and History converter counts (4 cultures); sync comparison; toolbar hierarchy and order; save vs official indicators; feedback stack; every pre-22D binding present; source tags; count inputs; math tab order; Shows/Products/Notes row markup; tokens only; Price History list style |
| `Phase15ERealWindowLayoutTests` (Ui) | run-alone | +11 | action bar at 1920×1000; every section no horizontal clipping at five size/theme combinations; hours and attendance share the first row above the fold; real focus change regroups without a new save; Price History list background = popup brush (Light/Dark) |

Intentional expectation updates (grouping is the requested change): `OperationsDashboardPresentationTests`,
`UsPresentationCultureTests`, `Phase19ALocalPropagationTests`, `SyncConflictDetailCultureTests` (32500 →
32,500 etc.), `Phase21BAssociateNavigationTests` (a reloaded 18000 now shows "18,000"); the
`Phase22ADesignFoundationTests` selectable-list regex accepts an additional class.

Deterministic total: **3718/3718** (baseline 3601). Release build 0 warnings, 0 errors.

## 22. Windows validation checklist

All **NOT EXECUTED** (no Windows host). Run on the RC.7 package.

| Check | Status |
|---|---|
| Date arrows, Today, date popup (keyboard, Escape, outside click) | NOT EXECUTED |
| Toolbar: Check Online, Create PDF (style), Copy ▾, More ▾ — wrap at 960×640 | NOT EXECUTED |
| Overview editing: gates overrides, attendance, pre-solds, ERT editor, overrides | NOT EXECUTED |
| Typing in attendance never moves the caret; Tab away shows 38,500; save chip behaves | NOT EXECUTED |
| Invalid count shows its error and is not reformatted | NOT EXECUTED |
| Products: official column alignment, override box, Use official price, Price history | NOT EXECUTED |
| Notes editing, persistent note Add/Remove/Replace | NOT EXECUTED |
| Shows checkboxes, Select all, Clear, changed/removed warnings | NOT EXECUTED |
| ∑ Math picker inserts at caret; preview attached; Tab order type → text → Math | NOT EXECUTED |
| Spell-check underlines cleared when switching sections | NOT EXECUTED |
| Autosave after edits; switching dates flushes | NOT EXECUTED |
| Dashboard and History detail show 38,500 / 21,240 | NOT EXECUTED |
| New PDFs (Classic/Compact/Modern) show grouped counts, Shows last | NOT EXECUTED |
| Teams Copy Operations Summary shows grouped counts | NOT EXECUTED |
| A History PDF finalized before RC.7 opens unchanged | NOT EXECUTED |
| Season Price History list in Light and Dark (background, selection, hover, focus, scroll) | NOT EXECUTED |
| Live theme switch on the Associate screen | NOT EXECUTED |
| 100% / 125% / 150% scaling (incl. 960×640) | NOT EXECUTED |
| Keyboard-only pass and visible focus | NOT EXECUTED |
| Narrator: source tags, Draft status, rail notes count | NOT EXECUTED |

## 23. Known limitations

- No Windows run, DPI scaling or Narrator check (§22); Mac Skia captures are not DPI validation.
- Associate → Products shows the official amount without a "From" prefix for Starting-From rows (the
  row's qualifier carries it, as before 22D); PDFs and Teams Copy print "From" as before. Not changed:
  pricing presentation was out of scope.
- The official-data freshness line on the title row only appears once an app-wide status exists (as
  before); the synthetic harness has none, so screenshots show only the Draft indicator there.
- Count boxes accept commas anywhere ("3,8500" → 38500) rather than enforcing grouping positions — a
  deliberate choice so editing a grouped value never traps the user in an error.
- Headless unit tests use fake font metrics; visual density evidence is from the Skia harness only.
- **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.**
