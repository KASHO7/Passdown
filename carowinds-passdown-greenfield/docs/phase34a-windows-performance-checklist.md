# Phase 34A — Windows performance checklist

Use this checklist to get the Windows numbers the macOS audit could not measure
(`docs/phase34a-repository-performance-audit.md` §20). Use a **separate Windows test account** so real
operational data is never touched; app data always lives in that account's `%LOCALAPPDATA%`. A phone
stopwatch or the slow-motion camera is enough for everything below. Record the build (About this build),
the PC model, and the display scaling.

Do every timed item **5 times** and write down all five values. Note whether the run was **cold** (first
launch after a reboot) or **warm** (relaunch within a minute).

## A. Cold start
- [ ] Reboot. Double-click the app. Time from double-click until **the window appears**: ___ s
- [ ] …until a **Dashboard tile or Associate field responds** to a click: ___ s
- [ ] …until **cached data shows** (hours/prices on the Dashboard): ___ s
- [ ] Any "Not Responding" title or frozen cursor during the first 5 s? Y / N

## B. Warm start
- [ ] Close the app, relaunch at once. Window appears: ___ s · first responsive click: ___ s

## C. Checkbox → preview (Associate → Products, preview visible)
- [ ] Toggle one product's **Include**. Time until the page in the preview changes: ___ ms (expect ~0.5 s on RC.12)
- [ ] Repeat with **Include Fast Lane**, a **Show** checkbox, and **More → PDF Style** (template).
- [ ] Does the "Updating…" chip flicker on every click? Y / N
- [ ] With **Narrator** on: is "Updating…" announced on every edit or keystroke? Y / N

## D. Typing → preview (Associate → Notes)
- [ ] Type a short sentence at normal speed. Does typing itself ever lag (letters appearing late)? Y / N
- [ ] Time from the **last** keystroke until the preview shows the text: ___ ms

## E. Long note (multi-page)
- [ ] Paste ~80 lines of notes so the PDF reaches 2+ pages. Edit one word. Preview update: ___ ms
- [ ] Page buttons show "1 / N", and Next/Previous respond instantly? Y / N

## F. Navigation
- [ ] Dashboard → Calendar → Associate → Notes → History → Settings: does any switch take > 0.3 s? Which: ___
- [ ] Change the Associate date 10 times with the date picker: any freeze? Longest wait: ___ s
- [ ] Open Price History, click 5 different products: any visible stall? ___

## G. Memory after repeated updates (Task Manager → Details → Memory (private working set))
- [ ] After launch, Associate open with preview: ___ MB
- [ ] Toggle a checkbox ~100 times (≈1 per second): ___ MB
- [ ] Hide Preview, wait 30 s: ___ MB. (Growth > 100 MB that does not come back = report it.)

## H. Printing
- [ ] Print → choose a printer (or Microsoft Print to PDF) → Print. Time until the job appears in the queue: ___ s
- [ ] While it spools, does the preview / editor stay responsive? Y / N
- [ ] Peak memory during printing: ___ MB

## I. GDI handle stability (Task Manager → Details → right-click headers → add "GDI objects" and "Handles")
- [ ] Note GDI objects ___ / Handles ___
- [ ] Open the Print dialog and **Cancel** it 20 times.
- [ ] GDI objects ___ / Handles ___. (Should come back to roughly the starting value.)

## J. Idle CPU
- [ ] Leave the app idle on the Dashboard for 5 minutes. CPU in Task Manager: mostly 0 %? Y / N · peaks: ___ %
- [ ] Same with Associate + preview visible: Y / N

## K. Display scaling and sync (if available)
- [ ] Drag the window between monitors with different scaling. Does the preview page stay sharp (or sharpen after a moment)? Y / N
- [ ] With Personal Sync pointed at a OneDrive folder, make 10 edits over 2 minutes. Any "sync failed" message? Y / N

---

### Optional diagnostic tools (only if a problem shows up above)
- **Windows Performance Recorder / Analyzer** (free, part of the Windows ADK): record "CPU usage" while
  reproducing a stall; send the `.etl` file.
- **dotnet-counters** (`dotnet tool install -g dotnet-counters`): `dotnet-counters monitor -n Carowinds.Passdown.Desktop`
  shows GC counts and heap size live during sections C–G.
- **Process Explorer** (Sysinternals): GDI/USER object counts for section I with more precision.
