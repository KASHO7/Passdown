# Dated haunted attractions (SCarowinds) source — investigation and adapter contract

Live investigation performed from this dev host on 2026-08-20, continuing from
`docs/retrieval/dated-admission-source.md` (Phase 3C) and `docs/retrieval/dated-fast-lane-source.md`
(Phase 3D). This documents what was actually observed against the official SCarowinds/haunted-
attraction checkout — nothing below is assumed from either prior phase without being independently
re-verified against real haunted-attraction traffic.

**Today (2026-08-20) is genuinely off-season** — the live SCarowinds event page states "Select
Nights, September 11 – November 1, 2026." This investigation was able to observe both off-season
behavior (querying today, and any date before 2026-09-11) and real in-season published behavior
(2026-09-11 onward is live and orderable right now), so this phase is not limited to fixture-only
seasonal reasoning — both states were directly confirmed live.

## Official entry URL

`https://www.sixflags.com/carowinds/events/scarowinds` — three "Buy Now" buttons: a non-dated 2027
Gold Pass (catalog, out of scope), a "SCarowinds Bundles" card, and a **"Haunted Attractions Pass"**
card. Traced with Playwright.

## Two distinct keywords found — only one is in scope

Clicking each card's "Buy Now" surfaces a different `cf-ca.buy.sixflags.com` keyword:

