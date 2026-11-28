# Phase 37E — Integrated Validation, Reliability Audit and RC.15 Release Readiness

Status: validation complete locally / owner-approved D-1 Calendar wording implemented locally / integrated Windows
candidate built (`RC15-Phase37E2-LOCALTEST`, superseding `RC15-Phase37E-LOCALTEST`) / awaiting owner Windows acceptance /
not owner-accepted as an integrated build / RC.15 not prepared / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; 41 commits ahead of the local `origin/feature/passdown-redesign` ref at the start; remote not re-fetched; nothing pushed) |
| Starting commit | `2427b9d` (pricing harness 2 package record, documentation only) |
| Ending commits | `8921324` (37E-1: NWS request-volume simulation tests, test only), `4f17bed` (37E-2: this record, the checklist, index and testing guide; documentation only), `69d70a7` (37E-3: owner-approved D-1 Calendar wording, tests, specification and architecture), then the 37E-4 documentation commit (D-1 results and the second package) |
| Reviewed source baseline | `69d70a7` for the D-1 change, the final suite and `RC15-Phase37E2-LOCALTEST`; `8921324` (production identical to `1be060d`) for the audit, benchmarks and the first package |
| Dirty-tree caveats | Clean at start and before packaging; no unrelated changes present |
| Authorization | Owner brief "Carowinds Operations — Phase 37E: Integrated Validation, Reliability Audit & RC.15 Release Readiness" (2026-11-28): validation, tests, bounded measurements, documentation and a LOCALTEST package; production changes only after owner approval. Owner reply "Yes, implement D-1" (2026-11-28) approved the §19 D-1 change, implemented in §25 |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` `0.1.0-rc.14` (not bumped); no RC.15 tag, manifest or signature exists |
| Database schema / sync formats | Schema 18 (`Migration0018CreateOfficialReferenceDocumentsTable`); sync envelope 1 (`SyncEnvelopeSerializer.CurrentSyncSchemaVersion`); draft payload 4 (`AssociateDraftPayload.CurrentSchemaVersion`); finalized snapshot 2 (`FinalizedPassdownSnapshot.CurrentSchemaVersion`); official price history file 1; weather cache format 1; update manifest format 1 — all unchanged since RC.14 |

## 1. Executive summary

The combined branch (RC.14 + Phase 36 maintenance + 37B/C/D/F3 Weather + the post-closing pricing retention fix)
passes the full deterministic suite (**6,548 / 0 failed / 0 skipped**), all 31 repository guards and a clean Release
build with 0 warnings. A source audit found **no release blocker**: no schema, sync, snapshot, updater, packaging-rule
or official-retrieval change since RC.14; weather is isolated, local-only and read-only outside its one monitor; the
pricing fix is narrowly scoped. Deterministic simulation confirms about **31 NWS requests per hour per running app**
(at most 33 with very short provider cache lifetimes, 37 when the nearest station is not reporting, about 12 while NWS
is failing or rate-limiting), so a few devices on one public IP make a negligible load.

The audit raised D-1 (Calendar retained-price wording), which the owner approved and which is now implemented
locally (§25). Still open: D-2 (preview freshness after weather arrives), D-3 (high-DPI printing observation) and D-4
(integrated Windows acceptance of `RC15-Phase37E2-LOCALTEST`). Recommendation: **conditional GO** to RC.15 preparation
once the integrated checklist is run on Windows; **NO-GO** for publication until then.

## 2. Verified source baseline

- HEAD at start `2427b9d`; ancestry verified: `700d881` (37D-6) → `1be060d` (fix) → `42f6a7b` → `e0ff3d9` → `780da37`
  (harness) → `f3966d1` → `efd6f63` (harness account) → `2427b9d`. All reported commits are ancestors of HEAD; no newer
  commit existed. `3d603f0` (RC.14 tag) is an ancestor.
- Production files changed since RC.14 (`git diff 3d603f0..8921324 -- src`): 64 files (Application 18, Desktop 25,
  Documents 8, Domain 5, Infrastructure 7, plus two tracked `.DS_Store` deletions from the governance commit). **None**
  under `Infrastructure/Storage/Migrations`, Personal Sync, `Carowinds.Passdown.Updater`, the official retrieval
  adapters or the shared official HTTP transport; `packaging/` unchanged apart from the same `.DS_Store` deletion.
- Instructions read: both `CLAUDE.md` files, the nine `.claude/rules/`, the `build-and-test` and `phase-review` skills
  (the `pdf-design`, `retrieval-source` and `ui-polish` skills apply to change work, none of which was done),
  `AGENT_BUILD_SPEC.md` (weather §6.6, pricing §7.5, Calendar §9.2, Settings §17.3, acceptance §22),
  `docs/architecture.md` and the retrieval, desktop-UI, data and testing guides, the 36A–C, 37B–D, 37F and pricing
  records, `docs/release-strategy.md`, `docs/release-manifest.md` and the RC.14 readiness record.

## 3. Previous Windows acceptance evidence

| Item | Evidence | Scope of the evidence |
|---|---|---|
| RC.13 → RC.14 in-app update | Owner confirmed success (RC.14 record §15) | Overall; not proof of RC.14 → RC.15 |
| Phase 36C ERT editor extraction | Owner: "it worked" | Overall, not item by item |
| Phase 37C Dashboard Weather | Owner: "it worked" | Overall, not item by item |
| Phase 37F3 PDF Weather Outlook | Owner: "looked good" | Overall, not item by item |
| Phase 37D Calendar Weather and Settings | Owner: "looked great" | Overall, not item by item |
| Pricing retention | `carowinds-pricing-retention-harness.exe seed` → PASS (8 checks); `verify` → PASS (8 checks); Park Ticket $49.00, Fast Lane Reserve $49.00, Fast Lane Ultimate $85.00, verified 2026-08-20 3:10 PM ET, unavailable check 10:15 PM ET; zero network requests | **Retention and storage confirmed on Windows.** The Products & Pricing screen, row note, override and PDF checks (checklist S/§F) have **not** been reported |
| This integrated package | — | **NOT EXECUTED** |

No item above is treated as a full run of its detailed checklist.

## 4. Integrated architecture review

- Dependency direction unchanged (architecture tests in the suite pass). The weather feature adds no project
  reference or package; Desktop composes one `NwsWeatherProvider`, one `FileWeatherCacheStore`, one
  `WeatherRefreshCoordinator` and one `IWeatherMonitor` singleton (`App.axaml.cs:841–857`).
- Lifecycle: `weatherMonitor.Start()` is called once after the main window is assigned (`App.axaml.cs:247`); the first
  request waits `WeatherMonitor.StartupDelay` (10 s) on the thread pool, so no weather work delays the window; the
  cache is read first, so cached weather shows offline. `StopAsync` joins the bounded shutdown
  (`App.axaml.cs:378`). The scheduled-refresh host never resolves the monitor: no request while the app is closed.
- Single owner of the Weather Outlook preference (`PassdownWeatherOutlookSettingsViewModel`, singleton); one draft
  owner (`AssociateViewModel`); the 36C `EarlyRideTimeEditorViewModel` edits through the same workspace and autosave
  path (36C tests and the real-window partitions pass unchanged).
- One PDF pipeline: preview, Print, Save PDF and Create PDF all build a `PassdownPdfRequest` from
  `CapturePreviewRequest` and the shared export service; the weather value is one immutable capture per generation.
- Retrieval: weather is a separate path (own transport, `api.weather.gov` only, own policy). Official sources,
  `OfficialDataRefreshCoordinator`, accesso coalescing (only `request_token` excluded at the approved calendar
  endpoint) and the official allowlist are untouched since RC.14 (verified by diff, and `refresh-requests` still
  reports 9 coalesced POSTs per 7-day window).

## 5. Dashboard Weather findings

Verified in source and existing tests (`DashboardWeather*`, `Phase37CWeatherCompositionTests`, real-window partials):
fixed reference points (`CarowindsWeatherLocation.Park`, NC `35.1050, −80.9420` / SC `35.1002, −80.9420`; no device
location API anywhere in the solution); observation labelled with its station; observed versus forecast worded
separately; park-hours rain chance; missing high/low handled; Fresh/Stale/Unavailable states. **Alert safety:** "No
active NWS alerts" is shown only when the alert check is current (`DashboardWeatherPresentation.cs:253, 616`); a stale or
failed check says "Alert status unknown — alerts last checked …" (`:205–207`), and a cached alert still in effect stays
visible. Refresh Weather uses the manual mode (floors and backoff honoured), reports failures honestly, and does not
start Check Online. Navigation never requests (guarded by `WeatherActivationGuardTests`). **No defect found.**

## 6. Calendar Weather findings

Verified in source and tests (`CalendarWeatherPresentationTests` 22, `Phase37DCalendarWeatherViewModelTests` 9,
`OperationsCalendarViewTests.Phase37D` 5): weather is built only for the loaded snapshot's own date (hidden between a
date change and its load, so no cross-date leakage); Eastern time throughout; past, beyond-horizon, closed, unknown
hours, overnight closing, DST fall-back (25-hour day), partial coverage and missing values each have explicit wording;
alerts only when current and only for intervals they cover; no weather in day cells (markup test); Pass Perks
unchanged (with/without weather test); no request or monitor start from any Calendar interaction. Benchmark (§13)
shows no responsiveness change. **No defect found.**

## 7. PDF Weather findings

Classic, Modern and Compact: outlook from the cached hourly forecast for the workspace's own date and effective hours
(`PassdownWeatherOutlookSource`), vector icons (no emoji), small NWS attribution, zero header displacement where
approved (baseline glyph comparison tests), never alerts, omitted when unavailable/stale/excluded/out of range, never
blocks generation; the document model drops an outlook for another date. Finalization passes no weather; History opens
stored bytes. All Phase 37F Documents and integration tests pass.

**Preview freshness (decision D-2).** A weather change alone does not refresh an already displayed preview (no
subscription, by the 37F design). Print and Save PDF reuse the preview's document only when editor version, date,
template **and** outlook are unchanged; otherwise they regenerate with the current weather and the preview then shows
the printed bytes (37F §5; `Phase37FPassdownWeatherIntegrationTests` "weather change → Print regenerates and preview
shows the printed bytes"). Consequence: the printed or saved file can be *newer* than what the preview showed a moment
earlier (for example the first ~10 s after launch: preview without weather, print with weather). The output is never
older than the preview and never another date's weather. Impact: low (a surprise, not incorrect data). This is
documented in the 37F record but not shown in the UI. Options are in §19.

## 8. Settings synchronization findings

One local-only value (`passdown-weather-outlook.json` under `AppPaths.LocalApplicationDataDirectory`); the Passdown
checkbox sits in Overview → Include in Passdown below Shows, not in Actions (markup tests); Settings → Passdown Defaults
"Include Weather Outlook by default" binds the same instance; changes propagate both ways with one save each; restart
restores the saved value; toggling never marks the draft changed; disabled means no weather and no reserved space in all
three templates; preview refreshes at the leading edge. Not in drafts, sync, SQLite or snapshots (reflection tests).
**No defect found.**

## 9. Pricing-retention findings

Root cause and fix (`1be060d`) re-read against source: retention applies only when a dated pricing category in the
Phase 21A `StoredObservationsOnly` set (dated admission, Fast Lane, haunted, dated Bring-A-Friend, BAF Duo, dated
Military/First Responder) answers `Unavailable`/`NotPublished` with no payload while a `Verified` payload is stored;
then only the `last_attempt_*` columns are written (`SqliteDatedResultRepository`, status guard). A new `Verified`
answer still replaces (increase, decrease, withdrawal while on sale, reopening); `Error` keeps the stale path; never
verified shows nothing; park hours and catalog sources are excluded. `AssociateVerificationState.PreviouslyVerified`
keeps the original verified time; Products & Pricing, Dashboard and the PDF footer disclose it; manual overrides stay
draft-only and effective. Not persisted or synced beyond the existing row. The 55 pricing-retention tests pass.

**Calendar caveat (decision D-1) — confirmed by source.** `OperationsCalendarService.BuildDailyAdmissionPrice` picks the
lowest dated admission product with an official price, whatever its verification state, and
`OperationsCalendarPresentation.BuildDailyAdmissionPriceText` prints only the amount (`$49.00`, or "Starting from
$49.00"). So after sales end for today the Calendar's selected-date "Daily ticket" reads `$49.00` with no "last
verified" or "availability not confirmed" qualifier, while Products & Pricing, the Dashboard and the PDF qualify the
same value. The same plain amount is shown for a `Stale` price (pre-existing since Phase 13D). Impact: a user could
read the Calendar as "tickets for today are on sale at $49"; no stored data is wrong. Severity: needs decision (not a
blocker: the price is the real verified price for that date, and every operational output qualifies it). Smallest
change proposed in §19; **approved by the owner and implemented in §25** (the `Stale` wording was left unchanged).

## 10. NWS rate-limit assessment

**Policy (verified in `WeatherRefreshPolicy`):** alerts target 5 min, floor 60 s, for each of the two points; forecast
and hourly floor 30 min, ceiling 3 h, otherwise the provider's cache expiry; observation floor 10 min, not re-asked
until the held report is 20 min old, nearest station then the next one only if the nearest has nothing fresh (two
stations at most); point and station list floor 24 h. Conditional requests (`If-None-Match`/`If-Modified-Since`),
`Retry-After` (longer wins), backoff 30 s → 2 min → 10 min → 30 min, one monitor per process (DI singleton, `Start` is
idempotent), single-flight coordinator, no request from Calendar, PDF, navigation, or while closed; no GPS.

**Deterministic simulation** (new `Application.Tests/Weather/WeatherRequestVolumeSimulationTests`, 8 tests): the real
`WeatherMonitor` loop and coordinator with the default policy, a virtual clock advanced by the loop's own waits and a
scripted provider (stations report hourly at :53; no network):

| Scenario (hours simulated) | First hour | Each later hour |
|---|---|---|
| Typical cache lifetimes (forecast 1 h, observation 5 min, alerts 5 s) (8 h) | 35 | **31** (alerts 24, forecast 1, hourly 1, observation 5) |
| Every response expires after 1 s — floors only (6 h) | 37 | 33 |
| Nearest station not reporting — second station also asked (6 h) | 41 | 37 |
| Persistent 429 / provider error / network failure from hour 1 (6 h) | 35 | 25, 14, then 12 |
| 429 with `Retry-After: 1 h` on alerts (4 h) | 35 | 9 during the wait (no alert request), then 31 |
| Refresh Weather pressed at every loop pass for 3 h (extreme) | 131 | 129 (alerts once per minute per point; forecasts and observation still at their floors) |

The historical "about 34 per hour" (37B) is confirmed as an upper bound for normal operation; the measured steady state
is 31. Points/stations: 2 and 1 requests per 8 h.

**Multiple devices on one public IP.** Each running app is independent (per-device cache, no cross-device
coordination), so N apps make N × the figures above: 3 apps ≈ 93/h (≈ 1.6/min), 5 apps ≈ 155/h (≈ 2.6/min), 10 apps ≈
310/h (≈ 5.2/min). NWS publishes no fixed numeric limit (its API documentation describes an undisclosed limit that is generous for
typical use, with requests allowed again shortly after it is exceeded; the page was not re-read in this phase), so no guarantee is claimed; these rates are a small fraction of a request
per second even for ten devices, and if NWS does answer 429 the app falls to ≈ 12/h per device and honours
`Retry-After`. Bursts: at startup each app makes 8 requests in about a second after its 10 s delay. **Assessment:
suitable for typical use on a few devices; no reduction proposed.** If ever needed, the narrowest lever is the alert
target (5 → 10 min halves the dominant 24/h) — an owner decision (D5), not recommended now.

## 11. Personal Sync compatibility

Envelope 1, channel payloads unchanged; no sync file touched since RC.14. Weather cache (`weather-cache.json`) and the
Weather Outlook preference are flat files under `AppPaths.LocalApplicationDataDirectory`, never in the sync folder
(guard and reflection tests: `WeatherOutlookPreferenceBoundaryTests`, `WeatherActivationGuardTests`, the 37D "no
draft/snapshot field" test). PDF Weather Outlook data is not in `AssociateDraftPayload` or `FinalizedPassdownSnapshot`.
The pricing harness is a separate tool (`tools/pricing-retention-harness`, outside the solution) and its synthetic data
lives only in a test account's database, which is never packaged or synced (release-content rules refuse databases).
The full Personal Sync suites (disposable temporary folders, atomic writes, conflict handling) pass. Windows sync with
a disposable folder: checklist §G, not executed.

## 12. PDF/History compatibility

Finalized snapshot 2 unchanged; finalization passes no weather (37F §6); History opens stored bytes without network
(immutability tests pass). Retained prices freeze into a next-morning snapshot with the existing "not recently
verified" footer flag. No backfill of weather into History.

## 13. Performance findings

Host: MacBook Pro, Apple M2 Pro (12 logical CPUs), 16 GB, macOS 27.2.1, .NET 10.0.11, Release; `tools/perf-benchmarks`
at `8921324`, synthetic data, no network. **Not Windows evidence.**

| Benchmark (iterations) | Phase 37E | Earlier reference |
|---|---|---|
| `pdf-startup` (5 processes/mode): first preview PDF work, warmed | 14.7 ms median | 13.3 ms (34B-2, n=9) |
| `pdf-startup`: cold / concurrent | 90.8 / 91.9 ms | 82.3 / 81.6 ms (34B-2) |
| `pdf-render` (40): busy page, direct path | 0.73 ms, 1.4 KB allocated per render | 0.63 ms, 1.3 KB (34B-2) |
| `preview` (25 edits): busy / 80 notes | 11.4 / 27.3 ms | 14.2 / 21.9 ms and 6.5 / 20.6 ms in two 37F runs |
| `print-bands` (3 pages, 600 DPI, no GDI) | 66 ms / page, 225 MB managed allocated per page, working set 137–154 MB after | 62 ms, 225 MB (34B-2) |
| `db` (40): Associate date load / Calendar month, 0 synced | 0.37 / 0.71 ms | 0.38–0.43 / 0.76–0.89 ms (pricing record) |
| `db`: Price History open (34B-2 reader), 2,000 synced | 11.5 ms | ~10 ms (34B-2) |
| `weather` (2,000): snapshot / card build | 12.3 µs / 31.3 µs; Dashboard realize 44.7 → 48.0 ms with the card | 37C: +3.6 ms with the card |
| `pdf-weather` (200): generate without / with outlook | Classic 4.2 / 7.0, Compact 2.8 / 4.5, Modern 2.8 / 4.3 ms | 37F figures, same shape |
| `calendar-weather` (1,000): VM date step / realized date step / heap after 120 steps | 57–66 µs / 48–51 ms / 41.4 MB | 37D: 59–64 µs / 43–53 ms / 41.1–41.2 MB |

Peak resident memory of each benchmark process: 59–270 MB (`/usr/bin/time -l`). All differences are within the
run-to-run spread shown by untouched paths; no regression. Memory stability: the repeated-navigation, startup timing and
preview real-window classes pass; peak sampled test-host RSS during the full suite was 484 MB (3 GB watchdog never
fired). Native PDFium memory is not separately measurable on this host beyond working set. High-DPI Windows printing
(the 225 MB transient band allocation per page) is a known, unchanged investigation finding with no confirmed leak;
observation item E.11 is in the checklist (D-3). Phase 36E remains deferred: nothing measured justifies it.

## 14. Automated test results

| Check | Result |
|---|---|
| Deterministic partitions at `2427b9d` (start; clean `bin`/`obj`, `--no-incremental`, memory watchdog, UI with `--blame-hang`) | **6,540 / 6,540**, 0 skipped: Domain 495, Application 1,521, Infrastructure 1,469, Documents 640, Ui mixed 2,142; run alone `OperationsCalendarViewTests` 35, `Phase15ERealWindowLayoutTests` 162, `RealWindowStartupTimingTests` 2, `SpellCheckNavigationLifecycleTests` 7, `Phase21DMathEditorWindowTests` 4, `Phase33PreviewWindowTests` 63 |
| Deterministic partitions after 37E-1 and the 37E-2 documentation | **6,548 / 6,548**, 0 skipped (Application 1,529: +8 simulation tests); other partitions as above (§14a) |
| Deterministic partitions at `69d70a7` (D-1; clean build) | **6,560 / 6,560**, 0 skipped: Domain 495, Application 1,531 (+2), Infrastructure 1,469, Documents 640, Ui mixed 2,152 (+10); run alone 35 + 162 + 2 + 7 + 4 + 63; guards 31/31 inside Infrastructure; build 0 warnings / 0 errors; peak sampled test-host RSS 430 MB (§25) |
| Repository guards | 31 / 31 |
| Clean Release build | 0 warnings, 0 errors (`bin`/`obj` deleted, `--no-incremental`); `tools/perf-benchmarks` 0 / 0 |
| Requested areas | Weather retrieval/cache, Dashboard, Calendar and PDF Weather, pricing retention, Personal Sync, PDF/History immutability, packaging (`ReleaseContent*`, `RealReleaseZip`) — all inside the partitions above, all pass |
| `RealReleaseZip` against the 37E ZIP | Passed; a bogus path fails |
| Live tests (`LiveSmoke`, NWS and official sites) | **Not run** (opt-in; no live request made in this phase) |

### 14a. Close-out run

Final tree (37E-1 committed, this documentation in the working tree), 2026-11-28 09:15–09:17 ET: `bin`/`obj` deleted,
`dotnet build CarowindsPassdown.slnx -c Release --no-incremental` 0 warnings / 0 errors, then every partition with
`--no-build`: Domain 495, Application 1,529, Infrastructure 1,469 (including the 31 guards and the documentation link
checks), Documents 640, Ui mixed 2,142, run alone 35 + 162 + 2 + 7 + 4 + 63 — **6,548 passed, 0 failed, 0 skipped**.
3 GB watchdog never fired; peak sampled test-host RSS 660 MB (484 MB in the starting run).

## 15. Windows LOCALTEST package

| | RC15-Phase37E-LOCALTEST |
|---|---|
| Source commit | `8921324` (marker `LOCAL-ONLY-TEST-BUILD.txt` names `892132482223d798c634f54fc3d57a3a8ce8d9bf`) |
| Version shown in About | `0.1.0-rc.15-LOCALTEST.phase37e` (informational `0.1.0-rc.15-LOCALTEST.phase37e+8921324…`; source `<Version>` `0.1.0-rc.14`) |
| ZIP | `artifacts/local-test/RC15-Phase37E-LOCALTEST/LOCAL-ONLY-RC15-Phase37E-LOCALTEST-win-x64.zip` (git-ignored) |
| Files / bytes | 308 files; 76,538,458 bytes (170,501,226 uncompressed) |
| SHA-256 | `00e623b5534009bddb42565172df03f208f8a827e83f1416f805ef275ec3594f` |
| Command | `Build-PortableRelease.ps1 -LocalTestLabel RC15-Phase37E-LOCALTEST -LocalTestVersion 0.1.0-rc.15-LOCALTEST.phase37e` (PowerShell 7.6.10 from an earlier session's scratch folder; nothing downloaded) |
| Package validation | Script content validation passed for the staging folder and the ZIP (308 files); a listing scan found no database, WAL/SHM, draft, weather cache, preference file, key, signature, update manifest, PDB, PDF, fixture or pricing-harness file |
| Real-ZIP test | `CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip` passed |
| Earlier artifacts | All 15,899 files under `artifacts/` hashed before the build and re-hashed after: byte-identical (RC.14 and every earlier package, including both harness ZIPs) |

**Superseded** by the D-1 package below (it lacks the Calendar wording). Kept unchanged.

| | RC15-Phase37E2-LOCALTEST (use this) |
|---|---|
| Source commit | `69d70a7` (marker names `69d70a7b44cc5e03befe3d4b2f9a9bf027dc436f`) |
| Version shown in About | `0.1.0-rc.15-LOCALTEST.phase37e2` (source `<Version>` `0.1.0-rc.14`) |
| ZIP | `artifacts/local-test/RC15-Phase37E2-LOCALTEST/LOCAL-ONLY-RC15-Phase37E2-LOCALTEST-win-x64.zip` (git-ignored) |
| Files / bytes | 308 files; 76,540,554 bytes (170,505,324 uncompressed) |
| SHA-256 | `15608e4736a233454bbd6a13eb123529ac066d4b5f617a98552af1955a6532a1` |
| Command | `Build-PortableRelease.ps1 -LocalTestLabel RC15-Phase37E2-LOCALTEST -LocalTestVersion 0.1.0-rc.15-LOCALTEST.phase37e2` |
| Package validation | Script content validation passed for the staging folder and the ZIP (308 files); listing scan: no database, WAL/SHM, draft, weather cache, preference file, key, signature, update manifest, PDB, PDF, fixture or pricing-harness file |
| Real-ZIP test | `RealReleaseZip` passed |
| Earlier artifacts | All 16,465 files under `artifacts/` (including the first 37E package) re-hashed after the build: byte-identical |

Neither package is signed, has an update manifest or is published.

## 16. Windows acceptance checklist

[releases/rc15-phase37e-integrated-localtest-windows-checklist.md](releases/rc15-phase37e-integrated-localtest-windows-checklist.md)
— sections A Startup/Settings, B Dashboard, C Calendar, D Passdown, E PDFs (incl. E.9 preview freshness and E.11
high-DPI printing), F Pricing retention (review only, no reseed), G Personal Sync, H Stability. Status: **NOT EXECUTED**.

## 17. Release readiness classification

| Area | Classification |
|---|---|
| Data corruption/loss, draft persistence, pricing verification, Weather date/alert safety, PDF creation/printing, sync/History, updater verification, packaging security | **No blocker found** |
| Calendar retained-price wording (D-1) | Owner-approved; implemented locally (§25); awaiting Windows (checklist C.11) |
| Preview freshness after weather arrives (D-2) | Needs decision |
| High-DPI Windows printing observation (D-3) | Needs decision (observation only) |
| Integrated Windows acceptance of this package (D-4) | Needs decision / evidence gap |
| Optional UI refinements, Phase 36E, TD-02 runner, seasonal themes, Personal Tasks/Issue Tracking | Deferred |

## 18. Blockers

None confirmed. Pending evidence (not blockers by finding, but required before publication): the integrated Windows
run of this package, and — at release preparation — the RC.14 → RC.15 updater test (§21).

## 19. Items requiring owner decisions

- **D-1 Calendar retained price — approved 2026-11-28, implemented in §25.** Proposed smallest change (Calendar presentation only, no layout, no new control):
  carry the candidate product's verification state into `OperationsCalendarDailyAdmissionPrice` and, when it is
  `PreviouslyVerified`, show "`$49.00` · last verified Nov 27" with the accessible name "Daily ticket price: $49.00, last
  verified November 27, availability not confirmed" (optionally the same for `Stale`: "· could not be rechecked"), plus
  focused presentation tests. Alternatives: accept as is for RC.15 and record it as a known limitation. Recommended:
  implement before RC.15 (it is the one surface that can read as "on sale").
- **D-2 Preview freshness.** Options: (a) accept and add a release-notes line ("Print and Save use the latest weather;
  the preview updates after printing"); (b) narrow change: when the monitor's snapshot would change the captured outlook
  for the displayed date, call the existing `Preview.NotifyContentChanged()` (coalesced, no new timer), with regression
  tests for "weather arrives → preview regenerates once" and "unchanged weather → no regeneration". Recommended: (a) for
  RC.15 — output is never older than the preview, and (b) adds a subscription the 37F design deliberately avoided.
- **D-3 High-DPI printing.** Run checklist E.11; no change unless memory fails to return after printing.
- **D-4 Windows acceptance.** Run the integrated checklist with `RC15-Phase37E2-LOCALTEST` on `CarowindsTestB`, including F.2–F.7 (the pricing screen and
  PDF checks the harness PASS does not cover).

## 20. Deferred work

Optional UI refinements; Phase 36E performance investigation (no measured regression); TD-02 test-runner tooling;
seasonal themes; Personal Tasks/Issue Tracking; Phase 36D (cancelled). Weather in finalized History (needs a new
persisted contract). A shared cross-device weather cache (excluded by the brief and not needed, §10).

## 21. RC.15 updater test plan

The updater, manifest format, signing tool and trusted key are unchanged since RC.14 (no diff under
`Carowinds.Passdown.Updater`, `Infrastructure/Updates`, `tools/update-signing`). The RC.13 → RC.14 owner success does
**not** prove RC.14 → RC.15. At release preparation (separately authorized), in order:

1. Bump `<Version>` to `0.1.0-rc.15`; update bundled release notes and the tester guide; clean build and full suite.
2. Build with `Build-PortableRelease.ps1` (no `-LocalTestLabel`) from a clean detached worktree of the release commit;
   the frozen-release guard must pass; re-hash every earlier artifact before and after.
3. Sign with `tools/update-signing` using the established key (outside the repository; never printed or copied):
   manifest format 1, package name/size/SHA-256 equal to the ZIP, every per-file size and SHA-256 recomputed from a fresh
   extraction (0 mismatches).
4. `verify` the signature against the current `appsettings.json` **and** RC.14's own `appsettings.json` from `3d603f0`
   (so RC.14 trusts it); RC.13's from `d784c60` if RC.13 → RC.15 is to be supported (the key is unchanged, so it
   should verify — confirm, do not assume).
5. Tamper checks: altered version text → `InvalidSignature`; altered package SHA-256 → `InvalidSignature`; an altered
   file inside the ZIP → per-file hash refusal; the updater must refuse each and leave the installation untouched.
6. Version ordering: `0.1.0-rc.15` > `0.1.0-rc.14` > `0.1.0-rc.13`, same channel, installable (unit tests). Note a
   LOCALTEST identity (`0.1.0-rc.15-LOCALTEST.*`) sorts **above** `0.1.0-rc.15`, so the in-app update must be tested
   from a real RC.14 installation, not from any LOCALTEST build.
7. Windows (disposable account): install the published RC.14 ZIP, create a synthetic draft, set Light/Dark, PDF style
   and Include Weather Outlook **off**, enable disposable sync; then Check for Updates → RC.15 offered, no signature
   warning → install → relaunch shows `0.1.0-rc.15`; draft, settings, the Weather Outlook preference (still off) and
   sync settings preserved; schema still 18 (no migration expected); History opens.
8. Rollback/error: interrupt or refuse a tampered package → RC.14 keeps running; `pre-update-…` backup present;
   returning to the RC.14 ZIP with the same data folder works (no schema change).
9. Public assets (at publication only, separately authorized): upload ZIP, manifest and signature; download them back
   and re-verify hashes and signature; never replace an earlier asset.

## 22. Product specification review status

Updated (by D-1 only). `AGENT_BUILD_SPEC.md` §7.5 ("Previously verified date-specific prices") now names the Calendar's
Daily ticket line, and §9.2 states its wording, citing owner decision D-1 (2026-11-28) and this record. §6.6, §17.3 and
§22 match the source reviewed; D-2 would need a specification line only if the owner approves a change.

## 23. Architecture documentation review status

Updated. `architecture/desktop-ui.md`: the D-1 Calendar wording and its accessible name (37E-3). `architecture/testing-and-performance.md`: one sentence on the Phase 37E request-volume simulation tests and the measured
request rate. `docs/README.md`: index entries for this record, its checklist and the (previously unindexed) pre-RC.15
pricing-retention record. Dependencies, DI, state ownership, data contracts, retrieval, persistence, sync, PDF,
lifecycle, threading, configuration, security, packaging and retired components: reviewed — no change (D-1 adds one
optional record member and one presentation property; no new dependency, registration, state owner, subscription or
persisted field).

## 24. Final go/no-go recommendation

**No-go for RC.15 publication today; conditional go for RC.15 release preparation** when: (1) the integrated checklist
is run on Windows with `RC15-Phase37E2-LOCALTEST` with no blocking failure, including C.11 (D-1 wording), F.2–F.7, E.9
and E.11; (2) the owner settles D-2 (recommended: accept with a release-notes line); (3) RC.15 preparation and the
updater plan (§21) are separately authorized.

## Validation

| Check | Result |
|---|---|
| Deterministic partitions (`build-and-test` skill) | 6,560 / 6,560 passed, 0 skipped at `69d70a7` (§14) |
| Clean Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — integrated checklist awaiting the owner |

## Documentation maintenance

**Product specification:** Updated

**Architecture documentation:** Updated

(See §22 and §23.)

## Known limitations and follow-ups

- All measurements are from the macOS development host; Windows timing, DPI, Narrator and printing are unverified.
- The NWS simulation models provider behaviour; it is not a live measurement. No live NWS or official request was made.
- Personal Sync was validated by the automated suites with temporary folders; a Windows disposable-folder run is in §G.
- D-2 to D-4 open; D-1 implemented, awaiting Windows.
- D-1 leaves the Calendar's `Stale` price wording unchanged (plain amount, as since Phase 13D); the D-1 wording was not
  rendered in a visual fixture (it relies on the panel's existing `TextWrapping="Wrap"`), so its on-screen fit is first
  checked on Windows (C.11).
- The pre-RC.15 pricing record's limitation 3 (Calendar without a qualifier) is superseded by §25 for the previously
  verified state; that historical record is not edited.

## 25. D-1 implementation (owner-approved 2026-11-28)

| | |
|---|---|
| Authority | Owner reply "Yes, implement D-1" to the §19 proposal |
| Commit | `69d70a7` |
| Application | `OperationsCalendarDailyAdmissionPrice` gains an optional last member `AssociateVerificationInfo? Verification` (default null; existing constructions unchanged); `OperationsCalendarService.BuildDailyAdmissionPrice` passes the shown (cheapest) product's own `Verification` |
| Desktop | `OperationsCalendarPresentation`: for `Available` + `PreviouslyVerified`, the text becomes "`$49.00` · last verified Nov 27" ("Starting from `$49.00` · last verified Nov 27" for a floor price; "· previously verified" without a time), using the original verification instant's **Eastern** date. New `DailyAdmissionPriceAccessibleText`: "Daily ticket price: $49.00, last verified November 27, availability not confirmed"; for every other state it equals the previous automation name ("Daily ticket price: …") |
| View | `OperationsCalendarView.axaml`: the Daily ticket value's `AutomationProperties.Name` binds `Presentation.DailyAdmissionPriceAccessibleText` instead of a `StringFormat` over the visible text. No layout, style or control change |
| Unchanged | Verified, Stale, NotCheckedYet, NotPublished, Unavailable texts; Products & Pricing, Dashboard, PDF; schema, sync, drafts, snapshots; no request, subscription or timer |
| Tests (12) | Ui `OperationsCalendarPresentationTests` +10: previously verified text and accessible name; late-evening instant uses the Eastern date (Nov 28 03:45 UTC → "Nov 27"); Starting-from keeps its qualifier; no time → "previously verified"; Verified/Stale/NotCheckedYet unchanged (theory); no verification unchanged; Not Published / Not checked yet accessible names unchanged; markup binds the new accessible name. Application `PostClosingPricingRetentionTests` +2: through the real registration and Calendar service, after closing the snapshot carries `PreviouslyVerified` with the 3:10 PM verification time; during sales it carries `Verified` |
| Validation | Full suite 6,560 / 0 / 0, clean Release build 0 / 0, guards 31/31 (§14); `RC15-Phase37E2-LOCALTEST` built from `69d70a7` (§15) |
