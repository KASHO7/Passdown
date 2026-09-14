using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IOperationsDashboardService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.OperationsDashboardViewModel"/> tests — never touches SQLite.</summary>
internal sealed class FakeOperationsDashboardService : IOperationsDashboardService
{
    private static readonly SourceCategory[] AllCategories =
    [
        SourceCategory.ParkHours,
        SourceCategory.DatedAdmissionTickets,
        SourceCategory.DatedFastLane,
        SourceCategory.HauntedAttractions,
        SourceCategory.SeasonPasses,
        SourceCategory.AddOns,
        SourceCategory.Promotions,
    ];

    public List<DateOnly> LoadCalls { get; } = [];

    public int GetTodayCallCount { get; private set; }

    public int CheckOnlineCallCount { get; private set; }

    public Func<DateOnly, OperationsDashboardSnapshot>? SnapshotFactory { get; set; }

    public Func<OperationsDashboardCheckOnlineResult>? CheckOnlineResultFactory { get; set; }

    public Exception? Exception { get; set; }

    public Exception? CheckOnlineException { get; set; }

    public TaskCompletionSource<bool>? Gate { get; set; }

    public TaskCompletionSource<bool>? CheckOnlineGate { get; set; }

    public static readonly DateOnly DefaultDate = new(2026, 8, 26);

    public async Task<OperationsDashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        GetTodayCallCount++;
        return await LoadAsync(DefaultDate, cancellationToken);
    }

    public async Task<OperationsDashboardSnapshot> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        LoadCalls.Add(operatingDate);

        if (Gate is not null)
        {
            await Gate.Task;
        }

        if (Exception is not null)
        {
            throw Exception;
        }

        return SnapshotFactory?.Invoke(operatingDate) ?? DefaultSnapshot(operatingDate);
    }

    public async Task<OperationsDashboardCheckOnlineResult> CheckOnlineAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CheckOnlineCallCount++;

        if (CheckOnlineGate is not null)
        {
            await CheckOnlineGate.Task;
        }

        if (CheckOnlineException is not null)
        {
            throw CheckOnlineException;
        }

        return CheckOnlineResultFactory?.Invoke() ?? new OperationsDashboardCheckOnlineResult(
            DefaultSnapshot(DefaultDate),
            new RefreshSummary(RefreshJobType.UserForced, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, OperationsAttempted: 0, Succeeded: 0, Failed: 0, Cancelled: 0, Results: []));
    }

    public int DeepRefreshCallCount { get; private set; }

    public Func<DeepRefreshActionResult>? DeepRefreshResultFactory { get; set; }

    public DeepRefreshStatusView? DeepRefreshStatusOverride { get; set; }

    public Exception? DeepRefreshException { get; set; }

    public TaskCompletionSource<bool>? DeepRefreshGate { get; set; }

    public Task<DeepRefreshStatusView> GetDeepRefreshStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DeepRefreshStatusOverride ?? new DeepRefreshStatusView(Status: null, NeedsDeepRefresh: false));

    public async Task<DeepRefreshActionResult> RunDeepRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        DeepRefreshCallCount++;

        if (DeepRefreshGate is not null)
        {
            await DeepRefreshGate.Task;
        }

        if (DeepRefreshException is not null)
        {
            throw DeepRefreshException;
        }

        return DeepRefreshResultFactory?.Invoke() ?? new DeepRefreshActionResult(
            DefaultSnapshot(DefaultDate),
            new RefreshSummary(RefreshJobType.DeepRefresh, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, OperationsAttempted: 0, Succeeded: 0, Failed: 0, Cancelled: 0, Results: []),
            new DeepRefreshStatusView(Status: null, NeedsDeepRefresh: false));
    }

    public static OperationsDashboardSnapshot DefaultSnapshot(DateOnly date) => new(
        date,
        new DashboardParkStatusSummary(DashboardParkStatus.NotChecked, DefaultParkHours()),
        new DashboardPassdownStatusSummary(DashboardPassdownStatus.NoDraft, null, null),
        ProjectedAttendance: null,
        PreSolds: null,
        EarlyRideTime: EffectiveErt.NotConfigured,
        DailyFeatureType: null,
        DailyFeatureText: null,
        Notes: DashboardNotesSummary.Empty,
        ActiveOperationalNotes: DashboardOperationalNotesSummary.Empty,
        DataHealth: DefaultDataHealth(),
        LatestFinalized: null,
        GeneratedAt: DateTimeOffset.UtcNow);

    private static DashboardDataHealthSummary DefaultDataHealth() => new(
        DashboardOverallDataStatus.NoPublishedData,
        CurrentCount: 0, StaleCount: 0, UnavailableCount: 0, VerificationIssueCount: 0, NotPublishedCount: 0, NotCheckedCount: AllCategories.Length,
        Sources: AllCategories.Select(c => new DashboardSourceStatusEntry(c, DashboardSourceStatus.NotChecked, null, null)).ToList(),
        LatestSuccessfulVerification: null);

    private static AssociateParkHoursView DefaultParkHours() => new(
        ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
        AssociateVerificationInfo.NotCheckedYet,
        OfficialIsClosed: false,
        OfficialOpening: null,
        OfficialClosing: null);
}
