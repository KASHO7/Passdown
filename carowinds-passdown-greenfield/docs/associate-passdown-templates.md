# Associate Passdown PDF templates — Phase 17C (superseded in part by Phase 17D, 17E, and 17F — see §16 and §17)

Adds three selectable Associate Passdown PDF visual templates (Classic/Compact/Modern) and a density
pass over the Associate Overview UI. Baseline this phase was built against: Phase 17B complete, DB
schema 15, Draft schema 4, Snapshot schema 2, SyncSchemaVersion 1, 2059/2059 deterministic tests.

**Phase 17D note:** §1's per-template visual descriptions below (tile/card grids, Modern's two-column
rail body) describe the *Phase 17C* implementation and are kept for history. Phase 17D replaced all
three templates' product rendering with one shared master layout (no cards, no rail) — see §15 for
the current, authoritative visual definition. The architecture in §2-§11 (document model, orchestrator,
one renderer per template, template preference, finalization/immutability) is unchanged by Phase 17D.

## 1. The three templates (Phase 17C shape — see §15 for the current visual definition)

| Template | Intent | Notes |
|---|---|---|
| **Classic** | Preserves the pre-17C recognizable passdown style | Byte-for-byte the same rendering as before this phase — extracted verbatim into `ClassicAssociatePassdownTemplateRenderer`, not redesigned. |
| **Compact** | Maximum information density | Minimal borders/whitespace, every product a dense text row regardless of tier, categories paired two-per-row throughout. Best chance of fitting a busy day on one page. |
| **Modern** | A distinct professional hierarchy | Two-column body: a narrow left rail for "what's happening today" (Park Hours/Attendance/ERT/Daily Feature), a wide right column for products/notes. Left-accent section labels instead of filled bands. Still grayscale/photocopy-safe, not a "web dashboard" look. |

**Classic is the default** for existing and fresh installs alike — no migration-free way to detect
"this is an existing user" exists (there is no migration at all for this preference; see §7), so
defaulting every install to Classic is the only choice that never silently changes what an existing
user's printed Passdown looks like.

## 2. Architecture

```
AssociateWorkspace
    ↓
AssociatePassdownDocumentModelBuilder.Build   (unchanged — template-neutral)
    ↓
AssociatePassdownDocumentModel                (§3: the shared, template-neutral model)
    ↓
AssociatePassdownPdfGenerator                 (orchestrator: font registration, preflight,
    │                                           QuestPDF document assembly, real page count)
    ├── ClassicAssociatePassdownTemplateRenderer
    ├── CompactAssociatePassdownTemplateRenderer
    └── ModernAssociatePassdownTemplateRenderer
```

