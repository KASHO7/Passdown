# RC15 integrated local Windows test candidate — Phase 37E

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.14 (published
prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged: all 15,899 were hashed before this
build and re-hashed after it, byte-identical (16,465 before the second, D-1 package, also unchanged).

This one package contains everything since RC.14 together: Phase 36 maintenance (including the 36C Early Ride Time
editor extraction), Phase 37B weather retrieval and cache, Phase 37C Dashboard Weather, Phase 37D Calendar Weather and
the Settings preference, Phase 37F3 optional Passdown PDF Weather Outlook, and the pre-RC.15 post-closing pricing
retention correction. It adds **no new feature**. Its purpose is a final combined check before RC.15 preparation is
separately authorized. It also contains one owner-approved wording change from this phase (decision D-1): the
Calendar's Daily ticket line now marks a price that is only previously verified ("$49.00 · last verified Nov 27").

Use **`RC15-Phase37E2-LOCALTEST`** below. The first 37E package (`RC15-Phase37E-LOCALTEST`, SHA-256
`00e623b5…594f`) is superseded: it lacks the D-1 wording. Do not use it.

Record: [phase37e-integrated-weather-pricing-release-readiness.md](../phase37e-integrated-weather-pricing-release-readiness.md).

| | RC15-Phase37E2-LOCALTEST |
|---|---|
| Source commit | `69d70a7` (D-1; the later commit is documentation only) |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.phase37e2` (Settings → About this build) |
| Package | `artifacts/local-test/RC15-Phase37E2-LOCALTEST/LOCAL-ONLY-RC15-Phase37E2-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,540,554 bytes (73.0 MB); 308 files (162.6 MB uncompressed) |
| ZIP SHA-256 | `15608e4736a233454bbd6a13eb123529ac066d4b5f617a98552af1955a6532a1` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-Phase37E2-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 6,560 passed / 0 failed / 0 skipped; repository guards 31/31; clean Release build 0 warnings / 0 errors; script content validation (folder and ZIP, 308 files); `RealReleaseZip` passed; marker names `69d70a7`; no database, WAL/SHM, draft, weather cache, preference file, key, signature, update manifest, PDB, PDF or pricing-harness file in the ZIP; all 16,465 earlier artifact files re-hashed byte-identical |

Source `<Version>` stays `0.1.0-rc.14`. Database schema 18, Personal Sync envelope 1, draft payload 4, finalized
snapshot 2, weather cache format 1 — no migration, no format change.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTestB`** with made-up Passdown data (setup:
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder (right-click →
Extract All) and double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTestB\Documents\SyncTest-37E`.
- Do not run `Enable Automatic Refresh.cmd` and do not install any update from this build.
- Do **not** run the pricing-retention harness `seed` again: this account's database already holds the synthetic
  2026-08-20 data, and the harness correctly refuses to overwrite it. Section F only *looks* at that data. (Running
  `verify` again is harmless and read-only.)
- Official data comes from the real Six Flags site and weather from the real National Weather Service; use them for
  this test only.

## How to record results

Mark each item **PASS / FAIL / NOT EXECUTED** in the "Observed" column, with a note or screenshot (Windows+Shift+S) for
anything odd. Record once at the top of the report: PC model, Windows 11 build, display scaling (100/125/150 %), local
time of each section, and whether the PC was online.

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

Owner feedback already on record (not repeated here unless you want to): RC.13 → RC.14 in-app update worked; the 36C
ERT editor worked; 37C Dashboard Weather worked; 37F3 PDF Weather Outlook looked good; 37D Calendar Weather and Settings
looked great; the pricing harness `seed` and `verify` both reported PASS (8 checks each). Those reports were overall
confirmations, not item-by-item runs of their checklists, so the items below still matter.

---

### A. Startup and Settings

| # | Step | Expected | Observed |
|---|---|---|---|
| A.1 | First launch from the new folder (if this account already ran an earlier test build, note that and continue) | Window opens promptly; no freeze or blank window; Dashboard appears | |
| A.2 | Settings → About this build | `0.1.0-rc.15-LOCALTEST.phase37e2` | |
| A.3 | Existing preferences from earlier builds (theme, PDF style, Include Weather Outlook) | Kept as you left them | |
| A.4 | Switch Light → Dark → System | Every screen readable in each; Weather card and Calendar Weather follow the theme | |
| A.5 | Turn the app's reduced-motion/animation setting on (and, if convenient, Windows *Settings → Accessibility → Visual effects → Animation effects* off) | No sliding/fading; nothing waits for an animation | |
| A.6 | Close and reopen the app twice | Opens normally each time; no "already running" or lock message; Weather shows the last values immediately (from the cache) before any new check | |

### B. Dashboard

| # | Step | Expected | Observed |
|---|---|---|---|
| B.1 | Wait ~15 seconds after launch, online | Weather card fills in: observed temperature with the station name, forecast high/low, park-hours rain chance | |
| B.2 | Read the card's source lines | "National Weather Service" attribution; observed vs forecast are labelled differently; times are Eastern | |
| B.3 | Alerts area | Either active NWS alerts (with NC/SC area and a link) or a clear "no active alerts" **only when the check is current** | |
| B.4 | Disconnect the network (Wi-Fi off), wait 20 minutes, look at the card | Values stay but are marked out of date; the alert area says the alert status is unknown / could not be checked — **never** "no alerts" | |
| B.5 | Still offline, press **Refresh Weather** | Honest failure message ("Could not reach the National Weather Service…"); last values kept; global Check Online not started | |
| B.6 | Reconnect, press **Refresh Weather** | "Weather updated <time>." or "Checked recently…" — never a false success | |
| B.7 | Press Refresh Weather several times quickly | No error; it says it was checked recently; the app stays responsive | |
| B.8 | Pricing card and Admission card | Prices as before; if a price is only "previously verified", it says "Last verified … — availability not confirmed" | |
| B.9 | Other Dashboard sections (Shows, park status, data health, Pass Perks) | Unchanged from RC.14 | |
| B.10 | Navigate Dashboard → Calendar → Passdown → Dashboard 10 times | Weather card shows the same data each time; no extra delay; no duplicate cards | |

### C. Calendar

| # | Step | Expected | Observed |
|---|---|---|---|
| C.1 | Select **today** | Weather group under Park information: scope line "Park hours, …" (or "Rest of park hours…"), temperature range, rain, high/low, NWS source line | |
| C.2 | Select **tomorrow** | Tomorrow's forecast for tomorrow's park hours — not today's | |
| C.3 | Select a date 4–6 days ahead | Forecast for that date (possibly with a partial-coverage note) | |
| C.4 | Select a date 2+ weeks ahead | "No NWS forecast this far ahead…" | |
| C.5 | Select a **past** date | "Past date. NWS forecasts cover today and upcoming days only…" — no weather values | |
| C.6 | Select a **closed** date | "Park closed · general forecast for the day" (if inside the forecast range) | |
| C.7 | Expand **Hourly forecast** on an open date | Hours cover only the park-hours window shown in the scope line; Eastern times; missing values show "—" | |
| C.8 | Look at the month grid | **No weather icons or temperatures in day cells** | |
| C.9 | Pass Perks markers, legend and month summary | Unchanged | |
| C.10 | Click through 15+ dates quickly and change months back and forth | Stays responsive; the Weather group always matches the date in the heading; never another date's forecast | |
| C.11 | Calendar → select **Thursday, August 20, 2026** (the harness date; past, so nothing is retrieved) and read the **Daily ticket** line; optionally also today after the park has closed | Reads "$49.00 · last verified Aug 20" (decision D-1), fits the panel (wraps cleanly if needed, nothing cut off) in Light and Dark; Narrator reads "Daily ticket price: $49.00, last verified August 20, availability not confirmed". A date still on sale shows just the price | |

### D. Passdown

| # | Step | Expected | Observed |
|---|---|---|---|
| D.1 | Passdown for today; then move to tomorrow and back | Each date loads its own draft; no edits move between dates | |
| D.2 | Type a Daily Note, wait 3 seconds, switch date and come back | Note kept (autosave) | |
| D.3 | Open the **Early Ride Time** editor, change a value, save; reopen | Value kept; draft not duplicated; autosave still works afterwards | |
| D.4 | Products & Pricing: edit one price, then clear the edit | Manual price shows as the effective price; clearing restores the official price | |
| D.5 | Shows and Operational Notes selection | Same as RC.14 | |
| D.6 | Overview → **Include in Passdown**: **Weather Outlook** checkbox | Appears below Shows; **not** in the Actions menu | |
| D.7 | Toggle Weather Outlook off, then open Settings → Passdown Defaults | "Include Weather Outlook by default" is off too; turn it back on there and return to Passdown — checkbox is on | |
| D.8 | Toggle the preference, then switch dates | The draft is not marked changed merely by the toggle | |
| D.9 | Restart the app | Weather Outlook preference is as you left it | |
| D.10 | Change PDF style Classic → Modern → Compact | Preview updates promptly each time | |

### E. PDFs

| # | Step | Expected | Observed |
|---|---|---|---|
| E.1 | Weather Outlook on, today, online, each style (Classic, Modern, Compact) | Small Weather Outlook in the header whitespace: today's park hours, temperature and rain summary, a line icon (no emoji), small NWS attribution; header no taller than without it | |
| E.2 | Weather Outlook off, each style | No weather and no blank space where it was | |
| E.3 | Check the outlook text | Matches the date's actual park hours; **no weather alerts** in the PDF | |
| E.4 | Any style with long notes | No overlapping text, nothing cut off | |
| E.5 | **Save PDF**, open the file | Same content as the preview | |
| E.6 | **Print → Microsoft Print to PDF**, open the file | Same content as the preview; sharp text | |
| E.7 | Physical printer if available | Prints correctly; note the printer model | |
| E.8 | **Create PDF** | Same weather choice as the preview | |
| E.9 | Preview freshness: open Passdown right after launch (before weather arrives, ~10 s), leave the preview untouched until the Dashboard shows weather, then **Print to PDF** without editing | **Record what the visible preview showed before printing and what the printed file contains.** Expected by design: the printed file uses the newest weather and the preview then updates to match (decision D-2) | |
| E.10 | History: open an earlier finalized Passdown, print it | Opens and prints the stored document; no current weather added; unchanged | |
| E.11 | High-DPI printing: at 150 % (or 125 %) display scaling, print a 2-page Passdown to Microsoft Print to PDF while watching Task Manager → Details → `Carowinds.Passdown.Desktop.exe` memory | Note peak memory during printing and memory 1 minute later (a temporary rise is expected; it should fall back) | |

### F. Pricing retention (review only — do not reseed)

| # | Step | Expected | Observed |
|---|---|---|---|
| F.1 | Harness results already reported: `seed` PASS (8), `verify` PASS (8) | Recorded; nothing to run | |
| F.2 | In this app, Passdown → select **Thursday, August 20, 2026** → Products & Pricing | Park Ticket $49.00, Fast Lane Reserve $49.00, Fast Lane Ultimate $85.00 | |
| F.3 | Row note under those prices | "Last verified Aug 20 at 3:10 PM ET — sales availability not confirmed." | |
| F.4 | Open a row's details | Original verification time (Aug 20, 3:10 PM ET) | |
| F.5 | Give Park Ticket a manual price (e.g. $45), check the preview, then clear it | Preview shows $45.00 while set; footer "Some product pricing has not been recently verified."; clearing restores $49.00 | |
| F.6 | Save PDF for Aug 20 in each style | Retained prices print; footer note present | |
| F.7 | Restart the app and re-open Aug 20 | Same prices and note (no network request is made for this past date) | |
| F.8 | Optional, real closing: on an open day, after closing, Check Online for today | Prices stay with the "Last verified …" note (not removed) | |

### G. Personal Sync

| # | Step | Expected | Observed |
|---|---|---|---|
| G.1 | With sync off, edit a draft and restart | Works normally | |
| G.2 | Turn sync on with the disposable folder `SyncTest-37E` | Accepted; first sync completes | |
| G.3 | Edit a draft; wait for sync | Files appear/update in the disposable folder | |
| G.4 | Look inside the disposable folder | **No** `weather-cache.json`, **no** `passdown-weather-outlook.json`, no database or `-wal`/`-shm` files | |
| G.5 | Optional (two test PCs only): edit the same date on both | Conflict protection behaves as in RC.14 | |
| G.6 | Turn sync off again | Local drafts unaffected | |

### H. Stability

| # | Step | Expected | Observed |
|---|---|---|---|
| H.1 | Leave the app open for 1 hour with the Dashboard visible | Weather ages and refreshes on its own; no freezes | |
| H.2 | Task Manager memory at start, after 30 navigations, and after 1 hour | Note the three numbers; no steady climb | |
| H.3 | Offline for 10 minutes, then reconnect | Weather recovers within a few minutes without pressing anything | |
| H.4 | Optional, if comfortable: Resource Monitor → Network → `Carowinds.Passdown.Desktop.exe` → TCP connections to `api.weather.gov` over 10 minutes | Only occasional connections (a few per minute at most), not continuous | |
| H.5 | Close the app normally | Closes within a few seconds; reopening shows your last draft | |

## Report back

Send: the A–H results, the header details (PC, Windows build, scaling), and screenshots of anything that failed or
looked wrong. Say explicitly whether items C.11, E.9 and E.11 were done: C.11 checks the D-1 change, and E.9 and E.11
inform decisions D-2 and D-3.
