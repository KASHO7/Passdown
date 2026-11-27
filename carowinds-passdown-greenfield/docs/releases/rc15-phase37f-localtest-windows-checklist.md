# RC15 local Windows test candidate — Phase 37F Compact PDF Weather Outlook

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged: all 13,060
were re-hashed after this build.

Phase 37F adds a small **Weather** summary to the Associate Passdown PDF (Classic, Modern and Compact): the forecast
for the selected date's park hours, from the weather the app already has (Phase 37C). It shows **no alerts**. The
Dashboard Weather card is unchanged and still shows alerts.

Record: [phase37f-compact-pdf-weather-outlook.md](../phase37f-compact-pdf-weather-outlook.md).

**Test this candidate: `RC15-Phase37F3-LOCALTEST`** — the Weather Outlook with its on/off choice as the **Weather
Outlook** checkbox in Overview → Include in Passdown (directly below Shows). It supersedes both earlier Phase 37F
candidates, which are kept unchanged below for reference and should not be tested further.

| | RC15-Phase37F3-LOCALTEST (test this one) |
|---|---|
| Source commit | F3-COMMIT |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37f3` |
| Package | `artifacts/local-test/RC15-Phase37F3-LOCALTEST/LOCAL-ONLY-RC15-Phase37F3-LOCALTEST-win-x64.zip` |
| ZIP size / files | F3-SIZE |
| ZIP SHA-256 | F3-SHA |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37F3-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | F3-VERIFY |

Superseded candidates (unchanged, do not test):

| | RC15-Phase37F2-LOCALTEST (superseded; option in the Actions menu) |
|---|---|
| Source commit | `829d219` (production source identical to `14f973a`; the later commit is documentation only) |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37f2` |
| Package | `artifacts/local-test/RC15-Phase37F2-LOCALTEST/LOCAL-ONLY-RC15-Phase37F2-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,472,064 bytes (72.9 MB); 308 files |
| ZIP SHA-256 | `fce0071103f22ab8f0a4174c9094606a241bbf0e9f2eb588602a63a14b139468` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37F2-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 6,406 passed / 0 skipped; repository guards 31/31; clean Release build 0 warnings / 0 errors; script content validation (folder and ZIP, 308 files); `RealReleaseZip` passed (a bogus path fails); marker names `829d219`; no database, WAL/SHM, draft, weather cache, preference file, key, signature, update-manifest, PDB or PDF file in the ZIP; all 13,626 earlier artifact files (including the first Phase 37F ZIP, still `ad9c4def…dd5f`) re-hashed byte-identical |

Superseded first candidate (unchanged, do not test):

| | RC15-Phase37F-LOCALTEST (superseded) |
|---|---|
| Source commit | `a18f8b3` (production source identical to `7ddc5f4`) |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37f` |
| Package | `artifacts/local-test/RC15-Phase37F-LOCALTEST/LOCAL-ONLY-RC15-Phase37F-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,463,874 bytes (72.9 MB); 308 files |
| ZIP SHA-256 | `ad9c4defcd87f0ccb25df75cac37823707b4afa2ff7b740c61c25415dd2ddd5f` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37F-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 6,367 passed / 0 skipped; repository guards 31/31; clean Release build 0 warnings / 0 errors; script content validation (folder and ZIP, 308 files); `RealReleaseZip` passed (a bogus path fails); marker names `a18f8b3`; no database, WAL/SHM, draft, weather cache, key, signature, update-manifest, PDB or PDF file in the ZIP; all 13,060 earlier artifact files re-hashed byte-identical |

The source `<Version>` is still `0.1.0-rc.14` (not bumped). Database schema 18, no migration. Personal Sync, the draft
format and finalized History are unchanged.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up Passdown data. Setup is in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation". Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest`.
- Do not run `Enable Automatic Refresh.cmd`, and do not install any update from this build.
- Settings → About shows `0.1.0-rc.15-LOCALTEST.phase37f3`.
- The weather printed is **real NWS forecast data** for the park area. Weather is informational only; it must not
  drive any real operational decision during the test.
- Printing: use **Microsoft Print to PDF** unless a real printer is convenient; for grayscale, choose the printer's
  black-and-white / grayscale option (or print to a black-and-white printer).

## Manual checklist

Run every section with the **Weather Outlook** checkbox checked unless the item says otherwise.

Mark each item **PASS / FAIL / NOT EXECUTED**, with a note or screenshot (Windows+Shift+S) for anything odd.
Record once at the top of the report: PC model, Windows 11 build, display scaling, printer(s) used, local time of the
test, and the park hours of each date tested.

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

### 0. Weather Outlook checkbox (Overview → Include in Passdown)
0.1 Open the Passdown's **Overview** tab and scroll to **Include in Passdown**. The last checkbox, **Weather
Outlook**, sits directly below **Shows** in the same column, looks like the other checkboxes, and is checked on a fresh
test account. The **Actions** menu has **no** weather item.

0.2 Resize the window to its minimum width (about 960 px): Weather Outlook is still fully visible below Shows, nothing
overlaps, and the page does not scroll sideways. Check this in **Light** and in **Dark** (Settings → Appearance), and at
100 %, 125 % and 150 % display scaling.

0.3 Uncheck it. The preview updates at once and shows **no** weather; the header looks exactly like RC.14 (no gap where
the weather was). The draft is not marked as changed (it is a setting for this computer, not part of the date).

0.4 With it unchecked, Print (Microsoft Print to PDF), Save PDF and Create PDF: none of the files has weather. Repeat for
Classic, Modern and Compact.

0.5 Close and reopen the app: still unchecked, and the first preview has no weather. Check it again: the weather
returns in the preview, Print, Save PDF and Create PDF; reopen the app: still checked.

0.6 Change the selected date with it unchecked: it stays unchecked for every date (unlike the other Include in Passdown
choices, it is not saved per date).

0.7 Keyboard: Tab through Include in Passdown — after Shows the next stop is Weather Outlook; Space toggles it. Narrator
reads "Include Weather Outlook in the Passdown" and its checked state.

0.8 Personal Sync (disposable folder only): the choice does not appear in the sync folder's files and does not change
on another test device.

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
