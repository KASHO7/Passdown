using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Finalization;

/// <summary>One newly finalized passdown, for the scheduled host/startup catch-up to log individually (spec Phase 9B Part R).</summary>
public sealed record ScheduledFinalizationEntry(DateOnly PassdownDate, FinalizationOrigin Origin);

/// <summary>
/// Result of one <see cref="IScheduledPassdownFinalizer.FinalizeEligibleAsync"/> call (spec Phase 9B
/// Part R). <see cref="EligibleCount"/> is every saved draft date whose cutoff has passed;
/// <see cref="FinalizedCount"/> + <see cref="AlreadyFinalizedCount"/> always sum to it — every
/// eligible date is either newly finalized this run or was already finalized by an earlier run.
/// </summary>
public sealed record ScheduledFinalizationSummary(
    int EligibleCount,
    int FinalizedCount,
    int AlreadyFinalizedCount,
    IReadOnlyList<ScheduledFinalizationEntry> Finalized)
{
    public static ScheduledFinalizationSummary None { get; } = new(0, 0, 0, []);
}

/// <summary>
/// Application-layer scheduled-finalization coordinator (spec Phase 9B Part K) — the sequencing
/// piece that turns "it is past 6:00 AM Eastern for one or more saved drafts" into finalized
/// <see cref="Persistence.FinalizedPassdown"/> rows, called by the Desktop scheduled host and startup
/// catch-up path before either ever runs a source refresh (Part B/H). Deliberately has no knowledge
/// of HTTP, Playwright, <see cref="Refresh.IRefreshOrchestrator"/>, or Windows Task Scheduler — it
/// only reads what is already persisted locally (Part M) via
/// <see cref="IAssociatePassdownService.LoadAssociateDateAsync"/>, which itself never triggers
/// retrieval.
/// </summary>
public interface IScheduledPassdownFinalizer
{
    Task<ScheduledFinalizationSummary> FinalizeEligibleAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IScheduledPassdownFinalizer"/>
public sealed class ScheduledPassdownFinalizer(
    IAssociateDraftRepository draftRepository,
    IAssociatePassdownService associatePassdownService,
    IFinalizedPassdownRepository finalizedPassdownRepository,
    IFinalizedPassdownFinalizationService finalizationService,
    IClock clock,
    ILogger<ScheduledPassdownFinalizer> logger) : IScheduledPassdownFinalizer
{
    public async Task<ScheduledFinalizationSummary> FinalizeEligibleAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var todayEastern = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(now).DateTime);

        // Only saved draft dates are candidates at all (Part D) — a date that merely *can* be
        // loaded (every date can; AssociatePassdownService.LoadAssociateDateAsync always composes a
        // default workspace) is never treated as having real content to finalize.
        var draftDates = await draftRepository.ListSavedDraftDatesAsync(cancellationToken).ConfigureAwait(false);
        var eligibleDates = draftDates
            .Where(date => FinalizationEligibility.EligibleForFinalizationAt(date) <= now)
            .OrderBy(date => date) // chronological: oldest eligible date first (Part F).
            .ToList();

        var finalized = new List<ScheduledFinalizationEntry>();
        var alreadyFinalizedCount = 0;

        foreach (var passdownDate in eligibleDates)
        {
            if (await finalizedPassdownRepository.ExistsForDateAsync(passdownDate, cancellationToken).ConfigureAwait(false))
            {
                alreadyFinalizedCount++;
                continue;
            }

            var origin = DetermineOrigin(passdownDate, todayEastern);

            // Local-only composition (Part M/U) — never calls IRefreshOrchestrator/HTTP/Playwright, so
            // this whole pass performs zero network calls. Phase 21A: the finalization load freezes the
            // passdown as it stood at the end of its day, without the historical-view pricing rule.
            var workspace = await associatePassdownService.LoadAssociateDateForFinalizationAsync(passdownDate, cancellationToken).ConfigureAwait(false);
            var result = await finalizationService.FinalizeAsync(passdownDate, workspace, origin, now, cancellationToken).ConfigureAwait(false);

            if (result.Outcome == FinalizationOutcome.Created)
            {
                finalized.Add(new ScheduledFinalizationEntry(passdownDate, origin));
                logger.LogInformation("Finalized passdown {PassdownDate} ({Origin}).", passdownDate, origin);
            }
            else
            {
                // Lost a race against an earlier/concurrent finalization between the ExistsForDateAsync
                // check above and this call — the finalization service's own check-before-insert (Part J)
                // already turned that into a safe no-op rather than a duplicate row.
                alreadyFinalizedCount++;
            }
        }

        return new ScheduledFinalizationSummary(eligibleDates.Count, finalized.Count, alreadyFinalizedCount, finalized);
    }

    /// <summary>
    /// Spec Phase 9B Part G's deterministic origin rule: a passdown whose normal cutoff falls on
    /// today's Eastern calendar date is a normal on-time finalization; one whose cutoff already
    /// passed on an earlier Eastern calendar date is a missed-run catch-up. Never inferred from
    /// Windows Task Scheduler's own missed-run reporting. <paramref name="passdownDate"/>'s cutoff
    /// calendar date is always <paramref name="passdownDate"/> + 1 day by
    /// <see cref="FinalizationEligibility.EligibleForFinalizationAt"/>'s own definition (6:00 AM
    /// Eastern the following day) — it can never be later than <paramref name="todayEastern"/> here,
    /// since <see cref="FinalizeEligibleAsync"/> only reaches this for already-eligible dates.
    /// </summary>
    private static FinalizationOrigin DetermineOrigin(DateOnly passdownDate, DateOnly todayEastern)
    {
        var cutoffCalendarDate = passdownDate.AddDays(1);
        return cutoffCalendarDate == todayEastern ? FinalizationOrigin.Scheduled : FinalizationOrigin.Recovery;
    }
}
