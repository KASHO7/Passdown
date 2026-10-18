# Phase 22G.2 — Intraday FIMS ride schedule monitoring

Research tooling only. No application code, existing FIMS parser, retrieval logic, SQLite schema,
Draft/Sync format, UI, PDF, Teams Copy or release metadata changed. RC.6 artifacts untouched; RC.7
not packaged. Nothing pushed.

**Status: tool built and tested. No operating-day study has been run yet, so no intraday
conclusion is drawn here.** The findings below are limited to one live snapshot.

## 1. Objective

Phase 22G.1 (`fims-historical-ride-data-verification.md`) found that past daily FIMS files keep each
ride's scheduled hours, appear frozen at about 11:55 PM ET on the operating day, and contain no
downtime information. It left open whether the published ride schedule **changes during an
operating day**. For example, does a ride's hours get cleared or shortened when it stops operating?
If it does, FIMS could give limited supporting evidence when researching a guest's complaint about a
ride being unavailable.

Questions this tool is built to answer once real observations exist:

1. Does FIMS update ride scheduled hours during an operating day?
2. Can a populated ride schedule become blank?
3. Can a blank schedule become populated?
4. Can scheduled opening or closing times change?
5. When a change occurs, how quickly is it reflected? (Bounded by the sampling interval.)
6. Do scheduled hours remain stable after park closing?
7. Does the historical daily file keep the final observed schedule?
8. Does any change correspond to unexpected downtime? This needs **independent operational
   evidence**; a FIMS change alone never answers it.

Context from operations: Fury 325 and Thunder Striker were **planned closed** on 10/17/2026. Their
blank FIMS times that day are consistent with that. A blank value alone is still never treated as a
confirmed closure.

## 2. Location and setup

`tools/research/fims-ride-monitor/`:

| File | Purpose |
|---|---|
| `fims_common.py` | URL, fetch (one GET), normalization, Eastern time, atomic storage, loading |
| `monitor.py` | `snapshot` and `watch` commands |
| `compare.py` | Change detection and the Markdown report |
| `notes.py` | Separate file for user-reported operational observations |
| `tests/` | 64 deterministic tests on synthetic fixtures; all HTTP mocked; network connections blocked |
| `README.md` | Usage |
| `.gitignore` | Ignores `output/` and Python caches |

Setup: Python 3.10 or newer, standard library only. No install step and no change to the .NET
solution. Run every command from the tool folder. On Windows use `py` in place of `python3`.

On Windows the Eastern conversion uses a built-in US Eastern rule (2007-onward DST) when Python has
no time-zone database. Tests check it hour by hour against `zoneinfo` across both 2026 DST
transitions. **Flag for Windows VM testing**: the tool has only been run on macOS.

## 3. Commands

```text
python3 monitor.py snapshot --date 2026-10-24
python3 monitor.py watch --date 2026-10-24 --end-at 23:30            # hourly (default)
python3 monitor.py watch --date 2026-10-24 --interval-minutes 30 --end-at 23:30
python3 compare.py --date 2026-10-24
python3 notes.py add --date 2026-10-24 --ride "Copperhead Strike" --time 15:10 --description "…"
python3 -m unittest discover -s tests -t .
```

**Stopping:** the watch stops by itself at `--end-at` (Eastern) or after `--duration-hours`
(default and maximum 16), whichever comes first. It also stops after 3 failed observations in a
row, or when a 429 `Retry-After` extends past the window. Press **Ctrl+C** to stop early. Saved
observations are kept, and a half-written file is never left as an observation. The tool creates no
background services or scheduled tasks.

## 4. Request limits

- `GET https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=YYYYMMDD`, for the studied
  date only. It is the same public file the official park-hours page loads. No other park, month
  file or endpoint is requested.
- No cookies, authentication or tokens. Redirects are not followed.
- User agent: `CarowindsPassdown-FIMSRideScheduleResearch/1.0 (read-only research script; hourly sampling)`.
- Connect timeout 10 s, read timeout 30 s, responses over 2 MB discarded.
- Interval 30–120 minutes, default 60; anything under 30 is refused. One request at a time.
- No immediate retries: the next scheduled sample is the retry. After a long pause (e.g. a sleeping
  laptop), the next sample is a full interval later, never a catch-up burst.
