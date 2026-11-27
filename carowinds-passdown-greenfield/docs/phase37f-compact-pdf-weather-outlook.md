# Phase 37F — Compact PDF Weather Outlook

Status: implemented locally / awaiting Windows validation (local test candidate `RC15-Phase37F2-LOCALTEST`, which supersedes `RC15-Phase37F-LOCALTEST`) / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `e3353e4` (Phase 37C-3 documentation) |
| Ending commits | `9d5d282` (37F-1 Application), `57e3d82` (37F-2 Documents), `7ddc5f4` (37F-3 Desktop, guards, tools) `a18f8b3` (37F-4 documentation), `51cf141` (37F-5 first package record); follow-up commits listed in the follow-up section |
| Reviewed source baseline | `7ddc5f4` (tests, builds and renders ran on this source, then uncommitted and identical; the Windows candidate was built from `a18f8b3`, same production source) |
| Dirty-tree caveats | Clean at start |
| Authorization | Owner brief "Carowinds Operations — Phase 37F: Compact, Park-Hours-Aware PDF Weather Outlook" (2026-11-27), which approves a compact informational PDF Weather Outlook without alerts and withdraws the mock-up's PDF alert example |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` `0.1.0-rc.14`; RC.15 not prepared |
| Database schema / sync formats | Schema 18; sync envelope 1; draft payload 4; finalized snapshot 2 — unchanged. Weather cache file format 1 — unchanged |

## Milestone identifier

Phase 37A planned 37D (Calendar selected-date weather) and 37E (Windows reliability and readiness); neither has a
record, an implementation or an authorization in the repository. The owner's brief names this work 37F, and no `37F`
record existed, so the identifier is used as given without overwriting anything. 37D and 37E remain unstarted.

## Scope

A compact weather forecast summary in the existing header of the three Associate Passdown PDF templates (Classic,
Modern, Compact), for the selected date's park operating hours. Not in scope and not done: PDF alerts of any kind,
Calendar weather (37D), Dashboard changes, finalization or History changes, Personal Sync, draft or schema changes,
pricing, Pass Perks, official retrieval, new dependencies, version bump, push, tag or publication.

## Owner-approved design and visual reference

- Reference: `weather_outlook_single_day_mockup.html` supplied by the owner (outside the repository, in the owner's
  `Passdown-Redesign` folder), SHA-256 `9bf10e371463a38ed9e2beeee1de82599f94717a9127392a07d75dda0492ac6c`. Read in full;
  not copied into the repository. Its sample temperatures, percentages and times were not used anywhere in code.
- Kept: Weather in existing header whitespace; Classic at the right of the park-hours block with a small WEATHER label,
  icon and temperature, a condition/rain line and a small NWS line; Modern opposite the hours in the top band, right
  aligned, no card; Compact beside the operating strip with tight type.
- Removed per the brief: the "Active Alert" state (`alert-inline`, "▲ … Advisory … til …"). No alert data reaches
  the Documents project (guarded).
- Deliberate differences from the mock-up: the temperature is the range over the park hours ("63–74°F"), not an
  opening → closing arrow or a daily high (the brief asks for the operating-hours range); rain wording is "Rain chance
  up to N%" / "<Showers|Storms|Rain> possible after <time> (up to N%)" rather than "0% precip" / "Showers 6p (40%)",
  so the maximum hourly probability is never read as a whole-day chance and a time is never stated as certain; in
  Modern the four metadata columns narrow beside the outlook instead of the band growing taller (zero added height).

## Implementation

### 1. Forecast-data selection — `Application/Weather/OperatingHoursForecast.cs`

`OperatingHoursForecastSelector.Select(snapshot, operatingDate, effectiveHours, now)` is pure and reads only an
already-cached `WeatherSnapshot`:

