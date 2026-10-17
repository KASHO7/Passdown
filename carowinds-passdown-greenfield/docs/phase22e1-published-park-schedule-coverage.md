# Phase 22E.1 — Automatic Published Park Schedule Coverage

The Operations Calendar now fills in every officially published future operating date for about a year
ahead on its own. December's WinterFest hours appear without anyone opening a December date, and a month
Carowinds publishes later shows up after the next daily maintenance run. Nothing about the Calendar's
layout changed.

Baseline: Phase 22E commit `458c527`. Schemas unchanged: DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1.
No migration. RC.6 artifacts and the app version (`0.1.0-rc.6` in `Directory.Build.props`) are untouched.

## 1. Root cause

There were two independent causes. Each one alone would have hidden December.

1. **Fixed horizon.** `ParkHoursCoveragePlanner.TargetHorizon` (Phase 13D) set the Deep Refresh Park Hours
   window to "today through the end of next calendar month". On 2026-10-17 that is Nov 30. The 6 AM
   scheduled task, startup catch-up and manual Deep Refresh all stopped there. The four-hour check and
   Check Online cover only the 7-day rolling window or one date, so December was reached only by opening a
   December date in Associate.
2. **Seasonal label not recognized.** `ParkHoursSource` picked the main-gate entry by the label
   `operatingTypeName == "Park"`. WinterFest and New Year's Eve days publish that same main-gate entry with
   the same official id (24) but a different label (`"Winterfest"`, `"New Years Eve"`). Those days were
   stored as **NotPublished**, even through a single-date check. This already affected Nov 27–29.

Ruled out: there is no hard-coded end date, the scheduler runs fine, the monthly endpoint pages correctly,
and the official website and the feed agree.

## 2. Official source evidence (checked live 2026-10-17)

**Source.** This is the same public CloudFront JSON the official page `sixflags.com/carowinds/park-hours`
fetches: `operating-hours/park/30?date=yyyyMMdd` (one day) or `?date=yyyyMM` (a whole month). See
`docs/retrieval/park-hours-source.md`.

**Official page.** In the built-in browser, the page's December calendar shows:

- **12/05/2026**: "Park 12:00 PM to 9:00 PM", with WinterFest listed as the special event.
- **12/31/2026**: "Park 3:00 PM to 1:00 AM".

Both match the feed's operating-type-24 entries, which carry the labels "Winterfest" and "New Years Eve".

**How the page picks the main-gate entry.** The page's server data defines `operatingTypeId 24 → "Park"`
and `70 → "Waterpark"`. Its shipped JavaScript matches feed entries by
`` `${e.operatingTypeId}` == `${t.operatingTypeId}` ``, not by label. The adapter now does the same, with
the label as a fallback only when an entry has no id.

**Month form, one request per month** (status 200 for every month):

| Month | Dates listed | Verified | Closed | NotPublished |
|---|---|---|---|---|
| 2026-10 | 14 (10/18–10/31; the current month lists tomorrow onward) | 8 | 6 | 0 |
| 2026-11 | 30 | 11 (8 "Park" + 3 "Winterfest") | 19 | 0 |
| 2026-12 | 31 | 16 ("Winterfest", plus Dec 31 "New Years Eve") | 15 | 0 |
| 2027-01 | 31 | 0 | 29 | 2 (Jan 1–2: open flag, no hours) |
| 2027-02 … 2027-09 | every date | 0 | 0 | all |
| 2027-10 | 17 (through 10/17/2027, exactly one year out) | 0 | 0 | 17 |

- **Last published operating date:** 2026-12-31, open 3:00 PM–1:00 AM.
- **Last date the feed answers:** 2027-10-17.
- **Response size:** about 58–850 KB per month raw, mostly `venues`. Nothing raw is stored.

No hours were inferred from the previous year or from neighbouring dates.

## 3. Coverage: before and after

| | Before (Phase 13D) | After |
|---|---|---|
| Deep Refresh Park Hours plan | One request per date, today → end of next month (~31–62 requests; 45 on Oct 17) | One request per date for the 7-day rolling window, plus one request per month through **today + 12 months** (13 month requests; 20 in total) |
| Furthest date stored on Oct 17 | Nov 30, 2026 | Oct 17, 2027 |
| WinterFest / New Year's Eve days | NotPublished | Verified hours |
| Four-hour automatic check | Rolling window only | Unchanged |
| Check Online | One date; bootstraps Deep Refresh when coverage is short of end of next month | Unchanged (the bootstrap threshold is still end of next month) |

## 4. Forward horizon

- `RefreshOrchestratorOptions.ParkHoursDiscoveryMonths` sets the horizon. It can be bound from
  configuration section `Refresh` and defaults to **12**. It is clamped to 1–18 months
  (`ParkHoursCoveragePlanner.MaxDiscoveryMonths`).
