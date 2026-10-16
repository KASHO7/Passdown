# Phase 20C — Spell-check navigation cleanup + current-year historical backfill

Two independent workstreams. They share no code, no state and no events: a spell-check failure cannot
affect retrieval, and a backfill failure cannot affect editing.

Baseline: RC.4 docs commit `7c30da1` (source as of Phase 20A `588cd93`). Schemas unchanged: DB **16**,
Draft **4**, Snapshot **2**, SyncSchemaVersion **1** — no migration was needed. This changes shipped
code, so the next candidate is **`0.1.0-rc.5`** (not packaged in this phase; RC.4 is not rebuilt).

---

## Part A — Spell-check underline lingering after navigation

### Root cause

Confirmed with a failing real-window test before any fix (`SpellCheckNavigationLifecycleTests`).

- The underline is a `SpellCheckAdorner` placed in the **window's** adorner layer
  (`AdornerLayer.SetAdorner`) — outside the TextBox's own visual subtree.
- The shell navigates by toggling `IsVisible` on the Border wrapping each section
  (`MainWindow.axaml`); the section views live for the app's lifetime and are **never detached**.
- Avalonia removes an adorner only when its adorned element leaves the visual tree. Hiding an ancestor
  does not, so the adorner kept rendering at its last position over Dashboard / Calendar / Products —
  until a later edit of the word re-rendered it with no issues.
- A secondary gap: the 350 ms debounce posted its check to the UI thread even for a hidden editor, and
  a dictionary change re-checked hidden editors too, so a stale check could re-draw after navigating.

It was **not** a cached control instance, a surviving Windows COM callback, or an overlay window. A
plain "remove from tree" scenario already cleaned up correctly — the test proving that was kept.

### Fix (lifecycle ownership, not a redraw)

`SpellCheckController` now owns the adorner's presence. The editor is **live** only while attached to
a visual tree *and* effectively visible:

| Event | Effect |
|---|---|
| Becomes not live (section hidden, detached, window closed) | pending debounce cancelled; adorner removed from the layer (also from the layer captured at attach time, so a detach can never orphan it); dictionary subscription dropped (the shared service no longer references the editor); issues cleared. **Text untouched.** |
| Becomes live again | adorner re-attached, dictionary re-subscribed, a fresh debounced check of the (unchanged) text |
| Late debounced check / dictionary change | ignored unless the editor is still live — a result can never render over another screen |

Avalonia 12.1.1's `IsEffectivelyVisibleChanged` is internal, so the controller watches
`IsVisibleProperty` on the editor and each visual ancestor, collected on attach and released on detach
(re-collected on every attach, so a re-parented editor never listens to old parents).

Unchanged: Windows native backend, offline/local privacy, 350 ms debounce, suggestions, Ignore, Add to
dictionary, Cut/Copy/Paste, domain allow-list, non-blocking behavior, no text logged.

Known limitation (pre-existing, not navigation): while the Season Price History *overlay* is open over
Associate, Associate is still visible underneath, so an underline in a visible Associate field still
draws above the dimmed overlay. Added to the Windows checklist to judge whether it matters.

---

## Part B — Current-year historical park-hours backfill

### Source audit

| Source | Past dates? | Decision |
|---|---|---|
| **Park Hours** (CloudFront `operating-hours/park/30?date=yyyyMMdd`) | **Yes**, within a retention window. Live 2026-10-16: 2026-03-25 → yesterday served normally (open/closed/water park); 2026-03-24 and earlier → HTTP 404 `{"error":"Not Found",…}` | **Eligible** (A) |
| Park Hours monthly form (`?date=yyyyMM`) | **No** — a past month returns only its last day; the current month returns only tomorrow onward | Not used |
| Dated Admission, Dated Fast Lane, Dated Haunted / SCarowinds, Bring-A-Friend, Bring-A-Friend Duo | No — sale calendars; every adapter short-circuits a past date with no request (`requestedDate < today`) | Skip (B) |
| Season Passes, Season Pass / General Add-Ons, Promotions, Military / First Responder | Current-state catalogs | Not a candidate (C) |
| Season Pass price history | Own observation model (Phase 20A) | Untouched (D) |

So the backfill is one request **per date** (the only form that serves history), park hours only.

### Scope ("current year")

`HistoricalBackfillPolicy.TargetRange(todayEastern)` = **January 1 of the current Eastern calendar year
through yesterday**. Today and later belong to the operational refresh. January 1 has no scope
(`NotApplicable`). Earlier years are never backfilled automatically; their stored rows are kept
(retention is unchanged — no deletion or archival anywhere).

### What counts as done (resumability without a progress blob)

