# Phase 22G.1 — FIMS historical ride data verification

Research only. No application code, schema, retrieval logic, UI, PDF, Teams Copy or version changed.
RC.7 not packaged. Nothing pushed.

- Builds on `docs/research/fims-data-capability-audit.md` (Phase 22G) and
  `docs/retrieval/park-hours-source.md` (Phase 20C retention notes).
- Investigation time: 2026-10-18, about 2:27 AM Eastern (06:27 UTC).
- Method: plain `curl` GETs to the public feed already used by the app,
  `https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=…` (park ID 30 = Carowinds).
  No headers, cookies, authentication, browser automation or private APIs. Raw responses kept only in
  a temporary scratch folder; nothing raw is committed.
- Comparison material: the raw Phase 22G responses captured 2026-10-17 at 6:56 PM and 8:00 PM
  Eastern (temporary scratch folders from those sessions). The repository has **no committed FIMS
  fixtures containing `venues`**; the app parses only `operatings` and `shows`.

Labels: **Verified** = read directly from the response or its headers. **Likely** = consistent with
every observation, not confirmed. **Unknown** = no evidence.

---

## 1. Requests and HTTP results

13 GETs, 1-second pause between them. No unexpected failures.

| Request | HTTP | Bytes | `last-modified` (UTC) | Result |
|---|---|---|---|---|
| `20260324` | 404 | 76 | 2026-03-25 08:47:33 | `{"error":"Not Found",…}` — same ETag as the 22G `20260301` 404 (a shared error object) |
| `20260325` | 200 | 163 | 2026-03-26 03:55:14 | Retained; park closed, `venues: []` |
| `20260328` | 200 | 28,478 | 2026-03-29 03:55:09 | Retained; open, full ride list (**oldest open date sampled**) |
| `20260401` | 200 | 163 | 2026-04-02 03:55:10 | Retained; park closed |
| `20260404` | 200 | 28,974 | 2026-04-05 03:55:10 | Retained; open |
| `20260523` | 200 | 27,978 | 2026-05-24 03:55:10 | Retained; open |
| `20260704` | 200 | 30,816 | 2026-07-05 03:55:10 | Retained; open |
| `20260731` | 200 | 29,177 | 2026-08-01 03:55:10 | Retained; open |
| `202607` (month) | 200 | 29,187 | 2026-07-31 00:01:58 | One entry only: 07/31/2026 |
| `20260819` | 200 | 27,056 | 2026-08-20 03:55:06 | Retained; open |
| `20261016` | 200 | 28,977 | 2026-10-17 03:55:04 | Retained; open (SCarowinds evening) |
| `20261017` | 200 | 29,730 | 2026-10-18 03:55:04 | Retained; open |
| `20261018` (today) | 200 | 29,689 | 2026-10-18 06:25:03 | Today; still being rewritten |

## 2. Ride records per date (`dates[].venues[venueId=1].detailHours[]`)

| Date | Gate (id 24) | Waterpark (id 70) | Rides listed | With hours | Blank | Earliest open | Latest close |
|---|---|---|---|---|---|---|---|
| 03/28/2026 Sat | 10:00–21:00 | — | 59 | 40 | 19 | 10:00 | 21:00 |
| 04/04/2026 Sat | 10:00–21:00 | — | 59 | 43 | 16 | 09:30 | 21:00 |
| 05/23/2026 Sat | 10:00–22:00 | — | 59 | 43 | 16 | 09:30 | 22:00 |
| 07/04/2026 Sat | 10:00–22:00 | 11:00–18:00 | 59 | 59 | 0 | 10:00 | 22:00 |
| 07/31/2026 Fri | 10:00–21:00 | 11:00–18:00 | 59 | 59 | 0 | 09:30 | 21:00 |
| 08/19/2026 Wed | 10:00–19:00 | — | 59 | 43 | 16 | 09:30 | 19:00 |
| 10/16/2026 Fri | 18:00–00:00 | — | 59 | 25 | 34 | 18:00 | 00:00 |
| 10/17/2026 Sat | 11:00–00:00 | — | 59 | 40 | 19 | 11:00 | 00:00 (also 18:00, 23:59) |
| 10/18/2026 Sun (today) | 11:00–23:00 | — | 59 | 39 | 20 | 11:00 | 23:00 |

