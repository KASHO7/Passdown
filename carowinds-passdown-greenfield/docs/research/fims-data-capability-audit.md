# Phase 22G — Official FIMS data capability audit

Research only. No application code, schema, retrieval logic, UI, PDF, Teams Copy, pricing, show
selection or version changed. RC.6 artifacts untouched; RC.7 not packaged.

- Repository state at audit time: branch `feature/passdown-redesign`, HEAD `c608c7a` (Phase 22F).
  Pre-existing staged files (`.DS_Store`, `.claude/skills/pdf-design/SKILL.md`) were left alone.
- Investigation time: 2026-10-17, about 8:00 PM Eastern (2026-10-18 00:00 UTC).
- Method: plain `curl` GETs, no headers, cookies, browser automation or authentication. Raw responses
  were kept in a temporary scratch folder only and analyzed with a small recursive schema walker. No raw
  response is committed; the JSON below is trimmed and sanitized.

Labels used throughout:

- **Verified** — read directly from the official response or official page during this audit.
- **Likely** — an interpretation consistent with every observation, but not confirmed by the source.
- **Not established** — no evidence either way.

---

## 1. Endpoint overview

The official park-hours page `https://www.sixflags.com/carowinds/park-hours` loads a public JSON file
served from S3 through CloudFront (`docs/retrieval/park-hours-source.md`). It is the same feed the app
already uses for park hours (`ParkHoursSource`) and shows (`ShowScheduleSource`). Park ID 30 is
Carowinds; every response echoes `"parkId":30,"parkAbbreviation":"CA","parkName":"Carowinds"`.

Each response contains, per date: whether the park is closed, the gate operating windows
(`operatings`), seasonal/special events (`events`), a catalog of in-park locations with optional
scheduled hours (`venues`), and entertainment (`shows`).

## 2. Request patterns

| Pattern | URL | Observed (Verified) |
|---|---|---|
| No date | `…/operating-hours/park/30` | HTTP 200, byte-identical to today's daily file (same ETag as `?date=20261017`). |
| One day | `…/operating-hours/park/30?date=yyyyMMdd` | One `dates[]` entry for the requested day. |
| One month | `…/operating-hours/park/30?date=yyyyMM` | One entry per date; the current month lists tomorrow onward (`202610` → 10/18–10/31). |
| Outside retention | `?date=20260301` | HTTP 404, JSON `{"error":"Not Found","message":"The requested resource could not be found."}` |

Base: `https://d3ln8b3jj5b2pq.cloudfront.net`. Response headers: `content-type: application/json`,
`server: AmazonS3`, CloudFront `x-cache`, an `etag`, and a `last-modified`. For the daily file
`last-modified` was one second before the request even though the ETag matched the no-date file fetched
earlier, so the file appears to be rewritten very frequently (**Likely**; exact cadence **Not
established**). The month file `202612` was last modified about two hours before the request.

## 3. Dates sampled

| Category | Request | Date(s) inspected |
|---|---|---|
| Today, weekend, SCarowinds + Tricks and Treats | `20261017` and no-date | 10/17/2026 (Sat) |
| SCarowinds evening-only weekday | `20261016` | 10/16/2026 (Fri) |
| Closed day | `20261019` | 10/19/2026 (Mon) |
| Normal regular-season weekday (past) | `20260819` | 08/19/2026 (Wed) |
| Holiday / special event (past) | `20260704` | 07/04/2026 |
| WinterFest operating day | `20261205` | 12/05/2026 |
| New Year's Eve | `20261231` | 12/31/2026 |
| Future unpublished date | `20270615` | 06/15/2027 |
| Past date outside retention | `20260301` | 03/01/2026 (404) |
| Month files | `202610`, `202611`, `202612`, `202701` | 10/18/2026 – 01/31/2027 (106 dates) |

## 4. Request count

- FIMS feed: **14 GETs** (13 × HTTP 200, 1 × HTTP 404 as expected). 1-second pause between requests.
- Official website: **1 GET** of `https://www.sixflags.com/carowinds/events/scarowinds` to cross-check
  the SCarowinds date range against the feed.
- No failures other than the intentional 404. Total raw download about 1.5 MB.

## 5. Complete observed JSON field inventory

Counts are over all 115 date entries returned by the 13 successful responses. "Used" means read by the
current app (`ParkHoursApiResponse.cs`, `ShowScheduleApiResponse.cs`).

