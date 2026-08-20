# Dated admission ticket source — investigation and adapter contract

Live investigation performed from this dev host on 2026-08-20, continuing from
`docs/phase1-discovery.md` (which found the `cf-ca.buy.sixflags.com` checkout shell to be an empty
Angular SPA behind Cloudflare, with the SnapWizard/FastLaneOnly/HAPWizard deep-link paths not yet
individually probed). This documents what was actually observed against the official site —
nothing below is guessed or carried over from the previous application.

## Official entry URL

`https://www.sixflags.com/carowinds/daily-tickets` → "Buy Now" on the first ("select dates")
ticket card. Traced with a real headless Chromium session (Playwright) recording every network
request from page load through date selection.

## Observed request flow

1. The "Buy Now" click fires a real navigation to
   `https://cf-ca.buy.sixflags.com/packageList/keyword/SnapWizard?`, which the site loads **as an
   iframe embedded in the daily-tickets page** (`<iframe src="https://cf-ca.buy.sixflags.com/embed/shim.html?...">`),
   not a full-page redirect or popup — confirmed by enumerating `page.frames()` after the click.
2. That iframe immediately client-redirects to
   `https://cf-ca.buy.sixflags.com/snap-calendar-wizard/SnapWizard/SnapWizardDailyTickets` — the
   SnapWizard checkout's dated-admission-tickets keyword. This confirms the exact checkout path
   spec §5.1 references ("the official SnapWizard checkout") without guessing it.
3. The wizard renders three ticket products for whatever date is currently selected ("Carowinds
   Park Ticket", "Ticket + Meal, Snack & Drink Bundle", "Ticket + Fast Lane Reserve Bundle") and a
   "Filter By Date" calendar control.
4. **Selecting a date in that calendar makes no new network request.** Every date cell's own
   `aria-label` (e.g. `"August 22, 2026 $55"`) already carries that date's price, because the full
   pricing calendar was fetched once, up front, before any date was clicked.

## The actual pricing endpoint

The per-date pricing shown throughout the wizard — the calendar cell prices *and* the product
cards once a date is picked — all comes from one call, fired as soon as the wizard loads (before
any user interaction):

```
POST https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar
```

This is accesso Passport's own ecommerce API (the platform backing the SnapWizard checkout;
`com-accessopassport-*` request headers and `accesso-pay` script bundles confirm this directly,
not inferred). It is a **separate host from `cf-ca.buy.sixflags.com`** — the Cloudflare
bot-management cookie phase 1 flagged on the checkout shell does not apply to this API host.

### Confirmed: no cart/session bootstrap needed, no Playwright needed

The real client also calls `getnewcartid` and `getcartsummary` first (visible in the network
trace) and includes the resulting `cart_id`/`cart_key`/`session_id` in the `getkeywordcalendar`
body. Directly tested by omitting them entirely: **`getkeywordcalendar` returns the identical
successful response with no cart/session fields at all.** They are not required.

Also directly tested with a bare Node `fetch()` — no browser, no cookies:

- A request **without** `referer`/`origin` headers was rejected: HTTP 400, plain-text `Bad
  Request` body.
- The identical request **with** `referer: https://cf-ca.buy.sixflags.com/` and
  `origin: https://cf-ca.buy.sixflags.com` added succeeded: HTTP 200 with the full calendar JSON.

