# Product classification, visibility, and lifecycle (Phase 17A)

This document covers the rules Phase 17A introduced for deciding *what a product means
operationally* (its display section) and *whether it should be shown at all* (its lifecycle),
independent of the raw source/category it happened to be scraped from. It supplements
`docs/retrieval/pricing-semantics.md` (which covers *price* semantics) and
`docs/retrieval/catalog-source-inventory.md` (which covers *retrieval* details).

## 1. Product-family precedence (`ProductDisplaySectionResolver`)

Every product resolves to a display section through one function,
`ProductDisplaySectionResolver.Resolve`, in this order:

1. **Category has no dedicated section at all** (Bring-A-Friend Duo's own dated category, the dated
   ordinary Bring-A-Friend category) — always `null`, regardless of name. Both offers are already
   represented once, elsewhere, by their Promotions catalog counterpart.
2. **Product family from name** — a season-pass tier word (`Silver`, `Gold`, `Prestige`) combined
   with the word `Pass` anywhere in the display name always wins, regardless of raw category or
   audience wording. This is what sends "Military Gold Pass" and "First Responder Gold Pass" to
   **Season Passes** instead of Daily Admission, and works for any current or future
   year-prefixed/suffixed variant ("2027 Silver Pass", "2026 Gold Pass", ...) without a per-year code
   change — the year is incidental text, never part of the match.
3. **SCarowinds-specific naming** — a Promotions-category card whose name contains "SCarowinds"
   reclassifies to the SCarowinds section (unchanged from Phase 15A).
4. **Raw category mapping** — otherwise, the product's `SourceCategory` decides the section
   (`DatedAdmissionTickets`/`MilitaryFirstResponder` → Daily Admission, `SeasonPasses` → Season
   Passes, etc.), unchanged from Phase 15A.

Audience wording (Military, First Responder) is **never**, by itself, a section signal — only a
season-pass tier word is. "Military 1-Day Ticket" and "Military & First Responder Tickets" (no tier
word) correctly stay in Daily Admission; "Military Gold Pass" (tier word present) does not.

### Do not hardcode product names

The tier-word check is deliberately generic (`Silver`/`Gold`/`Prestige` + `Pass`), never a list of
exact product names like `"2027 Silver Pass"`. A brand-new pass name that follows the same
vocabulary classifies correctly the first time it is observed, with no code change.

## 2. Equivalence and duplicate suppression (`ResolveAll`)

Two extra situations fall out of resolving by name instead of raw category, both handled by
`ProductDisplaySectionResolver.ResolveAll` — the one entry point the Associate Products screen and
the Associate PDF both use (never their own per-consumer grouping):

- **"2027 Gold Pass" tracked as two independent catalog rows.** The same real-world pass is
  discovered on two different official pages (Season Passes, and a promoted-offer card on
  `special-offers`/`daily-tickets`) and is deliberately kept as two separate
  `CatalogProductKey`-scoped rows (see `catalog-source-inventory.md`'s Phase 3F4/3F5 addendum).
  Before this phase they printed once each in two *different* sections (Promotions, Season Passes),
  so it was never a visible duplicate. Now that the season-pass override reclassifies both into
  Season Passes, `ResolveAll` keeps only the row whose own category is *native* to that section
  (`SeasonPasses`) and drops the other.
- **Exact dated price vs. generic "Starting From" fallback.** See §3 below — a separate mechanism,
  not part of `ResolveAll`.

## 3. Exact dated price supersedes a generic catalog fallback

`AssociatePassdownService.SuppressGenericFallbacksSupersededByExactDated` drops a non-custom
catalog product whose `PriceKind` is `StartingFrom` when an exact dated product (`PriceKind.ExactDated`)
for the **selected operating date** shares its normalized display name (`ProductDisplayIdentity`).

The concrete, real case this fixes: `DatedAdmissionSource`'s exact dated admission product and
`PromotionsCatalogSource`'s generic catalog card are both named **"Park Ticket"** (see
`dated-admission-source.md` and `fixtures/promotions/daily-tickets-page.html`) — before this phase
both appeared, one under Daily Admission (exact) and one under Promotions (generic "Starting From"),
for the same real-world product.

This is scoped narrowly:

- Only a `StartingFrom` generic is ever suppressed — a `Sale`/`StandardCatalog`/`Bundle` catalog
  product is never touched by this mechanism.
- Matching is by normalized display name only (`ProductDisplayIdentity`), so a genuinely distinct
  bundle ("Ticket + Meal, Snack & Drink Bundle", "Ticket + Fast Lane Reserve Bundle") is never
  suppressed just because the base admission's exact price is known — its name simply never matches.
- Recalculated from the **selected operating date only** — selecting a different date recomputes
  against that date's own exact result (or falls back to the generic card if none exists). Date
  selection stays local-only; this never triggers a retrieval.