- The horizon ends at `today.AddMonths(n)`, which matches the feed's own one-year reach.
- `ParkHoursCoveragePlanner.TargetHorizon` (end of next month) is unchanged. It is now only the minimum
  coverage below which Check Online bootstraps a Deep Refresh. If the feed ever publishes less than a
  year, Check Online therefore stays a fast single-date check.

## 5. Discovery algorithm

`RefreshOrchestrator.RunDeepRefreshAsync` now receives an optional `ParkHoursScheduleDiscovery`. The app
registers it. When it is absent, the Phase 13D behaviour is kept exactly.

1. **Near term.** Park Hours for the rolling window (today … today + 6) is fetched one date at a time, as
   before. It shares the front ranks with every other source's near-term dates, so today is dispatched
   first for all sources.
2. **Focus date.** A focus date beyond the horizon (from the Check Online bootstrap) gets its own
   single-date request.
3. **Months.** For each month from the month after the window through the horizon month, a
   `ParkHoursMonthRegistration` makes one `?date=yyyyMM` request. These are ranked after all near-term
   work and run under the same `MaxConcurrency` (3) limiter.
4. **Per-date evaluation.** Each listed date goes through the same rules as a single-date check
   (`ParkHoursSource.EvaluateDay`). Results are written to the existing `dated_results` rows keyed by
   (`ParkHours`, date). Only dates after the rolling window and up to the horizon are written.
5. **Coverage.** `CoverageThrough` is the last consecutive covered date starting from today. A date counts
   as covered if its single-date request succeeded or a month response listed it and its result is now
   stored.

There is no second polling engine. Discovery runs only inside a Deep Refresh and goes through the existing
gate, cross-process lock, cancellation and source-state bookkeeping. The month operations share the
`park-hours.official` source id.

## 6. Refresh cadence

