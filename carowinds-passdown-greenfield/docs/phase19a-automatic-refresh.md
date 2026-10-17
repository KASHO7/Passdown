# Phase 19A — Automatic Refresh & Live Data Propagation

Phase 19A makes the interactive app stay current during a normal day without pressing a Refresh or
Check Online button. It solves two separate problems with two separate mechanisms:

| Problem | Mechanism | Network? |
|---|---|---|
| Local edits (attendance, pre-solds, ERT, gate overrides, notes, synced changes) did not reach the Dashboard until its Refresh button was pressed | **Local data propagation** — one in-process change signal (`IDataChangeNotifier`) | Never |
| Official data (prices, hours, products) could be stale until Check Online was pressed | **Automatic official refresh** — one app-lifetime coordinator (`OfficialDataRefreshCoordinator`) | Yes, controlled |

A local edit never starts an online check. An online check never writes a draft.

Baseline: Phase 18F source `8020edb`, RC.2 `dbaa753`, RC.2 docs `f22d48e`. Schemas unchanged
(DB 15, Draft 4, Snapshot 2, SyncSchemaVersion 1). RC.2 artifacts are untouched; this is new shipped
code and needs a new release candidate number (`0.1.0-rc.3`) before it is released.

## What happens automatically

### Startup

1. The window opens with cached/local data. Nothing waits for the network.
2. On a background task, the coordinator:
   1. finalizes any eligible passdown (unchanged Phase 9B rule — local only);
   2. runs the **missed daily maintenance** (the due-gated Deep Refresh the 6 AM scheduled task runs,
      including Park Hours' month-ahead coverage) **only if** no Deep Refresh was attempted since
      today's 6:00 AM Eastern boundary, or Park Hours coverage has fallen behind. Before Phase 19A the
      startup catch-up re-swept the whole Park Hours coverage window on *every* launch, even minutes
      after the scheduled task;
   3. makes sure official data is fresh with **one comprehensive check** — unless every official
      source was already checked (attempted) within the **startup grace period (60 minutes)**, e.g. by
      the scheduled task or by step 2. That holds even if a source failed on that attempt: a failure
      minutes old is shown in the status line, not retried by repeating every request (found in the
      smoke run, where step 2's single Bring-A-Friend Duo error caused an immediate 47-request repeat).
      Check Online / Deep Refresh retry on demand; the four-hour policy retries automatically.
3. The shared status line shows "Checking official data…" while that runs and the app stays usable.

Example: the scheduled task finishes at 7:58 AM (or 7:30 AM) and the app opens at 8:00 AM → no
startup check and no maintenance re-run. The app opens at 9:30 AM after a 6:00 AM task → one quiet
comprehensive check.

### Every four hours while open

`OfficialRefreshPolicy.AutomaticInterval` = **4 hours** — the single authoritative constant (not a
setting). The next automatic check is due four hours after the most recent *qualifying comprehensive
check* — one that attempted every official source:

- this app's automatic check (startup or four-hourly);
- the scheduled task's run (it attempts every source once the daily boundary makes them due);
- a manual **Run Deep Refresh**.

So a manual Deep Refresh at 10:30 AM after an 8:00 AM startup check moves the next automatic check
from about 12:00 PM to about 2:30 PM. **Check Online does not reset the clock**: it checks one date and
never claims every source was checked.

The clock is derived from persisted `source_state` timestamps (UTC instants), so it also reflects work
done while the app was closed, survives restarts, and is unaffected by DST or time-zone changes.

### Today's and future Associate dates

When a date is shown in Associate (after its cached data is already on screen), the coordinator checks
that date quietly **only if** it is today or later and its date-specific official data is missing or
stale. "Stale" reuses the existing per-category freshness policy (daily at 6:00 AM Eastern): a cached
result is fresh when its latest attempt succeeded after the most recent 6:00 AM boundary — exactly when
the scheduled refresh would consider it not yet due. A failed latest attempt counts as stale.

The check uses Check Online's date-specific semantics (every dated source for that date, every catalog
source once) without the Deep Refresh coverage bootstrap. While it is online, Associate shows a muted
"Checking online for this date…"; nothing is disabled or blocked.

- Today usually needs no request: the startup/four-hour check already covers today through the next six
  days.
- A future date beyond that window is checked the first time it is opened each day.
- After an automatic date check completes, that date is not automatically checked again for
  15 minutes (`DateCheckCooldown`), so flipping between dates while offline cannot become a request loop.
- Changing the date cancels the previous date's pending or running check; a superseded request never
  starts and a late result is never applied to a different date.
- The date check waits for the startup check to finish first, so opening the app never fetches today twice.

### Local Dashboard propagation

Every committed local change publishes a `DataChange` on `IDataChangeNotifier`:

| Source | Change |
|---|---|
| `AssociatePassdownService` draft saves (projected attendance, pre-solds, North Gate / Avenue overrides and resets, notes, daily feature, products) | `AssociateDraft` + date |
| `ErtConfigurationService` monthly/override save or remove | `EarlyRideTime` |
| `OperationalNotesService` create/update/resolve | `OperationalNotes` |
| Personal Sync import / conflict resolution (forwarded from `ISyncLocalChangeNotifier`) | `PersonalSync` |
| Any online refresh finishing (automatic, Check Online, Deep Refresh) | `OfficialData` (+ date for a date check) |
| Startup finalization that finalized something | `FinalizedHistory` |
| Eastern day rolling over while the app is open | `OperatingDay` |

The single long-lived Dashboard view model reloads today's local projection on any change that may
affect today (a change naming another date is ignored). Reloads are coalesced: a burst of saves while a
reload runs produces one follow-up reload. There is no polling. A failed quiet reload keeps showing the
last good summary.

The Associate screen reacts to `OfficialData` for its selected date: it re-reads its local workspace
(no network) and applies it **only when the editor is clean**. With unsaved edits it waits until the
next successful autosave, then applies. If only verification times changed, it swaps the workspace in
without rebuilding product rows (focus and scroll are preserved). A reload is never an edit, so it never
triggers an autosave. Personal Sync changes keep their Phase 18B REL-004 path (clean editors reload;
dirty editors are never overwritten).

## What is manual

| Action | Where | What it does | Resets 4-hour clock? |
|---|---|---|---|
| **Check Online** | Associate, Dashboard | Immediately checks one date (every dated source for it + every catalog source), bypassing every automatic freshness/cooldown rule. Still bootstraps a Deep Refresh when coverage is insufficient (Phase 13D, unchanged). | No (yes only when it ran the bootstrap Deep Refresh) |
| **Run Deep Refresh** | Dashboard | Unchanged strongest recovery: every source unconditionally, Park Hours across the month-ahead coverage window. | **Yes** — the qualifying manual comprehensive action |

Both remain available at all times. If an automatic check is running inside the app, a manual action
waits for it and then runs in full (it is never reported as "already in progress" for in-app work). If
the scheduled task (another process) holds the refresh lock, the existing "A refresh is already in
progress" result is unchanged.

## What is never automatic

- **Past dates** are never checked online automatically, missing or stale. History navigation never
  causes network traffic.
- **Manual values are never overwritten.** Projected attendance, pre-solds, ERT, North Gate / Avenue
  overrides, manual hours, private buyout and display-price overrides live in the draft / ERT tables,
  which an online refresh never writes. Newer official data changes only the official values shown
  beside them; a gate default follows new official hours, an override stays.
- **Finalized History** (snapshots and frozen PDFs) is never touched by any refresh.
- A local edit never starts any online activity.

## Automatic vs Check Online vs Deep Refresh

| | Automatic comprehensive check | Associate date auto-check | Check Online | Deep Refresh |
|---|---|---|---|---|
| Trigger | startup (unless every source was checked within 60 min), then every 4 h | opening a today/future date with missing/stale data | user | user |
| Scope | every source, rolling window (today + 6 days), catalog once | one date, every dated source + catalog once | one date, every dated source + catalog once | every source; Park Hours to end of next month |
| Freshness-gated | by the 4-hour policy | by date freshness + 15 min cooldown | no | no |
| Writes `source_state` | yes | no | no | yes |
| Orchestrator | `RunComprehensiveRefreshAsync` (new, `RefreshJobType.Comprehensive`) | `RunUserForcedRefreshAsync` | `RunUserForcedRefreshAsync` (or bootstrap Deep) | `RunDeepRefreshAsync(bypassFreshness: true)` |

The four-hour check is deliberately not a Deep Refresh: it does not re-sweep ~60 Park Hours dates.
Month-ahead coverage stays with the 6 AM scheduled task / startup maintenance / manual Deep Refresh.

## Official-data status line

One shared `OfficialDataStatusViewModel` (single instance) feeds the Dashboard header (where the old
Refresh button was) and the Associate status area. It is derived from persisted `source_state` rows, so
it reflects the scheduled task and previous sessions too.

| State | Text |
|---|---|
| Fresh install, nothing attempted | `Official data not checked yet` |
| Comprehensive check (or Deep Refresh / startup maintenance) running in the app | `Checking official data…` |
| Every source succeeded on its latest attempt | `Official data checked 12:41 PM` |
| Some sources failed | `Some official data couldn't be updated · Last checked 12:41 PM` |
| Every source failed | `Official data couldn't be updated · Last checked 12:41 PM` |

- "Checked", never "updated": the time is when every source was last verified, whether or not a value
  changed (it is the oldest of the sources' latest successes).
- Eastern operational time, US format (`PresentationCulture`), with the date added when the check was
  on an earlier day (`Oct 14, 12:41 PM`). No timestamp is ever shown before a first attempt.
- Failure detail is a short source-name list ("Not updated: Fast Lane, Promotions") shown in a second
  muted line/tooltip. No URLs, HTTP errors or stack traces — those stay in the app log.
- A date-specific check never changes this line (it does not check every source).
- Screen readers get the same text (plus detail) as the automation name.

## Dashboard Refresh button — removed

Investigation: `OperationsDashboardViewModel` was constructed once for the app's lifetime and loaded
today's snapshot only in its constructor; nothing told it about later saves. The header "Refresh"
button only re-ran that same local load (it never checked the website), so it was both necessary and
misleading.

With local propagation in place (proven end to end in `Phase19ALocalPropagationTests`), the header
button was removed. The same local reload is kept only as a **"Try again"** button inside the error
message shown when today's summary could not be loaded at all (e.g. the database was briefly locked at
startup) — the one case with nothing on screen to keep showing. It also recovers by itself on the next
local change. The header's "Updated 12:45 PM" text (the time of the last *local* reload) was removed
with it, so the only time in the header is the official-data "checked" time.

## Concurrency

`IOfficialRefreshGate` is the single in-process entry point for every online refresh (automatic
comprehensive, startup maintenance, Associate auto-check, Check Online, Deep Refresh). Holding it also
holds the existing cross-process `FileScheduledRefreshLock`.

- Two online refreshes never overlap inside the app.
- Automatic work and explicit user actions **wait** for in-process work; after waiting, automatic work
  re-reads persisted state and skips if what it wanted was just done (a waiting four-hour check skips
  after a manual Deep Refresh; a waiting date check skips if the comprehensive check covered the date).
- A cancelled waiter never starts work, even if cancellation races the release
  (`SemaphoreSlim` observes cancellation asynchronously; the gate re-checks the token after acquiring).
- If the scheduled task process holds the lock: automatic work skips (and is re-evaluated within
  5 minutes), manual actions report "already in progress" exactly as before.
- Phase 20C: one low-priority kind, `HistoricalBackfill`, also passes through the gate. It never
  queues and never starts while other work waits; when any operational kind starts trying to enter,
  the gate raises `PriorityWorkWaiting` and the backfill cancels its in-flight request and releases, so
  operational work never waits out a historical request. The coordinator ignores it entirely (no
  "Checking official data…", no `OfficialData` change). See `docs/phase20c-historical-backfill.md`.

## Lifetime, sleep/resume, offline

- One coordinator per interactive app lifetime, started once after the main window is assigned and
  stopped during shutdown (bounded to 2 s; cancelled operations never overwrite last-known-good data or
  `source_state`). Navigation never creates timers; view models never own one.
- Headless `--scheduled-refresh` never resolves or starts it; its run-once semantics are unchanged.
  Automatic date checks are refused unless the coordinator is running.
- The loop never sleeps longer than 5 minutes (`MaxSleepSlice`) before comparing the wall clock — an
  in-memory comparison, not a database poll. After a PC sleeps through one or more intervals, at most
  **one** check runs on wake; missed intervals are never replayed.
- Offline: the app starts and stays usable on cached data; the check fails per source, last-known-good
  values and last-success timestamps are kept, the status line shows the failure, and — because a
  failed attempt still counts as the latest comprehensive attempt — the next automatic try is four hours
  later (no rapid retry loop). Check Online / Deep Refresh retry immediately on demand.
- A persisted check time more than 5 minutes in the future (clock moved backward) is distrusted, so it
  can never postpone checks indefinitely.

## PDF / Copy consistency

Associate, Create PDF and the Phase 18F Copy actions all read the same `AssociateViewModel.Workspace`.
A background update replaces that workspace; there is no separate refresh cache for PDF or clipboard.

## Logging and diagnostics

App log (`app-yyyyMMdd.log`) lines added: automatic refresh started (interval/grace); startup decision
(needed / skipped as fresh within grace / maintenance skipped because today's ran / maintenance needed);
comprehensive check started (trigger, last check), completed, partially failed and failed (source ids
only); waiting behind another in-process run; skipped because the scheduled task holds the lock; skipped
after a manual refresh reset the schedule; manual Deep Refresh moving the schedule; Associate date
auto-check started / skipped as fresh / completed; Dashboard local invalidation (change kind only);
operating-day rollover; stop. No note bodies, draft text, clipboard contents or payloads are logged.

The diagnostics `summary.txt` gains an "Automatic official-data refresh" section: interval, aggregate
status, last check, last comprehensive check, last fully successful check, next automatic check
estimate, and the ids of sources not updated on their last attempt.

## Code map

| Concern | Type |
|---|---|
| Policy constants and decisions | `Application/Refresh/OfficialRefreshPolicy.cs` |
| Aggregate status from `source_state` | `Application/Refresh/OfficialDataStatus.cs` |
| Date-specific freshness | `Application/Refresh/DateCheckFreshness.cs` |
| Coordinator (startup, 4 h loop, date checks, status) | `Application/Refresh/OfficialDataRefreshCoordinator.cs` |
| Comprehensive orchestrator operation | `IRefreshOrchestrator.RunComprehensiveRefreshAsync`, `RefreshJobType.Comprehensive` |
| In-process gate (+ cross-process lock) | `Application/Concurrency/OfficialRefreshGate.cs` |
| Local change signal | `Application/Changes/DataChangeNotifier.cs` |
| Status presentation / shared VM | `Desktop/Presentation/OfficialDataStatusPresentation.cs`, `Desktop/ViewModels/OfficialDataStatusViewModel.cs` |
| UI-thread marshalling seam | `Desktop/Services/IUiDispatcher.cs` |
| Retired | `Desktop/Startup/StartupCatchUpRefresh.cs` (absorbed by the coordinator; its tests ported) |

Domain is untouched. No schema or payload version changed: the four-hour clock, the status line and
date freshness are all derived from existing `source_state`, `dated_results` and `deep_refresh_status`
rows.

## Tests

New: `OfficialDataRefreshCoordinatorTests` (50 — four-hour boundary at 3h59m/4h, manual reset T0+2h →
T0+6h, Check Online not resetting, sleep/resume once, startup missing/stale/just-refreshed/recent-failure-not-repeated,
missed maintenance ordering, cross-process lock, finalization/runner failures, offline full failure,
partial failure, no rapid retry, concurrency, Stop, idempotent Start, loop, day rollover, date checks
today/future/past × missing/stale/fresh, cooldown, supersession, rapid switching, covered-while-waiting,
not-running), `Phase19ASupportTests` (25 — gate, status aggregation, date freshness, notifier, service
publishing, gated Check Online/Deep Refresh, diagnostics), `RefreshOrchestratorTests` (+2 comprehensive),
`Phase19ALocalPropagationTests` (8 — real SQLite end to end: attendance, pre-solds, ERT, Avenue/North
Gate set + reset, notes, Personal Sync, other-date isolation; each asserts no online work),
`AssociateViewModelAutomaticCheckTests` (11), `Phase19ADashboardAndStatusTests` (27). Updated: Dashboard
view/real-window header tests (Refresh button → status line), two constructor-shape guards
(`OperationalNotesService` gained the local-only notifier), the Phase 9E startup catch-up integration
test (now through the coordinator). Removed: `StartupCatchUpRefreshTests` (9, ported).

Deterministic baseline 2620/2620 → **2735/2735** (125 Domain + 748 Application + 307 Documents + 752
Infrastructure + 765 Ui together + 26 + 10 + 2 real-window classes alone), Release 0 warnings / 0 errors.

## Manual smoke test (macOS development host)

The Release build was launched once for 75 s and observed through its log; no GUI interaction tool was
available, so nothing was clicked or typed.

Observed (live official sources): window opened, database ready, coordinator started (interval 4 h,
grace 60 min) after the window; startup finalized one eligible passdown and published a
`FinalizedHistory` change (Dashboard reloaded locally); missed daily maintenance ran (87 checks,
80 succeeded, 7 Bring-A-Friend Duo errors for the rolling window — the source reported "couldn't check
availability"); the Dashboard reloaded on `OfficialData`. Startup then also ran a 47-check comprehensive
pass (40 succeeded, the same 7 failed) — the duplicate this exposed was fixed (startup now skips when
every source was attempted within the grace, whatever the outcome) and is covered by
`Startup_MaintenanceAttemptsEverySourceWithAFailure_NoSecondFullCheck`. Shutdown via process kill.

Not executed: steps that need typing/clicking (edits propagating, Associate date navigation, Check
Online, Deep Refresh, status-line visuals) — these are covered by the deterministic tests above and by
the Windows checklist.

Incident: the launch was intended to use an isolated profile by overriding `HOME`, but on macOS the app
still used `~/Library/Application Support/Carowinds Passdown`, an earlier development database (schema 7,
last used Aug 22). It was migrated to schema 15, 08/21/2026 was recovery-finalized into its History, and
official data was cached there. Personal Sync was disabled, so nothing left the machine. No backup
existed; the file was left as is.

## Remaining limitations

- No "New official data available" marker beside manual overrides: the draft does not record which
  official value an override was entered against, so "newer" cannot be determined without inventing
  override semantics. Manual values are always kept, and the current official value/default is shown
  alongside as before.
- No network-change monitoring: after connectivity returns, the next automatic attempt is the normal
  four-hour one (or the next app start); Check Online/Deep Refresh retry immediately.
- ~~The Operations Calendar is not live-updated by local changes~~ — resolved in Phase 20C: it quietly
  re-reads the displayed month on any relevant local change (including historical backfill), no polling.
- Product rows keep showing their earlier verification time when only verification times changed (the
  workspace — and therefore PDF/Copy — has the new one); the next date load refreshes them.
- An automatic check runs while the app is open but minimized — intended (it is quiet and bounded).

## Windows validation checklist (Phase 19A)

Run on Windows 11 x64 with a build containing this phase (a future `0.1.0-rc.3`, not RC.2). Nothing here
has been executed on Windows. Logs: `%LOCALAPPDATA%\Carowinds Passdown\logs\app-yyyyMMdd.log`.

| # | Check | How | Status |
|---|---|---|---|
| 1 | Startup background refresh | Launch with the 6 AM task not run today (or > 60 min ago). Status shows "Checking official data…", then "Official data checked h:mm AM/PM". Log: "Startup official-data check needed", "Automatic official-data refresh started (startup …)". | NOT EXECUTED |
| 2 | App remains responsive during it | Navigate, type in Associate, open History while #1 runs. | NOT EXECUTED |
| 3 | Startup skips right after the scheduled task | Run `Run Refresh Now`/scheduled task, then launch within 60 min. Log: "Startup official-data check skipped …" and "Startup catch-up maintenance skipped …". No second sweep. | NOT EXECUTED |
| 4 | Four-hour behavior (without waiting 4 h) | Launch; note the checked time. Set the Windows clock forward 4 h (or sleep the PC > 4 h); within ≤ 5 min one "four-hour schedule" check runs. Restore the clock. | NOT EXECUTED |
| 5 | Manual Deep Refresh resets the schedule | Run Deep Refresh; log shows "Manual Deep Refresh finished; … next check about …" ≈ 4 h later. | NOT EXECUTED |
| 6 | Dashboard local propagation | Edit projected attendance, pre-solds, ERT (monthly + override), Avenue and North Gate overrides (set and reset) for today in Associate; switch to Dashboard — each value already updated, no button pressed. | NOT EXECUTED |
| 7 | Dashboard Refresh removed | Header shows the status line, no Refresh button; at 960×640 the status text is fully visible. | NOT EXECUTED |
| 8 | Today's Associate auto-check | Delete/age today's data (or launch after midnight before 6 AM run): opening today shows cached data, then "Checking online for this date…", then updates. | NOT EXECUTED |
| 9 | Future stale date auto-check | Open a date > 7 days out: one quiet check; reopen within 15 min: no request (log "skipped"). | NOT EXECUTED |
| 10 | Past date: no auto-check | Open yesterday / a History-era date: no "date auto-check started" log line. | NOT EXECUTED |
| 11 | Check Online | Works on any date; while an automatic check runs it waits, then runs. | NOT EXECUTED |
| 12 | Deep Refresh | Works; status shows "Checking official data…" while it runs. | NOT EXECUTED |
| 13 | Freshness indicator states | Not checked yet (fresh profile) → checking → checked; partial failure text + source names. | NOT EXECUTED |
| 14 | Offline behavior | Disconnect network, launch: app opens on cached data, status shows "couldn't be updated · Last checked …", no repeated attempts in the log over the next hour. | NOT EXECUTED |
| 15 | Sleep/resume | Sleep > 4 h, wake: exactly one check within ~5 min. | NOT EXECUTED |
| 16 | Task Scheduler coexistence | Trigger the scheduled task while the app is open: no overlapping run (scheduled exit code 4 or app logs a skip/wait); app keeps working. | NOT EXECUTED |
| 17 | Personal Sync → Dashboard | Change today's attendance on PC B; after sync on PC A the Dashboard updates without a button. Dirty Associate editor on PC A is not overwritten (REL-004). | NOT EXECUTED |
| 18 | No duplicate refresh processes | Task Manager / logs show no second refresh while one runs; closing the app during a check exits within ~2 s. | NOT EXECUTED |
| 19 | PDF/Copy match the screen after a background update | After a background price change, Create PDF and Copy → Today's Pricing show the same price as Associate. | NOT EXECUTED |
