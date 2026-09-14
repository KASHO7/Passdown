# Associate Passdown PDF templates — Phase 17C

Adds three selectable Associate Passdown PDF visual templates (Classic/Compact/Modern) and a density
pass over the Associate Overview UI. Baseline this phase was built against: Phase 17B complete, DB
schema 15, Draft schema 4, Snapshot schema 2, SyncSchemaVersion 1, 2059/2059 deterministic tests.

## 1. The three templates

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