`IAssociatePassdownTemplateRenderer` (`src/Carowinds.Passdown.Documents/Associate/Templates/`) is the
seam: one method, `Compose(PageDescriptor, AssociatePassdownDocumentModel, CancellationToken)`, that
owns the entire page (size, margins, header, body, footer) so each template can choose a genuinely
different visual hierarchy, not just different colors on an identical layout. `AssociatePassdownPdfGenerator`
is now a thin orchestrator: it never composes page content itself — it runs preflight, selects the
renderer for the requested `AssociatePassdownTemplate`, and counts the real generated pages
afterward, identically regardless of which renderer ran. This avoids the "one giant method with `if
template == ...` throughout the document tree" shape the phase explicitly asked to avoid.

A small `PdfFormatting` internal helper (`Templates/PdfFormatting.cs`) holds the two trivial
formatting rules every renderer needs (`FormatCount`, `NotConfiguredText`) — not a design-system
project, just avoiding retyping two one-line rules three times.

## 3. The shared, template-neutral document model

`AssociatePassdownDocumentModel` (unchanged by this phase) already carried everything a template
needs: day/date, Park Hours, Attendance, the resolved `EarlyRideTime`, Daily Feature, the already-
selected `OperationalNotes`, and already-resolved `ProductCategories`. No renderer independently
reclassifies products, resolves ERT, decides expiration, or duplicates BAF/BAF-Duo/SCarowinds logic —
all three consume the exact same instance `AssociatePassdownDocumentModelBuilder.Build` produced, so
17A's product-display intelligence and 17B's ERT semantics cannot drift between templates by
construction (verified by `FinalizedPassdownSnapshotBuilderTemplateTests.BuildProjection_EveryTemplate_ProducesTheIdenticalSnapshot`,
which asserts the *snapshot* — the model's own frozen shape — is identical across all three
templates; only the PDF bytes differ).

## 4. Conditional sections

Every template preserves the existing conditional-section rules unchanged, because they live in the
model, not the renderer: an empty `ProductCategories` tier, zero `OperationalNotes`, or a Daily
Feature with no content all render nothing at all — no blank placeholder card — in Classic, Compact,
and Modern alike (`TemplateOutputTests.SparseDay_EmptyOptionalSections_ReclaimSpaceAndPrintNoEmptyHeading`).

## 5. Overflow behavior

`AssociatePassdownPdfGenerator` still never refuses generation for content that overflows one page —
QuestPDF paginates onto real additional pages, and the real, authoritative page count comes from the
rendered bytes, identically to before this phase. Measured representative page counts (`RepresentativePageCountTests`,
`AssociateWorkspaceFixtures`-based fixtures, one operating day per scenario):

| Scenario | Classic | Compact | Modern |
|---|---|---|---|
| A. Light day (`SparseDay`) | 1 | 1 | 1 |
| B. Normal day (`NormalDay`) | 1 | 1 | 1 |
| C. Busy product day (`DenseDay`) | 1 | 1 | 1 |
| D. Many notes + heavy products (`ExcessiveDay`) | 2 | **1** | 2 |
| E. ERT override day | 1 | 1 | 1 |
| F. NoERT day | 1 | 1 | 1 |

Compact meets or beats Classic's page count in every measured scenario, including the one genuinely
busy day where Classic and Modern both spill to a second page and Compact does not — matching the
spec's "Compact should generally equal or outperform Classic" expectation without hard-failing a
scenario that legitimately needs two pages under any template. No scenario ever drops selected
content to fit fewer pages (`TemplateOutputTests.ExcessiveDay_OverflowsSafely_NeverDropsSelectedContent`).

## 6. Template preference

`AssociatePassdownTemplate` (enum: `Classic`/`Compact`/`Modern`) and
`AssociatePassdownTemplatePreference`/`IAssociatePassdownTemplatePreferenceStore` live in
`Application/Preferences/` — a new, small namespace mirroring `PersonalSync`'s existing
`SyncConfiguration`/`ISyncConfigurationStore` pattern exactly, so this preference sits in Application
(reachable by both Infrastructure's file-backed store and Documents' renderer selection) without
Application ever depending on Documents or Infrastructure.

`FileAssociatePassdownTemplatePreferenceStore` (`Infrastructure/Preferences/`) persists it as one
small flat JSON file, `associate-passdown-template.json`, directly under
`AppPaths.LocalApplicationDataDirectory` — the same flat-file convention as `device-id.txt`/
`sync-config.json`. A missing or malformed file is always treated as `AssociatePassdownTemplatePreference.Default`
(Classic), never a thrown exception.

## 7. Persistence and schema impact

**No DB migration, no Draft schema bump, no Snapshot schema bump.** The template preference is a
local device/presentation setting, not draft data or finalized history — it lives entirely in its own
flat JSON file, exactly like `sync-config.json`/`device-id.txt`. Confirmed unchanged after this phase:

```
DB schema            15   (no new migration — Migration0015 remains the latest)
Draft schema          4   (AssociateDraftPayload.CurrentSchemaVersion)
Snapshot schema        2   (FinalizedPassdownSnapshot.CurrentSchemaVersion)
SyncSchemaVersion      1   (untouched — PersonalSync was not modified)
```

## 8. Template preference sync — deliberately local-only

The template preference is **not** added to Personal Sync in this phase. It is treated as a
device/presentation preference, like a window size or a font choice — it is acceptable, and expected,
for Device A and Device B belonging to the same associate to show different default templates. The
current settings architecture has no generic "sync this local preference automatically" mechanism
(each PersonalSync entity — drafts, notes, finalized passdowns, `MonthlyErt`, `ErtDateOverride` — was
individually designed and wired as its own sync entity type with its own payload/reconciliation code);
inventing a generic synced-preferences mechanism, or hand-wiring this one preference into the existing
per-entity sync plumbing, is unjustified scope for a local display choice and was explicitly out of
scope per the phase instructions ("do NOT add this preference to Personal Sync in 17C unless the
current settings architecture already supports synced preferences generically" — it does not).

## 9. Selection UX

A compact "PDF Style" dropdown sits directly in the Associate action bar
(`src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml`), next to Create PDF — template choice is
scoped to PDF generation, not an app-wide setting, so there is no separate Settings screen to
navigate to. Changing it:

- Persists immediately (`AssociateViewModel.OnSelectedTemplateOptionChanged`), via
  `IAssociatePassdownTemplatePreferenceStore.SaveAsync` — no explicit "Save" action needed.
- Affects the very next Create PDF (Preview) call with no app restart — `CreatePdfAsync` reads
  `SelectedTemplateOption.Value` fresh on every invocation.
- Never touches `Workspace`, marks the draft unsaved, or interacts with autosave in any way — template
  choice and draft data are fully independent (`AssociateViewModelTemplateSelectionTests.ChangingSelectedTemplateOption_NeverMarksTheDraftUnsavedOrTouchesWorkspace`).

## 10. Finalization / immutability

Finalization (`ScheduledPassdownFinalizer` → `FinalizedPassdownFinalizationService.FinalizeAsync`) is
still automatic-only (no manual "Finalize" button exists in the app, unchanged from Phase 9B). At the
moment a passdown is finalized, the service reads whatever `AssociatePassdownTemplatePreference` is
currently persisted (an optional constructor dependency — a caller that supplies none, including every
pre-17C test, gets Classic) and passes that single resolved template into
`IFinalizedPassdownSnapshotBuilder.BuildProjection`. That choice is frozen forever in the stored PDF
bytes; nothing about template identity is added to the Snapshot's own JSON shape (which would have
required a schema bump, explicitly avoided per §7) — the frozen PDF bytes are the only artifact that
ever reflects which template rendered a given finalized passdown, matching existing precedent (the
same reasoning `HasFrozenPdf`'s doc comment already gives for why legacy rows are "never backfilled by
regenerating a PDF under current styling").

`FinalizedPassdownFinalizationServiceTemplateTests.FinalizeAsync_LaterPreferenceChange_NeverAltersTheAlreadyFinalizedRecord`
proves the critical spec §31 scenario end to end: finalize under Compact, switch the live preference
to Modern, finalize again for the same date (idempotent no-op per existing Phase 9 behavior) — the
stored record's PDF bytes are untouched.

All existing finalized History rows remain exactly as they were finalized; nothing in this phase
regenerates, reinterprets, or displays a stored `FinalizedPassdown` through a "current" template —
History always plays back the frozen bytes, never a live render.

## 11. Associate UI density pass

Scope: the Associate Overview tab (Park Hours, Today at a Glance, Verification/Save Status,
Operational Notes summary) plus a small action-bar addition. Products and Notes tabs, and every other
app section (Dashboard/Calendar/History/Settings), were intentionally left alone except for a shared
style-resource tightening (padding/spacing) that also benefits their existing `Border.panel`/
`Border.inset` usage — a deliberate, small, shared-style consistency change, not a broader redesign of
those screens.

**Card audit / merges:**

- Park Hours (previously its own hero card) and Today at a Glance (Attendance/ERT/Daily Feature,
  previously its own card) **merged into one "Day & Operating Hours" panel** with a single vertical
  divider between the two content groups — one card boundary instead of two. No binding changed, only
  the surrounding `Border`/`Grid` container.
- Verification & Save Status and the Operational Notes summary (each previously its own card — "poor
  card candidates" per the audit criteria: a chip plus a line or two of text) **merged into one
  compact status strip**, again with a single divider instead of two full panels.
- "Include in Passdown" kept as its own lightweight panel (a genuinely distinct, substantial-enough
  section — a checkbox group with its own explanatory copy).
- Manual Hours Override / Private Buyout and the Monthly-ERT/Override-editor inline panels were left
  exactly as-is structurally (rule: compress read-only display more aggressively than editable forms)
  — only the shared `Border.inset` padding shrank slightly (12px → 10px) along with every other
  consumer of that style.

**Style tightening** (`AssociateView.axaml`'s `UserControl.Styles`, shared by the merged panels and by
Products/Notes' existing cards):

- `Border.panel` padding 18px → 14px, corner radius 8 → 7.
- `Border.inset` padding 12px → 10px, top margin 10px → 8px.
- `TextBlock.hoursHeadline` font size 30px → 22px (and its "subtle"/unknown-hours variant 16px →
  14px) — still the single largest text on the page, no longer disproportionate inside the merged
  panel.
- Overview's outer `StackPanel` spacing 16px → 10px; `ScrollViewer` padding 24/20/24/24 → 20/16/20/20.

**Action bar:** one addition — a "PDF Style" label + `ComboBox` (see §9) — placed after Export
Diagnostics and before Create PDF. Every existing action (Previous/Next day, date picker, Check
Online, Export Diagnostics, Create PDF) keeps its exact `Content`/`AutomationProperties.Name`.

## 12. Layout verification

- `Phase15ERealWindowLayoutTests.AssociateView_ActionBar_EveryActionReachableWithinWindowBounds`
  (real headless `Window`, both 1280×860 and 960×640) — every action button (including the new PDF
  Style dropdown's neighbors) stays within the window's client bounds with non-zero width.
- `Phase15ERealWindowLayoutTests.AssociateView_ProductsSection_NoControlClipsHorizontally` — unaffected
  by the Overview-only density changes; still passes at both sizes.
- `AssociateViewOperationalTimeLayoutTests` (Measure/Arrange-only, both sizes, every Manual Hours/
  Private Buyout enabled/disabled/warning combination) — unaffected by the merge (same controls, same
  bindings, tighter container only).
- `AssociateViewAccessibilityTests` — every previously-asserted `AutomationProperties.Name` still
  present (the merge changed containers, not controls); the new "PDF Style" `ComboBox` carries its own
  `AutomationProperties.Name`/`HelpText`.

These Window-realized tests share a documented, pre-existing cross-test-collection Avalonia headless
threading flake (unrelated to this phase — the same failure mode also hits `OperationsCalendarViewTests`,
`MainWindow`, `DashboardView`, `HistoryView`, and `RealWindowStartupTimingTests`, none of which this
phase touched); run in isolation (their own `dotnet test --filter` invocation) every one of them
passes cleanly at both 1280×860 and 960×640. DPI scaling (100%/125%/150%) could not be validated —
**NOT EXECUTED**, no Windows host/VM available this session (same standing limitation as every prior
phase).

## 13. Testing strategy

Semantic PDF-content inspection (PdfPig `ExtractAllText`/`CountPages`), never byte-equality, for live
(non-frozen) generation — matching the existing `AssociatePassdownPdfGeneratorTests` convention. One
exception, deliberately: comparing two independently-built `FinalizedPassdownSnapshot`s (a record with
`List<T>`-typed fields, which the compiler's synthesized record equality compares by reference, not
value) is done via `FinalizedPassdownSnapshotSerializer.Serialize(...)` string comparison rather than
`Assert.Equal` on the records directly — the same reason `ComputeContentHash` exists in production
code.

## 14. Known limitations

- Preflight's one-page-fit estimate (`AssociatePassdownPreflightService`) is not template-aware — it
  remains the same fixed line-budget estimate regardless of which template will actually render.
  This was already advisory-only before this phase (the real, authoritative page count is always the
  post-render count from `AssociatePassdownPdfGenerator`, which *is* template-accurate), so this is a
  pre-existing limitation carried forward, not a regression — making preflight template-aware was not
  necessary for any spec requirement in this phase.
- DPI scaling validation (100%/125%/150%) — NOT EXECUTED, no Windows host available.
- The native Avalonia `ComboBox` dropdown's own visual chrome was not screenshot-verified on a real
  Windows renderer (same standing headless-only limitation as every prior UI phase).

## 15. Phase 17D: approved master layout, branding/pricing cleanup, pagination fix

Baseline this phase was built against: Phase 17C complete (as documented above), DB schema 15, Draft
schema 4, Snapshot schema 2, SyncSchemaVersion 1, 2113/2113 deterministic tests (up from 2110/2110).

### 15.1 Why

Phase 17C gave every template a genuinely different visual treatment (Classic's price tiles/promo
cards, Compact's dense rows, Modern's two-column rail). In real use this proved to be more redesign
than the passdown needed: Classic/Modern's tile and rail treatments cost far more vertical space than
a plain aligned row for the same information, the app-branding header ("CAROWINDS OPERATIONS" /
"Associate Passdown") took real space to say something the reader already knows from holding the
paper, and the printed ERT/pricing carried information (start time, "was $X", raw qualifier text) an
associate does not need on a shift reference sheet. Phase 17D is a corrective pass: same architecture,
same three named templates, one shared content structure ("the approved master layout"), restyled per
template rather than restructured per template.

### 15.2 The approved master structure

Every template, every page, in this order:

```
{DAY, MONTH DATE, YEAR}                                    <- primary heading, no app branding above it
─────────────────────────────────────────────────────────
PARK HOURS        <headline>
North Gate        <time>                   Avenue          <time>
Attendance         <count or —>            Pre-solds       <count or —>
Early Ride         <rides, bullet-separated, or None/Not configured>