| Card | Keyword | In scope? |
|---|---|---|
| "SCarowinds Bundles" (park admission + maze access on select dates) | `fallwizard` | **No** — this is an admission-bundle variant (the same `Bundles,FallWizard` keyword tag was already visible on a `SnapWizardDailyTickets` package during Phase 3C's catalog investigation), not the checkout spec §5.1 names for haunted attractions. |
| "Haunted Attractions Pass" (maze-only access) | `HAPWizard` | **Yes** — this is exactly the checkout spec §5.1 calls out ("Haunted attractions: the official ... HAPWizard checkout"). |

This phase implements only `HAPWizard`.

## iframe behavior and endpoint (identical mechanism to Phase 3C/3D)

Same iframe-embedded checkout, same accesso Passport endpoint, independently re-confirmed via
network trace for this keyword:

```
POST https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar
```

The real client calls `getnewcartid` → `getcartsummary` → `getpackageswaps` first, exactly as
before, then `getkeywordcalendar` — **twice**, for two adjacent date windows
(`2026-08-20`→`2026-09-05`, then `2026-09-01`→`2026-10-03`; the wizard's own calendar-month
prefetch behavior, not something the adapter imitates).

### What's identical to Phase 3C/3D (independently re-verified, not assumed)

- Endpoint URL, HTTP method, envelope shape (`SERVICE`/`DATES`/`D`/`PS`/`P`/`CT`, same
  single-object-vs-array `D` behavior). Verified with `Object.keys(...)` on a live response —
  identical field set to both prior keywords, no extra/missing keys.
- No cart/session bootstrap required — a bare anonymous `fetch()` with no cart fields at all
  succeeds.
- `referer`/`origin` required — re-tested independently; omitting them gets the same plain-text 400
  `Bad Request`.
- `display_zero_capacity: "0"` — same value as `FastLaneOnly`, independently confirmed from real
  `HAPWizard` client traffic (not copied from Fast Lane).
- The same single ambiguous `status: "FAILED"`, `error_msg: "No dates found that meet requirements"`
  signal covers every negative case tried: **before the season starts** (today, 2026-08-20), **a
  non-operating night inside the season** (Sunday 2026-09-13 — SCarowinds runs select nights only,
  not every night), **after the season ends** (2026-11-15), and **next year's not-yet-announced
  season** (2027-09-11).
- One package per distinct name per date — like `FastLaneOnly` and *unlike* `SnapWizardDailyTickets`,
  no date has ever shown two packages sharing one name. Package ids repeat across dates that share
  a price tier (e.g. `10098520`/`10098526` appear on both 2026-09-11 and 2026-09-18 — matching
  nights, same price), the same pattern Fast Lane showed.
- No sold-out example was observed; `display_zero_capacity: "1"` produced no observable difference
  for the one in-season date checked.

### What's different (this is the point of re-investigating)

| | `SnapWizardDailyTickets` | `FastLaneOnly` | `HAPWizard` |
|---|---|---|---|
| Availability pattern | Daily, with occasional weekday-closure gaps | Daily, with occasional weekday-closure gaps | **Select nights only** — roughly Fri/Sat, with some added nights near the end of the run (e.g. Sun 09-27, and 10-02/03/04 all three) — a genuinely sparser, more irregular schedule than either prior keyword |
| Products (names observed) | `"Park Ticket"`, bundle names | `"FAST LANE RESERVE"`, `"FAST LANE ULTIMATE"` | `"Haunted Attractions Pass"`, `"Haunted Attractions Express Pass"` |
| `CT.id` | `"1"` / `"141"` | `"10"` | `"758"` / `"759"` |
| Published horizon at investigation time | 2026-11-08 | 2026-11-01 | 2026-11-01 (matches the marketing copy's stated season end date exactly — plausibly the same park-wide Halloween-season end date, not a shared technical constant) |
| Season boundary behavior | N/A (year-round product) | N/A (year-round product) | **A real "not yet started" state exists and was independently observed live**: today (before 09-11) returns the identical `FAILED` shape as every other negative case — the endpoint does not distinguish "season hasn't started" from "this particular night is dark" from "beyond the horizon" any more than it distinguishes those cases for the other two keywords |

Because `HAPWizard` has one package per name per date (never a duplicate-name pair), it follows
`DatedFastLaneSource`'s product-building approach (direct one-package-per-product mapping, no
by-name dedup/comparison-price grouping) — there is no live justification for admission's approach
here either, independently re-confirmed rather than assumed from Fast Lane.

## Direct HTTP vs. Playwright

Same conclusion, independently re-verified: a bare anonymous POST with the headers below succeeds.
`DatedHauntedSource` uses `RetrievalStrategy.DirectApi`.

## Request body

```jsonc
{
  "display_zero_capacity": "0",
  "keyword": "HAPWizard",
  "start_date": "2026-09-11",
  "end_date": "2026-09-11",       // single-day form for the primary call
  "request_type": "GetKeywordCalendar",
  "_version": "6.33.1",
  "application_id": "1500",
  "merchant_id": "1",
  "machine_id": "500",
  "agent_id": "5",
  "user_id": "5",
  "device": "desktop",
  "language": "en",
  "request_token": "<32 random uppercase hex chars>"
}
```

Built via the shared `AccessoKeywordCalendarRequest.BuildBody(keyword, displayZeroCapacity,
startDate, endDate)` — see "Reuse of AccessoKeywordCalendar" below.

## Response shape (in-season, real example)

```jsonc
{
  "SERVICE": {
    "status": "OK",
    "DATES": {
      "D": {
        "date": "2026-09-11",
        "lowest_price": "0.00",
        "PS": {
          "P": [
            {
              "id": "10098520", "name": "Haunted Attractions Pass", "event_id": "10049161",
              "available": "14", "capacity": "14",
              "CT": { "id": "758", "retail_amount": "10.00", "tax_amount": "0.00", "fee_amount": "0" }
            },
            {
              "id": "10098526", "name": "Haunted Attractions Express Pass", "event_id": "10049162",
              "available": "14", "capacity": "14",
              "CT": { "id": "759", "retail_amount": "49.00", "tax_amount": "0.00", "fee_amount": "0" }
            }
          ]
        }
      }
    }
  }
}
```

Off-season / non-operating-night / post-season / next-year response (identical for all four cases):

```jsonc
{ "SERVICE": { "status": "FAILED", "error_msg": "No dates found that meet requirements" } }
```

## Product identifiers, prices, and qualifiers

Same conclusions as Fast Lane, independently re-verified for this keyword rather than assumed:

- **Official product/package id**: `PS.P[].id`.
- **Display name / variant**: `PS.P[].name` (e.g. `"Haunted Attractions Pass"`,
  `"Haunted Attractions Express Pass"`). No separate type/variant field exists beyond `name`.
- **Exact-date price**: `PS.P[].CT.retail_amount`, read directly per package — no grouping needed
  (no live case has ever shown two packages sharing a name on one date for this keyword either).
- **Comparison/gate price**: not populated. No field in this response is ever a second price for
  the same package. Always `null` in production.
- **Price qualifier**: not populated, for the same reason as Phase 3C/3D — no field labels *why* a
  price is what it is. Always `null` in production.
- **`lowest_price` is still a decoy**: `"0.00"` in every live response observed; not read.

## Exact-date confirmation mechanism

Reuses `DatedFastLaneSource`'s two-call design exactly, chosen for the same reason (a genuine
requested/confirmed date mismatch needs a directly reachable `Error` path):

1. **Primary call**: single-day query for the requested date.
   - `status: "OK"` with exactly one entry whose parsed date equals the requested date → build
     products, `Verified`.
   - `status: "OK"` with an entry count other than exactly one, or a confirmed date that doesn't
     match the request → `Error`.
   - `status: "FAILED"` → step 2.
