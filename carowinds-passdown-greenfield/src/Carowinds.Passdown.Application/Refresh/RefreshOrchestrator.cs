using System.Collections.Concurrent;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval.Browser;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// <see cref="IRefreshOrchestrator"/> built on the Phase 2 freshness/persistence services and the
/// Phase 3 <see cref="SourceRegistration"/>s registered with it (Phase 3G).
///
/// One planned "operation" is one (source, date) pair for a <see cref="SourceScope.Dated"/>
/// registration, or one (source, no date) for a <see cref="SourceScope.Catalog"/> registration.
/// Every operation is persisted independently by <see cref="SourceRegistration.ExecuteAsync"/> as
/// it completes (Part F "save immediately"/"as they arrive") — this class never batches that.
/// What this class *does* aggregate, once per source after all of that source's operations in a
/// job finish, is the source's own scheduling row (<see cref="ISourceStateRepository"/>): a
/// dated source's daily job may cover several dates, but <c>source_state</c> is keyed by source
/// id alone (spec §6.2), so it tracks "did this source's job complete cleanly", not any one
/// date's outcome. A dated source's job counts as a successful completion only when every one of
/// its planned dates reached a successful retrieval status — one erroring date is enough to leave
/// last-success untouched and bump the failure count, so the source stays due for a retry, while
/// every date that did succeed already has its own independently-persisted last-known-good value
/// regardless (Part F/G). A job that gets cancelled skips the source-state update for any source
/// with a cancelled operation entirely, rather than recording a spurious failure (Part G "cancellation
/// does not corrupt completed results").
/// </summary>
public sealed class RefreshOrchestrator(
    IReadOnlyList<SourceRegistration> registrations,
    IFreshnessPlanner freshnessPlanner,
    IRollingDatePlanner rollingDatePlanner,
    ISourceStateRepository sourceStateRepository,
    IReadOnlyDictionary<SourceCategory, FreshnessPolicy> freshnessPolicies,
    IJobBrowserSessionFactory browserSessionFactory,
    IClock clock,
    RefreshOrchestratorOptions options,
    IDeepRefreshStatusRepository? deepRefreshStatusRepository = null,
    ParkHoursScheduleDiscovery? scheduleDiscovery = null) : IRefreshOrchestrator
{
    public async Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var operations = await PlanScheduledOperationsAsync(cancellationToken).ConfigureAwait(false);
        return await ExecuteJobAsync(RefreshJobType.ScheduledRollingWindow, operations, updateSourceState: true, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RefreshSummary> RunUserForcedRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Deliberately no freshness gating (spec §6.3, Phase 13B Part B/G: an explicit "Check
        // Online" always checks live, for every currently registered source family — dated *and*
        // catalog) and no source-state update (Part C: must never alter when the scheduled job next
        // considers a source due). Every registration participates exactly once: a Dated
        // registration against the requested date, a Catalog registration with no date at all —
        // the same shape ExecuteJobAsync/SourceRegistration.ExecuteAsync already expect from the
        // scheduled path, so partial-failure/immediate-persistence/bounded-concurrency all apply
        // identically here without any new logic.
        var operations = registrations
            .Select(r => new PlannedOperation(r, r.Scope == SourceScope.Dated ? date : null, Rank: 0))
            .ToList();

        return await ExecuteJobAsync(RefreshJobType.UserForced, operations, updateSourceState: false, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RefreshSummary> RunComprehensiveRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // The same rolling-window shape PlanScheduledOperationsAsync produces, minus its due-gate:
        // with every category on a daily 6 AM cadence, the due-gate alone would skip every source for
        // the rest of the day, which would make a four-hour check meaningless.
        var window = rollingDatePlanner.GetRefreshWindow();
        var operations = registrations
            .SelectMany(r => r.Scope == SourceScope.Dated
                ? window.Select((date, rank) => new PlannedOperation(r, date, rank))
                : [new PlannedOperation(r, RequestedDate: null, window.Count)])
            .OrderBy(p => p.Rank)
            .ToList();

        return await ExecuteJobAsync(RefreshJobType.Comprehensive, operations, updateSourceState: true, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RefreshSummary> RunDeepRefreshAsync(
        bool bypassFreshness,
        DateOnly? focusDate = null,
        IProgress<RefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var operations = await PlanDeepRefreshOperationsAsync(bypassFreshness, focusDate, cancellationToken).ConfigureAwait(false);
        var summary = await ExecuteJobAsync(RefreshJobType.DeepRefresh, operations, updateSourceState: true, progress, cancellationToken)
            .ConfigureAwait(false);

        if (deepRefreshStatusRepository is not null)
        {
            await PersistDeepRefreshStatusAsync(summary, CancellationToken.None).ConfigureAwait(false);
        }

        return summary;
    }

    /// <summary>
    /// Phase 13D Part U/AR: records truthfully what this run actually achieved — never optimistically
    /// marking a partial/failed run as Success, and <see cref="DeepRefreshStatusRecord.CoverageThrough"/>
    /// reflects the real last-consecutive-succeeded Park Hours date from today, not the desired target.
    /// Deliberately <see cref="CancellationToken.None"/>, mirroring <see cref="UpdateSourceStateAsync"/>'s
    /// own reasoning — a caller-cancelled job must not lose this bookkeeping for whatever did complete.
    /// </summary>
    private async Task PersistDeepRefreshStatusAsync(RefreshSummary summary, CancellationToken cancellationToken)
    {
        var outcome = summary.Failed == 0 && summary.Cancelled == 0
            ? DeepRefreshOutcome.Success
            : summary.Succeeded > 0
                ? DeepRefreshOutcome.Partial
                : DeepRefreshOutcome.Failed;

        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);
        // Phase 22E.1: a date is covered by a successful single-date request, or by being listed (and
        // stored) in a published-schedule month response — even when another date of that month failed.
        var coveredDates = new HashSet<DateOnly>();
        foreach (var result in summary.Results.Where(r => r.Category == SourceCategory.ParkHours))
        {
            if (result.CoveredDates is { } monthDates)
            {
                coveredDates.UnionWith(monthDates);
            }
            else if (result is { Outcome: SourceOperationOutcomeKind.Success, RequestedDate: { } date })
            {
                coveredDates.Add(date);
            }
        }

        DateOnly? coverageThrough = null;
        for (var date = today; coveredDates.Contains(date); date = date.AddDays(1))
        {
            coverageThrough = date;
        }

        var record = new DeepRefreshStatusRecord(
            LastAttemptAtUtc: summary.CompletedAt,
            LastSuccessAtUtc: outcome == DeepRefreshOutcome.Success ? summary.CompletedAt : null,
            Outcome: outcome,
            CoverageThrough: coverageThrough,
            SourceSuccessCount: summary.Succeeded,
            SourceFailureCount: summary.Failed);

        await deepRefreshStatusRepository!.SaveAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Park Hours always plans the full future coverage window, unconditionally (Part J/K/L) — the
    /// due-ness clock only ever asks "was this source checked in the last day," a different question
    /// from "is next month's calendar populated," so due-gating Park Hours here would never actually
    /// achieve the coverage this operation exists to guarantee. Every other source reuses the exact
    /// same due-gated planning <see cref="PlanScheduledOperationsAsync"/> already implements when
    /// <paramref name="bypassFreshness"/> is false (never a second, duplicated due-gating
    /// implementation), or the same unconditional shape <see cref="RunUserForcedRefreshAsync"/> uses
    /// — across the normal rolling window rather than one date — when it is true.
    /// </summary>
    private async Task<IReadOnlyList<PlannedOperation>> PlanDeepRefreshOperationsAsync(
        bool bypassFreshness, DateOnly? focusDate, CancellationToken cancellationToken)
    {
        var normalWindow = rollingDatePlanner.GetRefreshWindow();

        List<PlannedOperation> otherPlanned;
        if (bypassFreshness)
        {
            otherPlanned = registrations
                .Where(r => r.Category != SourceCategory.ParkHours)
                .SelectMany(r => r.Scope == SourceScope.Dated
                    ? BuildDatedWindowWithFocusDate(r, normalWindow, focusDate)
                    : [new PlannedOperation(r, RequestedDate: null, normalWindow.Count)])
                .ToList();
        }
        else
        {
            var scheduled = await PlanScheduledOperationsAsync(cancellationToken).ConfigureAwait(false);
            otherPlanned = scheduled.Where(p => p.Registration.Category != SourceCategory.ParkHours).ToList();

            // Even when only due sources are refreshed, an explicitly requested focus date (the
            // insufficient-coverage Check Online bootstrap) must still be covered for every dated
            // source regardless of due-ness — the associate asked about this specific date.
            if (focusDate is { } date && !normalWindow.Contains(date))
            {
                otherPlanned = otherPlanned
                    .Concat(registrations
                        .Where(r => r.Category != SourceCategory.ParkHours && r.Scope == SourceScope.Dated)
                        .Select(r => new PlannedOperation(r, date, normalWindow.Count)))
                    .ToList();
            }
        }

        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);
        var parkHoursRegistration = registrations.Single(r => r.Category == SourceCategory.ParkHours);
        if (scheduleDiscovery is not null)
        {
            return PlanPublishedScheduleDiscovery(scheduleDiscovery, parkHoursRegistration, normalWindow, otherPlanned, focusDate, today);
        }

        var parkHoursWindow = ParkHoursCoveragePlanner.BuildCoverageWindow(today);
        var parkHoursPlanned = parkHoursWindow
            .Select((date, rank) => new PlannedOperation(parkHoursRegistration, date, rank))
            .ToList();

        var rebasedOther = otherPlanned.Select(p => p with { Rank = p.Rank + parkHoursWindow.Count });
        return parkHoursPlanned.Concat(rebasedOther).OrderBy(p => p.Rank).ToList();
    }

    /// <summary>
    /// Phase 22E.1: Park Hours for Deep Refresh with published-schedule discovery. The rolling window
    /// (today first) is read one date at a time and shares the front ranks with every other source's
    /// near-term dates, so today and the next days keep priority. Every later date through the
    /// discovery horizon is read with one official month request per month, ranked after all of that —
    /// about 13 requests for a year instead of one per date. A focus date beyond the horizon still gets
    /// its own single-date request.
    /// </summary>
    private static IReadOnlyList<PlannedOperation> PlanPublishedScheduleDiscovery(
        ParkHoursScheduleDiscovery discovery,
        SourceRegistration parkHoursRegistration,
        IReadOnlyList<DateOnly> normalWindow,
        IReadOnlyList<PlannedOperation> otherPlanned,
        DateOnly? focusDate,
        DateOnly today)
    {
        var dailyDates = normalWindow.Count > 0 ? normalWindow.ToList() : [today];
        var horizon = ParkHoursCoveragePlanner.DiscoveryHorizon(today, discovery.MonthsAhead);
        var discoveryStart = dailyDates.Max().AddDays(1);

        var planned = dailyDates
            .Select((date, rank) => new PlannedOperation(parkHoursRegistration, date, rank))
            .Concat(otherPlanned)
            .ToList();

        if (focusDate is { } focus && focus > horizon && !dailyDates.Contains(focus))
        {
            planned.Add(new PlannedOperation(parkHoursRegistration, focus, dailyDates.Count));
        }

        var monthRankBase = planned.Count == 0 ? 0 : planned.Max(p => p.Rank) + 1;
        planned.AddRange(discovery.PlanMonths(discoveryStart, horizon)
            .Select((month, index) => new PlannedOperation(month.Registration, month.MonthStart, monthRankBase + index)));

        return planned.OrderBy(p => p.Rank).ToList();
    }

    private static IEnumerable<PlannedOperation> BuildDatedWindowWithFocusDate(
        SourceRegistration registration, IReadOnlyList<DateOnly> window, DateOnly? focusDate)
    {
        for (var rank = 0; rank < window.Count; rank++)
        {
            yield return new PlannedOperation(registration, window[rank], rank);
        }

        if (focusDate is { } date && !window.Contains(date))
        {
            yield return new PlannedOperation(registration, date, window.Count);
        }
    }

    /// <summary>Only a due source is planned at all (Part B) — the rolling window (today, tomorrow, the remaining days, spec §6.5) is expanded solely for sources that are actually due.</summary>
    private async Task<IReadOnlyList<PlannedOperation>> PlanScheduledOperationsAsync(CancellationToken cancellationToken)
    {
        var dueRegistrations = new List<SourceRegistration>();
        foreach (var registration in registrations)
        {
            var decision = await freshnessPlanner.EvaluateAsync(registration.SourceId, registration.Category, cancellationToken).ConfigureAwait(false);
            if (decision.IsDue)
            {
                dueRegistrations.Add(registration);
            }
        }

        var window = rollingDatePlanner.GetRefreshWindow();

        var planned = new List<PlannedOperation>();
        foreach (var registration in dueRegistrations.Where(r => r.Scope == SourceScope.Dated))
        {
            for (var rank = 0; rank < window.Count; rank++)
            {
                planned.Add(new PlannedOperation(registration, window[rank], rank));
            }
        }

        // Catalog operations carry no date-priority meaning (Part D only prioritizes dated work);
        // ranking them after every dated rank keeps dated work at the front of the dispatch order
        // without claiming a stricter guarantee than bounded concurrency actually provides.
        foreach (var registration in dueRegistrations.Where(r => r.Scope == SourceScope.Catalog))
        {
            planned.Add(new PlannedOperation(registration, RequestedDate: null, Rank: window.Count));
        }

        // Enumerable.OrderBy is a stable sort, so registration order (and therefore source
        // independence — Part D "do not sacrifice source independence to preserve ordering") is
        // preserved within each rank.
        return planned.OrderBy(p => p.Rank).ToList();
    }

    private async Task<RefreshSummary> ExecuteJobAsync(
        RefreshJobType jobType,
        IReadOnlyList<PlannedOperation> plannedOperations,
        bool updateSourceState,
        IProgress<RefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        var total = plannedOperations.Count;
        var completed = 0;
        var results = new ConcurrentBag<SourceOperationResult>();

        // Lazy per spec §6.4/Part E: creating the wrapper does no I/O, and EnsureStartedAsync is
        // only ever awaited for an operation whose registration actually requires it — a job with
        // no such due source never starts a browser at all.
        var needsBrowserSession = plannedOperations.Any(p => p.Registration.RequiresBrowserSession);
        var browserSession = needsBrowserSession ? browserSessionFactory.Create() : null;

        try
        {
            using var concurrencyLimiter = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));

            var tasks = plannedOperations.Select(operation => RunOneAsync(operation)).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            async Task RunOneAsync(PlannedOperation operation)
            {
                SourceOperationResult result;
                try
                {
                    await concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (operation.Registration.RequiresBrowserSession && browserSession is not null)
                        {
                            await browserSession.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
                        }

                        result = await operation.Registration.ExecuteAsync(operation.RequestedDate, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        concurrencyLimiter.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    // A cancelled operation is reported as Cancelled, never coerced into an
                    // ordinary Failure (Part D) — and one operation's cancellation never faults
                    // Task.WhenAll, so every other in-flight/independent operation still gets to
                    // finish and persist on its own (Part G).
                    result = SourceOperationResult.Cancelled(operation.Registration.SourceId, operation.Registration.Category, operation.RequestedDate);
                }
                catch (Exception ex)
                {
                    // Defensive only: every current adapter already converts its own failures into
                    // an Error SourceResult internally rather than throwing. This guards against a
                    // future adapter bug taking down the rest of an otherwise-independent job.
                    result = SourceOperationResult.Failure(
                        operation.Registration.SourceId, operation.Registration.Category, operation.RequestedDate,
                        status: null, $"Unexpected error: {ex.Message}");
                }

                results.Add(result);
                var completedCount = Interlocked.Increment(ref completed);
                progress?.Report(new RefreshProgress(total, completedCount, result, cancellationToken.IsCancellationRequested));
            }
        }
        finally
        {
            if (browserSession is not null)
            {
                await browserSession.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (updateSourceState)
        {
            // Deliberately CancellationToken.None: a source with no cancelled operation in its
            // group genuinely finished (Success/Failure only), and its bookkeeping write must
            // still land even if the caller cancelled the job in the meantime — otherwise caller
            // cancellation could throw out of this method entirely and discard the whole summary
            // for a job with two or more due sources (Part G "cancellation does not corrupt
            // completed results"). A source with a cancelled operation is skipped inside
            // UpdateSourceStateAsync before it ever reaches a repository call.
            foreach (var group in results.GroupBy(r => (r.SourceId, r.Category)))
            {
                var registration = plannedOperations.First(p => p.Registration.SourceId == group.Key.SourceId).Registration;
                await UpdateSourceStateAsync(registration, group.ToList(), startedAt, CancellationToken.None).ConfigureAwait(false);
            }
        }

        var orderedResults = results.OrderBy(r => r.Category).ThenBy(r => r.RequestedDate).ToList();
        return new RefreshSummary(
            jobType,
            startedAt,
            clock.UtcNow,
            OperationsAttempted: total,
            Succeeded: orderedResults.Count(r => r.Outcome == SourceOperationOutcomeKind.Success),
            Failed: orderedResults.Count(r => r.Outcome == SourceOperationOutcomeKind.Failure),
            Cancelled: orderedResults.Count(r => r.Outcome == SourceOperationOutcomeKind.Cancelled),
            orderedResults);
    }

    /// <summary>
    /// Updates one source's <c>source_state</c> row once, from every operation that source ran in
    /// this job (see class remarks for the aggregation rule). Skipped entirely — leaving prior
    /// state untouched — when any of that source's operations were cancelled.
    /// </summary>
    private async Task UpdateSourceStateAsync(
        SourceRegistration registration,
        IReadOnlyList<SourceOperationResult> operationResults,
        DateTimeOffset attemptAt,
        CancellationToken cancellationToken)
    {
        if (operationResults.Count == 0 || operationResults.Any(r => r.Outcome == SourceOperationOutcomeKind.Cancelled))
        {
            return;
        }

        var succeeded = operationResults.All(r => r.Outcome == SourceOperationOutcomeKind.Success);
        var existing = await sourceStateRepository.GetAsync(registration.SourceId, cancellationToken).ConfigureAwait(false);
        var policy = freshnessPolicies[registration.Category];

        SourceStateRecord updated;
        if (succeeded)
        {
            var nextDueAt = policy.ComputeNextDueAt(EasternTimeZone.ConvertFromUtc(attemptAt));
            updated = new SourceStateRecord(registration.SourceId, registration.Category, attemptAt, attemptAt, nextDueAt, existing?.SourceFingerprint, ConsecutiveFailureCount: 0);
        }
        else
        {
            DateTimeOffset? nextDueAt = existing?.LastSuccessAt is { } lastSuccess
                ? policy.ComputeNextDueAt(EasternTimeZone.ConvertFromUtc(lastSuccess))
                : null;
            updated = new SourceStateRecord(
                registration.SourceId, registration.Category, attemptAt, existing?.LastSuccessAt, nextDueAt,
                existing?.SourceFingerprint, ConsecutiveFailureCount: (existing?.ConsecutiveFailureCount ?? 0) + 1);
        }

        await sourceStateRepository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private sealed record PlannedOperation(SourceRegistration Registration, DateOnly? RequestedDate, int Rank);
}
