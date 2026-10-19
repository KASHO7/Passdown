# Phase 23A — Critical save-failure freeze fix

Release-blocking reliability fix after RC.7. Development only: RC.7 artifacts, hashes and
`Directory.Build.props` (`0.1.0-rc.7`) are unchanged. The fix ships in the next candidate,
**`0.1.0-rc.8`**, which is packaged in a separate phase.

| | |
|---|---|
| Starting HEAD | `9c474b8` (Phase 23: RC.7 release manifest, release notes and Windows validation record) |
| Branch | `feature/passdown-redesign` |
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 — unchanged; no migration; no new files or storage |
| Deterministic tests | **4011/4011** (3980 baseline + 31 new) |
| Release build | 0 warnings, 0 errors |
| Windows validation | **NOT EXECUTED** (macOS build host) — see §10 |

## 1. Root cause

`AssociateViewModel.FlushPendingAutosaveAsync` (the drain that runs before anything replaces the
editable fields) was:

```csharp
while (_isDirty || _saveInFlight)
{
    await SaveDraftNowAsync(date);
}
```

`SaveDraftNowAsync` catches a failed write and sets `_isDirty = true` again, so the edit stays
pending. With a **persistent** storage failure the condition never becomes false: the loop has no
bound, no delay and no exit.

Why it froze the whole app and used 100% CPU: Microsoft.Data.Sqlite's `OpenAsync`,
`BeginTransactionAsync` and `ExecuteNonQueryAsync` complete **synchronously**. So a failed save throws
before any real suspension point and each `await` continues inline. The loop therefore never returned
control to the Avalonia dispatcher: no repaint, no input, no timers. Proven on the real repository by
`Phase23ASaveFailureSyncTests.ARealSqliteWriteFailure_FailsSynchronously_AndIsAttemptedOnlyOnce`: an
ABORT trigger in a disposable test database makes the real INSERT fail, and the whole flush/fail path
has finished before `NextDayCommand.Execute` returns.

What it was **not** (checked and ruled out): recursive property notifications, dirty-state observers,
debounce retry scheduling (the debounce schedules one delay per edit and never re-arms itself after a
failure), dispatcher reposting, overlapping saves (the existing `_saveSemaphore` already serialized
them) or repeated network launches. It was one unbounded synchronous retry loop in the flush.

Every caller of the flush reached it:

| Trigger | Path |
|---|---|
| Date change (‹ › Today, date picker, Dashboard/Calendar/History deep links) | `OnSelectedDateChanged` → `SwitchDateAsync` → flush |
| Check Online | `CheckOnlineAsync` → flush |
| Add / remove / replace operational note | `Add/Remove/ReplaceOperationalNoteAsync` → flush |
| Create PDF | `CreatePdfAsync` → flush |
| Closing the app | `App.FlushPendingAutosaveBeforeShutdownAsync` → flush |

The close path's 5-second timeout (Phase 15D) did **not** protect it as the RC.7 notes stated: the
flush never returned a task, so `Task.WhenAny(flush, Task.Delay(5 s))` was never reached.

## 2. Previous behavior

- One failed autosave, then any action in the table above: an endless loop on the UI thread at
  100% CPU, no window response; Task Manager was the only way out. Reproduced in RC.7 testing for more
  than 400 s.
- Bounded reproduction (`DateChange_AfterAFailedSave_MakesOneAttempt_NotALoop` against the old code):
  one date change made 51 save attempts before the test's loop guard stopped it.
- Had the loop ever ended, the date switch / Check Online / note change would then have loaded the
  stored draft over the unsaved edits.

## 3. New save coordination

`src/Carowinds.Passdown.Desktop/ViewModels/AssociateViewModel.cs`:

- `SaveDraftNowAsync` returns `bool` (persisted or not). It is still the only call to
  `IAssociatePassdownService.SaveEditorDraftAsync` and is still single-flight through `_saveSemaphore`.
  It never retries.
- `FlushPendingAutosaveAsync` returns `bool`: true only when nothing on screen is left unsaved. A failed
  attempt ends the flush immediately. Passes are capped at `MaxFlushSavePasses` (3) even when every pass
  succeeds but edits keep arriving.
- Every flush caller checks the result and does not replace the editor when it is false.
- New `RetrySaveCommand` (one attempt per press, enabled only in `SaveFailed`).
- New `SaveBlockedMessage` says what was held back; new `IsSaveFailed` shows Retry Save.

Save states (`AssociateAutosaveState`, unchanged enum): `Saved`, `Unsaved` (pending edit), `Saving`,
`SaveFailed`. Conflict stays the existing `SyncConflictMessage` (Personal Sync), unchanged.

Staleness: saves are serialized and each one builds the draft from the fields **at the time it runs**,
so a slower attempt can never write an older snapshot over a newer one. An edit typed while a save is
in flight sets `_isDirty` again, so a successful older save cannot mark the newer edit saved.

## 4. Retry policy — suspended after failure

No automatic retry timer exists. After a failure a save is attempted only when the associate does
something:

