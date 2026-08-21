using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Deterministic, fully scriptable <see cref="IAssociatePassdownService"/> for
/// <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel"/> tests. Never touches
/// SQLite, HTTP, or Playwright — records every call so a test can assert whether the ViewModel
/// ever invoked the explicit online check (spec §6.3's "date change never triggers retrieval").
/// </summary>
internal sealed class FakeAssociatePassdownService : IAssociatePassdownService
{
    public List<DateOnly> LoadCalls { get; } = [];

    public List<DateOnly> CheckOnlineCalls { get; } = [];

    public Func<DateOnly, AssociateWorkspace> WorkspaceFactory { get; set; } = DefaultWorkspace;

    public Func<DateOnly, AssociateOnlineCheckResult>? CheckOnlineResultFactory { get; set; }

    public IReadOnlyList<RefreshProgress> ProgressToReport { get; set; } = [];

    /// <summary>When set, <see cref="LoadAssociateDateAsync"/> awaits this before returning — lets a test hold a load "in flight" to assert busy-state behavior.</summary>
    public TaskCompletionSource<bool>? LoadGate { get; set; }

    /// <summary>When set, <see cref="CheckAssociateDateOnlineAsync"/> awaits this before returning.</summary>
    public TaskCompletionSource<bool>? CheckOnlineGate { get; set; }

    public async Task<AssociateWorkspace> LoadAssociateDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        LoadCalls.Add(operatingDate);
        if (LoadGate is not null)
        {
            await LoadGate.Task;
        }

        return WorkspaceFactory(operatingDate);
    }

    public async Task<AssociateOnlineCheckResult> CheckAssociateDateOnlineAsync(DateOnly operatingDate, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CheckOnlineCalls.Add(operatingDate);

        foreach (var update in ProgressToReport)
        {
            progress?.Report(update);
        }

        if (CheckOnlineGate is not null)
        {
            await CheckOnlineGate.Task;
        }

        return CheckOnlineResultFactory?.Invoke(operatingDate) ?? new AssociateOnlineCheckResult(WorkspaceFactory(operatingDate), SuccessSummary(operatingDate));
    }

    public Task<AssociateSaveStatus> SaveDraftAsync(DateOnly operatingDate, AssociateDraftPayload draft, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AssociateSaveStatus(AssociateSaveState.Saved, DateTimeOffset.UtcNow));

    public static AssociateWorkspace DefaultWorkspace(DateOnly date) => new(
        date,
        AssociateDraftPayload.CreateDefault(),
        new AssociateParkHoursView(NotCheckedHours, AssociateVerificationInfo.NotCheckedYet),
        Products: [],
        CategoryStatuses: [],
        AssociateSaveStatus.NeverSaved);

    public static EffectiveHours NotCheckedHours => ParkHoursCalculator.Calculate(
        officialIsClosed: false, null, null,
        manualOverrideEnabled: false, null, null,
        privateBuyoutEnabled: false, null, null);

    public static RefreshSummary SuccessSummary(DateOnly date) => new(
        RefreshJobType.ManualSingleDate, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        OperationsAttempted: 1, Succeeded: 1, Failed: 0, Cancelled: 0,
        Results: [SourceOperationResult.Success("fake-source", SourceCategory.ParkHours, date, RetrievalStatus.Verified, "Verified.")]);

    public static RefreshSummary PartialFailureSummary(DateOnly date) => new(
        RefreshJobType.ManualSingleDate, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        OperationsAttempted: 2, Succeeded: 1, Failed: 1, Cancelled: 0,
        Results:
        [
            SourceOperationResult.Success("fake-park-hours", SourceCategory.ParkHours, date, RetrievalStatus.Verified, "Verified."),
            SourceOperationResult.Failure("fake-fast-lane", SourceCategory.DatedFastLane, date, RetrievalStatus.Error, "Could not be checked."),
        ]);
}
