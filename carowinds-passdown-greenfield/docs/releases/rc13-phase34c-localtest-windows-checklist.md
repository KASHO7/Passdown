# RC13 local Windows test candidate — Phase 34C (repository cleanup)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.12 (published
prerelease `v0.1.0-rc.12`, tag → `7763c7e`) and every frozen file under `artifacts/releases` are unchanged
(54 files re-hashed byte-identical after this build, together with every earlier local-test ZIP).

| | RC13-Phase34C-LOCALTEST |
|---|---|
| Source commit | `a569401` (Phase 34C-1…34C-4 on top of `e230520` / Phase 34B-2 `5cf716e`) |
| Version shown in the app | `0.1.0-rc.13-LOCALTEST.phase34c` (About: `… (a569401)`) |
| Package | `artifacts/local-test/RC13-Phase34C-LOCALTEST/LOCAL-ONLY-RC13-Phase34C-LOCALTEST-win-x64.zip` |
| ZIP size | 75,953,582 bytes (72.4 MB) — 5,025,630 bytes smaller than the Phase 34B-2 candidate; 308 files (same file list) |
| ZIP SHA-256 | `cb43a1fd025aba3af03ab53da814b72858b03463c5858bbe3f06836ce768821d` |
| EXE inside the ZIP | `LOCAL-ONLY-RC13-Phase34C-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Full deterministic suite 5,512 passed / 0 skipped (5,432 + 80 new); clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP; `RealReleaseZip` passed; stamped version `0.1.0-rc.13-LOCALTEST.phase34c+a569401…` read from the DLL; `pdfium.dll` and PDFium licenses present; no manifest/signature in the package |

Source `<Version>` is still `0.1.0-rc.12` (not bumped). The test identity sorts above RC.12 and a future RC.13, so
the updater never offers a release over it. Database schema 18 (no migration). No new local files. Personal Sync
format unchanged.

## What changed (what to look for)

Nothing should look, print or behave differently from the Phase 34B-2 candidate. This build is a cleanup:

1. **Smaller download.** The PDF engine now carries only the six Lato font styles the PDFs use (was 18). On the
   Mac every Classic, Compact and Modern PDF was byte-for-byte identical before and after. On Windows, look
   closely at PDF text: same font, weights, italics, line breaks and page breaks as before.
2. **Official ticket checks.** The six dated ticket sources (admission, Fast Lane, haunted, Bring-A-Friend,
   Bring-A-Friend Duo, Military / First Responder) now share one piece of request code. Results, wording and
   error messages must be the same.
3. An unused 0.8 MB image is no longer packed inside the app. The window icon and banner must be unchanged.

Mac results: `docs/phase34c-repository-cleanup.md`. They are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** (setup in `rc12-phase32-localtest-windows-checklist.md`
→ "Data isolation"). Extract the ZIP to a **new** folder in that account (right-click → Extract All), then
double-click `Carowinds.Passdown.Desktop.exe`. Do not run `Enable Automatic Refresh.cmd`, do not point Personal
Sync at your real shared folder, and do not install any update from this build. Your installed RC.12 in your
normal account must not be touched.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note for anything odd. Record the PC model and display scaling
once at the top of the results.

### A. Startup and navigation
- [ ] A1 The EXE launches from Explorer in CarowindsTest; Settings → About shows `0.1.0-rc.13-LOCALTEST.phase34c (a569401)`.
- [ ] A2 Window icon (title bar and taskbar) and the Fury banner look as before.
- [ ] A3 Dashboard, Calendar, Passdown, Notes, History and Settings all open; no error.
- [ ] A4 Test data is only under `C:\Users\CarowindsTest\AppData\Local\Carowinds Passdown`.

### B. Products, official pricing, Shows, Calendar, Pass Perks
- [ ] B1 Products list shows official prices and seasonal information as in 34B-2.
- [ ] B2 Shows and Calendar (markers, month navigation) as in 34B-2.
- [ ] B3 Pass Perks and their eligibility details as in 34B-2.

### C. Official-source refresh (the shared accesso code)
- [ ] C1 Passdown → Check Online for **today**: dated admission, Fast Lane and (if in season) haunted prices match the official site.
- [ ] C2 Check Online for a **closed** date and for a date **beyond** the published calendar: "unavailable" / "not published yet" wording as before; no error.
- [ ] C3 Bring-A-Friend, Bring-A-Friend Duo and Military / First Responder: status and prices as in 34B-2 for the same dates.
- [ ] C4 With Wi-Fi off, Check Online: an error is shown and the previously cached values stay visible (last-known-good). Turn Wi-Fi back on.
- [ ] C5 Check Online twice within a minute: same result.

### D. Associate editor, ERT and live preview
- [ ] D1 Editing notes, products, shows and the Daily Feature: the preview updates as quickly as in 34B-2.
- [ ] D2 ERT editor: open, change the monthly default and a date override, save, cancel: behaves as before.
- [ ] D3 Change dates quickly; unsaved edits are still there when you return.

### E. PDF fonts and templates (the font change)
- [ ] E1 **Classic, Compact and Modern**: preview, then Save PDF. Headings bold, show names semi-bold, badges and the Daily Feature italic — exactly as in 34B-2.
- [ ] E2 A long Passdown (2+ pages): page breaks in the same places as 34B-2 for the same content.
- [ ] E3 Daily Feature with math (e.g. `\(\frac{a}{b}\)`) and special characters (é, ñ, —, “quotes”, ½, ™, €): all characters appear; no boxes or missing glyphs.
- [ ] E4 Open a saved PDF in Edge → … → Document properties → Fonts: only Lato (Regular/Bold/SemiBold/Italic) and STIX Two Math are listed. Note anything else: ___

### F. Printing
- [ ] F1 Print → **Microsoft Print to PDF** (1 page and a 2+ page Passdown): output identical to the preview.
- [ ] F2 *(If available)* Physical printer: quality unchanged.
- [ ] F3 *(Optional, for a later print-memory decision)* Task Manager peak memory while printing a 2-page Passdown: ___ MB.

### G. Original Copy menu
- [ ] G1 Copy menu items produce the same text as in 34B-2.

### H. Price History
- [ ] H1 Open Price History; switching products is instant; filters work.

### I. Personal Sync (test folder only)
- [ ] I1 Point Personal Sync at a test folder; make 10 edits over 2 minutes: any "sync failed" message? Y / N.
- [ ] I2 Conflict handling unchanged (two test profiles editing the same date → a conflict, never a silent overwrite).

### J. Settings, updates and appearance
- [ ] J1 Settings → Application Updates shows the status as before; do not install anything.
- [ ] J2 Light and Dark themes: Passdown, preview, Price History, Calendar look as in 34B-2.
- [ ] J3 100 %, 125 %, 150 % display scaling where practical: nothing clipped; preview sharp.

### K. Afterwards
- [ ] K1 In your normal account, RC.12 still opens with your Passdowns unchanged.

## Results

| Item | Result | Notes |
|---|---|---|
| A–K (all) | NOT YET TESTED | Windows observations go here, separately from the Mac numbers in `docs/phase34c-repository-cleanup.md`. |
