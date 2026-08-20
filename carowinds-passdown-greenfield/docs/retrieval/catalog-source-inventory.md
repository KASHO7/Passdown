# Catalog source inventory and retrieval foundation

Live investigation performed from this dev host on 2026-08-20. This documents what was actually
observed against the official, non-date-specific Carowinds catalog pages — nothing below is
guessed or carried over from the dated sources (Phase 3C/3D/3E) without being independently
re-verified. Fetched with plain `fetch()`/HTTP GET only (no Playwright) — see "Browser required?"
below for why that was sufficient here, unlike `cf-ca.buy.sixflags.com`'s checkout flows.

## Part A — source inventory

| Category | Official URL | Retrieval method | Structured data source | Browser required? | Stable product id? | Price fields | Qualifier fields | Validity fields | Known limitations |
|---|---|---|---|---|---|---|---|---|---|
| Season passes | `sixflags.com/carowinds/season-passes` | Plain HTTP GET, HTML | Server-rendered `productv2-card` markup (Next.js/Sanity page, see below) | No | Yes — `data-ab-product` (Sanity doc id) and `data-accesso-keyword` both present | Current + comparison (`productv2-card-price`, `-price-strikethrough`) | Yes — `productv2-card-price-before` (e.g. `"Sale Price"`) | No explicit per-product field found (see below) | Keyword embeds a plan year (`FallGoldSP2027`) — see "Limitations" |
| Daily-ticket marketing page | `sixflags.com/carowinds/daily-tickets` | Plain HTTP GET, HTML | Same `productv2-card` markup | No | Yes, same mechanism | Current + comparison where shown | Yes (`"Sale Price"`, `"Starting From"`, and one bespoke `"Two Friends Just"`) | None found | **Not a catalog category of its own** — its dated-ticket price ("Starting From $39") is marketing/aggregate, never exact-date (already correctly avoided in Phase 3C, which uses the accesso calendar API instead); investigated only to confirm the shared card mechanism and re-confirm this warning |
| Promotions / short-term bundles | `sixflags.com/carowinds/special-offers`, `sixflags.com/carowinds/biggest-sale-of-the-year` | Plain HTTP GET, HTML | Same `productv2-card` markup, plus a distinct `promoBar` Sanity component for the site-wide countdown banner | No | Yes, same mechanism | Current + comparison where shown | Yes | **Yes, but only at the promo-bar level** — `"dateWithTimezone":{"date":"2026-09-08T03:59:00.000Z",...}` was found on the promo-bar component (matches the page's own "Sale Ends 9/7" text). Not found as a field on any individual product card | Promo-bar validity date is not demonstrably linked to a specific product's price — see Part C |
| Fast Lane marketing page | `sixflags.com/carowinds/fast-lane` | Plain HTTP GET, HTML | Same `productv2-card` markup | No | Yes, same mechanism | Current + comparison where shown | Yes | None found | Carries both a dated product (`FastLaneOnly`, already Phase 3D's scope) and a catalog product (`27AddonsFastlane`, "All Season Fast Lane," $899→$499) on the same page |
| General add-ons | `sixflags.com/carowinds/add-ons` | Plain HTTP GET, HTML | A **different, simpler** card component (no `productv2-card` wrapper, no `data-ab-product`) | No | Partial — `data-accesso-keyword` present, no Sanity doc id observed | Current only — no comparison-price element observed on any card checked | No qualifier element observed (plain "Only $19.99" text, no `productv2-card-price-before`-style label) | None found | This page's card shape was **not** covered by the shared parser built this phase — see Part B |
| Season-pass add-ons | `sixflags.com/carowinds/season-pass-add-ons` | Plain HTTP GET, HTML | Same `productv2-card` markup as season passes (richer than the general add-ons page) | No | Yes, same mechanism | Current + comparison where shown (e.g. "All Season Fast Lane" $899→$499) | Yes | None found | Reached via a tab link on the add-ons page (`href="/carowinds/season-pass-add-ons"`), not a client-side tab fetch — a real, separate URL |

All six pages returned HTTP 200 to an anonymous `fetch()` with only a `user-agent` header — no
cookie, session, or authentication of any kind was required for any of them.

### How the data is actually served

`docs/phase1-discovery.md` (Phase 1) already established that `sixflags.com/carowinds/*` is a
Next.js **App Router** site whose Sanity CMS content streams inline via `self.__next_f.push(...)`
script tags, server-rendered — confirmed again here (`_type` values like `parkPasses`, `promoBar`,
`curatedColumnsBlock` appear identically across all six pages checked). What Phase 1 did **not**
check is whether *product/pricing* content specifically is part of that server-rendered payload, as
opposed to being fetched client-side the way the dated checkout flows are. It is: real dollar
amounts (`$89`, `$115`, `$275`, …) and real product names (`"2027 Gold Pass"`) are present as plain
text in the raw HTTP response body, inside genuine `<h3>`/`<span>`/`<p>` elements — not injected by
client-side JavaScript. A plain `curl`/`fetch()` GET is sufficient; **no browser execution is
required for any of the six catalog pages investigated.**

### The shared card component

All product cards on five of the six pages (every one except general add-ons) use the same
component, independently confirmed via literal, byte-for-byte identical markers across pages:

```html
<div data-ab="productv2-card" data-ab-product="{sanity-doc-id}" ...>
  ...
  <h3 data-ab="productv2-card-title">{Display Name}</h3>
  ...
  <p data-ab="productv2-card-price-before">{Qualifier, e.g. "Sale Price"}</p>       <!-- optional -->
  <span data-ab="productv2-card-price-strikethrough">{Comparison price}</span>       <!-- optional -->
  <span data-ab="productv2-card-price">{Current price}</span>
  <p data-ab="productv2-card-savings">{Savings text}</p>                             <!-- optional, not read -->
  <button data-accesso-keyword="{checkout keyword}" data-ab="productv2-card-buynow">Buy Now</button>
</div>
```

- **`data-ab-product`** is the Sanity CMS document's own id. Two formats were observed live —
  UUIDs (e.g. `550e3db6-5bad-45f8-8404-5d608c367617`) and short alphanumeric ids (e.g.
  `Th8bPaISbxESOk8zPXdAuW`) — both are just opaque strings, not a format to validate against. At
  this phase's original fetch time, the "2027 Gold Pass" card appeared to carry the same
  `550e3db6-...` id everywhere it appeared (season passes, daily tickets, special offers, the
  current promotion page). **This cross-page stability claim was corrected by the Phase 3F4/3F5
  addendum below**: a later live re-check found `special-offers` serving a *different* id
  (`9d031bb4-...`) for that same-named card. Treat this id as durable only *within* the page/adapter
  that observed it — do not rely on it for cross-page/cross-adapter deduplication (unlike a checkout
  keyword that embeds a plan year, which was never assumed stable in the first place).
- **`data-accesso-keyword`** is the same kind of checkout-entry keyword Phase 3C/3D/3E already use
  for dated products (`SnapWizardDailyTickets`, `FastLaneOnly`, `HAPWizard`) — here naming a
  catalog product line instead of a per-date package (e.g. `FallGoldSP2027`, `27AddonsFastlane`).
- **No per-product URL** was observed. "View Details" (`productv2-card-viewdetails`) is a
  `<button>`, not an `<a href>` — it opens a client-side modal, not a distinct page. Spec §7.2's
  official-URL identity tier therefore has no live evidence to be built from yet on these pages.
- Comparison price and qualifier are genuinely optional per card — e.g. "Daily Fast Lane" (Fast
  Lane page) has neither, while "All Season Fast Lane" (same page) has both. Confirmed by directly
  observing both shapes on the same page, not assumed.

### General add-ons page — a different, unproven shape

`sixflags.com/carowinds/add-ons` does not use `productv2-card` at all — zero matches. Its cards
(e.g. "Single Day FunPix," `$19.99`, keyword `2026SinglePhoto`) have a title, a plain price, a
description, and a Buy Now button with `data-accesso-keyword`, but no comparison-price element and
no qualifier label were observed on either product checked. This is evidence of only **one** page,
so per Part B's rule it does not get its own shared helper this phase — a future add-on adapter
will need its own small, separately-evidenced extraction for this shape.

## Part B — shared mechanics actually proven

Extracted (`Infrastructure/Retrieval/Sources/ProductCardHtmlParser.cs`) because the exact same
`productv2-card` attribute structure was independently confirmed on **six** separate page fetches
(season passes ×2 cards, daily tickets, special offers, Fast Lane, the current promotion page, and
season-pass add-ons) — well past the "at least two" bar, and the field names, nesting, and optional/
required shape were identical every time, not merely similar.

**Not** extracted into anything shared, because only one page's evidence exists (or the concept is
category-specific business logic, which Part B explicitly says must stay out of a shared helper
regardless of how much evidence exists):

- The general add-ons page's simpler card shape (one page only).
- Which `SourceCategory` a page's products belong to, how a missing product should be reconciled,
  and any per-category filtering — all of that is adapter logic for 3F2+, not something
  `ProductCardHtmlParser` decides. It returns plain `ParsedProductCard` records with no category or
  identity awareness at all, the same boundary `AccessoKeywordCalendarApiResponse` keeps for the
  dated JSON sources.
- No generic "Sanity page" or "Next.js RSC" framework was built. The shared piece is narrowly the
  one proven, repeating product-card shape — not an attempt to model the CMS generally.

## Part C — catalog retrieval contracts

**Existing Phase 2 types are sufficient for the product shape** — no new product-level type was
added. `ObservedProduct` (`Application/Catalog/ObservedProduct.cs`) already carries every field
this investigation found evidence for: identity (via `CatalogProductKey`, built from
`data-ab-product`/`data-accesso-keyword` by a future adapter using the existing
`CatalogProductKey.Create` priority order — id, then URL, then source+name), category (via the
key), display name, current price amount + display string, comparison price, price qualifier,
date-specific flag/applicable date, and an explicit-unavailable flag. Nothing observed live needed
a field `ObservedProduct` doesn't have.

**One new type was added**, `Application/Retrieval/CatalogPayload.cs`:

```csharp
public sealed record CatalogPayload(IReadOnlyList<ObservedProduct> Products);
```

This is the minimal wrapper a catalog adapter needs to implement
`IOfficialDataSource<CatalogPayload>` the same way every dated adapter implements
`IOfficialDataSource<TItsOwnPayload>` — nothing more. `IProductReconciliationService.ReconcileAsync`
already takes exactly `(category, evidence, IReadOnlyList<ObservedProduct>)`, which already *is*
"one current catalog retrieval batch" as a method signature — no separate batch/request wrapper
type was needed beyond that.

**Validity/start/end date was investigated but not added anywhere.** The only explicit,
structured validity field found live is `promoBar`'s `dateWithTimezone`, and it is a **page-level
banner field**, not demonstrably attached to any individual product card — no product card observed
carries its own start/end date. Adding a validity field to `ObservedProduct` on the strength of a
banner field that isn't proven to correspond to a specific product would be inventing a linkage the
evidence doesn't support (explicitly prohibited by this phase's Part C). If a later phase's adapter
finds a genuine per-product validity field, it should be added then, grounded in that evidence.