| Trigger | What runs |
|---|---|
| 6 AM scheduled task (Windows) | Deep Refresh with discovery (unchanged trigger) |
| Startup | Missed-maintenance catch-up as before. It is **also** due when coverage is short of the discovery horizon and the last Deep Refresh attempt is ≥ 4 h old (`OfficialRefreshPolicy.ScheduleDiscoveryRetryInterval`). An upgraded install therefore discovers December at its first launch ≥ 4 h after the 6 AM run. |
| App left open (four-hour loop, every 5-minute evaluation) | After the normal four-hour check, the same due-gated maintenance runs when a new 6 AM boundary has passed or coverage is short and ≥ 4 h old. It needs a persisted Deep Refresh status (a first-ever run is startup's job), runs at most once per 4 h in-process, and is skipped while another process holds the refresh lock. |
| Manual Run Deep Refresh | Deep Refresh with discovery |
| Check Online / Associate date check | Unchanged (one date) |

Unchanged future months are not re-downloaded at every startup. Discovery runs once per daily boundary,
or every ≥ 4 h only while coverage is incomplete. Today and near-term retrieval always comes first.

## 7. Request limits

| Run | Park Hours requests |
|---|---|
| Deep Refresh, default horizon | 7 single-date + ≤ 13 month = **≤ 20** (was 31–62) |
| Configured maximum (18 months) | 7 + 19 = 26 |
| Four-hour check | 7 (unchanged) |
| Check Online | 1 (unchanged) |

No request is ever made per date beyond the rolling window. Responses are not cached across runs; the
existing 2-minute coalescing client still joins identical in-flight requests.

## 8. Publication semantics

| Feed answer for a listed date | Stored as |
|---|---|
| `isParkClosed: true` | **Closed** (replaces an earlier Verified result; an explicit official change) |
| Main-gate entry (id 24) with times | **Verified** (replaces earlier hours when they changed) |
| Open flag, no main-gate times | **NotPublished**. From a month listing this never replaces a stored Verified or Closed result, because it is ambiguous coverage, not a retraction. A single-date check (rolling window, Check Online, Associate date check) still applies a genuine unpublish. |
| Unreadable times | **Error**: failed attempt only; the last-known-good value is kept |

| Feed answer for the month | Effect |
|---|---|
| Date not listed (partial response) | Nothing is written. The date keeps its earlier value or stays **NotChecked** in the Calendar, never Closed. Coverage stops at the gap. |
| Date outside the month, invalid, or duplicated | Ignored (first entry wins); never substituted |
| Network error, timeout, HTTP error (404/429/503), unreadable or empty `dates` | **Error** for that month. Nothing is written for it, and every earlier value is kept. |

The **Unavailable** and **Stale** states are unchanged. Freshness indicators use the evidence time of each
stored date.

## 9. Failure handling

- A failed month operation makes the Deep Refresh **Partial**. The Park Hours source is marked failed, so
  it stays due. `CoverageThrough` stops before the failed month, and the ≥ 4 h retry rule picks it up again.
- Other months in the same run still store their dates, and other sources are unaffected.
- Cancellation (app exit, or supersession through the gate) reports months as Cancelled, writes nothing
  for them, and skips the source-state update. This is unchanged orchestrator behaviour.
- Historical backfill still yields to this work through the gate's low-priority rule.

## 10. Storage and performance (measured live, isolated scratch DB, real migrations → 16)

The harness used the real `RetrievalHttpClient` + `ParkHoursSource` + `RefreshOrchestrator` with SQLite
repositories, in the session scratchpad. It did not touch the real profile.

| Run | Requests | Elapsed | Coverage through | DB growth |
|---|---|---|---|---|
| Old plan (no discovery) | 45 | 6.5 s | 2026-11-30 | +24 KB |
| New plan, first run | 20 | 2.4 s | 2027-10-17 | +156 KB (366 future park-hours rows: 36 Verified, 69 Closed, 261 NotPublished) |
| New plan, second run | 20 | 3.0 s | 2027-10-17 | +0 (rows upserted in place) |

- **Storage.** About 430 bytes per date in the existing `dated_results` table: the normalized payload of
  ≤ 119 bytes plus evidence. There is one row per date, never a history of month responses. No raw
  HTTP/HTML is stored.
- **Startup.** The window still opens on cached data. Discovery runs on the existing background
  maintenance task and is now faster than the 45-request sweep it replaces.
- **Check Online.** Unchanged: one request. It no longer becomes a bootstrap merely because the horizon
  grew.

## 11. Calendar behaviour

- The Calendar reads local `dated_results` only. Browsing to December or later shows cached official
  hours and closures immediately, with no request.
- Unlisted dates show **Not checked**, never Closed. Each date's own evidence is shown; nothing is carried
  over from today.
- When maintenance finishes, the gate publishes `DataChangeKind.OfficialData`. The Calendar already
  re-reads the displayed month quietly on that signal, so new months appear without a restart.
- No new button. The Phase 22E compact/expandable Shows panel is untouched.

## 12. Show-schedule isolation

`ShowScheduleSource` is unchanged and keeps its rolling-window / Check Online scope
(`CurrentAndFutureDatedSourceRegistration`). Discovery handles Park Hours only. A date with published park
hours says nothing about its entertainment schedule. A test asserts that Deep Refresh stores no show
results beyond the rolling window.

## 13. Historical behaviour

The Phase 20C backfill (`IParkHoursHistorySource`, one daily request per past date) is unchanged. The
month form is never requested for a past month: discovery starts at the current month, and only dates
after the rolling window are written. Past shows and historical admission pricing policies are unchanged.
The id-based main-gate match also applies to backfilled dates. A past WinterFest date inside the feed's
retention window would now read as Verified rather than NotPublished, which corrects it from official data.

## 14. Code map

| Area | Files |
|---|---|
| Month contract | `Application/Retrieval/IParkHoursScheduleSource.cs` (`PublishedParkHoursMonth`) |
| Adapter | `Infrastructure/Retrieval/Sources/ParkHoursSource.cs` (id 24/70 match, `EvaluateDay`, `RetrieveMonthAsync`), `ParkHoursApiResponse.cs` (`OperatingTypeId`) |
| Discovery | `Application/Refresh/ParkHoursMonthRegistration.cs` (`ParkHoursScheduleDiscovery`, `ParkHoursMonthRegistration`) |
| Planning / coverage | `RefreshOrchestrator.PlanPublishedScheduleDiscovery`, `PersistDeepRefreshStatusAsync`; `SourceOperationResult.CoveredDates`; `ParkHoursCoveragePlanner.DiscoveryHorizon`/`MonthsBetween`; `DeepRefreshCoverage.NeedsScheduleDiscovery` |
| Cadence | `OfficialRefreshPolicy.IsDailyMaintenanceDue` (discovery rule), `OfficialDataRefreshCoordinator.RunMaintenanceIfDueAsync` |
| Options / DI | `RefreshOrchestratorOptions.ParkHoursDiscoveryMonths`; `App.axaml.cs` registers `ParkHoursScheduleDiscovery` |

## 15. Testing

The fixtures under `fixtures/park-hours/months/`, plus `winterfest-date.json` and `new-years-eve-date.json`,
are official responses recorded on 2026-10-17 with `venues`/`shows`/`events` stripped. The `derived-*`
files each make one documented edit to a recording: removed dates, changed hours or closures, or malformed
entries.

| Class | Tests | Covers |
|---|---|---|
| `ParkHoursPublishedScheduleTests` (Infrastructure) | 19 | WinterFest and NYE single dates; month URL; December (16 Verified / 15 Closed); November mixed labels and the midnight close; current month lists only remaining days; January NotPublished vs Closed; far-future all NotPublished; horizon month ends at the feed's end; missing dates absent; unreadable/foreign/invalid entries; duplicates; empty, 404, 503 HTML, malformed, 429; timeout/offline; cancellation |
| `Phase22E1PublishedScheduleTests` (Application) | 23 | Rolling window + 13 months (20 ops); configured horizon bounds 1/3/100; today dispatched before any month; never a past month; focus beyond horizon; December stored in October; the rolling window is never overwritten; coverage reaches the horizon; month published later; schedule changed later; NotPublished never erases Verified/Closed; partial month (untouched, never closed, coverage gap); failed month keeps LKG / Partial / stays due; unreadable entry; cancellation; shows stay on the rolling window; maintenance cadence (old coverage, ≥ 4 h, full coverage, configured months); horizon clamp; Calendar projection (Open/Closed/NotChecked, no request) |
| `OfficialDataRefreshCoordinatorTests` (+7) | 7 | Startup on old coverage runs maintenance; no repeat within 4 h; four-hour loop runs discovery once and publishes `OfficialData` (Calendar reload); full coverage → no maintenance but the normal check still runs; no status yet → left to startup; next-day boundary; other process holds the lock |
| `ParkHoursSourceLiveSmokeTests` (opt-in `LiveSmoke`, +1) | 1 | Every month of the horizon answers with readable dates (13 GETs) — passed live 2026-10-17 |

Existing Deep Refresh, coverage, historical backfill, show-schedule and Calendar tests pass unchanged. The
Phase 13D path (no discovery injected) keeps its own tests.

Totals: **3885/3885** deterministic (baseline 3836; +19 Infrastructure, +30 Application) = 221 Domain +
1089 Application + 852 Infrastructure + 451 Documents + 1176 Ui together + 26 + 57 + 2 + 7 + 4 run-alone
(`-c Release`, `--filter "Category!=LiveSmoke"`). The known-intermittent
`DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender` failed once
in this phase's full run and passed on rerun; it is unrelated to this change. Release build: 0 warnings,
0 errors.

## 16. Windows validation checklist

Windows 11 x64, with a build containing this phase (a future RC, not RC.6).

| # | Check | Status |
|---|---|---|
| W1 | Upgrade an RC.6 profile whose Deep Refresh ran this morning. Launch ≥ 4 h after it: the log shows "Startup catch-up maintenance needed … coverage (through yyyy-11-30)" and then a Deep Refresh of about 20 operations | NOT EXECUTED |
| W2 | Calendar → December: WinterFest dates show hours (e.g. Dec 5 12:00 PM–9:00 PM), Dec 24–25 Closed, Dec 31 3:00 PM–1:00 AM; no network activity while browsing months | NOT EXECUTED |
| W3 | Browse to Feb–Sep next year: dates show Not Published (not Closed) | NOT EXECUTED |
| W4 | Dashboard Deep Refresh status: coverage through about one year ahead | NOT EXECUTED |
| W5 | 6 AM scheduled task: the log shows about 20 Park Hours operations, Success | NOT EXECUTED |
| W6 | Leave the app open past 6 AM without the task: one background maintenance run, and the Calendar updates in place | NOT EXECUTED |
| W7 | Offline launch: no errors surface beyond the status line; Calendar values unchanged; online again after ≥ 4 h → discovery retried | NOT EXECUTED |
| W8 | Check Online on any date: single date, fast (no Deep Refresh bootstrap) | NOT EXECUTED |
| W9 | Calendar Shows panel on a December date: unchanged wording; no shows implied by the park hours | NOT EXECUTED |
| W10 | Historical backfill status line still completes; past months unchanged | NOT EXECUTED |

## 17. Known limitations

- The horizon depends on the feed's own one-year reach. Dates the feed lists as "open flag, no hours" are
  shown as Not Published, even though the official page renders them as "Closed". The page treats a
  missing entry as closed; this app does not infer closure.
- A month listing's NotPublished never clears stored hours. A genuine official unpublish of a date more
  than seven days out is applied only when that date is single-date checked (Check Online, or opening it
  in Associate), or once it enters the rolling window.
- No conditional GET or ETag: each discovery run downloads the month files again (≤ 13 requests per day,
  or per ≥ 4 h while coverage is incomplete).
- The time zone is still assumed America/New_York wall-clock, as before. A closing time after midnight
  (e.g. 1:00 AM on Dec 31) is displayed as published.
- The feed is a CloudFront/S3 dependency separate from `sixflags.com` (unchanged).
- Windows validation not yet executed.
