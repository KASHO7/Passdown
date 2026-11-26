# Phase 35C — Products & Pricing redesign and anchored price popover

Status: **implemented locally — awaiting Windows visual and functional review** (checklist:
`docs/releases/rc14-phase35c-localtest-windows-checklist.md`). Phase 35D is **not started**. The Manager workflow stays
deferred (CLAUDE.md).

## 1. Repository baseline

| Item | Value |
|---|---|
| Branch / starting HEAD | `feature/passdown-redesign` at `bad86da` (Phase 35B-2) |
| Phase 35A | Shell, sidebar, tokens and Interface animations — approved by the product owner after Windows testing |
| Phase 35B | Passdown workspace (`1677781`, record `bad86da`) — implemented locally; **Windows validation NOT EXECUTED** (its checklist is still unmarked). 35C builds on it; any 35B Windows finding also applies here |
| Release status | **RC.13 published and frozen** (`v0.1.0-rc.13`, tag → `d784c60`). Next candidate identity: **rc.14** |
| Source version | `0.1.0-rc.13` (`Directory.Build.props`) — not changed by this phase |
| Database schema | 18 — no migration |
| Personal Sync | `SyncSchemaVersion` 1, draft payload schema 4, finalized snapshot schema 2 — unchanged |
| Deterministic tests before | **5,655 / 5,655** as recorded for `1677781` / `bad86da` (not re-run separately here; the 35C run below keeps every earlier test — the Ui mixed partition still had 1,825 tests before the new ones were added) (Domain 462, Application 1,378, Documents 484, Infrastructure 1,309, Ui mixed 1,825, OperationsCalendarView 26, Phase15ERealWindowLayout 134, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 24) |
| Build before | Release, 0 warnings, 0 errors |
| Working tree | pre-existing unrelated changes left untouched: `.DS_Store` files, staged `.claude/skills/pdf-design/SKILL.md`, staged `tools/**/.DS_Store` |

## 2. Approved references

Folder `~/Downloads/carowinds-phase35-approved-design-references/` (bundle README lists every file with its hash):

| Reference | Bytes | SHA-256 | Status |
|---|---|---|---|
| `carowinds_products_refined_prototype.html` | 35,138 | `317489960e44eba1d237f75b99836144227c225819ea08a23048d589fc5749fe` | **available** — matches the bundle README |
| `carowinds_products_preview.png` | 169,087 | `5533c81011bcc142511bfcf6faf1f8268b95d488bf8c8485e2fe53c3cba74684` | **available** |
| `carowinds_products_compact_preview.png` | 126,955 | `bfbe38abf69bb347462a281de970745a3d78d9021256430c6b7a7262cc74b747` | **available** |
| `carowinds_price_popover_preview.png` | — | — | **missing** — not in the bundle, its README inventory, Downloads or the project folder |
| `carowinds-next-phase-uiux-handoff.md` | 31,611 | `374d2843993828fcd99d689fdafa88f45438b351c1c6c246eeaad7c61d1fa5ab` | **available** in `~/Downloads` (not in the bundle folder) |

The missing popover PNG did not block implementation: the approved HTML prototype fully specifies the popover
(`openPopover`: title, product label, prefilled input, official-price line, validation line, Cancel / Save, Use
official price only with an override, Enter / Escape, Tab cycling, focus return, edge repositioning). The popover was
compared against a headless-Chrome render of **that prototype** with its Park Ticket popover opened — not against a
generic mock-up. The prototype's prices, products, Deals/Perks entries and PDF illustration are **not** application
data and were not copied.

## 3. Products control and behaviour inventory (before → after)

