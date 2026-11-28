# Phase 36C — Associate workspace maintainability: Early Ride Time editor extraction and UI-thread seam

Status: implemented locally / awaiting Windows validation (local test candidate `RC15-Phase36C-LOCALTEST`) / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `5384663` (Phase 36B) |
| Ending commits | `07ae59b` (36C-1 ERT extraction), `15a31d2` (36C-2 dispatcher seam), ``18fac73`` (36C-3 tests), plus this record's documentation commit (hash in the completion report) |
| Reviewed source baseline | ``18fac73`` (the commit the local test candidate was built from) |
| Dirty-tree caveats | Clean tree at start and when the candidate was built |
| Authorization | Owner brief "Phase 36C — Associate Workspace Maintainability" (2026-11-27), from the [Phase 36A](phase36a-current-architecture-technical-debt-audit.md) roadmap (TD-05, TD-06) |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` still `0.1.0-rc.14`; RC.15 not prepared |
| Database schema / sync formats | Schema 18 (`Migration0018…`); sync envelope 1; draft payload 4; finalized snapshot 2; ERT sync payloads 1 — unchanged |

## Scope

An internal refactor with no change in product behavior:

1. The Early Ride Time (ERT) editor moves out of `AssociateViewModel` into a child view model.
2. The three remaining direct `Dispatcher.UIThread.Post` calls in view models (Phase 36A TD-06) now go through the
   existing `IUiDispatcher` seam.

Nothing else changed: no redesign, no new functionality, no schema, sync, PDF, retrieval, dependency or version
change, and no broader Associate decomposition.

## Starting architecture and ERT ownership (verified in source at `5384663`)

The 36A audit said the ERT editor "saves through its own service and has about five touch points". The source
review confirmed this. Early Ride Time has not been part of the Passdown draft since Phase 17B. The panel
persists only through `IErtConfigurationService`: `SaveMonthlyFromEditorAsync`, `SaveOverrideFromEditorAsync` and
`RemoveOverrideFromEditorAsync`, each conditional on a REL-004 content hash. Its rides are entered as a text list,
one per line; there are no checkboxes. No dialog or window is involved: the panel is an inline `Border` in the
Overview section, opened by **Edit Attractions**.

| Concern | Before (in `AssociateViewModel`) | After |
|---|---|---|
| Selected operating date | `SelectedDate` | **unchanged — parent**; the child reads it through a `Func<DateOnly>` |
| Editable draft and autosave | `BuildDraft` / `ScheduleAutosave` / `SaveDraftNowAsync` / `FlushPendingAutosaveAsync` | **unchanged — parent**; ERT was never part of it |
| Panel open flag | `IsErtEditorOpen` | child `IsErtEditorOpen` |
| Monthly fields | `MonthlyErtNoErt`, `MonthlyErtRidesText`, `MonthlyErtNoteText`, `MonthlyErtSaveError` | child (same names) |
| Override fields | `ErtUseOverrideForDate`, `OverrideNoErt`, `OverrideRidesText`, `OverrideReasonText`, `OverrideSaveError` | child (same names) |
| Conditional-save bases | `_monthlyErtBaseHash`, `_overrideBaseHash` | child |
| Clean baseline / dirty check | `_ertEditorLoadedFields`, `ErtEditorFields`, `IsErtEditorDirty()`, `CurrentErtEditorFields()` | child (`IsDirty`) |
| Commands | `OpenErtEditor`, `CancelErtEditor`, `SaveMonthlyErt`, `SaveOverride`, `RemoveOverride` | child (same command names) |
| Load / shape | `LoadErtEditorFieldsAsync`, `BuildErtDraftFields` | child |
| Conflict text | `AssociateViewModel.ErtSyncConflictMessage` | `EarlyRideTimeEditorViewModel.ErtSyncConflictMessage` (same text) |
| Workspace banner `SyncConflictMessage` | parent | **parent**; the child reports through `Action<string>` |
| Workspace reload after a save | `RefreshWorkspaceAsync(date)` | **parent**; the child calls it through `Func<DateOnly, Task>` |
| Effective-ERT display (`EarlyRideTimeStatusText`, `EarlyRideTimeIs…`), `SelectedMonthLabel`, `UseOverrideForDateLabel` | parent (read `Workspace.EarlyRideTime` / `SelectedDate`) | **parent**, unchanged |
| Line parsing / blank-to-null | private `ParseLines`, `NullIfBlank` | `EditorText` (same code; the parent's private methods delegate to it) |

**The five parent touch points, all retained:**

1. `ApplyWorkspaceToFields` closes the panel and clears its save errors; it is now
   `EarlyRideTimeEditor.CloseForWorkspaceReplacement()`.
2. Three reload paths keep the panel open across a workspace refresh (`RefreshNewPassdownDefaultsAsync`,
   `ReloadOfficialDataAsync`, `HandleSyncLocalChangesAsync`); each sets `EarlyRideTimeEditor.IsErtEditorOpen` back.
3. Unsaved panel edits defer the official-data reload and the new-defaults refresh, through
   `EarlyRideTimeEditor.IsDirty`.
4. A Personal Sync change to this month's or date's ERT refills a clean open panel, through
   `EarlyRideTimeEditor.LoadErtEditorFieldsAsync(date)`.
5. A save reloads the workspace, through the `refreshWorkspaceAsync` callback.

There is no circular reference: the child knows only the three delegates and the ERT service. `AssociateViewModel`
creates it once, before its first `SelectedDate` assignment, and exposes it as the stable `EarlyRideTimeEditor`
property. The parent went from 3,163 to 2,941 lines; the child has 298.

**Why main draft ownership is unchanged.** The child holds no `DateOnly` of its own, builds no draft, and has no
access to `ScheduleAutosave`, the save semaphore or the flush. Its only write path is the ERT service. The tests
in §Validation assert that no ERT action ever calls `SaveDraftAsync` or changes the autosave state, even with a
zero-length autosave debounce.

## Binding changes (`Views/AssociateView.axaml`)

20 binding paths gained the `EarlyRideTimeEditor.` prefix. No element, name, automation name, style class, order,
template or layout changed:

| Binding | Occurrences |
|---|---|
| `OpenErtEditorCommand` (Edit Attractions) | 1 |
| `IsErtEditorOpen` | 1 |
| `MonthlyErtNoErt`, including `!MonthlyErtNoErt` | 2 |
| `MonthlyErtRidesText` | 1 |
| `MonthlyErtNoteText` | 1 |
| `MonthlyErtSaveError` | 2 |
| `SaveMonthlyErtCommand` | 1 |
| `ErtUseOverrideForDate` | 2 |
| `OverrideNoErt`, including `!OverrideNoErt` | 2 |
| `OverrideRidesText` | 1 |
| `OverrideReasonText` | 1 |
| `OverrideSaveError` | 2 |
| `SaveOverrideCommand` | 1 |
| `RemoveOverrideCommand` | 1 |
| `CancelErtEditorCommand` | 1 |

`SelectedMonthLabel`, `UseOverrideForDateLabel` and `EarlyRideTimeIsOverride` inside the panel remain parent
bindings, and the panel's `DataContext` is unchanged. No code-behind references ERT members. The
`tools/visual-fixtures` `Workspace-ErtEditor` scenario was retargeted the same way.

## Date and stale-save safety

The rules are unchanged; they moved with the code:

- Every command reads the current date when it starts and re-checks it after each await.
- A load superseded by a date switch neither opens nor fills the panel.
- A save writes the month or date whose values the panel was showing, which is correct: those values belong to
  that date.
- When the date changed during the save, the workspace refresh and the panel reopen are skipped, so the new date
  never receives the old result.
- A date switch replaces the workspace, which closes the panel and discards its unsaved text, exactly as before.
  ERT edits were never autosaved.

The new tests cover:

- a date switch during a load;
- a date switch during a save;
- switching dates while editing;
- rapid switches followed by an open;
- reopening after Cancel;
- reload deferral while the panel has unsaved edits.

## UI-thread call sites (Phase 36A TD-06)

| Call site | Before | After | Decision |
|---|---|---|---|
| `AssociateViewModel` Personal Sync reload | `Dispatcher.UIThread.Post` | the already-injected `_uiDispatcher.Post` | Changed |
| `OperationalNotesViewModel` Personal Sync reload | `Dispatcher.UIThread.Post` | optional trailing `IUiDispatcher? uiDispatcher = null` (default `AvaloniaUiDispatcher`) | Changed |
| `PersonalSyncSettingsViewModel` automatic-sync status | `Dispatcher.UIThread.Post` | optional trailing `IUiDispatcher? uiDispatcher = null` (default `AvaloniaUiDispatcher`) | Changed |

All three are safe:

- The app registers `IUiDispatcher` as the singleton `AvaloniaUiDispatcher` (`App.axaml.cs`), and its `Post` *is*
  `Dispatcher.UIThread.Post`, so production marshalling, default priority and FIFO order are identical.
- Each handler still posts exactly once per notification.
- Subscriptions keep their lifetimes: each view model is created once per session.
- No existing test passed both a notifier and a dispatcher, so no existing test's timing changed.

No asynchronous scheduling was added, no repository moved threads and no command order changed. Nothing was
deferred.

## Tests

| File | Tests | Covers |
|---|---|---|
| `EarlyRideTimeEditorViewModelTests` (mixed) | 14 | constructor guards; initial state; open reads the current date's month and override; open superseded by a date switch; dirty/Cancel without save; reopen shows persisted values; Save Monthly (trimmed lines, note, no start time, refresh, stays open, clean); No ERT clears rides; save during a date switch writes the shown month and skips refresh/reopen; override save and remove; service failure messages, no refresh, retry; REL-004 conflict reported once; `CloseForWorkspaceReplacement`; open clears errors |
| `Phase36CErtEditorIntegrationTests` (mixed) | 9 | one stable editor across dates; no ERT action saves the draft or changes autosave state; the editor uses the workspace date; exactly one workspace reload per save, with updated status; override reflected in the reloaded workspace; Create PDF after an ERT save generates from the reloaded workspace; date switch closes, discards and saves nothing; rapid switches then open; a dirty editor defers the official-data reload |
| `Phase36CErtEditorWindowTests` (partial of run-alone `Phase33PreviewWindowTests`) | 5 | realized bindings both ways; Save keeps the panel open and the preview regenerates from the reloaded workspace with the new rides; Close hides it; override section through the child (and the parent-bound Remove button appears); real Tab-key order inside the panel; Light and Dark render with no transitions |
| `Phase36CDispatcherSeamTests` (mixed) | 5 | app registration is `AvaloniaUiDispatcher`; one post per notification, nothing applied until dispatched, publish order, no duplicate, selected date untouched (Associate, Operational Notes, Settings). Shown to fail against the old direct call |
| Existing tests retargeted (prefix only, no assertion removed or weakened) | — | `AssociateViewModelEarlyRideTimeTests`, `OpenEditorSyncProtectionTests`, `Phase35BWorkspaceWindowTests` (+ optional ERT fake in its window helper), `Phase22DAssociateWorkspaceTests`, `Phase35BPassdownWorkspaceTests`, `SpellCheckBehaviorTests` (markup paths now `EarlyRideTimeEditor.…`) |

**Not separately asserted:** focus restoration. The editor had no focus-restoration code before and has none now;
Close only hides the panel. Real-window tests cover keyboard order; DPI, Narrator and real Windows focus behavior
are Windows checklist items.

## Validation

All results are from the macOS development host (Apple M2 Pro, 16 GB, macOS 27.2.1, .NET SDK 10.0.400 / runtime
10.0.11). None of it is Windows validation.

| Check | Result |
|---|---|
| Deterministic partitions (`build-and-test` skill) | 5,957 / 5,957 passed, 0 skipped (5,924 + 33 new). Domain 467, Application 1,396, Infrastructure 1,350, Documents 484, Ui mixed 2,014, run-alone 30 + 144 + 2 + 7 + 4 + 59. UI partitions with `--blame-hang --blame-hang-timeout 90s` and a 3 GB command-line-matched watchdog: no hang, no kill, peak combined test-host RSS 367 MB |
| Repository guards | 31 / 31 passed (no guard changed; the new real-window tests are a partial of a run-alone class, as `RealWindowPartitionGuardTests` requires) |
| Clean Release build | 0 warnings, 0 errors (all `bin`/`obj` deleted, `--no-incremental`) |
| `tools/visual-fixtures`, `tools/perf-benchmarks` builds | 0 warnings, 0 errors |
| Rendering unchanged | `Workspace-ErtEditor` at all four sizes × Light/Dark from `5384663` (scratch worktree) and from the new code, run back to back: 8 PNGs + geometry report **byte-identical** |
| Windows local-test candidate | `artifacts/local-test/RC15-Phase36C-LOCALTEST/LOCAL-ONLY-RC15-Phase36C-LOCALTEST-win-x64.zip` from `18fac73`, shown as `0.1.0-rc.15-LOCALTEST.phase36c`; 76,143,312 bytes, 308 files; SHA-256 `d0d947e48a2d3ab5e8264c321287fb4f509221d171501b42bf8637c9aa3a726f`. Script content validation and `RealReleaseZip` passed; marker names `18fac73`; no database, draft, key, signature or manifest in the ZIP; all 11,762 earlier artifact files re-hashed byte-identical |
| Windows validation | NOT EXECUTED — checklist [rc15-phase36c-localtest-windows-checklist.md](releases/rc15-phase36c-localtest-windows-checklist.md) |

### Performance (Mac development evidence)

The scratch harness (not committed) drove the real view models with the Ui.Tests fakes: 3 runs × 200 iterations,
the median of the run medians, compiled against `5384663` and against the new code.

| Operation | Before | After |
|---|---|---|
| Workspace construct + first load | 0.0306 ms | 0.0288 ms |
| ERT open | 0.0037 ms | 0.0030 ms |
| Edit rides (property set) | 0.0002 ms | 0.0001 ms |
| Save Monthly (save + workspace reload) | 0.0128 ms | 0.0109 ms |
| Save Override | 0.0108 ms | 0.0095 ms |
| Remove Override | 0.0089 ms | 0.0082 ms |
| Cancel | 0.0001 ms | 0.0001 ms |
| Date switch with the editor open | 0.0077 ms | 0.0071 ms |
| Open/close cycle ×2,000 | 0.0020 ms, 1.48 KB allocated | 0.0021 ms, 1.48 KB allocated |
| Managed heap after 2,000 open/close cycles | no growth (−0.8 MB, GC noise) | no growth (−1.0 MB, GC noise) |

There is no regression: every difference is within run-to-run variation.

The view-model timings exclude rendering. The view's bindings changed only in path, and the realized renders are
byte-identical.

The Phase 34 preview benchmark (`perf-benchmarks preview 20`), with the old and new builds interleaved:

| Build | Busy page | 80 notes |
|---|---|---|
| Old | 15.1 / 15.6 ms | 39.9 / 40.0 ms |
| New | 15.1 / 15.9 ms | 39.0 / 39.1 ms |

These readings are higher than the 36A audit's 13.5 ms for **both** builds, which is host state, not code; no
preview, Documents or Infrastructure file changed. PDF warm-up is unchanged: warmed first preview 13.0 ms, cold
83.3 ms. No warm-up, debounce, cache, request or network behavior was added.

The dispatcher call sites are unchanged in production: the same `Dispatcher.UIThread.Post` behind one interface
call.

## Compatibility

- **Database and sync.** No schema, migration, repository, sync payload or file-format change. ERT saves use the
  same service methods with the same REL-004 hashes and the same conflict message text.
- **PDF.** No document, template or pipeline change. The preview, Print, Save PDF and Create PDF capture the same
  workspace.
- **UI.** Byte-identical renders of the editor, with the same automation names and keyboard order.
- **Rollback.** Revert the 36C commits. No data migration is involved.

## Documentation maintenance

The product specification describes ERT behavior (§10 Associate Passdown), which is unchanged; no requirement
changed.

**Product specification:** Reviewed — no changes required

`docs/architecture/desktop-ui.md` now records that the ERT editor is a child view model and that view models
marshal through `IUiDispatcher`. No dependency, project, persistence, sync, PDF or retrieval architecture changed.

**Architecture documentation:** Updated

## Risks, limitations and deferred work

- **Windows validation is pending.** It covers focus, DPI, Narrator, high contrast, printing and real
  Personal Sync timing.
- **Unsaved ERT text is still discarded by a date switch**, by design, since ERT edits were never autosaved.
  This was not changed (no new functionality).
- **The ERT panel layout is still part of `AssociateView.axaml`.** XAML extraction was rejected in 36A and not
  attempted.
- **No other Associate decomposition was started.** 36D is cancelled and 36E is deferred, per the 36A roadmap.