Closed days (03/25, 04/01) have `venues: []` — no ride records at all.

**Verified across all sampled dates:**

- The ride list is a fixed catalog of **59 rides**, identical (same IDs and names) on 03/28 and 10/18.
- `extLocationID` (e.g. `RIDE-030-00072`) is the stable identifier. `itemID` is always `-1`.
- Each row has only `itemID`, `itemName`, `extLocationID`, `operatingTimeFrom`, `operatingTimeTo`.
  The venue has `assignmentHours` (always empty). **No field anywhere describes actual operation,
  downtime, delays, weather holds, maintenance or closure reasons.**
- Scheduled times are on-the-hour or half-hour plan values (09:30, 10:00, 18:00, 23:59…), never
  minute-level operational times.

Overall: 391 of 531 ride rows on the nine open dates have hours (**74%**); excluding the 16 water-park
rides, 359 of 387 (**93%**).

## 3. Specific rides

| Ride | `extLocationID` |
|---|---|
| Fury 325 | `RIDE-030-00072` |
| Thunder Striker | `RIDE-030-00024` |
| Copperhead Strike | `RIDE-030-00098` |
| Woodstock Express | `RIDE-030-00055` |

All four were listed on every open date sampled. "Listed, blank" means the row exists with empty
`operatingTimeFrom`/`operatingTimeTo`. No historical file was unavailable for these dates.

| Date | Ride | Scheduled opening | Scheduled closing | Source result |
|---|---|---|---|---|
| 03/28/2026 | Fury 325 | 10:00 | 21:00 | Listed with scheduled hours |
| 03/28/2026 | Thunder Striker | 10:00 | 21:00 | Listed with scheduled hours |
| 03/28/2026 | Copperhead Strike | 10:00 | 21:00 | Listed with scheduled hours |
| 03/28/2026 | Woodstock Express | 10:00 | 21:00 | Listed with scheduled hours |
| 07/04/2026 | Fury 325 | 10:00 | 22:00 | Listed with scheduled hours |
| 07/04/2026 | Thunder Striker | 10:00 | 22:00 | Listed with scheduled hours |
| 07/04/2026 | Copperhead Strike | 10:00 | 22:00 | Listed with scheduled hours |
| 07/04/2026 | Woodstock Express | 10:00 | 22:00 | Listed with scheduled hours |
| 08/19/2026 | Fury 325 | 10:00 | 19:00 | Listed with scheduled hours |
| 08/19/2026 | Thunder Striker | 10:00 | 19:00 | Listed with scheduled hours |
| 08/19/2026 | Copperhead Strike | 09:30 | 19:00 | Listed with scheduled hours |
| 08/19/2026 | Woodstock Express | 10:00 | 19:00 | Listed with scheduled hours |
| 10/16/2026 | Fury 325 | — | — | Listed without scheduled hours |
| 10/16/2026 | Thunder Striker | — | — | Listed without scheduled hours |
| 10/16/2026 | Copperhead Strike | 18:00 | 00:00 | Listed with scheduled hours |
| 10/16/2026 | Woodstock Express | — | — | Listed without scheduled hours |
| 10/17/2026 | Fury 325 | — | — | Listed without scheduled hours |
| 10/17/2026 | Thunder Striker | — | — | Listed without scheduled hours |
| 10/17/2026 | Copperhead Strike | 11:00 | 00:00 | Listed with scheduled hours |
| 10/17/2026 | Woodstock Express | 11:00 | 18:00 | Listed with scheduled hours |

