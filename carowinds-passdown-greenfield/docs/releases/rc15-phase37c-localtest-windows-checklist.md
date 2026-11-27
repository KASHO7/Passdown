# RC15 local Windows test candidate — Phase 37C Dashboard Weather card

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged: all 12,328
were re-hashed after this build.

Phase 37C adds **new network behaviour and new UI**:

- A **Weather** card at the top of the Dashboard's right-hand column, built to the owner-approved mock-up (D4).
- While the app is open it requests public weather from the **National Weather Service** (`api.weather.gov`) for two
  fixed points inside Carowinds (one in NC, one in SC). Alerts are checked about every 5 minutes. Nothing is
  requested while the app is closed, and the computer's location is never used.

Record: [phase37c-dashboard-weather-integration.md](../phase37c-dashboard-weather-integration.md).

| | RC15-Phase37C-LOCALTEST |
|---|---|
| Source commit | `39bbb5b` |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37c` |
| Package | `artifacts/local-test/RC15-Phase37C-LOCALTEST/LOCAL-ONLY-RC15-Phase37C-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,418,184 bytes (72.9 MB); 308 files |
| ZIP SHA-256 | `b03f76376068c58f3e3558621900a1c6b841e6bb83e0296dd6df978f1b284e43` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37C-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 6,184 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation (folder and ZIP, 308 files); `RealReleaseZip` passed (a bogus path fails); marker names `39bbb5b`; no database, WAL/SHM, draft, weather cache, key, signature or update-manifest file in the ZIP; all 12,328 earlier artifact files re-hashed byte-identical |

The source `<Version>` is still `0.1.0-rc.14` (not bumped). Database schema 18, no migration. Personal Sync format
and PDF output are unchanged. One new local file: `weather-cache.json` in the app's local data folder (never synced).

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up data. Setup is in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation". Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest`.
- Do not run `Enable Automatic Refresh.cmd`, and do not install any update from this build.
- Settings → About shows `0.1.0-rc.15-LOCALTEST.phase37c`.
- The weather shown is **real, live NWS data** for the park area (public information; nothing personal is sent).
  Compare it with `https://forecast.weather.gov` if useful. Weather is informational only; it must not drive any
  real operational decision during the test.

## Manual checklist

Mark each item **PASS / FAIL / NOT EXECUTED**, with a note or screenshot (Windows+Shift+S) for anything odd.
Record once at the top of the report:

- PC model
- Windows 11 build
- display scaling
- monitor resolution
- local time of the test and whether any NWS alert was in effect for Carowinds (see weather.gov)

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

### 1. Startup and first weather
1.1 The window opens as quickly as RC.14. Nothing waits for weather.

1.2 First launch in the test account: the Weather card says "Getting weather from the National Weather Service…"
and "Alert status not yet checked." Within about 15 seconds it shows weather.

1.3 Close and reopen the app. Weather appears immediately from the saved copy, before any new check.

### 2. Dashboard appearance (compare with the approved mock-up)
Reference images: `docs/design/phase37c-weather-mockup/` (the values in them are samples).

2.1 Weather is the **first card in the right-hand column**, above Daily Ticket Pricing, with a **teal** top line.
The gate card, Today's Associate Passdown and Shows & Entertainment are where they were in RC.14.

2.2 The title band reads "Weather" and "National Weather Service".

2.3 Pricing, Operational Notes and Pass Perks move down by about the card's height. Nothing overlaps, nothing is
cut off, and there is no sideways scrolling.

2.4 Light and Dark (Settings → Appearance): every weather text is readable in both, with no light box on Dark.

### 3. Narrow and medium widths
3.1 At the smallest window size (960 × 640) the card shows the same content with wrapped text. Nothing is cut off
and no button runs past the edge.

3.2 Drag the width between full screen and the minimum. The card reflows. It shows no loading flash and makes no
new request (no "Getting weather…").

3.3 At 125 % and 150 % display scaling: same checks as 3.1.

### 4. Current observations
4.1 The section label says "OBSERVED · " and an airport name, normally "CHARLOTTE AIRPORT". The line under the
temperature names the station code (normally KCLT), its distance from the park and the observation time in ET. It
never says the reading was taken at Carowinds.

4.2 Temperature in °F, a condition (for example "Cloudy") and wind ("Wind SW 9 mph", or "Wind calm") are plausible
against weather.gov's current conditions for Charlotte.

4.3 If the card shows another station (for example "ROCK HILL AIRPORT", KUZA), that station's name and code are used
everywhere, including the details note.

