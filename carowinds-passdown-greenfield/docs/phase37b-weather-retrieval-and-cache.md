# Phase 37B — Weather retrieval, parsing, local cache and deterministic tests (not activated)

Status: implemented locally / not activated in the application / no Windows candidate (no runtime behavior change) / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `2bcdd57` (Phase 37A specification amendment) |
| Ending commits | `ad84590` (37B-1 source, tests, fixtures) and this documentation commit (hash in the completion report) |
| Reviewed source baseline | `ad84590` (the tests and build above ran on this source, before the documentation commit) |
| Dirty-tree caveats | Clean tree at start |
| Authorization | Owner brief "Phase 37 — Finalize Weather Decisions and Begin 37B" (2026-11-27): decisions D3–D8 and the 37B scope only |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` `0.1.0-rc.14`; RC.15 not prepared |
| Database schema / sync formats | Schema 18; sync envelope 1; draft payload 4; finalized snapshot 2 — unchanged. New, separate: weather cache file format 1 (local only) |

## Scope

NWS retrieval, parsing, bounded local caching and deterministic tests, with the feature **not activated**: nothing in
`src/Carowinds.Passdown.Desktop` composes, registers or calls weather code, so the application makes no weather
request and writes no weather file. No UI, no Dashboard or Calendar change, no dependency, no project, no schema,
sync, PDF, pricing, Pass Perks or version change. Phase 37C was not started.

## Decisions applied

| ID | Applied as |
|---|---|
| D3 | `NwsHttpTransport.BuildUserAgent`: `CarowindsOperations/<version> (+https://github.com/KASHO7/Passdown)`; any `+build` suffix is dropped; no email or private contact |
| D4 | Nothing implemented; the Dashboard card needs an approved mock-up first |
| D5 | `WeatherRefreshPolicy.Alerts`: target 5 min, floor 60 s, with cache headers, backoff and `Retry-After` |
| D6, D7 | Nothing implemented (UI decisions, recorded in spec §6.6) |
| D8 | Two candidate points in `CarowindsWeatherLocation`, marked `PendingVerification`; the coordinator makes no request until they are verified (§1) |

## 1. Reference points (D8) — PENDING VERIFICATION

| Point | Coordinate | U.S. Census geocoder (state / county) | NWS `/points` | NWS `/alerts/active?point=` | Inside OSM park outline |
|---|---|---|---|---|---|
| `carowinds-nc` | 35.1050, −80.9420 | North Carolina / Mecklenburg | GSP 116,59; NCZ071; NCC119 | 200, 0 alerts | yes, 135 m from its edge |
| `carowinds-sc` | 35.1002, −80.9420 | South Carolina / York | GSP 116,59; SCZ009; SCC091 | 200, 0 alerts | yes, 57 m from its edge |

Evidence (2026-11-27, read-only):

- **State and county:** U.S. Census Bureau geocoder (`geocoding.geo.census.gov`, `Public_AR_Current`, layers
  States and Counties), authoritative for the state line. Points were chosen at least about 140 m from the line along
  their meridian: a transect at −80.9420 changes from SC to NC between 35.1015 and 35.1030.
- **NWS applicability:** `/points` returns the matching state's zone and county for each point, and
  `/alerts/active?point=` answers for each. Confirmed again by the opt-in live test.
- **Inside the property:** only the OpenStreetMap theme-park outline (way 894382560, `tourism=theme_park`, 291
  vertices) supports this. OSM is community data, not an authoritative park boundary.

**Why D8 stays pending.** No authoritative source, such as an official park map with coordinates or county parcel
records, confirms that both points are inside Carowinds. The SC point is only 57 m inside the OSM outline. A
further finding: the published park coordinate (35.1028, −80.9417) is in North Carolina by Census boundaries but in
South Carolina zones by NWS. NWS zone boundaries are coarser near the line, so points must keep their distance from
it. To verify: the owner confirms both points on an official park map, or county parcel data (Mecklenburg/York GIS)
places both inside Carowinds parcels; then `CarowindsWeatherLocation.Candidate` changes to `Verified` in a recorded
owner decision. A test asserts it is still pending.

## 2. Architecture as implemented

| Layer | Files | Responsibility |
|---|---|---|
| Domain | `Weather/WeatherLocation.cs`, `WeatherModels.cs`, `WeatherAlertRules.cs`, `WeatherUnits.cs` | Reference points and verification status; point, forecast, hourly, station, observation and alert records; alert activity, replacement and order; unit conversion and distance |
| Application | `Weather/IWeatherProvider.cs`, `WeatherCacheDocument.cs`, `WeatherRefreshPolicy.cs`, `WeatherRefreshCoordinator.cs`, `WeatherSnapshot.cs`, `WeatherCacheRetention.cs`, `WeatherAttribution.cs`; `Links/WeatherSourceLinkPolicy.cs` | Ports, cache model, request policy, single-flight refresh, freshness states and daily summaries, retention, attribution, link allowlist |
| Infrastructure | `Weather/Nws/NwsHttpTransport.cs`, `NwsJson.cs`, `NwsParsers.cs`, `NwsWeatherProvider.cs`; `Weather/FileWeatherCacheStore.cs` | HTTP transport for `api.weather.gov`, defensive JSON reading, parsers, provider result mapping, cache file |

Dependency direction is unchanged (`ProjectDependencyGraphTests`, the Domain and Application assembly-reference
tests). The Application layer uses no `System.Net.Http`. The shared official `IRetrievalHttpClient`, the coalescing
client, `OfficialDataRefreshCoordinator` and `OfficialSourceLinkPolicy` are untouched and unused by weather.

## 3. NWS request policy (as implemented)

| Endpoint | Request | Due again | Notes |
|---|---|---|---|
| `GET /points/{lat},{lon}` (both points) | Unconditional | 24 h floor, cache expiry, 7-day ceiling | Grid change for the forecast point clears forecast, hourly, stations and observations; a 404 from a grid endpoint forces a new lookup |
| `GET {observationStations}` | Unconditional | 24 h floor, 7-day ceiling | NWS order kept (nearest first) |
| `GET {forecast}` and `GET {forecastHourly}` | `If-Modified-Since` / `If-None-Match` when cached | Cache expiry, 30 min floor, 3 h ceiling | Link must be `https://api.weather.gov` (parser and transport) |
| `GET /stations/{id}/observations/latest` | Conditional when cached | 10 min floor, and not until the held observation is 20 min old | Nearest station; the second only when the first has no fresh value. Station ids restricted to `[A-Z0-9]{3,8}` |
| `GET /alerts/active?point={lat},{lon}` (both points) | `If-None-Match` | Every 5 min, never under 60 s (D5) | Both points every time |

- **Transport:** its own `HttpClient` (`SocketsHttpHandler`, no automatic redirects, no cookies, gzip/deflate);
  `Accept: application/geo+json`; 15 s timeout (distinct from caller cancellation); at most two redirects, and only to
  `https://api.weather.gov`; bodies over 2 MB refused; any other host refused before sending.
- **Expiry:** `Cache-Control: max-age` from now, else `Expires`.
- **Failures:** no retry within a refresh. The next attempt waits 30 s, 2 min, 10 min, then 30 min for consecutive
  failures, or longer if `Retry-After` asks. 404 → NotFound, 429 → RateLimited, other non-2xx/304 → ProviderError,
  network → Network, timeout → Timeout, unparseable or a foreign link → InvalidResponse.
- **Budget:** a first refresh is 8 requests (2 points, stations, forecast, hourly, one observation, 2 alert checks).
  Steady state is about 24 alert checks, 2 forecast and 2 hourly checks, and up to 6 observation checks per hour, so
  about 34 per hour while the app is open, and none while it is closed. That is a little above the plan's estimate
  of about 30 because both points are checked for alerts.
- **Single flight:** a refresh started while one runs joins it.
- **Gate:** `RefreshDueAsync` returns `NotPermitted` with zero requests while the location is unverified.

## 4. Cache behavior

- `weather-cache.json` under `AppPaths.LocalApplicationDataDirectory`, written through `AtomicFileWriter`,
  `FormatVersion` 1, compact JSON with string enums. Written once at the end of a refresh that made requests.
- **Limits:** a file over 256 KB is ignored; a document over 256 KB is not written (the previous file stays). A
  realistic full cache (14 periods, 156 hourly periods, 3 stations, an observation, 8 alerts) round-trips inside the
  limit in tests.
- **Retention:** ended forecast and hourly periods are dropped. A forecast with nothing left also loses its
  validators, so the next request is unconditional. Alerts are dropped after `ends`/`expires`, or when they have no
  end time. Observations are kept only for the two stations in use.
- **Last-known-good:** a failure updates only the attempt time, failure kind and backoff. A 304 refreshes the check
  time and keeps the value; a 304 without a cached value counts as a failure.
- **Invalid files:** missing, corrupt, oversized or unknown-version files are read as empty. Not in SQLite, Personal
  Sync, drafts, History, PDFs or release packages.

## 5. Freshness states (`WeatherSnapshotBuilder`, local only)

| Part | Fresh | Stale | Unavailable (value hidden) | Never retrieved |
|---|---|---|---|---|
| Forecast / hourly (by last check) | ≤ 3 h | ≤ 24 h | > 24 h, or attempts failed before any value | no attempt |
| Observation (by observation time) | ≤ 75 min | ≤ 3 h | > 3 h, no temperature, or attempts failed | no attempt |
| Alerts (by the older of the two points' checks) | ≤ 15 min | > 15 min | a point never checked successfully after an attempt | no attempt |

Observation, forecast and alerts are separate parts, so a forecast value can never fill the observation. Each part
carries `LastRefreshFailed` for "could not refresh" wording. `SummarizeDays` pairs the daytime period (high) with the
following night (low) by Eastern date. Today's high is null once the forecast starts at "Tonight"; it is never
substituted. `MaxPrecipitationChance` covers a requested window of hourly periods.

## 6. Alert handling

- Only `status = Actual`; Test, Exercise, System and Draft are never active. A Cancel is never active.
- Active means `effective` (or `sent`) has passed and `ends`, or without it `expires`, has not. A message with neither
  is never active.
- Updates and Cancels remove every message listed in their `references`. The same id from both points appears once.
- Order: severity (Extreme → Unknown), then urgency (Immediate → Unknown), then onset.
- Applicability is NWS's own point query for each side of the state line; the application never adds a state-wide
  or county-wide alert. Entries without an id or event are skipped and counted.

## 7. Source attribution

`WeatherAttribution.ProviderName` = "National Weather Service". `WeatherAttribution.ForecastPage(point)` gives the
public `forecast.weather.gov/MapClick.php` page. Alerts keep their `api.weather.gov` message link.
`WeatherSourceLinkPolicy` accepts only `https` on `www.weather.gov`, `forecast.weather.gov`, `alerts.weather.gov` and
`api.weather.gov`, while `OfficialSourceLinkPolicy` still refuses NWS addresses (tested). UI wording is Phase 37C
work.

## 8. Tests

Fixtures: `fixtures/weather/nws/` (live 2026-11-27 responses, some trimmed, plus one clearly labelled synthetic alert
set; see its README). The normal suite makes no network call.

| Project | Class | Tests | Covers |
|---|---|---|---|
| Domain | `WeatherAlertRulesTests` | 14 | active/expired/not-yet-effective, `ends` vs `expires`, no end time, non-Actual statuses, Cancel, Update/Cancel references, two-point de-duplication, ordering |
| Domain | `WeatherLocationTests` | 6 | candidate **pending** and not permitted; both states; recorded coordinates; four-decimal invariant formatting; unit conversions and distance |
| Application | `WeatherRefreshCoordinatorTests` | 20 | unverified → zero requests; first refresh order and count; nothing due → no request or save; 5-min alert cadence with ETag; next-due time; conditional forecast and 304; last-known-good on failure; backoff steps and `Retry-After`; failure before any value; 304 without a value; grid change; 404 → point lookup; station fallback; observation without temperature; single flight; cancellation; unknown cache format; offline reopen; save failure |
| Application | `WeatherSnapshotAndPolicyTests` | 30 | every freshness edge; ended periods hidden; observation never from forecast; alerts need both points and expire on time; daily high/low and "Tonight" first; precipitation window; due-time rules; approved policy values; retention; link policies; attribution |
| Infrastructure | `NwsParserTests` | 24 | both point fixtures (NC/SC zones), foreign link and missing grid rejected; 14-period forecast; hourly units; temperature as quantity, unknown unit, bad periods; empty or malformed; station order and id rule; observation units, QC rejection, missing timestamp; empty and synthetic alerts, resolution at three instants |
| Infrastructure | `NwsHttpTransportTests`, `NwsWeatherProviderTests` | 26 | User-Agent (no email), Accept and validators sent, cache headers read, 304/429 and `Retry-After`, non-API hosts refused before sending, redirects only within the API host, timeout vs cancellation, body cap; request paths, expiry from max-age, status and exception mapping, unusable bodies, station-id refusal, foreign forecast link |
| Infrastructure | `FileWeatherCacheStoreTests` | 9 | realistic round-trip within 256 KB; missing, corrupt, unknown-version and oversized files; oversized document not written; default path |
| Infrastructure | `WeatherActivationGuardTests` | 10 | Desktop, Updater and Documents never mention weather; sync, draft, finalization, History, PDF output and SQLite storage folders carry no weather; weather code uses no SQLite, storage, official retrieval or sync-folder code |
| Infrastructure | `NwsWeatherProviderLiveSmokeTests` (`LiveSmoke`, opt-in) | 1 | Excluded from the deterministic suite |

## Validation

| Check | Result |
|---|---|
| Deterministic partitions (`build-and-test` skill) | PASS — 6,096 / 6,096, 0 skipped (5,957 + 139 new: Domain +20, Application +50, Infrastructure +69): Domain 487, Application 1,446, Infrastructure 1,419, Documents 484, Ui mixed 2,014, run-alone 30 + 144 + 2 + 7 + 4 + 59 (UI partitions with `--blame-hang`, no hang). Development host (macOS); not Windows evidence |
| Clean Release build | PASS — every `bin`/`obj` under `src`, `tests` and `tools` deleted, then `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors |
| Repository guards | PASS — 31 / 31 (in the Infrastructure partition, and again with this record and the documentation edits in place) |
| Opt-in live check (`NwsWeatherProviderLiveSmokeTests`, run once, 2026-11-27) | Five read-only NWS GETs: NC point GSP 116,59 NCZ071/NCC119; SC point GSP 116,59 SCZ009/SCC091; forecast 14 periods (updated 11:13:44Z); alerts for both points retrieved, 0 active, with weak ETags. The run's pass/fail summary line was not captured; all five printed outcomes were `Retrieved`, matching each assertion. Recorded separately from the deterministic evidence; nothing written to disk |
| Other live requests for D8 (2026-11-27) | 1 OpenStreetMap Nominatim search; 12 Census geocoder lookups; 4 NWS requests (2 `/points`, 2 `/alerts/active`), all read-only, with a generic research User-Agent; responses kept only in the session scratch folder |
| Windows validation | NOT EXECUTED — not required: no runtime behavior changed and nothing is activated |

## Compatibility and performance

- Schema 18, migrations, sync envelope 1, draft payload 4, finalized snapshot 2, official pricing cache, Pass Perks
  cache, PDF generation and templates, the dependency graph, packages and version: unchanged.
- No startup, navigation, autosave, preview, printing or update path changed, because nothing calls the new code.
  Performance budgets apply when 37C wires it in; they are not measured here.
- **Rollback:** revert the 37B commits. No user data is involved, since the application never wrote a weather file.

## Documentation maintenance

`AGENT_BUILD_SPEC.md` §6.6 now records the approved decisions D4–D8: the right-hand-column Dashboard card
subject to an approved mock-up, weather refresh only in the expanded details, no Calendar cell weather, five-minute
alert checks while open, and two verified in-park points before retrieval is enabled. D3 is technical and recorded
here and in the 37A decision register.

**Product specification:** Updated

`docs/architecture.md`, `docs/architecture/retrieval-and-refresh.md`, `docs/architecture/data-and-persistence.md` and
`docs/architecture/testing-and-performance.md` describe the weather code, the cache file and the tests as
implemented but not activated. The documentation index lists this record.

**Architecture documentation:** Updated

## Known limitations and follow-ups

- **D8 is unresolved:** the in-park position of both points needs authoritative confirmation (§1). Until then
  retrieval cannot be enabled, by design.
- `304` handling is covered by deterministic tests. Live NWS returned `ETag` and `Last-Modified`, but no live `304` was
  requested.
- The rate-limit status code is undocumented by NWS; 429 and 5xx with `Retry-After` are handled.
- The refresh is not scheduled or composed. Phase 37C must add the composition-root registration, the background
  timer that calls `GetNextDueAtAsync` / `RefreshDueAsync` off the UI thread, presentation and the request-count and
  performance measurements, and must update `WeatherActivationGuardTests` deliberately.
- About 34 requests per hour while open (§3), slightly above the 37A estimate; within NWS guidance.