| Rule | Result |
|---|---|
| Effective hours not Open, a missing time, or opening = closing | Omitted (`NoParkHours`). No alternative period is substituted — the safer choice, since the spec ties the forecast to park hours and a closed or unchecked day has no operating period to describe |
| No snapshot / no hourly forecast (never retrieved, offline with no cache, older than 24 h) | Omitted (`NoForecast`) |
| Hourly forecast retrieved more than 3 h before `now` (`MaxForecastAge` = the app's existing `ForecastStaleAfter`) | Omitted (`ForecastTooOld`) |
| `now` ≥ closing (past dates, ended hours) | Omitted (`HoursEnded`) |
| No hourly period overlaps the hours (beyond the ~6.5-day NWS horizon) | Omitted (`OutsideForecast`) |
| The periods do not cover every remaining operating minute (horizon ends inside the hours, or a gap) | Omitted (`IncompleteCoverage`) |
| Otherwise | The periods overlapping [max(opening, now), closing), with the NWS `updateTime` (issue time) and the retrieval time |

Times are `America/New_York` wall times converted per date (EST/EDT); closing at or before opening means the next
day (evening events, midnight closings); a spring-forward wall time moves an hour later; a repeated fall-back time
uses standard time. Partial hours (10:30 opening) include the hour that contains them.

### 2. Summary wording — `Documents/Associate/PdfWeatherOutlook.cs`

`PdfWeatherOutlookBuilder.Build(forecast, generatedAt)` → `PdfWeatherOutlook` (operating date, icon, and display
strings only, so value equality is meaningful):

- **Temperature:** lowest–highest hourly temperature ("63–74°F"; "72°F" when equal; "−12° to 5°F" with a negative
  end). Omitted if any covered hour lacks a temperature.
- **Condition:** the NWS short forecast covering the most time (ties → earliest), in sentence case; a short form for
  condensed layouts is the condition itself when ≤ 14 characters, else its category ("Storms", "Showers", "Rain",
  "Snow", "Wintry mix", "Fog"/"Haze"/"Smoke"/"Mist", "Partly cloudy", "Cloudy", "Sunny", "Clear", "Windy", "Breezy"),
  else none.
- **Rain:** only when every covered hour has a probability. All equal → "Rain chance N%"; otherwise "Rain chance up to
  N%". A time is named only when the first hour(s) are all below 30% (NWS "chance" wording starts there) and a later
  hour reaches 30%: "<Showers|Storms|Snow|Rain> possible after 6 PM (up to 40%)". Short form "Rain up to N%".
- **Source:** "NWS forecast 6:05 AM" from the hourly forecast's NWS issue time (date added when it was issued on
  another day); "NWS · retrieved <time>" when NWS gave none. When the park had already opened: "· covers 3:12 PM–8 PM".
  The document's print time is never used as the forecast time.
- No outlook when neither a temperature nor a condition is known. No wording ever mentions alerts, watches,
  warnings or advisories (tested).

### 3. Template layout — `Templates/PdfWeatherOutlookComponent.cs` and the three renderers

Only when `model.Weather` is set, each renderer wraps the operations rows beside the outlook in a row with a fixed
right-hand column; the rows keep their own spacing and Early Ride keeps the full width below. With no weather the
original code path runs (bytes identical to the same document without weather, tested).

| Template | Placement | Column | Height budget (fixed heights of the rows beside it) | Levels, most → least detailed |
|---|---|---|---|---|
| Classic | Right of PARK HOURS caption → Pre-solds; beside caption → Avenue only when Private Buyout details print (they keep full width) | 220 pt, 16 pt gap | Standard ≈ 88 pt, Tight ≈ 85 pt (≈ 60 pt with buyout details) | Full: WEATHER / icon + range / condition · rain / NWS forecast · split full (condition and rain on separate lines) · condensed (short condition · short rain / NWS) · minimal (icon range short-condition / NWS) |
| Modern | Opposite the hours headline, beside the headline and the four-column metadata row | 196 pt, 16 pt gap | ≈ 51 pt (≈ 48 pt Tight) | Full (condition · rain · NWS forecast on one line) · four-line full · condensed (short forms on one line) · minimal |
| Compact | Right of the HOURS/NG/AVE and ATTENDANCE/PRE-SOLDS lines | 204 pt, 12 pt gap | ≈ 23 pt (two lines) | WEATHER icon range condition / rain · NWS · condensed with short forms · minimal without label |

