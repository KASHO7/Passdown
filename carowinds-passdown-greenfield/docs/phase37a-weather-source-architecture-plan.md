# Phase 37A — Carowinds Weather Integration: Source & Architecture Plan

Status: planning only (documentation) / no production weather functionality implemented / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `c7c79aa` (Phase 36C-4 record) |
| Ending commits | This record's documentation commit (hash in the completion report) |
| Reviewed source baseline | `c7c79aa`; production source equals `18fac73` (Phase 36C-3), the Phase 36C local test candidate's source |
| Dirty-tree caveats | Clean tree at start |
| Authorization | Owner brief "Carowinds Operations — Weather Integration, Milestone A: NWS Research, Architecture & Implementation Plan" (2026-11-27). Research and architecture only |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published 2026-11-27T00:56Z and frozen; source `<Version>` `0.1.0-rc.14`; RC.15 not prepared |
| Database schema / sync formats | Schema 18 (`Migration0018CreateOfficialReferenceDocumentsTable`); sync envelope 1; draft payload 4; finalized snapshot 2 — unchanged and not to be changed by this plan |

## 1. Verified repository baseline

| Item | Verified value | How |
|---|---|---|
| HEAD | `c7c79aa` Phase 36C-4 (docs only), after `18fac73` / `15a31d2` / `07ae59b` (36C-1–3), `5384663` (36B), `da65b53` (36A) | `git log` |
| Working tree | Clean | `git status` |
| Source version | `0.1.0-rc.14`, `FileVersion` `0.1.0.14` | `Directory.Build.props` |
| Latest tag | `v0.1.0-rc.14` | `git tag` |
| Deterministic tests | 5,957 / 5,957 passed at `c7c79aa` (§17) | `build-and-test` skill partitions |
| Repository guards | 31 tests in `tests/Carowinds.Passdown.Infrastructure.Tests/RepositoryGuards` | §17 |
| Phase 36C Windows candidate | **Owner-reported** successful Windows test of `RC15-Phase36C-LOCALTEST` (owner statement in the 2026-11-27 brief; Claude ran no Windows test). The [Phase 36C record](phase36c-ert-editor-maintainability.md) is historical and is not edited; this line is the later sign-off note | Owner statement |
| RC.13 → RC.14 in-app update | **Owner-reported** successful genuine update on Windows; recorded as a later sign-off in [RC.14 readiness §15](releases/rc14-release-readiness.md) | Owner statement |

## 2. Phase-number decision

- Phase 36 is the architecture and maintenance initiative (36A audit, 36B tests, 36C ERT editor; 36D cancelled, 36E
  deferred, 36F combined into each close-out — [36A §15](phase36a-current-architecture-technical-debt-audit.md)).
- No repository document, rule, skill, record or release note assigns Phase 37. A search of `docs/`, `CLAUDE.md`,
  `AGENT_BUILD_SPEC.md` and `.claude/` finds no "Phase 37" and no personal-task feature. The earlier mention of
  Phase 37 for personal tasks was never recorded in the repository, and task tracking is not authorized.
