using System.Text.Json;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.HistoricalBackfill;

/// <summary>
/// Phase 20C: fills in official park hours for the current year's past dates, in the background of
/// an interactive session. Never started by the headless scheduled refresh.
/// </summary>
public interface IHistoricalBackfillService
{
    HistoricalBackfillStatus CurrentStatus { get; }

    /// <summary>Raised (on a background thread) whenever <see cref="CurrentStatus"/> changes.</summary>
    event EventHandler<HistoricalBackfillStatus>? StatusChanged;

    /// <summary>Starts one background session (after the operational startup work). Returns immediately; idempotent.</summary>
    void Start();

    /// <summary>Stops the session; an in-flight request is cancelled and writes nothing.</summary>
    Task StopAsync();

    /// <summary>
    /// The Operations Calendar is showing <paramref name="month"/>: when it is a current-year month with
    /// dates still missing, check those next (still behind all operational work). Never starts network
    /// work by itself.
    /// </summary>
    void PrioritizeMonth(DateOnly month);
}

/// <summary>
/// Phase 20C: <see cref="IHistoricalBackfillService"/>.
///
/// Priority: it starts only after the operational startup work (<see cref="Refresh.IOfficialDataRefreshCoordinator.StartupCompletion"/>
/// — today, the rolling seven days and month-ahead coverage) and a further quiet delay, and every
/// request passes through the shared <see cref="IOfficialRefreshGate"/> as
/// <see cref="OfficialRefreshKind.HistoricalBackfill"/>: it never queues for the gate, never starts while
/// operational work is waiting, and cancels its in-flight request the moment operational work (Check
/// Online, Deep Refresh, an automatic or date check) asks for the gate. A cancelled request writes nothing.
///
/// Work: one date per request (the feed's monthly form does not serve past dates), newest first —
/// yesterday, then the day before, back to January 1 — with a viewed Calendar month promoted. What
/// remains is derived from <c>dated_results</c> each session (<see cref="HistoricalBackfillPolicy.IsComplete"/>),
/// so an interrupted session simply resumes and a finished year makes no requests.
///
/// Storage: only the normalized row the operational refresh already writes (status, times, checked
/// timestamp, the small hours payload), through <see cref="IDatedResultRepository.RecordSuccessAsync"/>
/// — no raw response is kept. Failures write nothing, so they can never displace stored data.
/// </summary>
public sealed class HistoricalBackfillService : IHistoricalBackfillService
{
    private enum DateOutcome
    {
        Stored,
        KeptExisting,
        Failed,
        NotRetained,
        Yielded,
        Busy,
    }

    private readonly IParkHoursHistorySource _source;
    private readonly IDatedResultRepository _datedResultRepository;
    private readonly IOfficialRefreshGate _gate;
    private readonly IClock _clock;
    private readonly Func<Task> _operationalStartupCompletion;
    private readonly IDataChangeNotifier? _dataChangeNotifier;
    private readonly ILogger<HistoricalBackfillService>? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly HistoricalBackfillOptions _options;

    private readonly Lock _lock = new();
    private HistoricalBackfillStatus _status;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _sessionTask;
    private DateOnly? _prioritizedMonth;

    public HistoricalBackfillService(
        IParkHoursHistorySource source,
        IDatedResultRepository datedResultRepository,
        IOfficialRefreshGate gate,
        IClock clock,
        Func<Task>? operationalStartupCompletion = null,
        IDataChangeNotifier? dataChangeNotifier = null,
        ILogger<HistoricalBackfillService>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        HistoricalBackfillOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _datedResultRepository = datedResultRepository ?? throw new ArgumentNullException(nameof(datedResultRepository));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _operationalStartupCompletion = operationalStartupCompletion ?? (() => Task.CompletedTask);
        _dataChangeNotifier = dataChangeNotifier;
        _logger = logger;
        _delayAsync = delayAsync ?? Task.Delay;
        _options = options ?? new HistoricalBackfillOptions();
        _status = HistoricalBackfillStatus.NotStarted(TodayEastern().Year);
    }

