# Pricing semantics vocabulary

Authoritative vocabulary for the distinct kinds of "price" this app encounters across official
Carowinds/Six Flags sources, and how each must flow from retrieval through domain/persistence,
presentation, and PDF/History. Written during Phase 13A's audit (see
`docs/retrieval/catalog-source-inventory.md`, `docs/retrieval/dated-admission-source.md`) as a
documentation-only artifact — it defines terms and a precedence rule for future work, it does not
change any source file. Every field/type named below already exists in code as cited; nothing here
introduces a new field.

## Why this doc exists

Manual testing surfaced a real risk that this vocabulary is meant to prevent: a generic,
non-date-specific "Starting From" catalog price being presented as if it were the exact price for a
specific selected date. The two worked examples at the bottom of this doc are the concrete cases
that motivated writing this down now, before any adapter that could actually trigger the collision
is built.

## Definitions

### EXACT DATED PRICE

The price actually charged for one specific calendar date, as confirmed by an official source that
is itself date-scoped (e.g. the accesso `GetKeywordCalendar` API `DatedAdmissionSource` calls,
`src/Carowinds.Passdown.Infrastructure/Retrieval/Sources/DatedAdmissionSource.cs`). Represented
today by `ObservedProduct.CurrentPriceAmount`/`CurrentDisplayPrice` (or
`DatedAdmissionProduct`'s price) **combined with** `DateSpecific = true` and a non-null
`ApplicableDate` equal to the selected date. An exact dated price is never a marketing floor or
aggregate — spec §5.4 already forbids reading the calendar response's `lowest_price` field for
exactly this reason (see `DatedAdmissionSource.cs`'s remarks on `BuildProducts`), and this
definition generalizes that rule to any future dated source: a value only counts as an exact dated
price if it was read from a response entry whose own date field was confirmed to equal the
requested date, never inferred or carried over from an adjacent date or a page-level aggregate.

### STARTING FROM

A non-date-specific floor/aggregate price, always paired with an explicit qualifier string
(`PriceQualifier = "Starting From"`, e.g. `fixtures/promotions/published-page.html:23`). Represented
by the same `CurrentPriceAmount`/`CurrentDisplayPrice` fields as any other catalog price, with
`DateSpecific = false`. A "Starting From" value describes the cheapest observed variant of a
product family across dates/options, not what any specific date costs — it must never be presented
in a context that implies it is the price for a particular selected date. Today this qualifier
survives to the PDF only as free text (`AssociatePassdownDocumentModelBuilder.BuildOfficialRow`,
lines 217-222) with no distinct visual or structural treatment from an exact price; that is a real
gap this doc calls out but does not fix.

### STANDARD / CATALOG PRICE

The regular, non-promotional, non-date-specific price for a catalog product family (season passes,
add-ons) as currently listed on its official catalog page. Same fields as above
(`CurrentPriceAmount`/`CurrentDisplayPrice`, `DateSpecific = false`), with `PriceQualifier` either
null or a neutral label. This is the baseline "what does this cost normally" figure a
`CatalogStable`-bucket product carries between refresh cycles (see
`docs/retrieval/catalog-source-inventory.md`, `FreshnessPolicyCatalog.cs:35-40`'s rolling-interval
cadence for `SeasonPasses`/`AddOns`).

### SALE / CURRENT PRICE

The price a customer would pay right now for a catalog product that is currently discounted from
its standard price. Represented identically to Standard/Catalog Price
(`CurrentPriceAmount`/`CurrentDisplayPrice`), distinguished only by the co-present
`ComparisonPriceAmount` and/or a `PriceQualifier` of `"Sale Price"` (e.g. "2027 Gold Pass" — $89
now, $115 comparison, `fixtures/promotions/published-page.html:44-47`). There is no separate field
marking "this is currently on sale" — it is inferred from the presence of a comparison price, which
means a sale with no stated comparison price (if one is ever observed) would be indistinguishable
from a standard price under today's model.

### COMPARISON / WAS PRICE

The higher, "was"/reference price shown alongside a current price to convey savings. Represented by
`ComparisonPriceAmount` (`decimal?`) on `ObservedProduct`, `AssociateProduct`, and
`DatedAdmissionProduct`. Important existing gap: the raw scraped display string for this price
(`ParsedProductCard.ComparisonDisplayPrice`, e.g. `"$80 Gate Price"`) is parsed down to a bare
decimal by `CatalogCardMapper.ToObservedProducts`
(`src/Carowinds.Passdown.Infrastructure/Retrieval/Sources/CatalogCardMapper.cs:62-70`) and its own
wording (here, the "Gate Price" suffix, distinct from the card's main `"Starting From"` qualifier)
is discarded — only the numeric amount survives. The PDF's comparison price is therefore always a
freshly formatted `$80.00`, never the source's own phrasing. This doc treats that as acceptable for
numeric comparison purposes but flags it as a known, deliberate simplification, not an oversight to
silently rely on for anything beyond "what was the higher number."

### PER-PERSON PRICE

The price attributable to one person within a multi-person bundle (e.g. BAF Duo's "$25 per
friend" within a $50-for-two total). **Not represented anywhere in the current model.** No field on
`ObservedProduct`, `AssociateProduct`, `DatedAdmissionProduct`, or any PDF row type expresses a
per-unit/per-person amount distinct from the bundle's total price. Any future source that carries
this semantic needs a new, explicitly-named field (not a repurposed `ComparisonPriceAmount` or
`PriceQualifier` string) — see the BAF Duo worked example below.

### BUNDLE PRICE

The single total price for a package containing more than one component (multiple people, or a
ticket plus an add-on). Represented today only nominally, through the product's own display name —
e.g. `"Ticket + Meal, Snack & Drink Bundle"` and `"Ticket + Fast Lane Reserve Bundle"` as distinct
named entries in `fixtures/dated-admission/published-window.json`, each carrying its own ordinary
`CurrentPriceAmount`. There is no structured `IsBundle`, `ComponentCount`, or `PerUnitPrice` field —
"this price covers N things" is only ever conveyed by prose in `DisplayName`, which is fine for
display but not queryable/branchable by future logic (e.g. a hypothetical "flag all bundle prices
for extra verification" rule would have to string-match display names today).

## Retrieval → domain/persistence → presentation → PDF/History flow

1. **Retrieval**: an adapter reads a source and produces plain values — a decimal amount, a display
   string, an optional comparison amount, an optional qualifier string, and (for date-scoped
   sources) a confirmed `ApplicableDate` matched against the actually-requested date. Adapters must
   never synthesize a qualifier or an applicable date that the source didn't actually provide for
   the specific value being read (already the rule behind `DatedAdmissionSource`'s null
   `PriceQualifier` and `PromotionsCatalogSource`'s unpopulated per-product validity field).
2. **Domain/persistence** (`ObservedProduct` → reconciliation → stored catalog product state): the
   decimal amount, display string, comparison amount, qualifier, and date-specificity flag/date are
   carried through unchanged. Reconciliation identity (`CatalogProductKey`) is scoped by
   `SourceCategory`, so values from two different categories are never merged or allowed to
   overwrite one another automatically — any future precedence decision between two sources
   describing "the same" real-world product must be made explicitly by a caller, not left to
   identity collision.
3. **Presentation** (`AssociateProduct`): the same fields flow through 1:1
   (`OfficialPriceAmount`/`OfficialDisplayPrice`/`ComparisonPriceAmount`/`PriceQualifier`/
   `DateSpecific`/`ApplicableDate`), plus the draft-only `DisplayPriceOverride` and computed
   `EffectiveDisplayPrice`. A manual override always wins over the official price
   (`EffectiveDisplayPrice => DisplayPriceOverride ?? OfficialPriceAmount`), by existing design —
   this doc does not change that.
4. **PDF/History** (`PdfProductRow` via `AssociatePassdownDocumentModelBuilder.BuildOfficialRow`):
   `EffectivePriceText`, `ComparisonPriceText`, and `Qualifier` are formatted for print. Today the
   qualifier is rendered as plain text with no distinction between an exact price and a
   "Starting From"/estimate — any future work that wants to visually distinguish these should treat
   `DateSpecific` (not the free-text `PriceQualifier`) as the authoritative signal for "is this an
   exact price for the passdown's selected date," since `PriceQualifier` is uncontrolled source
   prose and must not be string-matched to make that determination.

## Source precedence for date-sensitive products (proposal — not implemented)

When more than one official source could describe the same real-world product for the currently
selected passdown date, the precedence is:

1. **Exact selected-date official value** — a value with `DateSpecific = true` and
   `ApplicableDate` equal to the selected date always wins, regardless of what any catalog/starting-
   from value for the same or a related product name says.
2. **Non-date-specific catalog value** — used only when no exact-date value exists for the selected
   date. Its qualifier (e.g. "Starting From") must be retained and shown, never silently dropped, so
   whoever reads it can tell it is not date-exact.
3. **Last-known-good / stale value** — used only when the current retrieval attempt itself failed;
   its stale/unverified status must be retained and surfaced (existing `RetrievalStatus`/
   `AssociateVerificationInfo` machinery already carries this at the source-evidence level).

This ordering is not implemented as a merge/comparison rule anywhere in code today — as noted above,
`CatalogProductKey`'s category-scoped identity means two sources' values for what a human would call
"the same product" simply become two separate rows today, so no collision is currently possible
*mechanically*. This precedence is written down so that whenever a future source is added that could
plausibly describe the same product family as an existing one (e.g. a dated Bring-A-Friend keyword
alongside the existing catalog Bring-A-Friend card), the implementer applies this ordering
deliberately rather than by accident of whichever source happens to run or persist last.

## Worked example 1 — Bring-A-Friend precedence regression (Aug 29 2026)

Manual testing observed, for Saturday August 29, 2026: an exact dated-ticketing value for "Season
Pass Bring-A-Friend" of **$39.00** (comparison/"was" **$80.00**), while the generic catalog page
(`PromotionsCatalogSource`, backed by `fixtures/promotions/published-page.html`) exposes a
"Bring-A-Friend Ticket" card at **"Starting From" $35** (comparison **$80 Gate Price**) that is not
specific to any date. These are not the same value, and it is not a coincidence that they're close —
$35 is the floor across all dates/conditions, while $39 is what Aug 29 specifically costs.

Under this doc's precedence rule: for a passdown whose selected date is Aug 29, 2026, the $39 exact
dated value must win outright. The $35 "Starting From" value must never be substituted for it, must
never overwrite it once retrieved, and if the exact-date source is temporarily unreachable, the
correct fallback is the last-known-good *exact-date* value (if one was previously confirmed for this
date) or an explicit "not currently available" state — never a silent drop-down to the generic $35
catalog floor, which would misrepresent a lower, non-applicable price as if it were today's actual
rate. This is exactly the "never substitute another date['s or any non-applicable] value" rule
CLAUDE.md's retrieval rules and spec §5.4 already establish for dated products generally; this
example exists to make the Bring-A-Friend case concrete since it is the one where two structurally
similar-looking but semantically different values ($35 catalog floor vs. $39 exact date) are most
likely to be confused by a future implementer.

## Worked example 2 — BAF Duo bundle/per-person pricing

The "Celebrate America! Bring-A-Friend Duo Offer" observed during manual testing ($50 for 2 friends,
$25 per friend) is a **bundle price with an associated per-person breakdown** — two semantics this
doc defines above as currently unrepresented in the model (Per-Person Price, and Bundle Price beyond
a display-name convention). A future adapter/domain change for this product must not collapse "$50
for 2" and "$25 per friend" into a single ambiguous `CurrentPriceAmount`+`ComparisonPriceAmount`
pair — that shape is for a current-price/was-price pair, not a total/per-unit pair, and reusing it
would make a PDF row print "$50, was $25" (backwards and meaningless) if the two amounts were merely
dropped into the existing two price fields in the wrong order, or "$50, was $80" if the true
comparison price were dropped entirely to make room. Whatever concrete field(s) a future phase adds
for this, they must be named and typed distinctly from `ComparisonPriceAmount` (e.g. a genuine
per-person amount alongside the bundle total, both alongside their own optional comparison price) so
that "total for N people" and "was $X" are never conflated.
