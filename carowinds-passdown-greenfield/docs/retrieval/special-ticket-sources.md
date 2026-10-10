# Special-ticket sources — Bring-A-Friend Duo, Military/First Responder

Live investigation performed 2026-08-27 (Phase 13C), continuing from the Phase 13A gap flagged in
`docs/retrieval/catalog-source-inventory.md` ("Bring-A-Friend Duo and Military/First-Responder
tickets do not appear in any adapter, fixture, or doc"). Everything below was fetched live from
`sixflags.com`/`ecomm.api.cf.accessoticketing.com` during this phase — nothing is carried over from
the stale 2026-08-20 investigation notes without being independently re-verified, per CLAUDE.md's
retrieval rules.

## Celebrate America! Bring-A-Friend Duo

### Catalog surface

Live on `https://www.sixflags.com/carowinds/daily-tickets` (not `special-offers`, where the Phase
13A notes speculated it might live — re-checked, it is not there). Uses the same `productv2-card`
markup every other catalog adapter already parses:

```
data-ab-product = "o1gUjW0O273eyMG69L54eV"
title           = "Celebrate America! Bring-A-Friend Duo"
subtitle        = "Two friends, one price. Passholders & members only."
badge           = "Limited Time Offer!"
price-before    = "Two Friends Just"
price           = "$50"
(no strikethrough/comparison element)
accesso keyword = "SUBAF50"
```

Full body text: *"Passholders bring two (2) friends for just $50\* for a limited time. Any day
through 9/7."* — confirming the $50/2-people/$25-per-friend bundle semantics locked in
`docs/retrieval/pricing-semantics.md`'s worked example 2.

### Dated surface — genuinely discoverable, unlike ordinary BAF

The card's own `data-accesso-keyword` (`SUBAF50`) was tried directly against the same
`getkeywordcalendar` endpoint every dated source already calls
(`https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar`), with no
authentication — a plain anonymous request, same headers as every other dated source. It **works**
and returns real, date-scoped pricing:

```jsonc
// keyword=SUBAF50, start_date=end_date=2026-08-29
{
  "SERVICE": {
    "status": "OK",
    "DATES": { "D": {
      "date": "2026-08-29",
      "PS": { "P": {
        "id": "10097207",
        "name": "Celebrate America! Bring-A-Friend Duo Offer ",
        "CT": { "retail_amount": "25.00", ... }
      }}
    }}
  }
}
```

Re-confirmed across a 5-date window (Aug 29, Aug 30, Sep 5, Sep 6, Sep 7 2026) — every date returned
the identical package id (`10097207`) and price (`$25.00`). This `$25.00` is the **per-person**
checkout charge for one passholder-accompanied friend ticket on that date — i.e. exactly the
"$25 per friend" half of the catalog card's "$50 for 2" framing, confirmed by the checkout math
matching the marketing card's own stated split. `DatedBringAFriendDuoSource` (new,
`src/Carowinds.Passdown.Infrastructure/Retrieval/Sources/DatedBringAFriendDuoSource.cs`) implements
this exactly like `DatedFastLaneSource` (primary single-day request, horizon fallback), registered
under the new `SourceCategory.BringAFriendDuo`.

**A new shared-plumbing bug was found and fixed by this discovery**: `AccessoKeywordCalendarPackageList.P`
was typed as a plain `List<T>` with no single-object/array normalization, unlike `DATES.D` (which
already had a `SingleOrArrayConverter`). Every previously-onboarded keyword's fixtures always had
≥2 packages per date (an array), so this never surfaced — `SUBAF50`'s single-package-per-date shape
(`"P":{...}`, a bare object) is the first real evidence of the same duality applying to `PS.P` too.
Fixed in `AccessoKeywordCalendar.cs` by applying the same `SingleOrArrayConverter<AccessoKeywordCalendarPackage>`
to `P` — a shared-plumbing fix, not specific to this one keyword, so every dated source benefits.

## Military / First Responder

Reached via the site's own "Military & First Responders" nav tab —
`https://www.sixflags.com/carowinds/military-discounts` — discovered live, not guessed. Plain
anonymous HTTP GET, no cookie/session/authentication of any kind, same as every other catalog page.

This page uses **neither** existing card shape exactly:

```html
<h3 class="font-medium text-blue-dark type-20-600 break-words">Military &amp; First Responder Tickets</h3>
...
<div class="text-gray type-14-400">Starting From</div>
<div class="break-words"><span class="type-20-600">$37</span></div>
<div class="flex flex-col gap-0">
  <div class="text-gray type-14-400">Gate Price</div>
  <span class="type-20-600 line-through text-black">$80</span>
</div>
...
<button data-accesso-keyword="MilitaryWizard">Buy Now</button>
```

