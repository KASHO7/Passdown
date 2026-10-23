# Phase 25 — Membership & Season Pass Source Compatibility

Unreleased development after `0.1.0-rc.8` (version unchanged: `0.1.0-rc.8` / FileVersion `0.1.0.8`).
Nothing was packaged, pushed or published, and the frozen RC.8 artifacts were not touched.

> **Status in one line:** the official Season Pass source works again against the redesigned
> Memberships & Season Passes page, with Season Passes, renewal offers, Memberships and initiation fees
> as separate products. Verified by deterministic tests and one live check on the macOS build host;
> nothing has been executed on Windows.

## 1. Starting state

| | Verified |
|---|---|
| HEAD / branch | `f3263ed` on `feature/passdown-redesign` |
| Version | `0.1.0-rc.8` / FileVersion `0.1.0.8` |
| Deterministic tests | **4439/4439** (Domain 270, Application 1169, Infrastructure 976, Documents 463, Ui mixed 1413, run-alone 26 + 109 + 4 + 2 + 7) |
| Release build | 0 warnings, 0 errors |
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 |
| Unrelated staged files | `.DS_Store` files and `.claude/skills/pdf-design/SKILL.md` — untouched, outside the commit |

No discrepancies from the expected baseline.

## 2. Root cause of "Season Passes unavailable"

- The adapter (`SeasonPassCatalogSource`) requested `https://www.sixflags.com/carowinds/season-passes`.
  That address now **redirects (HTTP 200 after redirect)** to
  `https://www.sixflags.com/carowinds/memberships-and-season-passes` — download was not the problem.
- The failure was in **parsing**. The redesigned tier cards still use the `data-ab="productv2-card"`
  wrapper, but no longer contain the `data-ab="productv2-card-price"` element. Prices now live in
  "Choose your way" option buttons (`productv2-card-payment-option`). The Phase 3F1 card parser treats
  a card without a title *and* price element as a restructured page and throws `FormatException`, so
  every check ended as `Error` ("We couldn't check season passes right now.") and the source showed as
  unavailable.
- Persistence behaved correctly: an `Error` result is never reconciled, so last-known-good catalog rows
  and their price history were preserved throughout.
- Changing the URL alone would not have fixed it: the new card shape needs a new parser, and the new page
  sells two different products per tier.

## 3. The official data structure

One plain anonymous GET (~1.26 MB). The page is a Next.js App Router page; its Sanity CMS content is
server-rendered twice: as visible HTML **and** as React Server Components "flight" data in
`self.__next_f.push([1,"…"])` script chunks (chunk boundaries fall mid-document). Each tier is one
`product_v2` document:

| Field | Meaning | Example (live, 2026-10-22) |
|---|---|---|
| `guestFacingTitle` | Tier card title | `Gold Tier` |
| `paymentOptions[]` | One entry per purchase option | Membership, Season Pass |
| `.optionTitle` | Product type label | `Season Pass` / `Membership` |
| `.price` / `.priceFrequency` | Headline amount and unit | `$99` `/ea*`; `$7` `/mo*` |
| `.pricePretense` | Crossed-out reference amount | `$115` |
| `.optionSubtitle` | Validity or enrollment line | `Valid all 2026 & 2027`; `$20 initiation fee due today` |
| `.priceSavings` | Savings / renewal / terms line | `Renewals Just $89`; `Auto-renews After 12mo`; `Save $30` |
| `.lockedText` | Option not purchasable | `Not Available` (Silver Membership) |
| `.accessoKeyword` | Checkout keyword | `FallGoldSP2027`, `26GoldMEM2` |
| `taxesAndFeesDisclosure` | Fee/tax caveat (portable text) | `*Plus processing fee of up to $9.99 & applicable taxes.` |

The flight format escapes a literal leading `$` as `$$` (`"$$99"` → `$99`); any other leading `$` is a
reference to another chunk and is never read as text. No public pricing API was found; no client-side
fetch, authentication or browser is needed. The same amounts also appear in a comparison table
(`compare-price-*`), which is ignored.

**Why the structured fields, not the rendered buttons:** the rendered card only names the checkout keyword
of the option selected by default, and the comparison table repeats amounts — the structured fields relate
every amount to its own label.

## 4. Parser strategy

