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

    /// <summary>
    /// Phase 18A BUG-001: the park's prices are USD regardless of the PC's region format. Pre-18A this
    /// used the machine culture's "C" format, so an English (UK) or German Windows region showed
    /// "£54.99" / "54,99 €" for a USD price.
    /// </summary>
    [Theory]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Build_DailyAdmissionPrice_IsAlwaysUsd_RegardlessOfMachineCulture(string cultureName)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            var open = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, buyoutEnabled: false);

            var exact = OperationsCalendarPresentation.Build(WithAdmission(BuildSnapshot(OperationsCalendarOperatingState.Open, open), Domain.Catalog.PriceKind.ExactDated));
            var floor = OperationsCalendarPresentation.Build(WithAdmission(BuildSnapshot(OperationsCalendarOperatingState.Open, open), Domain.Catalog.PriceKind.StartingFrom));

            Assert.Equal("$54.99", exact.DailyAdmissionPriceText);
            Assert.Equal("Starting from $54.99", floor.DailyAdmissionPriceText);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- Phase 37E D-1 (owner-approved 2026-11-28): a previously verified price is qualified, not shown as on sale ----

    /// <summary>3:10 PM EST on Friday 2026-11-27.</summary>
    private static readonly DateTimeOffset VerifiedAfternoon = new(2026, 11, 27, 20, 10, 0, TimeSpan.Zero);

    private static OperationsCalendarPresentation BuildWithAdmission(
        AssociateVerificationState? state,
        DateTimeOffset? lastVerifiedAt = null,
        Domain.Catalog.PriceKind kind = Domain.Catalog.PriceKind.ExactDated)
    {
        var open = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, buyoutEnabled: false);
        var snapshot = BuildSnapshot(OperationsCalendarOperatingState.Open, open);
        var verification = state is { } s ? new AssociateVerificationInfo(s, lastVerifiedAt, null, null) : null;
        return OperationsCalendarPresentation.Build(snapshot with
        {
            Selected = snapshot.Selected with
            {
                DailyAdmission = new OperationsCalendarDailyAdmissionPrice(
                    OperationsCalendarDailyAdmissionStatus.Available, "Park Ticket", 49.00m, kind, null, verification),
            },
        });
    }

    [Fact]
    public void DailyTicket_PreviouslyVerified_SaysLastVerifiedWithTheOriginalEasternDate()
    {
        var presentation = BuildWithAdmission(AssociateVerificationState.PreviouslyVerified, VerifiedAfternoon);

        Assert.Equal("$49.00 · last verified Nov 27", presentation.DailyAdmissionPriceText);
        Assert.Equal("Daily ticket price: $49.00, last verified November 27, availability not confirmed", presentation.DailyAdmissionPriceAccessibleText);
    }

    [Fact]
    public void DailyTicket_PreviouslyVerifiedLateEvening_UsesTheEasternDate_NotUtc()
    {
        // 10:45 PM EST on Nov 27 is already Nov 28 in UTC.
        var presentation = BuildWithAdmission(AssociateVerificationState.PreviouslyVerified, new DateTimeOffset(2026, 11, 28, 3, 45, 0, TimeSpan.Zero));

        Assert.Equal("$49.00 · last verified Nov 27", presentation.DailyAdmissionPriceText);
    }

    [Fact]
    public void DailyTicket_PreviouslyVerifiedStartingFrom_KeepsTheFloorQualifier()
    {
        var presentation = BuildWithAdmission(AssociateVerificationState.PreviouslyVerified, VerifiedAfternoon, Domain.Catalog.PriceKind.StartingFrom);

        Assert.Equal("Starting from $49.00 · last verified Nov 27", presentation.DailyAdmissionPriceText);
        Assert.Equal("Daily ticket price: Starting from $49.00, last verified November 27, availability not confirmed", presentation.DailyAdmissionPriceAccessibleText);
    }

    [Fact]
    public void DailyTicket_PreviouslyVerifiedWithoutATime_SaysPreviouslyVerified()
    {
        var presentation = BuildWithAdmission(AssociateVerificationState.PreviouslyVerified);

        Assert.Equal("$49.00 · previously verified", presentation.DailyAdmissionPriceText);
        Assert.Equal("Daily ticket price: $49.00, previously verified, availability not confirmed", presentation.DailyAdmissionPriceAccessibleText);
    }

    [Theory]
    [InlineData(AssociateVerificationState.Verified)]
    [InlineData(AssociateVerificationState.Stale)]
    [InlineData(AssociateVerificationState.NotCheckedYet)]
    public void DailyTicket_OtherVerificationStates_AreUnchanged(AssociateVerificationState state)
    {
        var presentation = BuildWithAdmission(state, VerifiedAfternoon);

        Assert.Equal("$49.00", presentation.DailyAdmissionPriceText);
        Assert.Equal("Daily ticket price: $49.00", presentation.DailyAdmissionPriceAccessibleText);
    }

    [Fact]
    public void DailyTicket_WithoutVerification_IsUnchanged()
    {
        var presentation = BuildWithAdmission(state: null);

        Assert.Equal("$49.00", presentation.DailyAdmissionPriceText);
        Assert.Equal("Daily ticket price: $49.00", presentation.DailyAdmissionPriceAccessibleText);
    }

    [Fact]
    public void DailyTicket_NoPrice_AccessibleTextMatchesTheExistingFormat()
    {
        var open = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, buyoutEnabled: false);
        var snapshot = BuildSnapshot(OperationsCalendarOperatingState.Open, open);
        var notPublished = OperationsCalendarPresentation.Build(snapshot with
        {
            Selected = snapshot.Selected with
            {
                DailyAdmission = new OperationsCalendarDailyAdmissionPrice(OperationsCalendarDailyAdmissionStatus.NotPublished, null, null, Domain.Catalog.PriceKind.Unknown, null),
            },
        });

        Assert.Equal("Not Published", notPublished.DailyAdmissionPriceText);
        Assert.Equal("Daily ticket price: Not Published", notPublished.DailyAdmissionPriceAccessibleText);
        Assert.Equal("Daily ticket price: Not checked yet", OperationsCalendarPresentation.Build(snapshot).DailyAdmissionPriceAccessibleText);
    }

    [Fact]
    public void DailyTicketLine_BindsItsAccessibleNameToTheQualifiedText()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        var markup = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Carowinds.Passdown.Desktop", "Views", "OperationsCalendarView.axaml"));
        Assert.Contains(
            "Text=\"{Binding Presentation.DailyAdmissionPriceText}\" Classes=\"detailValue\"\n                                 AutomationProperties.Name=\"{Binding Presentation.DailyAdmissionPriceAccessibleText}\"",
            markup.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("StringFormat='Daily ticket price: {0}'", markup, StringComparison.Ordinal);
    }

    private static OperationsCalendarSnapshot WithAdmission(OperationsCalendarSnapshot snapshot, Domain.Catalog.PriceKind kind) =>
        snapshot with
        {
            Selected = snapshot.Selected with
            {
                DailyAdmission = new OperationsCalendarDailyAdmissionPrice(OperationsCalendarDailyAdmissionStatus.Available, "Single Day Ticket", 54.99m, kind, null),
            },
        };

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
