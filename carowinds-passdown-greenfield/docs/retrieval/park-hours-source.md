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
      "shows": [ /* not used here — read by ShowScheduleSource (Phase 21C) */ ],
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
        // Phase 13D: now also read — see "Phase 13D update — waterpark hours now read" below.
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

## Phase 13D update — waterpark hours now read

Traced (not guessed) directly from this same response: on dates Carolina Harbor is separately open,
`operatings[]` carries a second entry, `operatingTypeName == "Waterpark"` (id 70), with the identical
`items[].timeFrom`/`timeTo` shape the `"Park"` entry uses. `ParkHoursSource` (Infrastructure) now
reads it via the same defensive lookup pattern as the main gate — lenient rather than strict: an
absent, blank, or unparseable waterpark entry yields `null`/`null` (no waterpark hours) rather than
failing the whole retrieval, since the main gate's hours remain the critical data this source exists
for. `ParkHoursPayload.WaterparkOpeningTime`/`WaterparkClosingTime` (both nullable) carry the result
through persistence unchanged (JSON-serialized into the existing `dated_results` value column — no
schema migration). See `docs/phase13-hardening.md`'s Phase 13D section for the full presentation
flow (Calendar day cell/selected panel, Associate Overview). Never hardcodes a season-end date —
absence of the entry is read directly from the source every time, not inferred from a fixed calendar
assumption.

## Known limitations

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

## Phase 20C update — past dates, retention window, and the monthly form

Re-investigated live on 2026-10-16 (plain GETs, no Playwright):

- **Daily form serves past dates within a retention window.** 2026-03-25 through yesterday returned the
  normal shape (open with Park/Waterpark `operatings`, or `isParkClosed: true`). 2026-03-24 and every
  earlier 2026 date tried (03-01, 02-15, 01-01) returned **HTTP 404** with the JSON body
  `{"error":"Not Found","message":"The requested resource could not be found."}` — the same shape
  already documented above for "far outside the retained range".
- **Monthly form is not history.** `?date=202606` / `202609` / `202603` each returned a single entry
  (the month's last day); `?date=202610` returned only 10/17–10/31 (tomorrow onward). It cannot be used
  for backfill, so the backfill makes one daily request per date.
- `ParkHoursSource` now also implements `IParkHoursHistorySource.RetrieveHistoricalAsync`: identical
  request and parsing, plus `NotRetainedBySource` = (HTTP 404 with that JSON error body) **and** the date
  is before today (Eastern). The evidence is still `Error`, so the operational contract above is
  unchanged and a "Not Found" can never be stored or replace a stored value. HTML error pages (the 503
  CloudFront/Lambda page), other statuses and network failures are never treated as "not retained".
- A live response is ~28–30 KB (mostly `venues`); only the normalized `ParkHoursPayload` (≤ 119 bytes
  of JSON) and evidence are stored — measured at ~580 bytes per date on disk.

The retention window is observed behavior, not a documented contract; the backfill re-probes it once
per session. See `docs/phase20c-historical-backfill.md`.

## Phase 21A — January 1–3, 2026

Checked live 2026-10-16: the feed returns HTTP 404 "Not Found" for 2026-01-01..03, for the `202601`
and `202512` month files, and for every date before 2026-03-25 (CloudFront "Error from cloudfront").
The official WinterFest/park-hours pages now describe only the 2026–27 season. These dates are no
longer exposed by the official source; the Phase 20C backfill includes them in scope (calendar year
2026 even though they belong to the previous WinterFest season), probes the boundary from the 404,
and stores nothing. No hours are inferred from neighboring dates; no cutoff date is hard-coded.

## Phase 21C update — shared request with the show schedule

The `shows[]` array of this same response is now read by `ShowScheduleSource`
(`docs/phase21c-official-show-schedules.md`); this adapter still reads only `operatings`. In the app
both adapters share a `CoalescingRetrievalHttpClient`, so checking a date's hours and shows together
issues one GET (identical in-flight requests joined; a successful response reused for 2 minutes;
failures never cached). `ParkHoursSource`'s own behavior and contract are unchanged.
