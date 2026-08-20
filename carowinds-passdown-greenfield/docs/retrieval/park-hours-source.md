# Park hours source — investigation and adapter contract

Live investigation performed from this dev host on 2026-08-20, continuing from
`docs/phase1-discovery.md` (plain-HTTP GET only found CMS/promo content on
`/park-hours`, not a per-date calendar — a real browser network trace was
required to find it, per that doc's conclusion). This documents what was
actually observed against the official site — nothing below is guessed or
carried over from the previous application.

## Official page

`https://www.sixflags.com/carowinds/park-hours` (spec §5.1).

## How the page actually gets calendar data

Traced with a real headless Chromium session (Playwright, installed on this
host for the investigation) recording every network request the page makes
after load, then cross-checked against the page's own downloaded JavaScript
chunks to confirm — not guess — how the fetched data maps to what's
displayed. Two independent pieces of evidence were required before trusting
this:

1. **Live network trace**: loading the page fires exactly two calls to a
   public, unauthenticated CloudFront-hosted JSON endpoint:
   - `https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20260820`
     (single day — the "effective" park date)
   - `https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=202608`
     (whole month, for the calendar grid)

   No `sixflags.com` API route, no GraphQL, no Accesso call, and no
   `cf-ca.buy.sixflags.com` involvement — despite phase 1's suspicion that a
   `park.accessoBaseUrl` field might be involved, that field was `null` on
   this page and unused. Response headers show `server: AmazonS3` served
   through CloudFront (`x-cache`, `via: cloudfront`) — a static, pre-generated
   JSON file, not a live backend query; `last-modified` was minutes old,
   implying it's regenerated on a schedule, not per-request.

2. **DOM cross-check**: the calendar's day cells get a `.is-open` CSS class
   client-side. For the full visible month, `.is-open` appeared on exactly
   the dates the JSON marked `"isParkClosed": false`, and was absent on every
   `"isParkClosed": true` date (verified 08/20–08/31/2026: 24–28 and 31
   closed, everything else open) — confirming this endpoint, not some other
   source, drives what the page shows.

3. **Shipped-JS confirmation**: downloaded the page's `_next/static/chunks/*`
   bundles and found the literal fetcher (`getCalendarDataFromFIMS`):
   `` fetch(`https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/${parkId}?date=${...}`) ``
   — this is not a guessed URL; it's the site's own shipped code, read after
   the network trace already confirmed the same URL live. "FIMS" (seen in
   other CMS fields like `fimsId`) is Six Flags' internal facility/ticketing
   system that backs this feed.

This settles Phase 1's open question: the day-by-day calendar is **not**
embedded in server-rendered HTML/RSC payload (confirmed again: `raw.html` for
this page contains zero occurrences of `operating-hours` or the CloudFront
host), and it is **not** behind Playwright-only, cookie-gated, or
bot-protected access the way `cf-ca.buy.sixflags.com` is (§ phase1-discovery,
"Buy/checkout shell" section). It is a plain public JSON file reachable with
`curl`, no headers, no cookies, no browser required.

## Endpoint

```
GET https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{parkId}?date={yyyyMMdd}
```

- `parkId` — `30` for Carowinds. Directly confirmed: every response body
  itself echoes `"parkId":30,"parkAbbreviation":"CA","parkName":"Carowinds"`.
  Not guessed — read back from the park's own data, and it's the same id the
  live page's own JS passes for this park.
- `date` — the **daily** form the adapter uses: `yyyyMMdd`, e.g. `20260820`.
  (A `yyyyMM` "monthly" form also exists, for the calendar grid — not needed
  for a single exact-date lookup, so the adapter does not use it.)
- No API key, auth header, or cookie required. `vary: Origin` is present
  (CORS enabled), matching that the live page calls this directly from the
  browser, cross-origin.

Confirmed via direct `curl` (no Playwright) — this is a case where §6.4's
"prefer direct HTTP/API over browser automation" applies cleanly, unlike the
`cf-ca.buy.sixflags.com` checkout flows phase 1 flagged as needing Playwright.

## Response shape (observed, sanitized fixture saved under `fixtures/park-hours/`)

```jsonc
{
  "parkId": 30,
  "parkAbbreviation": "CA",
  "parkName": "Carowinds",
  "dates": [
    {
      "date": "08/20/2026",           // MM/dd/yyyy — matches the requested date
      "isParkClosed": false,
      "venues": [ /* per-ride/restaurant/retail detail — NOT used; see below */ ],
      "shows": [ /* not used */ ],
      "events": [ /* not used */ ],
      "operatings": [
        {
          "operatingTypeId": 24,
          "operatingTypeName": "Park",
          "items": [
            { "assignmentDisplayName": "Park", "assignmentDescription": "", "timeFrom": "10:00", "timeTo": "19:00", "isBuyout": false }
          ]
        }
        // a second entry with "operatingTypeName": "Waterpark" (id 70) appears
        // on dates the water park is separately open, e.g. 08/22–08/23/2026;
        // not read by this adapter — spec §8.1 only requires the main gate.
      ]
    }
  ]
}
```

Requesting the **daily** form always returned exactly one entry in `dates[]`
for every in-range date tried (past-within-range, today, and several months
ahead) — the adapter treats any other count as an unexpected shape (`Error`),
rather than guessing which entry to trust.

### Why `operatings[].operatingTypeName == "Park"`, not the per-ride `venues[]` data

The page's "Park: 10:00 AM – 7:00 PM" banner text was initially assumed to be
computed by aggregating the `venues[].detailHours[]` ride-level times (e.g.
min/max), because `venues[]` is the more obviously "calendar-shaped" part of
the payload. That assumption was tested and rejected with evidence: on
08/20/2026 the minimum ride opening time in `detailHours` was `09:30`
(an early rope-drop ride), not `10:00`, so a min/max aggregation over ride
data would have silently fabricated a wrong gate time. Reading the shipped JS
component that renders the banner (`useFIMSParkHours` → the same
`operating-hours` fetch, consumed via `dates[].operatings[]`, filtered by
`operatingTypeName`/`operatingTypeFimsId`) showed the real, distinct
`operatings` field the site itself uses — and it matches the displayed
banner exactly (`10:00`/`19:00`). The adapter reads `operatings` only, never
`venues`.

## Date format

Request: `yyyyMMdd` (e.g. `20260820`).
Response `dates[].date`: `MM/dd/yyyy` (e.g. `08/20/2026`).
Hour fields (`timeFrom`/`timeTo`): 24-hour `HH:mm`, e.g. `"19:00"`.

**Limitation**: the response carries no explicit time zone. Carowinds is a
single physical park in Charlotte, NC, and the page shows these hours to
guests with no time zone conversion UI, so the adapter treats `timeFrom`/
`timeTo` as America/New_York wall-clock time (consistent with
`EasternTimeZone`/§6.6 elsewhere in this codebase). This is a reasonable,
but not independently re-verified-per-value, assumption — flagged here per
CLAUDE.md "Retrieval rules" rather than left implicit.

## Distinguishing Verified / Closed / NotPublished / Error

All directly observed, not inferred from other apps or guessed:

| Case | Observed response | Status |
|---|---|---|
| Open date, hours published | `isParkClosed: false`, `operatings` contains an entry named `"Park"` with non-blank `timeFrom`/`timeTo` | **Verified** |
| Closed date (e.g. 08/24–08/28/2026, off-season weekdays) | `isParkClosed: true`, `operatings: []` | **Closed** |
| Valid future date, schedule not yet published (tested `2027-06-15`, ~10 months out) | `isParkClosed: false`, `operatings: []` (and every `venues[].detailHours[].operatingTime*` blank) | **NotPublished** |
| Date requested doesn't match date confirmed in the response | `dates[0].date != requested date` | **Error** (never substituted — spec §5.4) |
| Date far outside the published/retained range (tested `2020-01-01`) or unknown `parkId` (tested `9999`) | HTTP `404` with JSON body `{"error":"Not Found","message":"..."}` | **Error** |
| Malformed `date` query value (tested a non-numeric value) | HTTP `503` from a CloudFront/Lambda@Edge error page (HTML, not JSON) | **Error** |
| `dates` array missing or not exactly one entry, unparseable JSON, or an unparseable time value | — | **Error** (defensive; not observed live, but the shape contract could change) |

`isParkClosed` and the presence/absence of a `"Park"` entry in `operatings`
are both read directly from the response — no derived heuristic (e.g. "most
common ride time") is used anywhere in the final adapter, unlike the
rejected `venues[]`-aggregation approach above.

## Known limitations

- Only the main gate (`operatingTypeName == "Park"`) is read. `Waterpark`
  hours exist in the same payload but aren't part of spec §8.1's required
  fields for this phase, so `ParkHoursPayload` deliberately omits them.
- Time zone is assumed, not asserted by the API (see above).
- The monthly (`yyyyMM`) form of this endpoint was observed but is not used
  by this adapter — only documented for completeness, since Phase 3B asks
  for exact-date retrieval, not the calendar grid.
- This is a third-party-hosted (CloudFront/S3) file, not `sixflags.com`
  itself. It's still the same data the official page itself relies on (live
  network trace, not a guess), but it's a separate operational dependency
  that could change or move independently of the page.
- `isBuyout` on the `"Park"` operating item is present in the payload but not
  read — spec §8.2's private-buyout handling is a manual associate override,
  not sourced from this feed, so reading it here would be speculative scope
  creep.
