using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>How a request to quietly check one Associate date ended (Phase 19A).</summary>
public enum DateCheckOutcome
{
    /// <summary>The date's official data was checked online.</summary>
    Checked,

    /// <summary>Past dates are never checked automatically.</summary>
    SkippedPastDate,

    /// <summary>The date's cached official data was already fresh — no request was made.</summary>
    SkippedFresh,

    /// <summary>The date was already checked automatically a short while ago (see <see cref="OfficialRefreshPolicy.DateCheckCooldown"/>).</summary>
    SkippedCooldown,

    /// <summary>Another process (the scheduled task) holds the refresh lock; nothing was run.</summary>
    SkippedBusy,

    /// <summary>Automatic refresh is not running (never started — e.g. the headless host or a test — or already stopped); nothing was run.</summary>
    SkippedNotRunning,
}

/// <summary>
/// Phase 19A: the interactive app's single owner of automatic official-data retrieval — one
/// instance for the whole app lifetime (never per screen), started once after the main window is
/// shown and stopped during shutdown. Never started by the headless <c>--scheduled-refresh</c> host.
/// </summary>
public interface IOfficialDataRefreshCoordinator
{
    /// <summary>The current app-wide official-data status (what the shared freshness indicator shows).</summary>
    OfficialDataStatus CurrentStatus { get; }

    /// <summary>Raised (on a background thread) whenever <see cref="CurrentStatus"/> changes.</summary>
    event EventHandler<OfficialDataStatus>? StatusChanged;

    /// <summary>Starts the startup freshness check and the four-hour background loop. Returns immediately; idempotent.</summary>
    void Start();

    /// <summary>Cancels the background loop and any automatic check in flight, then waits for it to unwind.</summary>
    Task StopAsync();

    /// <summary>
    /// The Associate screen's quiet automatic check for <paramref name="date"/>: never for a past date,
    /// and only when that date's official data is missing or stale. Uses the same date-specific
    /// semantics as Check Online (every dated source for the date, every catalog source once).
    /// <paramref name="onCheckStarted"/> is invoked, on a background thread, only when a network check
    /// actually begins. Cancel <paramref name="cancellationToken"/> when the selected date changes.
    /// </summary>
    Task<DateCheckOutcome> CheckDateIfStaleAsync(DateOnly date, Action? onCheckStarted = null, CancellationToken cancellationToken = default);
}

public sealed class OfficialDataRefreshCoordinator : IOfficialDataRefreshCoordinator
{
    private readonly IRefreshOrchestrator _orchestrator;
    private readonly IReadOnlyList<SourceRegistration> _registrations;
    private readonly ISourceStateRepository _sourceStateRepository;
    private readonly IDatedResultRepository _datedResultRepository;
    private readonly IReadOnlyDictionary<SourceCategory, FreshnessPolicy> _policies;
    private readonly IOfficialRefreshGate _gate;
    private readonly IClock _clock;
    private readonly ILogger<OfficialDataRefreshCoordinator>? _logger;
    private readonly IDataChangeNotifier? _dataChangeNotifier;
    private readonly IScheduledPassdownFinalizer? _finalizer;
    private readonly IScheduledRefreshRunner? _maintenanceRunner;
    private readonly IDeepRefreshStatusRepository? _deepRefreshStatusRepository;
    private readonly IPersonalSyncCoordinator? _personalSyncCoordinator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    private readonly Lock _lock = new();
    private readonly Dictionary<DateOnly, DateTimeOffset> _dateCheckCompletedAt = [];
    private OfficialDataStatus _status = OfficialDataStatus.NotCheckedYet;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _loopTask;
    private Task _startupCompleted = Task.CompletedTask;
    private DateOnly _lastObservedEasternDate;

