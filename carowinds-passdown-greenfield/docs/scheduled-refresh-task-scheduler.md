# Windows Task Scheduler integration (Phase 6C)

This document covers the per-user Windows Task Scheduler entry that triggers the existing headless
refresh command. It does not re-describe the refresh/overlap/catch-up logic itself — see
`docs/architecture.md` ("Phase 6A additions", "Phase 6B additions") for that. The one thing worth
repeating up front:

> **Task Scheduler is only the operating-system trigger.** It launches
> `Carowinds.Passdown.Desktop.exe --scheduled-refresh` once a day. The application itself — not
> Task Scheduler — decides which sources are actually due (`IFreshnessPlanner`/
> `FreshnessPolicyCatalog`, Phase 2/3G). A daily catalog source that is on a 7-day or 14-day policy
> will see Task Scheduler launch the process every day and correctly do nothing for that source
> most of those days. That is expected, not a bug.

## Task identity

| | |
|---|---|
| Task name | `Carowinds Passdown Daily Refresh` |
| Action | `<installed path>\Carowinds.Passdown.Desktop.exe --scheduled-refresh` |
| Working directory | The executable's own directory |
| Run as | The current Windows user (no admin, no stored password) |

The task name and the `--scheduled-refresh` argument are also asserted by the cross-platform unit
tests in `ScheduledTaskPlanTests` (`tests/Carowinds.Passdown.Ui.Tests/ScheduledRefresh/`), against
the same `ScheduledTaskPlan`/`CommandLineArguments.ScheduledRefreshFlag` constants
`Install-ScheduledRefresh.ps1` uses. The PowerShell script's copy of the task name is a plain
literal kept in sync by hand — PowerShell can't reference the C# constant directly — so if the task
name ever changes, update both.

## Install / uninstall

```powershell
# Install or update the task (idempotent — safe to re-run, e.g. after a reinstall/upgrade):
.\packaging\windows\Install-ScheduledRefresh.ps1 -ExecutablePath "C:\Program Files\Carowinds Passdown\Carowinds.Passdown.Desktop.exe"

# Optional: override the trigger time (defaults to 06:00 local):
.\packaging\windows\Install-ScheduledRefresh.ps1 -ExecutablePath "C:\...\Carowinds.Passdown.Desktop.exe" -TriggerHour 6 -TriggerMinute 0

# Uninstall (safe to run even if the task was never installed):
.\packaging\windows\Uninstall-ScheduledRefresh.ps1
```

Both scripts require no administrator rights on a standard Windows 11 setup — they register/remove
a **per-user** task (`LogonType Interactive`, `RunLevel Limited`), not a machine-wide or SYSTEM
task. `Install-ScheduledRefresh.ps1` never hard-codes an installation path; the caller always
supplies `-ExecutablePath`. Phase 7 packaging will call the same script with the packaged
installation's actual path — nothing here needs to change for that.

Re-running install with a different `-ExecutablePath` (e.g. after moving the install location)
**updates the existing task in place** (`Register-ScheduledTask -Force`) rather than creating a
duplicate — it only ever replaces the one task named `Carowinds Passdown Daily Refresh`, never any
other scheduled task on the machine.

## Trigger time and local clock interpretation

The product spec targets 6:00 AM **Eastern**. Windows Task Scheduler triggers fire on the
**machine's local wall-clock time**, whatever time zone that machine is configured for — there is
no fixed UTC offset baked into the registered trigger, and this script does not attempt to convert
one. In practice this means:

- On a machine actually set to Eastern time (the expected case for Carowinds staff hardware), the
  task fires at 6:00 AM Eastern, matching the spec exactly, including automatically following
  Eastern's DST transitions the same way the OS clock does.
- On a machine set to a different time zone, the task fires at 6:00 AM in *that* zone's local time,
  not 6:00 AM Eastern. Nothing in this integration detects or corrects for that; it is a known,
  documented limitation rather than something silently wrong.