{FEATURE TYPE, e.g. "USELESS FACT OF THE DAY"}
──────────────────────────────────────────────────────────
{feature text}

{DAILY ADMISSION}                              {FAST LANE}
───────────────────                            ───────────────────
{rows: name ................ price}            {rows: name ... price}

{PROMOTIONS}                                   {SEASON PASSES}
───────────────────                            ───────────────────
{rows}                                         {rows}

{SCAROWINDS / ADD-ONS / CUSTOM-INTERNAL, each full width when present}
──────────────────────────────────────────────────────────
{rows}

OPERATIONAL NOTES
──────────────────────────────────────────────────────────
• {note}
• {note}

                                                    {Mon d} • {h:mm tt}
```

Implemented as `MasterProductLayout.Compose` (`Documents/Associate/Templates/MasterProductLayout.cs`,
new this phase) for the product-pairing/full-width part of this structure, called identically by all
three renderers; the header/summary-grid/Daily-Feature/Notes/footer are each renderer's own code
(same content and order, different typography per §15.7), not shared, since their visual treatment is
genuinely template-specific and there was little duplication to remove.

### 15.3 Product pairing (spec §12)

`MasterProductLayout` identifies a category by `PdfProductCategory.Section` (a new field this phase
added to that record — a nullable `ProductDisplaySection`, `null` only for the Custom/Internal
pseudo-category) — never by the category's display-name string, so pairing can never silently break if
a heading's wording changes. Daily Admission pairs with Fast Lane; Promotions pairs with Season Passes;
every other category (SCarowinds, Add-Ons, Custom/Internal, and anything not covered by an explicit
pairing) renders full width, in whatever order the categories already arrived in. A pairing with only
one side present renders that side alone at full row width (QuestPDF's own `RelativeItem` already
fills the row — no extra logic needed); a pairing with both sides absent contributes nothing.

### 15.4 What was removed from the PDF

- **App branding / document title** (spec §7): "CAROWINDS OPERATIONS" and "Associate Passdown" no
  longer print anywhere, in any template. The date is the page's primary heading, printed upper-case.
- **Comparison ("was $x") pricing** (spec §8): `PdfProductRow.ComparisonPriceText` is always `null`
  from `AssociatePassdownDocumentModelBuilder.BuildOfficialRow` onward — no renderer reads it any more,
  though the field itself stays on the record (and on `FinalizedProductRowSnapshot`) for schema
  compatibility with rows already frozen before this phase. Comparison pricing remains fully visible
  in the Products screen and product history — only the printed handoff stops carrying it.
- **Raw ecommerce qualifier text** (spec §9): "Only", "Online Price", "Sale Price", and any other raw
  `PriceQualifier` string no longer print — `PdfProductRow.Qualifier` is always `null` from the builder
  onward, for the same reason/compatibility shape as comparison pricing above. The one exception that
  materially changes the number's meaning, `PriceKind.StartingFrom`, is preserved by folding "From "
  into the price text itself (`AssociatePassdownDocumentModelBuilder.FormatPrice`) — "From $37" in the
  price column, never a separate qualifier line, matching spec §9's exact example row ("Military /
  First Responder   From $37").
- **Early Ride Time start time** (spec §10): see `docs/ert-configuration.md` §12 — rides only,
  bullet-separated, no time, no "Start" label, no explanatory note/reason line either (the approved
  master structure has no line for it).
- **Product tiles/cards/rail** (spec §12-14, §18): every product is one aligned row (name left, price
  right) in every template — no price tiles, no promo cards, no Modern rail/sidebar. `AdaptiveGridLayout`
  (the Phase 8B tile-grid row-chunking helper) was deleted; nothing replaced it, since nothing renders
  a grid of tiles any more.

### 15.5 Footer (spec §19)

`AssociatePassdownDocumentModelBuilder.FormatTimestamp` now produces `"Sep 15 • 1:52 PM"` (month/day,
bullet, time — no "Generated", no "at") instead of the old `"Aug 21 at 6:08 AM"`. Every renderer's
footer prints `model.GeneratedAtText` directly with no verb/prefix; the previously-shown "Generated "
prefix is gone from all three templates.

### 15.6 Pagination: the "isolated tiny page 2" fix (spec §16/§17/§25)

Before this phase's fix, a genuinely busy day (enough content that the last section barely didn't fit
page 1) could split Operational Notes mid-list — most notes on page 1, one or two orphaned notes alone
on an otherwise-empty page 2. Each renderer's `ComposeNotes` now wraps its notes block in QuestPDF's
`ShowEntire()`: if the whole Operational Notes block does not fit in the space remaining on the current
page, it moves *entirely* to the next page rather than splitting — the block that does land on page 2
is always the complete, real Operational Notes section, never a tiny fragment. The same wrapping was
applied to each renderer's Daily Feature block (always small/bounded, confirmed safe against a
realistic long-feature-text fixture during this phase's visual review — see §15.9).

**Product categories are deliberately NOT wrapped in `ShowEntire()`** — this was tried first and
reverted after it broke `Phase9EIntegrationTests.ExcessiveDraftContent_NoLongerBlocksScheduledFinalization_ProducesMultiPageFrozenPdf`:
a manager can add an unbounded number of custom/internal products, and `ShowEntire()` throws
`QuestPDF.Drawing.Exceptions.DocumentLayoutException` outright for any block taller than one entire
page — turning "a lot of custom products" into a hard generation failure instead of the required
graceful multi-page overflow (spec §17's "never truncate content" applies to *generation succeeding*,
not just to not dropping data within a successful generation). A product category is allowed to split
across a page boundary in the genuinely-excessive case; the specific "isolated tiny page 2" failure
this phase needed to fix was Operational Notes, not product categories, and the regression test added
(`TemplateOutputTests.ExcessiveDay_OperationalNotes_NeverSplitAcrossPages`) asserts exactly that scope.

### 15.7 Template visual definitions (current, supersedes §1's table)

| Template | Header | Body typography | Section heading | Notes |
|---|---|---|---|---|
| **Classic** | Large date + thin accent rule, no band | Medium (9-13pt), conservative | Small caps + thin hairline rule | Closest to the pre-17D feel in spacing/restraint, but no longer preserves Phase 17C's tile grid — spec §18's "no product cards, no app branding header" applies to Classic too. |
| **Compact** | Small date + ink rule | Smallest safe (6.5-9.5pt) | Small caps, no rule under Daily Admission/Fast Lane label row (space-saving) | Tightest spacing everywhere; the hours/attendance/ERT summary collapses onto 2-3 lines instead of a label/value grid. Meets or beats Classic's page count in every measured scenario. |
| **Modern** | Largest date (22pt) + crisp accent rule | 8-13.5pt, navy accent on prices | Small caps + accent-tinted hairline | No sidebar/rail, no giant dark header, no decorative cards (all explicitly removed this phase) — same single-column master structure as Classic/Compact, tuned to stay nearly as pagination-efficient as Compact despite the larger type scale. |

### 15.8 Document model additions

- `PdfProductCategory` gained `Section` (`ProductDisplaySection?`) — see §15.3.
- `AssociatePassdownDocumentModelBuilder.BuildOfficialRow`/`FormatPrice` — see §15.4's pricing rules.
- `PdfFormatting.FormatCount` now returns "—" (em dash) instead of "Not entered" for a null count, and
  gained `ErtLineText` (the one shared "rides only" ERT display-text rule every renderer calls, so the
  three templates can never drift on ERT wording).
- `AssociatePassdownPreflightService`'s line-budget estimate was recalibrated for the new one-row-per-product
  layout (previously estimated Key Selling/Promotional categories as multi-line tile/card grids via the
  now-deleted `AdaptiveGridLayout`) — still advisory-only, still not template-aware (§14's pre-existing
  limitation, unchanged).

No `PdfProductRow`/`FinalizedProductRowSnapshot`/`FinalizedEarlyRideTimeSnapshot` field was removed —
see §15.4/`docs/ert-configuration.md` §12 for why. Snapshot schema stays at 2; no DB/Draft migration.

### 15.9 Visual QA process used this phase

Spec §23 required visual review, not test-only sign-off. A throwaway console harness (outside the
solution, not committed) built representative `AssociateWorkspace` fixtures for all eight scenarios
spec §24 lists (normal day; busy product day; SCarowinds day; season-pass-heavy day; operational-notes
day; Add-Ons-enabled day; long Daily Feature day; ERT override day) plus a genuinely-excessive stress
scenario (40 custom products + 6 notes, the scenario that found the §15.6 pagination bug), generated
each through all three templates, and rendered the PDFs to PNG with Ghostscript
(`gs -sDEVICE=png16m -r150`) under `artifacts/pdf-preview/` (gitignored, not committed — matches spec
§23's "do not commit preview PNGs") for direct visual inspection. Every representative scenario
rendered as exactly one page except the genuinely-excessive stress scenario (Classic/Modern: 2 pages,
Compact: 1 page — consistent with §5's Phase 17C table, Compact still meeting or beating Classic).

### 15.10 Known limitations added this phase

- The visual QA harness (§15.9) is not a committed, re-runnable test fixture — it was a one-off tool
  for this phase's review pass. Any future phase touching template visuals should rebuild an equivalent
  harness rather than relying on this one having persisted.
- Real pixel-level screenshots of the Associate Overview *screen* (as opposed to the PDF output) were
  not obtainable in this environment — the test assembly's shared Avalonia headless setup
  (`HeadlessTestSetup.cs`) does not enable Skia/`GetLastRenderedFrame()`, and changing that shared,
  assembly-wide module initializer for one phase's visual check was judged too invasive/risky (it is
  used by all ~650 other UI tests). Structural real-`Window` layout tests (`Phase15ERealWindowLayoutTests`,
  `OperationsCalendarViewTests`, and friends — real `Measure`/`Arrange`, real control bounds, asserting
  no clipping/overlap) remain the available evidence, matching the precedent already set in Phase 17C
  §12/§14 for DPI scaling and native `ComboBox` chrome.

## 16. Phase 17E: genuinely distinct compositions

Baseline this phase was built against: Phase 17D complete (as documented above), DB schema 15, Draft
schema 4, Snapshot schema 2, SyncSchemaVersion 1, 2125/2125 deterministic tests.

### 16.1 Root cause of the Phase 17D similarity problem

Phase 17D's corrective pass (§15) fixed real content problems (branding, comparison pricing, ERT
time, pagination) but left all three templates sharing one structural algorithm,
`MasterProductLayout.Compose` (§15.2/§15.3): Daily Admission paired with Fast Lane on one row,
Promotions paired with Season Passes on a second row, everything else full width — called identically
by Classic, Compact, and Modern. Each renderer's summary block was also the same label/value grid
shape, just at different font sizes. The result: restyled type scales and colors on one shared
composition, not three different documents — exactly what spec Part 16 calls out as a failure
condition ("only font-size/padding differences").

### 16.2 What changed structurally

`MasterProductLayout` (paired, two-per-row) now belongs to **Compact alone**. A new
`SingleColumnProductLayout` (`Documents/Associate/Templates/SingleColumnProductLayout.cs`) — every
category full width, one after another, in the same fixed display order (Daily Admission, Fast Lane,
Promotions, Season Passes, then everything else in arrival order) — is used by **Classic and Modern**.
The two layouts never disagree on section *order*, only on whether two sections ever share a row; this
keeps the reading order (`TemplateOutputTests.EveryTemplate_ReadingOrderIsSingleColumn...`) identical
across all three despite the compositional difference. `SingleColumnProductLayout.Compose` returns how
many categories it rendered, so Modern can continue its running section-number gutter from the last
product category into Operational Notes without either renderer needing to recompute the other's
ordering logic.

### 16.3 Classic — operations sheet (single column)

- **Top summary**: fully vertical label/value list (`ComposeSummaryList`) — Park Hours headline, then
  North Gate, Avenue, Attendance, Pre-solds, and Early Ride each on their own row under a consistent
  ~100pt label column. Previously (Phase 17D) these were paired two-per-row, visually closer to
  Modern's later grid; Phase 17E's fully vertical stack is what a reviewer now reads as "traditional
  handout" at a glance, and is the one thing in this phase captioned literally "PARK HOURS" (Compact
  says "HOURS"; Modern has no caption at all — `TemplateCompositionTests.TopSummaryCaption_DiffersByTemplate`).
- **Products**: `SingleColumnProductLayout`, one category per row, conservative gray/blue hairlines
  under each category heading, moderate row padding.
- **Daily Feature / Notes**: unchanged from Phase 17D — plain heading + hairline, plain bullets.
- **Typography**: 9.75pt body, 19pt date — restrained, no letter-spacing tricks.

### 16.4 Compact — dense two-column (unchanged structurally, retuned)

Compact keeps `MasterProductLayout`'s pairing — this is now Compact's *defining* trait rather than
something all three templates happened to share. Top summary stays the tight inline strip
(`ComposeSummaryStrip`, 2-3 lines total) introduced in Phase 17D; body/row typography and spacing were
left essentially as-is, since Compact was already the tightest of the three and remains the density
benchmark this phase measures the other two against (§16.7).

### 16.5 Modern — editorial/Swiss hybrid (single column, numbered)

The largest rewrite. Still single column (via `SingleColumnProductLayout`, sharing Classic's
non-pairing trait) but composed nothing like Classic:

- **Header**: a large (23pt), letter-spaced, bold date under a thicker accent rule than Classic's.
- **Top summary**: an editorial metadata grid (`ComposeSummaryGrid`/`MetadataColumn`) — Park Hours
  headline on its own line, then North Gate/Avenue/Attendance/Pre-solds as four true side-by-side
  columns (uppercase label above a larger bold value in each), then a dedicated Early Ride block
  (label above value, not inline). Genuinely different shape from Classic's vertical list and
  Compact's inline strip — proven structurally, not just visually, by
  `TemplateCompositionTests.TopSummaryCaption_DiffersByTemplate`.
- **Numbered sections** *(removed in Phase 17F — see §17.2)*: every product category and Operational Notes gets a small accent-colored
  gutter number (`NumberedSectionLabel` — "01", "02", ...), continuing sequentially from the last
  product category into Notes. Classic and Compact never print a numbered gutter at all
  (`TemplateCompositionTests.ClassicAndCompact_NeverPrintNumberedSectionGutters`). Daily Feature stays
  unnumbered (a stronger editorial heading than Classic/Compact, but not part of the numbered system)
  — the mockup's own "target structure" leaves it unnumbered too.
- **Typography**: 9.25pt body, restrained navy accent on prices and gutter numbers, no sidebar/rail, no
  shaded cards — the Phase 17D §18 constraints ("no giant dark header", "no decorative cards") still
  hold, they just now sit on a composition that is not Classic-with-bigger-type.

### 16.6 Shared business-model boundary (unchanged)

`AssociatePassdownDocumentModel` (§3) is untouched by this phase. Every renderer still receives the
exact same `ProductCategories`, `EarlyRideTime`, `OperationalNotes`, and pricing text the builder
already resolved; `SingleColumnProductLayout`/`MasterProductLayout` only decide **whether two
categories share a row**, never which products exist, which tier a category is, or how a price is
worded. `FinalizedPassdownSnapshotBuilderTemplateTests.BuildProjection_EveryTemplate_ProducesTheIdenticalSnapshot`
(unchanged, still passing) continues to prove the model itself never diverges by template.

### 16.7 Pagination philosophy

Compact remains the density benchmark — every representative scenario and the pagination stress
scenario below still shows Compact meeting or beating Classic
(`RepresentativePageCountTests.EveryScenario_GeneratesSuccessfullyUnderEveryTemplate_AndNeverDropsContent`'s
`Compact <= Classic` assertion, now exercised over ten scenarios instead of six — see §16.9). Classic's
move to a single-column product layout inherently costs more vertical space than the Phase 17D paired
layout it replaced (a category that used to share a row with its pairing partner now takes a full row
of its own); row padding inside `ClassicAssociatePassdownTemplateRenderer.ComposeCategory` was
retightened (2pt → 1.5pt top/bottom) specifically to keep the busy-product-day scenario
(`AssociateWorkspaceFixtures.DenseDay`) on one page under Classic, matching the pre-existing
`AssociatePassdownPdfGeneratorTests.RealisticWorkspaces_RenderExactlyOnePage` guarantee. Modern made
the identical structural move (paired → single column) and absorbs it the same way its Phase 17D
tuning already did — staying close to Classic's page count rather than Compact's. The one scenario that
still spills to two pages under Classic/Modern (many operational notes + heavy products — the same
stress fixture Phase 17D called "genuinely excessive") produces a fully-populated second page in both
cases (verified visually — §16.8), never an isolated near-empty one; Compact still renders it on a
single page.

### 16.8 Visual review process used this phase

Spec Part 13 required visual review, not test-only sign-off. A throwaway console harness (outside the
solution, not committed — same approach as Phase 17D §15.9, since the prior phase's own harness was
explicitly not persisted) built the eight representative `AssociateWorkspace` fixtures spec Part 14
lists (normal day; busy product day; season-pass-heavy day; SCarowinds day; operational-notes day;
Add-Ons-enabled day; long Daily Feature day; minimal/light day) plus the Phase 17D "genuinely
excessive" stress fixture, generated each through all three templates, and rendered the PDFs to PNG
with Ghostscript (`gs -sDEVICE=png16m -r150`) under `artifacts/pdf-preview/` (gitignored, not
committed). Every representative scenario's three templates were inspected side by side per spec Part
15: Classic, Compact, and Modern are immediately distinguishable at a glance in every scenario checked
— vertical list vs. paired two-column grid vs. numbered editorial grid — with no shared top-summary
shape, no shared product-grid shape, and no reintroduced branding/comparison-pricing/ERT-time clutter.
The stress scenario's page 2 was inspected explicitly under Classic and Modern to rule out spec Part 16's
"isolated tiny page 2" failure mode — both land a fully-populated page 2 (remaining Season Passes rows,
all Add-Ons rows, and every Operational Note), not a stranded fragment.

### 16.9 Representative page-count results (this phase)

| Scenario | Classic | Compact | Modern |
|---|---|---|---|
| A. Light day | 1 | 1 | 1 |
| B. Normal day | 1 | 1 | 1 |
| C. Busy product day | 1 | 1 | 1 |
| D. Many notes (stress) | 2 | **1** | 2 |
| E. ERT override day | 1 | 1 | 1 |
| F. NoERT day | 1 | 1 | 1 |
| G. Season-pass-heavy day | 1 | 1 | 1 |
| H. SCarowinds day | 1 | 1 | 1 |
| I. Add-Ons enabled day | 1 | 1 | 1 |
| J. Long Daily Feature day | 1 | 1 | 1 |

Identical to the Phase 17D baseline on every scenario the two phases share — moving Classic and Modern
to a single-column product layout did not cost either of them a page on any of the eight new
representative scenarios or the two original stress/busy scenarios, only on the deliberately excessive
stress fixture (unchanged from Phase 17D: Classic/Modern 2 pages, Compact 1).

### 16.10 Tests added

`tests/Carowinds.Passdown.Documents.Tests/Associate/Templates/TemplateCompositionTests.cs` (new) —
structural proof, not just semantic content, that the three templates differ:

- `Compact_DailyAdmissionAndFastLane_ShareTheSamePhysicalRow` / `Classic_..._RenderOnSeparateRows_NoPairing`
  / `Modern_..._RenderOnSeparateRows_NoPairing` — real glyph Y-positions (`PdfInspector.FindWordTop`,
  new) prove pairing vs. single-column composition from rendered coordinates, not merely from linear
  text order (which paired and stacked layouts could otherwise produce identically).
- `Modern_NumbersSectionsSequentially_ThroughProductsAndIntoOperationalNotes` /
  `ClassicAndCompact_NeverPrintNumberedSectionGutters` — the numbered/accented section system is
  Modern-only.
- `TopSummaryCaption_DiffersByTemplate` — Classic's "PARK HOURS", Compact's "HOURS" (never the longer
  phrase), and Modern's absence of either caption are mutually exclusive per template.
- `AllThreeTemplates_AreStructurallyDistinctFromOneAnother` — combines the pairing signal across all
  three in one assertion, the direct regression test for the Phase 17D failure mode this phase fixes.

`AssociateWorkspaceFixtures` gained four fixtures (`SeasonPassHeavyDay`, `ScarowindsDay`,
`AddOnsEnabledDay`, `LongDailyFeatureDay`) feeding four new `RepresentativePageCountTests` scenarios
(§16.9's G-J), so the `Compact <= Classic` pagination guarantee is now exercised over every spec Part
14 scenario, not just the original six.

### 16.11 Regression

- 17A product semantics, 17B rides-only ERT, 17D PDF content cleanup (no branding, no comparison
  pricing, no ERT time) — unchanged, still enforced by the unmodified Phase 17D tests, all still
  passing.
- Personal Sync, finalized History immutability, template selection/preference persistence — untouched
  by this phase (no file outside `Documents/Associate/Templates/*` and its own tests was changed).
- `AssociatePassdownPdfGeneratorTests.RealisticWorkspaces_RenderExactlyOnePage` (Classic-default,
  `DenseDay` included) still passes — this is what drove the §16.7 Classic row-padding retightening.

### 16.12 Schema impact

Unchanged: DB schema 15, Draft schema 4, Snapshot schema 2, SyncSchemaVersion 1. This phase touched
only renderer composition (`Documents/Associate/Templates/*`) and tests/docs — no model, persistence,
or sync code.

### 16.13 Known limitations

- The visual QA harness (§16.8) is not a committed, re-runnable test fixture, matching the Phase 17D
  §15.10 precedent and for the same reason — it is a one-off review tool, not a regression guard (the
  new `TemplateCompositionTests` structural tests are the committed regression guard instead).
- As in Phase 17D, real pixel-level screenshots of the Avalonia *screen* (as opposed to PDF output)
  remain unavailable in this environment (§15.10); this phase does not touch the Desktop/Avalonia
  project at all, so that limitation is unchanged and not newly relevant here.
- Preflight's one-page-fit estimate remains not template-aware (§14's pre-existing limitation,
  unchanged again this phase).

## 17. Phase 17F: visual refinement, Modern numbering removed

Baseline: Phase 17E complete plus the Dashboard load-recovery fix (`399d40c`), DB schema 15, Draft
schema 4, Snapshot schema 2, SyncSchemaVersion 1, 2150/2150 deterministic tests. Presentation-only:
only `Documents/Associate/Templates/*`, `AssociatePassdownPdfGenerator`, and Documents tests changed.
No model, business-rule, persistence, sync, Desktop, or Dashboard code was touched.

### 17.1 Final purpose of each template

| Template | Purpose | Identity at a glance |
|---|---|---|
| **Classic** | Traditional, professional operations handout; easiest to annotate by hand | One-line date over a full-width ink rule; "PARK HOURS" caption + vertical label/value list; every section = uppercase heading over a full-width thin rule; single-column rows; moderate whitespace; near-black only. |
| **Compact** | Maximum useful density; the one-page benchmark | Small date over a heavy rule; three-line inline summary ("HOURS … NG … AVE …" / "ATTENDANCE … PRE-SOLDS …" / "EARLY RIDE …"); paired two-column product sections; "NOTES"; smallest safe type/spacing; one thin rule per heading, no row rules. |
| **Modern** | Clean editorial, high-hierarchy internal document | Two-line date (letter-spaced weekday over a large month/day/year) over a heavy rule; uncaptioned hours headline; four-column NORTH GATE / AVENUE / ATTENDANCE / PRE-SOLDS metadata grid; separate EARLY RIDE block; strong letter-spaced headings marked by a short accent rule (not full-width); single-column rows on a narrower editorial measure; notes as spaced paragraphs. |

### 17.2 Modern numbering removal

The Phase 17E gutter numbers ("01 DAILY ADMISSION", …, "0N OPERATIONAL NOTES") are gone from
Modern; nothing replaces them — no letters, numerals, icons, bullets, or badges.
`SingleColumnProductLayout.Compose` no longer passes a running index to the renderer. Modern stays
distinct from Classic through structure that tests can see, not numbering:

- **date**: two lines (Modern) vs one line (Classic/Compact): `DateHeading_IsTwoLinesOnlyInModern`
- **summary**: four labels on one row (Modern grid) vs a vertical list (Classic): `Modern_MetadataGrid_FourLabelsShareOneRow_WhileClassicStacksThem`
- **rules**: Modern has a short accent rule under each heading, with a full-width hairline only under
  the Daily Feature (closing the operations block). Classic has a full-width rule under every heading.
- **measure**: Modern product rows sit on a 400pt measure with an open right margin. Classic rows
  run the full page width.

### 17.3 Operations vs commercial hierarchy

Hours, gates, attendance, pre-solds, Early Ride, and the Daily Feature form the operations block.
The product sections that follow are the commercial reference. The two are separated only by extra
vertical space (Classic +2pt, Modern +3pt at standard density). Compact separates them by its paired
grid alone. No containers.

### 17.4 Price alignment and long names

All three templates use one shared row primitive, `PdfPrimitives.PriceRow`: a flexible, wrapping name
column plus a fixed-width, right-aligned price column (Classic 72pt, Modern 76pt, Compact 52pt per
column). The rules:

- Every price in a section shares one right edge, including "From $X".
- Classic and Modern share one price column across all sections on the page.
- A long name wraps at the normal type size inside its own column, never under the price.
- The price stays on the name's first line.
- The type is never shrunk to avoid wrapping.

Compact change: a pair member whose partner is absent now stays at column width in the left column,
so its prices line up with the paired sections; it no longer stretches to full width. When both
pairings have exactly one member (for example Daily Admission plus Season Passes only), the two lone
sections share one row. Catalog names are never modified. No PDF display aliases exist in the current
presentation layer, so Compact prints the model's names unchanged ("Family Pack" and "Gold" style
shortenings were **not** introduced).

### 17.5 Pagination and orphan control

- **Keep small sections whole.** `PdfPrimitives.KeepSection` gives a section with 10 rows or fewer
  QuestPDF's `PreventPageBreak`: if it would split, the whole section moves to the next page. Unlike
  `ShowEntire`, it never throws for content taller than a page.
- **Let large sections flow.** A larger section gets `EnsureSpace` instead, so its heading always
  lands with its first rows and the rest flows naturally. This avoids half-empty pages.
- **What each rule covers.** The rules apply to every product section (Compact applies them per pair
  row) and to Operational Notes. Notes no longer use `ShowEntire`, so a very long notes list can no
  longer throw. Daily Feature uses `PreventPageBreak`.
- **Compress before paginating.** The `IAssociatePassdownTemplateRenderer.Compose` signature now takes
  a `PdfDensity` (`Standard`/`Tight`). `AssociatePassdownPdfGenerator` renders at `Standard`. Only if
  that spills past one page does it re-render at `Tight`, which shrinks spacing only: same content,
  same composition, same type sizes. The tighter result is kept only if it saves a page. Single-page
  days render once. Output stays deterministic for a given (model, template).
- **Dynamic note density.** Notes spacing tightens once a list has more than three notes.

### 17.6 Footer

The footer is the timestamp only ("Sep 15 • 3:25 PM"), plus "k / N" at the right **only when the
document has more than one page**. QuestPDF's `ShowIf(ctx => ctx.TotalPages > 1)` resolves this from
the real rendered total. The footer never shows an app name, template name, filename, or machine
name. The existing "Some product pricing has not been recently verified." note keeps its left-hand
slot, unchanged.

### 17.7 Daily Feature, notes, Add-Ons, special states

- **Daily Feature**: the feature type stays the heading. Classic puts a full-width rule under it,
  Compact sets the heading tight over the body, and Modern uses a strong heading with a hairline and
  editorial spacing. Never a card.
- **Notes**: Classic has readable bullets. Compact has tight bullets under "NOTES". Modern has
  unbulleted paragraphs with more line spacing than Compact.
- **Add-Ons**: still printed only when the model carries the category. Classic and Modern render it as
  a normal single-column section; Compact renders it full width.
- **Special states**: Closed, Manual Override, and Private Buyout states print the model's existing
  headline/badge in small italic beside the hours. There are no banners and no new logic.

### 17.8 Visual QA (this phase)

A throwaway harness outside the solution (not committed, per the §15.10/§16.13 precedent) built
`AssociatePassdownDocumentModel`s directly for scenarios A–J below. It generated each scenario under
all three templates and rasterized every page with Ghostscript in **grayscale**
(`gs -sDEVICE=pnggray -r80`, plus spot checks at `-r150`). The pages were inspected side by side and
individually, revised, and re-rendered. Fixes driven by the review:

- The busy day originally stranded three notes alone on page 2 under Classic/Modern. The tight
  density retry now fixes this.
- Compact's lone Promotions section stretched full width, misaligning its prices.
- Compact's partial-pair day left a blank right half.
- Modern's prices sat at the far page edge. It now uses the narrower measure.

Grayscale: hierarchy is carried by weight, size, letter-spacing, and rules. Modern's navy accent rule
and weekday read as solid mid-gray. Nothing depends on color.

| Harness scenario | Classic | Compact | Modern |
|---|---|---|---|
| A. Normal day | 1 | 1 | 1 |
| B. Busy product day (24 rows + 3 notes) | 1 (tight) | 1 | 1 (tight) |
| C. Minimal/light day | 1 | 1 | 1 |
| D. SCarowinds day | 1 | 1 | 1 |
| E. Season-pass-heavy day | 1 | 1 | 1 |
| F. Operational Notes day (9 notes) | 1 | 1 | 1 |
| G. Long Daily Feature | 1 | 1 | 1 |
| H. Add-Ons enabled | 1 | 1 | 1 (tight) |
| I. Long product names | 1 | 1 | 1 |
| J. Stress (all sections + 30 custom + 9 notes) | 2 | 2 | 2 |

Committed fixture counts (`RepresentativePageCountTests`) are unchanged from §16.9: every scenario is
1/1/1 except D. Many notes, which is 2 / **1** / 2. Compact still meets or beats Classic everywhere.
In stress cases, page 2 is always a substantial continuation (remaining sections plus the full notes
list), never a stranded heading or a lone note fragment.

### 17.9 Tests

New `Templates/Phase17FTemplateRefinementTests.cs` (glyph-position proofs via the new
`PdfInspector.FindWordBox`/`Words`) covers:

- no Modern numbering, letters, or numerals beside its headings
- the two-line date appears only in Modern
- Modern's metadata grid vs Classic's vertical list
- Classic's single column; Compact's pairing; two lone pair members sharing one row
- one price right edge per section, including "From"; one price column across sections in
  Classic/Modern; lone-pair alignment in Compact
- long-name wrapping with a first-line price
- absent sections printing nothing
- the single-page footer has no page indicator; on multi-page output, every page shows
  "k / N" plus the timestamp
- a just-overflowing day recovered to one page
- no section heading stranded at a page bottom

Updated tests:

- `TemplateCompositionTests`: the Modern-numbering test is replaced by
  `NoTemplate_PrintsNumberedSectionGutters`.
- `TemplateOutputTests` and `TemplateRendererSelectionTests`: date and notes-heading assertions are now
  template-aware via `PdfTextAssert.ContainsOperatingDate`/`NotesHeading`.

Finalized-PDF immutability, snapshot identity across templates, and template preference tests are
unchanged and passing.

### 17.10 Known limitations

- The QA harness is not committed (same as §15.10/§16.13).
- The orphan check is "heading never ends a page", not full typographic widow/orphan control inside
  a wrapped line.
- Compact display aliases were not added: none exist in the presentation layer, and inventing them
  was out of scope.
- The tight-density retry doubles render time for documents that overflow at standard spacing (only
  those).
- Preflight remains template-unaware (§14).

