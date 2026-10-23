using Carowinds.Passdown.Application.Time;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Owns WHEN an automatic Personal Sync pass runs (Phase 16F §8) — <see cref="IPersonalSyncService"/>
/// remains the record-reconciliation engine (HOW to sync) and is completely unaware this type exists.
/// One application-level singleton, started once at interactive app startup and stopped once at
/// shutdown, replacing what would otherwise be timers/semaphores scattered across ViewModels.
/// </summary>
public interface IPersonalSyncCoordinator
{
    /// <summary>The most recently computed automation status — safe to read at any time, including before <see cref="Start"/>.</summary>
    PersonalSyncAutomationSnapshot CurrentStatus { get; }

    /// <summary>Raised every time <see cref="CurrentStatus"/> changes — a Settings screen subscribes for live updates while open.</summary>
    event EventHandler<PersonalSyncAutomationSnapshot>? StatusChanged;

    /// <summary>
    /// Begins the periodic safety-sync loop and fires the one-time startup trigger (§3). Returns
    /// immediately — the startup pass itself runs in the background and never blocks the caller (the
    /// app shell must appear before this resolves anything). Calling this more than once is a no-op.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the periodic loop and cancels any pending debounce/retry timer (§18/§23). Does not wait
    /// indefinitely for an in-flight sync pass — bounded by whatever the caller's own
    /// <see cref="CancellationToken"/>/timeout around this call enforces; a pass already talking to
    /// <see cref="IPersonalSyncService"/> observes cancellation cooperatively.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Schedules a sync pass for <paramref name="reason"/> (§2). <see cref="SyncTriggerReason.LocalChange"/>
    /// and <see cref="SyncTriggerReason.Finalization"/> are debounced/coalesced (§4/§9) — repeated calls
    /// within the debounce window collapse into one pass. Every other reason dispatches promptly (each
    /// already has its own natural spacing — periodic/retry timers, or a one-time startup call). A no-op
    /// before <see cref="Start"/> has been called. Fire-and-forget by design — callers that need the
    /// actual result (Manual "Sync Now") use <see cref="SyncNowAsync"/> instead.
    /// </summary>
    void RequestSync(SyncTriggerReason reason);

    /// <summary>
    /// "Sync Now" (§27): bypasses the debounce delay and, if a pass is already running, waits for it to
    /// finish and then runs one guaranteed fresh pass reflecting current state — never in parallel with
    /// another pass, never silently coalesced away. Returns that pass's own <see cref="PersonalSyncResult"/>
    /// so the caller can render a summary immediately.
    /// </summary>
    Task<PersonalSyncResult> SyncNowAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPersonalSyncCoordinator"/>
public sealed class PersonalSyncCoordinator : IPersonalSyncCoordinator
{
    /// <summary>
    /// Phase 16F §4: long enough to coalesce a burst of the Associate screen's own 800ms-debounced
    /// autosaves (edit/edit/edit → one autosave → one more autosave a moment later) into a single sync
    /// pass, short enough that "shortly afterward" still feels immediate.
    /// </summary>
    public static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromSeconds(5);

    /// <summary>Phase 16F §6: a low-frequency safety sweep — catches remote changes and missed local triggers without polling a local OneDrive folder any harder than useful.</summary>
    public static readonly TimeSpan DefaultPeriodicInterval = TimeSpan.FromMinutes(5);

    /// <summary>Phase 16F §10: bounded stepped backoff for a transient failure (folder unavailable / I-O failures) — reset to the first step after any success.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoffSchedule =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    private readonly IPersonalSyncService _syncService;
    private readonly IPersonalSyncConflictResolutionService _conflictService;
    private readonly IClock _clock;
    private readonly ILogger<PersonalSyncCoordinator>? _logger;
    private readonly TimeSpan _debounceInterval;
    private readonly TimeSpan _periodicInterval;
    private readonly IReadOnlyList<TimeSpan> _backoffSchedule;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly Lock _lock = new();

    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _retryCts;
    private Task? _periodicLoopTask;
    private bool _started;
    private bool _runRequestedAgain;
    private int _consecutiveTransientFailures;
    private PersonalSyncAutomationSnapshot _status = PersonalSyncAutomationSnapshot.Initial;