| Before (Phase 22D–35B) | Phase 35C |
|---|---|
| Products inside a scroller as a stack of bordered `panel` cards | One white `editorSheet` (same as Overview) on the neutral workspace |
| "Prices for {date}" + Official tag + "Season pass price history" button | Heading "Products & Pricing" + the same "Prices for {date}" Official tag + subline "Select what prints. Official prices remain unchanged when you edit a display value." + **Season pass price history** link (same command) |
| — | **Search products and offers** (filters product and custom rows by visible words; view state only) |
| "Add custom item" at the bottom of the custom panel | **Add custom item** in the heading tools (same command) |
| Category: `categoryBand` + rows | Category heading is a **disclosure button**: chevron · name · "N items · N included" (follows the checkboxes live). Collapse state is per category name for the session; it never changes what prints |
| Category status / source note / official source link | Kept, under the heading, same bindings and accessible link |
| Columns: Item · Official · Display price (TextBox) | Columns: **Inc · Product / description · Official · Display price · Details** |
| Display price = permanent TextBox with placeholder "Use official ($x)", inline error, "Use official price" link | Display price = **plain text** + pencil in one **Edit** button ("Edit display price for X, currently $Y"); a quiet "Manual override" line under it when an override exists; the official value stays in its own column |
| View details / Price history links under the name | **Details** / **History** links in the right-hand column (same commands, same accessible names) |
| Compare at / Reference price / Renewal offer / Initiation fee / qualifier / bundle note / stale warning / deadline | All kept as the row's description lines |
| Non-printable rows (Memberships, unavailable options): "Not printed on the passdown." | Kept; no Edit control on those rows |
| Override on a starting-price row: "Prints as From $x" | Kept under the display price |
| Custom item: inset card with name / price / notes TextBoxes, Custom chip, Remove | A table row: checkbox · Custom chip + name box + notes box (free text authored here) · "—" official · **price as text + Edit** (same popover) · **Remove** |
| Past-season items expander | Kept (same content), inside the sheet |
| Current Deals panel (current / upcoming / previously verified / expired, status, empty text) | Kept below the products with the same bindings, after the note "Reference information below is not automatically included as individual products in the PDF." |
| Pass Perks panel (month picker, status, history notice, empty state, perks, program terms) | Kept below Current Deals with the same bindings; the "publicly advertised, not an individual passholder's wallet" note sits beside the month picker |

Product groups, order, inclusion defaults, wording and provenance come from the same composed workspace as before —
nothing about categories or products is hard-coded from the prototype.

## 4. The price popover

**Files**: `ViewModels/PriceEditorViewModel.cs` (editor state + `IPriceEditTarget`), `ProductRowViewModel` /
`CustomProductRowViewModel` (implement `IPriceEditTarget`, `EditPriceCommand`), `AssociateViewModel` (owns the single
`PriceEditor`, closes it on date/section/search/rebuild, stale guard), `AssociateView.axaml` (`PriceEditorPopup`),
`AssociateView.axaml.cs` (anchor, focus in/out, Enter/Escape, close on scroll/resize).

Lifecycle decisions:

1. **One editor** (`AssociateViewModel.PriceEditor`) and one `Popup` in the view — two editors can never be open.
   Opening another product discards the pending text of the first (never saves it).
2. **Anchor**: the activated Edit control's display-price cell (so its "Manual override" hint stays visible). The popup
   uses the window **overlay layer**: it stays inside the window, opens below aligned to the cell's right edge, **flips
   above** near the bottom edge and slides inward near the sides. The 6 px gap is a margin, so it stays a gap when
   flipped. The row does not expand or move (real-window test compares the row and the scroll extent before/after).
3. **Opening**: a partly hidden row is scrolled fully into view and laid out *before* the editor opens; focus moves
   into the input with the value selected.
4. **Prefill**: the effective display amount — the override, else the official amount, else empty (no value is
   invented for "Not priced" or textual promotional wording).
