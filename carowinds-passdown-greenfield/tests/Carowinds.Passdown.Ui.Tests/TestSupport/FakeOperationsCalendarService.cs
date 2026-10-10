using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IOperationsCalendarService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.OperationsCalendarViewModel"/> tests — never touches SQLite.</summary>
internal sealed class FakeOperationsCalendarService : IOperationsCalendarService
{
    public List<(DateOnly DisplayedMonth, DateOnly SelectedDate)> LoadCalls { get; } = [];

    public Func<DateOnly, DateOnly, OperationsCalendarSnapshot>? SnapshotFactory { get; set; }

    public Exception? Exception { get; set; }

    public TaskCompletionSource<bool>? Gate { get; set; }

    public async Task<OperationsCalendarSnapshot> LoadAsync(DateOnly displayedMonth, DateOnly selectedDate, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateOnly(displayedMonth.Year, displayedMonth.Month, 1);
        LoadCalls.Add((monthStart, selectedDate));

        if (Gate is not null)
        {
            await Gate.Task;
        }

        if (Exception is not null)
        {
            throw Exception;
        }

        return SnapshotFactory?.Invoke(monthStart, selectedDate) ?? DefaultSnapshot(monthStart, selectedDate);
    }

    public static OperationsCalendarSnapshot DefaultSnapshot(DateOnly displayedMonth, DateOnly selectedDate)
    {
        var monthStart = new DateOnly(displayedMonth.Year, displayedMonth.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var gridStart = monthStart.AddDays(-(int)monthStart.DayOfWeek);
        var gridEnd = monthEnd.AddDays(6 - (int)monthEnd.DayOfWeek);

        var days = new List<OperationsCalendarDay>();
        for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
        {
            days.Add(new OperationsCalendarDay(
                date,
                IsInDisplayedMonth: date.Month == monthStart.Month && date.Year == monthStart.Year,
                IsToday: date == selectedDate,
                HasFinalizedPassdown: false,
                HasAssociateDraft: false,
                RelevantOperationalNoteCount: 0,
                OperatingState: OperationsCalendarOperatingState.NotChecked,
                ParkHours: NotCheckedParkHours));
        }

        var selected = new OperationsCalendarSelectedDateSummary(
            selectedDate,
            IsToday: true,
            OperationsCalendarPassdownStatus.NoDraft,
            LastSavedAt: null,
            FinalizedAt: null,
            RelevantOperationalNoteCount: 0,
            OperatingState: OperationsCalendarOperatingState.NotChecked,
            ParkHours: NotCheckedParkHours);

        return new OperationsCalendarSnapshot(monthStart, selectedDate, selectedDate, days, selected, DateTimeOffset.UtcNow);
    }

    public static AssociateParkHoursView NotCheckedParkHours => new(
        ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
        AssociateVerificationInfo.NotCheckedYet,
        OfficialIsClosed: false,
        OfficialOpening: null,
        OfficialClosing: null);
}