| Event | Attempts |
|---|---|
| A new edit (any field, including notes) | 1, after the normal 800 ms debounce (one per pause in typing) |
| Retry Save | 1 per press |
| Date change, Check Online, note change, Create PDF | 1 per action, then the action is refused if it fails |
| Close | 1 per close request |
| Section switch, official-data refresh, sync reload, idle time | 0 |

The number of attempts is therefore bounded by user actions: no loop, no queue, no backoff timer and no
CPU use while idle. Navigation cannot "reset a retry limit" because there is no counter to reset; each
navigation is itself one bounded attempt.

## 5. Edit preservation

On a failed save the edits stay exactly as typed: no field is cleared, nothing is reloaded over them,
they are never marked saved, and they are only ever written to the date they belong to
(`_draftDateInEdit`). `SaveFailed` and its reason clear only after a confirmed successful save (or a
successful load of a date, which can only happen after the flush succeeded).

## 6. Navigation and refresh behavior

- **Date change while the save keeps failing**: refused. `SelectedDate` is put back on the date being
  edited (guarded by `_isRestoringSelectedDate` so the restore does not start another switch), the
  other date is not loaded, and the message reads *"Your changes could not be saved on this computer,
  so the date was not changed. Select Retry Save, then try again."* If a newer date switch has already
  superseded this one, the restore is skipped.
- **Check Online**: no network request. *"Not checked online: your changes could not be saved on this
  computer. Select Retry Save, then check again."* The button stays enabled.
- **Add / remove / replace a persistent note**: not applied (it would reload the stored draft).
  Free-text notes editing works normally and retries once per debounce.
- **Create PDF**: not created (the PDF is built from the saved workspace and would silently omit the
  edits).
- **Section rail (Overview/Products/Notes) and other top-level screens** (Dashboard, Calendar, Notes,
  History, Settings): unaffected; they never save.
- **Automatic official-data refresh / Personal Sync reload**: already deferred while the editor is
  dirty; a failed save keeps it dirty, so neither replaces the edits nor clears the failure.

## 7. Recovery

Fix the storage problem, then press **Retry Save** (or make any edit). On success the chip returns to
"Saved on this computer at …", the reason and the blocked message disappear, and date changes, Check
Online, notes and PDF work again. A deferred official-data reload runs then, as before.

## 8. Closing

`App.AttachShutdownFlush`:

1. First close request: one save attempt (still bounded by the 5 s timeout). If it fails, the close is
   cancelled **once**. The app shows the Associate screen (unless a Notes edit is in progress) with
   *"Your latest changes could not be saved on this computer. Select Retry Save, or close the app again
   to exit without them."*
2. Next close request: one more attempt, then the app exits regardless (logged). No confirmation loop.
3. Close requests that arrive while a flush is running are ignored instead of starting another flush.

No recovery file or new storage was added. An unsaved edit that cannot be written is lost if the user
closes twice, and the app says so before it happens.

## 9. Personal Sync

Unchanged rules; no sync code changed. `AssociatePassdownService.SaveEditorDraftAsync` marks the sync
change (`MarkLocalChangeAsync` → `RequestSync`) and publishes the `DataChange` only **after** the
transaction commits. A failed write therefore leaves no sync-state row and requests no sync pass, so an
uncommitted draft can never be published. Proven on real SQLite by `Phase23ASaveFailureSyncTests`
(both a wrapper-injected `SqliteException` and a real trigger-raised SQLite error). After recovery,
Retry Save marks the draft for sync exactly once. Conflict handling is untouched; SyncSchemaVersion stays 1.

## 10. Tests

New: `tests/Carowinds.Passdown.Ui.Tests/Phase23ASaveFailureLoopTests.cs` (29) and
`Phase23ASaveFailureSyncTests.cs` (2), both in the mixed Ui partition (no real windows).
`FakeAssociatePassdownService.SaveLoopGuardLimit` makes any runaway loop end with a
`SaveLoopGuardException` instead of hanging the test run.

Coverage: persistent failure; transient failure then recovery (Retry Save and new edit); repeated date
navigation (‹ › Today, picker); Check Online during failure and after recovery; free-text notes during
failure; persistent-note add during failure and after recovery; attendance, pre-solds and Shows kept
and saved together; consecutive edits; concurrent triggers (debounce + Check Online + PDF during an
in-flight save); failed in-flight save vs. newer edit; edit typed during a successful save stays dirty;
official-data reload during failure; section switches never save; PDF held back; error wording and
persistence until success; Retry Save enablement; shutdown flush (fails promptly, succeeds after
recovery, nothing pending); close warning; Personal Sync on real SQLite; markup for Retry Save and the
blocked message; a stress test; idle failure schedules nothing.

Changed: `Phase22DAssociateWorkspaceTests.Feedback_StaysVisibleOnEverySection_NothingCriticalRemoved`
expects the new error-colored save-blocked line (3 error-brush lines instead of 2) and its binding.

Against the old loop (flush restored to `while (dirty) save()`), 13 of the new tests fail — every
navigation, Check Online, note, PDF, shutdown, concurrency and stress test; the rest cover behavior that
was already correct.