`PdfWeatherOutlookComponent` is a QuestPDF `IDynamicComponent`: it measures each level with
`DynamicContext.CreateElement` (QuestPDF's own layout, the real natural size) and draws the first whose width fits
the column and whose height fits the budget; when none fits it draws nothing and takes no space. Type sizes never
shrink. Every level includes the NWS source and time. Per document it resolves levels once, remembers the chosen
level for the available space, and parses each icon once (an instance serves one generation and is never shared).
Typography reuses each template's existing sizes and ink/muted greys (Classic label 9 pt like PARK HOURS, headline
14 pt, secondary 9 pt, source 7.5 pt; Modern label 7.25 pt like its metadata labels, source 7.25 pt; Compact 6.75 /
10 / 7.5 / 6.75 pt). No accent colour (tested).

### 4. Icons

`PdfWeatherIcons`: nine local SVG drawings on a 24-unit grid, one 1.8 stroke weight, round caps — sun, moon, partly
cloudy (day/night), cloudy, rain, thunderstorm, snow, fog — in the template's ink with white fills only to mask what a
cloud covers. Drawn as vectors by QuestPDF's SVG support (no image XObjects, tested), inline beside the temperature;
no emoji, font or asset file. A condition that cannot be classified prints no icon.

### 5. Snapshot consistency and Desktop capture