Progress is derived each session from the normal `dated_results` rows
(`HistoricalBackfillPolicy.IsComplete`): a date is complete when its park-hours row has a successful
completion (Verified, Closed, NotPublished, Unavailable — the existing semantics) **checked on or after
that date**. A value captured only as a forecast before the date is eligible for one post-date
confirmation; an error-only row is never complete. Completion means "every eligible date reached a
known official state", not "N rows of hours" — closed days are complete as `Closed`.

### No fabrication

- Stored values come only from the official answer for that exact date, through the same
  `ParkHoursSource` parsing as the operational refresh (confirmed-date check included).
- A past date the feed answers "Not Found" for is **not stored** in any form. `ParkHoursSource` still
  reports it as `Error` (so the operational path is unchanged and a Check Online on such a date can
  never replace stored hours); the history entry point additionally returns a structured
  `NotRetainedBySource` flag — true only for the feed's JSON 404 body on a past date, never for an
  outage, an HTML error page or a network failure.
- Dates older than that boundary are left untouched (not marked, not guessed). The boundary is
  re-probed with **one** request per session, so a later change in the feed's window is noticed.
- A weaker past-date answer (NotPublished/Unavailable) never replaces stored Verified/Closed hours.
- Failures write nothing at all — not even a latest-attempt row — so they can never displace data.

### Priority and coexistence

1. The backfill starts only after `IOfficialDataRefreshCoordinator.StartupCompletion` (startup
   finalization, missed maintenance incl. month-ahead coverage, the startup freshness check — i.e.
   today, the next days and the normal horizon), plus a 20 s quiet delay.
2. Every request goes through the Phase 19A `IOfficialRefreshGate` as the new low-priority kind
   `OfficialRefreshKind.HistoricalBackfill` (`IsLowPriority`, not `IsComprehensive`):
   - it never queues for the gate and never starts while operational work holds or wants it
     (`HasPriorityWorkWaiting`);
   - when any operational kind (Check Online / Associate date auto-check, Deep Refresh, the automatic
     comprehensive check, startup maintenance) starts trying to enter, the gate raises
     `PriorityWorkWaiting`; the backfill cancels its in-flight request (nothing is written), releases,
     waits until the gate is idle plus 15 s, and retries that date. Operational work therefore never
     waits out a historical request, and the two never run concurrently;
   - holding the gate also holds the cross-process lock, so it cannot overlap the 6 AM task either
     (if the task holds the lock, history waits).
3. Within history: **newest first** (yesterday, then the day before, … January 1). When the Operations
   Calendar shows a current-year month with missing dates, that month goes next (newest first within
   it) — still behind all operational work, and viewing a month never starts a request by itself.
4. Pacing: one request per date, 1 s apart; three consecutive failures (offline/outage) end the session
   (resumes next start); a failed date is not retried in the same session. No network-change detector.
5. Interactive only. The headless `--scheduled-refresh` host never resolves or starts it. One session
   per app launch; shutdown cancels it within the existing 2 s refresh-stop bound.

### Operational status isolation

`HistoricalBackfillStatus` is separate from `OfficialDataStatus`. The coordinator ignores low-priority
gate activity: no "Checking official data…", no status recomputation, and no `OfficialData` change (so
the Dashboard never reloads for history). The backfill publishes its own
`DataChangeKind.HistoricalOfficialData` (one per touched month per batch of 7 stored dates, naming the
newest stored date in that month).

### Operations Calendar live update

`OperationsCalendarViewModel` now subscribes to `IDataChangeNotifier` (via `IUiDispatcher`): any local
change that may affect the displayed grid — historical data, any online refresh, drafts, notes, sync,
the operating-day rollover — triggers a **quiet, coalesced** re-read of the displayed month (no loading
bar, month and selection kept). Changes naming a date outside the grid are ignored. No polling. This
retires the Phase 19A limitation "the Operations Calendar is not live-updated". Associate also reloads
when a historical change names the past date it is showing (same clean-editor rules as `OfficialData`).

### Status line

A single muted line under the Calendar's month title (no card), tooltip = detail:

| State | Text |
|---|---|
| Not started / Jan 1 | hidden |
| Running, nothing checked yet | `Loading this year's history…` |
| Running | `Historical data: 84 of 127 dates checked` — denominator fixed at session start = dates that session set out to check |
| Done | `Historical data up to date` |
| Partial | `Historical data partially available` + detail: "Official park hours on or before Mar 24 are no longer published." and/or "Some dates couldn't be checked; they'll be tried again the next time the app starts." |

### Storage (measured)

Normalized rows only — the same `dated_results` columns the operational refresh writes (status,
confirmed date, checked time, short detail, source name/URL, and a ≤ 119-byte hours payload). The
~28–30 KB live response (`venues`, `shows`, `events`) is never stored. No new table or index. No VACUUM
added (none existed; compaction stays deliberate).

