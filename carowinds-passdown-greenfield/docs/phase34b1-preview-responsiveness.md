# Phase 34B-1 — Live PDF preview responsiveness

Status: implemented locally (not pushed, not tagged, not released). RC.12 is published and frozen; any release
containing this work ships as **RC.13 or later**. Scope is preview scheduling only. PDF content, templates,
printing, saving, autosave, the database (schema 18) and the Personal Sync formats are unchanged.

## 1. Root cause (from Phase 34A)

`docs/phase34a-repository-performance-audit.md` §5 measured about 468–472 ms from an ordinary edit to the
refreshed preview, and about 496–503 ms for long documents. A fixed **450 ms trailing-only debounce**
(`PassdownPdfPreviewViewModel.DefaultDebounce`) applied to every kind of change and accounted for about 96 %
of that time. QuestPDF generation (about 7–23 ms warm) and PDFium rendering (3–5 ms) were small. Workspace
replacement (date load, Check Online, reloads) went through the same debounce. The "Updating…" chip turned on
as soon as the debounce *started*, so it flashed on every edit even though no work had begun (findings F01–F03).

## 2. Scheduling: before and after

| | Phase 33 (RC.12) | Phase 34B-1 |
|---|---|---|
| Ordinary edit after idle | wait 450 ms, then generate | **generate immediately** (leading edge) |
| Edits close together | each restarts a 450 ms wait | each restarts a **200 ms** quiet window. One trailing refresh builds the final state. |
| Generations per typing burst | 1 (after 450 ms) | **≤ 2**: first key and final state |
| Workspace replaced (date load, Check Online, reload) | debounced 450 ms | **immediate**, with no window, once the selected date's workspace is loaded |
| Overlapping scheduled generations | possible (results dropped by token) | **never**: requests during a generation are folded into one follow-up pass |
| "Updating…" chip | from debounce start, on every edit | only when a generation is **still running after 150 ms** |

All of this lives in one class, `PassdownPdfPreviewViewModel`. The roughly 30 `ScheduleAutosave` call sites
in `AssociateViewModel` are untouched and still funnel through `Preview.NotifyContentChanged()`.
`AssociateViewModel` changed in two places only: `OnWorkspaceChanged` and the restored-date path now call
`Preview.NotifyWorkspaceReplaced()`.

### 2.1 Leading and trailing refresh rules

State (UI thread only): `_quietWindowCts` (window open?), `_trailingRefreshPending`, `_activeRefresh` (the
refresh pass in flight) and `_refreshAgain`.

1. **`NotifyContentChanged()`** bumps the content version. While the preview is shown:
   - **No window open** → start a refresh pass now (leading edge) and open a 200 ms window.
   - **Window open** → cancel and restart the window, and set `_trailingRefreshPending`.
2. **Window elapses** (only the newest, uncancelled window counts) → close it. If a change arrived inside it,
   request one trailing refresh pass.
3. **`RequestRefreshPass()`** → if a pass is already generating, set `_refreshAgain` and return it. The pass loops
   once more after its current generation and captures the newest state at that moment. Otherwise start a pass.
   Scheduled preview generations therefore never run in parallel, and on a slow machine they never pile up.
4. **`NotifyWorkspaceReplaced()`** → bump the version, cancel any window (a trailing refresh would only
   duplicate this one) and request a pass immediately.
5. **Hidden preview** → nothing is scheduled. Hiding cancels the window, clears `_refreshAgain` and invalidates
   any in-flight result. Showing again requests a pass for the latest state.

The window length is still injectable (`debounce:`), and the delay function is still injectable (`delay:`), as
in Phase 33. The tests use `ManualDebounceScheduler`, so no test depends on real time.

### 2.2 Stale-generation handling

The Phase 33 safeguards are kept and tightened:

- Every generation takes a token (`_generation`). Only a result whose token is still current is displayed.
- A document is installed as `CurrentDocument` only if its content version is not older than the installed one.
  An identical fingerprint keeps the page, page number and zoom without re-rasterizing (no flicker).
- **New:** a date change records a version floor (`_minimumInstallVersion`). A generation captured before the
  date change is never installed or displayed. Phase 33 could briefly install such a late result as
  `CurrentDocument` after the date changed. Print was still protected by its date check, but the state was wrong.