- `Infrastructure/Retrieval/Sources/PassTierPageParser.cs` — decodes and concatenates the flight chunks,
  finds every `"_type":"product_v2"` document, reads its option fields verbatim. Transport shape only.
- `PassOfferInterpreter.cs` — turns options into offers, **only from labeled fields**:
  - "Season Pass" option → one-time new-purchase price; `pricePretense` → reference amount; a
    `renew…` label with exactly one `$` amount → a separate renewal offer.
  - "Membership" option → monthly rate **only if the frequency says monthly**; an
    `initiation|enrollment fee` label with one amount → a separate initiation fee; a `renew…` line with no
    dollar amount → billing terms kept verbatim as the qualifier (never parsed into a contract length).
  - `lockedText` with no price → an unavailable, unpriced option.
  - Never "first/largest number"; processing fees and taxes are never added to a price.
- Season year: from the Season Pass checkout keyword's trailing year (`FallGoldSP2027` → 2027) — the
  observation date is never used. Unknown → no year in the identity.
- Unknown new tier ("Diamond Tier") → parsed under its own identity and flagged. A card without a
  "… Tier" title, a document without payment options, or an unrecognized option title → ignored with a
  warning (never mistaken for a pass price).
- Duplicates within one retrieval: identical repeats → one observation; conflicting repeats → neither
  trusted, the offer is *unconfirmed*.
- `SeasonPassCatalogSource` (source id `season-passes.official` and source name "Carowinds Season Passes"
  unchanged, so freshness state and absence scoping continue) now requests the new URL directly.

| Outcome | Status |
|---|---|
| Transport failure, non-2xx, undecodable/truncated data, cards rendered without tier data, no flight data, or tiers with no verifiable offer | `Error` — nothing reconciled; last-known-good kept; diagnostic in `TechnicalDetail` |
| Some offers unreadable (missing/unparseable price, unresolved reference, conflicting repeat, Season Pass quoted monthly, unreadable renewal/initiation) | `Verified`, detail **"Partially verified: … Could not verify X — showing the last verified price."**; unreadable offers passed as `CatalogPayload.UnconfirmedProductKeys` |
| Well-formed page with no tiers and no tier cards | `Verified` empty catalog (existing Phase 3F1 semantics) |

Requests: one GET per check (the existing daily/Check Online cadence); no retries were added.

## 5. Product identity

`Domain/Catalog/PassOffer.cs` — park, product type, tier, price type, season year:

```
SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2027
SeasonPasses:pass-offer:carowinds|gold|season-pass|renewal|2027
SeasonPasses:pass-offer:carowinds|gold|membership|monthly
SeasonPasses:pass-offer:carowinds|gold|membership|initiation-fee
```

- Deterministic from verified labels; never card position or price. The page's only per-card CMS id
  (`_id`) belongs to the **tier card**, shared by that tier's Season Pass and Membership, so it cannot be a
  product identity. Checkout keywords are year-bound, absent for renewals, and inconsistent for
  memberships (`26GoldMEM2`), so they are evidence (season year), not identity.
- Gold Season Pass ≠ Gold Membership; monthly rate ≠ initiation fee; renewal ≠ new purchase; reference
  amounts are never a current price. Units derive from the identity (`PassOffer.Unit`), like `PriceKind`
  — nothing new is persisted.

## 6. Legacy compatibility and historical continuity

Pre-redesign rows were keyed by the old page's per-card CMS id, e.g.
`SeasonPasses:id:550e3db6-…` "2027 Gold Pass" and `SeasonPasses:id:5de73366-…` "2027 Prestige Pass"
(checkout keywords `FallGoldSP2027` / `FallPrestigeSP2027` — the same keywords the new Season Pass options
carry, i.e. the same real products).

Reconciliation (`ProductReconciliationService.LegacyPassIdentityAliases`) files a new **Season Pass
new-purchase** observation under the existing legacy key only when: it states a season year; no record
exists yet under its own new key; and exactly one stored non-pass-offer Season Passes record names the same
tier and year (`PassOffer.FromLegacyName`, strict: `[year] Tier [Season] Pass [— year]`; active records
preferred). Then:

