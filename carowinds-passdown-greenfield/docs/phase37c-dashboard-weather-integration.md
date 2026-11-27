# Phase 37C — Dashboard Weather card and weather activation

Status: implemented locally / awaiting Windows validation (local test candidate `RC15-Phase37C-LOCALTEST`) / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `ff1f94f` (Phase 37B-3); the D4 approval was committed first as `b8676eb` (docs only) |
| Ending commits | `c13c6e1` (37C-1 Application), `39bbb5b` (37C-2 Desktop, tests, tools) and this documentation commit (hash in the completion report) |
| Reviewed source baseline | `39bbb5b` (tests, clean build, captures and the Windows candidate all ran on this source) |
| Dirty-tree caveats | Clean at start apart from the two approved D4 documentation edits, which were committed separately as `b8676eb` |
| Authorization | Owner brief "Phase 37C — Implement the Approved Dashboard Weather Card" (2026-11-27), after the D4 mock-up approval; PowerShell tool download for packaging approved in the session |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` `0.1.0-rc.14`; RC.15 not prepared |
| Database schema / sync formats | Schema 18; sync envelope 1; draft payload 4; finalized snapshot 2 — unchanged. Weather cache file format 1 (local, from 37B) — unchanged |

## Scope

The Dashboard Weather card from the approved D4 mock-up, and activation of the Phase 37B NWS backend for the running
app. Not in scope and not started: Phase 37D (Calendar selected-date weather), Passdown/PDF weather, any second
provider. No new dependency, project, schema, sync, draft, History, PDF, pricing, Pass Perks or official-retrieval
change.

## Approved design reference

- Mock-up: `artifacts/design-review/Phase37C-WEATHER-MOCKUP/` (git-ignored). Fingerprints re-verified before
  implementation, matching the [37A D4 sign-off](phase37a-weather-source-architecture-plan.md#later-sign-off-d4-mock-up-approved-2026-11-27):
  HTML `0d212fec…55bc`, README `7081a816…5e77`, renders list `02e342a8…66f8` (13 PNGs). The originals are untouched.
- Tracked copy: four unchanged renders with SHA-256 in [design/phase37c-weather-mockup/](design/phase37c-weather-mockup/README.md)
  (light normal, dark normal, two alerts expanded, stale; about 0.9 MB).

## Implementation

### Activation (one monitor, no second weather service)

| Piece | Where | Role |
|---|---|---|
| `WeatherMonitor` / `IWeatherMonitor` | `Application/Weather/WeatherMonitor.cs` | The interactive app's one weather activity over the 37B `WeatherRefreshCoordinator`: `Start()` (once; returns at once), `StopAsync()`, `RefreshNowAsync()`, `Current`, `IsRefreshing`, `Changed` |
| `WeatherRefreshMode.Manual` + `WeatherEndpointPolicy.EarliestAt` | `WeatherRefreshCoordinator`, `WeatherRefreshPolicy` | Refresh Weather asks for endpoints past their **minimum interval and backoff** (not cache expiry); same single flight as scheduled refreshes |
| `WeatherSnapshot.AlertCoverage` | `WeatherSnapshot.cs` | Which reference point (NC/SC) returned each active alert (or a message it replaced) |
| Composition | `App.axaml.cs` | Singletons: `CarowindsWeatherLocation.Park`, `FileWeatherCacheStore`, `NwsWeatherProvider` (own `HttpClient`, versioned User-Agent), coordinator, monitor, `AvaloniaWeatherLinkLauncher`, `DashboardWeatherViewModel`. `weatherMonitor.Start()` after the window and the other background services; `StopAsync()` inside `AttachShutdownFlush`'s existing 2 s bound |

Monitor loop (thread pool): read the cache and publish it → wait 10 s (`StartupDelay`) → repeat: if anything is due,
refresh (scheduled mode) → publish → sleep until the next due endpoint, clamped to 15 s … 1 min. Exceptions other
than shutdown are logged and retried after a minute; they never reach the UI thread. Resolving the services starts
nothing (tested), and the headless `--scheduled-refresh` host never starts the monitor, so no weather request is
made while the app is closed.

### NWS requests and caching

Unchanged from [37B §3–4](phase37b-weather-retrieval-and-cache.md): two points, conditional requests, cache headers,
floors (alerts 60 s, observation 10 min, forecasts 30 min, point/stations 24 h), backoff 30 s → 2 → 10 → 30 min
(longer `Retry-After` wins), one save per refresh that made requests. About 34 requests per hour while open, none
while closed. Refresh Weather can add at most the requests whose floor has passed (in practice the two alert checks
once a minute). Check Online, the official transport, `OfficialSourceLinkPolicy` and the refresh orchestrator are
untouched.

### Dashboard card

| Piece | Role |
|---|---|
| `Presentation/DashboardWeatherPresentation.cs` | Builds every text from the snapshot, today's park status and operating date, and the time. Pure; cannot request anything |
| `ViewModels/DashboardWeatherViewModel.cs` | One instance (`OperationsDashboardViewModel.Weather`); subscribes once to `IWeatherMonitor.Changed`, marshals through `IUiDispatcher` with **one coalesced rebuild**; expansion in memory; `RefreshWeatherCommand` (disabled while running); `OpenLinkCommand` through `IWeatherLinkLauncher` |
| `Services/IWeatherLinkLauncher.cs` | Opens only addresses that pass `WeatherSourceLinkPolicy` (the four NWS hosts) |
| `Views/DashboardView.axaml` | The card, first in `DashboardSecondaryColumn`; existing `dashCard`, `cardRule teal`, `cardTitleBand`, shared disclosure, `secondaryAction`, `linkAction`; a few screen-local text styles at the mock-up's sizes; token brushes only |

Rules applied:

- **Observed vs forecast:** separate sections and records. With no usable observation the card says "Current
  conditions unavailable" and never shows a forecast temperature in its place.
- **Station identity:** "OBSERVED · <station>" uses a short name keyed by the station that actually supplied the
  observation (KCLT → Charlotte airport, KUZA → Rock Hill airport, KAKH → Gastonia airport, KEQY → Monroe airport),
  otherwise the NWS station name. The line below gives "<ID>, <km> km from the park · <time> ET", and the details note
  says it is not measured inside Carowinds.
- **Forecast:** today's high (daytime period) and low (following night) by Eastern date; after the forecast starts
  at "Tonight", "Today's high not in current forecast" and nothing substituted.
- **Rain chance (D4):** the highest hourly probability for today's park hours (`Effective` opening/closing from the
  Dashboard snapshot, closing after midnight handled), from now on once open; otherwise the rest of today, labelled
  "(park closed)", "(no park hours)" or "(after park hours)". Park hours are used only when the Dashboard's operating
  date is today's Eastern date. No hourly data → "Rain chance not available", never 0%.
- **Alerts:** only active ones (`WeatherAlertRules.IsActiveAt` re-checked at display time), most severe first. The strip
  shows the count, the first event with "until <time>", and "Also: …" or its portion. Details list every alert with
  severity, end, "Applies to NC and SC portions / NC portion only / SC portion only", issuing office and time, and a
  weather.gov link to the **NWS point forecast page for the portion it applies to** (the human page; the alert's
  `api.weather.gov` link is JSON).
- **Unknown alert status:** whenever alerts are not freshly checked (stale after 15 min, a point never checked), the
  card says "Alert status unknown — alerts last checked <time>" and never "No active NWS alerts".
- **States:** loading (no cache yet), fresh, stale ("May be out of date", reason strip, ages, stale colour), unavailable
  (no values, the reason), evening. Status is always in words; colour and the one warning icon only reinforce it.
- **Refresh feedback:** started ("Checking the National Weather Service…"), already refreshing, recently checked
  (with the next automatic check), completed ("Weather updated <time>"), partly updated, network unavailable, NWS
  error, stopped. Failure is never reported as success.
- **Safety note (37A §14):** informational only, not a warning system, alerts about every 5 minutes while open, park
  procedures and official warning channels govern operations.

## Responsive layout and comparison with the approved mock-up

Real Avalonia renders: `tools/visual-fixtures` scenarios `Dashboard37C-{Normal, Alerts, Stale, Unavailable, Loading,
Evening}` at 1280×860, 960×640 and 1920×1080, and `Dashboard37C-Expanded-{Normal, Alerts, Stale}` at 1280×1400 and
960×1200, each Light and Dark. 48 captures in 9 processes (at most 6 per process, peak resident memory 376–444 MB;
limits 48 / 3 GB). Saved to the git-ignored `artifacts/design-review/Phase37C-WEATHER-IMPLEMENTATION/`.

| Aspect | Result |
|---|---|
| Position | First card in the right-hand column, same left edge and width as Daily Ticket Pricing; gate card and left column unmoved (real-window test at 1920, 1280, 960 and 700 px) |
| Height (1280×860, from page scroll vs the RC.14 baseline of 243 px) | Normal ≈ 217 px (mock 231); alerts ≈ 274 (mock 292); stale ≈ 269 (mock 278); unavailable ≈ 232 (mock 236); loading ≈ 110 (mock 112) |
| Page scroll at 1280×860 | 243 → 476 px normal, 533 with two alerts (mock predicted ≈ 480 / ≈ 540) |
| Typography, teal rule, alert strip, observation and forecast labels, details, Light/Dark | Match the mock-up |
| Medium (960×640, columns 1.4 : 1) | Text wraps within the card; nothing clipped, no horizontal scroll |
| Narrow (stacked) | After Shows and before Pricing (real-window test at 700 px). The app's minimum window (960 px with the sidebar) still gives the medium layout, so the stacked layout needs a narrower content area than the app allows |

Deviations (none changes content or order):

1. Evening: "Today's high not in current forecast" sits on its own line above the low (the mock-up showed it after
   the low). The WrapPanel puts it there at 330 px.
2. The footer "No active NWS alerts · checked <time> ET" wraps to two lines at 1280×860, as it did in the mock-up's
   normal render.
3. Alert links open the NWS point forecast page rather than an alert-specific page (above).
4. The mock-up's "SAMPLE" tag and dashed "PROPOSED" outline were review annotations and are not in the app.

## Performance (macOS development host; not Windows evidence)

Apple M2 Pro, macOS 27.2.1, .NET 10.0.11, `tools/perf-benchmarks weather` (synthetic cache: 14 periods, 156 hours,
2 observations, 8 alert messages):

| Measure | Result |
|---|---|
| Cache file (53,360 bytes) load | median 0.55 ms, max 11.0 ms; thread pool at monitor start, never the UI thread |
| Snapshot build | 10.3 µs, 9.1 KB per call |
| Card presentation build | 28.9 µs, 18.2 KB per call (about once a minute and after each refresh; 0 gen-2 collections in 2,000 calls) |
| Dashboard realize, 1280×860 headless | median 79.9 ms with cached weather vs 76.3 ms without the card (+3.6 ms, 10 runs each after warm-up) |
| Startup | Monitor `Start()` only schedules work (`Task.Run`); first request 10 s later. `RealWindowStartupTimingTests` pass. Windows startup was not profiled |
| Subscriptions and instances | One monitor, one coordinator, one card view model, one `Changed` subscription (tested); navigation creates nothing and requests nothing (tested); at most one pending UI rebuild (tested) |

Passdown editing, autosave, PDF preview, printing and Calendar navigation share no code path with weather.

## Tests

| Project / class | New | Covers |
|---|---|---|
| Application `WeatherMonitorTests` | 23 | Manual floors and backoff (alerts 60 s, forecasts 30 min, observation report age), scheduled vs manual; `EarliestAt`; NC/SC coverage including Update replacement and expired alerts; outcome classification (never success for a failure); monitor publishes the cache before any request and after the startup delay; loop wait clamps; not started → not permitted; already refreshing; recently checked with the next time; network failure keeps cached values; stop cancels an in-flight request and saves nothing; idempotent start |
| Ui `DashboardWeatherPresentationTests` | 24 | Observed vs forecast labelling; station-specific labels (KCLT, KUZA, unlisted); unavailable observation never replaced by forecast; wind; park-hours rain window, closed day, other operating date, after closing, missing hourly; evening missing high; alert strip and details with portions; safe NWS links; single alert; expired alert; stale, alerts stale, alerts unavailable (never "no active alerts"); unavailable; provider error wording; loading; no request possible from the builder; safety text |
| Ui `DashboardWeatherViewModelTests` | 18 | Dashboard load/reload never refreshes weather and uses park hours; no card without a monitor; coalesced rebuilds; freshness ageing without requests; expansion is local; refresh once, no double run, failure and exception wording, all eight outcome messages; links through the weather launcher |
| Ui `Phase37CWeatherCompositionTests` | 4 | One monitor/coordinator/card VM; verified park points (never device location); resolving starts nothing; refresh before start refused |
| Ui `Phase15ERealWindowLayoutTests` (partial, run alone) | 14 | Position, size, no overlap or clipping at 1920/1280/960/700 px Light and Dark; details with both alerts and Refresh Weather; stale, unavailable, loading and evening words rendered; Tab reaches "Weather details" between View Shows and View Pricing and Space expands; resize and theme changes make no request |
| Infrastructure `WeatherActivationGuardTests` (updated deliberately) | +4 (14) | Desktop mentions weather only in the composition root and the card's five files; card code never uses the official link launcher, link policy, refresh coordinator or Check Online; Application Dashboard/Calendar and Desktop ScheduledRefresh carry no weather (in addition to the 37B sync/draft/History/PDF/SQLite folders) |
| Existing tests updated | — | `Phase35EDashboardTests.Markup_FollowsTheApprovedCardStructure…`: 6 → 7 card rules, now also asserting the Weather card is first in the right column with the teal rule. `DashboardViewTests.DashboardView_CheckOnlineBusy…`: finds the page's reload bar by its accessible name (the card added two progress bars); same assertion. No assertion removed or weakened |

Live NWS tests remain opt-in (`LiveSmoke`); none was run in this phase.

## Validation

All results are from the macOS development host. None of it is Windows validation.

| Check | Result |
|---|---|
| Deterministic partitions (`build-and-test` skill) | PASS — 6,184 / 6,184, 0 skipped (6,097 + 87 new): Domain 488, Application 1,469, Infrastructure 1,423, Documents 484, Ui mixed 2,060, run-alone 30 + 158 + 2 + 7 + 4 + 59 (UI partitions with `--blame-hang`, no hang). The first full run found 4 failures in existing markup/view tests caused by the new card; fixed (markup corrected, two expectations updated as above) and both affected UI partitions rerun green |
| Repository guards | PASS — 31 / 31 (unchanged; real-window tests are a partial of a run-alone class) |
| Clean Release build | PASS — all `bin`/`obj` under `src`, `tests`, `tools` deleted, `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors. `tools/visual-fixtures` and `tools/perf-benchmarks`: 0 warnings, 0 errors |
| Windows local-test candidate | `artifacts/local-test/RC15-Phase37C-LOCALTEST/LOCAL-ONLY-RC15-Phase37C-LOCALTEST-win-x64.zip` from `39bbb5b`, shown as `0.1.0-rc.15-LOCALTEST.phase37c`; 76,418,184 bytes, 308 files; SHA-256 `b03f76376068c58f3e3558621900a1c6b841e6bb83e0296dd6df978f1b284e43`. `Build-PortableRelease.ps1 -LocalTestLabel RC15-Phase37C-LOCALTEST -LocalTestVersion 0.1.0-rc.15-LOCALTEST.phase37c` (PowerShell 7.6.10 .NET tool in the session scratch folder); script content validation (folder and ZIP) passed; `RealReleaseZip` passed (a bogus path fails); marker names `39bbb5b`; no database, WAL/SHM, draft, weather cache, key, signature or update manifest in the ZIP; all 12,328 earlier artifact files re-hashed byte-identical |
| Windows validation | NOT EXECUTED — checklist [rc15-phase37c-localtest-windows-checklist.md](releases/rc15-phase37c-localtest-windows-checklist.md) |

