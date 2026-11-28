# Phase 37D — Calendar Weather and Settings Integration

Status: implemented locally / awaiting Windows validation / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `a1c090d` (Phase 37F-12, documentation only) |
| Ending commits | `bdda770` (37D-1 Application/Documents reuse seams), `d988e04` (37D-2 shared preference and Settings), `1ccebbc` (37D-3 Calendar Weather), `83c9a85` (37D-4 visual fixtures and benchmark), then the documentation commit that adds this record; the Windows candidate is recorded in a later documentation-only commit |
| Reviewed source baseline | `83c9a85` (tests, builds, renders and measurements ran on this production source) |
| Dirty-tree caveats | Clean at start; no unrelated changes present |
| Authorization | Owner brief "Carowinds Operations — Phase 37D: Calendar Weather Integration + PDF Weather Default Setting" (2026-11-27): the Calendar selected-date forecast with expandable hourly detail (planned in Phase 37A §12, D7) and a Settings control for the existing Phase 37F Weather Outlook preference |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` `0.1.0-rc.14` (not bumped); RC.15 not prepared |
| Database schema / sync formats | Schema 18 (`Migration0018…`); sync envelope 1; draft payload 4; finalized snapshot 2 — unchanged. Weather cache format 1 and `passdown-weather-outlook.json` shape unchanged |

## Scope

Two production changes, as authorized: (1) a compact Weather group in the Calendar's selected-date panel, with a
collapsed hourly forecast, built from the weather the app already has; (2) Settings → Passdown Defaults → "Include
Weather Outlook by default", the same preference as the Passdown's Weather Outlook checkbox. Not done (not authorized):
weather in day cells, Dashboard or PDF layout changes, a second weather service, polling loop, HTTP client, provider,
API key, location detection, weather sync, schema/migration, draft or snapshot fields, pricing or Pass Perks changes,
the post-closing pricing issue, RC.15, version bump, push, tag or publication. Phase 37E was not started.

## 1. Starting architecture

At `a1c090d` the app had one `WeatherMonitor` (composition root; started after the main window, stopped in the
bounded shutdown) over the Phase 37B coordinator, cache file and NWS provider at the two verified park points (NC
`35.1050, −80.9420`; SC `35.1002, −80.9420`). The Dashboard card observed it; the Passdown read `Current` when it
captured a PDF. `OperatingHoursForecastSelector` (Application) selected the hourly periods for a date's park hours;
`PdfWeatherOutlookBuilder` (Documents) worded them. `AssociateViewModel` owned the Include Weather Outlook preference
(loaded from `FilePassdownWeatherOutlookPreferenceStore`, default on). The Calendar showed no weather; Settings had no
weather control. Phase 35E's selected-date panel (fixed 290 px, `detailRow` label/value rows, `Expander.disclosure`
for Shows and month-long perks) was the layout to fit into.

## 2. Calendar Weather presentation

`Desktop/Presentation/CalendarWeatherPresentation` (pure record) builds the group; `OperationsCalendarView.axaml`
places it after Park information and before Pass Perks, with the panel's own heading, rule, row and note styles:

- Heading "Weather"; a condition headline with one line icon (the PDF icon set as Avalonia geometries in the theme's
  secondary text colour, `Converters/WeatherIconGeometryConverter`; no emoji, image or font).
- A scope line saying what the values describe ("Park hours, 10 AM–10 PM", "Rest of park hours, 1 PM–10 PM", "Park
  closed · general forecast for the day", "Park hours not confirmed · general forecast for the day", "Park hours have
  ended · forecast for the rest of the day").
- Rows: Temperature (range over the covered hours), Rain (the PDF's "Up to 40%" / "Showers possible after 6 PM (up to
  40%)"), High / low (the NWS day forecast; "High 77°" or "Low 55° (high not in forecast)" when only one is given).
- A coverage note when the period is only partly covered or some hours gave no rain chance.
- Alert lines when applicable (§8).
- "Hourly forecast (N hours)" disclosure.
- Source line "National Weather Service forecast · issued … · updated …", or in the warning colour "… · may be out of
  date, last updated …".

It is not a second Dashboard card: no observation, no Refresh Weather, no alert details, no card chrome.

## 3. Hourly detail behaviour

Collapsed when the Calendar is created; `IsWeatherHourlyExpanded` keeps the choice across date changes for the
session and is never saved (as the Shows schedule). Each row: Eastern time ("10 AM"), icon, temperature, condition,
wind when NWS gives it ("SW 5 mph"), rain chance. Rows cover only the period in the scope line: the park hours (from
the hour in progress once open), the rest of the day after closing, or the whole Eastern day for a closed or
unconfirmed date (25 rows on the fall-back day, with "1 AM" twice). A missing temperature or rain chance shows "—" and
the row's accessible name says "not given"; no hour is interpolated. Each row has an automation name ("10 AM, 70
degrees, Sunny, rain chance 2 percent, wind SW 5 mph"); the disclosure is the shared Expander (Enter/Space,
ExpandCollapse automation).

## 4. Selected-date handling

The group is built for the loaded snapshot's own selected date only. Between a date change and its load completing
`Weather` is null (hidden), so another date's forecast never appears under the new heading; rapid changes end on the
last date (version-checked load). Past dates: "Past date. NWS forecasts cover today and upcoming days only, so no
weather is shown for this date." — never today's forecast as history. Dates beyond the cached forecast: "No NWS
forecast this far ahead. The current forecast runs through Sunday, Oct 18." Dates covered only by the 12-hour forecast
(beyond the ~6.5-day hourly horizon): condition, high/low and the day's rain chance, with "No hourly forecast for the
park hours yet; the values are the NWS day forecast." Every date and hour is Eastern (`EasternTimeZone`).

## 5. Operating-hours logic

`OperatingHoursForecastSelector.OperatingWindow` (new public method; `Select` now calls it, same results) gives the
park-hours window with the Phase 37F rules: closing at or before opening runs past midnight into the next day; a wall
time skipped by spring-forward moves an hour later. The Calendar uses the date's effective hours from the same
snapshot as the rest of the panel (manual override and Private Buyout included). The wording reuses
`PdfWeatherOutlookBuilder` over the hours actually covered (contiguous from the start; never stretched across a gap or
past the horizon), plus the new `PdfWeatherOutlookBuilder.DescribeCondition` for per-hour wording and icons. Unlike the
PDF, the Calendar shows partial coverage (with a note) and stale forecasts (marked), because it is an on-screen
reference, not a frozen document. Unknown hours are never invented: the date gets the labelled general forecast,
which the Phase 37A plan (§12: "or the whole day when closed") supports, and is never called park hours.

## 6. Forecast freshness

The snapshot's existing policy decides freshness (`WeatherRefreshPolicy`: stale after 3 h, dropped after 24 h); no
second definition exists. A stale forecast keeps its values, marks every row stale for screen readers, and shows the
warning-coloured "may be out of date, last updated …" line; nothing reads "updated" for it.

## 7. Missing and stale states

| State | Shown |
|---|---|
| Cache not read yet / never retrieved | "Getting the forecast from the National Weather Service…" |
| Retrieval not set up | "Weather unavailable. Weather retrieval is not set up for the park's location." |
| No usable forecast (outage before any data, provider error, too old) | "Weather unavailable." + the failure reason (could not be reached / returned an error / asked to wait) |
| Offline with a cached forecast | Values with the stale treatment once older than 3 h |
| Partial coverage | Values for the covered hours + "Hourly forecast covers 10 AM–9 PM only of the park hours (closing 10 PM)." |
| Missing values | Row left out (temperature range) or "Not given in the forecast"; hourly "—"; never 0 |
| Past date / beyond horizon / closed / unknown hours | §4 and §5 wording |

A weather outage never blocks the Calendar: the group is built from memory only and every other part of the panel
loads exactly as before.

## 8. Alert presentation decision

Alerts appear only when the alert check is current (`AlertsStatus` Fresh) and only on dates from today on whose
interval the alert's own onset/effective and end/expiry times cover; an alert without an end time is never active
(`WeatherAlertRules`), so it is never projected onto a later date. At most two lines ("NWS alert: Wind Advisory · until
8:00 PM ET") plus "and N more", followed by "Current alert details are on the Dashboard." A stale or failed alert feed
shows nothing and never says "no alerts"; expired alerts are never shown; past dates never show alerts (the active feed
is not a history). Informational only.

## 9. Existing monitor reuse

`OperationsCalendarViewModel` gains an optional `IWeatherMonitor` (DI supplies the existing singleton). It reads
`Current` and subscribes once to `Changed`; nothing else. No new service, loop, timer, HTTP client, cache or file.

## 10. Network request behaviour

Selecting dates, stepping days, changing months, Go to Today and expanding the hourly forecast make no request and do
not start the monitor (`FakeWeatherMonitor` request/start counts stay 0 in tests; guarded in source by
`WeatherActivationGuardTests.CalendarWeather_OnlyReadsAndObservesTheMonitor_AndNeverRequestsWeather`). The monitor's
schedule (alerts about every 5 minutes, others by cache headers) is unchanged.

## 11. Calendar performance

Monitor reports are coalesced (one UI post however many arrive) and rebuild only `Weather`; the month cells, weeks and
the official presentation are not rebuilt (asserted by reference). `CalendarWeatherPresentation` has value equality
over its lists, so an unchanged report re-renders nothing. The panel width and day cells are unchanged (real-window
test compares every cell size with and without weather).

Opt-in `tools/perf-benchmarks calendar-weather 2000` (macOS development host, Apple Silicon, .NET 10.0.11, Release; not
Windows evidence):

| Measure | Without weather | With weather |
|---|---|---|
| Weather group build, today (12 park hours) | — | 32.0 µs, 20,464 B per call |
| Weather group build, closed day (whole day) | — | 46.7 µs, 30,696 B |
| Weather group build, past date | — | 0.3 µs, 416 B |
| View-model date step (400 steps) | 17.1–18.9 µs, 31,281 B | 59.2–64.3 µs, 56,909 B |
| Realized Calendar 1280×860, date step median (headless Skia, 100 steps) | 44.7–53.5 ms | 43.2–43.3 ms |
| Realized Calendar, month step median (20 steps) | 40.0–51.0 ms | 40.9–51.0 ms |
| Managed heap after 120 realized steps | 40.7–41.1 MB | 41.1–41.2 MB |

The view-model cost is about 45 µs per date change; realized navigation is dominated by the existing day-cell
re-templating and the difference is within run-to-run noise. No growth across steps. Phase 34/35 paths (preview
coalescing, engine warm-up, request coalescing, direct bitmap rendering) are untouched.

## 12. Settings preference behaviour

Settings → Passdown Defaults, below the Default included sections, under "PDF Weather Outlook": checkbox "Include
Weather Outlook by default", described "Include the Weather Outlook in Passdown PDFs when a valid forecast is
available." and "Saved on this computer only. It is the same setting as Weather Outlook under Include in Passdown."
Standard Settings checkbox, typography and divider; no new section or navigation item. As the brief directs, the
stored value is the user's preferred state (no default-versus-override): on for a first-time user, and either surface
changes it for both.

## 13. Cross-view synchronization

`PassdownWeatherOutlookSettingsViewModel` (Desktop, registered as a singleton) is the one owner. Settings binds
`PersonalSyncSettingsViewModel.WeatherOutlook.IncludeWeatherOutlook`. `AssociateViewModel` awaits the shared
`PendingLoad` before its first capture (no preview with the wrong value; no added startup work beyond the existing
load), forwards its own checkbox changes to the shared value, and follows the shared `PropertyChanged`, applying a
change made in Settings without saving it again (`Preview.NotifyContentChanged()` refreshes the preview at the
leading edge). No feedback loop: each change is saved exactly once (11 changes → 11 saves in tests). Changes before
the file is read win over the file. No file watcher or poll; both views live for the session.

## 14. Preference persistence

Same store (`FilePassdownWeatherOutlookPreferenceStore`), same file (`passdown-weather-outlook.json`), same atomic
writer; the shape `{"IncludeWeatherOutlook": bool}` is unchanged, so a Phase 37F file is read as is. Saves run one at a
time and each writes the value current when it runs (rapid toggles end with the latest on disk). A failed save keeps the
choice for this session and shows "This change couldn't be saved on this computer. It applies until the app closes;
after a restart the previously saved choice is used." (the Phase 37F semantics, now stated in Settings); the next
successful save clears it. Not in drafts, Personal Sync, SQLite or finalized snapshots (reflection test on
`AssociateDraftPayload` and `FinalizedPassdownSnapshot`).

## 15. PDF compatibility

No Documents layout or template change. The Passdown checkbox stays below Shows; changing either control refreshes
the preview; Print, Save PDF and Create PDF use the shared value; off omits weather from all three templates with no
space; missing or stale weather never blocks generation; icons stay vectors; no PDF alerts; finalized History PDFs are
untouched. Visual fixtures `Preview-Modern`, `Preview-Compact` and `Associate-PassdownChoices` render byte-identical
to `a1c090d`; all Phase 37F Documents and integration tests pass unchanged.

## 16. Automated tests

| File | Covers |
|---|---|
| `Ui.Tests/CalendarWeatherPresentationTests` (22) | today, after opening, tomorrow with partial coverage, 12-hour-only date, past, beyond horizon, closed, unknown hours, overnight hours, hours ended, DST fall-back (25-hour day), missing temperatures, missing rain, PDF wording, gaps, stale/offline cache, unavailable/provider error/not set up, loading, Eastern labels and wind, no wrong-date leakage, alerts by interval and freshness, value equality |
| `Ui.Tests/Phase37DCalendarWeatherViewModelTests` (9) | today/tomorrow/past/beyond via commands, no leakage while loading, rapid changes, no request/start and one subscription after 20+ navigations, monitor update rebuilds only Weather, coalescing, hourly expand/collapse, official data unchanged without/with weather |
| `Ui.Tests/Phase32PassPerksUiTests.Phase37D` (1) | markers, counts, legend and month summary identical with and without weather and across updates |
| `Ui.Tests/Phase37FPassdownWeatherIntegrationTests.Phase37D` (10) | default on, reading a 37F file, Settings → Passdown (preview refresh, one save), Passdown → Settings, rapid toggles, restart, one file, save failure, early choice, load before first preview, no draft/snapshot field |
| `Ui.Tests/Phase37DSettingsWeatherOutlookMarkupTests` (3) | exact label/description and section, one control, singleton composition, Weather only in the panel and not in day cells |
| `OperationsCalendarViewTests.Phase37D` (run-alone, 5) | Light/Dark at 960×640 and 1280×860: group inside the panel, vector icon, keyboard expansion, named rows; day cells unchanged in size and content |
| `Phase15ERealWindowLayoutTests.Phase37DSettings` (run-alone, 4) | Light/Dark at 960–1920: visible, same styling as its neighbours, named, Space toggles the shared value, follows external changes |
| `Infrastructure.Tests/Weather/WeatherActivationGuardTests` (+1, list extended) | Desktop weather files are the approved list (Calendar and Settings files added); the Calendar only reads `Current` and subscribes to `Changed`; the preference holds no weather data |

Regression: Dashboard Weather, NWS cadence/cache, both reference points, the three PDF templates, PDF omission when off,
Print/Save PDF, finalized immutability, official refresh, Calendar schedules and Pass Perks tests all run unchanged in
the full suite. The Phase 37F rig gained an optional `Shared` property (no assertion changed). Live NWS tests stay
opt-in and were not run.

## 17. Visual testing

`tools/visual-fixtures` scenarios `Calendar37D-Today`, `-TodayHourly`, `-Tomorrow`, `-Beyond`, `-Past`, `-Alerts`,
`-Stale`, `-Unavailable` and `Settings37D-WeatherOutlook`, rendered at 1280×860 and 960×640 in Light and Dark, one
process per scenario (peak 378–631 MB; 4–8 captures each): no horizontal overflow and no button past the right edge in
any capture. Reviewed: the group reads as part of the panel, subordinate to Park information; hourly rows fit the
290 px panel at 960 px; the stale line uses the warning colour; Settings matches the Passdown Defaults section. One
wording issue found in review and fixed before commit (the temperature row first reused the "Park hours" label, and a
partial rain value wrapped). Before/after against `a1c090d` (built in a temporary worktree):
`Calendar-Busy`, `-BusyExpanded`, `-Historical`, `-PassPerksGrid`, `Preview-Modern`, `Preview-Compact`,
`Associate-PassdownChoices` byte-identical (the Calendar without a monitor is unchanged); `Settings-CalendarDisplay`
differs only because the new checkbox moves the Calendar section down. Headless macOS renders are not Windows DPI or
accessibility evidence.

## 18. Windows LOCALTEST details

Recorded after the package is built (see the follow-up section and
[the Windows checklist](releases/rc15-phase37d-localtest-windows-checklist.md)).

## 19. Windows acceptance checklist

[releases/rc15-phase37d-localtest-windows-checklist.md](releases/rc15-phase37d-localtest-windows-checklist.md).
Windows validation: **NOT EXECUTED**.

## 20. Compatibility guarantees

Schema, migrations, Personal Sync envelope and channels, draft payload, finalized snapshot, weather cache format,
preference file shape, update manifest/signing, packaging and the PDF templates are unchanged. No new project
reference or package. RC.14 and every earlier artifact are untouched. The Calendar without a monitor (tools, older
tests) behaves exactly as before; `AssociateViewModel` without a shared preference keeps the Phase 37F behaviour.

## Validation

| Check | Result |
|---|---|
| Deterministic partitions (`build-and-test` skill) | PASS — **6,463 / 6,463, 0 skipped**: Domain 488, Application 1,499, Infrastructure 1,436, Documents 634, Ui mixed 2,133; run alone: `OperationsCalendarViewTests` 35, `Phase15ERealWindowLayoutTests` 162, `RealWindowStartupTimingTests` 2, `SpellCheckNavigationLifecycleTests` 7, `Phase21DMathEditorWindowTests` 4, `Phase33PreviewWindowTests` 63 (UI partitions with `--blame-hang`; no hang). Baseline 6,408 + 55 new |
| Repository guards | PASS — 31 / 31 (with this record and the documentation edits in place) |
| Clean Release build | PASS — `bin`/`obj` of every solution project deleted, then `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors. `tools/visual-fixtures` and `tools/perf-benchmarks` (outside the solution) build with 0 warnings |
| Focused runs during the work | Weather backend (Infrastructure `Weather`), Dashboard Weather (`DashboardWeather*`, `Phase37CWeatherCompositionTests`), Phase 37F PDF weather (Documents `Associate/Weather`, `Phase37FPassdownWeatherIntegrationTests`, `Phase33PreviewWindowTests` incl. the 37F checkbox) all pass inside the partitions above |
| Live NWS tests | Not run (opt-in) |
| Windows validation | NOT EXECUTED |