| Measurement | Rows | DB before | DB after | Added | Per date |
|---|---|---|---|---|---|
| **Live isolated smoke, 2026-10-16** (real feed, scratch DB) | 205 (140 Verified, 65 Closed) | 155,648 B | 274,432 B | **118,784 B (116 KB)** | **579 B** |
| Test, observed retention window (feed-shaped responses with ~28 KB venues) | 205 | 155,648 B | 274,432 B | 116 KB | 579 B |
| Test, every past date retained (full-year projection) | 288 | 155,648 B | 319,488 B | 160 KB | 569 B |

Projected full calendar year: **~205 KB**. Regression guard
(`Phase20CHistoricalBackfillSqliteTests`): < 2 KB per stored date, payload JSON < 200 B, no raw venue
marker anywhere in the file, table/index count unchanged.

### Logging and diagnostics

App log (Information): start / resume (pending, in scope, already stored), "no longer publishes"
boundary (count of dates), yielded to operational work, paused after consecutive failures, finished
(checked / failed / complete / boundary / yields), stopped on close. Per-date lines are Debug only.
Diagnostics `summary.txt` gains "Historical park-hours backfill": target year, status, past dates in
scope, dates holding official hours, remaining, this session's checked/queued/failed, the no-longer-
published boundary, last activity — counts and dates only, never rows.

### Personal Sync

Not synced (every device backfills independently from the official source; caches/official-derived
state are not synced). SyncSchemaVersion unchanged. The 404 boundary means dates older than the feed's
window cannot be re-acquired; a PC that kept the app running earlier in the year keeps those rows
locally. Whether syncing them is worth it is a possible future decision — not done here.

### Code map

| Concern | Type |
|---|---|
| Scope / completeness rules, pacing options | `Application/HistoricalBackfill/HistoricalBackfillPolicy.cs` |
| Status | `Application/HistoricalBackfill/HistoricalBackfillStatus.cs` |
| Service | `Application/HistoricalBackfill/HistoricalBackfillService.cs` |
| Past-date retrieval seam | `Application/HistoricalBackfill/IParkHoursHistorySource.cs`, implemented by `Infrastructure/Retrieval/Sources/ParkHoursSource.cs` |
| Low-priority gate kind + preemption | `Application/Concurrency/OfficialRefreshGate.cs` |
| Coordinator isolation, `StartupCompletion` on the interface | `Application/Refresh/OfficialDataRefreshCoordinator.cs` |
| Change kind | `Application/Changes/DataChangeNotifier.cs` (`HistoricalOfficialData`) |
| Calendar live reload, month promotion, status line | `Desktop/ViewModels/OperationsCalendarViewModel.cs`, `Desktop/Views/OperationsCalendarView.axaml`, `Desktop/Presentation/HistoricalBackfillStatusPresentation.cs` |
| Composition, start/stop | `Desktop/App.axaml.cs` |
| Diagnostics | `Application/Diagnostics/DiagnosticReport*.cs` |

---

## Tests added (+59; deterministic 2893 → 2952)

- `SpellCheckNavigationLifecycleTests` (7, real headless Window — run alone, see partition below):
  underline → navigate away removes it with text untouched; pending check cancelled by navigation never
  renders; navigate back re-checks; 5 cycles never stack adorners or dictionary handlers; hidden editor
  ignores dictionary changes; detach/re-add; window close with a pending check.
- `HistoricalBackfillServiceTests` (27): scope theory (Oct 16, Dec 31, Jan 2, Jan 1); fresh install
  newest-first, never today/future; retention boundary probed once, nothing stored before it, rerun
  re-probes only the boundary; Jan–May stored → only June onward, stored rows untouched; interrupted
  after 40 → resume with no repeated requests, third run makes none; forecast-only value re-confirmed
  once; Closed/NotPublished stored with existing semantics; weaker answer never replaces hours; offline
  stops after 3, writes nothing, later session resumes; one failed date skipped and LKG kept; new year
  targets only the new year and keeps the old; Jan 1 not applicable; Dec 31 still current year; viewed
  month promoted; other months ignored; waits for operational startup; preemption by DateCheck /
  DeepRefresh / Comprehensive / StartupMaintenance (in-flight request cancelled, nothing written, no
  concurrency, date re-checked); never starts while operational work holds the gate; waits for the
  cross-process lock; changes are historical-only and batched; gate contract.
- `OfficialDataRefreshCoordinatorTests` (+2): fresh-install ordering (maintenance/comprehensive before
  any history request); backfill gate activity never changes the operational status or publishes
  `OfficialData`.
- `ParkHoursSourceTests` (+4): JSON 404 past → not retained but still `Error`; 404 today/future → not a
  boundary; HTML 503/404 and network failure never mistaken for it; historical parse equals operational.