    public PersonalSyncCoordinator(
        IPersonalSyncService syncService,
        IPersonalSyncConflictResolutionService conflictService,
        IClock clock,
        ILogger<PersonalSyncCoordinator>? logger = null,
        TimeSpan? debounceInterval = null,
        TimeSpan? periodicInterval = null,
        IReadOnlyList<TimeSpan>? backoffSchedule = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(conflictService);
        ArgumentNullException.ThrowIfNull(clock);

        _syncService = syncService;
        _conflictService = conflictService;
        _clock = clock;
        _logger = logger;
        _debounceInterval = debounceInterval ?? DefaultDebounceInterval;
        _periodicInterval = periodicInterval ?? DefaultPeriodicInterval;
        _backoffSchedule = backoffSchedule is { Count: > 0 } ? backoffSchedule : DefaultBackoffSchedule;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public PersonalSyncAutomationSnapshot CurrentStatus
    {
        get { lock (_lock) { return _status; } }
    }

    public event EventHandler<PersonalSyncAutomationSnapshot>? StatusChanged;

    public void Start()
    {
        lock (_lock)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _lifetimeCts = new CancellationTokenSource();
        }

        _periodicLoopTask = RunPeriodicLoopAsync(_lifetimeCts!.Token);
        RequestSync(SyncTriggerReason.Startup);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetimeCts;
        Task? periodicLoopTask;

        lock (_lock)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            lifetimeCts = _lifetimeCts;
            _lifetimeCts = null;
            periodicLoopTask = _periodicLoopTask;
            _debounceCts?.Cancel();
            _retryCts?.Cancel();
        }

        lifetimeCts?.Cancel();

        if (periodicLoopTask is not null)
        {
            try
            {
                await periodicLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected — the loop's own delay observed the lifetime cancellation.
            }
        }

        lifetimeCts?.Dispose();
    }

    public void RequestSync(SyncTriggerReason reason)
    {
        lock (_lock)
        {
            if (!_started)
            {
                return; // Nothing scheduled yet — Start()'s own startup trigger will cover current state.
            }
        }

        switch (reason)
        {
            case SyncTriggerReason.LocalChange:
            case SyncTriggerReason.Finalization:
                ScheduleDebounced(reason);
                break;

            default:
                _ = TriggerRunAsync(reason);
                break;
        }
    }

