using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IOperationsDashboardService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.OperationsDashboardViewModel"/> tests — never touches SQLite.</summary>
internal sealed class FakeOperationsDashboardService : IOperationsDashboardService
{
    public List<DateOnly> LoadCalls { get; } = [];

    public int GetTodayCallCount { get; private set; }

    public Func<DateOnly, OperationsDashboardSnapshot>? SnapshotFactory { get; set; }

    public Exception? Exception { get; set; }

    public TaskCompletionSource<bool>? Gate { get; set; }

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

    public static OperationsDashboardSnapshot DefaultSnapshot(DateOnly date) => new(
        date,
        new DashboardParkStatusSummary(DashboardParkStatus.NotChecked, DefaultParkHours()),
        new DashboardPassdownStatusSummary(DashboardPassdownStatus.NoDraft, null, null),
        ProjectedAttendance: null,
        PreSolds: null,
        EarlyRideTimeEnabled: false,
        EarlyRideTimeDetails: null,
        DailyFeatureType: null,
        DailyFeatureText: null,
        Notes: DashboardNotesSummary.Empty,
        SourceHealth: new DashboardSourceHealthSummary(DashboardSourceHealth.AllCurrent, 0, 0, 0, 0),
        GeneratedAt: DateTimeOffset.UtcNow);

    private static AssociateParkHoursView DefaultParkHours() => new(
        ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
        AssociateVerificationInfo.NotCheckedYet,
        OfficialIsClosed: false,
        OfficialOpening: null,
        OfficialClosing: null);
}