- **Decision (provisional):** the Weather initiative uses **Phase 37**, with milestones **37A** (this plan, the
  brief's "Milestone A") through **37E**. It is provisional until the owner confirms it. If the owner wants Phase 37
  kept for personal tasks, the Weather initiative becomes Phase 38 and this one file is renamed before any
  implementation starts; no other record depends on the number.

## 3. Carowinds location and source evidence

| Item | Value | Source and precision |
|---|---|---|
| Official address | 14523 Carowinds Blvd, Charlotte, NC 28273 | Official Six Flags Carowinds directions page `https://www.sixflags.com/carowinds/directions` (read 2026-11-27; `carowinds.com` redirects 301 to `sixflags.com/carowinds`) |
| Published coordinate | 35°06′10″N 80°56′30″W = **35.10278, −80.94167** | Wikipedia "Carowinds" infobox (GeoHack type `landmark`); whole-second DMS, about ±30 m. Not an official park coordinate |
| Official map pin (cross-check only) | 35.1024236, −80.9412691 | Destination embedded in the official directions page's Google Maps link (a map pin, not stated text); 50 m from the Wikipedia point |
| Park extent | About 407 acres (1.65 km²), straddling the NC–SC line; "the state line ... runs through the main entrance area" | Wikipedia lead and infobox |
| Proposed fixed reference | **35.1028, −80.9417** (the published coordinate at 4 decimal places) | Precision about ±10 m in the number, but it represents a 1–1.5 km park, so treat it as a park-level point, not a building |
| NWS grid | Forecast office **GSP** (Greenville-Spartanburg), grid **116,59** | Live `/points` (§4), for both points below |
| Zones at 35.1028, −80.9417 | Forecast zone **SCZ009**, county **SCC091** (York County, SC) | Live `/points` |
| Zones at 35.1037, −80.9426 (130 m north-west) | Forecast zone **NCZ071**, county **NCC119** (Mecklenburg County, NC) | Live `/points` |
| Timezone | `America/New_York` (NWS `timeZone` agrees); operating-date presentation uses the existing `EasternTimeZone` | Live `/points`; `src/Carowinds.Passdown.Application/Time/EasternTimeZone.cs` |

**Finding.** The published park coordinate falls in South Carolina zones, and a point 130 m away falls in North
Carolina zones. Both resolve to the same 2.5 km forecast grid cell, so **one point represents the park well for
forecasts**. It is **not enough for alerts**: NWS issues zone- and county-based products (heat, wind, freeze,
winter) separately for Mecklenburg and York. Alerts therefore use **two fixed reference points, one on each side of
the line** (§10). The NC point above was chosen only as an offset to find the NC zones. Before Weather B, confirm
two points that are inside the park on each side of the line, against an official park map. Never use the computer's
location, GPS or a location permission.

## 4. Current NWS API endpoints (verified 2026-11-27)

Documentation read: [NWS API](https://www.weather.gov/documentation/services-web-api) (page "Updated 3/24/2026") and
[NWS alerts](https://www.weather.gov/documentation/services-web-alerts). Base `https://api.weather.gov`.

| Purpose | Endpoint | Observed response (2026-11-27 ~13:50 UTC) |
|---|---|---|
| Point → grid, zones, links | `GET /points/{lat},{lon}` | 200 `application/geo+json`; `Cache-Control: public, max-age=86400`; `gridId` GSP, `gridX` 116, `gridY` 59; `forecast`, `forecastHourly`, `forecastGridData`, `observationStations`, `forecastZone`, `county`, `timeZone`, `radarStation` KGSP |
| 12-hour forecast | `GET /gridpoints/GSP/116,59/forecast` | 200; `max-age=3600`, `Last-Modified` = forecast `updateTime` (11:13:44Z); 14 periods ("Today" … "Thursday Night"), `validTimes` `…/P7DT20H`, `units` `us` |
| Hourly forecast | `GET /gridpoints/GSP/116,59/forecast/hourly` | 200; `max-age=3600`; 156 periods (2026-11-27 08:00 to 2026-12-03 20:00 EST) |
| Raw grid data | `GET /gridpoints/GSP/116,59` | Not fetched. Carries apparent temperature, heat index, wind chill and wind gust series (documented); deferred |
| Nearby stations | `GET /gridpoints/GSP/116,59/stations` | 200; `max-age=86400`; sorted by distance; first: KCLT, KUZA, KAKH, KEQY |
| Latest observation | `GET /stations/{id}/observations/latest` | 200; KCLT `max-age=114`, KUZA `max-age=300`; SI units with per-value `qualityControl` |
| Active alerts | `GET /alerts/active?point={lat},{lon}` | 200; `max-age=5`, weak `ETag`; 0 alerts at the time |

Other documented options: `/alerts/active?zone=…` and `?area=…`, `/alerts` (past seven days), `Feature-Flags`
header (for example `forecast_temperature_qv`, `forecast_wind_speed_qv`, `obs_station_provider`), formats GeoJSON
(default), JSON-LD, DWML (forecast only), CAP and ATOM for alerts.

## 5. API usage requirements

| Topic | Documented requirement or observed behavior | Plan |
|---|---|---|
| Identification | "A User Agent is required to identify your application." Contact (website or email) is optional; the API "will be replaced with an API key in the future" | Production header `CarowindsOperations/<version> (+https://github.com/KASHO7/Passdown)`: the public repository as contact, **no email**. Owner decision D3. The existing shared `HttpClient` sends no User-Agent, so weather cannot use it unchanged |
| Accept | GeoJSON default | Send `Accept: application/geo+json` |
| Caching | Content expires "based upon the information life cycle"; points may be cached but rechecked periodically | Honor `Cache-Control: max-age` / `Expires` per endpoint, within the floors and ceilings in §8 |
| Conditional requests | Not documented; `Last-Modified` (forecast, observations) and `ETag` (alerts) observed | Send `If-Modified-Since` / `If-None-Match`, accept `304`, and fall back to a full `200`. Confirm `304` support with fixtures and one opt-in live check in Weather B |
| Rate limit | Not public; excess requests "may be retried after the limit clears (typically within 5 seconds)"; alerts: no more than "every 30 seconds"; abuse can lead to a ban | Never poll alerts faster than 60 s (default 5 min, §8); on 429/503 honor `Retry-After`, else back off 30 s → 2 min → 10 min → 30 min; at most one automatic retry per cycle |
| Errors | Status code for rate limiting and the error body format are not documented | Treat any non-2xx/304 as a provider error; read `application/problem+json` `title`/`detail` defensively and never show raw text |
| Timestamps | "All times generated by the API are in ISO-8601 format" (offsets observed: `-05:00` and `+00:00`) | Parse as `DateTimeOffset`; present in Eastern time through `EasternTimeZone` |
| Units | Forecast in US units (`°F`, mph strings like `16 to 21 mph`); hourly dewpoint in °C; observations SI (`degC`, `km_h-1`, `Pa`, `m`) | Convert in the parser with explicit unit codes; reject unknown unit codes rather than guess |
| Terms | Open data, "free to use for any purpose" | Attribute "National Weather Service" with the source link on every weather surface |
| Not a warning system | "NWS CAP must not be used to activate the Emergency Alert System"; Tornado Watches come from the WFO `WCN` product, not SPC's SEL | The app states that alerts are informational and checked periodically (§14) |

## 6. Capability matrix

| Field | Classification | Source and notes |
|---|---|---|
| Current temperature | Supported with conditions | Latest station observation (nearest fresh station, KCLT 11.9 km or KUZA 16.8 km); not measured at the park; whole °C from routine METARs; null until QC completes (up to 20 min) |
| Feels-like temperature | Supported with conditions | Observation `heatIndex` / `windChill`, supplied by NWS when applicable; null otherwise. Never computed by the app from incomplete inputs |
| Humidity | Supported with conditions | Observation `relativeHumidity`; hourly forecast `relativeHumidity` (forecast) |
| Wind speed | Reliably supported (forecast) / supported with conditions (observed) | Forecast text (`16 to 21 mph`); observation `windSpeed` km/h |
| Wind gusts | Supported with conditions | Observation `windGust` (may be null; gust-null bug fixed 2025-10-02); forecast gusts only in raw grid data (deferred) |
| Wind direction | Reliably supported | Forecast compass text; observation degrees |
| Current conditions | Supported with conditions | Observation `textDescription` ("Cloudy"); may be empty |
| Hourly temperature | Reliably supported | Hourly forecast, 156 h |
| Hourly precipitation probability | Reliably supported | Hourly `probabilityOfPrecipitation` (percent; may be null) |
| Hourly conditions | Reliably supported | Hourly `shortForecast` |
| Daily high / low | Derived from available data | Daytime period temperature = high; following night period = low. After about 6 PM the first period is "Tonight", so today's high is no longer in the forecast: show "not in the current forecast", do not substitute an hourly maximum unlabelled |
| Daily forecast summary | Reliably supported | 12-hour `shortForecast` / `detailedForecast` |
| Official alerts | Reliably supported (on retrieval) | `/alerts/active?point=` for both reference points; periodic checks, not continuous |
| Forecast update time | Reliably supported | `updateTime` (also `Last-Modified`); `generatedAt` is response time, not issuance |
| Observation time | Reliably supported | Observation `timestamp` |
| Provider/source links | Reliably supported | `https://forecast.weather.gov/MapClick.php?lat=…&lon=…` for people; alert `@id` / `web` links. Link policy in §10 |
| Historical weather | Unsupported (by design) | Observations kept 7 days only; excluded from the feature |
| Lightning / real-time radar | Requires another source | Radar endpoints give station status only; lightning is not offered. Out of scope |

**Observed versus forecast.** Every displayed value carries its kind: *Observed* (station name, distance and
observation time), *Forecast* (NWS forecast, issued time) or *Alert*. The models keep them in different types, so a
forecast temperature cannot be bound where an observation is expected. When no fresh observation exists, the
Dashboard shows "Current conditions unavailable", never the forecast temperature as "now".

## 7. Accuracy limitations

- NWS forecasts are official, forecaster-reviewed grids (2.5 km cells). That does not make them the most accurate
  forecast in every situation; summer convection near Charlotte is hard to place in time and space for any provider.
- Observations come from airport stations 12–17 km away. Pop-up storms, wind and heat at the park can differ.
- Probability of precipitation is the chance of measurable rain at a point in the period, not duration or coverage.
- **Future option, separate from this feature:** backtesting NWS forecasts against KCLT observations, or comparing
  another provider, could quantify accuracy. It needs its own approval, storage design and source review. No second
  provider is added in Phase 37.

## 8. Refresh and caching policy

The weather refresh is **its own app-lifetime coordinator**, separate from `OfficialDataRefreshCoordinator` and the
scheduled 6 AM task. It never runs because of date selection, Calendar month navigation, tab changes or opening a
detail view.

| Data | Refresh when | Floor / ceiling | Requests |
|---|---|---|---|
| Point → grid and zones (two points) | Cache older than `max-age` (24 h observed), or a gridpoint URL returns 404/301 | ≥ 24 h / ≤ 7 days | ≤ 2 per day |
| Stations list | With the point refresh | ≥ 24 h | ≤ 1 per day |
| 12-hour forecast | `Expires` passed, conditional on `Last-Modified` | ≥ 30 min / ≤ 3 h | ≤ 2 per hour |
| Hourly forecast | Same as forecast | ≥ 30 min / ≤ 3 h | ≤ 2 per hour |
| Latest observation | `Expires` passed and the current observation is older than 20 min | ≥ 10 min | ≤ 6 per hour (one station; fallback station only when the first is stale or missing) |
| Active alerts (two points) | Every 5 min while the app runs (owner decision D5), conditional on `ETag` | ≥ 60 s | ≤ 24 per hour |

- **Startup:** after the window opens, on a background task, only for items whose cache has expired. Startup never
  waits on weather.
- **Total budget:** about 30 requests per hour while the app is open; zero while it is closed. No headless or
  scheduled weather retrieval.
- **Single flight:** one refresh at a time; a request for a refresh during one joins it. A grid change invalidates
  the forecast cache before the next forecast request.
- **Manual refresh:** a small "Refresh weather" action in the weather detail only (owner decision D6). Check Online
  keeps its official-data meaning and counts.

**Local cache.** One JSON file, `weather-cache.json`, under `AppPaths.LocalApplicationDataDirectory`, written with
the existing `AtomicFileWriter` (the flat-file convention of `FileCalendarDisplayPreferenceStore`). It has its own
`cacheFormatVersion`. **No SQLite table, no migration, not in Personal Sync, not in the sync folder, not in release
packages, not in database backups.** It stores normalized values, not raw responses.

| Rule | Value |
|---|---|
| Size limit | Normalized content ≤ 256 KB; a larger or unreadable file is ignored and replaced on the next successful retrieval |
| Forecast retention | Periods that have ended are dropped; the forecast is not shown at all if retrieved more than 24 h ago |
| Observation retention | Latest per station only |
| Alert retention | Removed after `ends` (or `expires` when `ends` is absent) or a Cancel; the last successful check time is kept |
| Invalidation | Unknown `cacheFormatVersion`, grid change, corrupt JSON → treated as never retrieved, never as an error dialog |
| Disposable | Deleting the file loses nothing that cannot be fetched again |

## 9. Failure and stale-data states

| State | Meaning | Presentation |
|---|---|---|
| Never retrieved | No cache, no successful retrieval | "Weather not yet retrieved" |
| Fresh | Within the refresh policy | Values plus "NWS · updated 9:14 AM" |
| Previously retrieved | Last check failed, cached data still inside its validity | Values plus "Last updated 8:10 AM · could not refresh" |
| Stale | Forecast older than 3 h, or observation older than 75 min | Muted values with "Older forecast/observation" (existing amber stale style, never red) |
| Unavailable | Too old to show (forecast > 24 h, observation > 3 h), or the field is null | "Not available" for that field only |
| Network failure / provider error | Request failed, timed out, 429 or 5xx | Keep last-known-good; message names the failure kind, no raw error text |
| Partial | One endpoint succeeded, another failed | Each part has its own state and time |
| Alerts check stale | Last successful alert check > 15 min ago | "Alerts last checked 9:02 AM." Never "No active alerts" when the check is stale |
| Expired alert | `ends`/`expires` passed | Never shown as active, even from cache |

A failed refresh never writes zeros, empty lists or a "no alerts" result over last-known-good data.

## 10. Weather data architecture

No new project, package or dependency. `System.Text.Json` and `SocketsHttpHandler` are already in use. The
dependency graph checked by `ProjectDependencyGraphTests` stays unchanged.

| Layer | Proposed types (names provisional) | Responsibility |
|---|---|---|
| Domain `Weather/` | `WeatherQuantity` (value, unit, nullable), `ForecastPeriod`, `HourlyForecastPeriod`, `WeatherObservation` (station id, name, distance, timestamp), `WeatherAlert` (id, event, status, message type, severity, urgency, certainty, effective, onset, expires, ends, area description, headline, instruction, sender, references, source URL), `WeatherDataKind` (Observed / Forecast / Alert), `WeatherFreshness` | Pure values and rules: alert activity at an instant, observation and forecast staleness, daily high/low pairing in Eastern dates |
| Application `Weather/` | `IWeatherProvider` port, `IWeatherCacheStore` port, `WeatherSnapshot`, `WeatherRefreshPolicy`, `WeatherRefreshCoordinator` (single flight, due planning with `IClock`), `IWeatherQueryService` (local reads for a date or a month; never calls the provider) | Coordination and local reads. No HTTP, no files |
| Infrastructure `Weather/Nws/` | `NwsHttpTransport` (own `HttpClient` from `SocketsHttpHandler`, User-Agent, `Accept`, gzip, no cookies, no automatic redirects; follows only `https://api.weather.gov` redirects; returns status, headers and body), `NwsPointParser`, `NwsForecastParser`, `NwsObservationParser`, `NwsAlertParser`, `NwsWeatherProvider`; `Weather/FileWeatherCacheStore` | Transport and parsing; unit conversion; QC filtering (drop values flagged `X`/`B`/`Q`, confirm the codes in Weather B) |
| Desktop | `DashboardWeatherViewModel`, Calendar selected-date weather presenter, `WeatherSourceLinkPolicy` | Presentation only; UI updates through `IUiDispatcher` |

**Reused:** `IClock`, `EasternTimeZone`, `AppPaths`, `AtomicFileWriter`, `IUiDispatcher`, the external link
launcher, the design tokens and stale style, and the Phase 32 card/panel patterns. The ETag conditional-request
approach of `GitHubReleaseSource` is the model for `NwsHttpTransport`.

**Deliberately not reused:** `IRetrievalHttpClient` / `RetrievalHttpResponse` (it exposes no response headers and
sends no User-Agent; widening the shared official transport would touch every Accesso source),
`CoalescingRetrievalHttpClient` (Accesso nonce-specific), `IOfficialDataSource` / `SourceResult` / `SourceCategory`
and `OfficialDataRefreshCoordinator` (official operational-data states such as Verified, Closed and NotPublished
mean something else), `OfficialSourceLinkPolicy` (it must stay Carowinds/Six Flags only). `WeatherSourceLinkPolicy`
accepts only `https` on exactly `www.weather.gov`, `forecast.weather.gov`, `alerts.weather.gov` and
`api.weather.gov`.

**Alert logic.** Query both reference points and merge by alert `id`. Keep only `status` = `Actual`. An `Update`
replaces the messages it lists in `references`; a `Cancel` removes them. Active means
`effective ≤ now < (ends ?? expires)`. Sort by severity, then urgency, then onset. Applicability is NWS's own
point-in-polygon or point-in-zone result; the app never adds a state-wide alert.

**Untouched:** schema 18, draft payload 4, sync envelope 1, finalized snapshots, official pricing cache, Pass Perks
cache (`IOfficialReferenceDocumentRepository`), stored PDFs and PDF generation.

## 11. Dashboard integration plan

The Dashboard follows the approved Admissions Overview (Phase 35E-3). Spec §8 requires an approved visual
reference for further redesign, so **the owner approves a mock-up before Weather C** (decision D4).

Recommendation: **one compact card at the top of the right-hand column** (`DashboardSecondaryColumn`, above Daily
Ticket Pricing), about the height of the Pass Perks card's collapsed state, using the existing `dashCard` and
title-band styles:

- Title band "Weather · Carowinds" with "National Weather Service" and the updated time.
- Row 1, **Observed**: temperature and condition with "KCLT, 12 km · 8:52 AM"; heat index or wind chill only when
  NWS supplies it; wind and gust.
- Row 2, **Forecast**: today's high/low, the largest precipitation chance during today's operating hours (from the
  hourly forecast and the cached park hours), and the short forecast.
- Alerts: one line per active alert (event name, ends time), using the existing restrained warning accent with an
  icon and text (never colour alone); "View alert" opens the NWS page through the link policy.
- A "Details" link opens the selected-date Calendar panel's weather group for today (local navigation).

The gate card, Passdown, Shows, pricing, notes, Pass Perks and Official Data stay as they are, in the same order.
The card is in the right column, so it does not push the gate card or Passdown down; at the 1000/680 content-width
breakpoints it stacks after pricing. No radar, no animation, no chart, no emoji and no large icon art.

## 12. Calendar integration plan

The Calendar keeps the Phase 32/35E semantics: open/closed, draft/finalized lines, the three Pass Perks marker
kinds with the legend, the month-long perk summary, the selected date, existing buttons and routes.

- **Selected-date panel:** a "Weather" group after Park information, only for dates inside the cached forecast:
  high/low, short forecast, largest precipitation chance, any active alert for that date, "NWS forecast issued 6:13
  AM", and a collapsed "Hourly forecast" disclosure covering the operating hours (or the whole day when closed).
- **Day cells:** nothing in Weather D. A small high/low line in cells is a later option only if the owner asks for
  it (decision D7); cells already carry state, hours, Carolina Harbor, lifecycle and marker rows.
- **Horizon:** the live response covered today through 2026-12-03 (seven calendar days; 156 hourly periods). Dates
  beyond the last period: "No NWS forecast this far ahead". Past dates: no weather. Today shows the forecast; the
  observation stays on the Dashboard.
- Month navigation and date selection read the local cache only.

## 13. Optional Passdown considerations (evaluation only)

Weather in a Passdown could help the next shift, but it would mean a draft field (draft payload 4 → 5, sync and
PDF template changes) or a value captured when the PDF is made (which a later regeneration would change).
**Recommendation: defer.** If approved later: the forecast only (high/low, precipitation chance, short forecast),
labelled "NWS forecast issued …", chosen by an Include in Passdown option that is off by default, frozen into the
finalized snapshot, never inserted into an existing Passdown automatically, and never changing stored PDFs. Out of
scope for 37B–37E.

## 14. Operational safety boundaries

Weather is informational. The app does not close the park, decide whether attractions operate, declare lightning
clearance, infer closures from rain or wind, invent a weather policy, suppress or reword official alerts, treat a
forecast as certain, offer lightning detection, or present itself as an emergency warning system. Every weather
surface names the National Weather Service. The detail view states that alerts are checked about every five minutes
while the app is open and that established park weather procedures and official warning channels govern operations.
The cached screen never claims continuous monitoring.

## 15. Deterministic test plan

Fixtures in `fixtures/weather/nws/` are small synthetic or sanitized GeoJSON files shaped like the live responses in
§4 (public data; no personal information). The normal suite makes no NWS call. A `LiveSmoke` test (opt-in, like the
existing live tests) checks one point, one forecast and one alert query.

| Area | Tests |
|---|---|
| Forecast | point parsing; both reference points' zones; forecast URLs come from `/points`, not hard-coded; grid change invalidates the forecast; 14 periods; 156 hourly periods; `"16 to 21 mph"` wind text; null PoP; unknown unit code rejected; ISO offsets converted to Eastern; DST transition day; horizon end; "Tonight"-first forecast (no today high) |
| Observations | nearest fresh station chosen, fallback when stale or null; QC flags; null temperature; null heat index and wind chill (no derived feels-like); SI to US conversion; 75 min and 3 h staleness edges; an observation never shown as a forecast and the reverse |
| Alerts | two-point merge by id; Test/Exercise/System dropped; Update replaces and Cancel removes through `references`; `ends` versus `expires`; expired alert never active from cache; several simultaneous alerts sorted; missing headline, area or times; malformed JSON; link policy accepts only the four NWS hosts |
| Cache and network | `max-age`/`Expires` honored within floors/ceilings; `If-Modified-Since` / `If-None-Match` sent and `304` keeps the cache; timeout versus cancellation; 429 with and without `Retry-After`; 5xx; offline start with cache; corrupt or oversized cache file; unknown cache version; a failure never overwrites last-known-good; single flight (no duplicate refresh); no request on date selection, month navigation or tab change (request-count assertions) |
| UI | Dashboard card in all states; card position and the existing card order; 1920×1080, 1536×960, 1280×720 and 960×640 in Light and Dark with no horizontal overflow; Calendar weather group for in-horizon, beyond-horizon and past dates; Pass Perks markers, legend and month summary unchanged; automation names and keyboard order; reduced motion (no transitions); stale and empty states |
| Boundaries | no Weather type in Personal Sync payloads, draft payload or PDF models; schema and sync version guards unchanged; dependency graph guard unchanged |

## 16. Windows validation plan

Each implementation milestone that changes runtime behavior ships a `RC15-Phase37x-LOCALTEST` candidate with a
checklist in `docs/releases/`. On Windows: first run with no cache; offline start with cache; network unplugged
during a refresh; Dashboard card and Calendar group at 100/125/150 % DPI; Light, Dark and high contrast; Narrator
names for observed/forecast/alert rows; keyboard order; reduced motion; an 8-hour session (memory steady, request
count about §8); update from RC.14 keeps the cache file outside the install folder. Development-host renders are not
Windows evidence.

## 17. Performance and resource budget

| Measure | Budget |
|---|---|
| Startup | No added work before the window opens; the weather load is posted after open (no measurable change in `RealWindowStartupTimingTests`) |
| Dashboard with cached weather | ≤ 2 ms added view-model work (in-memory snapshot; no file read on navigation) |
| Calendar month navigation | ≤ 1 ms added per month (dictionary by Eastern date); zero network |
| Requests | ≤ about 30 per hour while open; ≤ 3 per day for points and stations; none while closed |
| Cache file | ≤ 256 KB |
| First weather after a cold start | Cached values immediately; network values within the 15 s timeout |
| UI thread | All HTTP, parsing and file I/O off the UI thread; updates through `IUiDispatcher` |
| Memory | One snapshot (< 1 MB); no growth over an 8-hour session (perf benchmark long-run) |
| Unaffected | Passdown editing, autosave, PDF preview, Save PDF, printing and navigation never wait for weather; an NWS outage leaves the rest of the app unchanged |

Baseline evidence at `c7c79aa` (development host, macOS; not Windows validation):

| Check | Result |
|---|---|
| Clean Release build (`--no-incremental`) | PASS — 0 warnings, 0 errors |
| Deterministic partitions (`build-and-test` skill) | PASS — 5,957 / 5,957, 0 skipped: Domain 467, Application 1,396, Infrastructure 1,350, Documents 484, Ui mixed 2,014, run-alone 30 + 144 + 2 + 7 + 4 + 59 (equals the Phase 36C baseline; UI partitions with `--blame-hang`, no hang) |
| Repository guards (31, inside the Infrastructure partition) | PASS — 31 / 31 at `c7c79aa` and again with this record and the index, architecture and RC.14 edits in place |

Live NWS discovery, recorded separately from the deterministic evidence: 10 read-only GET requests on 2026-11-27
around 13:50 UTC (three `/points`, forecast, hourly forecast, stations, one `/alerts/active`, two latest
observations), with a generic research User-Agent that names no person or project, two seconds apart. Responses
were kept only in the session scratch folder, not in the repository, fixtures or any application data.

## 18. Proposed implementation milestones

| Milestone | Scope | Runtime change | Windows candidate |
|---|---|---|---|
| **37B** Retrieval and local cache | Domain/Application/Infrastructure weather types, NWS transport and parsers, cache store, coordinator, fixtures and tests; **not registered in the composition root**; spec §4.1/§6 update after approval D1 | None (unwired) | Not needed; automated evidence only |
| **37C** Dashboard weather | Composition root wiring, the background coordinator, the Dashboard card, link policy, detail view and states | Yes (network + UI) | `RC15-Phase37C-LOCALTEST` |
| **37D** Calendar and detailed forecast | Selected-date weather group and hourly disclosure | Yes (UI) | `RC15-Phase37D-LOCALTEST` |
| **37E** Windows reliability and readiness | Offline, long session, accessibility, request-count and performance measurements; RC.15 readiness inputs | Fixes only | Final candidate |

Each milestone is small, has focused tests and the full partitions at close, keeps RC.14 frozen, waits for the
owner's Windows review before the next one, and does not push, tag or publish without separate authorization. Any
release is `0.1.0-rc.15` or the next verified identifier.

## 19. Risks and dependencies

- **Product approval (D1):** NWS is not a Carowinds/Six Flags source; spec §6.1 must be amended first.
- State-line applicability: alerts must use both points; the in-park points need confirmation.
- NWS may require an API key in the future (documented); the transport keeps identification in one place.
- Undocumented rate-limit behavior; observed headers can change; `304` support unconfirmed.
- Station outages or QC delays leave current conditions empty for hours; the design shows that honestly.
- Users may read cached alerts as live monitoring; the wording in §14 and the stale-check state address this.
- A forecast endpoint may fail temporarily (5xx) for a grid; last-known-good covers it.
- Windows-only behavior (proxy, TLS, DPI, Narrator) needs the owner's review.

## 20. Product specification review status

`AGENT_BUILD_SPEC.md` §4 (scope), §5 (principles), §6 (retrieval), §7 (cache and refresh), §8 (Dashboard), §9
(Calendar), §15 (PDF), §16 (Personal Sync), §19 (storage and privacy) and §21 (performance) were read. Weather is not
an active product area (§4.1), and §6.1 limits retrieval to public, official Carowinds and Six Flags sources with
"other explicitly approved official operational information". The National Weather Service is official government
information, but it is not a Carowinds source and has not been approved. The specification was not changed, because
this milestone authorizes research only. Before Weather B lands, the owner must approve adding Weather as an
informational area with NWS as its source (§4.1, a new weather section, and an exception in §6.1), and a Dashboard
visual reference under §8.

**Product specification:** Discrepancy requires approval

## 21. Architecture documentation review status

The diff is documentation only: this record, the documentation index, the release-state lines of
`docs/architecture.md` (owner-reported Phase 36C Windows test and RC.13 → RC.14 update) and a later sign-off section
in the RC.14 readiness record. No dependency, DI registration, state ownership, data contract, retrieval, persistence,
sync, PDF, lifecycle, threading, configuration, UI, security, packaging or retired-component change. The weather
design is proposed here and is not described as current in the architecture guides.

**Architecture documentation:** Updated

## 22. Explicitly deferred features

Weather in Passdowns and PDFs; day-cell weather indicators; historical weather; forecast backtesting and provider
comparison; any second or commercial provider; raw grid data (apparent temperature, gust forecasts); radar, maps and
lightning; push or desktop notifications for alerts; scheduled or headless weather retrieval; Personal Sync of
weather; personal task tracking; seasonal themes; Manager workflow; Morning Message.

## Owner decisions requested

| ID | Decision |
|---|---|
| D1 | Approve Weather as an informational product area with NWS as its only source (spec §4.1, §6.1) |
| D2 | Confirm Phase 37 (or move the initiative to Phase 38) |
| D3 | User-Agent contact: the public repository URL, no email |
| D4 | Approve a Dashboard weather card mock-up (spec §8 visual reference) |
| D5 | Alert check interval (proposed 5 minutes while the app is open) |
| D6 | "Refresh weather" placement (proposed: weather detail only; Check Online unchanged) |
| D7 | Day-cell weather indicators (proposed: none) |
| D8 | Confirm in-park reference points on each side of the state line |

## Later sign-off: owner decisions received (2026-11-27)

Added after this record's first commit (`4c15115`); the sections above are kept as written.

The product owner approved a product specification amendment, "Approved Product Specification Amendment —
Weather Information". It resolves:

- **D1 — approved.** NWS is an explicitly approved source for weather information only. The amendment does not
  widen the Carowinds/Six Flags source requirement for any park-specific information. Coverage must include the
  North Carolina and South Carolina portions of the property. Weather is informational only and stays outside
  Personal Sync, drafts, finalized History and PDF output. Alternative providers, accuracy comparisons, historical
  weather and operational automation are outside the approved scope.
- **D2 — confirmed.** Weather is Phase 37. The §2 provisional label no longer applies.

`AGENT_BUILD_SPEC.md` now records the amendment as approved but not yet implemented: §4.4, the §6.1 exception,
§6.6 (requirements), short cross-references in §8, §9.2 and §16.3, and acceptance criteria in §22.13. The
specification states no phase number, per its §25.4; this record is the cited authority. With that update, the
§20 discrepancy is resolved.

Still open: D3 (User-Agent contact), D4 (Dashboard mock-up), D5 (alert interval), D6 (refresh placement), D7 (day
cells) and D8 (in-park reference points). D4 and D8 must be settled before Weather C; D3 and D8 before Weather B
code goes live. No implementation milestone has started.
