namespace Carowinds.Passdown.Application.Concurrency;

/// <summary>Which kind of online work holds the <see cref="IOfficialRefreshGate"/> (Phase 19A).</summary>
public enum OfficialRefreshKind
{
    /// <summary>The automatic app-wide check of every official source over the rolling window (startup or every four hours).</summary>
    Comprehensive,

    /// <summary>The startup catch-up of a missed daily scheduled maintenance run (finalization plus the due-gated Deep Refresh).</summary>
    StartupMaintenance,

    /// <summary>A date-specific check: an explicit Check Online, or the Associate screen's quiet automatic check of a stale today/future date.</summary>
    DateCheck,

    /// <summary>The manual "Run Deep Refresh" action.</summary>
    DeepRefresh,

    /// <summary>
    /// Phase 20C: one date of the current-year historical park-hours backfill. The only low-priority
    /// kind: it never waits for the gate, and any other kind arriving while it holds the gate asks it to
    /// yield (<see cref="IOfficialRefreshGate.PriorityWorkWaiting"/>).
    /// </summary>
    HistoricalBackfill,
}

/// <summary>One unit of online work passing through the gate. <see cref="Date"/> is set for <see cref="OfficialRefreshKind.DateCheck"/> only.</summary>
public sealed record OfficialRefreshActivity(OfficialRefreshKind Kind, DateOnly? Date = null)
{
    /// <summary>True for the kinds that check every official source (and so may honestly be shown as "Checking official data…").</summary>
    public bool IsComprehensive => Kind is OfficialRefreshKind.Comprehensive or OfficialRefreshKind.StartupMaintenance or OfficialRefreshKind.DeepRefresh;

    /// <summary>Phase 20C: true for opportunistic background enrichment that must always give way to operational work.</summary>
    public bool IsLowPriority => Kind is OfficialRefreshKind.HistoricalBackfill;
}

/// <summary>
/// Phase 19A: the single in-process entry point every online refresh passes through — automatic
/// comprehensive refresh, startup maintenance, Check Online (Dashboard and Associate), the
/// Associate screen's automatic date check, and the manual Deep Refresh — so two of them can never
/// run at the same time inside the app. Holding the gate also holds the existing cross-process
/// <see cref="IScheduledRefreshLock"/>, so a <c>--scheduled-refresh</c> process and the open app
/// still exclude each other exactly as before (Phase 13B Part K).
/// </summary>
public interface IOfficialRefreshGate
{
    /// <summary>The activity currently holding the gate, or <see langword="null"/>.</summary>
    OfficialRefreshActivity? Current { get; }

    /// <summary>Raised (on the entering thread) once an activity holds the gate.</summary>
    event EventHandler<OfficialRefreshActivity>? Started;

    /// <summary>Raised when an activity releases the gate, whatever its outcome.</summary>
    event EventHandler<OfficialRefreshActivity>? Completed;

    /// <summary>
    /// Phase 20C: raised (on the entering thread) when operational work — any activity that is not
    /// <see cref="OfficialRefreshActivity.IsLowPriority"/> — starts trying to enter while low-priority
    /// work holds the gate, so the holder can cancel its in-flight request and release at once.
    /// </summary>
    event EventHandler? PriorityWorkWaiting;

    /// <summary>Phase 20C: true while operational work is waiting for (or about to take) the gate. Low-priority work never starts then.</summary>
    bool HasPriorityWorkWaiting { get; }

    /// <summary>
    /// Tries to start <paramref name="activity"/>. With <paramref name="waitForInProcessWork"/>, an
    /// in-process activity already holding the gate is waited for (an explicit user action queues
    /// behind an automatic check rather than failing); without it, the attempt returns
    /// <see langword="null"/> immediately (an automatic check is simply skipped). A cross-process
    /// holder of the scheduled-refresh lock is never waited for: the result is
    /// <see langword="null"/>. Dispose the returned handle to release the gate.
    /// </summary>
    Task<IDisposable?> TryEnterAsync(OfficialRefreshActivity activity, bool waitForInProcessWork, CancellationToken cancellationToken = default);
}

public sealed class OfficialRefreshGate(Func<IScheduledRefreshLock>? crossProcessLockFactory = null) : IOfficialRefreshGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private OfficialRefreshActivity? _current;
    private int _priorityWaiters;

    public OfficialRefreshActivity? Current => Volatile.Read(ref _current);

    public bool HasPriorityWorkWaiting => Volatile.Read(ref _priorityWaiters) > 0;

    public event EventHandler<OfficialRefreshActivity>? Started;

    public event EventHandler<OfficialRefreshActivity>? Completed;

    public event EventHandler? PriorityWorkWaiting;

    public async Task<IDisposable?> TryEnterAsync(OfficialRefreshActivity activity, bool waitForInProcessWork, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.IsLowPriority)
        {
            // Low-priority work never queues: it starts only when nothing else holds or wants the gate.
            if (HasPriorityWorkWaiting || !await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (HasPriorityWorkWaiting)
            {
                _semaphore.Release();
                return null;
            }

            return CompleteEntry(activity, cancellationToken);
        }

        Interlocked.Increment(ref _priorityWaiters);
        try
        {
            if (Current is { IsLowPriority: true })
            {
                RaisePriorityWorkWaiting();
            }

            if (waitForInProcessWork)
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (!await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _priorityWaiters);
        }

        return CompleteEntry(activity, cancellationToken);
    }

    private IDisposable? CompleteEntry(OfficialRefreshActivity activity, CancellationToken cancellationToken)
    {
        // SemaphoreSlim observes cancellation asynchronously: a waiter cancelled just before a Release
        // can still be handed the slot. A superseded request (e.g. the Associate date already changed)
        // must never start work, so give the slot straight back.
        if (cancellationToken.IsCancellationRequested)
        {
            _semaphore.Release();
            cancellationToken.ThrowIfCancellationRequested();
        }

        IScheduledRefreshLock? crossProcessLock = null;
        try
        {
            if (crossProcessLockFactory is not null)
            {
                crossProcessLock = crossProcessLockFactory();
                if (!crossProcessLock.TryAcquire())
                {
                    _semaphore.Release();
                    return null;
                }
            }
        }
        catch
        {
            _semaphore.Release();
            throw;
        }

        // Full fence: operational work announces itself (Interlocked increment) before reading Current,
        // and low-priority work re-checks HasPriorityWorkWaiting after this write — so one of the two
        // always sees the other and a background request never makes operational work wait it out.
        Interlocked.Exchange(ref _current, activity);
        Raise(Started, activity);
        return new Entry(this, activity, crossProcessLock);
    }

    private void RaisePriorityWorkWaiting()
    {
        try
        {
            PriorityWorkWaiting?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // A yielding background job must never break the operational work asking it to yield.
        }
    }

    private void Exit(OfficialRefreshActivity activity, IScheduledRefreshLock? crossProcessLock)
    {
        try
        {
            crossProcessLock?.Dispose();
        }
        finally
        {
            Volatile.Write(ref _current, null);
            _semaphore.Release();
            Raise(Completed, activity);
        }
    }

    private void Raise(EventHandler<OfficialRefreshActivity>? handlers, OfficialRefreshActivity activity)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<OfficialRefreshActivity>>())
        {
            try
            {
                handler(this, activity);
            }
            catch (Exception)
            {
                // Status/invalidation subscribers are conveniences; they must never break the refresh.
            }
        }
    }

    private sealed class Entry(OfficialRefreshGate owner, OfficialRefreshActivity activity, IScheduledRefreshLock? crossProcessLock) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Exit(activity, crossProcessLock);
            }
        }
    }
}
