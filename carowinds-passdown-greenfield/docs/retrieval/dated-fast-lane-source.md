# Dated Fast Lane source — investigation and adapter contract

Live investigation performed from this dev host on 2026-08-20, continuing from
`docs/retrieval/dated-admission-source.md` (Phase 3C). This documents what was actually observed
against the official Fast Lane checkout — nothing below is assumed from Phase 3C without being
independently re-verified against real Fast Lane traffic, per this phase's explicit instructions.

## Official entry URL

`https://www.sixflags.com/carowinds/fast-lane` → "Buy Now" under the **"Daily Fast Lane"** card
(the page's *other* card, "All Season Fast Lane", is a catalog product — not dated, out of scope
for this phase). Traced with a real headless Chromium session (Playwright).

## iframe behavior

Identical mechanism to Phase 3C: the "Buy Now" click loads
`https://cf-ca.buy.sixflags.com/packageList/keyword/FastLaneOnly?` **in an iframe** embedded in the
`daily-tickets`-style page (`<iframe src="https://cf-ca.buy.sixflags.com/embed/shim.html?...">`),
not a full-page navigation or popup. This matches Phase 3C's finding, not assumed from it — the
same `page.frames()` enumeration technique was used to find and confirm this frame independently
for this flow.

## Observed request flow and endpoint

The **same accesso Passport endpoint** as Phase 3C is called:

```
POST https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar
```

Directly confirmed via network trace — this is not assumed from Phase 3C. The real client also
calls `getnewcartid` → `getcartsummary` → `getpackageswaps` first, exactly as it did for dated
admission, before `getkeywordcalendar`. As with Phase 3C, this was independently tested and found
**unnecessary**: a bare `fetch()` with no cart/session fields at all, no cookies, and no prior
calls returns the identical successful calendar response. `referer`/`origin` headers are still
required — an otherwise-identical request without them was independently re-tested here and
rejected with the same plain-text 400 `Bad Request`.

### What's identical to Phase 3C (independently re-verified, not assumed)

- Endpoint URL, HTTP method (POST), content type.
- No cart/session bootstrap required.
- `referer: https://cf-ca.buy.sixflags.com/` / `origin: https://cf-ca.buy.sixflags.com` required;
  same `com-accessopassport-*` headers accepted.
- Response envelope shape byte-for-byte: `SERVICE.status`, `SERVICE.error_msg`,
  `SERVICE.DATES.D` (single object for a one-day request, array for a multi-day request — the
  exact same shape-switching behavior, re-confirmed live for this keyword specifically), each date
  entry's `PS.P[]` package list, and each package's `id`/`name`/`CT.retail_amount` fields. Verified
  by dumping `Object.keys(...)` on a live Fast Lane response package/CT/date-entry and comparing
  against Phase 3C's — identical field sets, no extra or missing keys.
- `SERVICE.status: "FAILED"`, `error_msg: "No dates found that meet requirements"` is the same
  single ambiguous negative signal for a closed/gap date, a date well beyond the published
  calendar, and a past date alike (all three re-tested independently for `FastLaneOnly`).
- The wide-window-then-compare-to-max-date technique for distinguishing `NotPublished` from
  `Unavailable` (see below) was independently re-proven for this keyword: a `2026-08-20` →
  `2027-01-17` probe returned real data through `2026-11-01` (Phase 3C's admission horizon was
  `2026-11-08` — close but independently different, confirming this is a per-keyword horizon, not
  a shared constant).

### What's different from Phase 3C (this is the whole point of re-investigating)

| | Dated admission (`SnapWizardDailyTickets`) | Dated Fast Lane (`FastLaneOnly`) |
|---|---|---|
| `keyword` | `SnapWizardDailyTickets` | `FastLaneOnly` |
| `display_zero_capacity` | `"1"` | `"0"` — directly observed from real client traffic, not copied from admission |
| Product naming per date | Same name can repeat twice per date at two different prices (e.g. two `"Park Ticket"` entries) | Every observed date has **exactly one package per distinct name** — no repeated names were ever observed |
| Product names | `"Park Ticket"`, bundle names | `"FAST LANE RESERVE"`, `"FAST LANE ULTIMATE"` — both returned by the *same* `FastLaneOnly` keyword call, no separate keyword needed per variant |
| `CT.id` (customer-type id) | `"1"` / `"141"` | `"10"` for both Fast Lane products |
| `CT.tax_amount` | Non-zero (e.g. `"3.25"`) | `"0.00"` in every response observed |
| Package `id` reuse | Distinct id per specific date observed | **Ids repeat across different dates** that share the same price tier (e.g. package id `10094823` appeared for `2026-08-21`, `2026-08-23`, and `2026-08-30` — a recurring weekday/weekend price-tier pattern, not a unique-per-date id) |
| Same-day product set | Only the "day-of" package remains once the date is today | Both `FAST LANE RESERVE` and `FAST LANE ULTIMATE` remain available on the requested date equal to "today" — no collapse to a single package |
| Published horizon at investigation time | `2026-11-08` | `2026-11-01` |