    public async Task<PersonalSyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            // Bypass any pending debounce/retry — the user asked for this right now (§27/§33-I).
            _debounceCts?.Cancel();
            _retryCts?.Cancel();
        }

        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecutePassAsync(SyncTriggerReason.Manual, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            bool again;
            lock (_lock)
            {
                again = _runRequestedAgain;
                _runRequestedAgain = false;
            }

            _runGate.Release();

            if (again)
            {
                _ = TriggerRunAsync(SyncTriggerReason.LocalChange);
            }
        }
    }

    private void ScheduleDebounced(SyncTriggerReason reason)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _debounceCts?.Cancel();
            cts = new CancellationTokenSource();
            _debounceCts = cts;
        }

        _ = RunAfterDebounceAsync(reason, cts.Token);
    }

    private async Task RunAfterDebounceAsync(SyncTriggerReason reason, CancellationToken debounceToken)
    {
        try
        {
            await _delayAsync(_debounceInterval, debounceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // Superseded by a newer edit within the debounce window — coalesced, not queued (§4/§9).
        }

        if (debounceToken.IsCancellationRequested)
        {
            return;
        }

        await TriggerRunAsync(reason).ConfigureAwait(false);
    }

    /// <summary>
    /// The single entry point every automatic trigger funnels through. If a pass is already running,
    /// this never starts a second one in parallel — it marks exactly one follow-up pass pending (§9:
    /// "one follow-up run after #1, not five queued runs") and returns immediately.
    /// </summary>
    private async Task TriggerRunAsync(SyncTriggerReason reason)
    {
        if (!await _runGate.WaitAsync(0).ConfigureAwait(false))
        {
            lock (_lock)
            {
                _runRequestedAgain = true;
            }

            _logger?.LogDebug("Personal Sync pass already running; one follow-up pass will run after it ({Reason}).", reason);
            return;
        }

        try
        {
            var currentReason = reason;
            while (true)
            {
                await ExecutePassAsync(currentReason, CancellationToken.None).ConfigureAwait(false);

                bool again;
                lock (_lock)
                {
                    again = _runRequestedAgain;
                    _runRequestedAgain = false;
                }

                if (!again)
                {
                    break;
                }

                currentReason = SyncTriggerReason.LocalChange;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<PersonalSyncResult> ExecutePassAsync(SyncTriggerReason reason, CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        UpdateStatus(s => s with { IsSyncRunning = true, State = PersonalSyncAutomationState.Syncing, LastAttemptUtc = startedAt });
        _logger?.LogInformation("Personal Sync pass starting (reason={Reason}).", reason);

        PersonalSyncResult result;
        try
        {
            result = await _syncService.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Personal Sync pass threw unexpectedly (reason={Reason}).", reason);
            result = new PersonalSyncResult(PersonalSyncOutcome.FolderUnavailable, 0, 0, 0, 0, 0, 0, "An unexpected error occurred.");
        }

        var conflicts = await SafeGetConflictsAsync(cancellationToken).ConfigureAwait(false);
        var historyConflictCount = conflicts.Count(c => c.EntityType == SyncEntityType.FinalizedPassdown);
        var mutableConflictCount = conflicts.Count - historyConflictCount;

        ApplyResult(reason, result, mutableConflictCount, historyConflictCount);

        _logger?.LogInformation(
            "Personal Sync pass finished (reason={Reason}): outcome={Outcome} imported={Imported} exported={Exported} unchanged={Unchanged} conflicts={Conflicts} historyConflicts={HistoryConflicts} malformed={Malformed} ioFailures={IoFailures}.",
            reason, result.Outcome, result.ImportedCount, result.ExportedCount, result.UnchangedCount,
            mutableConflictCount, historyConflictCount, result.MalformedCount, result.IoFailureCount);

        return result;
    }

    private async Task<IReadOnlyList<SyncConflictSummary>> SafeGetConflictsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _conflictService.GetConflictsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to refresh Personal Sync conflict counts.");
            return [];
        }
    }

    /// <summary>
    /// Phase 16F §10/§28: classifies this pass's outcome into the status the Settings screen shows and
    /// decides whether it warrants a scheduled backoff retry. Only a condition retrying could plausibly
    /// fix (folder unavailable, per-file I-O failures) schedules one; a conflict, a malformed/unsupported
    /// configuration, or a clean success never does (§10's "do not spam retries indefinitely if retry
    /// cannot resolve them" / §15's "do not repeatedly auto-resolve anything").
    /// </summary>
    private void ApplyResult(SyncTriggerReason reason, PersonalSyncResult result, int mutableConflictCount, int historyConflictCount)
    {
        PersonalSyncAutomationSnapshot next;

        lock (_lock)
        {
            var lastSuccess = _status.LastSuccessfulSyncUtc;
            var isTransient = false;
            string? errorMessage = null;
            PersonalSyncAutomationState state;

            switch (result.Outcome)
            {
                case PersonalSyncOutcome.Disabled:
                    state = PersonalSyncAutomationState.Off;
                    break;

                case PersonalSyncOutcome.NotConfigured:
                    state = PersonalSyncAutomationState.NotConfigured;
                    break;

                case PersonalSyncOutcome.FolderUnavailable:
                    state = PersonalSyncAutomationState.Offline;
                    isTransient = true;
                    errorMessage = result.Message;
                    break;

                case PersonalSyncOutcome.ConfigurationError:
                    state = PersonalSyncAutomationState.Error;
                    errorMessage = result.Message;
                    break;

                case PersonalSyncOutcome.AlreadyRunning:
                    // The coordinator itself already serializes passes; if the underlying service still
                    // reports this (e.g. something outside the coordinator called RunAsync directly),
                    // leave the status exactly as it was — nothing new happened this attempt.
                    state = _status.State;
                    lastSuccess = _status.LastSuccessfulSyncUtc;
                    break;

                case PersonalSyncOutcome.Completed:
                    if (historyConflictCount > 0)
                    {
                        state = PersonalSyncAutomationState.HistoryConflictAttention;
                    }
                    else if (mutableConflictCount > 0)
                    {
                        state = PersonalSyncAutomationState.ConflictAttention;
                    }
                    else if (result.IoFailureCount > 0)
                    {
                        state = PersonalSyncAutomationState.Pending;
                        isTransient = true;
                        errorMessage = "Some files could not be read or written; will retry automatically.";
                    }
                    else
                    {
                        state = PersonalSyncAutomationState.UpToDate;
                        lastSuccess = _clock.UtcNow;
                    }

                    break;

                default:
                    state = PersonalSyncAutomationState.Error;
                    break;
            }

            int retryAttempt;
            if (isTransient)
            {
                retryAttempt = _consecutiveTransientFailures + 1;
                _consecutiveTransientFailures = retryAttempt;
                ScheduleRetryLocked(retryAttempt);
            }
            else
            {
                retryAttempt = 0;
                _consecutiveTransientFailures = 0;
                _retryCts?.Cancel();
                _retryCts = null;
            }

            _status = new PersonalSyncAutomationSnapshot(
                state,
                IsSyncRunning: false,
                LastSuccessfulSyncUtc: lastSuccess,
                LastAttemptUtc: _status.LastAttemptUtc,
                HasPendingChanges: state is PersonalSyncAutomationState.Pending or PersonalSyncAutomationState.Offline,
                ConflictCount: mutableConflictCount,
                HistoryConflictCount: historyConflictCount,
                LastResultSummary: BuildSummary(result),
                LastErrorMessage: errorMessage,
                RetryAttempt: retryAttempt);
            next = _status;
        }

        StatusChanged?.Invoke(this, next);
    }

    private static string? BuildSummary(PersonalSyncResult result) => result.Outcome switch
    {
        PersonalSyncOutcome.Completed => $"{result.ImportedCount} imported, {result.ExportedCount} exported, {result.UnchangedCount} unchanged."
            + (result.PriceObservationsMergedCount > 0 ? $" {OfficialPriceHistorySummary.MergedText(result.PriceObservationsMergedCount)}" : string.Empty),
        _ => result.Message,
    };

    /// <summary>Caller must already hold <see cref="_lock"/>.</summary>
    private void ScheduleRetryLocked(int attempt)
    {
        _retryCts?.Cancel();
        var cts = new CancellationTokenSource();
        _retryCts = cts;
        var delay = _backoffSchedule[Math.Min(attempt - 1, _backoffSchedule.Count - 1)];
        _ = RunRetryAfterDelayAsync(delay, cts.Token);
    }

    private async Task RunRetryAfterDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await _delayAsync(delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // Superseded by a success/disable/manual sync that already cleared this retry.
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await TriggerRunAsync(SyncTriggerReason.Retry).ConfigureAwait(false);
    }

    private async Task RunPeriodicLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _delayAsync(_periodicInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            await TriggerRunAsync(SyncTriggerReason.Periodic).ConfigureAwait(false);
        }
    }

    private void UpdateStatus(Func<PersonalSyncAutomationSnapshot, PersonalSyncAutomationSnapshot> update)
    {
        PersonalSyncAutomationSnapshot next;
        lock (_lock)
        {
            _status = update(_status);
            next = _status;
        }

        StatusChanged?.Invoke(this, next);
    }
}
