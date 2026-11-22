# RC14 local Windows test candidate — Phase 35A (shared shell and design tokens)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`, tag → `d784c60`) and every frozen file under `artifacts/releases` are unchanged
(152 earlier release and local-test files re-hashed byte-identical after this build).

| | RC14-Phase35A-LOCALTEST |
|---|---|
| Source commit | `552904f` (Phase 35A-1…35A-5 on top of the RC.13 record `cfebdc9`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35a` (About: `… (552904f)`) |
| Package | `artifacts/local-test/RC14-Phase35A-LOCALTEST/LOCAL-ONLY-RC14-Phase35A-LOCALTEST-win-x64.zip` |
| ZIP size | 75,990,322 bytes (72.5 MB); 308 files |
| ZIP SHA-256 | `4b39cfcd7572345a566b00b019dca5f00097c708c15bcac47d7ada21e0cb54af` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35A-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,619 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP; `RealReleaseZip` passed |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). The test identity sorts above RC.13, so the updater never
offers a release over it. Database schema 18 (no migration). Personal Sync format unchanged. Two new local files may
appear beside `appearance.json`: `interface-animations.json` and `navigation-sidebar.json` (this computer only).

## What changed (what to look for)

1. **No photo banner.** The window's own Windows title bar shows the original app icon and "Carowinds Operations";
   the Fury 325 banner and the horizontal section strip are gone.
2. **Left sidebar**: Dashboard, Calendar, Passdown, Operational Notes, History, and Settings at the bottom. The
   button at the top collapses it to icons only and expands it again; the choice is remembered on this computer.
   Windows narrower than about 1,100 pixels show icons only automatically.
3. **Settings → Appearance → Interface animations: On / Off.** On by default. Motion is short and cosmetic (label
   fade when the sidebar expands, quick hover fills). Turning Windows "Animation effects" off turns the app's motion
   off without changing your choice; Settings explains this.
4. Every page, button, Copy option, preview, print, save and sync behaves exactly as in RC.13.

Mac results: `docs/phase35-unified-ui-modernization.md` §15. They are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** (setup in `rc12-phase32-localtest-windows-checklist.md`
→ "Data isolation"). Extract the ZIP to a **new** folder in that account (right-click → Extract All), then
double-click `Carowinds.Passdown.Desktop.exe`. Do not run `Enable Automatic Refresh.cmd`, do not point Personal
Sync at your real shared folder, and do not install any update from this build.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows
build and display scaling.

### A. Title bar and icon

1. The title bar shows the original app icon (not "CW" letters) and "Carowinds Operations" — nothing else beside it.
2. The taskbar and Alt+Tab show the same icon.
3. Minimize, Maximize, Restore and Close work; double-clicking the title bar maximizes/restores; dragging the title
   bar moves the window; hovering Maximize shows Windows Snap Layouts; Win+Left / Win+Right snap the window.
4. In Dark appearance the title bar is dark too (if it stays light, note it — it is a Windows/Avalonia detail).

### B. Sidebar

5. Each item opens its page; the current one has a dark bar on its left, a soft fill and bold text.
6. Collapse with the top button: icons stay, labels disappear, the page widens; hovering an icon shows its name.
   Expand again. Nothing on the page resets (e.g. type in a Passdown field, collapse, expand — the text is still there).
7. Close and reopen the app: the sidebar opens in the state you left it.
8. Make the window narrow (about 1,000 px wide): the sidebar shows icons only and the top button is greyed out;
   widen it again: labels return in your saved state.
9. Start creating an Operational Note: the other sidebar items are unavailable until you Save or Cancel (as before);
   the collapse button still works.
10. Changing sections never starts an official check by itself (no "Checking…" appears just from navigating).
11. Settings shows a small dot on the gear only when an update is available (as before).

### C. Keyboard and Narrator

12. Press Tab from the top of the window: focus goes to the collapse button, then Dashboard … History, Settings,
    with a clearly visible focus outline. Enter or Space opens the focused section and focus stays on it.
13. With Narrator on, each item reads its name ("Dashboard section", "Operational Notes section", "Settings") and
    the current one says "Current page"; the collapse button reads "Collapse navigation" / "Expand navigation".

### D. Interface animations

14. Settings → Appearance → Interface animations shows **On** selected on first run.
15. With On: expanding the sidebar fades the labels in quickly (well under a quarter second); hovering items eases
    the fill. Nothing waits — clicks, typing, autosave, preview and printing are immediate.
16. Choose **Off**: the change applies at once; expanding the sidebar shows labels instantly; no fades. Close and
    reopen: still Off.
17. Choose On, then in Windows Settings → Accessibility → Visual effects turn **Animation effects** off and return to
    the app: animations stop, On stays selected, and a note under the options explains why. Turn Animation effects
    back on and return: animations resume.
18. Arrow keys move between On and Off; Tab enters the group on the selected option.

### E. Sizes, scaling and themes

19. At 100 %, 125 % and 150 % scaling, and window sizes about 1920×1080, 1536×960, 1280×720 and the 960×640 minimum:
    no clipped or overlapping sidebar text, no hidden buttons, no sideways scrolling of the whole window.
20. Light, Dark and "Use Windows setting" all look right in the sidebar, Settings and every page; switching while a
    page is open restyles everything at once.

### F. Regression (should be exactly as RC.13)

21. Passdown: live PDF preview updates almost immediately after a checkbox change; Create PDF, Save PDF and Print
    (Windows dialog; Cancel is fine) work; all Copy options work.
22. Products (prices and display-price overrides), Shows, Notes, Calendar (open/closed, Pass Perks markers), History
    (open a stored PDF) and Settings → Personal Sync / Diagnostics / Updates behave as before.
23. Check Online still works and failures keep the last good data.

Report each item as **Pass / Fail / Not executed**. Phase 35A is not Windows-validated until this is done.
