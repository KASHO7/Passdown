# RC15 local Windows test candidate — Phase 37D Calendar Weather and Settings

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged.

Phase 37D adds a compact **Weather** group to the Calendar's selected-date panel (with an expandable hourly forecast)
and the **Include Weather Outlook by default** checkbox to Settings → Passdown Defaults — the same setting as the
Passdown's **Weather Outlook** checkbox (Phase 37F). The Dashboard Weather card and the PDFs are unchanged.

Record: [phase37d-calendar-weather-settings-integration.md](../phase37d-calendar-weather-settings-integration.md).

| | RC15-Phase37D-LOCALTEST |
|---|---|
| Source commit | PENDING (recorded when the package is built) |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37d` |
| Package | `artifacts/local-test/RC15-Phase37D-LOCALTEST/LOCAL-ONLY-RC15-Phase37D-LOCALTEST-win-x64.zip` |
| ZIP size / files | PENDING |
| ZIP SHA-256 | PENDING |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37D-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | PENDING |

The source `<Version>` is still `0.1.0-rc.14` (not bumped). Database schema 18, no migration. Personal Sync, the draft
format and finalized History are unchanged.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up Passdown data. Setup is in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation". Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest`.
- Do not run `Enable Automatic Refresh.cmd`, and do not install any update from this build.
- Settings → About shows `0.1.0-rc.15-LOCALTEST.phase37d`.
- The weather shown is **real NWS forecast data** for the park area. Weather is informational only; it must not drive
  any real operational decision during the test.

## Manual checklist

Mark each item **PASS / FAIL / NOT EXECUTED**, with a note or screenshot (Windows+Shift+S) for anything odd. Record
once at the top of the report: PC model, Windows 11 build, display scaling, local time of the test, and the park hours
of each date tested.

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

### 1. Calendar — today and future dates
1.1 Open the app and wait until the Dashboard Weather card shows weather (about 15 seconds on first launch). Open
**Calendar**. With **today** selected, the panel on the right shows **Weather** below Park information (Park status,
hours, Daily ticket, Shows) and above Pass Perks.

1.2 Today with park hours not yet ended: a condition with a small line icon, a line such as "Park hours, 10 AM–10 PM"
(or "Rest of park hours, 1 PM–10 PM" once open), **Temperature**, **Rain**, **High / low**, "Hourly forecast (N
hours)", and a small line "National Weather Service forecast · issued … · updated …".

1.3 Compare with `https://forecast.weather.gov` (Carowinds area, hourly view): temperatures and rain chances match the
hourly forecast for those hours. Nothing is labelled "now", "observed" or "current".

1.4 Select **tomorrow** and two more dates within the next 6 days: each shows its own forecast and its own park hours;
the values change with the date and never repeat today's. Watch the Dashboard card or Task Manager: selecting dates
causes no weather loading or delay.

### 2. Operating-hours forecast
2.1 On an open date the scope line matches the park hours shown in Park information, and the hourly list starts at
opening (or the current hour once open) and ends at closing — no early-morning or late-night hours.

2.2 If a date with hours past midnight exists (or set a manual override such as 6 PM–1 AM on a test date), the scope
says "(closes after midnight)" and the hourly list continues to 12 AM. Undo any override afterwards.

2.3 If today's park hours have ended, the scope says "Park hours have ended · forecast for the rest of the day".

### 3. Expanded hourly forecast
3.1 Click **Hourly forecast (N hours)**: rows show time (Eastern), icon, temperature, condition, wind when given and
rain chance. A value NWS did not give shows "—", never 0.

3.2 Collapse it, select another date: it stays collapsed (or stays expanded if you left it expanded). It is collapsed
again after restarting the app.

### 4. Dates outside the forecast, past dates, closed dates
4.1 Select a date **8 or more days ahead**: "No NWS forecast this far ahead. The current forecast runs through …".
Around 7 days ahead you may see day-forecast values with "No hourly forecast for the park hours yet …".

4.2 Select **yesterday** or any past date: "Past date. NWS forecasts cover today and upcoming days only …" — no values.