Additional dates (same rides): 04/04 — Fury 10:00–21:00, Thunder Striker 09:30–21:00, Copperhead
10:00–21:00, Woodstock 10:00–21:00. 05/23 — Fury 10:00–22:00, Thunder Striker 10:00–22:00,
Copperhead 09:30–22:00, Woodstock 10:00–22:00. 07/31 — Fury 09:30–21:00, the others 10:00–21:00.
10/18 (today) — Fury blank, Thunder Striker blank, Copperhead 11:00–23:00, Woodstock 11:00–18:00.

## 4. Examples of populated and blank schedules

- **Fully populated:** 07/04 and 07/31 — all 59 rides timed, including water-park rides (11:00–18:00,
  matching the Waterpark gate).
- **Blank pattern that tracks a published closure (Verified correlation, cause Likely):** the 16
  `WP - …` rides are blank on every date without a Waterpark (id 70) gate entry and timed on every
  date with one.
- **Blank pattern that tracks an event format (Likely):** on 10/16 (SCarowinds evening-only) the
  Planet Snoopy family rides (Woodstock Express, Camp Bus, Snoopy vs Red Baron, …) are blank. On
  10/17, a Tricks and Treats day, Woodstock Express is timed 11:00–18:00.
- **Blank pattern with no visible explanation:** Fury 325 and Thunder Striker are blank on 10/16,
  10/17 and 10/18 while the park is open and most coasters are timed. Hurler, WindSeeker and
  Electro-Spin are blank on 03/28 (opening weekend). The feed gives no reason. These might be
  maintenance, a refurbishment, a seasonal plan or a data omission — **Unknown**.

## 5. Historical retention and reliability

**How far back (Verified):** daily files are served from **03/25/2026** onward; **03/24/2026 and
earlier return 404**. The Phase 20C check on 2026-10-16 found the same boundary. Two days later it has
not moved, so this is **not a rolling window by days** (**Likely** a fixed start: when this feed
began producing files). Whether old files will be pruned later (e.g. at season or year end) is
**Unknown**. In Phase 21A, January 2026 dates were already gone.

**Past files appear frozen at end of day (Verified from headers, interpretation Likely):** every
retained past file has `last-modified` at about **03:55 UTC the next day = 11:55 PM Eastern on the
operating date itself** (03/26, 03/29, 04/02, 04/05, 05/24, 07/05, 08/01, 08/20, 10/17, 10/18).
None was modified after that. Today's file is rewritten during the day (22G saw 23:55 and 00:00 UTC
writes five minutes apart; today 06:25 UTC). So each past file is the **final schedule as of 11:55 PM
on that date**, then never touched again.

**Not rewritten retroactively (Verified for the window available):** the four files 22G captured at
8:00 PM Eastern on 10/17 (07/04, 08/19, 10/16, 10/17) are byte-identical (same MD5 and ETag) to
today's responses. For 07/04, 08/19 and 10/16 this confirms nothing changed in about 10 hours. A
retroactive rewrite weeks later cannot be ruled out. With no older snapshot, **long-term
retroactive stability cannot be established.** The headers are the only evidence.

**Monthly responses are not history (Verified):** `?date=202607` returns only 07/31/2026. That entry
equals the 07/31 daily entry (identical ride rows). Past ride history needs one daily request per
date, as the Phase 20C backfill already does for gate hours.

**Schedule revisions before/during the day:** three captures of 10/18 — month file at 6:56 PM and
8:00 PM on 10/17 and the daily file at 2:27 AM on 10/18 — have **identical ride rows**. The only
difference is the order of the two `events` items. No revision was observed. Revisions during an
operating day were **not** tested: no capture spans 10/18 operating hours.

**Forward coverage (context):** in the 10/17 month files, ride hours were present through 11/06 and
absent from 11/07 onward. The schedule fills in a few weeks ahead.

## 6. Complaint-research questions

1. **Can we look up the scheduled hours of a specific ride on a past date?** Yes, for 03/25/2026 onward,
   by `extLocationID`, one daily request per date. What comes back is the planned schedule, not what
   happened.