5. **Save** (button or Enter in the input): validation is the existing `PriceFormatting.TryParse` (amounts; `$`
   optional; ≥ 0). On a starting-price row a typed "From" prefix is accepted (the PDF prints "From" from the row's
   price kind). Valid → applied through the row's existing `DisplayPriceOverrideText` path (autosave + preview refresh,
   official fields untouched) → editor closes. Unchanged value → closes with no override created and no autosave.
   Invalid → editor stays open with the typed text and the existing message ("Enter a valid price, e.g. 24.99."),
   announced assertively; nothing is applied. Blank on an official row → "Enter a display price, or choose Use official
   price." (a custom item's price may be blank — its existing optional rule).
6. **Cancel** (button, Escape, click outside = light dismiss): closes, discards the text, no mutation, no autosave, no
   preview refresh.
7. **Use official price** (only when an override exists): `ResetToOfficialCommand` — clears the override (never copies
   the cached official string into it), autosaves, refreshes the preview, closes.
8. **Stale protection**: the editor records the row instance and the date in edit. A date switch, section switch,
   search change, collapsing the row's category, a list rebuild (load / Check Online), removing the custom item,
   scrolling the list or resizing it closes the editor unsaved. Save / Use official re-check that the row is still on
   screen and the date is still both the selected date and the date in edit; otherwise nothing is applied and the page
   shows "The date or product list changed, so this edit was not saved."
9. **Focus return**: to the Edit control that opened it, when it still exists and is visible.

## 5. Official / manual price preservation

- The official value (`OfficialPriceText`) is always shown in its own column, also under an override.
- Overrides stay **amounts** (`decimal?`) — the existing draft, SQLite (schema 18) and Personal Sync shape. The popover
  does not introduce text overrides; non-numeric official wording ("Free", "3rd Night FREE", "Not available") is shown
  as the official/display text but cannot be typed as an override (see §11.1).
- "Manual override" is shown whenever the display value comes from the associate; it never claims verification.
- Rows with no official value read muted ("Not priced" / "Not available"), not like a price.
- Stale rows keep "Stale — showing the last verified price."; the editor still works on them.
- No retrieval code, request coalescing (Phase 34B), pricing rule, source rule or PDF template changed.

## 6. Accessibility

- Edit is a real Button (Tab stop, Enter/Space, visible focus) named "Edit display price for X, currently $Y"; custom
  rows: "Edit price for X, currently …". Category headings are buttons named "Name, N items · N included, expanded/collapsed".
- Popover panel name "Edit display price for X"; input "Display price for X" with the official line as help text;
  buttons "Cancel price edit", "Save display price for X", "Use official price for X".
- Validation message has `AutomationProperties.LiveSetting="Assertive"`; the stale-edit notice on the page too.
- Tab cycles inside the popover (`KeyboardNavigation.TabNavigation="Cycle"`); a click outside dismisses (= Cancel), so
  focus can never sit behind an invisible popup.

## 7. Motion

One opacity transition under `Window.motion` using `PassdownMotionOverlay` (150 ms, CubicEaseOut), in the shared Motion
section of `PassdownStyles.axaml`. The panel rests at 0.35 opacity while closed (`entering` class) and drops the class
on open, so with Interface animations **Off** or Windows reduced motion it is fully visible at once. No movement, no
scaling, no keyframes (the Phase 22B/35A motion contract forbids keyframe animations; it is unchanged). The popover is
hit-testable and focused from the first frame; Save, Cancel, Edit and preview never wait.

## 8. Responsive layout (Avalonia, Skia headless, synthetic data — not Windows DPI evidence)

`tools/visual-fixtures` `P35C-*` scenarios at 1920×1080, 1536×960, 1280×720, 960×640 in Light and Dark — 88 captures:
regular view, sidebar collapsed, preview hidden, manual override, popover open (with and without override), invalid
input, popover near the bottom edge (flips above), keyboard focus on Edit, search, scrolled to Memberships / Add-Ons /
Custom. Geometry report: **no page-level horizontal overflow and no button past the right edge in any capture.**
Real-window tests repeat the overflow / Edit-within-editor / popover-within-window checks at the four sizes with the
preview shown and hidden. The dev profile's files were byte-for-byte untouched by the harness (mtimes compared).