## Part D — status semantics for a catalog source

No new `RetrievalStatus` values are needed; `Verified`/`Unavailable`/`Error` already exist
(Domain, unchanged this phase) and apply to a future catalog adapter exactly as spec §5.2 defines
them generally:

- **Verified**: the page loaded successfully and `ProductCardHtmlParser.Parse` returned a result
  (possibly an empty list — see below) without throwing. `SourceEvidence.Status = Verified` is what
  authorizes `IProductReconciliationService.ReconcileAsync` to run at all (it throws otherwise —
  see `ProductReconciliationService.cs`), so this is also what actually lets Phase 2's add/update/
  archive lifecycle operate on catalog products.
- **Unavailable**: reserved for a source that *explicitly* states a product/category is
  unavailable — no live example of this was observed on any of the six pages this phase (removal
  simply means a card is absent from an otherwise-normal page, which spec §7.3 already treats as
  confirmed-absence evidence via `ObservedProduct.IsExplicitlyUnavailable = false` and the missing-
  product path in `ProductReconciliationService`, not a source-level `Unavailable` result).
- **Error**: transport failure, non-2xx status, or `ProductCardHtmlParser.Parse` throwing
  `FormatException` (a card marker was found but its content couldn't be extracted — see below).

**A successful empty catalog is distinguishable from an error by construction, not by a status
flag added this phase**: `ProductCardHtmlParser.Parse` returns an empty `IReadOnlyList<T>` when a
well-formed page simply contains no recognized cards (proven with a real shape — this is exactly
what `add-ons.html` produces against this parser, since it uses the other card variant), and only
throws when a card *was* found but its required title/price content could not be extracted from
within it (a truncated/restructured response). A future adapter maps the empty-list case to
`Verified` with zero `ObservedProduct`s (spec §7.1: successfully retrieving nothing is exactly the
signal Phase 2 reconciliation needs to safely hide a section), and the thrown-exception case to
`Error` — never the reverse, and per spec §7.3/`CatalogProductLifecycle.RequireNotError`, an
`Error` result must never be passed to reconciliation as if it were a confirmed-absent retrieval.

## Test infrastructure

`tests/.../Retrieval/Sources/ProductCardHtmlParserTests.cs`, against fixtures under
`fixtures/catalog-source/`. The two multi-card fixtures (`season-passes-page.html`,
`fast-lane-page.html`) are sanitized/trimmed excerpts of real captured responses — every
`data-ab`/`data-ab-product`/`data-accesso-keyword` value and price/qualifier text in them is copied
verbatim from the live pages fetched during this investigation, not invented. `no-products-page.html`
is modeled on the real add-ons page's actual behavior against this parser (zero matches).
`malformed-card.html` and `fallback-identity-card.html` are synthetic defensive fixtures, clearly
labeled as such in their own file comments, covering shapes not observed live (a truncated card, and
a card with neither id) — the same practice already used for defensive fixtures in the Phase 3D/3E
test suites (e.g. `unexpected-multiple-entries.json`).

Added `InternalsVisibleTo` from `Carowinds.Passdown.Infrastructure` to
`Carowinds.Passdown.Infrastructure.Tests` (csproj-level `<ItemGroup>`) — needed because, unlike the
JSON DTOs other sources keep internal, there is no adapter yet in this phase to exercise the parser
indirectly through a public `RetrieveAsync` call.

Transport-failure and cancellation handling are not re-tested here: this phase adds no new HTTP
code (a future catalog adapter would reuse the existing `IRetrievalHttpClient.GetAsync` and
`RetrievalDiagnostics.Error`, already proven by every existing adapter's test suite), so there is
nothing new to exercise for those concerns until an adapter actually wires this parser to a live
GET call.

## Limitations

- **Keyword-based ids may not survive year-to-year.** `FallGoldSP2027` names a 2027-specific plan;
  next year's equivalent pass will very likely get a new keyword (`FallGoldSP2028` or similar),
  which `CatalogProductKey`'s id-first priority would treat as a brand-new product rather than a
  renewal of the same pass. The `data-ab-product` Sanity id may or may not be more stable across a
  full year boundary — this investigation could only confirm it's stable *within* the current
  season across pages, not across a season-to-season CMS content refresh, since no such boundary
  occurred during the investigation window.
- **General add-ons' simpler card shape is not covered.** A future add-on adapter needs its own
  extraction for that page, evidenced separately when it's actually built.
- **No per-product validity/expiration field exists in observed evidence** — only a page-level
  promo-bar field, not linked to individual products. See Part C.
- **No removed/expired product was directly observed** (nothing to diff against, since this is a
  point-in-time investigation) — reasoned from markup structure only: absence would look like a
  card simply not appearing in an otherwise-normal page load, matching spec §7.3's confirmed-absence
  handling rather than an explicit "sold out" marker.
- **The parser is substring-based, not a real HTML parser.** It does not understand HTML comments
  (a card-marker-shaped string inside a `<!-- -->` comment would be misread as a real card — caught
  and fixed in this phase's own fixture authoring, not observed as a real-page risk) or arbitrarily
  nested/reordered markup. This is a deliberate, documented tradeoff for this phase, not an
  oversight; a real HTML-parsing library (e.g. AngleSharp) was considered and deferred rather than
  added speculatively, since no adapter yet depends on more robustness than this provides.
- Only two of the six pages (season passes, Fast Lane) were parsed field-by-field against the
  shared helper's fixtures. The other four (daily tickets, special offers, the current promotion
  page, season-pass add-ons) were confirmed to use the identical `productv2-card`/`data-ab-product`/
  `data-accesso-keyword` mechanism via direct grep evidence, but no fixture was built from them —
  sufficient to prove the mechanism is shared (Part B's bar), not intended as full coverage of every
  page's specific products.

## Recommended order for 3F2–3F5

Simplest/lowest-risk to most complex, based on what this investigation found:

1. **Season passes** (`SourceCategory.SeasonPasses`) — cleanest `productv2-card` evidence (2 cards,
   both with explicit comparison price + qualifier + both id types), single page, no season-boundary
   ambiguity to reason about. Best first adapter to validate the parser end-to-end through a real
   `IOfficialDataSource<CatalogPayload>` and reconciliation.
2. **Season-pass add-ons** (`SourceCategory.AddOns`, from `season-pass-add-ons`) — same
   `productv2-card` shape, already proven by this phase's own fixture-equivalent evidence (the "All
   Season Fast Lane" card), just a different page/product set and a longer product list (7 keywords
   observed).
3. **Promotions** (`SourceCategory.Promotions`, from `special-offers`/`biggest-sale-of-the-year`) —
   same card shape, but adds the open question of whether/how to use the promo-bar validity date;
   likely fine to launch without it (Part C's conclusion) and revisit only if a later need arises.
4. **General add-ons** (`SourceCategory.AddOns`, from `add-ons`) — needs its own new,
   separately-evidenced card-shape extraction (no comparison price/qualifier observed), so it's
   more work than reusing the proven parser, even though conceptually simple.
5. **Fast Lane marketing page's catalog product** (`27AddonsFastlane` specifically) — lowest
   priority since it's a single product already reachable via the season-pass add-ons page; only
   worth a dedicated pass if the two pages are found to disagree on it later.

Daily-ticket and Fast Lane *marketing* pages themselves are not separate adapters to build — their
only catalog-relevant content (the "All Season"/pass-style products) is better sourced from the
season-passes/add-ons pages directly, per the durable-id evidence above (same `data-ab-product`
across pages).

## Phase 3F4/3F5 addendum — narrow live revalidation before PromotionsCatalogSource/GeneralAddOnsCatalogSource

Performed 2026-08-20, immediately before writing `PromotionsCatalogSource` and
`GeneralAddOnsCatalogSource`. Plain anonymous `fetch()`/HTTP GET only, same as Part A.

**`special-offers` (Promotions adapter's target page)**: still 2 `productv2-card`s, same shared
markup mechanism. Both re-fetched cards carry an explicit comparison price and qualifier ("Bring-A-
Friend Ticket" — $35, was $80 Gate Price, "Starting From"; "2027 Gold Pass" — $89, was $115, "Sale
Price"). `promoBar`'s `dateWithTimezone` re-confirmed present (`2026-09-08T03:59:00.000Z`, 2 of 4
promoBar instances on the page) and re-confirmed **not** nested inside either product-card fragment
— independently re-verifies Part C's conclusion rather than assuming it still holds.
`PromotionsCatalogSource` therefore populates no per-product validity field, same as every other
catalog adapter.

**Correction to Part B's cross-page id-stability claim**: the "2027 Gold Pass" card on
`special-offers` carries `data-ab-product="9d031bb4-5b2f-46ea-8da0-08d826e7bd11"` — **not** the
`550e3db6-...` id the same-named pass carries on `season-passes`, re-confirmed live on both pages
the same day. Part B's "independently confirmed to stay identical... across different pages" claim
does not hold for this pair. This does not affect correctness here: `CatalogProductKey` scopes
identity by `SourceCategory` (`Promotions:id:9d031bb4-...` vs. `SeasonPasses:id:550e3db6-...` are
already different keys regardless), so `PromotionsCatalogSource` and `SeasonPassCatalogSource`
correctly track this as two independent catalog rows rather than accidentally colliding. Flagged
here so a future phase doesn't rely on cross-page id stability as a deduplication mechanism without
re-checking it.

**`biggest-sale-of-the-year` (Promotions' documented secondary URL — investigated, not adopted)**:
still returns 200, still uses `productv2-card`, but now carries 6 cards, one of which
("Passholders save more! Claim your special rate today.") has a title and a `data-ab-product` id but
**no price element anywhere in the rest of the response** — a marketing CTA card reusing the product
card wrapper, not a real product. `ProductCardHtmlParser.ParseCard`'s all-or-nothing title/price
requirement (deliberately chosen in Phase 3F1 specifically so a truncated card fails safely — see
`malformed-card.html`'s own fixture comment) cannot distinguish this from a truncated response, so
parsing this page as-is would throw `FormatException` and turn a genuinely successful page load into
an `Error`. `special-offers` itself has no such card today. Given the task scope named only
`special-offers`, `PromotionsCatalogSource` targets that page alone and this is not addressed —
`biggest-sale-of-the-year` remains unadopted, now for two independent reasons (Part A's original "not
asked for" plus this new priceless-card risk). A future adapter that wants this page needs either a
priceless-card exception in the shared parser (affecting every caller) or its own filtering — not
decided here.

**`add-ons` (General Add-Ons adapter's target page)**: re-confirmed zero `productv2-card` matches.
Exactly 2 real products found, both duplicated once each for a responsive breakpoint (4 total
`data-accesso-keyword` occurrences, 2 distinct values) — the same duplication pattern
`CatalogCardMapper`'s existing dedup already handles for the shared parser. Card shape: title in
`<h3 class="font-medium text-blue-dark type-20-600 break-words">`, price in a sibling
`<span class="type-20-600">` preceded by a plain "Only" label (not a `productv2-card-price-before`
style qualifier element), and `data-accesso-keyword` on the Buy Now button — no
`data-ab-product`/Sanity id anywhere on this page, and no comparison-price element found on either
card. This re-confirms every specific claim in Phase 3F1's row for this page, not just the general
"different shape" conclusion. `GeneralAddOnHtmlParser` extracts only what this evidence supports:
identity (via the accesso keyword, `CatalogProductKey.Create`'s fallback since no official id
exists), display name, and current price. Comparison price and qualifier are left permanently null
by this parser's design (not merely absent per-fixture) — there is no markup shape for either field
anywhere in the live evidence for this page, and inventing one to satisfy would-be optional fields
would violate spec §5.2's never-fabricate rule the same way populating validity from `promoBar` would
have. If a future revalidation finds either field, add extraction for it then, grounded in that
evidence.
