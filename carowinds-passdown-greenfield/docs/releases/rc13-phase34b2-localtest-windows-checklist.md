# RC13 local Windows test candidate — Phase 34B-2 (application-wide performance)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.12 (published
prerelease `v0.1.0-rc.12`, tag → `7763c7e`) and every frozen file under `artifacts/releases` are unchanged
(re-hashed after this build).

| | RC13-Phase34B2-LOCALTEST |
|---|---|
| Source commit | `5cf716e` (Phase 34B-2 on top of `af9e53a` / Phase 34B-1 `87b265e`) |
| Version shown in the app | `0.1.0-rc.13-LOCALTEST.phase34b2` |
| Package | `artifacts/local-test/RC13-Phase34B2-LOCALTEST/LOCAL-ONLY-RC13-Phase34B2-LOCALTEST-win-x64.zip` |
| ZIP size | 80,979,212 bytes (77.2 MB); 308 files (same file set as the Phase 34B-1 candidate) |
| ZIP SHA-256 | `24d92a9072bd8ebdf2166b7c382275410356363c091b2c6820dc309fc350c41b` |
| EXE inside the ZIP | `LOCAL-ONLY-RC13-Phase34B2-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Full deterministic suite 5,432 passed / 0 skipped (5,378 + 54 new); clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP; `RealReleaseZip` passed; stamped version `0.1.0-rc.13-LOCALTEST.phase34b2+5cf716e…` read from the DLL; no manifest/signature in the package; all files under `artifacts/releases` re-hashed byte-identical |

Source `<Version>` is still `0.1.0-rc.12` (not bumped). The test identity is stamped only into this build and
sorts above RC.12 and a future RC.13, so the updater never offers a release over it. Database schema 18 (no
migration). No new local files. Personal Sync format unchanged.

## What changed (what to look for)

Everything should look and print **exactly** as in the Phase 34B-1 candidate; only speed and efficiency changed:

1. **First PDF after launch.** A few moments after the window opens, the app quietly prepares the PDF engine
   in the background. The first preview, Create PDF or Print should no longer have a noticeable extra pause.
2. **Price History.** Switching between products is instant even with a lot of synced history. The open
   screen also refreshes after Check Online.
3. **Check Online / automatic refresh.** Fewer duplicate requests to the official ticket site. Results must
   be identical.
4. **Preview memory.** Each preview update allocates much less memory. Pages must look identical.

Mac measurements are in `docs/phase34b2-general-performance-optimization.md`. They are **not** Windows results.

## Data isolation — use the separate Windows account

The app always uses the signed-in user's `%LOCALAPPDATA%\Carowinds Passdown`, so test only in the separate local
account **`CarowindsTest`** (setup in `rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract
the ZIP to a **new** folder in that account (right-click → Extract All), then double-click
`Carowinds.Passdown.Desktop.exe`. Do not run `Enable Automatic Refresh.cmd`, do not point Personal Sync at your
real shared folder, and do not install any update from this build. Your installed RC.12 in your normal account
is not touched.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note for anything odd. Where a time is asked for, a rough
impression ("instant", "about ½ s") is enough; a phone slow-motion video is better. Record the PC model and
display scaling once at the top of the results.

### A. Startup
- [ ] A1 The EXE launches from Explorer in CarowindsTest; Settings → About shows `0.1.0-rc.13-LOCALTEST.phase34b2 (5cf716e…)`.
- [ ] A2 **Cold start** (first launch after a reboot): time until the window appears ___ s; until a click responds ___ s. No "Not Responding" and no frozen cursor.
- [ ] A3 **Warm start** (close, relaunch at once): window ___ s; first responsive click ___ s.
- [ ] A4 In the first few seconds after the window appears, clicking around (Dashboard, Calendar, Passdown) never stutters.
- [ ] A5 Test data is only under `C:\Users\CarowindsTest\AppData\Local\Carowinds Passdown`.

### B. First PDF after launch
- [ ] B1 With the preview **visible**: open Passdown; the first page appears without a noticeable pause. Impression: ___
- [ ] B2 **Hide Preview**, close and relaunch. Wait ~5 s on the Dashboard, open Passdown, then **Create PDF**: no extra pause compared with a second Create PDF. Impression: ___
- [ ] B3 Relaunch again (preview hidden), wait ~5 s, then **Print → Microsoft Print to PDF**: the dialog and spooling feel no slower than a second print.
- [ ] B4 Relaunch and click **Create PDF immediately** after the window appears (before waiting): it still works, and is no slower than before.
- [ ] B5 Close the app within 1–2 s of launching: it closes normally (no hang, no error).