No cookie, API key, or login was ever required — only these static, publicly-visible identity
headers (the same values the site's own shipped JS sends for every visitor):

| Header | Value |
|---|---|
| `com-accessopassport-client` | `cedarfair` |
| `com-accessopassport-language` | `en` |
| `com-accessopassport-merchant-id` | `1` |
| `com-accessopassport-app-id` | `1500` |
| `referer` | `https://cf-ca.buy.sixflags.com/` |
| `origin` | `https://cf-ca.buy.sixflags.com` |

This means **Playwright is not required** for this source: §6.4's "prefer a dependable direct
endpoint" applies cleanly here, unlike phase 1's expectation (based on the checkout shell alone)
that `cf-ca.buy.sixflags.com` would need browser automation. `DatedAdmissionSource` uses
`RetrievalStrategy.DirectApi`.

### Request body

```jsonc
{
  "display_zero_capacity": "1",
  "keyword": "SnapWizardDailyTickets",
  "start_date": "2026-08-20",   // yyyy-MM-dd
  "end_date": "2026-10-03",     // yyyy-MM-dd, inclusive range
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

`keyword` is the confirmed value read from the live redirect path itself
(`SnapWizard**DailyTickets**`), not guessed — it is what distinguishes this dated-admission
product line from Fast Lane (`FastLaneOnly`) or haunted-attraction (`HAPWizard`) keywords, which
this phase does not implement (scope restriction).

`_version` is the checkout SPA's own build version, read directly off the live asset URLs
(`scripts-4e2c3daefa.js?cacheBuster=6.33.1`). It is hardcoded as a known limitation — see below.

### Response shape

Requesting a **single** day (`start_date == end_date`) returns `DATES.D` as one JSON **object**.
Requesting a **range** returns `DATES.D` as a JSON **array** of that same object shape — both
forms directly observed, not assumed; `DatedAdmissionSource` normalizes both via
`SingleOrArrayConverter<T>` rather than guessing which one a given request will produce.

```jsonc
{
  "SERVICE": {
    "status": "OK",                 // or "FAILED"
    "error_msg": "...",             // present when FAILED
    "DATES": {
      "D": [
        {
          "date": "2026-08-21",           // yyyy-MM-dd — matches the requested date
          "lowest_price": "0.00",         // always "0.00" in every live response observed; unused, see below
          "PS": {
            "P": [
              {
                "id": "10094885",
                "name": "Park Ticket",
                "event_id": "10048997",
                "available": "14",
                "capacity": "14",
                "CT": { "id": "1", "retail_amount": "49.00", "tax_amount": "3.25", "fee_amount": "0" }
              },
              {
                "id": "10094886",
                "name": "Park Ticket",
                "event_id": "10048997",
                "available": "14",
                "capacity": "14",
                "CT": { "id": "1", "retail_amount": "55.00", "tax_amount": "3.64", "fee_amount": "0" }
              }
            ]
          }
        }
      ]
    }
  }
}
```

A date can carry **two packages sharing the same `name`** (e.g. two "Park Ticket" entries above).
Neither `getkeywordcalendar` package object (`PS.P[]` or its nested `CT`) carries any field that
labels *why* — no rate name, no qualifier, no tier text. This response shape is reproduced exactly
in `fixtures/dated-admission/published-window.json`. Directly observed (a plain response-shape
fact, not a labeling question): on the requested date that equals "today", only **one** package per
name is offered — there is no second, higher-priced package once the date has already arrived.

### Investigation-only: what the second price likely represents

Purely to understand the shape (not used by production code — see the "PriceQualifier is not
populated" bullet under "Product identifiers, prices, and qualifiers" below), this second price
was cross-referenced during investigation against the site's
*separate* static package catalog
(`GET https://ecomm.api.cf.accessoticketing.com/static-api/bootstrap?m=CF-CA`, fetched once,
by hand, for this investigation only): the higher-priced package of each pair carried that
catalog's own `"SameDayPricing"` keyword tag, while the lower-priced one did not — plausibly
matching spec §6.1's description of this data group ("Prices may change for day-of purchase"). This
is documented here as investigation context only. It is **not** official-source evidence for any
individual live response, because the catalog that backs it is a separate, unauthenticated-at-that-
call-site fetch the adapter does not make at runtime — attaching a label derived from a different
request to this response's data would be exactly the kind of silent substitution spec §5.2
prohibits. Production code does not perform this cross-reference and does not attach this or any
other label.

## Product identifiers, prices, and qualifiers

- **Official product/package id**: `PS.P[].id` (e.g. `"10094885"`) — stable per package, distinct
  per date and per price tier.
