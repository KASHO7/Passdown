using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Composes a <see cref="DiagnosticReport"/> from already-registered abstractions only (Phase 7F
/// Part D/I) — this type does no I/O itself. Every value comes from persisted scheduling state
/// (<see cref="ISourceStateRepository"/>, already written by the refresh orchestrator), local
/// file/OS metadata (<see cref="IDatabaseDiagnosticsProvider"/>/<see cref="IScheduledTaskStatusProvider"/>),
/// or the sanitized scheduled-refresh log (<see cref="IScheduledRefreshLogProvider"/>). Building a
/// report never triggers a network refresh (Part I "local-only").
/// </summary>
public sealed class DiagnosticReportBuilder(
    IReadOnlyList<SourceRegistration> sourceRegistrations,
    ISourceStateRepository sourceStateRepository,
    IDatabaseDiagnosticsProvider databaseDiagnosticsProvider,
    IScheduledTaskStatusProvider scheduledTaskStatusProvider,
    IScheduledRefreshLogProvider logProvider,
    IClock clock,
    IInteractiveAppLogProvider? appLogProvider = null,
    BuildIdentity? buildIdentity = null,
    ICatalogProductRepository? catalogProductRepository = null,
    ISeasonPriceHistoryRepository? seasonPriceHistoryRepository = null,
    HistoricalBackfill.IHistoricalBackfillService? historicalBackfill = null) : IDiagnosticReportBuilder
{
    private const int MaxLogLines = 200;

    public async Task<DiagnosticReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var sources = new List<SourceStatusSummary>(sourceRegistrations.Count);
        var states = new List<(SourceRegistration, SourceStateRecord?)>(sourceRegistrations.Count);
        foreach (var registration in sourceRegistrations)
        {
            var state = await sourceStateRepository.GetAsync(registration.SourceId, cancellationToken).ConfigureAwait(false);
            sources.Add(BuildSourceSummary(registration, state));
            states.Add((registration, state));
        }

        var database = await databaseDiagnosticsProvider.GetAsync(cancellationToken).ConfigureAwait(false);
        var scheduledTask = await scheduledTaskStatusProvider.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var logLines = await logProvider.GetRecentLogLinesAsync(MaxLogLines, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> appLogLines = appLogProvider is null
            ? []
            : await appLogProvider.GetRecentLogLinesAsync(MaxLogLines, cancellationToken).ConfigureAwait(false);

        // Phase 18D: the informational version (with its +<commit> suffix), not the numeric assembly
        // version every build shares — so a support file identifies exactly which copy produced it.
        var identity = buildIdentity ?? BuildIdentity.Current();

        return new DiagnosticReport(identity.FullVersion, clock.UtcNow, sources, database, scheduledTask, logLines, appLogLines)
        {
            ExecutablePath = identity.ExecutablePath,
            OfficialData = OfficialDataStatus.Compute(states),
            CatalogLifecycle = await BuildCatalogLifecycleAsync(cancellationToken).ConfigureAwait(false),
            SeasonPriceHistory = await BuildSeasonPriceHistoryAsync(cancellationToken).ConfigureAwait(false),
            HistoricalBackfill = historicalBackfill?.CurrentStatus,
        };
    }

    /// <summary>Phase 20A: counts only; an unreadable history table never fails the export.</summary>
    private async Task<SeasonPriceHistorySummary?> BuildSeasonPriceHistoryAsync(CancellationToken cancellationToken)
    {
        if (seasonPriceHistoryRepository is null)
        {
            return null;
        }

        try
        {
            return await seasonPriceHistoryRepository.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<CatalogLifecycleDiagnostics?> BuildCatalogLifecycleAsync(CancellationToken cancellationToken)
    {
        if (catalogProductRepository is null)
        {
            return null;
        }

        var records = new List<CatalogProductRecord>();
        foreach (var category in sourceRegistrations.Where(r => r.Scope == SourceScope.Catalog).Select(r => r.Category).Distinct())
        {
            records.AddRange(await catalogProductRepository.GetByCategoryAsync(category, lifecycleState: null, cancellationToken).ConfigureAwait(false));
        }

        var active = records.Where(r => r.LifecycleState == ProductLifecycleState.Active).ToList();
        var activeTags = active.Select(r => ProductSeasonClassifier.Classify(r.Category, r.DisplayName)).ToList();
        return new CatalogLifecycleDiagnostics(
            Active: active.Count,
            PendingConfirmation: records.Count(r => r.LifecycleState == ProductLifecycleState.PendingConfirmation),
            Inactive: records.Count(r => r.LifecycleState is ProductLifecycleState.Archived or ProductLifecycleState.Expired),
            ActiveCarowinds: activeTags.Count(t => t.HasFlag(ProductSeasonTags.Carowinds)),
            ActiveSCarowinds: activeTags.Count(t => t.HasFlag(ProductSeasonTags.SCarowinds)),
            ActiveWinterFest: activeTags.Count(t => t.HasFlag(ProductSeasonTags.WinterFest)),
            ActiveUnclassified: activeTags.Count(t => t == ProductSeasonTags.None));
    }

    private static SourceStatusSummary BuildSourceSummary(SourceRegistration registration, SourceStateRecord? state)
    {
        if (state is null || state.LastAttemptAt is null)
        {
            return new SourceStatusSummary(registration.SourceId, DisplayName(registration.SourceId), registration.Category, DiagnosticSourceStatus.NeverChecked, null, null, 0);
        }

        var status = state.ConsecutiveFailureCount > 0 ? DiagnosticSourceStatus.Error : DiagnosticSourceStatus.Ok;
        return new SourceStatusSummary(registration.SourceId, DisplayName(registration.SourceId), registration.Category, status, state.LastAttemptAt, state.LastSuccessAt, state.ConsecutiveFailureCount);
    }

    /// <summary>
    /// A human-readable label per registered source id (spec Phase 7F Part I's "Park Hours" /
    /// "Dated Admission" example format). Keyed on the source id rather than
    /// <see cref="SourceCategory"/> because two distinct sources share the <see cref="SourceCategory.AddOns"/>
    /// category (season-pass add-ons and general add-ons) and would otherwise collide under one
    /// label. Falls back to the raw source id for any future source this mapping has not been
    /// updated for, rather than throwing.
    /// </summary>
    private static string DisplayName(string sourceId) => sourceId switch
    {
        "park-hours.official" => "Park Hours",
        "dated-admission.official" => "Dated Admission",
        "dated-fast-lane.official" => "Dated Fast Lane",
        "dated-haunted.official" => "Haunted Attractions",
        "promotions.official" => "Promotions",
        "season-passes.official" => "Season Passes",
        "season-pass-add-ons.official" => "Season Pass Add-Ons",
        "general-add-ons.official" => "General Add-Ons",
        _ => sourceId,
    };
}