Final run (`-c Release`, `--filter "Category!=LiveSmoke"`): 221 Domain + 1089 Application + 852
Infrastructure + 463 Documents + 1281 Ui together + 26 `OperationsCalendarViewTests` + 66
`Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7
`SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone = **4011/4011**.

## 11. Performance findings

`Stress_PersistentFailure_UnderRepeatedActions_StaysBoundedAndResponsive`: storage failing throughout,
300 mixed actions (date change, Check Online, add note, Create PDF, notes edit, Retry Save):

| Measure | Result |
|---|---|
| Save attempts | 301 (1 initial + 1 per action; linear, never unbounded) |
| Debounce timers scheduled | 51 (1 initial + 1 per notes edit; nothing accumulates) |
| Online checks / note selections / PDFs | 0 / 0 / 0 |
| Elapsed | 9 ms |
| Process CPU during the run | 11 ms |
| Selected date afterwards | unchanged (edits kept) |

Then one Retry Save with storage restored saved every edit. With no action at all, a persistent failure
schedules no further timer and makes no further attempt. Before the fix, a single date change was an
unbounded loop (51 attempts in under a millisecond before the guard; > 400 s at 100% CPU on Windows).
UI responsiveness here is measured as "every action returns control"; real Windows responsiveness and
CPU are checked in §12.

## 12. Windows manual validation — NOT EXECUTED

Run on a Windows 11 x64 VM with an RC.8 candidate build. Use a **disposable profile**: back up
`%LOCALAPPDATA%\Carowinds Passdown` first, or use a fresh Windows user. Never damage a real profile.

1. Start the app with the disposable profile. Open Associate Passdown for today.
2. Create a draft: projected attendance 38,500, pre-solds 1,250, a free-text note, select one Show.
   Wait for "Saved on this computer at …".
3. Trigger a controlled, reversible failure by holding the database's write lock from another
   process (the official `sqlite3.exe` command-line tool from sqlite.org):
   `sqlite3 "%LOCALAPPDATA%\Carowinds Passdown\carowinds-passdown.db"`, then type `BEGIN EXCLUSIVE;`
   and leave that window open. (Changing file permissions is not reliable: the app keeps its database
   connection open, so new ACLs do not affect it.) Change attendance to 40,000 and wait about 6 s —
   each attempt now waits out SQLite's 5 s busy timeout before failing.
4. Confirm the Draft chip reads **"Save failed — changes not saved"** in the error color, hovering shows
   the reason, and a **Retry Save** button appears.
5. Click ›, the date picker and Today. Each click pauses up to about 5 s (one attempt waiting on the
   lock), then confirm the date does not change, the message explains why, and 40,000 is still on screen.
6. Click Check Online. Confirm no "Checking…" progress, the "Not checked online" message, and the app
   stays usable.
7. Type in the free-text notes; add a suggested persistent note; Create PDF. Confirm the text stays,
   the note is not added, no PDF dialog opens, each shows its message.
8. Confirm the app stays responsive throughout (switch Overview/Products/Notes, open Dashboard and
   Calendar, move the window). In Task Manager, CPU for Carowinds Operations stays near 0% when idle.
9. Release the lock: in the sqlite3 window type `ROLLBACK;` then `.quit`.
10. Click Retry Save.
11. Confirm "Saved on this computer at …", Retry Save and the messages disappear, and › now changes the
    date.
12. Close and restart the app. Confirm today's draft shows 40,000, 1,250, the note text and the Show.

Close check (optional, repeat 3–4 then): click X once — the window stays open on Associate with the
"could not be saved … close the app again" message; click X again — the app exits within about 5 s.

## 13. Compatibility and release implications

- No schema, storage, sync-format or file-layout change; RC.7 data opens unchanged in RC.8 and back.
- New UI: Retry Save button and one feedback line on the Associate screen; save-failure tooltip text
  now mentions Retry Save.
- RC.7 artifacts and hashes are untouched. RC.7's documented "persistent save failure + date change can
  freeze the app" limitation (and validation item 7.7's caution) is fixed in development and will be
  lifted in the RC.8 notes. Windows checklist item 7.7 for RC.8 should use §12.
- Next candidate: **`0.1.0-rc.8`** (version bump and packaging in a separate phase).

## 14. Remaining limitations

- Windows behavior, DPI layout of the Retry Save button and real Task Manager CPU readings are not
  validated (§12).
- A *locked* database (rather than one that fails at once) still makes each single attempt wait up to
  SQLite's 5 s busy timeout on the UI thread, because SQLite writes are synchronous (pre-existing,
  Phase 13). The window pauses for that attempt, then responds again; it is never an endless freeze.
- An edit that can never be written is lost if the user closes twice; there is deliberately no
  recovery file (that would need new storage and approval).
- A flush that ends at the 3-pass cap (only possible if the associate keeps typing while three
  consecutive saves are in flight) reports "could not be saved" although no write failed; the edits
  stay on screen and the next action saves them. Not reachable with SQLite, whose writes complete
  synchronously.
- Whether Avalonia raises `ShutdownRequested` for the window X button on Windows is relied on as in
  Phase 15D; confirm in the optional close check.