## Compatibility boundaries

Unchanged: schema 18 and migrations, sync envelope 1, draft payload 4, finalized snapshot 2, Personal Sync,
finalized History, stored PDFs, PDF templates and generation, official retrieval and Check Online, pricing, Pass
Perks, official park data rules, the frozen RC.14 artifacts, the version and the dependency graph
(`ProjectDependencyGraphTests`). New runtime behaviour: NWS requests while the app is open, and `weather-cache.json`
written in the local application-data folder (never synced or packaged). Rollback: revert `c13c6e1` and `39bbb5b`;
the cache file can be deleted safely.

## Documentation maintenance

`AGENT_BUILD_SPEC.md` §6.6: status now says the Dashboard summary is implemented locally and awaits Windows
validation and release approval (still "not yet released" per §4.4); manual refresh respects minimum intervals and
backoff and reports failures as failures; the D4-approved rain-chance period rule. §8: the weather summary's status.

**Product specification:** Updated

`docs/architecture.md` (weather summary), `architecture/retrieval-and-refresh.md` (activation and manual mode),
`architecture/data-and-persistence.md` (the app now writes the cache), `architecture/desktop-ui.md` (the card and its
ownership), `architecture/application-lifecycle.md` (start/stop), `architecture/testing-and-performance.md` (fixtures,
partial, tools); `docs/README.md` index. The 37A and 37B records are unchanged (historical evidence).

**Architecture documentation:** Updated

## Known limitations and follow-ups

- **Windows validation pending** for everything above, including DPI, Narrator, real network failure, real alerts
  and startup timing.
- No NWS alert was in effect during development, so real alert rendering is covered only by synthetic tests and
  captures; checklist item 6.2 runs only if an alert occurs.
- Station short names cover the four nearest stations; any other station shows its full NWS name, which may wrap.
- The stacked layout cannot occur at the app's minimum window size (it is tested with the view alone at 700 px).
- Weather is not shown in the Calendar (Phase 37D, not authorized) or anywhere in Passdown, History or PDFs.