### Root

| Path | Type | Example | Null/empty | Consistent | Used | Meaning (confidence) |
|---|---|---|---|---|---|---|
| `parkId` | int | `30` | never | all 200 responses | No (implicit in URL) | Park ID (Verified) |
| `parkAbbreviation` | string | `"CA"` | never | all | No | Park code (Verified) |
| `parkName` | string | `"Carowinds"` | never | all | No | Park name (Verified) |
| `dates` | array | — | never empty in 200s | all | Yes | One entry per date |
| `error` | string | `"Not Found"` | only on 404 | 404 only | Yes (error detection) | Error kind (Verified) |
| `message` | string | `"The requested resource could not be found."` | only on 404 | 404 only | No | Error text |

### `dates[]`

| Path | Type | Example | Null/empty | Used | Meaning (confidence) |
|---|---|---|---|---|---|
| `date` | string `MM/dd/yyyy` | `"10/17/2026"` | never | Yes | Operating date (Verified) |
| `isParkClosed` | bool | `false` | never null | Yes | Park closed that day (Verified) |
| `operatings` | array | — | empty on 73/115 (closed days and unpublished open days) | Yes | Gate windows (Verified) |
| `events` | array | — | empty on 72/115 | **No** | Seasonal / special events (Verified as listed; see §9) |
| `venues` | array | — | empty on every closed day; 7 entries otherwise | **No** | Location catalog with optional hours |
| `shows` | array | — | empty on every closed day; 11–16 entries otherwise | Yes | Shows (Phase 21C) |

### `dates[].operatings[]`

| Path | Type | Example | Null/empty | Used | Meaning (confidence) |
|---|---|---|---|---|---|
| `operatingTypeId` | int | `24`, `70` | never | Yes | 24 = main gate, 70 = water park (Verified, Phase 22E.1) |
| `operatingTypeName` | string | `"Park"`, `"Waterpark"`, `"Winterfest"`, `"New Years Eve"` | never | Fallback only | Seasonal display label (Verified) |
| `items` | array | — | always exactly 1 item in sample | Yes | Operating windows |
| `items[].assignmentDisplayName` | string | same as `operatingTypeName` | never | No | Duplicate of the label in every sample |
| `items[].assignmentDescription` | string | `""` | **always empty** (43/43) | No | Not established |
| `items[].timeFrom` | string `HH:mm` | `"11:00"` | never in sample | Yes | Opening (Verified) |
| `items[].timeTo` | string `HH:mm` | `"00:00"`, `"01:00"` | never in sample | Yes | Closing; `00:00`/`01:00` mean after midnight (Verified against the official page for 12/31) |
| `items[].isBuyout` | bool | `false` | never null; **never true** in sample | No | Private buyout flag (Likely by name only; no `true` observed) |

### `dates[].events[]`

| Path | Type | Example | Null/empty | Used | Meaning (confidence) |
|---|---|---|---|---|---|
| `eventId` | int | `40` | never | No | Numeric event ID, stable across dates (Verified) |
| `extLocationID` | string | `"EVENT-030-00040"` | never | No | Official event ID; suffix = `eventId` padded (Verified) |
| `extEventName` | string | `"SCarowinds"` | never | No | Event name (Verified) |
| `filter` | bool | `true` | never null | No | Not established |
| `theming` | bool | `true` | never null | No | Not established |
| `items` | array | — | always exactly 1 item in sample | No | Event time windows |
| `items[].assignmentDescription` | string | `""` | **always empty** (55/55) | No | Not established |
| `items[].isAllDay` | bool | `false` | never null | No | `true` ⇔ `timeRangesComma` empty in every sample (Verified correlation) |
| `items[].timeRangesComma` | string | `"18:00 - 00:00"`, `"21:00 - 01:00"` | empty on 29/55 (all-day items) | No | Event window, 24-hour, can cross midnight (Verified) |

### `dates[].venues[]`

