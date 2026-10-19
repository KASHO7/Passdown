using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Updates;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Updates;

public enum UpdateAvailabilityStatus
{
    /// <summary>No trusted key or repository in this build: nothing is ever checked.</summary>
    NotConfigured,

    /// <summary>Configured, no check has finished yet in this session.</summary>
    NotChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    CheckFailed,
}

/// <summary>Phase 23D: what Settings shows about update availability. Immutable; replaced on every change.</summary>
public sealed record UpdateAvailabilitySnapshot(
    UpdateAvailabilityStatus Status,
    UpdateOffer? Offer,
    DateTimeOffset? LastSuccessfulCheckUtc,
    UpdateSourceFailureKind? FailureKind,
    DateTimeOffset? RetryAfterUtc,
    bool IsSnoozed)
{
    /// <summary>True only for an approved update the user has not asked to be reminded about later — drives the subtle Settings indicator.</summary>
    public bool ShouldNotify => Status == UpdateAvailabilityStatus.UpdateAvailable && !IsSnoozed;
}

public interface IApplicationUpdateCoordinator
{
    UpdateAvailabilitySnapshot Current { get; }

    event EventHandler<UpdateAvailabilitySnapshot>? Changed;

    void Start();

    Task StopAsync();

    /// <summary>The Check for Updates button. Joins a check already running instead of starting another.</summary>
    Task<UpdateAvailabilitySnapshot> CheckNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Remind Me Later: hides the indicator for this version for <see cref="ApplicationUpdateCoordinator.SnoozeDuration"/>.</summary>
    Task SnoozeAsync(SemanticVersion version);

    /// <summary>The last version whose What's New was dismissed on this computer (local only, never synced).</summary>
    string? LastAcknowledgedReleaseNotesVersion { get; }

    /// <summary>Records that What's New for <paramref name="version"/> was seen, so it does not open automatically again.</summary>
    Task AcknowledgeReleaseNotesAsync(SemanticVersion version);
}