Prototype (headless Chrome, 1536×960, its Park Ticket popover opened) vs implementation:

| Area | Match | Intentional difference |
|---|---|---|
| Sheet & heading | White sheet, title + subline, search, Add custom item | "Prices for {date} · Official" tag kept; search sits under the heading (room for the Season pass price history link) |
| Category headings | Tinted disclosure bar, chevron, "N items · N included" | Chevron is a drawn path, not a text glyph |
| Columns | Inc · Product / description · Official · Display price · Details | Description lines are the real notes (Compare at, Reference price, Renewal offer, qualifier, deadline, stale warning) |
| Display price | Text + pencil, "Manual override" hint | — |
| Popover | Title, product label, prefilled input, official line, validation, Cancel / Save, Use official | Use official price sits left of Cancel on the button row; starting-price rows add a "prints as From" note; anchored to the price cell, so the override hint stays visible |
| Custom items | Group with Custom chip, price as text + Edit, Remove | Name and notes remain inline text boxes (they are authored here); no "Manual override" (custom prices are never official) |
| Deals / Perks | Below the products after the "not automatically included" note | Real statuses, history notices, expanders and month picker instead of the prototype's sample offers |
| Shell / preview | — | 35A native title bar and sidebar; Phase 33 preview toolbar (unchanged) |

## 9. Performance (macOS Arm64, headless Skia, 40 synthetic products — development evidence, not Windows results)

Scratch benchmark (not committed) run against `bad86da` (before) and the 35C tree (after); median of 25 samples after
3 warm-ups, each including dispatcher pump + one rendered frame:

| Measure | Before | After |
|---|---|---|
| Open Products from Overview | 5.6 ms (p90 11.1) | 5.2 ms (p90 5.8) |
| Section round trip (Products→Shows→Notes→Overview→Products) | 7.8 ms | 7.6 ms |
| Inclusion checkbox → preview refreshed | 0.8 ms | 0.9 ms |
| 10 rapid inclusion toggles → preview | 3.3 ms | 3.9 ms |
| Display-price change → preview refreshed | 1.8 ms (row TextBox) | 6.7 ms (popover Save incl. close + focus return + frame) |
| Popover open (Edit → focused input, frame) | n/a | 7.5 ms (p90 10.2) |
| Category collapse + expand | n/a | 4.7 ms |
| Price History request from a row | 0.3 ms | 0.3 ms |

Memory: 4 × 500 popover open/cancel cycles driven through the view model or with the anchor set directly stay flat
(±10 MB GC noise, no trend). Cycles that also move **Tab-style keyboard focus** between different controls grow about
25–30 MB per 500 focus moves — and the identical growth reproduces on `bad86da` by Tab-focusing checkboxes, so it is a
pre-existing Avalonia focus-visual behaviour (possibly headless-specific), not caused by the popover. Flagged for a
Windows Task Manager check (checklist item 25). No new UI-thread blocking work; the preview pipeline, its debounce and
Phase 34B request coalescing are unchanged.

## 10. Automated tests

- `Phase35CPriceEditorTests` (31, mixed partition): official/override/no-official/textual/free/stale/starting-price
  display; open targets the right row and date with the right prefill; one editor at a time; rapid switching; Save
  through the existing override + autosave + Workspace product (official amount intact); Enter = SaveCommand; invalid
  and negative values keep the editor and change nothing; blank refused; Cancel and light dismiss never mutate or save;
  Use official clears (draft null) and is not offered without an override; date switch closes and never writes to the
  new date; stale Save/Use official refused with the notice; Check Online rebuild, section switch, search and collapse
  close it; persistence after reopening the workspace; custom item price via the popover (blank allowed, no Use
  official, removal closes it); section counts follow inclusion and collapse survives a rebuild; search filters without
  touching the draft; preview reflects a Save and does not change on Cancel/invalid input.