**Because product names never repeat within a date, the admission adapter's "group by name, take
the lower price as exact-date price and the higher as `ComparisonPrice`" logic has no live
justification for Fast Lane and is not reused.** Each `FastLaneOnly` package becomes its own
product record directly — see "Product identifiers, prices, and qualifiers" below.

## Direct HTTP vs. Playwright

Same conclusion as Phase 3C, independently re-verified: a bare `fetch()`/curl-equivalent POST with
no browser, no cookies, and the headers above succeeds. `DatedFastLaneSource` uses
`RetrievalStrategy.DirectApi`.

## Request body

Two live-observed request shapes are used, matching the two calls the adapter actually makes
(see "Exact-date confirmation" below) — both share the common accesso envelope fields, differing
only in `keyword`, `display_zero_capacity`, `start_date`, and `end_date`:

```jsonc
{
  "display_zero_capacity": "0",
  "keyword": "FastLaneOnly",
  "start_date": "2026-08-21",
  "end_date": "2026-08-21",        // single-day form: start_date == end_date
  "request_type": "GetKeywordCalendar",
  "_version": "6.33.1",
  "application_id": "1500",
  "merchant_id": "1",
  "machine_id": "500",
  "agent_id": "5",
  "user_id": "5",
  "device": "desktop",
  "language": "en",
  "request_token": "<32 random uppercase hex chars, client-generated per request>"
}
```

`_version: "6.33.1"` — same SPA build version observed live for this flow too (same checkout
application serves both products); same hardcoded-value limitation as Phase 3C applies.

## Response shape

Identical envelope to Phase 3C (see table above). Example (single-day form, `D` as an object):

```jsonc
{
  "SERVICE": {
    "status": "OK",
    "DATES": {
      "D": {
        "date": "2026-08-21",
        "lowest_price": "0.00",
        "PS": {
          "P": [
            {
              "id": "10094823", "name": "FAST LANE RESERVE", "event_id": "10048996",
              "available": "14", "capacity": "14",
              "CT": { "id": "10", "retail_amount": "59.00", "tax_amount": "0.00", "fee_amount": "0" }
            },
            {
              "id": "10093917", "name": "FAST LANE ULTIMATE", "event_id": "10048996",
              "available": "14", "capacity": "14",
              "CT": { "id": "10", "retail_amount": "99.00", "tax_amount": "0.00", "fee_amount": "0" }
            }
          ]
        }
      }
    }
  }
}
```

## Product identifiers, prices, and qualifiers