/// <summary>
/// Phase 23D: when to check. Once shortly after startup (never blocking the window), then every
/// <see cref="AutomaticInterval"/> while the app stays open, plus the manual button. All checks go through one
/// gate so they never overlap; manual presses within <see cref="ManualCoalesceWindow"/> of a finished check reuse
/// its result; a rate limit or failure pushes the next automatic check out (persisted, so restarts do not
/// hammer GitHub's 60-requests-per-hour unauthenticated limit shared by every PC behind one office address).
/// Entirely separate from official-data retrieval: different HTTP requests, no shared locks, no database.
/// </summary>
public sealed class ApplicationUpdateCoordinator : IApplicationUpdateCoordinator
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(90);

    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(6);

    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan ManualCoalesceWindow = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(7);

    /// <summary>Upper bound on how long one check may take end-to-end, whatever the network does.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    private readonly UpdateDiscoveryService _discovery;
    private readonly IUpdateStateStore _stateStore;
    private readonly IClock _clock;
    private readonly SemanticVersion _installedVersion;
    private readonly bool _isConfigured;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger? _logger;
    private readonly object _gate = new();

    private UpdateLocalState _state;
    private Task<UpdateAvailabilitySnapshot>? _running;
    private DateTimeOffset? _lastFinishedUtc;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;

    public ApplicationUpdateCoordinator(
        UpdateDiscoveryService discovery,
        IUpdateStateStore stateStore,
        IClock clock,
        SemanticVersion installedVersion,
        bool isConfigured,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        ILogger<ApplicationUpdateCoordinator>? logger = null)
    {
        _discovery = discovery;
        _stateStore = stateStore;
        _clock = clock;
        _installedVersion = installedVersion;
        _isConfigured = isConfigured;
        _delayAsync = delayAsync ?? Task.Delay;
        _logger = logger;
        _state = stateStore.Load();
        Current = new UpdateAvailabilitySnapshot(
            isConfigured ? UpdateAvailabilityStatus.NotChecked : UpdateAvailabilityStatus.NotConfigured,
            null, _state.LastSuccessfulCheckUtc, null, null, false);
    }

    public UpdateAvailabilitySnapshot Current { get; private set; }

    public event EventHandler<UpdateAvailabilitySnapshot>? Changed;

    /// <summary>The background loop; tests await it.</summary>
    public Task? LoopTask => _loopTask;

    public void Start()
    {
        if (!_isConfigured || _loopTask is not null)
        {
            return;
        }

        _loopCancellation = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_loopCancellation is null || _loopTask is null)
        {
            return;
        }

        await _loopCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task<UpdateAvailabilitySnapshot> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            return Task.FromResult(Current);
        }

        lock (_gate)
        {
            if (_running is not null)
            {
                return _running;
            }

            var now = _clock.UtcNow;
            if (_lastFinishedUtc is { } finished && now - finished < ManualCoalesceWindow && Current.Status != UpdateAvailabilityStatus.CheckFailed)
            {
                return Task.FromResult(Current);
            }

            if (Current is { Status: UpdateAvailabilityStatus.CheckFailed, FailureKind: UpdateSourceFailureKind.RateLimited, RetryAfterUtc: { } retry } && retry > now)
            {
                return Task.FromResult(Current);
            }

            // A check that finished synchronously already cleared _running in its finally (same thread, lock is reentrant).
            var check = RunCheckAsync(cancellationToken);
            if (!check.IsCompleted)
            {
                _running = check;
            }

            return check;
        }
    }

    public async Task SnoozeAsync(SemanticVersion version)
    {
        _state = _state with { SnoozedVersion = version.Normalized, SnoozedUntilUtc = _clock.UtcNow + SnoozeDuration };
        await _stateStore.SaveAsync(_state).ConfigureAwait(false);
        Publish(Current with { IsSnoozed = IsSnoozed(Current.Offer) });
    }

    public string? LastAcknowledgedReleaseNotesVersion => _state.LastAcknowledgedReleaseNotesVersion;

    public async Task AcknowledgeReleaseNotesAsync(SemanticVersion version)
    {
        if (_state.LastAcknowledgedReleaseNotesVersion == version.Normalized)
        {
            return;
        }

        _state = _state with { LastAcknowledgedReleaseNotesVersion = version.Normalized };
        await _stateStore.SaveAsync(_state).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        await _delayAsync(StartupDelay, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _clock.UtcNow;
            var wait = _state.NextCheckNotBeforeUtc is { } notBefore && notBefore > now ? notBefore - now : TimeSpan.Zero;
            if (wait > TimeSpan.Zero)
            {
                await _delayAsync(wait < AutomaticInterval ? wait : AutomaticInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await CheckNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Automatic update check failed unexpectedly ({ExceptionType}).", ex.GetType().Name);
            }

            await _delayAsync(AutomaticInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<UpdateAvailabilitySnapshot> RunCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            Publish(Current with { Status = UpdateAvailabilityStatus.Checking, FailureKind = null, RetryAfterUtc = null });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            UpdateCheckResult result;
            try
            {
                result = await _discovery.CheckAsync(_installedVersion, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = UpdateCheckResult.Failed(UpdateSourceFailureKind.Timeout);
            }
            catch (OperationCanceledException)
            {
                Publish(Current with { Status = Current.Offer is null ? UpdateAvailabilityStatus.NotChecked : UpdateAvailabilityStatus.UpdateAvailable });
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Update check failed ({ExceptionType}).", ex.GetType().Name);
                result = UpdateCheckResult.Failed(UpdateSourceFailureKind.InvalidResponse);
            }

            var now = _clock.UtcNow;
            UpdateAvailabilitySnapshot snapshot;
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _state = _state with { LastSuccessfulCheckUtc = now, NextCheckNotBeforeUtc = null };
                    snapshot = new UpdateAvailabilitySnapshot(UpdateAvailabilityStatus.UpdateAvailable, result.Offer, now, null, null, IsSnoozed(result.Offer));
                    break;
                case UpdateCheckStatus.UpToDate:
                    _state = _state with { LastSuccessfulCheckUtc = now, NextCheckNotBeforeUtc = null };
                    snapshot = new UpdateAvailabilitySnapshot(UpdateAvailabilityStatus.UpToDate, null, now, null, null, false);
                    break;
                case UpdateCheckStatus.NotConfigured:
                    snapshot = Current with { Status = UpdateAvailabilityStatus.NotConfigured };
                    break;
                default:
                    var retry = result.FailureKind == UpdateSourceFailureKind.RateLimited && result.RetryAfterUtc is { } after && after > now
                        ? after
                        : now + FailureBackoff;
                    _state = _state with { NextCheckNotBeforeUtc = retry };
                    // A previously found offer stays valid (it was verified); only the status line changes.
                    snapshot = Current with { Status = UpdateAvailabilityStatus.CheckFailed, FailureKind = result.FailureKind, RetryAfterUtc = result.FailureKind == UpdateSourceFailureKind.RateLimited ? retry : null };
                    break;
            }

            await _stateStore.SaveAsync(_state, CancellationToken.None).ConfigureAwait(false);
            Publish(snapshot);
            return snapshot;
        }
        finally
        {
            lock (_gate)
            {
                _running = null;
                _lastFinishedUtc = _clock.UtcNow;
            }
        }
    }

    private bool IsSnoozed(UpdateOffer? offer) =>
        offer is not null
        && _state.SnoozedVersion == offer.Version.Normalized
        && _state.SnoozedUntilUtc is { } until
        && until > _clock.UtcNow;

    private void Publish(UpdateAvailabilitySnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, snapshot);
    }
}

/// <summary>Phase 23D: the one rule for automatically opening What's New after a launch.</summary>
public static class WhatsNewPolicy
{
    /// <summary>
    /// Opens What's New only when the latest install record is a confirmed success for exactly the version now
    /// running and that version's notes were not acknowledged yet. A clean installation (no record), a failed
    /// or rolled-back attempt, or a record for another version never opens it.
    /// </summary>
    public static bool ShouldShowAfterUpdate(UpdateInstallOutcome? latest, SemanticVersion runningVersion, string? lastAcknowledgedVersion) =>
        latest is { Status: UpdateInstallStatus.Succeeded }
        && SemanticVersion.TryParse(latest.ToVersion, out var installed)
        && installed.Equals(runningVersion)
        && !string.Equals(lastAcknowledgedVersion, runningVersion.Normalized, StringComparison.Ordinal);
}