| Legacy record | New presentation |
|---|---|
| "2027 Gold Pass" (`id:550e3db6-…`) | Gold Season Pass — 2027, same key, same history |
| "2027 Prestige Pass" (`id:5de73366-…`) | Prestige Season Pass — 2027, same key, same history |
| "2027 Silver Pass" (if an install has one) | Silver Season Pass — 2027, same key |
| Any other-year pass ("2026 Gold Pass") | Unchanged; retires through the normal two-absence rule |

- Old intervals are never rewritten (amount, timestamps, display name, source, kind, origin). The new
  observation appends a new interval only when the price state differs; the live Gold pass moved
  $89 (Sale Price) → $99 (reference $115), recorded as a genuine price change, not a rename artifact
  (the display name change alone never opens an interval).
- Memberships, renewal offers and initiation fees never adopt a legacy identity (the old page never sold
  them) and start their own series at their first observation — no backfill.
- Draft per-product overrides stay keyed to the legacy identity, so existing drafts keep their PDF
  inclusion/override choices.
- Promotions-category "2027 Gold Pass" cards keep their own identity; in Products/PDF they are still
  de-duplicated against the native Season Pass by pass offer (tier + year), not only by exact name.

## 7. Pricing semantics in the data model

No migration: **DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 unchanged.** Each price is its own catalog
row under its own key:

| Offer | `CurrentPriceAmount` | `CurrentDisplayPrice` | `ComparisonPriceAmount` | `PriceQualifier` | History kind |
|---|---|---|---|---|---|
| Season Pass | 99 | `$99` | 115 (reference) | — | Sale (as before) |
| Renewal offer | 89 | `$89` | — | — | Standard |
| Membership | 7 | `$7/mo` | — | `Auto-renews After 12mo` | Standard, per month |
| Initiation fee | 20 | `$20` | — | `$20 initiation fee due today` | Standard |
| Unavailable option | — | `Not Available` | — | — | none (no price state) |

The fee/tax disclosure is kept verbatim in the source evidence detail ("Official note: …") and shown with
the source line; it is never added to an amount. Monthly × 12 totals are never computed.

Partial checks: `CatalogPayload.UnconfirmedProductKeys` (new, default empty) and an optional
`unconfirmedProductKeys` parameter on `IProductReconciliationService.ReconcileAsync`. Unconfirmed rows are
neither confirmed nor counted absent — their price, history and last-verified time stay as they were, and
Products labels them Stale (see §8).

## 8. Application UI

**Products** (compact Phase 22 rows, theme tokens only):

- *Season Passes*: `Gold Season Pass — 2027 · $99.00`, muted "Reference price $115.00", "Renewal offer
  $89.00". The renewal is folded onto its pass row (`PassOfferComposition.FoldCompanions`); an unmatched
  renewal stays a row of its own marked "Not printed on the passdown."
- *Memberships* (new `ProductDisplaySection.Memberships`, appended to the enum and ordered directly after
  Season Passes by `ProductDisplaySectionResolver.DisplayOrder`): `Gold Membership · $7.00/month`,
  "$20.00 initiation fee", terms in italics; `Silver Membership · Not available` (never $0.00). Membership
  rows have no PDF checkbox/override (a fixed slot keeps alignment); the group says once that Memberships
  are not printed.
- Source line under both groups: "Official Carowinds source (www.sixflags.com/carowinds/memberships-and-
  season-passes) · last verified *date at time* ET. *verification summary* Official note: *fee disclosure*".
  The page address is shown as text; the app has no established in-app link-opening convention for sources.
- A Season Passes row verified before the source's latest successful check (unreadable on a partial check,
  or missing once) is labeled **Stale** with its own last verified time (Season Passes category only).

**Price History**: families `SeasonPass`, `SeasonPassRenewal`, `Membership`, `MembershipInitiationFee`,
`SeasonPassAddOn` (never persisted); a "Price type" filter; every price, change and note uses the series'
unit ("$7.00/month", "+$2.00/month (+28.6%)"); one product key = one unit, so units never mix. An
unavailable option with no recorded price is not listed. There is no chart in this screen (table only),
so there is nothing to plot across units.

**Dashboard**: no change needed — it shows only source status for Season Passes, never a headline pass
price, so it can never substitute a monthly rate.

## 9. PDF and Teams Copy