    public OfficialDataRefreshCoordinator(
        IRefreshOrchestrator orchestrator,
        IReadOnlyList<SourceRegistration> registrations,
        ISourceStateRepository sourceStateRepository,
        IDatedResultRepository datedResultRepository,
        IReadOnlyDictionary<SourceCategory, FreshnessPolicy> policies,
        IOfficialRefreshGate gate,
        IClock clock,
        ILogger<OfficialDataRefreshCoordinator>? logger = null,
        IDataChangeNotifier? dataChangeNotifier = null,
        IScheduledPassdownFinalizer? finalizer = null,
        IScheduledRefreshRunner? maintenanceRunner = null,
        IDeepRefreshStatusRepository? deepRefreshStatusRepository = null,
        IPersonalSyncCoordinator? personalSyncCoordinator = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
        _sourceStateRepository = sourceStateRepository ?? throw new ArgumentNullException(nameof(sourceStateRepository));
        _datedResultRepository = datedResultRepository ?? throw new ArgumentNullException(nameof(datedResultRepository));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
        _dataChangeNotifier = dataChangeNotifier;
        _finalizer = finalizer;
        _maintenanceRunner = maintenanceRunner;
        _deepRefreshStatusRepository = deepRefreshStatusRepository;
        _personalSyncCoordinator = personalSyncCoordinator;
        _delayAsync = delayAsync ?? Task.Delay;

        _lastObservedEasternDate = TodayEastern();

        // Every online refresh in this process passes through the gate, so its events are the one place
        // that knows "official cached data may have just changed" and "a comprehensive check is running".
        _gate.Started += OnGateStarted;
        _gate.Completed += OnGateCompleted;
    }

    public OfficialDataStatus CurrentStatus
    {
        get { lock (_lock) { return _status; } }
    }

    public event EventHandler<OfficialDataStatus>? StatusChanged;

    /// <summary>The background loop (startup check, then the four-hour cadence), so a test can await it.</summary>
    public Task? LoopTask => _loopTask;

    /// <summary>Completes once the startup check after <see cref="Start"/> has finished (successfully or not).</summary>
    public Task StartupCompletion
    {
        get { lock (_lock) { return _startupCompleted; } }
    }

    private bool IsRunning
    {
        get { lock (_lock) { return _lifetimeCts is { IsCancellationRequested: false }; } }
    }

    public void Start()
    {
        TaskCompletionSource startupCompleted;
        CancellationToken token;
        lock (_lock)
        {
            if (_lifetimeCts is not null)
            {
                return;
            }

            _lifetimeCts = new CancellationTokenSource();
            token = _lifetimeCts.Token;
            startupCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startupCompleted = startupCompleted.Task;
        }

        _loopTask = Task.Run(() => RunLoopAsync(startupCompleted, token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetimeCts;
        Task? loopTask;
        lock (_lock)
        {
            lifetimeCts = _lifetimeCts;
            loopTask = _loopTask;
            if (lifetimeCts is null)
            {
                return;
            }
        }

        await lifetimeCts.CancelAsync().ConfigureAwait(false);
        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the loop observed the lifetime cancellation.
            }
        }

        _logger?.LogInformation("Automatic official-data refresh stopped.");
    }

