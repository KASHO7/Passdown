# RC12 local Windows test candidate — Phase 33 (live PDF preview and printing)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed, or published; no update manifest. RC.11
(published prerelease `v0.1.0-rc.11`, tag → `573d17c`) and its frozen files are unchanged (re-hashed after
this build). Supersedes `RC12-Phase32A-LOCALTEST` (kept; this build also contains Phase 32/32A).

| | RC12-Phase33-LOCALTEST |
|---|---|
| Source commit | `c2718b2` (Phase 33 on top of Phase 32A) |
| Version shown in the app | `0.1.0-rc.12-LOCALTEST.phase33` |
| Package | `artifacts/local-test/RC12-Phase33-LOCALTEST/LOCAL-ONLY-RC12-Phase33-LOCALTEST-win-x64.zip` |
| ZIP size | 80,957,394 bytes (77.2 MB); 308 files (+20: `pdfium.dll` and 19 license files) |
| ZIP SHA-256 | `1f98bac694b598b66db6d3ac3bdc45c5c49b6af60ccf584820b9e9a7df0d2ab3` |
| EXE inside the ZIP | `LOCAL-ONLY-RC12-Phase33-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Full deterministic suite 5,347 passed; clean Release build 0 warnings / 0 errors; content validation on folder and ZIP; `RealReleaseZip` passed; stamped version read from the DLL; no manifest/signature; RC.10/RC.11 re-hashed unchanged |

Assembly/file version `0.1.0.11` (source `<Version>` still `0.1.0-rc.11`, not bumped); database schema 18 (no
migration). The test identity sorts above RC.11 and a future RC.12, so the updater never offers a release over
it. New local file in the test account: `pdf-preview-layout.json` (never synced).

## Data isolation — use the separate Windows account

Same as the Phase 32 candidates: the app always uses the signed-in user's `%LOCALAPPDATA%\Carowinds Passdown`,
so test only in the separate local account (`CarowindsTest`; setup steps in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract this ZIP to a **new** folder in that
account. Do not run `Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder, and
do not install any update from this build.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note for anything odd. Physical-printer items stay **NOT
EXECUTED** if no printer is available. Do not mark printing passed from anything but a real Windows run.

### A. Launch and safety
- [ ] A1 The EXE launches from Explorer in CarowindsTest; Settings → About shows `0.1.0-rc.12-LOCALTEST.phase33 (c2718b2…)`.
- [ ] A2 Test data is only under `C:\Users\CarowindsTest\AppData\Local\Carowinds Passdown`.
- [ ] A3 Afterwards, in your normal account, RC.11 still opens with your Passdowns unchanged.

### B. Preview generation and editor navigation
- [ ] B1 Passdown opens with the PDF preview on the right, showing today's Passdown (caption "Sat, … · Classic").
- [ ] B2 The preview page looks like the PDF from Create PDF (same fonts, layout, header/footer, sections).
- [ ] B3 Switching Overview → Products → Shows → Notes leaves the preview exactly where it was (no flash/reload).

### C. Live updates (wait ~½ second after each)
- [ ] C1 Attendance / pre-solds change appears in the preview.
- [ ] C2 Park-hours override (Manual hours) and a gate override appear.
- [ ] C3 Early Ride Time change (Edit) appears.
- [ ] C4 Daily Feature text, including a math expression, appears correctly formatted.
- [ ] C5 Products: unchecking a product removes it; a price override changes it.
- [ ] C6 Shows: turning Shows on and selecting a show adds it.
- [ ] C7 Notes: typed notes and adding/removing a saved operational note appear.
- [ ] C8 Include in Passdown switches add/remove their sections.
- [ ] C9 Typing quickly does not make the preview flicker per keystroke; it settles once you pause.
- [ ] C10 Changing the date: the old page disappears immediately ("Loading this date's preview…"), then the new date's PDF appears; the caption shows the new date.
- [ ] C11 Responsiveness: the app never freezes while the preview updates (note any lag).

### D. Resizing and show/hide
- [ ] D1 Drag the divider: smooth, no overlapping controls, editor never squeezed below a usable width.
- [ ] D2 Hide Preview (action bar or pane): the editor uses the full width. Show Preview restores the same width.
- [ ] D3 Make the window narrow (about half the screen): only the editor shows; Show Preview shows the preview in its place; Hide Preview returns to the editor.
- [ ] D4 Maximize again: side by side returns.
- [ ] D5 No horizontal scrollbar on the page at normal sizes; the preview toolbar wraps neatly when narrow.