    public HistoricalBackfillStatus CurrentStatus
    {
        get { lock (_lock) { return _status; } }
    }

    public event EventHandler<HistoricalBackfillStatus>? StatusChanged;

    /// <summary>The running (or finished) session, so a test can await it.</summary>
    public Task? SessionTask
    {
        get { lock (_lock) { return _sessionTask; } }
    }

    public void Start()
    {
        CancellationToken token;
        lock (_lock)
        {
            if (_lifetimeCts is not null)
            {
                return;
            }

            _lifetimeCts = new CancellationTokenSource();
            token = _lifetimeCts.Token;
        }

        var task = Task.Run(() => RunAsync(token), CancellationToken.None);
        lock (_lock)
        {
            _sessionTask = task;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetimeCts;
        Task? sessionTask;
        lock (_lock)
        {
            lifetimeCts = _lifetimeCts;
            sessionTask = _sessionTask;
        }

        if (lifetimeCts is null)
        {
            return;
        }

        await lifetimeCts.CancelAsync().ConfigureAwait(false);
        if (sessionTask is not null)
        {
            try
            {
                await sessionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    public void PrioritizeMonth(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        lock (_lock)
        {
            _prioritizedMonth = first;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _operationalStartupCompletion().WaitAsync(cancellationToken).ConfigureAwait(false);
            await _delayAsync(_options.StartDelay, cancellationToken).ConfigureAwait(false);
            await RunSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Historical backfill stopped (app closing); completed dates are kept and the rest resumes next start.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Historical backfill stopped unexpectedly; operational data is unaffected and it resumes next start.");
            SetStatus(CurrentStatus with { State = HistoricalBackfillState.PartiallyAvailable, LastActivityAtUtc = _clock.UtcNow });
        }
    }

    /// <summary>One session: derive the remaining dates from stored rows, then check them newest first. Public for tests.</summary>
    public async Task RunSessionAsync(CancellationToken cancellationToken = default)
    {
        var today = TodayEastern();
        if (HistoricalBackfillPolicy.TargetRange(today) is not { } range)
        {
            SetStatus(new HistoricalBackfillStatus(today.Year, HistoricalBackfillState.NotApplicable, LastActivityAtUtc: _clock.UtcNow));
            _logger?.LogInformation("Historical backfill not applicable on {Today:yyyy-MM-dd}: no past dates in {Year} yet (earlier years are never backfilled automatically).", today, today.Year);
            return;
        }

        var stored = await _datedResultRepository
            .GetByCategoryAndDateRangeAsync(SourceCategory.ParkHours, range.Start, range.End, cancellationToken)
            .ConfigureAwait(false);
        var byDate = stored.ToDictionary(r => r.Key.Date);

        var inScope = range.End.DayNumber - range.Start.DayNumber + 1;
        var pending = new SortedSet<DateOnly>();
        for (var date = range.Start; date <= range.End; date = date.AddDays(1))
        {
            if (!HistoricalBackfillPolicy.IsComplete(byDate.GetValueOrDefault(date), date))
            {
                pending.Add(date);
            }
        }

        var complete = inScope - pending.Count;
        var status = new HistoricalBackfillStatus(today.Year, HistoricalBackfillState.Running, inScope, complete, SessionQueued: pending.Count, LastActivityAtUtc: _clock.UtcNow);
        if (pending.Count == 0)
        {
            SetStatus(status with { State = HistoricalBackfillState.UpToDate });
            _logger?.LogInformation("Historical backfill up to date: all {InScope} past dates of {Year} already hold official park hours.", inScope, today.Year);
            return;
        }

        SetStatus(status);
        _logger?.LogInformation(
            complete > 0
                ? "Historical backfill resuming for {Year}: {Pending} of {InScope} past dates still to check ({Complete} already stored), newest first."
                : "Historical backfill starting for {Year}: {Pending} of {InScope} past dates to check, newest first.",
            today.Year, pending.Count, inScope, complete);

        var checkedCount = 0;
        var failed = 0;
        var consecutiveFailures = 0;
        var yields = 0;
        DateOnly? oldestNotRetained = null;
        var unpublished = new Dictionary<(int Year, int Month), DateOnly>();
        var pausedForFailures = false;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = NextDate(pending);
            var outcome = await CheckDateAsync(date, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case DateOutcome.Yielded:
                case DateOutcome.Busy:
                    if (outcome == DateOutcome.Yielded)
                    {
                        yields++;
                        _logger?.LogInformation("Historical backfill yielded to operational refresh work; it resumes once that finishes.");
                    }

                    PublishChanges(unpublished);
                    await WaitForQuietGateAsync(cancellationToken).ConfigureAwait(false);
                    continue;

                case DateOutcome.NotRetained:
                    // The feed keeps a limited window of history; everything older than a "Not Found" past
                    // date is outside it. Those dates are left untouched (never marked, never guessed) and
                    // one probe re-confirms the boundary next session.
                    oldestNotRetained = oldestNotRetained is { } known && known > date ? known : date;
                    var dropped = pending.GetViewBetween(range.Start, date).ToList();
                    foreach (var older in dropped)
                    {
                        pending.Remove(older);
                    }

                    _logger?.LogInformation(
                        "Historical backfill: the official park-hours feed no longer publishes {Date:yyyy-MM-dd}; {Dropped} dates on or before it are not available from the official source.",
                        date, dropped.Count);
                    break;

                case DateOutcome.Failed:
                    pending.Remove(date);
                    failed++;
                    consecutiveFailures++;
                    break;

                case DateOutcome.Stored:
                case DateOutcome.KeptExisting:
                    pending.Remove(date);
                    checkedCount++;
                    consecutiveFailures = 0;
                    if (outcome == DateOutcome.Stored)
                    {
                        complete++;
                        var month = (date.Year, date.Month);
                        unpublished[month] = unpublished.TryGetValue(month, out var newest) && newest > date ? newest : date;
                    }

                    break;
            }

            SetStatus(CurrentStatus with
            {
                DatesComplete = complete,
                SessionChecked = checkedCount,
                SessionFailed = failed,
                OldestNotRetainedDate = oldestNotRetained,
                LastActivityAtUtc = _clock.UtcNow,
            });

            if (unpublished.Count > 0 && checkedCount % _options.ChangeBatchSize == 0)
            {
                PublishChanges(unpublished);
            }

            if (consecutiveFailures >= _options.MaxConsecutiveFailures)
            {
                pausedForFailures = true;
                break;
            }

            if (pending.Count > 0)
            {
                await _delayAsync(_options.RequestSpacing, cancellationToken).ConfigureAwait(false);
            }
        }

        PublishChanges(unpublished);

        var finalState = complete >= inScope ? HistoricalBackfillState.UpToDate : HistoricalBackfillState.PartiallyAvailable;
        SetStatus(CurrentStatus with { State = finalState, DatesComplete = complete, LastActivityAtUtc = _clock.UtcNow });

        if (pausedForFailures)
        {
            _logger?.LogWarning(
                "Historical backfill paused after {Failures} consecutive failed checks (offline or the official source is unavailable): {Checked} checked, {Failed} failed this session. Stored data is unchanged; it resumes next start.",
                _options.MaxConsecutiveFailures, checkedCount, failed);
            return;
        }

        _logger?.LogInformation(
            "Historical backfill finished for {Year}: {Checked} dates checked, {Failed} failed, {Complete} of {InScope} past dates hold official park hours{Boundary} ({Yields} yields to operational work).",
            today.Year, checkedCount, failed, complete, inScope,
            oldestNotRetained is { } boundary ? $"; official history is not published on or before {boundary:yyyy-MM-dd}" : string.Empty,
            yields);
    }

    /// <summary>The newest pending date of a promoted Calendar month (when it still has any), otherwise the newest pending date.</summary>
    private DateOnly NextDate(SortedSet<DateOnly> pending)
    {
        DateOnly? prioritized;
        lock (_lock)
        {
            prioritized = _prioritizedMonth;
        }

        if (prioritized is { } month)
        {
            var inMonth = pending.GetViewBetween(month, month.AddMonths(1).AddDays(-1));
            if (inMonth.Count > 0)
            {
                return inMonth.Max;
            }
        }

        return pending.Max;
    }

    private async Task<DateOutcome> CheckDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        using var preemption = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void OnPriorityWorkWaiting(object? sender, EventArgs e)
        {
            try
            {
                preemption.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request already finished.
            }
        }

        _gate.PriorityWorkWaiting += OnPriorityWorkWaiting;
        try
        {
            using var entry = await _gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.HistoricalBackfill, date), waitForInProcessWork: false, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return DateOutcome.Busy;
            }

            if (_gate.HasPriorityWorkWaiting)
            {
                return DateOutcome.Yielded;
            }

            HistoricalParkHoursResult result;
            try
            {
                result = await _source.RetrieveHistoricalAsync(date, preemption.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DateOutcome.Yielded; // nothing was written; the date stays pending
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogDebug(ex, "Historical park-hours check failed for {Date:yyyy-MM-dd}.", date);
                return DateOutcome.Failed;
            }

            return await StoreAsync(date, result, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.PriorityWorkWaiting -= OnPriorityWorkWaiting;
        }
    }

    private async Task<DateOutcome> StoreAsync(DateOnly date, HistoricalParkHoursResult result, CancellationToken cancellationToken)
    {
        if (result.NotRetainedBySource)
        {
            return DateOutcome.NotRetained;
        }

        var evidence = result.Result.Evidence;
        if (!evidence.Status.IsSuccessfulCompletion())
        {
            _logger?.LogDebug("Historical park-hours check for {Date:yyyy-MM-dd} returned {Status}; nothing stored.", date, evidence.Status);
            return DateOutcome.Failed;
        }

        var key = new DatedResultKey(SourceCategory.ParkHours, date);

        // A definitive stored value (hours or closed) is never replaced by a weaker past-date answer.
        var existing = await _datedResultRepository.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing?.SuccessEvidence is { Status: RetrievalStatus.Verified or RetrievalStatus.Closed }
            && evidence.Status is RetrievalStatus.NotPublished or RetrievalStatus.Unavailable)
        {
            _logger?.LogDebug("Historical park-hours check for {Date:yyyy-MM-dd} returned {Status}; the stored {Stored} value was kept.", date, evidence.Status, existing.SuccessEvidence.Status);
            return DateOutcome.KeptExisting;
        }

        // Same serialization as the operational DatedSourceRegistration, so the Calendar/Associate read
        // historical rows exactly like any other.
        var payloadJson = result.Result.Payload is null ? null : JsonSerializer.Serialize(result.Result.Payload);
        await _datedResultRepository.RecordSuccessAsync(key, evidence, payloadJson, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Historical park hours stored for {Date:yyyy-MM-dd} ({Status}).", date, evidence.Status);
        return DateOutcome.Stored;
    }

    /// <summary>Waits until no operational work holds or wants the gate, then a short quiet period (an in-memory check, never a database poll).</summary>
    private async Task WaitForQuietGateAsync(CancellationToken cancellationToken)
    {
        do
        {
            await _delayAsync(_options.YieldQuietPeriod, cancellationToken).ConfigureAwait(false);
        }
        while (_gate.Current is not null || _gate.HasPriorityWorkWaiting);
    }

    private void PublishChanges(Dictionary<(int Year, int Month), DateOnly> unpublished)
    {
        foreach (var newest in unpublished.Values)
        {
            _dataChangeNotifier?.Publish(new DataChange(DataChangeKind.HistoricalOfficialData, newest));
        }

        unpublished.Clear();
    }

    private void SetStatus(HistoricalBackfillStatus status)
    {
        lock (_lock)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "A historical backfill status subscriber failed.");
        }
    }

    private DateOnly TodayEastern() => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(_clock.UtcNow).DateTime);
}