The title/price element classes (`h3.font-medium.text-blue-dark.type-20-600` /
`span.type-20-600`) exactly match the general add-ons page's simple card shape
(`GeneralAddOnHtmlParser`'s target), but this page additionally carries a qualifier ("Starting
From") and a "Gate Price" comparison sub-block — a shape neither existing page has. Per the
established "only extract into a shared helper once proven on 2+ pages" convention
(`docs/retrieval/catalog-source-inventory.md` Part B), a new, dedicated, separately-evidenced parser
was written (`MilitaryFirstResponderHtmlParser.cs`) rather than generalizing either existing one — it
reuses `ProductCardHtmlParser`'s already-shared, already-`internal`-public utilities
(`ExtractAttributeValue`/`ParseAmount`) instead of duplicating them.

No `data-ab-product`/Sanity CMS id exists on this page — identity falls back to the accesso keyword
(`MilitaryWizard`), same fallback tier every other keyword-only page already uses.

The real page renders this one card twice (a responsive-breakpoint duplicate, the identical pattern
`CatalogCardMapper`'s existing dedup already handles for the general add-ons page) — confirmed live
and reproduced in the fixture.

Page-level eligibility prose (requires ID.me verification; cannot be combined with other offers) is
**not** attached to the per-product `ObservedProduct` — it lives outside any card fragment, and
attaching it would invent a per-product field the source doesn't actually expose there (spec §5.2).
Eligibility is instead preserved structurally: this product is tracked under its own
`SourceCategory.MilitaryFirstResponder` (never `DatedAdmissionTickets`), so it can never overwrite or
be confused with ordinary Daily Admission, and its own display name ("Military & First Responder
Tickets") already signals the restriction to whoever reads it.

## Ordinary (non-Duo) Bring-A-Friend

> **Superseded by Phase 15A** (see `docs/phase13-hardening.md`'s Phase 15A section for the full
> write-up). This section is kept for historical accuracy — it records exactly what Phase 13C tried
> and why it concluded (incorrectly, as it turned out) that the exact dated price was unreachable
> anonymously — but its **Conclusion** below no longer reflects production behavior. A real adapter
> (`DatedBringAFriendSource`, `SourceCategory.DatedBringAFriend`) exists as of Phase 15A.

The regression scenario `docs/phase13-hardening.md` locked in (Aug 29 2026: exact dated $39.00,
comparison $80.00) describes **ordinary** Bring-A-Friend, not the Duo offer above. Phase 13C's live
investigation found:

- The ordinary BAF catalog card's own "Buy Now" button (`special-offers` page) carries accesso
  keyword `SnapWizard` — the **same** keyword the plain "Park Ticket"/daily-admission card uses, not
  a dedicated BAF keyword.
- Calling `getkeywordcalendar` with `keyword=SnapWizardDailyTickets` (the keyword
  `DatedAdmissionSource` already queries) for Aug 29, 2026 returns only ordinary "Park Ticket"
  packages ($55.00/$59.00) — no Bring-A-Friend line item at all.
- A reasonable set of guessed alternate keywords (`SnapWizard`, `SUBAF`, `SUBAF1`, `SUBAF25`,
  `BAFWizard`, `SnapWizardBAF`, `SnapWizardBringAFriend`, `SnapWizardPassholder`,
  `PassholderBringAFriend`, `PassholderBAF`) were tried directly against the same endpoint — every
  one either returned the identical Park Ticket data (the shared keyword) or `"Invalid keyword
  value"`/`"No dates found"`. **Missing from this list**: `BAFWizardDailyTickets` — the "Wizard +
  DailyTickets" suffix pattern every other admission-family keyword already follows
  (`SnapWizardDailyTickets`), never tried for BAF specifically.
- The catalog card's own subtitle ("An exclusive for passholders and members") and fine print ("Must
  be accompanied by a valid Season Passholder") were read as meaning the discounted rate is applied
  only after an authenticated passholder login inside the shared `SnapWizard` checkout flow. **This
  reading was wrong** — see Phase 15A below.

**Phase 13C's conclusion (superseded)**: ~~ordinary Bring-A-Friend's exact dated price is not
retrievable from any anonymous official surface~~. **Phase 15A finding**: the anonymous Special
Offers page's own "Select a date to begin!" calendar (reached via the site's "Bring-A-Friend Ticket"
nav link — no login prompt at any point) calls `getkeywordcalendar` with keyword
`BAFWizardDailyTickets`, which anonymously returns real, date-scoped "Bring-A-Friend Ticket" pricing
(confirmed live for 2026-08-29 = $39.00 and 2026-08-30 = $35.00, matching the locked regression
values exactly). No credentials, cookies, or passholder session were ever sent or required. Full
detail: `docs/phase13-hardening.md` Phase 15A section.

## Product identity collision check (Part L)

Before onboarding, every new official id/keyword was checked against every id currently in use by
any existing adapter (grepped across `src/`/`fixtures/`):

| New id/keyword | Category | Checked against | Collision? |
|---|---|---|---|
| `SUBAF50` (BAF Duo, dated) | `BringAFriendDuo` | `SnapWizardDailyTickets`, `FastLaneOnly`, `HAPWizard` | No — distinct keyword, distinct category |
| `o1gUjW0O273eyMG69L54eV` (BAF Duo, catalog Sanity id) | `Promotions` | `449e6fe8-...` (ordinary BAF), `9d031bb4-...`/`550e3db6-...` (Gold Pass, two categories) | No — distinct id |
| `MilitaryWizard` | `MilitaryFirstResponder` | Every other keyword above | No — distinct keyword, distinct category |

No collision found for any new id in either the id tier or the name tier. `CatalogProductKey`'s
category-scoping (`{Category}:{Value}`) means even a hypothetical future id collision across
categories would still be safe — only a collision *within* the same category's id tier is a real
risk, and none of the three new ids/keywords share a category with any pre-existing one that also
uses the id tier for the same value.