- HTTP 429: `Retry-After` (seconds or HTTP date) is honoured.
- An hourly 16-hour watch makes at most 17 requests, about 30 KB each.

## 5. Normalized observation schema (`fims-ride-observation/1`)

One file per request: `output/<date>/obs-<yyyyMMddTHHmmssZ>.json`. A same-second collision gets a
`-1`, `-2` suffix. Files are written to a temp file, fsynced, then hard-linked into place, so an
existing file is never overwritten. Raw responses are not saved. A full observation is about 9.5 KB.

| Field | Meaning |
|---|---|
| `operating_date` | Requested date, `YYYY-MM-DD` |
| `observed_utc`, `observed_eastern` | When the request was made |
| `source_url`, `http_status`, `etag`, `last_modified` | Response facts (`last_modified` is the `Last-Modified` header) |
| `outcome` | `ok`, `park_closed`, `not_found`, `rate_limited`, `http_error`, `network_error`, `invalid_response`, `incomplete` |
| `comparable` | True only for `ok` / `park_closed` |
| `detail`, `warnings` | Why it failed; non-fatal oddities |
| `park_closed` | `isParkClosed` |
| `gate[]` | Main gate (operating type id 24) and water park (id 70): label, from, to |
| `ride_count`, `rides_with_hours`, `rides_without_hours`, `rides_unreadable` | Counts over `venueId 1` only |
| `duplicate_ids` | IDs that appeared more than once with different values |
| `rides[]` | `id` (`extLocationID`), `name` (`itemName`), `from`, `to`, `populated`, `state` |

Ride `state` values:

| State | Meaning |
|---|---|
| `populated` | Both times valid `HH:mm` |
| `blank` | Both times empty |
| `missing_fields` | Data quality: a time key absent or null |
| `invalid` | Data quality: one time blank, or a malformed time |
| `conflicting_duplicate` | Data quality: the same ID listed twice with different values |

Only rides under `venueId 1` (Rides) are read. The full 196-location catalog is not treated as a
list of operating rides. Rides are keyed by `extLocationID`, never by list position or name order.
Closing times after midnight (`00:00`) are shown as "(next day)".

## 6. Comparison rules

`compare.py` loads every observation for the date. It orders them by the recorded UTC timestamp,
never by filename or Eastern clock time; the repeated 1 AM hour in November is handled. It then
compares each successful observation with the **previous successful** one:

| Detected | Rule |
|---|---|
| Blank → populated | `blank` → `populated` |
| Populated → blank | `populated` → `blank` |
| Opening time changed | Both populated, `from` differs |
| Closing time changed | Both populated, `to` differs (both changes are reported if both differ) |
| Ride added / removed | ID present in only one of two **complete** open-day responses |
| Ride renamed | Same ID, different `itemName` |
| ETag-only change | ETag differs, ride + gate + park data identical |
| Last-Modified-only change | File re-saved, ETag (content) unchanged |
| Gate / park-closed change | Reported separately from rides |
| Data-quality difference | Any transition involving `missing_fields` / `invalid` / `conflicting_duplicate`; listed apart and **not** counted as a schedule change |

Safeguards:

- A failed or incomplete response (timeout, 404, 429, 5xx, malformed JSON, missing date, missing
  Rides venue, empty ride list, missing `venues`) is saved and counted as failed. It is never
  compared, so it cannot make rides look removed. The last successful observation is kept for the
  next comparison.
- When the park-closed flag differs, the report shows a park-level change, not 59 ride removals.
- Observations made at or after 6:00 AM ET on a later day are treated as **later historical
  lookups**. They are compared with the final observation from the operating day (question 7).
- Observations after the published main-gate closing time are counted, along with any changes
  first seen after closing (question 6).
- User notes (`notes.py`) appear in their own report section, labeled "user-reported operational
  observation". They are never matched to FIMS changes automatically. The report keeps official
  data, user observations and a blank "Researcher's interpretation" section apart.
- Every change is labeled a **published schedule change**. The tool has no downtime or closure
  classification at all, and a test checks that no report states one.

## 7. Report and sample output

`compare.py` writes `output/<date>/report-<date>.md` with these sections: study summary (date,
start/end, successful/failed counts, interval, unique rides, scheduled/blank counts, rides
changed); the change table and changes grouped by ride; first and last observed value per ride; gate
and park-level changes; ETag/Last-Modified behaviour; first vs final observation, after-closing and
later-lookup checks; user observations; researcher's interpretation (left blank); limitations.

