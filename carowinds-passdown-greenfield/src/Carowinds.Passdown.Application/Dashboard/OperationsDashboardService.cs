using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>
/// <see cref="IOperationsDashboardService"/> built entirely as a projection over existing
/// Application-layer state (Phase 10A Part C/J):
/// <see cref="IAssociatePassdownService.LoadAssociateDateAsync"/> for the composed hours/draft/
/// verification state (already local-only by its own contract) and
/// <see cref="IFinalizedPassdownRepository"/> for finalization metadata only — never
/// <c>GetByDateAsync</c>, so a dashboard load can never materialize a historical PDF blob (Phase
/// 10A Part S). No HTTP/Playwright/Task Scheduler dependency exists on this type by construction.
/// </summary>
public sealed class OperationsDashboardService(
    IAssociatePassdownService associatePassdownService,
    IFinalizedPassdownRepository finalizedPassdownRepository,
    IClock clock) : IOperationsDashboardService
{
    public Task<OperationsDashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(TodayEastern(), cancellationToken);

    public async Task<OperationsDashboardSnapshot> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var workspace = await associatePassdownService.LoadAssociateDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);

        var parkStatus = BuildParkStatus(workspace.ParkHours);
        var passdownStatus = await BuildPassdownStatusAsync(operatingDate, workspace.SaveStatus, cancellationToken).ConfigureAwait(false);

        // Mirrors AssociateViewModel.OnIsEarlyRideTimeEnabledChanged's own "off means not
        // applicable" rule (Desktop/ViewModels/AssociateViewModel.cs) so the dashboard's summary
        // never disagrees with what the Associate Overview screen itself would show.
        var earlyRideTimeEnabled = !string.IsNullOrWhiteSpace(workspace.Draft.EarlyRideTime.Details)
            || workspace.Draft.EarlyRideTime.ParticipatingAttractions.Count > 0;

        var notes = BuildNotesSummary(workspace.Draft.OperationalNotes);
        var sourceHealth = BuildSourceHealth(workspace);

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
            sourceHealth,
            clock.UtcNow);
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

    private async Task<DashboardPassdownStatusSummary> BuildPassdownStatusAsync(DateOnly operatingDate, AssociateSaveStatus saveStatus, CancellationToken cancellationToken)
    {
        if (!await finalizedPassdownRepository.ExistsForDateAsync(operatingDate, cancellationToken).ConfigureAwait(false))
        {
            return saveStatus.State == AssociateSaveState.NeverSaved
                ? new DashboardPassdownStatusSummary(DashboardPassdownStatus.NoDraft, LastSavedAt: null, FinalizedAt: null)
                : new DashboardPassdownStatusSummary(DashboardPassdownStatus.DraftInProgress, saveStatus.LastSavedAt, FinalizedAt: null);
        }

        // Metadata-only (IFinalizedPassdownRepository.ListAsync never selects pdf_bytes) — even the
        // rare case of a past finalized date never loads the frozen PDF blob here.
        var summaries = await finalizedPassdownRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var finalizedAt = summaries.FirstOrDefault(summary => summary.PassdownDate == operatingDate)?.FinalizedAtUtc;
        return new DashboardPassdownStatusSummary(DashboardPassdownStatus.Finalized, saveStatus.LastSavedAt, finalizedAt);
    }

    private static DashboardNotesSummary BuildNotesSummary(IReadOnlyList<string> operationalNotes)
    {
        if (operationalNotes.Count == 0)
        {
            return DashboardNotesSummary.Empty;
        }

        return new DashboardNotesSummary(HasNotes: true, operationalNotes.Take(3).ToList(), operationalNotes.Count);
    }

    private static DashboardSourceHealthSummary BuildSourceHealth(AssociateWorkspace workspace)
    {
        var states = workspace.CategoryStatuses
            .Select(status => status.State)
            .Append(workspace.ParkHours.Verification.State)
            .ToList();

        var currentCount = states.Count(state => state is AssociateVerificationState.Verified or AssociateVerificationState.Closed);
        var staleCount = states.Count(state => state == AssociateVerificationState.Stale);
        var unavailableCount = states.Count(state => state is AssociateVerificationState.Unavailable or AssociateVerificationState.Error or AssociateVerificationState.NotPublished);
        var notCheckedCount = states.Count(state => state == AssociateVerificationState.NotCheckedYet);

        var health = unavailableCount > 0
            ? DashboardSourceHealth.SomeUnavailable
            : staleCount > 0
                ? DashboardSourceHealth.SomeStale
                : DashboardSourceHealth.AllCurrent;

        return new DashboardSourceHealthSummary(health, currentCount, staleCount, unavailableCount, notCheckedCount);
    }
}