- Contract preserved: the Season Passes section prints one row per Season Pass with its one-time
  new-purchase price (the PDF has never printed comparison or qualifier text). Renewal offers, Membership
  rates, initiation fees and unavailable options are never printed (`AssociateProduct.IsPassdownExportable`,
  `IsUnavailablePassOption`); the Memberships section is never included.
- **Deferred (intentional):** a printed Memberships section and printed renewal offers — not part of the
  operational PDF contract.
- Classic, Compact and Modern all render (tested). Teams Copy reads the same document model.
- Pass names on newly generated passdowns read "Gold Season Pass — 2027" (was "2027 Gold Pass").
  Stored finalized PDFs/snapshots are never regenerated or reinterpreted.

## 10. Personal Sync

Catalog rows and price history are local and are not synced (unchanged). Drafts sync with their product
overrides keyed by product identity; legacy identities are kept, so no sync payload changes.

## 11. Tests

| Project | Added | Covers |
|---|---|---|
| Domain | `PassOfferTests` (33) | distinct identities per product/price type, key format/round-trip, units, exportability, parents, strict legacy-name rule, monthly price text |
| Application | `Phase25PassOfferCompositionTests` (16), `Phase25PassIdentityReconciliationTests` (5) | folding, unit/initiation/renewal, unavailable option, sections and order, stale labeling, legacy row resolution, promoted-card dedupe, history families/units/list; ambiguous legacy match, membership never adopts, category scoping, unconfirmed keys, unchanged absence rules |
| Infrastructure | `SeasonPassCatalogSourceTests` (28, rewritten for the new page), `Phase25PassHistoryIntegrationTests` (9, real SQLite) | every tier/option, renewal, reference, fees, reorder, duplicates, conflicts, missing tier/price/renewal, unknown tier, extra pricing, unresolved reference, monthly-quoted pass, truncated/missing data, legacy card page, empty catalog, HTTP/transport/cancel; legacy continuity, unchanged old intervals, confirm-not-append, separate series, unavailable option, partial and failed checks, two-absence retirement, other-year legacy |
| Documents | `Phase25PassOfferDocumentTests` (6) | PDF/Teams contract, all three templates, legacy model unchanged |
| Ui (mixed) | `Phase25PassOfferUiTests` (13) | row texts/units/labels/PDF controls, source note, history units/labels/filter, markup bindings and accessible names, no hard-coded colours |

Fixtures: `fixtures/memberships-and-season-passes/` (16 files, ~89 KB), built from one live capture by
`tools/research/memberships-season-passes/build_fixtures.py` (pricing fields only; images, benefits,
descriptions, CMS internal titles removed; variants derived from the real documents). Updated:
`CatalogSourceReconciliationIntegrationTests` (new page), `SeasonPassCatalogSourceLiveSmokeTests` (opt-in,
logs every parsed offer). `tools/visual-fixtures` gained Phase 25 pass rows and an `Associate-Memberships`
scenario (synthetic values).

Final deterministic total: **4538/4538** (see §15). Release build 0 warnings, 0 errors.

## 12. Live verification — LIVE VERIFIED

One bounded check through the real adapter (`SeasonPassCatalogSourceLiveSmokeTests`, opt-in), plus one
earlier research GET of the same page and one of the old URL (both `curl`, HTTP 200; the old URL
redirected to the new one).

| | |
|---|---|
| Retrieved | 2026-10-22 10:46:52 AM EDT (14:46:52 UTC) |
| URL | `https://www.sixflags.com/carowinds/memberships-and-season-passes` |
| HTTP | 200 (success; same URL returned 200 at 10:17 AM EDT) |
| Status / detail | Verified — "Verified 3 Season Passes, 2 renewal offers, 2 Memberships (1 not available). Official note: *Plus processing fee of up to $9.99 & applicable taxes." |
| Warnings / unconfirmed | none / none |

| Identity | Amount | Unit | Reference | Notes |
|---|---|---|---|---|
| Silver Season Pass — 2027 | $79 | one-time | $109 | no renewal published |
| Silver Membership | — | — | — | "Not Available" |
| Gold Season Pass — 2027 | $99 | one-time | $115 | — |
| Gold Season Pass Renewal — 2027 | $89 | one-time | — | "Renewals Just $89" |
| Gold Membership | $7 | per month | — | "Auto-renews After 12mo" |
| Gold Membership Initiation Fee | $20 | one-time | — | "$20 initiation fee due today" |
| Prestige Season Pass — 2027 | $249 | one-time | $275 | — |
| Prestige Season Pass Renewal — 2027 | $239 | one-time | — | "Renewals Just $239" |
| Prestige Membership | $20 | per month | — | "Auto-renews After 12mo" |
| Prestige Membership Initiation Fee | $20 | one-time | — | "$20 initiation fee due today" |