**Illustrative example only (synthetic test data, not an observation):**

```text
| Old seen (ET)          | New seen (ET)         | Ride                                 | Old schedule        | New schedule              | Change            |
| 2026-10-24 11:00 AM EDT | 2026-10-24 3:00 PM EDT | Copperhead Strike (`RIDE-030-00098`) | 11:00 AM – 10:00 PM | No scheduled hours listed | Populated → blank |

#### Copperhead Strike (`RIDE-030-00098`)
- 2026-10-24 11:00 AM EDT: 11:00 AM – 10:00 PM
  → 2026-10-24 3:00 PM EDT: No scheduled hours listed
  - Change: Populated → blank
  - Interpretation: FIMS changed the published schedule between these observations.
    Actual downtime or closure is not established.
```

"Old seen" is the last observation that showed the old value. "New seen" is the first that showed
the new one. The change was published somewhere between the two.

With no changes, the report states:

**No ride schedule changes were observed during the sampled intervals. This does not establish that
no rides experienced downtime.**

## 8. Opt-in live snapshot (actual result)

One live request, 2026-10-18 2:44 AM EDT (06:44 UTC): `monitor.py snapshot --date 2026-10-18`.

- HTTP 200. Gate 11:00–23:00. 59 rides listed: 39 with scheduled hours, 20 blank (Fury 325 and
  Thunder Striker among the blanks). This matches the Phase 22G.1 fetch of the same date at
  06:27 UTC.
- ETag `"8cc194a390b6b6c36ad1dc9c5f15c35a"`, the same as at 06:27 UTC. `Last-Modified` moved from
  06:25:03 to 06:40:05 UTC. So **today's file is re-saved during the day even when its content is
  unchanged**, and only ETag/content changes are meaningful. This is the only intraday fact
  established so far.
- The report generated correctly from that single observation and stated the no-change sentence.

## 9. Known limitations

- FIMS publishes scheduled hours only. There is no live status, downtime, delay or reason field, so
  even a perfect study can only show *published schedule* changes.
- A blank is not a closure; the cause of any change is not in the data.
- The resolution equals the sampling interval: a change is known only to lie between two
  observations, and anything changed and reverted within an interval is invisible.
- One machine and one network; a sleeping or offline computer leaves gaps. Gaps appear in the
  interval statistics.
- The 6:00 AM "later historical lookup" boundary and the 11:55 PM freeze are observed behaviour, not
  documented by the source.
- No study has run, so every research question above is still open.

## 10. Interpreting results

- **No changes in a full-day study:** consistent with FIMS being a fixed daily plan. It does not
  mean every ride operated.
- **Populated → blank, or an earlier closing time:** FIMS removed or shortened the published
  schedule between two observations. Compare it with independent operational evidence (a
  user-reported observation of the same ride near that time) before forming any view, and write that
  view in the "Researcher's interpretation" section by hand.
- **Blank → populated / later opening:** the schedule was added or revised. It may be a late
  publication rather than an operational event.
- **ETag changes with identical data:** the file was regenerated with different content outside the
  ride/gate data (e.g. shows or events), or the order of entries changed. It is not a ride change.
- **Later lookup differs from the final observation:** the historical file was rewritten after the
  day. That would weaken any use of past files as a record.

## 11. Running the first study

1. On an operating day, before opening (e.g. 9:30 AM for an 11:00 AM opening), in the tool folder:
   `python3 monitor.py watch --date <today> --end-at <about one hour after the published close>`.
   Leave the window open; the computer must stay awake.
2. If you see a ride not operating, record it in a second window:
   `python3 notes.py add --date <today> --ride "<ride>" --time HH:MM --description "<what you saw>"`.
   Record operational facts only, no guest or employee details.
3. The watch stops on its own. Then run `python3 compare.py --date <today>` and open
   `output/<today>/report-<today>.md`.
4. The next morning (after 6:00 AM ET), run `python3 monitor.py snapshot --date <that date>` once,
   then `python3 compare.py --date <that date>` again to answer question 7.
5. Share the report for review **before** any decision on Ride Schedule History.
