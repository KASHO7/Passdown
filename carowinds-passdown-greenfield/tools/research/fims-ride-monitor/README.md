# FIMS ride schedule monitor (research only)

A standalone tool for Phase 22G.2. It samples the official Carowinds FIMS park-hours feed for
**one operating date** at intervals and records each ride's **published scheduled hours**. A
second script then reports how that schedule changed during the day.

It is not part of Carowinds Passdown. It does not touch the app, its database or its profile, and
it uses only the Python 3 standard library (3.10 or newer). Background and findings are in
`docs/research/fims-intraday-ride-monitoring.md`.

> The feed publishes a plan. It has no live ride status or downtime field. A change this tool finds
> is a **published schedule change**, never proof that a ride was closed or down.

## Commands

Run these from this folder (`tools/research/fims-ride-monitor`). On Windows, use `py` instead of
`python3`.

| Task | Command |
|---|---|
| One observation now | `python3 monitor.py snapshot --date 2026-10-24` |
| Watch an operating day | `python3 monitor.py watch --date 2026-10-24 --end-at 23:30` |
| Watch with a custom interval | `python3 monitor.py watch --date 2026-10-24 --interval-minutes 30 --end-at 23:30` |
| Write the report | `python3 compare.py --date 2026-10-24` |
| Record something you saw yourself | `python3 notes.py add --date 2026-10-24 --ride "Copperhead Strike" --time 15:10 --description "…"` |
| List those notes | `python3 notes.py list --date 2026-10-24` |
| Run the tests | `python3 -m unittest discover -s tests -t .` |

Watch options:

- `--interval-minutes`: 30–120, default 60. Values under 30 are refused.
- `--end-at HH:MM`: stop at this Eastern time, 24-hour. A time earlier than the start time means
  after midnight, so `00:30` stops just after midnight.
- `--duration-hours`: hard limit, default and maximum 16.
- The watch stops at whichever limit comes first, after 3 failed observations in a row, or on
  **Ctrl+C**. Observations already saved are kept.

The first sample is taken immediately, then one per interval. To cover a full day, start it before
the park opens and set `--end-at` about an hour after the published closing time.

For the "later historical lookup" check, run one `snapshot` for the same date on a later day after
6:00 AM ET, then re-run `compare.py`. The report compares that lookup with the final observation
from the day.

## Request rules (built in)

- HTTP GET of `https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=YYYYMMDD`, for
  the requested date only. No other park, endpoint or date is requested.
- No cookies, authentication or tokens. Redirects are not followed.
- User agent `CarowindsPassdown-FIMSRideScheduleResearch/1.0 (…)`. Connect timeout 10 s, read
  timeout 30 s, responses over 2 MB discarded.
- One request at a time. No immediate retries: the next scheduled sample is the retry. No catch-up
  bursts after a pause, e.g. a sleeping laptop.
- HTTP 429: `Retry-After` is honoured. If it reaches past the window, the watch stops.

## Output

`output/<date>/` (git-ignored):

- `obs-<UTC timestamp>.json` — one compact, normalized observation per request, failures included.
  Files are written atomically and never overwritten.
- `report-<date>.md` — written by `compare.py`, replaced each time it runs.
- `user-observations.json` — only if you use `notes.py`. It is kept apart from the official data.

Raw responses are not saved.

## Privacy

`notes.py` is for short operational facts only, for example a ride seen not operating at about
3 PM. Do not record guest identities, complaint contents, employee names or private documents.