- **New:** because scheduled passes are serialized, the only way two generations can overlap is Print / Save PDF
  running during a preview pass. Tokens and versions resolve that case (tested both ways).
- PDFium access is unchanged and still guarded by the global `PdfiumNative.Sync` lock. Rendering stays on the
  thread pool via `IPreviewWorkScheduler`.

### 2.3 Date switch

- `NotifySelectedDateChanged()` (unchanged entry point) immediately clears the page, the document, the caption
  and the page count. It shows "Loading this date's preview…", cancels the window and any queued follow-up pass,
  and sets the version floor.
- While the new date loads, the capture function reports *Waiting*. A refresh request during that time
  generates nothing.
- When the loaded workspace is assigned, `OnWorkspaceChanged` calls `NotifyWorkspaceReplaced()`, so the new
  date's PDF is generated **at once** (no 450 ms or 200 ms wait).
- A late result for the previous date can never replace, print or save as the new date (version floor plus token).
- Unsaved-draft protections, the blocked-switch restore (`_isRestoringSelectedDate`) and the flush-before-switch
  behavior are unchanged. The restore path now refreshes immediately as well.
- Section navigation (Overview, Products, Shows, Notes) does not notify the preview, so nothing regenerates.

### 2.4 "Updating…" indicator

- `Status` still becomes `Updating` when a generation actually starts, and no longer at debounce start.
  `IsUpdating` still reports that state.