### 5. Forecast
5.1 Before about 6 PM: "NWS FORECAST · TODAY, <date>" with High and Low matching weather.gov's Today/Tonight.

5.2 The rain line says "Highest hourly rain chance, <open>–<close> park hours" using **today's park hours** on the
gate card, and "issued <time> ET". The percentage is plausible against weather.gov's hourly graph for those hours.

5.3 On a closed day (if the test day is one), it says "rest of today (park closed)".

5.4 After about 6 PM: "TONIGHT", "Today's high not in current forecast", and only the low. No other number
replaces the high.

### 6. NC/SC alerts
6.1 With no NWS alert in effect: the footer reads "No active NWS alerts · checked <time> ET", and the time updates
about every 5 minutes while the app is open.

6.2 **Only if an alert is in effect for the park area** (check weather.gov for Mecklenburg NC / York SC): an amber
strip with a warning triangle shows "N active NWS alert(s)" and the most severe event with "until <time> ET".
Weather details list every alert with severity, end time, "Applies to NC and SC portions" / "NC portion only" /
"SC portion only" and the issuing office. Mark NOT EXECUTED if no alert occurred.

6.3 An alert disappears from the card once its end time passes. It is never shown after that.

### 7. Expanded weather details
7.1 "Weather details" expands in place. It shows: today's short forecast, the station's humidity and gusts ("Not
reported" when none), the alert status line, the note that observations come from the airport station and not from
inside Carowinds, "Last updated <time> ET", **Refresh Weather**, and the informational-only note.

7.2 "NWS forecast on weather.gov" (and any alert's "weather.gov") opens `forecast.weather.gov` in the default browser
and nothing else.

7.3 "Hide weather details" collapses it. Expansion is forgotten after restarting the app.

### 8. Manual refresh
8.1 Select Refresh Weather right after weather appeared. The card says "Weather was checked recently; nothing new to
request yet. Next automatic check <time> ET." It does not claim it updated.

8.2 Wait more than a minute, then select it again. "Weather updated <time> ET." The footer check time changes.

8.3 While a refresh is running the button is disabled. Pressing quickly twice runs one refresh.

8.4 Check Online (page header) still checks only official park data. It does not change the weather card.

### 9. Offline and stale behaviour
9.1 Disconnect the network (Wi-Fi off) and select Refresh Weather after a minute: "Could not reach the National
Weather Service…" in amber. The last weather stays visible. Nothing else in the app is affected.

9.2 Leave the network off for about 20 minutes. The card says "May be out of date", "Weather could not be refreshed.
Showing the last NWS data received." and "Alert status unknown — alerts last checked <time> ET". It **never** says
"No active NWS alerts" while unverified. Ages appear ("(1 h 5 min ago)").

9.3 Reconnect. Within about 5 minutes the card returns to normal without restarting.

9.4 Optional: start the app offline in a fresh account (no saved weather): "Weather unavailable." with no numbers.

### 10. Keyboard and accessibility
10.1 Tab from Check Online: focus goes through the left column, then **Weather details**, then View Pricing. The
focus outline is visible on the toggle and on Refresh Weather. Space/Enter expands and collapses.

10.2 Narrator reads the card as "Weather" with a one-sentence summary, reads the toggle as "Weather details" / "Hide
weather details", and announces the refresh result.

10.3 With Windows "Animation effects" off, nothing in the card animates except the existing loading bar behaviour.

### 11. Normal Dashboard and app behaviour
11.1 Every existing Dashboard action works as in RC.14: Check Online, View in Calendar, Open History, Open Passdown,
Review Notes, View Shows, View Pricing, View Notes, Pass Perks Details, Source details, Run Deep Refresh.

11.2 Switching between Dashboard, Calendar, Passdown, Notes and History is as quick as RC.14. Returning to the
Dashboard shows weather at once, with no loading state and no new check.

11.3 The Calendar shows **no** weather in day cells or in the selected-date panel (Phase 37D is not part of this
build).

### 12. No weather activity when closed; no data impact
12.1 Close the app. In Task Manager no Carowinds process remains, and no weather request continues.

12.2 Create and edit a synthetic Passdown, wait for autosave, open the PDF preview and print to PDF or a printer:
works as in RC.14, and **no weather appears** in the draft, preview, PDF or History.

12.3 With Personal Sync pointed at the disposable folder, sync once: the sync folder contains **no weather file**.
`weather-cache.json` exists only in the app's local data folder.

## Reporting

Send the PASS/FAIL list, the details recorded at the top and screenshots of any FAIL. Owner acceptance is recorded
separately in the phase record. This candidate is never published.