- **Official product/package id**: `PS.P[].id` (e.g. `"10094823"`).
- **Display name / variant**: `PS.P[].name` (e.g. `"FAST LANE RESERVE"`, `"FAST LANE ULTIMATE"`).
  There is no separate "product type/variant" field distinct from `name` anywhere in the observed
  schema — `name` *is* the only variant signal the source provides, so
  `DatedFastLaneProduct` does not add a fabricated separate variant/category field (spec's "do not
  infer fields the runtime source does not explicitly provide").
- **Exact-date price**: `PS.P[].CT.retail_amount`, parsed as a decimal — directly read per package,
  no grouping/dedup step (see table above: no live case has ever shown two packages sharing one
  name on one date for this keyword).
- **Comparison/gate price**: **not populated.** No field in this response is ever a second price
  for the same package (unlike admission's genuinely observed duplicate-name pairs), so there is
  nothing here to attribute a `ComparisonPrice` to. Always `null` in production.
- **Price qualifier**: **not populated**, for the same reason as Phase 3C's hardened behavior — no
  field in this response labels *why* a price is what it is. Always `null` in production. (This
  phase never implemented the qualifier-inference mistake Phase 3C's hardening task removed; there
  was never live evidence for one here either.)
- **Applicable date**: attached to each `DatedFastLaneProduct` directly, same reasoning as
  `DatedAdmissionProduct.ApplicableDate`.

### `lowest_price` is still a decoy

Same as Phase 3C: `DATES.D[].lowest_price` was `"0.00"` in every live Fast Lane response observed.
Not read by the adapter.

## Exact-date confirmation mechanism (differs from Phase 3C)

Phase 3C queries one wide window (`[today, max(requestedDate, today+120)]`) per call and searches
the returned array for an exact date match. Phase 3D uses a **two-call design** instead, chosen
specifically to make a genuine requested/confirmed date mismatch a distinctly reachable `Error`
case (per this phase's explicit status-behavior requirements), while still making only one call in
the common case:

1. **Primary call**: single-day query, `start_date == end_date == requestedDate`.
   - `status: "FAILED"` → fall through to step 2 (need more information to classify).
   - `status: "OK"` with a response that does not contain **exactly one** date entry → `Error`
     (unexpected shape; a single-day query should never yield more than one entry — the same
     "exactly one" contract `ParkHoursSource` already enforces for its own daily form).
   - `status: "OK"` with exactly one entry whose parsed `date` **does not equal** `requestedDate`
     → `Error` (date mismatch — the source affirmatively returned data, but not for the date
     asked). This is the literal, reachable `Error` case this phase's spec calls for; it was not
     observed live (this endpoint has never been seen to do this), but the adapter checks for it
     defensively and rejects rather than trusting a lone object shape as automatically being the
     requested date.
   - `status: "OK"` with exactly one entry whose date matches → build products from that entry;
     `Verified`.
2. **Horizon call** (only reached when the primary call is `FAILED`): wide window
   `[today, max(requestedDate, today + 120 days)]`, same padding constant and rationale as Phase
   3C (independently re-justified above by this phase's own `2026-08-20`→`2027-01-17` probe).
   Compare `requestedDate` against the maximum date actually returned:
   - beyond it → `NotPublished`.
   - on or before it (i.e. a real gap within the currently published range, matching the
     `08-24`…`08-28`-style closures observed for both keywords) → `Unavailable`.
   - Same defensive `Error` fallback as Phase 3C if this wide window itself somehow returns
     nothing at all (never observed live for either keyword).

A date already before "today" is short-circuited straight to `Unavailable` with no network call at
all — independently re-confirmed live for `FastLaneOnly` (`2026-08-19` returned the same `FAILED`
"No dates found" shape).

## Whether NotPublished and Unavailable can be objectively distinguished

Same limitation as Phase 3C, independently re-confirmed for this keyword: the endpoint itself gives
only one negative signal for both "closed/gap date" and "date beyond the horizon" and "past date."
The adapter distinguishes them the same way — by comparing the requested date to the maximum date
actually returned by a wide horizon probe — which is an evidence-based technique proven correct
against real data for *both* keywords now, not a guess specific to either one.

## Reusable pieces shared with Phase 3C (Part C)

Extracted into a shared, keyword-agnostic file
(`Infrastructure/Retrieval/Sources/AccessoKeywordCalendar.cs`) **only because both live
investigations proved these pieces byte-for-byte identical**, not merely because both sources use
accesso:

- The JSON response **envelope DTOs** (`SERVICE`/`DATES`/`D`/`PS`/`P`/`CT` field names and nesting)
  and the `SingleOrArrayConverter<T>` that normalizes `D`'s single-object-vs-array shape. This is
  pure transport shape, not business interpretation — both adapters still decide independently what
  to do with the parsed packages.
- The endpoint URL and the public `com-accessopassport-*`/`referer`/`origin` header set — proven
  identical and required by both.
- The common request-body envelope fields (`request_type`, `_version`, `application_id`,
  `merchant_id`, `machine_id`, `agent_id`, `user_id`, `device`, `language`, and per-request
  `request_token` generation) — proven byte-for-byte identical between both keywords. `keyword` and
  `display_zero_capacity` remain per-adapter parameters because they are **not** identical
  (directly observed: `"1"` vs `"0"`).

**Not shared**, per this phase's explicit instructions and because there is no live justification:

- `RetrieveAsync`'s control flow / status-decision logic (single wide-window search vs. two-call
  primary-then-horizon design — genuinely different strategies, not the same code with different
  inputs).
- Product-building logic (admission's by-name dedup/comparison-price grouping has no Fast Lane
  equivalent; Fast Lane's is a direct one-package-per-product mapping).
- `SourceEvidence`/status-message construction helpers (kept private and per-adapter, matching this
  codebase's existing `ParkHoursSource`/`DatedAdmissionSource` precedent of not sharing these
  either).

## Dynamically offered products

The adapter does not hard-code `"FAST LANE RESERVE"`/`"FAST LANE ULTIMATE"` anywhere — it builds
one `DatedFastLaneProduct` per package the response actually returns for the confirmed date, however
many there are. If a third variant (or fewer) appears live in the future, it flows through
unchanged; no test or fixture asserts an exact fixed set beyond what a given fixture's JSON
contains.

## Known limitations

- `_version: "6.33.1"` is hardcoded, same limitation and same caveat as Phase 3C.
- No sold-out example was ever observed for `FastLaneOnly` either (`available` always equaled
  `capacity`; toggling `display_zero_capacity` between `"0"` and `"1"` produced no observable
  difference in the one date checked) — `available`/`capacity`/`bookings_sold_out` are not read,
  same as Phase 3C.
- The two-call design costs one extra request in the (uncommon) case a requested date isn't
  currently published — an intentional tradeoff for making date-mismatch a directly testable
  `Error` path rather than folding it into the same wide-window search Phase 3C uses.
- As with Phase 3C, `NotPublished` vs. `Unavailable` classification is an evidence-based heuristic
  (comparing against the maximum date a horizon probe returns), not something the source labels
  explicitly — if the official calendar's true availability pattern ever becomes more irregular
  than what was observed (a single contiguous published range from "today" forward), this
  heuristic could misclassify a date that's far in the future but coincidentally beyond a
  temporary near-term gap. No such case was observed live.
- This phase implements only the `FastLaneOnly` keyword (both Fast Lane Reserve and Fast Lane
  Ultimate come from it). The `HAPWizard` (haunted attractions) keyword referenced in spec §5.1 is
  out of scope here and was not investigated.