| Path | Type | Example | Null/empty | Used | Meaning (confidence) |
|---|---|---|---|---|---|
| `venueId` | int | `1` | never | No | Category ID (Verified, stable) |
| `venueName` | string | `"Rides"` | never | No | Category name (Verified) |
| `assignmentHours` | array | 1 item | never empty | No | Category-level hours |
| `assignmentHours[].assignmentDescription` / `timeFrom` / `timeTo` | string | `""` | **always empty** (315/315) | No | Not established; carries nothing in practice |
| `detailHours` | array | 1–59 items | never empty on open days | No | One row per location |
| `detailHours[].itemID` | int | `-1` | **always −1** (8,819/8,819) | No | Placeholder; not a usable ID |
| `detailHours[].itemName` | string | `"Fury 325"`, `"WP - TIDAL WAVE BAY"` | never | No | Location name (Verified) |
| `detailHours[].extLocationID` | string | `"RIDE-030-00003"` | never | No | Official location ID, stable name-to-ID across dates (Verified) |
| `detailHours[].operatingTimeFrom` | string `HH:mm` | `"11:00"` | empty on 7,594/8,819 | No | Scheduled opening (Likely) |
| `detailHours[].operatingTimeTo` | string `HH:mm` | `"00:00"`, `"23:59"` | empty when `From` is empty | No | Scheduled closing (Likely) |

Venue categories (Verified, identical IDs on every open date):

| `venueId` | `venueName` | Items | ID prefix | Ever had hours in sample |
|---|---|---|---|---|
| 1 | Rides | 59 | `RIDE-030-` | Yes |
| 3 | Mazes | 11 (Oct) / 10+ (Jul) | `MAZE-030-` | Yes, SCarowinds nights only |
| 4 | Restaurants | 52 | `RESTAURANT-030-` | Yes |
| 5 | Restrooms | 13 | `RESTROOM-030-` | Never |
| 6 | Retail | 30 | `RETAIL-030-` | Never |
| 7 | Guest Services | 30 | `GS-030-` | Never |
| 8 | Parking | 1 | `PARKING-030-` | Never |

### `dates[].shows[]`

| Path | Type | Example | Null/empty | Used | Meaning (confidence) |
|---|---|---|---|---|---|
| `showId` | int | `2261` | never | Yes (identity fallback) | Numeric show ID (Verified) |
| `extLocationID` | string | `"SHOW-030-00122"` | never | Yes (primary identity) | Official show ID (Verified) |
| `showName` | string | `"Wake the Dead"` | never | Yes | Name (Verified) |
| `items` | array | — | always exactly 1 item in sample | Yes | Schedule |
| `items[].assignmentLocation` | string | `"Carowinds Theatre"` | empty on 366/490 | Yes | Venue (Verified) |
| `items[].assignmentDescription` | string | `""` | **always empty** (490/490) | No | Not established |
| `items[].times` | string | `"07:00 PM, 08:00 PM"` | empty on 362/490 | Yes | Start times, 12-hour (Verified) |

No other fields exist anywhere in the sampled responses. There is no description, category, duration,
cancellation, status, last-updated, wait-time or capacity field at any level.

## 6. Sanitized examples

Closed day (complete entry, 10/19/2026):

```json
{ "date": "10/19/2026", "isParkClosed": true, "venues": [], "shows": [], "events": [], "operatings": [] }
```

Split-event day (10/17/2026, trimmed):

```json
{
  "date": "10/17/2026", "isParkClosed": false,
  "operatings": [ { "operatingTypeId": 24, "operatingTypeName": "Park",
    "items": [ { "assignmentDisplayName": "Park", "assignmentDescription": "",
                 "timeFrom": "11:00", "timeTo": "00:00", "isBuyout": false } ] } ],
  "events": [
    { "eventId": 40, "extLocationID": "EVENT-030-00040", "extEventName": "SCarowinds",
      "filter": true, "theming": true,
      "items": [ { "assignmentDescription": "", "isAllDay": false, "timeRangesComma": "18:00 - 00:00" } ] },
    { "eventId": 9, "extLocationID": "EVENT-030-00009", "extEventName": "Tricks and Treats",
      "filter": false, "theming": false,
      "items": [ { "assignmentDescription": "", "isAllDay": false, "timeRangesComma": "11:00 - 18:00" } ] }
  ],
  "venues": [
    { "venueId": 3, "venueName": "Mazes",
      "assignmentHours": [ { "assignmentDescription": "", "timeFrom": "", "timeTo": "" } ],
      "detailHours": [ { "itemID": -1, "itemName": "Slaughter House", "extLocationID": "MAZE-030-00005",
                         "operatingTimeFrom": "19:00", "operatingTimeTo": "00:00" } ] }
  ]
}
```