- `Phase35CProductsWindowTests` (13, partial of the run-alone `Phase33PreviewWindowTests`): no row TextBox, Edit names,
  override label + official visible; popover anchored below the price cell, row and extent unchanged, focus in the
  input, Tab cycle, full opacity at once without motion; Enter saves / Escape cancels with focus back on Edit; near the
  bottom it flips above and stays in the window (and opening a partly hidden row does not close it); scroll and resize
  close it unsaved; invalid message visible and assertive; six size/theme/preview combinations fit; motion contract.
- Superseded assertions replaced with their popover equivalents (not deleted): `AssociateViewAccessibilityTests`
  (Display price override name → Edit / input names), `Phase22DAssociateWorkspaceTests` (row bindings now
  `EffectivePriceText`, `EditPriceCommand`, `EditText`, `ErrorText`, `UseOfficialCommand`; new column definition),
  `Phase25PassOfferUiTests` (block end marker), `Phase31CCompactProductsUiTests` (product rows' link reads "Details").
  The motion-contract test (`Phase22BShellNavigationTests`) caught a first keyframe fade, which was replaced by the
  token transition rather than weakening the test.

**Deterministic total: 5,697 / 5,697**, 0 skipped (Domain 462, Application 1,378, Documents 484, Infrastructure 1,309,
Ui mixed 1,855, OperationsCalendarView 26, Phase15ERealWindowLayout 134, RealWindowStartupTiming 2,
SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 36). Clean Release build: 0
warnings, 0 errors.

## 11. Known limitations and intentional deviations

1. **Display-price overrides remain amounts.** The prototype shows "$25.00 Special"; the app's override is a decimal in
   the draft, schema 18 and Personal Sync. Text overrides ("Free", "Only …") would need a schema and sync-format change,
   which this phase does not authorize — **a product decision for later** if wanted.
2. The popover closes (unsaved) on scroll or resize rather than following the row.
3. Category collapse is remembered for the app session only (not saved to a preference file).
4. Search filters product and custom rows only; Current Deals and Pass Perks are not filtered.
5. Headless renders use approximate fonts; Segoe UI metrics and 100 / 125 / 150 % scaling must be checked on Windows.
6. Pre-existing focus-visual memory growth under repeated Tab focus moves (§9) — Windows check.

## 12. Windows validation

**Windows test build** (local only, not a release): `artifacts/local-test/RC14-Phase35C-LOCALTEST/LOCAL-ONLY-RC14-Phase35C-LOCALTEST-win-x64.zip`,
**76,066,099 bytes**, SHA-256 **`69d4edb1a8f9ed9f87f270591fd929f007bd11d240b7943651f57c3e6c29dcad`**, 308 files, built from
`051cc4e` with `Build-PortableRelease.ps1 -LocalTestLabel RC14-Phase35C-LOCALTEST -LocalTestVersion 0.1.0-rc.14-LOCALTEST.phase35c`,
shown as `0.1.0-rc.14-LOCALTEST.phase35c`. Script validation (folder and ZIP) and `RealReleaseZip` passed; no database,
draft, history or update-manifest file inside; all 117 earlier release and local-test files re-hashed unchanged. Not
pushed, tagged, signed or published. Checklist: `docs/releases/rc14-phase35c-localtest-windows-checklist.md`.
**Windows validation: NOT EXECUTED.**

## 13. Follow-up for Phase 35D

- Shows and Passdown Notes can reuse the sheet, `categoryHead` disclosure and `priceRow`-style hairline rows (promote
  the Products styles from `AssociateView.axaml` to `PassdownStyles.axaml` when a second screen uses them).
- `PriceEditorViewModel` + the overlay `Popup` pattern (anchor in code-behind, VM-owned state, stale guard) is the model
  for any further anchored editor.
- Decide whether text display-price overrides are wanted (§11.1) before any schema work.