2. **Horizon call** (only when the primary call misses): wide window `[today, max(requestedDate,
   today + 120 days)]`. Compare the requested date against the maximum date the window actually
   returns:
   - beyond it → `NotPublished`.
   - on or before it → `Unavailable`. **This is the case that covers "season hasn't started yet"**
     (e.g. requesting today, 2026-08-20, against a window whose max date is 2026-11-01) as well as
     a genuine dark night inside the season (e.g. 2026-09-13) — both are real, recognized calendar
     dates for which haunted products are explicitly not being sold, which is exactly what
     `Unavailable` means per this phase's status definitions. A date *before* the season is
     therefore not a special case requiring its own status; it is classified by the same evidence
     used for every other negative date.
   - Same defensive `Error` fallback as the other two adapters if the horizon window itself
     somehow returns nothing (never observed live for any of the three keywords).

A date already before "today" is short-circuited straight to `Unavailable` with no network call,
same as the other two adapters.

## Off-season behavior and spec §7.1 seasonal rules

This adapter makes no permanent assumption about whether SCarowinds is active:

- Every `RetrieveAsync` call independently asks the live source for the requested date. There is no
  hardcoded season-date range anywhere in the adapter — the 2026-09-11…2026-11-01 window mentioned
  throughout this document is investigation evidence, not a constant the code checks against.
- A `FAILED`/off-season response for a given date produces `Unavailable` (or `NotPublished` if
  beyond the horizon) — never `Error` — so it is never mistaken for a retrieval failure. Per spec
  §7.1/§7.3, only an `Error` must never be treated as absence; a clean `Unavailable`/`NotPublished`
  result is exactly the "successful retrieval with no product for this date" signal Phase 2
  reconciliation needs to safely hide a seasonal section without guessing.
- A `Verified` result with real products (achievable today, live, for 2026-09-11 onward) gives Phase
  2 reconciliation genuine product data to activate a section with — this adapter does not decide
  visibility policy itself, only reports what the official source returned for one exact date.
- No product list is hardcoded anywhere in the adapter; both `"Haunted Attractions Pass"` and
  `"Haunted Attractions Express Pass"` (and any other name the response ever contains) flow through
  identically via the same package-iteration logic Fast Lane uses.

## Reuse of AccessoKeywordCalendar (Part B)

`DatedHauntedSource` reuses exactly the same shared pieces `DatedFastLaneSource` already reuses
from Phase 3C — now proven common across **three** independently investigated keywords, not two:

- `AccessoKeywordCalendarRequest.Endpoint`, `.Headers`, `.BuildBody(keyword, displayZeroCapacity,
  startDate, endDate)`.
- The response envelope DTOs (`AccessoKeywordCalendarApiResponse` and friends) and
  `SingleOrArrayConverter<T>`.

No changes were made to the shared file for this phase — every field and behavior it models was
already proven identical for `HAPWizard` too, so nothing new needed to be added to it. `keyword`
(`"HAPWizard"`) and `displayZeroCapacity` (`"0"`, matching Fast Lane's value — independently
confirmed for this keyword, not copied) remain adapter-supplied parameters, as designed.

**Not shared**, same reasoning as Phase 3D: the two-call control flow/status decisions and the
product-building logic live in `DatedHauntedSource` itself. It is structurally very similar to
`DatedFastLaneSource`'s (same two-call shape, same one-package-per-product mapping) because the
live evidence for both keywords genuinely supports the same approach — but per this phase's
explicit instruction not to add haunted-specific logic to the shared helper, and to keep
status/availability decisions out of shared code, this logic is duplicated rather than factored into
a third shared layer. A generalized "two-call accesso adapter base class" was considered and
rejected: it would encode control-flow assumptions (single-day-primary-then-horizon) that happen to
fit two keywords so far, not a mechanic proven necessary by the transport shape itself the way the
envelope DTOs are.

## Limitations

- `_version: "6.33.1"` is hardcoded, same caveat as Phase 3C/3D.
- No sold-out example was ever observed for `HAPWizard`; `available`/`capacity`/`bookings_sold_out`
  are not read.
- `NotPublished` vs. `Unavailable` remains a heuristic (comparing against a horizon probe's maximum
  date), not something the source labels explicitly — same limitation as the other two adapters,
  now independently re-confirmed for a third, sparser availability pattern.
- The live investigation happened to occur while SCarowinds is genuinely off-season, which made it
  possible to observe *both* off-season and in-season behavior for the same keyword in one
  investigation — a coincidence of timing, not something the adapter depends on. If this adapter is
  revisited well after the 2026 season ends and before a 2027 season is announced, only the
  off-season path will be directly re-observable at that time.
- `fallwizard` (the admission+maze bundle keyword) was found but deliberately not investigated
  further or implemented — out of scope per spec §5.1's specific naming of the HAPWizard checkout
  for haunted attractions.