- The chip binds to the new `IsUpdatingIndicatorVisible`. When `Status` enters `Updating`, a cancellable
  150 ms delay starts (`updatingIndicatorDelay:`, injectable). The chip appears only if the status is still
  `Updating`, the preview is still shown and a page is on screen. Any other status cancels the delay and hides
  the chip at once. Without a page, the centered status text (for example "Creating preview…" or "Loading this
  date's preview…") already says what is happening.
- Quick refreshes (≈ 20–60 ms on the Mac host) never show the chip, so the `LiveSetting="Polite"` region is
  not announced while typing. Slow generations announce it at most once per real generation (≤ 2 per burst),
  never once per keystroke.
- Errors and preflight messages are set directly by the generation result and are never delayed.
- A test that injects a fake window delay but no indicator delay gets an indicator delay that never elapses
  (it still honours cancellation), so tests never depend on real time.

## 3. PDF content, Print and Save PDF consistency

- Print, Save PDF, Create PDF and finalization still share the single export pipeline
  (`IAssociatePassdownPdfExportService`). No template, layout, page-break or content code changed.
- `EnsureLatestDocumentAsync()` (Print / Save PDF) still captures the editor state at click time. It reuses the
  current document only if nothing changed since, and otherwise generates. It now also cancels any pending
  quiet window, because its own generation already covers that state, so no duplicate trailing generation follows.
- If the user clicks while a leading refresh is still generating, Print or Save generates the newest state
  itself. The older preview result is discarded.
- While a new date is loading, Print and Save are disabled (`CanExport` requires a loaded workspace and no busy
  state). If they are invoked anyway, they are refused with "… still loading. Try again in a moment."
- **New:** if the date changes while Print or Save is still generating, nothing is printed or saved. The user
  sees "The selected date changed while the Passdown was being prepared. Nothing was printed or saved — try again."
- All three templates (Classic, Compact, Modern) were exercised by the benchmark (§4) and the existing Phase 33
  real-PDF tests.

## 4. Benchmarks (macOS development host — not Windows)

### 4.1 Methodology

Same harness as Phase 34A (scratch-only, never committed), rerun in one session for both builds:

- Apple M2 Pro, macOS 27.2, **Release**, headless Avalonia + Skia (real `WriteableBitmap`, no compositor).
- Real `AssociateViewModel`, `PassdownPdfPreviewViewModel`, QuestPDF export service, `PdfiumPdfRasterizer`,
  `AvaloniaPdfPageImageFactory`, `ThreadPoolPreviewWorkScheduler` and real `Task.Delay`. Fakes are used only for
  storage, printer and picker. Synthetic `BusyDayFixtures` give a 1-page PDF; the long variant adds 80 notes
  (2 pages Classic/Compact, 4 pages Modern). Fit-width 460 DIP at 150 % scaling.
- "Before" was built from an exported copy (`git archive 5cb71f5`), with the same harness code for both builds.
- 3 warm-ups, then 25 measured runs (15 for date switch, 10 for bursts). First preview: 5 fresh processes per build.
- One methodology change from Phase 34A: isolated actions are now spaced 300 ms apart (was 150 ms), so each
  starts after the 200 ms window has closed, as a real user's separate click would. The baseline is unaffected,
  because its 450 ms wait dominates either way.
- The "5 rapid toggles" scenario became **5 distinct attendance edits**. Five checkbox flips end on the same PDF
  the leading refresh already showed, so the identical-content check correctly keeps the page and there is no
  new image to time. Generation count for the toggle case is reported separately.
- Latency = input → `PageImage` assigned. The screen adds about one frame.

### 4.2 Results (milliseconds, median / p95; generations per action)

| Scenario | Before (450 ms trailing) | After (leading + 200 ms) | Gens before → after |
|---|---|---|---|
| Include in Passdown checkbox | 468.8 / 474.4 | **21.5 / 27.2** | 1 → 1 |
| Attendance edit | 468.3 / 474.2 | **22.6 / 27.0** | 1 → 1 |
| Operational Notes, single keystroke | 470.6 / 476.9 | **22.3 / 27.6** | 1 → 1 |
| Product Include toggle | 472.1 / 478.5 | **20.5 / 26.1** | 1 → 1 |
| Show selection toggle | 471.8 / 476.9 | **19.0 / 23.9** | 1 → 1 |
| Template switch | 468.2 / 475.0 | **20.5 / 25.1** | 1 → 1 |
| Date switch (instant fake load) | 471.2 / 476.3 | **20.7 / 26.0** | 1 → 1 |
| Section switch + keystroke | 468.5 / 476.0 | **25.1 / 27.7** | 1 → 1 |
| Long, multi-page: Daily Feature keystroke | 503.0 / 512.9 | **59.3 / 62.7** | 1 → 1 |
| Long, multi-page: checkbox | 508.0 / 514.5 | **63.5 / 68.7** | 1 → 1 |
| Typing burst, 15 keys @ 110 ms, from **last** key | 472.9 / 477.4 | **220.8 / 238.1** | 1 → **2** |
| 5 distinct edits @ 60 ms, from last edit | 471.3 / 478.1 | **221.8 / 227.3** | 1 → **2** |
| 5 checkbox toggles @ 60 ms (generations only) | — | — | 1 → **2** |
| First preview after view-model creation (5 processes) | 705.6 (685–770) | **226.4 (225–242)** | 1 → 1 |
| Section switch, view-model cost | 0.00 | 0.00 | 0 → 0 |

Stage micro-benchmarks (40 runs, unchanged code paths) show no regression in generation or rendering. Busy
Classic: generate 6.7 → 6.9 ms, render at 150 % 2.96 → 2.96 ms. Long Modern: generate 20.4 → 20.7 ms, render
3.21 → 3.20 ms. The differences are run-to-run noise.

### 4.3 Targets (Mac host)

| Target | Result |
|---|---|
| Isolated checkbox / selection ≤ 60 ms median (ideal 20–40) | **Met**: 19–25 ms |
| Long-document isolated change preferably < 80 ms median | **Met**: 59–64 ms |
| Final refresh after typing stops ≤ 260 ms median | **Met**: 221 ms (p95 238 ms) |
| ≤ 2 useful generations per 15-key burst | **Met**: exactly 2 |
| No regression in page rendering or UI responsiveness | **Met** (stage numbers unchanged; all work still off the UI thread) |

The long-document medians (59–64 ms) are slightly higher than Phase 34A's estimate of 40–55 ms. The extra is
QuestPDF generation for the 80-note document (about 20–45 ms warm, depending on how warm the JIT is between
actions), not scheduling. The first-preview figure still includes the about 100–125 ms cold QuestPDF start
(finding F04, a later Phase 34B step). These are development-host numbers and **do not establish Windows
performance**. Phase 34A estimated Windows laptops at 1.5–3× slower for the PDF work. The scheduling change
removes a fixed 450 ms on every machine.

## 5. Automated tests

New, all deterministic (`ManualDebounceScheduler` for the window and the chip, inline or gated preview work,
10 s hang guards):

- `Phase34B1PreviewResponsivenessTests` (24):
  - First change after idle refreshes without elapsing any delay.
  - An isolated change does not generate again when the window closes.
  - A 15-key burst gives 2 generations and shows the final text.
  - Changes inside the window restart it; only the newest window fires.
  - Rapid toggles during a held leading refresh are folded into one follow-up pass and end on the final state.
  - An older Print generation finishing late never replaces a newer preview, and Print still prints what it captured.
  - A date change clears the page and cancels the window.
  - A late previous-date generation is never installed.
  - Workspace replacement refreshes with no window.
  - Workspace replacement while the date is not loaded never generates.
  - Workspace replacement supersedes a typing window.
  - Hide cancels the trailing refresh; Show displays the latest content.
  - Page number and zoom survive leading and trailing refreshes.
  - Print with a pending trailing refresh prints the newest state with no duplicate generation.
  - Save PDF during a held leading refresh saves the newest state.
  - Print while the new date loads is refused.
  - Print when the date changes mid-generation prints nothing.
  - Quick refreshes never show the chip; a slow refresh shows it only after the delay and hides it when done.
  - The chip delay elapsing after Hide shows nothing.
  - Continuous typing with slow generations announces the chip at most once per generation.
  - Markup binds the chip to `IsUpdatingIndicatorVisible` and keeps `LiveSetting="Polite"`.
  - The 200 ms and 150 ms constants.
- `Phase34B1AssociatePreviewIntegrationTests` (7), with a real `AssociateViewModel`:
  - The first preview after load needs no window.
  - A date switch generates nothing while loading, then refreshes as soon as the workspace loads.
  - An Include in Passdown checkbox refreshes immediately.
  - Typing in Operational Notes is coalesced into ≤ 2 generations.
  - Section navigation during a window adds no generations.
  - Print right after typing prints the newest edit.
  - A failed preview generation does not affect autosave (the draft is saved, Saved state, content intact).

Adjusted Phase 33 tests (expectations updated for the new policy, guarantees kept):

- `RapidChanges_AreDebounced_IntoOneGeneration` → `RapidChanges_AreCoalesced_IntoLeadingAndTrailingGenerations`.
  It now expects the leading generation, then exactly one trailing generation of the final state.
- `StaleGeneration_FinishingLate_NeverReplacesTheNewerDocument`: the newer generation is now driven through
  `EnsureLatestDocumentAsync` (the Print/Save path), because two *scheduled* preview generations can no longer
  overlap. The assertion (a late older result never replaces the newer document) is unchanged.

All other Phase 33 preview, markup, real-PDF rendering, Create PDF and window tests pass unchanged. Totals are
recorded in the completion report and the local-test checklist.

## 6. Windows checklist

See `docs/releases/rc13-phase34b1-localtest-windows-checklist.md` (LOCAL-ONLY candidate
`RC13-Phase34B1-LOCALTEST`). Windows results are recorded there, separately from the Mac numbers above, and
nothing is marked passed until the product owner has run it.

## 7. Known limitations

- **Continuous typing longer than a pause**: the trailing refresh waits for a 200 ms pause. Someone typing
  without any pause for many seconds sees the first keystroke and then the final state only. Phase 33 behaved
  the same way, with 450 ms. No maximum-wait was added, to keep the policy small.
- **Superseded generations are not cancelled** (F14). A follow-up pass waits for the in-flight generation to
  finish (about 7–45 ms on the Mac, possibly 2–3× on Windows) before capturing the newest state. Passing a
  cancellation token into `GeneratePdf` remains optional future work.
- **Chip minimum display time**: on a machine where generation takes only slightly longer than 150 ms, the chip
  can appear briefly. No minimum on-screen time was added.
- **First preview** still includes the cold QuestPDF start (about 100–125 ms on the Mac). The prewarm is
  recommendation 3 of Phase 34B and was not done here.
- **DPI change without resize** (F13) and **printing blocking the PDFium lock** (F12) are unchanged.
- Mac numbers do not stand in for Windows. Narrator behavior, display scaling and real responsiveness need the
  Windows checklist.