4.3 Select a **closed** date within the next week: "Park closed · general forecast for the day"; nothing says park
hours. A date whose hours are not checked says "Park hours not confirmed · general forecast for the day".

4.4 If an NWS alert is active (Dashboard shows one), today's Weather shows "NWS alert: … · until …" and "Current alert
details are on the Dashboard."; a date after the alert ends shows no alert line.

### 5. Light and Dark, window widths
5.1 Settings → Appearance → Light, then Dark: the Weather group, icon, hourly rows and the "may be out of date" line
(if seen) are readable in both.

5.2 Resize to about **960 px** wide (narrow) and about **1280 px** (medium): the Weather group stays inside the panel,
nothing is cut off, the page does not scroll sideways. Check at 100 %, 125 % and 150 % display scaling.

### 6. Keyboard and screen reader
6.1 Tab into the selected-date panel: the "Hourly forecast" disclosure is reachable; Space or Enter expands and
collapses it.

6.2 Narrator: the Weather group is read as a sentence starting "Weather for <day>, National Weather Service forecast";
each hourly row reads its time, temperature, condition and rain chance; the disclosure reads "Hourly forecast" with its
expanded/collapsed state.

### 7. Calendar responsiveness and day cells
7.1 Step through 30+ days quickly with the › button and change months back and forth: as fast as RC.14; the Weather
group follows the selected date without flicker of another date's values.

7.2 No day cell shows any weather (no icon, temperature, rain or badge). Day cells look and size exactly as before.

7.3 The three **Pass Perks** markers (qualifying visit, redemption, booking deadline), their counts, the legend, "Show
Perks" and the "Valid all <month>" summary are unchanged.

### 8. Settings checkbox
8.1 Settings → **Passdown Defaults**: below the default sections, **PDF Weather Outlook** with the checkbox **Include
Weather Outlook by default**, checked on a fresh test account, with the description "Include the Weather Outlook in
Passdown PDFs when a valid forecast is available." It looks like the other checkboxes in that section; there is no new
Settings section.

8.2 Settings → Passdown sync: uncheck it in Settings, open Passdown → Overview → Include in Passdown: **Weather
Outlook** is unchecked and the preview has no weather. Check it in Settings: the Passdown checkbox is checked again and
the preview shows weather.

8.3 Passdown → Settings sync: uncheck **Weather Outlook** in the Passdown, open Settings: the Settings checkbox is
unchecked. Check it again in the Passdown: Settings shows it checked.

8.4 Restart: set it off, close and reopen the app: both checkboxes are off and the first preview has no weather. Set
it on and restart: both on.

8.5 Keyboard and Narrator: Tab reaches the Settings checkbox, Space toggles it, Narrator reads "Include Weather Outlook
by default" and its state.

### 9. PDF preview and output
9.1 With it on: preview, Print (Microsoft Print to PDF), Save PDF and Create PDF show the weather as in Phase 37F3, in
Classic, Modern and Compact. With it off: none has weather and the header has no gap.

9.2 Changing the checkbox in either place never marks the date's draft as changed.

### 10. Offline behaviour
10.1 Disconnect the network, quit and reopen the app. The Calendar still opens and navigates normally. Within three
hours of the last update it shows the cached forecast; later it shows the forecast marked "may be out of date"; with no
cache it says "Weather unavailable …". Reconnect: within a few minutes the Calendar values update without selecting
the date again.

### 11. No duplicate weather requests
11.1 Optional (Resource Monitor → Network, or a proxy if you use one): browsing the Calendar for a few minutes adds no
connections to `api.weather.gov` beyond the background checks (alerts about every 5 minutes).

### 12. No impact on printing or Personal Sync
12.1 Print a Passdown to a real printer if convenient: unchanged from Phase 37F3.

12.2 Personal Sync (disposable folder only): neither checkbox nor any weather appears in the sync folder's files, and
the setting does not change on another test device.

## Report back

Send the PASS / FAIL / NOT EXECUTED list with notes, and screenshots of the Calendar Weather group (today, expanded
hourly, a closed date, a past date) in Light and Dark, and of Settings → Passdown Defaults. Windows validation is not
recorded as passed until the owner reports it.