    private async Task RunLoopAsync(TaskCompletionSource startupCompleted, CancellationToken cancellationToken)
    {
        try
        {
            await RunStartupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Startup official-data check failed; cached data remains usable.");
        }
        finally
        {
            startupCompleted.TrySetResult();
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                wait = await RunDueCheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Automatic official-data refresh failed; cached data remains usable. It will be tried again later.");
                wait = OfficialRefreshPolicy.MaxSleepSlice;
            }

            try
            {
                await _delayAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Interactive startup (spec §5/§6/§36): finalize any eligible passdown and catch up a missed daily
    /// maintenance run (the former startup catch-up — now skipped when the scheduled task already ran
    /// today), then make sure official data is fresh with one comprehensive check — unless a fully
    /// successful comprehensive check finished within <see cref="OfficialRefreshPolicy.StartupFreshnessGrace"/>.
    /// Runs on a background task after the window is shown; never blocks the UI.
    /// </summary>
    public async Task RunStartupAsync(CancellationToken cancellationToken = default)
    {
        if (!await RunStartupMaintenanceAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var status = await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
        if (OfficialRefreshPolicy.IsFreshEnoughToSkipStartupCheck(status, _clock.UtcNow))
        {
            _logger?.LogInformation(
                "Startup official-data check skipped: every official source was checked at {LastComprehensiveCheckAt:O} ({State}), within the {GraceMinutes}-minute startup grace period.",
                status.LastComprehensiveCheckAt, status.State, OfficialRefreshPolicy.StartupFreshnessGrace.TotalMinutes);
            return;
        }

        _logger?.LogInformation(
            "Startup official-data check needed (status {State}, last comprehensive check {LastComprehensiveCheckAt}).",
            status.State, status.LastComprehensiveCheckAt?.ToString("O") ?? "never");
        await RunComprehensiveAsync(isStartup: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns false when startup online work must not continue (another process holds the refresh lock, or finalization failed — Phase 9B Part T ordering).</summary>
    private async Task<bool> RunStartupMaintenanceAsync(CancellationToken cancellationToken)
    {
        if (_finalizer is null && _maintenanceRunner is null)
        {
            return true;
        }

        using var entry = await _gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.StartupMaintenance), waitForInProcessWork: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            _logger?.LogInformation("Startup catch-up skipped: a scheduled refresh is already running.");
            return false;
        }

        if (_finalizer is not null)
        {
            ScheduledFinalizationSummary finalization;
            try
            {
                finalization = await _finalizer.FinalizeEligibleAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Startup passdown finalization failed; startup online refresh was not run. Cached data remains usable.");
                return false;
            }

            if (finalization.EligibleCount > 0)
            {
                _logger?.LogInformation(
                    "Startup passdown finalization: {Eligible} eligible, {Finalized} finalized, {AlreadyFinalized} already finalized.",
                    finalization.EligibleCount, finalization.FinalizedCount, finalization.AlreadyFinalizedCount);
            }

            if (finalization.FinalizedCount > 0)
            {
                _personalSyncCoordinator?.RequestSync(SyncTriggerReason.Finalization);
                _dataChangeNotifier?.Publish(new DataChange(DataChangeKind.FinalizedHistory));
            }
        }

        if (_maintenanceRunner is null)
        {
            return true;
        }

        var deepRefreshStatus = _deepRefreshStatusRepository is null
            ? null
            : await _deepRefreshStatusRepository.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!OfficialRefreshPolicy.IsDailyMaintenanceDue(deepRefreshStatus, _policies[SourceCategory.ParkHours], _clock.UtcNow))
        {
            _logger?.LogInformation(
                "Startup catch-up maintenance skipped: today's scheduled maintenance already ran at {LastAttemptAtUtc:O}.",
                deepRefreshStatus?.LastAttemptAtUtc);
            return true;
        }

        _logger?.LogInformation("Startup catch-up maintenance needed: today's scheduled maintenance has not run yet.");
        await _maintenanceRunner.RunAsync(RefreshMode.StartupCatchUp, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// One evaluation of the four-hour cadence: runs a comprehensive check when one is due, and returns
    /// how long the loop should sleep before evaluating again (never longer than
    /// <see cref="OfficialRefreshPolicy.MaxSleepSlice"/>, so a PC waking from sleep is noticed promptly —
    /// and then at most one check runs, never one per missed interval).
    /// </summary>
    public async Task<TimeSpan> RunDueCheckAsync(CancellationToken cancellationToken = default)
    {
        ObserveOperatingDay();

        var now = _clock.UtcNow;
        if (OfficialRefreshPolicy.IsAutomaticCheckDue(CurrentStatus, now))
        {
            // The cached status may be stale (another process may have refreshed); confirm from the database.
            var status = await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
            if (OfficialRefreshPolicy.IsAutomaticCheckDue(status, _clock.UtcNow))
            {
                await RunComprehensiveAsync(isStartup: false, cancellationToken).ConfigureAwait(false);
            }
        }

        now = _clock.UtcNow;
        var next = OfficialRefreshPolicy.NextAutomaticCheckAt(CurrentStatus, now);
        var untilNext = next is { } due && due > now ? due - now : OfficialRefreshPolicy.MaxSleepSlice;
        return untilNext < OfficialRefreshPolicy.MaxSleepSlice ? untilNext : OfficialRefreshPolicy.MaxSleepSlice;
    }

    private async Task RunComprehensiveAsync(bool isStartup, CancellationToken cancellationToken)
    {
        if (_gate.Current is { } active)
        {
            _logger?.LogInformation("Automatic official-data refresh waiting: {ActiveKind} is already in progress (never run concurrently).", active.Kind);
        }

        using var entry = await _gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            _logger?.LogInformation("Automatic official-data refresh skipped: another refresh (the scheduled task) is already running.");
            return;
        }

        // While this waited for the gate, a manual Deep Refresh (or another process) may already have
        // checked everything — that resets the four-hour clock, so do not repeat it.
        var status = await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        if (isStartup ? OfficialRefreshPolicy.IsFreshEnoughToSkipStartupCheck(status, now) : !OfficialRefreshPolicy.IsAutomaticCheckDue(status, now))
        {
            _logger?.LogInformation(
                "Automatic official-data refresh skipped: official data was checked at {LastComprehensiveCheckAt:O} while waiting (manual refresh resets the schedule).",
                status.LastComprehensiveCheckAt);
            return;
        }

        _logger?.LogInformation(
            "Automatic official-data refresh started ({Trigger}; interval {IntervalHours}h; last comprehensive check {LastComprehensiveCheckAt}).",
            isStartup ? "startup" : "four-hour schedule", OfficialRefreshPolicy.AutomaticInterval.TotalHours,
            status.LastComprehensiveCheckAt?.ToString("O") ?? "never");

        var summary = await _orchestrator.RunComprehensiveRefreshAsync(progress: null, cancellationToken).ConfigureAwait(false);
        LogComprehensiveSummary(summary);
    }

    private void LogComprehensiveSummary(RefreshSummary summary)
    {
        var failures = summary.Results.Where(r => r.Outcome == SourceOperationOutcomeKind.Failure).ToList();
        if (summary.Cancelled > 0)
        {
            _logger?.LogInformation("Automatic official-data refresh cancelled: {Succeeded} succeeded, {Cancelled} cancelled.", summary.Succeeded, summary.Cancelled);
            return;
        }

        if (failures.Count == 0)
        {
            _logger?.LogInformation(
                "Automatic official-data refresh completed: {Attempted} checks succeeded in {ElapsedMs:F0}ms.",
                summary.OperationsAttempted, (summary.CompletedAt - summary.StartedAt).TotalMilliseconds);
            return;
        }

        // Source id/category/status only — the detail text can carry endpoint/exception specifics that
        // belong to the orchestrator's own per-source records, not a routine status line.
        var failedSources = string.Join(", ", failures.Select(f => f.SourceId).Distinct(StringComparer.Ordinal));
        if (summary.Succeeded == 0)
        {
            _logger?.LogWarning(
                "Automatic official-data refresh failed: all {Failed} checks failed ({FailedSources}). Last-known-good data was kept.",
                summary.Failed, failedSources);
        }
        else
        {
            _logger?.LogWarning(
                "Automatic official-data refresh partially failed: {Succeeded} succeeded, {Failed} failed ({FailedSources}). Last-known-good data was kept for the failed checks.",
                summary.Succeeded, summary.Failed, failedSources);
        }
    }

    public async Task<DateCheckOutcome> CheckDateIfStaleAsync(DateOnly date, Action? onCheckStarted = null, CancellationToken cancellationToken = default)
    {
        // Automatic retrieval exists only while the interactive app's coordinator runs — never in the
        // headless host, and never from a view model resolved by a test.
        if (!IsRunning)
        {
            return DateCheckOutcome.SkippedNotRunning;
        }

        if (date < TodayEastern())
        {
            _logger?.LogDebug("Associate date auto-check not applicable: {OperatingDate:yyyy-MM-dd} is in the past.", date);
            return DateCheckOutcome.SkippedPastDate;
        }

        // Let the startup check (which covers today and the rolling window) finish first, so opening the
        // app never fetches today's data twice.
        await StartupCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (IsInDateCheckCooldown(date))
        {
            _logger?.LogDebug("Associate date auto-check skipped: {OperatingDate:yyyy-MM-dd} was checked automatically moments ago.", date);
            return DateCheckOutcome.SkippedCooldown;
        }

        if (!await IsDateStaleAsync(date, cancellationToken).ConfigureAwait(false))
        {
            _logger?.LogInformation("Associate date auto-check skipped: official data for {OperatingDate:yyyy-MM-dd} is fresh.", date);
            return DateCheckOutcome.SkippedFresh;
        }

        using var entry = await _gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DateCheck, date), waitForInProcessWork: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            _logger?.LogInformation("Associate date auto-check skipped for {OperatingDate:yyyy-MM-dd}: another refresh (the scheduled task) is already running.", date);
            return DateCheckOutcome.SkippedBusy;
        }

        // Whatever held the gate before (e.g. the comprehensive check) may already have covered this date.
        if (!await IsDateStaleAsync(date, cancellationToken).ConfigureAwait(false))
        {
            _logger?.LogInformation("Associate date auto-check skipped: official data for {OperatingDate:yyyy-MM-dd} became fresh while waiting.", date);
            return DateCheckOutcome.SkippedFresh;
        }

        _logger?.LogInformation("Associate date auto-check started for {OperatingDate:yyyy-MM-dd} (missing or stale official data).", date);
        onCheckStarted?.Invoke();

        var summary = await _orchestrator.RunUserForcedRefreshAsync(date, progress: null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _dateCheckCompletedAt[date] = _clock.UtcNow;
        }

        _logger?.LogInformation(
            "Associate date auto-check completed for {OperatingDate:yyyy-MM-dd}: {Succeeded} succeeded, {Failed} failed.",
            date, summary.Succeeded, summary.Failed);
        return DateCheckOutcome.Checked;
    }

    private bool IsInDateCheckCooldown(DateOnly date)
    {
        lock (_lock)
        {
            return _dateCheckCompletedAt.TryGetValue(date, out var completedAt)
                && _clock.UtcNow - completedAt < OfficialRefreshPolicy.DateCheckCooldown
                && _clock.UtcNow >= completedAt;
        }
    }

    private async Task<bool> IsDateStaleAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var cached = await _datedResultRepository.GetByRequestedDateAsync(date, cancellationToken).ConfigureAwait(false);
        var datedCategories = _registrations.Where(r => r.Scope == SourceScope.Dated).Select(r => r.Category);
        return DateCheckFreshness.IsMissingOrStale(date, cached, datedCategories, _policies, _clock.UtcNow);
    }

