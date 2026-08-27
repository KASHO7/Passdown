using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>
/// <see cref="IOperationsDashboardService"/> built entirely as a projection over existing
/// Application-layer state (Phase 10A Part C/J):
/// <see cref="IAssociatePassdownService.LoadAssociateDateAsync"/> for the composed hours/draft/
/// verification state (already local-only by its own contract),
/// <see cref="IFinalizedPassdownRepository"/> for finalization metadata only — never
/// <c>GetByDateAsync</c>, so a dashboard load can never materialize a historical PDF blob (Phase
/// 10A Part S) — and (Phase 11C Part C) <see cref="IOperationalNotesService"/> for persistent
/// Operational Notes relevant to the same operating date, reusing its existing
/// <see cref="IOperationalNotesService.ListRelevantForDateAsync"/> relevance/ordering rules rather
/// than a second implementation. No HTTP/Playwright/Task Scheduler dependency exists on this type by
/// construction.
/// </summary>
public sealed class OperationsDashboardService(
    IAssociatePassdownService associatePassdownService,
    IFinalizedPassdownRepository finalizedPassdownRepository,
    IOperationalNotesService operationalNotesService,
    IClock clock) : IOperationsDashboardService
{
    public Task<OperationsDashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(TodayEastern(), cancellationToken);

    public async Task<OperationsDashboardSnapshot> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var workspace = await associatePassdownService.LoadAssociateDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        return await BuildSnapshotAsync(operatingDate, workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Explicit Check Online (Phase 10D Part L): reuses the manual-date refresh already exposed by
    /// <see cref="IAssociatePassdownService.CheckAssociateDateOnlineAsync"/> — the same dependency
    /// this service already has, so no new coupling to <see cref="IRefreshOrchestrator"/> is needed.
    /// The returned snapshot is built directly from the refresh's own recomposed workspace (Part O),
    /// never a second <c>LoadAssociateDateAsync</c> round trip.
    /// </summary>
    public async Task<OperationsDashboardCheckOnlineResult> CheckOnlineAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var today = TodayEastern();
        var result = await associatePassdownService.CheckAssociateDateOnlineAsync(today, progress, cancellationToken).ConfigureAwait(false);
        var snapshot = await BuildSnapshotAsync(today, result.Workspace, cancellationToken).ConfigureAwait(false);
        return new OperationsDashboardCheckOnlineResult(snapshot, result.Summary);
    }

    private async Task<OperationsDashboardSnapshot> BuildSnapshotAsync(DateOnly operatingDate, AssociateWorkspace workspace, CancellationToken cancellationToken)
    {
        var parkStatus = BuildParkStatus(workspace.ParkHours);
        var (passdownStatus, latestFinalized) = await BuildPassdownAndHistoryAsync(operatingDate, workspace.SaveStatus, cancellationToken).ConfigureAwait(false);

        // Mirrors AssociateViewModel.OnIsEarlyRideTimeEnabledChanged's own "off means not
        // applicable" rule (Desktop/ViewModels/AssociateViewModel.cs) so the dashboard's summary
        // never disagrees with what the Associate Overview screen itself would show.
        var earlyRideTimeEnabled = !string.IsNullOrWhiteSpace(workspace.Draft.EarlyRideTime.Details)
            || workspace.Draft.EarlyRideTime.ParticipatingAttractions.Count > 0;

        var notes = BuildNotesSummary(workspace.Draft.OperationalNotes);
        var activeOperationalNotes = await BuildActiveOperationalNotesAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var dataHealth = BuildDataHealth(workspace);

        return new OperationsDashboardSnapshot(
            operatingDate,
            parkStatus,
            passdownStatus,
            workspace.Draft.ProjectedAttendance,
            workspace.Draft.PreSolds,
            earlyRideTimeEnabled,
            workspace.Draft.EarlyRideTime.Details,
            workspace.Draft.DailyFeatureType,
            workspace.Draft.DailyFeatureText,
            notes,
            activeOperationalNotes,
            dataHealth,
            latestFinalized,
            clock.UtcNow);
    }

    /// <summary>
    /// Persistent Operational Notes relevant to <paramref name="operatingDate"/> (Phase 11C Part
    /// B/C/D/E) — one call to the existing, authoritative
    /// <see cref="IOperationalNotesService.ListRelevantForDateAsync"/> (already Important-first
    /// ordered per <see cref="OperationalNoteOrdering.ByRelevance"/> — never a second relevance or
    /// ordering rule), bounded here to <see cref="DashboardOperationalNotesSummary.MaxPreviewCount"/>
    /// preview items while <see cref="DashboardOperationalNotesSummary.Count"/> keeps the true total
    /// (Part H). A failure querying local persistent notes degrades only this section (Part AI)
    /// rather than the whole Dashboard snapshot — deliberately just one try/catch, not a general
    /// partial-result framework, since nothing else on the Dashboard currently needs one.
    /// </summary>
    private async Task<DashboardOperationalNotesSummary> BuildActiveOperationalNotesAsync(DateOnly operatingDate, CancellationToken cancellationToken)
    {
        IReadOnlyList<OperationalNote> relevant;
        try
        {
            relevant = await operationalNotesService.ListRelevantForDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DashboardOperationalNotesSummary.Empty with { LoadFailed = true };
        }

        var importantCount = relevant.Count(note => note.Priority == OperationalNotePriority.Important);
        var items = relevant
            .Take(DashboardOperationalNotesSummary.MaxPreviewCount)
            .Select(note => new DashboardOperationalNoteItem(note.Id, note.Title, note.Category, note.Priority, note.EffectiveFrom, note.EffectiveThrough))
            .ToList();

        return new DashboardOperationalNotesSummary(relevant.Count, importantCount, items, LoadFailed: false);
    }

    private DateOnly TodayEastern() => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);

    private static DashboardParkStatusSummary BuildParkStatus(AssociateParkHoursView hours)
    {
        var effective = hours.Effective;

        var status = effective.Status switch
        {
            EffectiveHoursStatus.Open when effective.IsPrivateBuyoutApplied => DashboardParkStatus.PrivateBuyout,
            EffectiveHoursStatus.Open when effective.IsManualOverrideApplied => DashboardParkStatus.CustomHours,
            EffectiveHoursStatus.Open => DashboardParkStatus.Open,
            EffectiveHoursStatus.Closed => DashboardParkStatus.Closed,
            _ => hours.Verification.State switch
            {
                AssociateVerificationState.NotPublished => DashboardParkStatus.NotPublished,
                AssociateVerificationState.Unavailable => DashboardParkStatus.Unavailable,
                AssociateVerificationState.Error => DashboardParkStatus.Unavailable,
                _ => DashboardParkStatus.NotChecked,
            },
        };

        return new DashboardParkStatusSummary(status, hours);
    }

    /// <summary>
    /// One metadata-only <see cref="IFinalizedPassdownRepository.ListAsync"/> call serves both today's
    /// own passdown status and the "latest finalized" History integration surface (Phase 10C Parts
    /// A/B/L) — never <c>GetByDateAsync</c>, so a dashboard load can never materialize a historical
    /// PDF blob or deserialize a <see cref="Persistence.FinalizedPassdown.SnapshotJson"/> (Phase 10C
    /// Parts L/O). "Latest" is defined explicitly as the highest <see cref="DateOnly"/>
    /// <see cref="Persistence.FinalizedPassdownSummary.PassdownDate"/> (Phase 10C Part C) — computed
    /// here rather than trusted from the repository's own ordering, so this stays correct even if a
    /// future repository implementation changes <c>ListAsync</c>'s ordering.
    /// </summary>
    private async Task<(DashboardPassdownStatusSummary PassdownStatus, FinalizedPassdownSummary? LatestFinalized)> BuildPassdownAndHistoryAsync(
        DateOnly operatingDate, AssociateSaveStatus saveStatus, CancellationToken cancellationToken)
    {
        var summaries = await finalizedPassdownRepository.ListAsync(cancellationToken).ConfigureAwait(false);

        var latestFinalized = summaries.Count == 0
            ? null
            : summaries.Aggregate((latest, candidate) => candidate.PassdownDate > latest.PassdownDate ? candidate : latest);

        var todaysRecord = summaries.FirstOrDefault(summary => summary.PassdownDate == operatingDate);
        var passdownStatus = todaysRecord switch
        {
            null when saveStatus.State == AssociateSaveState.NeverSaved =>
                new DashboardPassdownStatusSummary(DashboardPassdownStatus.NoDraft, LastSavedAt: null, FinalizedAt: null),
            null =>
                new DashboardPassdownStatusSummary(DashboardPassdownStatus.DraftInProgress, saveStatus.LastSavedAt, FinalizedAt: null),
            _ => new DashboardPassdownStatusSummary(DashboardPassdownStatus.Finalized, saveStatus.LastSavedAt, todaysRecord.FinalizedAtUtc),
        };

        return (passdownStatus, latestFinalized);
    }

    private static DashboardNotesSummary BuildNotesSummary(IReadOnlyList<string> operationalNotes)
    {
        if (operationalNotes.Count == 0)
        {
            return DashboardNotesSummary.Empty;
        }

        return new DashboardNotesSummary(HasNotes: true, operationalNotes.Take(3).ToList(), operationalNotes.Count);
    }

    /// <summary>Product/catalog categories surfaced in the Dashboard's compact source breakdown (Phase 10D Part H/I), grouped as "Products / Offers".</summary>
    private static readonly SourceCategory[] ProductCatalogCategories =
    [
        SourceCategory.SeasonPasses,
        SourceCategory.AddOns,
        SourceCategory.Promotions,
        SourceCategory.MilitaryFirstResponder,
    ];

    /// <summary>
    /// Compact per-source breakdown plus a deterministic overall classification (Phase 10D Part C/H) —
    /// built entirely from the same per-category <see cref="AssociateVerificationState"/> values the
    /// Associate screen's own status banners already use, never a new freshness system.
    /// </summary>
    private static DashboardDataHealthSummary BuildDataHealth(AssociateWorkspace workspace)
    {
        var entries = new List<DashboardSourceStatusEntry>
        {
            // Park Hours has a full AssociateVerificationInfo, whose LastVerifiedAt is always
            // success-only (AssociateVerificationInfo.From), even while Stale — safe to surface directly.
            new(SourceCategory.ParkHours, Classify(workspace.ParkHours.Verification.State), workspace.ParkHours.Verification.LastVerifiedAt, LastCheckedAt: null),
        };

        // The three status-tracked dated categories only carry AssociateProductCategoryStatus.LastCheckedAt
        // (most recent attempt of any outcome, per its own doc comment) — never presented as "verified".
        entries.AddRange(workspace.CategoryStatuses.Select(status =>
            new DashboardSourceStatusEntry(status.Category, Classify(status.State), LastVerifiedAt: null, status.LastCheckedAt)));

        // Catalog categories have no category-level status record at all (unlike dated categories) —
        // every persisted CatalogProductRecord is always Verified with a real success-only
        // LastVerifiedAt (AssociatePassdownService.VerificationFromCatalog), so a category with at
        // least one current product is Current; a category with none is honestly NotChecked (this
        // service has no way to distinguish "never retrieved" from "retrieved and genuinely empty"
        // without a new dependency on ISourceStateRepository, which is out of scope here).
        foreach (var category in ProductCatalogCategories)
        {
            var verifiedTimestamps = workspace.Products
                .Where(p => p.Category == category)
                .Select(p => p.Verification.LastVerifiedAt)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .ToList();

            entries.Add(verifiedTimestamps.Count == 0
                ? new DashboardSourceStatusEntry(category, DashboardSourceStatus.NotChecked, null, null)
                : new DashboardSourceStatusEntry(category, DashboardSourceStatus.Current, verifiedTimestamps.Max(), null));
        }

        var currentCount = entries.Count(e => e.Status == DashboardSourceStatus.Current);
        var staleCount = entries.Count(e => e.Status == DashboardSourceStatus.Stale);
        var unavailableCount = entries.Count(e => e.Status == DashboardSourceStatus.Unavailable);
        var verificationIssueCount = entries.Count(e => e.Status == DashboardSourceStatus.VerificationIssue);
        var notPublishedCount = entries.Count(e => e.Status == DashboardSourceStatus.NotPublished);
        var notCheckedCount = entries.Count(e => e.Status == DashboardSourceStatus.NotChecked);

        // Deterministic precedence (Phase 10D Part C), worst first. NotPublished/NotChecked never
        // degrade the overall state on their own (Part E) — they only surface as NoPublishedData
        // when nothing else is currently confirmed either.
        var overallStatus = verificationIssueCount > 0
            ? DashboardOverallDataStatus.VerificationIssue
            : staleCount > 0
                ? DashboardOverallDataStatus.SomeStale
                : unavailableCount > 0
                    ? DashboardOverallDataStatus.SomeUnavailable
                    : currentCount == 0
                        ? DashboardOverallDataStatus.NoPublishedData
                        : DashboardOverallDataStatus.Current;

        var successTimestamps = entries.Where(e => e.LastVerifiedAt.HasValue).Select(e => e.LastVerifiedAt!.Value).ToList();
        DateTimeOffset? latestSuccessfulVerification = successTimestamps.Count > 0 ? successTimestamps.Max() : null;

        return new DashboardDataHealthSummary(
            overallStatus, currentCount, staleCount, unavailableCount, verificationIssueCount, notPublishedCount, notCheckedCount,
            entries, latestSuccessfulVerification);
    }

    private static DashboardSourceStatus Classify(AssociateVerificationState state) => state switch
    {
        AssociateVerificationState.NotCheckedYet => DashboardSourceStatus.NotChecked,
        AssociateVerificationState.Verified => DashboardSourceStatus.Current,
        AssociateVerificationState.Closed => DashboardSourceStatus.Current, // Part D: a verified "the park is closed" is operationally valid, not a confidence issue.
        AssociateVerificationState.NotPublished => DashboardSourceStatus.NotPublished,
        AssociateVerificationState.Unavailable => DashboardSourceStatus.Unavailable,
        AssociateVerificationState.Error => DashboardSourceStatus.VerificationIssue,
        AssociateVerificationState.Stale => DashboardSourceStatus.Stale,
        _ => DashboardSourceStatus.NotChecked,
    };
}
