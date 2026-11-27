# RC15 local Windows test candidate — Phase 37F Compact PDF Weather Outlook

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged.

Phase 37F adds a small **Weather** summary to the Associate Passdown PDF (Classic, Modern and Compact): the forecast
for the selected date's park hours, from the weather the app already has (Phase 37C). It shows **no alerts**. The
Dashboard Weather card is unchanged and still shows alerts.

Record: [phase37f-compact-pdf-weather-outlook.md](../phase37f-compact-pdf-weather-outlook.md).

| | RC15-Phase37F-LOCALTEST |
|---|---|
| Source commit | Not built yet (awaiting approval to install PowerShell for packaging) |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37f` |
| Package | `artifacts/local-test/RC15-Phase37F-LOCALTEST/LOCAL-ONLY-RC15-Phase37F-LOCALTEST-win-x64.zip` |
| ZIP size / files | Not built yet (awaiting approval to install PowerShell for packaging) |
| ZIP SHA-256 | Not built yet (awaiting approval to install PowerShell for packaging) |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37F-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Not built yet (awaiting approval to install PowerShell for packaging) |

The source `<Version>` is still `0.1.0-rc.14` (not bumped). Database schema 18, no migration. Personal Sync, the draft
format and finalized History are unchanged.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up Passdown data. Setup is in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation". Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest`.
- Do not run `Enable Automatic Refresh.cmd`, and do not install any update from this build.
- Settings → About shows `0.1.0-rc.15-LOCALTEST.phase37f`.
- The weather printed is **real NWS forecast data** for the park area. Weather is informational only; it must not
  drive any real operational decision during the test.
- Printing: use **Microsoft Print to PDF** unless a real printer is convenient; for grayscale, choose the printer's
  black-and-white / grayscale option (or print to a black-and-white printer).

## Manual checklist

Mark each item **PASS / FAIL / NOT EXECUTED**, with a note or screenshot (Windows+Shift+S) for anything odd.
Record once at the top of the report: PC model, Windows 11 build, display scaling, printer(s) used, local time of the
test, and the park hours of each date tested.

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

### 1. Weather for the correct operating date
1.1 Open the app and wait until the Dashboard Weather card shows weather (about 15 seconds on first launch).

1.2 Open the Passdown for **today**. If today has park hours that have not ended, the PDF preview shows **WEATHER**
at the right of the park-hours area: an icon, a temperature or range in °F, a short condition, a rain chance
("Rain chance up to N%" or "… possible after <time> (up to N%)"), and a small grey line "NWS forecast <time>" (or
"NWS <time>"). If the park has already opened, the source line also says which hours it covers.

1.3 Compare with `https://forecast.weather.gov` (Carowinds area, hourly view): the temperatures and rain chances fall
within the hourly forecast for the park hours shown on the Passdown. Nothing says "watch", "warning" or "advisory".

1.4 Select **tomorrow** (with park hours): the preview's weather changes to tomorrow's park hours; it never shows
today's values for tomorrow. Selecting the date itself causes no visible weather loading anywhere.

1.5 Select a date **8 or more days ahead**, a **closed day** or a date whose **hours are not checked**: the PDF has
**no** weather and no empty space where it would be. Select **yesterday**: no weather.

1.6 Change the park hours with a manual override (or Private Buyout hours) for a date: the weather follows the new
hours (for example a later opening drops the morning hours). Undo the change afterwards.

### 2. Classic PDF
2.1 PDF Style → Classic. Weather sits at the right of the PARK HOURS block, its label level with "PARK HOURS"; the
park hours, North Gate, Avenue, Attendance, Pre-solds and Early Ride read exactly as before.

2.2 Nothing overlaps. Question of the Day / Daily Feature and pricing start at the **same height** as with weather
absent (compare with a date that has no weather, or with the screenshot in the record).

2.3 Print (Actions → Print) to Microsoft Print to PDF and to a real printer if available: one page for a normal day,
weather legible, icon visible.

### 3. Modern PDF
3.1 PDF Style → Modern. Weather sits opposite the hours headline in the top information band (no coloured card or
panel). The four columns North Gate / Avenue / Attendance / Pre-solds sit a little closer together and remain
readable; Early Ride is unchanged.

3.2 Same as 2.2 and 2.3 for Modern.

### 4. Compact PDF
4.1 PDF Style → Compact. Weather takes two lines at the right of the HOURS and ATTENDANCE lines; the strip is not
taller than before and Early Ride keeps its full line.

4.2 Same as 2.2 and 2.3 for Compact.

### 5. Grayscale printing
5.1 Print one page of each template in black and white / grayscale. The icon, temperature, condition and the grey
NWS line are all readable; nothing relies on colour.

### 6. Save PDF and Create PDF
6.1 Actions → Save PDF: the saved file shows the same weather as the preview. Open it in Edge: text is selectable and
the icon is sharp when zoomed (vector).

6.2 Create PDF (if shown in your workflow) produces the same weather for the same date.

### 7. Weather omitted when unavailable
7.1 Disconnect the network (Wi-Fi off or cable out), quit and reopen the app. If the cached forecast is less than
three hours old the PDF may still show it; after three hours without a successful refresh (or with no cache in a fresh
test account), the PDF shows **no** weather and no gap, and Print / Save PDF still work.

7.2 Reconnect. Within a few minutes the Dashboard card updates; the next preview refresh, Print or Save PDF includes
the weather again.

### 8. Preview behaviour
8.1 Edit attendance or a note: the preview updates as quickly as in RC.14. Weather never shows a loading message in
the Passdown.

8.2 Leave the Passdown open for 30+ minutes, then Print: the printed weather may be newer than what the preview
showed; the preview then shows exactly what was printed.

### 9. Finalized History and offline reopen
9.1 Open History and a finalized Passdown from before this build: it opens the stored PDF exactly as before (no
weather added).

9.2 If a Passdown is finalized during the test (finalization runs automatically after the operating day), its stored
PDF has no weather — the forecast is not applicable after the day. With the network off, open and Save a copy of a
finalized PDF: it works.

### 10. No extra pages, no collisions, content unchanged
10.1 A busy synthetic day (many products and notes) has the same page count with and without weather.

10.2 Pricing, promotions, shows, operational notes, gate times, attendance, Early Ride and the footer are unchanged.

10.3 Copy for Teams output contains no weather.

## Report back

Send the PASS / FAIL / NOT EXECUTED list with notes and screenshots of the three templates (with weather) and one
grayscale print. Windows validation is not recorded as passed until the owner reports it.