    /// <summary>Re-reads the authoritative status from persisted source state and publishes it if it changed.</summary>
    private async Task<OfficialDataStatus> RefreshStatusAsync(CancellationToken cancellationToken)
    {
        var persisted = await OfficialDataStatus.ReadAsync(_registrations, _sourceStateRepository, cancellationToken).ConfigureAwait(false);
        var isChecking = _gate.Current is { IsComprehensive: true };
        return SetStatus(persisted with { IsChecking = isChecking });
    }

    private OfficialDataStatus SetStatus(OfficialDataStatus status)
    {
        lock (_lock)
        {
            if (_status.HasSameContentAs(status))
            {
                return _status;
            }

            _status = status;
        }

        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "An official-data status subscriber failed.");
        }

        return status;
    }

    private void OnGateStarted(object? sender, OfficialRefreshActivity activity)
    {
        if (activity.IsComprehensive)
        {
            SetStatus(CurrentStatus with { IsChecking = true });
        }
    }

    private void OnGateCompleted(object? sender, OfficialRefreshActivity activity)
    {
        // Official cached data may have changed: invalidate local projections (no network involved).
        _dataChangeNotifier?.Publish(new DataChange(DataChangeKind.OfficialData, activity.Kind == OfficialRefreshKind.DateCheck ? activity.Date : null));
        _ = RefreshStatusAfterCompletionAsync(activity);
    }

    private async Task RefreshStatusAfterCompletionAsync(OfficialRefreshActivity activity)
    {
        try
        {
            var status = await RefreshStatusAsync(CancellationToken.None).ConfigureAwait(false);
            if (activity.Kind == OfficialRefreshKind.DeepRefresh)
            {
                _logger?.LogInformation(
                    "Manual Deep Refresh finished; the automatic four-hour schedule now runs from {LastComprehensiveCheckAt} (next check about {NextAutomaticCheckAt}).",
                    status.LastComprehensiveCheckAt?.ToString("O") ?? "never", status.NextAutomaticCheckAt(_clock.UtcNow)?.ToString("O") ?? "as soon as possible");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read official-data status after a refresh.");
            SetStatus(CurrentStatus with { IsChecking = _gate.Current is { IsComprehensive: true } });
        }
    }

    /// <summary>Publishes an <see cref="DataChangeKind.OperatingDay"/> change when the Eastern date rolls over while the app is open.</summary>
    private void ObserveOperatingDay()
    {
        var today = TodayEastern();
        lock (_lock)
        {
            if (today == _lastObservedEasternDate)
            {
                return;
            }

            _lastObservedEasternDate = today;
            foreach (var past in _dateCheckCompletedAt.Keys.Where(d => d < today).ToList())
            {
                _dateCheckCompletedAt.Remove(past);
            }
        }

        _logger?.LogInformation("Operating day changed to {OperatingDate:yyyy-MM-dd}; refreshing local views.", today);
        _dataChangeNotifier?.Publish(new DataChange(DataChangeKind.OperatingDay, today));
    }

    private DateOnly TodayEastern() => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(_clock.UtcNow).DateTime);
}