This generalizes, without regressing, the existing Phase 15A Bring-A-Friend merge
(`MergeExactDatedBringAFriend`): that mechanism *merges* the exact price into the existing generic
row in place (because Bring-A-Friend's dated category never gets its own composed row at all), while
this mechanism *removes* the redundant generic row (because an exact dated admission product already
gets its own composed row independent of the catalog one). Both are expressions of the same
underlying rule — exact wins over generic for the same product, for the selected date.

## 4. Expired vs. confirmed-absent vs. retrieval failure

`ProductLifecycleState` distinguishes three ways a catalog product stops being shown, corresponding
to three different real-world facts:

| State | Meaning | Trigger |
|---|---|---|
| `Active` / `PendingConfirmation` → `Archived` | The source simply stopped listing the product (`ConfirmAbsent`, two consecutive successful checks) | Confirmed absence |
| `Archived` (immediate) | The source explicitly flagged the product gone/sold out *right now* (`MarkExplicitlyUnavailable`) | `ObservedProduct.IsExplicitlyUnavailable` |
| `Expired` (immediate) | An authoritative valid-through date has passed (`MarkExpired`) | `ObservedProduct.ValidThroughDate` |

All three are reached only from a **successful** (`Verified`) retrieval — `RetrievalStatus.Error` is
rejected by every lifecycle transition (`CatalogProductLifecycle`'s `RequireNotError`/`RequireVerified`
guards) and never counts as evidence of absence or expiration. A failed retrieval always preserves
last-known-good state untouched (spec §6.7).

`Expired` and `Archived` are both hidden from normal operational display
(`AssociatePassdownService.ComposeCatalogProducts` skips both), but kept fully queryable in
`catalog_products`/`product_history` for diagnostics and audit — nothing is ever deleted for a
presentation reason.

### End-date semantics

Inclusive: the product is still valid *on* its valid-through date and expires the day after
(`CatalogProductLifecycle.MarkExpired` requires the current Eastern date to be strictly greater than
the valid-through date; it throws otherwise, rather than silently no-op-ing, so a caller can never
accidentally expire a product early).

### Current limitation: no adapter populates `ValidThroughDate` yet

`ObservedProduct.ValidThroughDate` is deliberately a generic, adapter-populated field — the
reconciliation/lifecycle machinery above is fully implemented and tested, but **no current retrieval
adapter sets it**. `PromotionsCatalogSource`'s own doc comment documents why: the only structured
validity signal live-confirmed on the Promotions/daily-tickets pages (`promoBar`'s
`dateWithTimezone`) is a page-level marketing banner, never nested inside a product card, so there is
no live evidence tying it to any individual product. Inventing that linkage would violate spec §5.2
("never invent data"). When a future adapter *does* find a real per-product end date, wiring it in is
a one-line addition to that adapter's `ObservedProduct` construction — no further schema or
reconciliation change needed.

Because of this, `ValidThroughDate` is intentionally **not persisted** to `catalog_products` in this
phase (no DB migration — `ProductLifecycleState.Expired` is stored as an ordinary new enum-name
string in the existing `lifecycle_state` column). Expiration is recognized at the next successful
reconciliation (Check Online / Deep Refresh / scheduled refresh) for whichever adapter eventually
supplies the date, not from a pure wall-clock tick while the app is idle with no adapter wired in.
This is a deliberate, documented scope boundary for 17A, not an oversight — see the phase's final
report for the full reasoning.

## 5. Source-unavailable accounting (`OperationsDashboardService`)

`DashboardSourceStatus.Unavailable` (a real "we expected an answer and didn't get a usable one") is
now distinguished from a **new** `DashboardSourceStatus.NotApplicable` ("the source gave a confirmed,
correct answer: this optional/seasonal offer simply isn't running for the requested date").

Only `SourceCategory.BringAFriendDuo` and `SourceCategory.DatedBringAFriend` can currently produce
`NotApplicable` — both are seasonal, promotional, per-date offers whose accesso keyword-calendar
check legitimately returns `RetrievalStatus.Unavailable` for a date outside the promotion's running
window. `NotApplicable` is excluded from `DashboardDataHealthSummary.UnavailableCount` and from the
overall `DashboardOverallDataStatus` precedence, the same way a confirmed "the park is closed" never
degrades park status. A genuine retrieval failure (`Error`) on either category is completely
unaffected and still reports `VerificationIssue` exactly as before.

## 6. Historical immutability

None of the above ever runs against a finalized `AssociateWorkspace` snapshot — a finalized passdown
freezes its own product names/categories/prices/PDF bytes at finalization time
(`Application/Finalization`), before any of this phase's resolver/lifecycle logic is invoked again.
New classification and expiration rules apply only to live/current composition and future
finalizations.

## 7. Deferred

- Persisting `ValidThroughDate` (and any richer end-date UI/diagnostics built on it) once a real
  adapter has live evidence for one — deferred, not scheduled as 17B/17C work specifically.
- Any Monthly ERT changes, Manager workflow, or UI redesign remain explicitly out of scope per
  CLAUDE.md's current-release decisions and are not addressed by this document.