- **Display name**: `PS.P[].name` (e.g. `"Park Ticket"`).
- **Exact-date price**: `PS.P[].CT.retail_amount`, parsed as a decimal. Confirmed against the live
  page's own rendered UI (selecting "Friday, Aug 21, 2026" displayed exactly `"$49.00"`, matching
  the *lower* of the two raw `retail_amount` values for that date's "Park Ticket" pair) — this is
  pre-tax; the page's own fine print says taxes/fees are added later, so the adapter does not add
  `CT.tax_amount`/`CT.fee_amount` into this figure.
- **Comparison/gate price**: when a date has two same-named packages, the adapter takes the lower
  as the exact-date price and retains the higher, directly-observed price from the same response as
  `ComparisonPrice` — a real second number this response actually returned, not fabricated.
- **PriceQualifier is not populated.** `DatedAdmissionProduct.PriceQualifier` is always `null` in
  production. The live endpoint never returns an explicit field naming why a second price exists
  (see "Investigation-only: what the second price likely represents" above), and spec §5.2 prohibits inventing or silently substituting
  pricing semantics the source didn't actually provide — so the adapter does not guess a label like
  `"Same-day pricing"`, `"Online Price"`, or `"Sale Price"`. If the endpoint is ever observed to
  return an explicit qualifier field for a price, the adapter should read that field directly
  instead of continuing to leave this `null` — not resume inferring one structurally.
  `CT.gate_price` (a static, non-dated MSRP figure used only for the daily-tickets page's un-dated
  marketing card, e.g. its crossed-out "$80 Gate Price") lives in the same out-of-scope static
  catalog mentioned above and is not read by this adapter either.
- **Requested/applicable date**: attached to each `DatedAdmissionProduct` directly (see
  `DatedAdmissionPayload.cs`), not only on `SourceEvidence`, since products from this source may be
  persisted/merged independently of the evidence they arrived with.

### `lowest_price` is a decoy, not a price

