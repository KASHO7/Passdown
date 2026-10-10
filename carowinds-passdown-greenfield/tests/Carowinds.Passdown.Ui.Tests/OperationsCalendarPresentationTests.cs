using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="OperationsCalendarPresentation"/>'s selected-date "Park Status"/"Effective
/// Hours" text (Phase 12B Part K) — proves it reuses <see cref="ParkHoursPresentation.Build"/>
/// directly rather than a second hours formatter, for each of the four detail states the spec
/// calls out explicitly (normal, custom, buyout, closed).
/// </summary>
public class OperationsCalendarPresentationTests
{
    private static readonly DateOnly SelectedDate = new(2026, 8, 29);
    private static readonly DateOnly DisplayedMonth = new(2026, 8, 1);

    // Part Z #11: selected-date detail for a normal open day.
    [Fact]
    public void Build_SelectedDateOpen_ShowsParkStatusOpenAndEffectiveHours()
    {
        var snapshot = BuildSnapshot(OperationsCalendarOperatingState.Open, ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, buyoutEnabled: false));

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("Open", presentation.ParkStatusText);
        Assert.Equal("10:00 AM – 10:00 PM", presentation.EffectiveHoursText);
    }

    // Part Z #12: selected-date detail for a custom-hours day.
    [Fact]
    public void Build_SelectedDateCustomHours_ShowsParkStatusCustomAndOverriddenHours()
    {
        var snapshot = BuildSnapshot(
            OperationsCalendarOperatingState.CustomHours,
            ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), true, new TimeOnly(9, 0), new TimeOnly(23, 0), buyoutEnabled: false));

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("Custom Hours", presentation.ParkStatusText);
        Assert.Equal("9:00 AM – 11:00 PM", presentation.EffectiveHoursText);
    }

    // Part Z #13: selected-date detail for a private-buyout day.
    [Fact]
    public void Build_SelectedDateBuyout_ShowsParkStatusBuyoutAndBuyoutHours()
    {
        var snapshot = BuildSnapshot(
            OperationsCalendarOperatingState.PrivateBuyout,
            ParkHours(true, null, null, false, buyoutEnabled: true, buyoutOpen: new TimeOnly(18, 0), buyoutClose: new TimeOnly(23, 0)));

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("Private Buyout", presentation.ParkStatusText);
        Assert.Equal("6:00 PM – 11:00 PM", presentation.EffectiveHoursText);
    }

    // Part Z #14: selected-date detail for a closed day — no fabricated hours text.
    [Fact]
    public void Build_SelectedDateClosed_ShowsParkStatusClosedAndNoHoursText()
    {
        var snapshot = BuildSnapshot(OperationsCalendarOperatingState.Closed, ParkHours(true, null, null, false, buyoutEnabled: false));

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("Closed", presentation.ParkStatusText);
        Assert.Null(presentation.EffectiveHoursText);
    }

    // Part J/T: selected-date No Draft — the empty-state note and the "Active Operational Notes" wording.
    [Fact]
    public void Build_SelectedDateNoDraft_ShowsNoDraftLabelAndEmptyStateNotesSentence()
    {
        var snapshot = BuildSnapshot(
            OperationsCalendarOperatingState.NotChecked,
            ParkHours(false, null, null, false, buyoutEnabled: false),
            status: OperationsCalendarPassdownStatus.NoDraft, relevantNotes: 0, importantNotes: 0);

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("No Draft", presentation.PassdownStatusText);
        Assert.Equal("No passdown started.", presentation.PassdownDetailNoteText);
        Assert.Equal("No active operational notes for this date.", presentation.OperationalNotesCountText);
        Assert.Null(presentation.OperationalNotesImportantText);
    }

    // Part J: selected-date Draft In Progress.
    [Fact]
    public void Build_SelectedDateDraftInProgress_ShowsDraftInProgressLabel()
    {
        var snapshot = BuildSnapshot(
            OperationsCalendarOperatingState.NotChecked,
            ParkHours(false, null, null, false, buyoutEnabled: false),
            status: OperationsCalendarPassdownStatus.DraftInProgress, relevantNotes: 0, importantNotes: 0);

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("Draft In Progress", presentation.PassdownStatusText);
    }

    // Part J: selected-date Finalized.
    [Fact]
    public void Build_SelectedDateFinalized_ShowsFinalizedLabel()
    {
        var snapshot = BuildSnapshot(
            OperationsCalendarOperatingState.NotChecked,
            ParkHours(false, null, null, false, buyoutEnabled: false),
            status: OperationsCalendarPassdownStatus.Finalized, relevantNotes: 0, importantNotes: 0);

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("Finalized", presentation.PassdownStatusText);
    }

    // Part J/F: selected-date notes count with an Important sub-line.
    [Fact]
    public void Build_SelectedDateHasRelevantAndImportantNotes_ShowsBothCountLines()
    {
        var snapshot = BuildSnapshot(
            OperationsCalendarOperatingState.NotChecked,
            ParkHours(false, null, null, false, buyoutEnabled: false),
            status: OperationsCalendarPassdownStatus.NoDraft, relevantNotes: 3, importantNotes: 1);

        var presentation = OperationsCalendarPresentation.Build(snapshot);

        Assert.Equal("3 active", presentation.OperationalNotesCountText);
        Assert.Equal("1 Important", presentation.OperationalNotesImportantText);
    }

    private static OperationsCalendarSnapshot BuildSnapshot(
        OperationsCalendarOperatingState state, AssociateParkHoursView parkHours,
        OperationsCalendarPassdownStatus status = OperationsCalendarPassdownStatus.NoDraft,
        int relevantNotes = 0, int importantNotes = 0)
    {
        var selected = new OperationsCalendarSelectedDateSummary(
            SelectedDate, IsToday: false, status,
            LastSavedAt: null, FinalizedAt: null, RelevantOperationalNoteCount: relevantNotes,
            state, parkHours, ImportantOperationalNoteCount: importantNotes);

        return new OperationsCalendarSnapshot(DisplayedMonth, new DateOnly(2026, 8, 21), SelectedDate, [], selected, DateTimeOffset.UtcNow);
    }

    private static AssociateParkHoursView ParkHours(
        bool officialClosed, TimeOnly? officialOpen, TimeOnly? officialClose,
        bool manualEnabled, TimeOnly? manualOpen = null, TimeOnly? manualClose = null,
        bool buyoutEnabled = false, TimeOnly? buyoutOpen = null, TimeOnly? buyoutClose = null)
    {
        var effective = ParkHoursCalculator.Calculate(
            officialClosed, officialOpen, officialClose,
            manualEnabled, manualOpen, manualClose,
            buyoutEnabled, buyoutOpen, buyoutClose);

        return new AssociateParkHoursView(effective, AssociateVerificationInfo.NotCheckedYet, officialClosed, officialOpen, officialClose);
    }
}