- `Phase20CHistoricalBackfillSqliteTests` (5, real SQLite + real adapter): storage measurement theory
  (two scenarios), normalized shape incl. water park/closed, idempotent rerun (row count and file size
  unchanged), offline writes nothing and keeps an existing value.
- `Phase20CHistoricalCalendarTests` (12): viewed-month reload quiet and position-preserving; other
  month ignored; other local change kinds refresh; burst coalesces; month promotion; status line
  follows the service; partial-state wording; markup (muted header line); composition (interactive
  only, started after the coordinator, stopped at shutdown).
- `DiagnosticReportFormatterTests` (+2).

Deterministic partition (Release, `Category!=LiveSmoke`): 136 Domain + 873 Application + 309 Documents
+ 782 Infrastructure + 807 Ui together + 26 `OperationsCalendarViewTests` + 10
`Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + **7 `SpellCheckNavigationLifecycleTests`**
alone = **2952/2952**. The new real-Window class hits the same pre-existing headless thread-affinity
issue ("a different thread owns it") when mixed with the rest of `Ui.Tests`, so it joins the run-alone
classes (3/3 clean alone).

## Isolated smoke test

No GUI launch (on macOS the app cannot be redirected away from the real profile). A scratch console
harness used real migrations (→ 16), the real `RetrievalHttpClient` + `ParkHoursSource` against the live
feed, a scratch DB under the session scratchpad, and production pacing (1 s): 206 requests in 295 s,
205 stored, boundary 2026-03-24 found with one probe, a simulated Check Online mid-run acquired the gate
in 0 ms, second launch = 1 request (boundary) and no new rows. Real profile DB mtime/size identical
before and after.

## Windows validation checklist (RC.5)

Nothing here has run on Windows. Logs: `%LOCALAPPDATA%\Carowinds Passdown\logs\app-yyyyMMdd.log`.

| # | Check | Status |
|---|---|---|
| S1 | Type a misspelling in Associate notes; underline appears; switch to Dashboard / Operations Calendar / Products — **no red squiggle anywhere** | NOT EXECUTED (PASS BY TEST) |
| S2 | Type a misspelling and switch screens within ~⅓ s — no underline appears later on the other screen | NOT EXECUTED (PASS BY TEST) |
| S3 | Return to Associate — the same misspelling is underlined again; text unchanged | NOT EXECUTED (PASS BY TEST) |
| S4 | Switch back and forth 10× — no doubled/offset squiggles; right-click menu still offers suggestions/Ignore/Add | NOT EXECUTED (PASS BY TEST) |
| S5 | Operational Notes editor: same as S1/S3 | NOT EXECUTED |
| S6 | Open Price History overlay over Associate with a visible misspelling — judge whether the underline over the dim overlay is acceptable | NOT EXECUTED |
| H1 | Fresh profile: app opens immediately; status shows checking/checked; Calendar shows "Loading this year's history…" ~20 s after startup work | NOT EXECUTED |
| H2 | Log order: startup maintenance / startup check lines before "Historical backfill starting" | NOT EXECUTED (PASS BY TEST) |
| H3 | Calendar on a past month (e.g. June) fills in while you watch, without restart; navigating months stays responsive | NOT EXECUTED |
| H4 | Press Check Online / Deep Refresh during backfill — they start at once; log shows "yielded"; no overlap | NOT EXECUTED (PASS BY TEST) |
| H5 | Close mid-backfill, reopen — "resuming … still to check"; no completed date requested again | NOT EXECUTED (PASS BY TEST) |
| H6 | Final state "Historical data partially available" with tooltip naming the no-longer-published date (feed window), or "up to date" | NOT EXECUTED |
| H7 | DB size grows by roughly 0.1–0.2 MB, not megabytes (`summary.txt` file size) | NOT EXECUTED (measured on build host) |
| H8 | Offline launch: app usable; history pauses after 3 failures; operational status unaffected by history | NOT EXECUTED (PASS BY TEST) |
| H9 | 6 AM scheduled task log has no historical lines; task unaffected | NOT EXECUTED (PASS BY TEST) |
| H10 | Dashboard does not reload/flicker during backfill; official-data status line unchanged by it | NOT EXECUTED (PASS BY TEST) |
| H11 | Diagnostics export contains the "Historical park-hours backfill" section | NOT EXECUTED (PASS BY TEST) |

## Remaining limitations

- Official history before the feed's retention window (2026-03-24 and earlier when investigated) cannot
  be obtained from any official source; those dates stay empty unless a device stored them earlier.
- The feed's window is observed, not documented by Six Flags; contiguity is assumed for one session
  (everything older than a "Not Found" past date is skipped that session) and re-probed every launch.
- One backfill session per launch; an app left open across midnight picks up new past dates through
  the normal operational refresh (which already stores each day as it happens), not the backfill.
- Rows stored are local only (not synced).
- The Price History overlay limitation in Part A.