New Year's Eve (12/31/2026, trimmed): gate 15:00–01:00 labeled `"New Years Eve"`; events `WinterFest`
`"15:00 - 21:00"` and `New Year's Eve` `"21:00 - 01:00"`.

## 7. Operating-hours categories

| Label | ID | Dates seen | Example window |
|---|---|---|---|
| `Park` | 24 | regular season, SCarowinds, Oktoberfest days | 10:00–19:00, 11:00–00:00, 18:00–00:00, 11:00–18:00 |
| `Waterpark` | 70 | 07/04/2026 | 11:00–18:00 |
| `Winterfest` | 24 | 11/27–12/30/2026 open days | 12:00–20:00, 12:00–21:00 |
| `New Years Eve` | 24 | 12/31/2026 | 15:00–01:00 |

- **Verified:** one `items` entry per operating type on every sampled date; no date had two windows for
  the same type. Water park and main gate coexist as separate entries.
- **Verified:** closing times past midnight are written as small hours (`00:00`, `01:00`).
- **Verified:** `isBuyout` was `false` on all 43 operating items. A buyout day was not observed, so
  whether the flag is ever set — and what the page does with it — is **Not established**.
- **Verified:** an open flag with no `operatings` occurs (01/01–01/02/2027, every date Feb–Oct 2027). On
  01/01–01/02/2027 the `WinterFest` event is already listed even though hours are not. The app's
  current **NotPublished** treatment of these dates is correct and conservative.

**Calendar explanation opportunity (no implementation now):** the seasonal label (`Winterfest`,
`New Years Eve`) and the event list could explain *why* a day has its hours ("SCarowinds 6 PM–midnight",
"Tricks and Treats 11 AM–6 PM"), and on 01/01–01/02/2027 could say "WinterFest listed — hours not yet
published" instead of a bare "not published".

## 8. Special events

Every event observed in the sample (**Verified**; IDs stable across all dates they appeared on):

| `eventId` | Name | `filter` | `theming` | Dates seen | Time form |
|---|---|---|---|---|---|
| 9 | Tricks and Treats | false | false | Sat/Sun 10/17–11/07/2026 | `11:00 - 18:00` (11/07: `11:00 - 17:30`) |
| 40 | SCarowinds | true | true | 10/16–11/07/2026 SCarowinds nights | `18:00 - 00:00` / `18:00 - 23:00` / `17:30 - 00:00` |
| 41 | WinterFest | true | true | 11/27/2026–01/02/2027 open days | all-day; 12/31 `15:00 - 21:00` |
| 66 | New Year's Eve | false | false | 12/31/2026 | `21:00 - 01:00` |
| 75 | Star-Spangled Nights | true | true | 07/04/2026 | all-day |
| 183 | Oktoberfest | false | false | 11/07–11/22/2026 open weekend days | all-day |
| 403 | SPLASH! Water Parade | true | true | 07/04/2026 | all-day |

Findings:

- **Verified:** events are date-specific. Closed days carry none; unpublished 2027 dates carry none
  except 01/01–01/02.
- **Verified:** the feed agrees with the official SCarowinds page, which now says "Select Nights.
  September 11 - November 7, 2026" (extended from the Nov 1 recorded in `docs/retrieval/dated-haunted-source.md`). The feed lists
  SCarowinds on 11/06 and 11/07.