### E. Templates
- [ ] E1 More → PDF Style: Classic, Compact, Modern each update the preview; the Create PDF button label matches.
- [ ] E2 After switching style, Print and Save PDF use that style.

### F. Pages and zoom
- [ ] F1 Add many long notes until the Passdown is 2+ pages: "1 / 2" shows; ‹ › and Page Up / Page Down move pages; the multi-page warning still shows.
- [ ] F2 Remove notes back to one page while on page 2: the preview goes to page 1 without an error.
- [ ] F3 − / + zoom (and Ctrl+Minus / Ctrl+Plus), Fit width (Ctrl+0), Fit page (Ctrl+9) work and stay within 25 %–400 %; zoomed text is sharp, not blurry.
- [ ] F4 All toolbar buttons are reachable with Tab and show a visible focus outline.

### G. Save PDF
- [ ] G1 Save PDF (preview toolbar) opens the Windows Save dialog with `Carowinds-Operations-YYYY-MM-DD.pdf` for the selected date.
- [ ] G2 Saving over an existing file asks to replace it.
- [ ] G3 Saving to a read-only/unavailable location shows a helpful message and nothing crashes.
- [ ] G4 The saved PDF opens in a PDF viewer and matches the preview.
- [ ] G5 Create PDF still works exactly as before.

### H. Printing (Windows print dialog)
- [ ] H1 Print (preview toolbar) opens the standard Windows print dialog over the app; the printer list shows installed printers.
- [ ] H2 Preferences shows paper size / orientation (and duplex if the printer has it); Copies and Pages ("1-2") are available.
- [ ] H3 Cancel in the print dialog: nothing prints, no error message.
- [ ] H4 **Microsoft Print to PDF**: print, save the file; open it and compare side by side with the Save PDF file — same layout, page breaks, margins and size (printed output is a high-resolution image of the page; text should still look crisp at 100 %).
- [ ] H5 Microsoft Print to PDF, then Cancel in its "Save Print Output As" box: no error message, app fine.
- [ ] H6 Pages: print only page 2 of a 2-page Passdown → only page 2 is in the output.
- [ ] H7 Copies = 2 → two copies in the output (where the printer supports copies).
- [ ] H8 Print with the preview hidden (action bar Print): same dialog, same output.
- [ ] H9 Edit something and press Print immediately (within ½ second): the printed output includes the edit.
- [ ] H10 Printing does not change the Passdown: save status unchanged, History unchanged, nothing marked finalized.
- [ ] H11 Physical printer (if available): print one page — correct size, nothing cut off at the edges. **If no printer is available, mark NOT EXECUTED.**
- [ ] H12 A printer that is off/unavailable: understandable message or the job waits in the Windows queue; the app does not crash.

### I. Appearance and accessibility
- [ ] I1 Light and Dark mode: the PDF page stays white on a neutral background; toolbar readable in both.
- [ ] I2 Display scaling 100 %, 125 % and 150 % (Settings → System → Display): nothing clipped; the page is sharp.
- [ ] I3 Narrator (Ctrl+Win+Enter): toolbar buttons are announced by name (Print Passdown, Save PDF, Next page, Zoom in, …).

### J. Restart and preservation
- [ ] J1 Hide the preview, move the divider, choose Fit page; close and reopen: the same layout returns.
- [ ] J2 Move the window to a smaller screen (or make it smaller) and reopen: nothing is clipped; the preview is narrower, not overlapping.
- [ ] J3 Draft edits made before closing are still there after reopening (preview/printing did not lose or alter them).
- [ ] J4 Calendar, Pass Perks, History, Notes and Settings behave as in the Phase 32A candidate.

## Results

| Item | Result | Notes |
|---|---|---|
| A–J (all) | USER-VERIFIED ON WINDOWS | Product owner reported on 2026-11-22 that all tests passed. No per-item notes; whether a physical printer (H11/H12), each display scale and Narrator were covered was not stated. Released as RC.12 — see `rc12-release-readiness.md`. |