This is a deliberate simplification, not an oversight (Phase 6C Part B: "do not assume a fixed UTC
offset"). The application's own freshness logic never depends on *when* the process happened to be
invoked (Phase 6B's `ScheduledRefreshLateInvocationTests` prove this) — it always computes "is this
source due" from the last recorded success and the current instant — so even if the trigger fires
at an unexpected local time, the app still behaves correctly; only the target 6:00 AM Eastern
convenience may drift on a misconfigured machine's clock/time zone.

## Missed-run behavior

`-StartWhenAvailable` is set, which is Task Scheduler's "run the task as soon as possible after a
scheduled start is missed" setting. This covers the laptop-asleep/laptop-powered-off case at the
scheduled time.

This is one of two independent layers that cover a missed 6 AM run:

1. **Task Scheduler's own missed-start catch-up** (this setting) — fires the task once the machine
   is next awake, whether or not the desktop app is ever opened that day.
2. **The application's interactive startup catch-up** (Phase 6B; since Phase 19A part of
   `Carowinds.Passdown.Application.Refresh.OfficialDataRefreshCoordinator`) — the next time a user opens
   the desktop app it finalizes and, only if today's maintenance has not run yet (no Deep Refresh
   attempted since 6:00 AM Eastern, or Park Hours coverage fell behind), runs the same maintenance in the
   background, independent of Task Scheduler. While the app stays open it also runs its own lighter
   comprehensive check every four hours (`docs/phase19a-automatic-refresh.md`).

Both call the exact same `IScheduledRefreshRunner`/`IRefreshOrchestrator` pipeline and both acquire
the same `FileScheduledRefreshLock` before doing any real work, so if both happen to trigger around
the same time, only one actually runs the refresh; the other sees the lock held and skips
cleanly (exit code 4, `Overlap`, if it was the Task Scheduler process; a silently-logged skip if it
was the interactive catch-up). See "Task Scheduler + startup catch-up coexistence" below.

## Overlap / multiple-instance policy

Two independent layers, deliberately redundant:

1. **Task Scheduler**: `-MultipleInstances IgnoreNew` ("do not start a new instance") — if the
   previous run is still active when the next trigger fires, Task Scheduler does not start a second
   process at all.