- **Verified:** event windows can split the day and the gate window is their union (10/17: Tricks and
  Treats 11:00–18:00 + SCarowinds 18:00–00:00 = gate 11:00–00:00; 12/31: WinterFest 15:00–21:00 +
  New Year's Eve 21:00–01:00 = gate 15:00–01:00).
- **Not present:** descriptions, categories, venues, passholder-preview flags, private/buyout flags. No
  passholder preview was in the sampled range, so whether one would appear here is **Not established**.
- **Not established:** what `filter` and `theming` mean. All marquee seasonal events have both `true`;
  sub-events (Tricks and Treats, Oktoberfest, New Year's Eve) have both `false`. A plausible guess is
  "show as a calendar filter / headline event", but nothing confirms it. Do not build on these flags.

**Completeness for a Calendar section:** good. Events are populated wherever hours are published, the
names match official marketing, and the data is already in every response the app downloads.

## 9. Venues and attractions

- **Verified:** `venues` is a **location catalog repeated on every open date**, not a list of what is
  open. The same 196 locations (identical IDs and names) appear on 08/19/2026, 10/17/2026, 12/05/2026
  and 06/15/2027. Only the hours vary.
- **Verified:** water-park rides sit under `Rides` with a `WP - ` name prefix and use the same
  `RIDE-030-` ID space.
- **Verified:** hours are present only for some locations on some dates:

| Date | Gate | Timed rides | Timed mazes | Timed restaurants |
|---|---|---|---|---|
| 07/04/2026 (past) | 10:00–22:00 | 59 | 0 | 50 |
| 08/19/2026 (past) | 10:00–19:00 | 75 timed across all venues | — | — |
| 10/17/2026 (today) | 11:00–00:00 | 40 of 59 | 11 (19:00–00:00) | 41 of 52 |
| 11/06/2026 | 18:00–00:00 | 25 | 11 | 0 |
| 11/08/2026 | 11:00–18:00 | 0 | 0 | 0 |
| 12/05/2026 WinterFest | 12:00–21:00 | 0 | 0 | 0 |
| 06/15/2027 | none | 0 | 0 | 0 |

- **Likely:** location hours are a near-term operating plan that fills in progressively (dense for the
  current week, partial three weeks out, absent for WinterFest).
- **Verified:** location hours are sometimes useful detail the gate time does not show: on 10/17 the
  family rides (e.g. Woodstock Express, Kiddy Hawk) close at 18:00 while thrill rides run to 00:00; WindSeeker runs
  19:00–23:59; mazes run 19:00–00:00; restaurants vary (Camp Cookout to 20:00, Blue Ridge Country
  Kitchen to 23:00). On 08/19 some rides open at 09:30, before the 10:00 gate (the Phase 3B finding).
- **Not established — and must not be inferred:** why a location has no hours. On 10/17 Fury 325,
  Thunder Striker and Electro-Spin are listed with blank hours while the park is open. That could be a
  closure, maintenance, or simply unpublished data. The feed does not say.
- **Not present:** live open/closed status, wait times, downtime, categories beyond the seven venue
  groups, coordinates, or height requirements. **This feed provides scheduled hours only, never live
  ride status.**
- `itemID` is always `-1`; `extLocationID` is the only usable location identifier.
- **Not established:** past-date catalog behavior. 07/04/2026 lists a different maze set (e.g.
  "Harvest FEAR", "GLITCH") from October. That suggests past files may be frozen snapshots of what was
  current then, but this was not proven.

## 10. Shows

- Every field in `shows[]` is already used except `items[].assignmentDescription`, which was empty on
  all 490 rows.
- **Verified:** show lists are seasonal catalogs, not date-specific line-ups. The 11 Halloween shows are
  listed on 12/05/2026 (WinterFest) and 06/15/2027 with blank times. This confirms the Phase 21C rule
  that a listed show without times is not a performance.
- **Verified:** `showId`/`extLocationID` were stable across every date sampled. Each show had exactly one
  `items` entry.
- **Not present:** duration, category, description, event association, cancellation, or change
  indicators. The Phase 21C limitations stand unchanged. No further show enhancement is supported by
  the data.

## 11. Seasonal differences

| Season | Gate label | Events | Location hours | Shows |
|---|---|---|---|---|
| Summer (07/04) | `Park` + `Waterpark` | Star-Spangled Nights, SPLASH! Water Parade | rides + restaurants | summer line-up with times |
| Regular weekday (08/19) | `Park` | none | rides (some 09:30) | 1 show |
| SCarowinds (Oct–11/07) | `Park` | SCarowinds (+ Tricks and Treats weekends) | rides, mazes, restaurants | Halloween line-up |
| Oktoberfest weekends (11/07–11/22) | `Park` | Oktoberfest | mostly none yet | 1 timed show |
| WinterFest (11/27–12/30) | `Winterfest` | WinterFest | none yet | Halloween catalog, no times |
| New Year's Eve | `New Years Eve` | WinterFest + New Year's Eve | none yet | no times |
| Unpublished (2027) | none | none (except 01/01–01/02) | none | Halloween catalog, no times |

## 12. Daily vs monthly responses

- **Verified:** for 12/05/2026, 12/31/2026 and 10/19/2026 the daily entry and the month-file entry are
  exactly equal (every field, including venues, shows and events).
- **Verified:** the no-date request returns today's daily file.
- **Verified:** the current month file starts tomorrow; today needs the daily form (already handled by
  Phase 22E.1).
- Month files are large (58–448 KB here) because `venues` repeats the 196-location catalog for every
  open date. Events add almost nothing to that size.

## 13. Reliability limitations

- The feed is a scheduled plan, not a live status source. Nothing says "current as of".
- No time zone; times are assumed America/New_York (existing project assumption).
- Location hours are partial and fill in over time. A blank is not a closure.
- `isBuyout` has never been seen `true`; no buyout evidence exists.
- The meaning of `filter`/`theming` is unknown.
- `assignmentDescription` (four places) and venue `assignmentHours` were always empty; they may carry
  data someday, but there is nothing to design against.
- Event and location data for past dates may reflect what was planned, not what happened.
- The response is a third-party-hosted file; its shape could change without notice (existing risk).

## 14. Fields already used by the app

`dates[].date`, `isParkClosed`, `operatings[].operatingTypeId`, `operatingTypeName` (fallback),
`operatings[].items[].timeFrom`/`timeTo`, `shows[].showId`, `extLocationID`, `showName`,
`shows[].items[].assignmentLocation`/`times`, and the root `error` for 404 handling.

## 15. Fields ignored by the app

`parkId`, `parkAbbreviation`, `parkName`, `message`; `operatings[].items[].assignmentDisplayName`,
`assignmentDescription`, `isBuyout`; **all of `events[]`**; **all of `venues[]`**;
`shows[].items[].assignmentDescription`.

## 16. Capability comparison

| Data category | Officially observed | Already used | Potential use | Value | Confidence |
|---|---|---|---|---|---|
| Main gate hours (id 24) | Yes | Yes | — | — | High |
| Water park hours (id 70) | Yes | Yes | — | — | High |
| Seasonal gate label | Yes | As fallback | Label the day ("WinterFest hours") | Medium | High |
| Special events + windows | Yes | No | Calendar/Dashboard event line | **High** | High |
| Maze hours (SCarowinds) | Yes, near-term | No | "Mazes 7 PM–midnight" line | Medium | Medium |
| Ride scheduled hours | Yes, partial | No | Exceptions (e.g. family rides close 6 PM) | Low–Medium | Medium |
| Restaurant scheduled hours | Yes, partial | No | Dining hours reference | Low–Medium | Medium |
| Restrooms/Retail/Guest Services/Parking hours | Catalog only, never timed | No | — | Not recommended | High (that it's empty) |
| Live ride status / wait times | **Not present** | No | — | Not recommended | High |
| Private buyout flag | Field present, never true | No | Buyout indicator | Insufficient evidence | Low |
| Show durations / cancellations / categories | **Not present** | No | — | Not recommended | High |
| `filter` / `theming` flags | Present | No | — | Insufficient evidence | Low |

## 17. Possible future features

### A. Special events on the Calendar and Dashboard — **High value**

- **Problem solved:** associates see *what kind of day it is* (SCarowinds night, Tricks and Treats
  daytime, Oktoberfest, WinterFest, New Year's Eve) and why the hours split, without opening the
  website. Also explains 01/01–01/02/2027 ("WinterFest listed — hours not yet published").
- **Fields:** `events[].eventId`, `extLocationID`, `extEventName`, `items[].isAllDay`,
  `items[].timeRangesComma`.
- **Reliability:** high; matches the official SCarowinds page and the Phase 22E.1 calendar check.
- **UI:** Calendar selected-date panel (an "Events" line beneath park hours), optionally a short name in
  the day cell; one line in Dashboard "Today at a Glance". Possibly an Associate Overview line. PDF and
  Teams Copy inclusion would be separate product decisions.
- **Retrieval/storage cost:** **zero extra requests** — events are in the daily and month responses
  `ParkHoursSource` already fetches (rolling window + 13 month files). Storage is tens of bytes per
  date as normalized JSON in the existing dated-results pattern.
- **Misleading risk:** low. Must not infer an event from a neighboring date, and an empty `events` array
  on a published day should read as "no special event listed", not "no events".
- **Complexity:** medium — a new payload/category or an extension of the park-hours payload, parsing
  `HH:mm - HH:mm` with midnight crossing, freshness and last-known-good rules, Calendar/Dashboard
  presentation and tests. Needs a decision on whether it extends `ParkHoursPayload` or is its own
  `SourceCategory`.

### B. Haunted maze hours on SCarowinds nights — **Medium value**

- **Problem solved:** "When do the mazes open tonight?" — a common guest question; gate time alone
  (11:00–00:00) does not answer it.
- **Fields:** `venues[venueId=3].detailHours[]` (`itemName`, `extLocationID`, `operatingTimeFrom/To`).
- **Reliability:** medium — populated for the current weeks of SCarowinds only, consistent across mazes
  (all 19:00–00:00 on sampled nights).
- **UI:** one summary line under events ("Haunted mazes 7 PM–midnight"), only when every listed maze
  shares one window; otherwise per-maze list. Never say a maze is closed when hours are blank.
- **Cost:** no extra requests. Small storage.
- **Complexity:** low–medium once feature A exists.

### C. Scheduled hours exceptions for rides and restaurants — **Low–Medium value**

- **Problem solved:** "Is the kids' area open late?" / "Is X restaurant open after 8?"
- **Fields:** `venues[venueId=1|4].detailHours[]`.
- **Risk:** medium–high. 59 rides and 52 restaurants is too much for a quick reference, blanks are
  ambiguous (Fury 325 blank on an open day), and data is missing beyond a few weeks out. Only a narrow
  "differs from gate hours" summary, clearly labeled "scheduled", would be defensible.
- **Recommendation:** defer until A and B prove useful.

## 18. Features not recommended

- **Live ride status / "ride closed" indicators** — the feed has no live status; blank hours are not a
  closure.
- **Buyout / private-event banner from `isBuyout`** — never observed `true`; keep the existing manual
  override (spec §8.2).
- **Restroom, retail, guest services, parking hours** — catalog only, never timed.
- **Show duration, cancellation or category** — not in the feed (unchanged Phase 21C position).
- **Anything driven by `filter`, `theming`, `itemID`, or `assignmentDescription`** — meaning unknown or
  always empty.
- **Historical event/location backfill** — past files may be planning snapshots; not worth the cost.

## 19. Questions still unanswered

1. What do `events[].filter` and `events[].theming` control?
2. Is `isBuyout` ever set, and does a buyout day also set `isParkClosed`?
3. Do passholder previews or private events appear in `events`?
4. Can `timeRangesComma` carry more than one range (the name suggests comma-separated ranges)? Only one
   range was observed.
5. Can an `operatings`, `events` or `shows` entry ever have more than one `items` row?
6. How far ahead are location hours published, and when do they fill in?
7. Are past-date files frozen planning snapshots or regenerated from current data?
8. Does a blank ride time on an open day ever mean a scheduled closure?

## 20. Ranked implementation recommendations

1. **Special events (feature A)** — highest usefulness per unit of risk; already downloaded.
2. **Maze hours summary (feature B)** — small add-on to A during SCarowinds.
3. **Seasonal-label wording on the Calendar** — fold into A (e.g. "WinterFest hours 12–9 PM").
4. **Ride/restaurant exceptions (feature C)** — only after A/B, and only as a "scheduled" summary.
5. Everything in §18 — do not build.

## Existing app correctness

No correctness issue was found. The observations support current behavior:

- Main gate by `operatingTypeId 24` (Phase 22E.1) is right: the label varies by season.
- 01/01–01/02/2027 as NotPublished is right: an event is listed, hours are not.
- Shows listed without times as not performing (Phase 21C) is right: Halloween shows are listed on
  WinterFest and 2027 dates.
- Daily and month entries are identical, so mixing the two forms (Phase 22E.1) is safe.

One documentation note: `docs/retrieval/dated-haunted-source.md` records the SCarowinds season as
ending November 1, 2026. The official page and the feed now show November 7, 2026. This does not affect
code (dated haunted pricing is read per date, with no hard-coded end), but the doc is out of date.

## Suggested next phase

**Phase 22H — Official special events (read-only display).** Parse `events[]` from the responses the
park-hours source already fetches, store a normalized per-date event list under existing
last-known-good rules, and show it in the Calendar selected-date panel and Dashboard "Today at a
Glance". Optionally add the SCarowinds maze-hours summary. PDF/Teams Copy inclusion is a separate
product decision.