`DATES.D[].lowest_price` was `"0.00"` in every live response observed — a vestigial, unpopulated
field. The adapter never reads it. A fixture (`decoy-lowest-price.json`) sets it to a non-zero
value specifically to prove the parser ignores it and reads `PS.P[].CT.retail_amount` instead
(spec §5.4's "Starting at" marketing-price rule).

## Distinguishing Verified / NotPublished / Unavailable / Error

The endpoint itself gives exactly **one** negative signal — `SERVICE.status: "FAILED"`,
`error_msg: "No dates found that meet requirements"` — for every case tried: a genuinely closed
weekday within the near-term schedule (e.g. a Tuesday in September, closed for the school-year
shoulder season), a date many months beyond the currently published calendar, and a date already
in the past. It does **not** distinguish "not yet published" from "closed"/"unavailable" on its
own. Directly proven with paired single-day requests:

| Requested date | Result |
|---|---|
| `2026-08-20` … `2026-08-23` (open days) | `status: "OK"`, priced products |
| `2026-08-24` … `2026-08-28` (weekday closure) | `status: "FAILED"`, same generic message |
| `2026-09-05`/`06`/`07` (Sat/Sun/Labor-Day-Mon) | `status: "OK"` |
| `2026-09-08` (the following Tuesday) | `status: "FAILED"`, same generic message |
| `2026-11-08` (last date currently published) | `status: "OK"` |
| `2027-01-01`, `2027-06-15`, `2028-06-01` | `status: "FAILED"`, same generic message |
| `2020-01-01`, `2026-08-19` ("yesterday") | `status: "FAILED"`, same generic message |

To distinguish `NotPublished` from `Unavailable` without guessing, the adapter always requests a
**wide window** — `start_date = today`, `end_date = max(requestedDate, today + 120 days)` — in one
call, then classifies by comparing the requested date against what the window actually returned:

1. **Verified**: the window's `DATES.D` contains an entry whose `date` equals the requested date
   exactly (string/value equality, never positional/first-entry trust — spec §5.4's
   "never substitute"). Products come only from that matched entry.
2. **NotPublished**: no entry matches, and the requested date is *after* the latest date the
   window actually returned (i.e. beyond the calendar's currently observed publishing horizon —
   `2026-11-08` at investigation time, discovered live via a `2026-08-20`→`2027-12-31` probe, not
   hardcoded).
3. **Unavailable**: no entry matches, but the requested date is *on or before* the latest date
   returned (i.e. inside the currently published range — this is what a genuine closed-day gap,
   like the `08-24`…`08-28` example above, looks like from a wide-window query).
4. **Past dates never make a network call.** A date before "today" is short-circuited straight to
   `Unavailable` — directly justified by the `2026-08-19` test above, and avoids the unbounded
   look-back window an arbitrary past `requestedDate` would otherwise force.
5. **Error**: HTTP failure, non-JSON/malformed body, an unparseable date within a returned entry,
   a matched entry with no usable packages, or — defensively, never observed live — the entire
   `[today, today+120]` window itself coming back `FAILED`/empty. Every live check performed during
   this investigation returned data somewhere in that span, so a fully-empty result is treated as
   an unexpected shape rather than assigned a guessed business meaning (CLAUDE.md "never invent
   data").

`HorizonPaddingDays = 120` is a padding constant, not the true horizon (which the adapter never
hardcodes — it is always read from what the live window actually returns). It only needs to be
comfortably larger than the real horizon so a requested date just past it is not misclassified;
120 gives ~40 days of margin over the ~80-day horizon observed live. If Six Flags' publishing
horizon ever grows past that, the adapter still gets a correct answer for any requested date
beyond the padding — `end_date` is extended to cover it explicitly (see code) — this constant only
affects requests for dates *within* the padding window.

## Exact-date confirmation

Every `Verified` result's `SourceEvidence.ConfirmedDate` is set to the requested date only after
finding an entry in the response whose own `date` field equals it exactly — the same
string-equality match used to select which entry's products to read. There is no code path that
can attach one date's products to a different date's evidence; `SourceEvidence`'s constructor
additionally throws if `Verified` is ever passed a `confirmedDate` that doesn't equal
`requestedDate` (Domain-level invariant, unchanged, shared with `ParkHoursSource`).

## Cloudflare/browser requirements

None. `ecomm.api.cf.accessoticketing.com` is not the Cloudflare-fronted host phase 1 flagged
(`cf-ca.buy.sixflags.com` is a different origin); no bot-management cookie, challenge, or session
was encountered across ~40 live requests made during this investigation, including from a bare
Node `fetch()` with no browser at all.

## Known limitations

- `_version: "6.33.1"` is the checkout SPA's build version at investigation time, hardcoded. If
  Six Flags ships a build that changes or starts validating this value server-side, the adapter
  would need updating — not detectable in advance without periodically re-checking the live
  bundle.
- `PriceQualifier` is always `null` in production — the live endpoint never returns an explicit
  label for why a second price exists, so the adapter does not guess one (see "PriceQualifier is
  not populated" above). Downstream code that would like to show *why* a comparison price differs
  (e.g. "day-of pricing") cannot currently do so from this source alone.
- `available`/`capacity`/`bookings_sold_out` fields exist on each package but are not read. No
  live response observed during investigation ever showed a sold-out product (`available` always
  equaled `capacity`), so the adapter does not special-case that shape rather than guess its
  meaning.
- No independent Cloudflare/bot-mitigation distinguishing logic exists for this host, unlike the
  concern phase 1 raised for `cf-ca.buy.sixflags.com` itself — not needed since this endpoint isn't
  behind that protection, but if that changes, failures here would currently surface as a plain
  `Error` without a specific "likely bot-mitigation" hint.
- This phase implements only the `SnapWizardDailyTickets` keyword (general dated admission).
  `FastLaneOnly` and `HAPWizard` keywords were seen referenced in spec §5.1 but are out of scope
  here (scope restriction) and were not investigated.