2. **Application**: `FileScheduledRefreshLock` (Phase 6B) — an exclusively-held local file under
   `%LOCALAPPDATA%\Carowinds Passdown\scheduled-refresh.lock`. Any `--scheduled-refresh` invocation
   that cannot acquire it (a manual double-run, the interactive startup catch-up racing a scheduled
   run, or a gap in Task Scheduler's own guard) exits immediately with code `4` (`Overlap`) without
   touching migrations or the refresh job.

Task Scheduler's setting is *not* a substitute for the file lock — it only prevents Task Scheduler
itself from launching a second instance of a task it's tracking; it says nothing about a manually
launched process or the interactive startup catch-up path, which is exactly why the file lock still
exists and is never bypassed.

## Power and network conditions

| Condition | Setting | Why |
|---|---|---|
| Run on battery | Allowed (`-AllowStartIfOnBatteries`, `-DontStopIfGoingOnBatteries`) | The refresh is a short set of HTTP/HTML fetches, not a heavy job. A nontechnical user's laptop is often unplugged; requiring AC power would mean the daily refresh frequently just doesn't happen. |
| Wake the computer to run | **Not enabled** (no `-WakeToRun`) | Forcibly waking a sleeping/hibernating laptop just to refresh ticket prices is intrusive (fan noise, a bag potentially overheating) for a requirement the missed-run catch-up already satisfies less invasively — see "Missed-run behavior" above. |
| Require network availability at trigger time | **Not configured** | The app already handles offline failure safely — a source failure preserves last-known-good data and never overwrites it (Phase 6B). Requiring network at the exact trigger instant would risk the task reporting itself skipped on a day that later reconnects, with no corresponding safety benefit. |
| Execution time limit | 1 hour (defensive, not spec-required) | Bounds a hung run rather than letting it hold the overlap lock indefinitely. Normal runs are expected to take well under this. |

## Logging and observability

Task Scheduler does not need to capture stdout/stderr to a separate file — the application already
writes its own persistent, human-readable log for every `--scheduled-refresh` invocation (Phase
6A): one UTF-8 file per UTC day at

```
%LOCALAPPDATA%\Carowinds Passdown\logs\scheduled-refresh-yyyyMMdd.log
```

That file is the authoritative record of what a run actually did (which sources were due, which
succeeded/failed, `SUCCESS`/`PARTIAL`/`OVERLAP`/`FATAL`/`CANCELLED` outcome). Task Scheduler's own
state answers a narrower, complementary question — *did the OS launch it, and what did the process
exit with*:

```powershell
# Does the task exist, and what's its next/last run time?
Get-ScheduledTask -TaskName 'Carowinds Passdown Daily Refresh' | Get-ScheduledTaskInfo

# Full task definition (action, trigger, settings, principal):
Get-ScheduledTask -TaskName 'Carowinds Passdown Daily Refresh' | Select-Object -ExpandProperty Actions
Get-ScheduledTask -TaskName 'Carowinds Passdown Daily Refresh' | Select-Object -ExpandProperty Triggers
Get-ScheduledTask -TaskName 'Carowinds Passdown Daily Refresh' | Select-Object -ExpandProperty Settings
```

`Get-ScheduledTaskInfo`'s `LastTaskResult` is the process's exit code
(`ScheduledRefreshExitCode`): `0` Success, `1` PartialFailure, `2` FatalFailure, `3` InvalidUsage,
`4` Overlap (skipped — another run already held the lock). A nonzero `LastTaskResult` other than
`4` is worth opening the day's log file for; `4` on its own is not a problem — it means the
redundant protection in "Overlap / multiple-instance policy" did exactly what it's for.

## Manually running the task

```powershell
Start-ScheduledTask -TaskName 'Carowinds Passdown Daily Refresh'
```

Expected behavior: no window appears (the `--scheduled-refresh` path never initializes Avalonia —
`Program.cs`), the process runs and exits on its own, and the day's log file gains new lines. Check
`Get-ScheduledTaskInfo`'s `LastRunTime`/`LastTaskResult` afterward.

## Troubleshooting a failed run

1. Check `Get-ScheduledTaskInfo`'s `LastTaskResult` first — it tells you which of the five exit
   codes above happened.
2. Open the current day's log file
   (`%LOCALAPPDATA%\Carowinds Passdown\logs\scheduled-refresh-yyyyMMdd.log`, UTC-dated) for the
   detailed per-source outcome and, for a `FatalFailure`, the exception that caused it.
3. If `LastTaskResult` is `4` (Overlap) repeatedly, something is holding the lock unexpectedly long
   — check whether a previous run is genuinely still in progress (its process still running) rather
   than assuming corruption; the lock file itself
   (`%LOCALAPPDATA%\Carowinds Passdown\scheduled-refresh.lock`) is never read/written for content,
   only held open, so it is not something to hand-edit.
4. If the task's `LastTaskResult` looks like a generic Task Scheduler launch failure (not one of the
   app's own exit codes) rather than a code from the list above, re-run
   `Install-ScheduledRefresh.ps1` with the correct `-ExecutablePath` — the most common cause is a
   stale path after moving/reinstalling the app.

## Task Scheduler + interactive startup catch-up coexistence

Both the scheduled process and the desktop app's own startup catch-up can be triggered around the
same time (e.g. a user opens the app right as the scheduled task fires). This is expected and
handled entirely by the existing `FileScheduledRefreshLock`, not by anything new in this phase:

- Whichever acquires the lock first performs the actual refresh.
- The other observes the lock held and skips its own refresh attempt without error: the scheduled
  process exits with code `4` (Overlap); the interactive app logs the skip (it never surfaces an error
  to the UI) and re-evaluates within five minutes. Inside the app, every online refresh (automatic,
  Check Online, Deep Refresh) additionally passes through one in-process gate, so the app never runs
  two of its own at once (Phase 19A).
- Phase 19A: when the scheduled task already ran today, opening the app does not repeat its Park Hours
  coverage sweep, and if every source was checked within the last 60 minutes the app skips
  its startup check entirely. A scheduled run also resets the app's four-hour automatic clock, because
  that clock is derived from the same persisted `source_state` timestamps.
- The desktop window always opens normally regardless of which path is refreshing, since the
  startup catch-up runs as a detached background task that never blocks `MainWindow` being shown.
- No duplicate refresh occurs and no data corruption can result, because only the lock holder ever
  reaches `MigrationRunner`/`IRefreshOrchestrator`.

## Security review summary

- No credentials, passwords, or tokens are stored in the repository, the scripts, or the registered
  task (`LogonType Interactive` needs none).
- The task never runs as `SYSTEM` or any elevated/service account — it runs as the interactively
  logged-on user, `RunLevel Limited`.
- The executable path and working directory are passed to `New-ScheduledTaskAction` as separate
  `-Execute`/`-Argument`/`-WorkingDirectory` values, never concatenated into one command string, so
  a path containing spaces cannot be misparsed or used to inject an extra argument.
- The registered argument is always the fixed literal `--scheduled-refresh` — there is no code path
  in either script that can register the executable with a different or additional argument, and
  `ScheduledTaskPlanTests` asserts this in C# against the same constant `Program.cs` parses.
- No network share dependency — the executable path is expected to be local (or, if a UNC path is
  supplied, that is the caller's explicit choice, not something either script defaults to).
- `Uninstall-ScheduledRefresh.ps1` targets only the one task by its exact name and never touches
  application data, drafts, cache, or logs under `%LOCALAPPDATA%\Carowinds Passdown`.

## Manual Windows VM validation checklist

This integration was not exercised against a real Windows Task Scheduler by Claude — only inspected
by code review and covered by the cross-platform unit tests above (`ScheduledTaskPlanTests`). The
checklist below was run on a Windows 11 VM and **manually validated by the user on Windows 11**
(not something Claude performed directly).

1. Build/publish or locate the Windows executable.
2. `.\Install-ScheduledRefresh.ps1 -ExecutablePath "<actual path>"`.
3. Open Task Scheduler (`taskschd.msc`) and find `Carowinds Passdown Daily Refresh`. Verify:
   - Action's executable path and `--scheduled-refresh` argument.
   - Trigger time (06:00 local by default).
   - "Run task as soon as possible after a scheduled start is missed" is checked.
   - "Do not start a new instance" is selected under multiple-instance behavior.
   - Runs only when the user is logged on (no stored password prompt appeared during install).
4. Right-click → **Run**. Verify: no app window appears, the current day's log file under
   `%LOCALAPPDATA%\Carowinds Passdown\logs\` gains new lines, the task finishes, and
   `Get-ScheduledTaskInfo`'s `LastTaskResult` is `0` (or `1` if a source is genuinely failing, which
   is still a clean run of the mechanism itself).
5. While a run is in progress (or while the `scheduled-refresh.lock` file is held open manually),
   trigger **Run** again. Verify no duplicate refresh occurs — either Task Scheduler itself refuses
   the second instance, or the second process exits with code `4`/logs "Scheduled refresh already
   running" — and no data corruption results.
6. `.\Uninstall-ScheduledRefresh.ps1`. Verify the task disappears from Task Scheduler and that
   `%LOCALAPPDATA%\Carowinds Passdown` (drafts, cache, logs) is untouched.
7. Re-run `Install-ScheduledRefresh.ps1` to leave the VM in the expected end-state, if desired.
8. Optional: temporarily set the trigger a few minutes in the future, let the machine/session miss
   it (sleep, or briefly disable the task and re-enable after the trigger time passes), and confirm
   the missed-start setting causes it to run shortly after. A full sleep/wake automation test is
   optional — at minimum, step 3's inspection of "run as soon as possible after a missed start"
   already confirms the setting is registered correctly.

### Result — manually validated by the user on Windows 11 (Phase 6D)

Every step above was performed and passed. Recorded results (no usernames or machine-specific
absolute paths — see `docs/architecture.md` "Phase 6D" for the full record):

- Task registered successfully; task state was `Ready`.
- Action pointed to the correct `Carowinds.Passdown.Desktop.exe`; arguments were exactly
  `--scheduled-refresh`; working directory matched the executable's own directory.
- Manual `Start-ScheduledTask` completed; `LastTaskResult = 0`.
- `NextRunTime` was 06:00; `StartWhenAvailable = True`; `MultipleInstances = IgnoreNew`;
  `RunOnlyIfNetworkAvailable = False`; `DisallowStartIfOnBatteries = False`;
  `StopIfGoingOnBatteries = False`.
- Uninstall removed only the scheduled task; application data/cache/drafts/logs were preserved.
- Reinstall succeeded cleanly.

Step 8 (missed-start simulation) was covered by step 3's registration inspection, per the checklist's
own "optional" note above; a full sleep/wake automation run was not separately recorded.