- `Presentation/PassdownWeatherOutlookSource.For(snapshot, workspace, now)` = selector + builder, pure.
- `AssociateViewModel` takes an optional `IWeatherMonitor` (DI supplies the app's one monitor). It reads only
  `IWeatherMonitor.Current` — an immutable snapshot — when it captures a preview/Print/Save request
  (`PassdownPdfRequest.Weather`) and when Create PDF runs; it never subscribes, refreshes or starts the monitor
  (guarded). The outlook is built for the captured workspace's own date and effective hours, and
  `AssociatePassdownDocumentModelBuilder` drops an outlook whose `OperatingDate` differs from the workspace's.
- Generation receives that one value through the existing export service (`GeneratePdf(…, weather)`), so a weather
  refresh during generation cannot change the document (tested).
- `PassdownPdfDocument.Weather` records what the bytes print. The preview reuses its document for Print/Save only when
  the editor version, date, template **and** outlook are unchanged; otherwise Print/Save regenerate, and the preview then
  shows exactly what was printed. A weather change alone does not schedule a preview refresh (no new subscription or
  timer); the next edit, date load, Print or Save picks it up.

### 6. PDF integrity, finalization and History

- Finalization (`FinalizedPassdownSnapshotBuilder.BuildProjection`) is unchanged and passes no weather; it runs
  automatically after the operating day (06:00 Eastern the next day), when no forecast for those hours could apply.
  Finalized snapshots and stored PDFs are unchanged and immutable; History opens the stored bytes with no network.
  So the finalized History PDF of a day does **not** carry the weather that the morning's printed Passdown showed
  (known limitation; adding it would need a new persisted contract, which the brief excludes).
- No weather in drafts, Personal Sync, finalized snapshots, Copy for Teams or SQLite (guarded).

## Missing and stale behaviour

Omitted entirely, with no reserved space and no placeholder, for every omission reason in §1, and whenever no level
fits. The PDF always generates. There is no "partial" weather beyond the per-field rule (a field missing for any
covered hour is left out; the source and time are always present when anything prints).

## Visual comparison (real QuestPDF output, PDFium renders; macOS, not Windows evidence)

`tools/perf-benchmarks pdf-weather 40 artifacts/design-review/Phase37F-PDF-WEATHER-RENDERS` (git-ignored) wrote the PDF
and a 2 px/pt PNG for each template × scenario: normal, rain later (12–8 PM), short hours (5–6 PM), evening (6–11 PM),
long condition, missing temperature, missing precipitation, missing forecast, Private Buyout, dense day; weather-free
counterparts for normal, buyout and dense; grayscale PNGs for normal and rain later. All 1 page; missing forecast is
byte-for-byte the weather-free size. Reviewed by eye: placement and hierarchy match the mock-up direction; no
overlap; icons legible; grayscale legible.

Automated layout evidence (`Phase37FPdfWeatherLayoutTests`, 114 cases, PdfPig on real PDFs):

| Check | Result |
|---|---|
| Vertical displacement of every other glyph (3 templates × 6 days incl. Private Buyout, dense, excessive multi-page, minimal × 5 outlooks) | **0** — every glyph of the weather-free PDF is at the same baseline with weather (the first non-header section does not move) |
| Page count | Equal with and without weather in all 90 cases |
| Text overlap | None (all word boxes on page 1) |
| Column / margin | All weather glyphs inside the template's right-hand column and the page margin |
| Level selection | Classic full; Modern condensed for typical text; Compact full two-line; long conditions fall back to short forms; an unfittable outlook prints nothing and changes nothing |
| Fonts / images / size | No new font family; no image objects; +3.6–4.2 KB per PDF |
| Determinism | Same outlook → identical bytes (timestamps normalized); another date's outlook → identical to no weather |

## Performance (macOS development host, Apple M2 Pro, .NET 10.0.11 — not Windows evidence)

| Measure | Result |
|---|---|
| Outlook build (selection + wording) per capture | 14–17 µs, 7.1 KB allocated |
| Associate PDF generation, NormalDay, warm (median of 40–60) | Classic 3.8 → 6.6 ms, Compact 3.3 → 5.6 ms, Modern 3.4 → 5.2 ms (+1.8–2.8 ms; before caching per document Modern was +6.6 ms). The icon accounts for < 1 ms |
| Weather-free preview, edit → page image (`preview 25`, two runs each, starting commit vs this phase) | busy page median 14.2 / 6.5 ms → 6.9 / 11.2 ms; 80-note document 21.9 / 20.6 → 21.4 / 20.4 ms — no change beyond run-to-run noise |
| PDF size | +3.6–4.2 KB with weather |

No network request, PDF renderer, warm-up, font, bitmap or printing change. Windows timing was not measured.

## Tests

| Project / class | New | Covers |
|---|---|---|
| Application `OperatingHoursForecastSelectorTests` | 23 | Correct date and hours; another date; short/long/evening windows; half-hour hours; midnight crossing and midnight closing; EST; fall-back night (7 real hours); spring-forward day; park already open (coverage from now); ended hours; past date; beyond horizon; horizon inside hours; gap; 3-hour age limit; no snapshot/offline; closed/unchecked/equal times never substituted; manual override hours; issue and retrieval times carried |
| Documents `PdfWeatherOutlookBuilderTests` | 33 | Stable; rain later; storms later; wet from start (no time); blip below threshold (no time); one-hour window; all-zero; missing precipitation; missing temperature; nothing known; dominant condition and ties; short forms; unknown condition; icons; extreme temperatures; covered-period wording; issue on another day; no issue time ("retrieved"); no alert words; value equality; sentence case |
| Documents `Phase37FPdfWeatherLayoutTests` | 114 | The layout evidence above |
| Ui `Phase37FPassdownWeatherIntegrationTests` | 10 | Preview prints the cached outlook for the selected date; no monitor/no snapshot/too old/offline → none, still generates; weather change → Print regenerates and preview shows the printed bytes; unchanged weather → Print and Save reuse the preview bytes; refresh during generation cannot change the document; date changes (tomorrow, beyond horizon, past); Create PDF; never refreshes, starts or subscribes; source is pure; composition supplies the monitor |
| Infrastructure `WeatherActivationGuardTests` (updated deliberately) | +3 (17) | Desktop weather files now include the Passdown capture files; Documents weather only in the eight listed files and never in `FinalizedPassdownSnapshotBuilder` or Copy for Teams; the PDF path names no alert type, monitor, provider, coordinator or HTTP; `AssociateViewModel` uses only `_weatherMonitor?.Current`. Replaces `Updater_AndDocuments_DoNotUseWeather` (Updater check kept) |

No existing assertion was removed or weakened. Live NWS tests remain opt-in; none was run.

## Validation

All results are from the macOS development host. None of it is Windows validation.

| Check | Result |
|---|---|
| Deterministic partitions (`build-and-test` skill) | PASS — 6,367 / 6,367, 0 skipped (6,184 + 183 new): Domain 488, Application 1,492, Infrastructure 1,426, Documents 631, Ui mixed 2,070, run-alone 30 + 158 + 2 + 7 + 4 + 59 (UI partitions with `--blame-hang`, no hang) |
| Repository guards | PASS — 31 / 31 (after the documentation was written; includes the phase-record format check) |
| Clean Release build | PASS — all `bin`/`obj` under `src`, `tests`, `tools` deleted, `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors. `tools/visual-fixtures` and `tools/perf-benchmarks`: 0 warnings, 0 errors |
| Windows local-test candidate | `artifacts/local-test/RC15-Phase37F-LOCALTEST/LOCAL-ONLY-RC15-Phase37F-LOCALTEST-win-x64.zip` from `a18f8b3` (production source identical to `7ddc5f4`; the later commit is documentation only), shown as `0.1.0-rc.15-LOCALTEST.phase37f`; 76,463,874 bytes, 308 files; SHA-256 `ad9c4defcd87f0ccb25df75cac37823707b4afa2ff7b740c61c25415dd2ddd5f`. `Build-PortableRelease.ps1 -LocalTestLabel RC15-Phase37F-LOCALTEST -LocalTestVersion 0.1.0-rc.15-LOCALTEST.phase37f` (PowerShell 7.6.10 .NET tool installed in the session scratch folder with the owner's approval; the latest package version would not install as a .NET tool); script content validation (folder and ZIP) passed; `RealReleaseZip` passed (a bogus path fails); marker names `a18f8b3`; no database, WAL/SHM, draft, weather cache, key, signature, update manifest, PDB or PDF in the ZIP; all 13,060 earlier artifact files re-hashed byte-identical |
| Windows validation | NOT EXECUTED — checklist [rc15-phase37f-localtest-windows-checklist.md](releases/rc15-phase37f-localtest-windows-checklist.md) |

During development a draft of the refresh-during-generation UI test hung (a held thread-pool generation released while
the test thread drove the quiet window synchronously — a harness race, not product behaviour); it was rewritten to
swap the weather inside the generation itself and no hang remains.

## Compatibility

Unchanged: schema 18 and migrations, sync envelope 1, draft payload 4, finalized snapshot 2, Personal Sync, finalized
History and stored PDFs, the PDF renderer, PDFium preview and printing, warm-up, embedded fonts, official retrieval,
pricing, Pass Perks, the frozen RC.14 artifacts, the version and the dependency graph. New behaviour: newly generated
Passdown PDFs may print the outlook. Rollback: revert the Phase 37F commits; nothing persisted depends on them.

## Documentation maintenance

`AGENT_BUILD_SPEC.md`: §6.6 status, a new "Passdown PDF Weather Outlook" subsection (content, no alerts, omission
rules, layout, one immutable summary per generation, no persistence) and the Exclusions paragraph (PDF output now
carries only that outlook); §15.4 content list; §22.8 and §22.13 acceptance criteria (§22.13 heading now "approved, not
yet released"). Authority: the owner's Phase 37F brief.

**Product specification:** Updated

`docs/architecture.md` (PDF Weather Outlook paragraph), `architecture/pdf-and-printing.md` (model, export overload,
placement, component, icons, finalization), `architecture/desktop-ui.md` (capture, no subscription, reuse rule),
`architecture/data-and-persistence.md` (PDF exception wording), `architecture/retrieval-and-refresh.md` (selector reads
the cache only), `architecture/testing-and-performance.md` (new tests and the `pdf-weather` benchmark);
`docs/README.md` index; `tools/perf-benchmarks/README.md`. Phase 37A–37C records unchanged (historical evidence).

**Architecture documentation:** Updated

## Follow-up — Include Weather Outlook option

Authorization: owner brief "Phase 37F Follow-up — Optional Weather Outlook Checkbox" (2026-11-27), restoring a
previously approved requirement the first Phase 37F brief omitted. The Weather Outlook itself is unchanged. Commits:
`14f973a` (37F-6 source and tests; reviewed source baseline), `829d219` (37F-7 docs), `10dc300` (37F-8 checklist
package details) and this record commit.

| Piece | Where | Role |
|---|---|---|
| `PassdownWeatherOutlookPreferences` / `IPassdownWeatherOutlookPreferenceStore` / in-memory store | `Application/Preferences/PassdownWeatherOutlookPreferences.cs` | Device preference `IncludeWeatherOutlook`, built-in default **on** |
| `FilePassdownWeatherOutlookPreferenceStore` | `Infrastructure/Preferences/` | `passdown-weather-outlook.json` in the local application-data folder, the shared atomic writer, nullable field so a missing value means on; unreadable → on. Registered as a singleton in `App.axaml.cs`. Not in drafts, Personal Sync, SQLite or finalized snapshots |
| `AssociateViewModel.IncludeWeatherOutlook` | Desktop | Loaded before the first date starts loading; preview/Print/Save captures report Waiting until it is known and Create PDF awaits it, so no document is built with the default when "off" is saved. A change calls `Preview.NotifyContentChanged()` (the existing leading-edge refresh, no new delay) and saves the preference; it never marks the draft unsaved. When off, `CaptureWeatherOutlook` returns null without reading the monitor |
| Check item "Include Weather Outlook" | `AssociateView.axaml`, Passdown **Actions** menu, directly after the PDF Style choices | `ToggleType="CheckBox"`, two-way bound, automation name "Include Weather Outlook" |

**Placement decision.** A visible checkbox on the Passdown action bar beside Create PDF was implemented first and
failed three existing Phase 35B/35D real-window tests at the 960 × 640 minimum window (the operating-date control was
squeezed to zero width) and changed the approved keyboard order. Rather than weaken those tests or the approved
toolbar, the option is a check item in the Actions menu next to the PDF style — the PDF actions — where it is
keyboard-reachable and leaves the toolbar unchanged.

**Consistency.** Each capture reads the option and the cached weather together into one immutable request, and the
preview's reuse check compares the outlook, so preview, Print, Save PDF and Create PDF always agree with the option in
force when the document was made. Finalized PDFs are unaffected (finalization never passes weather; tested).

Follow-up tests (all deterministic, no network):

| Project / class | New | Covers |
|---|---|---|
| Ui `Phase37FPassdownWeatherIntegrationTests` | +18 (28) | On by default with no save; off saves the device preference, refreshes the preview at once (one leading-edge generation, no extra pass), prints no weather, never saves the draft; back on restores the outlook; saved off restored after restart (first preview has no weather, nothing re-saved); off and on — Print, Save PDF and Create PDF match the preview; every template on and off; missing/stale forecast with the option on; off persists across date changes; off never reads the monitor or requests weather; slow preference load — preview waits, then uses the saved choice (on and off, one generation); markup: label, check type, two-way binding, automation name, position after PDF Style |
| Ui `Phase33PreviewWindowTests` (partial `.Phase37FWeatherOutlook`, run alone) | +2 | Real window at 1280 and 960 px: the check item in the Actions menu after PDF Style: Modern, both binding directions, nothing clipped |
| Infrastructure `FilePassdownWeatherOutlookPreferenceStoreTests` | +9 | Missing → on (no write); saved on/off survives a new instance (restart); unreadable/missing value → on; file holds only the choice; default path is the local application-data folder |
| Application `WeatherOutlookPreferenceBoundaryTests` | +7 | Draft payload, sync envelope, manifest, finalized snapshot, finalized sync payload, sync configuration and sync settings shapes (recursively) carry no weather member |
| Documents `Phase37FFinalizedPdfTests` | +3 | The finalized PDF equals the weather-free document for each template |
| Infrastructure `WeatherActivationGuardTests` | updated | `AssociateView.axaml` added to the Desktop weather file list |

The first draft of the UI tests stalled: the shared test helper elapsed only the newest preview delay, so with "off"
restored during construction it waited on the older one. The helper now elapses pending delays until the refresh
finishes (real timers elapse all). The same tests exposed a real startup ordering issue (the first capture used the
default before the saved choice loaded); fixed by the load-before-date ordering and capture gate above.

Follow-up validation (macOS development host; not Windows validation):

| Check | Result |
|---|---|
| Deterministic partitions | PASS — 6,406 / 6,406, 0 skipped (6,367 + 39 new): Domain 488, Application 1,499, Infrastructure 1,435, Documents 634, Ui mixed 2,088, run-alone 30 + 158 + 2 + 7 + 4 + 61 (UI partitions with `--blame-hang`, no hang) |
| Existing tests updated deliberately | `Phase35DShowsNotesPolishTests.ActionsMenu_KeepsEveryCommand_InOrder_WithItsShortcut` (exact header list now includes "Include Weather Outlook" after "PDF Style: Modern") and `Phase35BPassdownWorkspaceTests.ActionsMenu_HeadlessView_EveryItemIsNamed_AndTheMenuButtonIsKeyboardReachable` (item count 11 → 12; separators unchanged). Both stay exact inventories; nothing removed or loosened |
| Repository guards | PASS — 31 / 31 |
| Clean Release build | PASS — all `bin`/`obj` deleted, `--no-incremental`: 0 warnings, 0 errors; `tools/visual-fixtures` and `tools/perf-benchmarks` 0 / 0 |
| Windows local-test candidate | `artifacts/local-test/RC15-Phase37F2-LOCALTEST/LOCAL-ONLY-RC15-Phase37F2-LOCALTEST-win-x64.zip` from `829d219` (production source identical to `14f973a`), shown as `0.1.0-rc.15-LOCALTEST.phase37f2`; 76,472,064 bytes, 308 files; SHA-256 `fce0071103f22ab8f0a4174c9094606a241bbf0e9f2eb588602a63a14b139468`. Same packaging command with the new label and version (PowerShell 7.6.10 tool in scratch); content validation (folder and ZIP), `RealReleaseZip` (bogus path fails), marker and forbidden-content checks passed; all 13,626 earlier artifact files re-hashed byte-identical, so the first 37F ZIP was not overwritten. Supersedes `RC15-Phase37F-LOCALTEST` for testing |
| Windows validation | NOT EXECUTED |

## Known limitations

- **Windows validation pending**: Windows rendering, printing (colour and grayscale), DPI and timing.
- Finalized History PDFs carry no weather (finalization runs after the day; see §6).
- A weather change alone does not refresh a visible preview; Print/Save always use current weather and then show it.
- The Include Weather Outlook option is in the Actions menu, not on the action bar (see the follow-up's placement decision).
- In the first moments after start, before the device preference has loaded, Print/Save PDF report "still loading" like a date that is still loading.
- A forecast older than 3 hours is omitted, so a PDF made offline after a long gap has no weather.
- Rain timing is named only for a dry-then-wetter pattern crossing 30%; other changes (for example clearing later) are
  summarized by the dominant condition only.
- Modern's four metadata columns are narrower when the outlook prints (no wrapping found with synthetic data; very long
  values would wrap within the column).
- Condition categories cover NWS's common short-forecast words; an unrecognized long condition prints without an icon
  and, in condensed levels, without a condition.