Comparison with the visible source: each value matches the page's own server-rendered "Choose your way"
buttons (e.g. Gold: Membership "$20 initiation fee due today · $7 /mo* · Auto-renews After 12mo"; Season
Pass "Valid all 2026 & 2027 · $99 /ea* · $115 · Renewals Just $89"), and the example table in the phase
request. The Membership option in each tier differs from its Season Pass option and was compared only with
its own Membership amounts. Not executed: a visual comparison in a real browser window, and a Check Online
against a real profile.

## 13. Windows validation checklist — NOT EXECUTED

Use a disposable or backed-up profile (`%LOCALAPPDATA%\Carowinds Passdown`). Record PASS/FAIL per item.

1. Check Online completes; Dashboard data status shows Season Passes current.
2. Season Passes is no longer reported unavailable/"couldn't check".
3. Silver Season Pass — 2027 shows a one-time price and its reference price.
4. Gold Season Pass — 2027 shows its price, reference price and "Renewal offer".
5. Prestige Season Pass — 2027 likewise.
6. Gold Membership shows "$x.xx/month" and "$x.xx initiation fee".
7. Prestige Membership likewise.
8. Silver Membership shows "Not available" (never $0.00).
9. Renewal lines read "Renewal offer", never as the headline price.
10. Reference lines read "Reference price", never as a sale/current price.
11. Monthly prices carry "/month" in Products and Price History.
12. Initiation fees appear only as their own line / series.
13. A profile with pre-Phase 25 prices still shows its existing 2027 Gold/Prestige pass history (older
    rows unchanged) — no duplicate Gold/Prestige rows.
14. After two Check Online runs on different days, new history rows appear only for real price changes.
15. Price History: "Price type" filter groups Season Passes / renewals / Memberships / initiation fees /
    add-ons; no list mixes units.
16. Offline (network disabled): Check Online fails honestly, Products keeps last verified prices.
17. Failure messages are plain language; Diagnostics export contains the technical detail.
18. Light and Dark: every new line readable; no hard-coded colours.
19. 100%, 125%, 150% scaling: no clipping/overlap in Season Passes, Memberships, Price History.
20. PDFs (Classic, Compact, Modern): Season Passes rows unchanged in form; no Memberships/renewal rows.
21. Teams Copy: same Season Pass prices; no Membership/renewal amounts.
22. Opening and checking the passdown changes no draft value except what the associate edits.
23. Personal Sync: no new conflicts or payload changes after Check Online.

## 14. Known limitations

- Depends on the page's embedded CMS data shape (`product_v2` / `paymentOptions`); a future redesign that
  drops it yields an honest `Error`, not guessed prices.
- Season year comes from the checkout keyword; if a future keyword lacks a year, the pass identity has no
  year (and legacy adoption does not apply).
- Season Pass validity text ("Valid all 2026 & 2027") and savings text ("Save $30") are not stored.
- A partially verified check is recorded as `Verified` at source level (the reconciliation contract);
  the detail text and the per-row Stale label carry the partial state.
- The official page address is shown as text, not a clickable link.
- Catalog-level staleness for other categories is unchanged (only Season Passes gained per-row Stale).
- No printed Memberships section / renewal offers (deferred by design).
- Windows: not executed (§13).

## 15. Final state

| | |
|---|---|
| Deterministic tests | **4538/4538** (Domain 303, Application 1190, Infrastructure 1002, Documents 469, Ui mixed 1426, run-alone 26 + 109 + 4 + 2 + 7) |
| Release build | `dotnet build CarowindsPassdown.slnx -c Release` — 0 warnings, 0 errors |
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged) |
| Version | `0.1.0-rc.8` (unchanged; no package built) |
| RC.8 artifacts | untouched |
| Recommendation | Ship as **`0.1.0-rc.9`** after Windows validation (§13): the Season Pass source is broken in RC.8 |