### C. Ordinary preview changes (preview visible)
- [ ] C1 Toggle a product's Include, a show, an Overview switch and the PDF Style: each updates almost immediately, as in 34B-1.
- [ ] C2 Pages look identical to 34B-1/RC.12 (text sharpness, colours, layout) at the current zoom, Fit width and Fit page.

### D. Rapid typing
- [ ] D1 Notes → type quickly for ~10 s: typing never lags; the preview settles on the final text about ¼ s after you stop.
- [ ] D2 Long notes (2+ pages): edit on page 2; it stays on page 2 and updates quickly.

### E. Date switching
- [ ] E1 Change the date 10 times quickly (picker, ‹ ›, Today): no freeze; the preview always ends on the selected date. Longest wait: ___
- [ ] E2 Unsaved edits on a date are still there when you return.

### F. Products and Shows
- [ ] F1 Products and Shows lists respond instantly to selection; the preview follows.

### G. Price History
- [ ] G1 Open Price History from Products: it opens without a visible stall. Impression: ___
- [ ] G2 Click 10 different products quickly: each history appears instantly (no stall).
- [ ] G3 With "Include retired products", search and price-type filters: results as in RC.12.
- [ ] G4 With Price History open, run Check Online: the screen refreshes in place and keeps your selected product, search and filters.
- [ ] G5 *(If a second test PC or synced history is available)* After a Personal Sync that brings in price history, the open screen shows the merged rows (including Gold Pass legacy history folded into one product).

### H. Calendar
- [ ] H1 Move month to month 12 times with ‹ ›: no stall. Selecting days is instant.

### I. Check Online and automatic refresh
- [ ] I1 Passdown → Check Online for today, tomorrow and a closed date: results (prices, availability, "not published", "unavailable") match RC.12 for the same dates.
- [ ] I2 Check Online twice within a minute: the second result is the same and still says it was checked.
- [ ] I3 With Wi-Fi off, Check Online: an error is shown and the previously cached values stay visible (last-known-good).

### J. Printing
- [ ] J1 Print → **Microsoft Print to PDF** (1 page and a 2+ page Passdown): output identical to the preview and to RC.12 (layout, page breaks, margins).
- [ ] J2 While a multi-page job spools, the editor and preview stay responsive.
- [ ] J3 *(If available)* Physical printer: output quality unchanged.
- [ ] J4 Task Manager → Details → add "GDI objects": note ___; open and Cancel the Print dialog 20 times; GDI objects ___ (should return close to the start).
- [ ] J5 Peak memory while printing a 2-page Passdown: ___ MB.

### K. Preview memory and responsiveness
- [ ] K1 Task Manager → Memory (private working set) after opening Passdown with preview: ___ MB.
- [ ] K2 Toggle a checkbox ~100 times (about once a second): ___ MB. Still responsive.
- [ ] K3 Hide Preview, wait 30 s: ___ MB. (Growth over 100 MB that does not come back: report it.)

### L. Personal Sync (test folder only, never the real shared folder)
- [ ] L1 Point Personal Sync at a test folder inside OneDrive (or a plain test folder if OneDrive is not set up in CarowindsTest). Make 10 edits over 2 minutes: any "sync failed" message? Y / N. If yes, send the app log lines around it.
- [ ] L2 Conflict handling unchanged: editing the same date on two test profiles still produces a conflict to resolve, never a silent overwrite.

### M. Appearance and scaling
- [ ] M1 Light and Dark: Passdown, preview, Price History and Calendar look as in 34B-1.
- [ ] M2 100 %, 125 % and 150 % display scaling: nothing clipped, preview sharp, updates quick.

### N. Afterwards
- [ ] N1 In your normal account, RC.12 still opens with your Passdowns unchanged.

### Optional: Windows benchmark run (only if the .NET 10 SDK is installed in CarowindsTest)
- [ ] O1 From the repository folder: `dotnet build tools/perf-benchmarks -c Release`, then
      `dotnet tools/perf-benchmarks/bin/Release/net10.0/PerfBenchmarks.dll all $env:TEMP\carowinds-perf` (twice); send the second run's output.

## Results

| Item | Result | Notes |
|---|---|---|
| A–N (all) | NOT YET TESTED | Windows observations go here, separately from the Mac numbers in `docs/phase34b2-general-performance-optimization.md`. |