2. **How far back?** 03/25/2026 (earliest retained). The season's first open days are covered. Future
   retention is not guaranteed.
3. **What proportion of rides have populated times?** 74% of ride rows on sampled open days. 93%
   excluding the water park. All 59 on full-park summer days. As low as 25 of 59 on SCarowinds evenings.
4. **Does a blank schedule reliably mean the ride was down?** **No.** Some blanks line up with published
   closures (water park closed, family area during evening-only SCarowinds). Others (Fury 325,
   Thunder Striker in October) have no stated reason. A blank means "not on the published schedule",
   not "was down".
5. **Any evidence of actual downtime, unexpected closure or maintenance?** **None.** The feed has no
   status, downtime, reason or timestamp field per ride. An unplanned breakdown at 2 PM would not
   appear, and the 11:55 PM file is not evidence the ride ran.
6. **Would saving daily snapshots add value?** Limited. The source already keeps frozen end-of-day
   files back to 03/25, so a saved copy mostly adds insurance against future pruning. It also adds
   independent evidence that the official file was not rewritten later. It adds no new kind of
   information.
7. **Would collecting several times a day reveal revisions?** **Unknown.** The file is rewritten during
   the day, but no change to ride rows has been observed so far. Only intraday captures can show
   whether a ride's hours are cleared or shortened when it goes down. If that happened, it would be
   the only route to anything near closure evidence, and it is unproven.
8. **What evidence is still missing for a guest complaint?** Whether the ride actually opened, when
   it went down and came back, the reason (mechanical, weather, staffing, evacuation), whether a
   blank meant "planned closed" or "data missing", whether any guest notice was posted, and
   anything before 03/25/2026. These need internal ride-operations logs, not this feed.

## 7. Reliability limitations

- Planned schedule only; no actual operation or downtime, ever.
- Blank hours are ambiguous; never present a blank as "closed" or "down".
- Retention start (03/25/2026) is observed behavior, not a documented contract; it may change.
- End-of-day freezing is inferred from `last-modified` headers on a third-party-hosted (S3/CloudFront)
  file; not documented.
- No time zone in the data (America/New_York assumed, consistent with the 11:55 PM freeze).
- Each daily file is about 28–31 KB because it repeats the 196-location catalog. A full-season
  backfill (03/25 to today) is about 210 GETs and about 6 MB downloaded, but only small normalized rows need storing.

## 8. Recommendation

Building Ride Schedule History is **defensible only as a clearly labeled "scheduled hours on the
official park schedule" archive**, never as ride status. It would answer "was this ride on the
published schedule that day, and for what hours?" from 03/25/2026 onward. It cannot answer "was the
ride down?", the usual guest complaint.

If authorized, the cheap, low-risk shape is: an on-demand lookup or backfill of past daily files
(reusing the existing per-date history request), storing normalized per-ride rows by `extLocationID`,
labeled "Scheduled (official plan, as of end of day)". Blanks are shown as "No scheduled hours listed",
never "Closed". Intraday snapshotting should be a separate, later experiment, run first as research
over a few operating days, to test whether ride rows change when rides go down.

## Final decision

**B. Limited historical schedule data** — potentially useful, but with substantial missing
information.

Supporting evidence:

- **For:** ride-level scheduled hours exist for every open date from 03/25/2026 to now. Each date
  carries the same 59 rides with stable `extLocationID`s, 68–100% of rides are timed on daytime operating days,
  and past files appear frozen at 11:55 PM on the operating day. No retroactive change was found.
- **Against:** the data is a plan, not a record. There is no downtime, closure-reason or actual-operation
  field. Blank hours are ambiguous (Fury 325 and Thunder Striker blank all of mid-October with no
  explanation). Intraday revision behavior is unproven, and long-term retention is not guaranteed.
  The core complaint question ("was the ride down when I was there?") cannot be answered from this
  source.