## Documentation maintenance

**Product specification:** Updated

**Architecture documentation:** Updated

Product specification: §6.6 status line; Include Weather Outlook gains the Settings control (supersedes "there is no
second control"; authorized by the Phase 37D brief); new Calendar selected-date forecast bullet; §9.2 Calendar weather
note; §17.3 Passdown Defaults. Architecture: `architecture.md` (Weather paragraph, new Phase 37D paragraph),
`architecture/desktop-ui.md`, `architecture/data-and-persistence.md`, `architecture/retrieval-and-refresh.md`,
`architecture/testing-and-performance.md`; `docs/README.md` index. Verified against source at `83c9a85`.

## 21. Known limitations

- Windows rendering, DPI (100/125/150 %), Narrator and keyboard behaviour are not yet validated on Windows.
- The hourly list for a closed or unconfirmed date shows the whole remaining day (up to 25 rows) inside the
  collapsed disclosure; no shorter general window is defined by the specification.
- Dates beyond the hourly horizon but inside the 12-hour forecast show day-forecast values only.
- Alert lines need a current alert check; when the feed is stale the Calendar shows none and the Dashboard remains the
  place for alert status.
- The Weather group refreshes on monitor reports (at least each minute while values change); an unchanged report
  causes no re-render.
- Performance figures are from the macOS development host.
