using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="OperationsCalendarOperatingStatePresentation"/> (Phase 12B Part C/D/E/F/G/H/I/J/U)
/// directly — the compact day-cell vocabulary, never recomputing hours or a freshness rule of its own.
/// </summary>
public class OperationsCalendarOperatingStatePresentationTests
{
    // Part D: an open day shows hours only — no redundant "OPEN" label.
    [Fact]
    public void Build_Open_ShowsHoursOnlyNoRedundantLabel()
    {
        var hours = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), manualEnabled: false, buyoutEnabled: false);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Open, hours);

        Assert.Null(presentation.CompactStateLabel);
        Assert.Equal("10 AM–10 PM", presentation.CompactHoursLine);
        Assert.Equal("Open, 10 AM to 10 PM", presentation.AccessibilityText);
    }

    // Part E: Closed shows "CLOSED" only — never fabricated 12 AM–12 AM hours or a blank line.
    [Fact]
    public void Build_Closed_ShowsClosedLabelWithNoHours()
    {
        var hours = ParkHours(true, null, null, manualEnabled: false, buyoutEnabled: false);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Closed, hours);

        Assert.Equal("CLOSED", presentation.CompactStateLabel);
        Assert.Null(presentation.CompactHoursLine);
        Assert.Equal("Closed", presentation.AccessibilityText);
    }

    // Part F: Custom shows both a "CUSTOM" label and the overridden hours.
    [Fact]
    public void Build_CustomHours_ShowsCustomLabelAndOverriddenHours()
    {
        var hours = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), manualEnabled: true, manualOpen: new TimeOnly(9, 0), manualClose: new TimeOnly(23, 0), buyoutEnabled: false);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.CustomHours, hours);

        Assert.Equal("CUSTOM", presentation.CompactStateLabel);
        Assert.Equal("9 AM–11 PM", presentation.CompactHoursLine);
        Assert.Equal("Custom Hours, 9 AM to 11 PM", presentation.AccessibilityText);
    }

    // Part G: Private Buyout is never presented as Closed or an error, even when the official date is closed.
    [Fact]
    public void Build_PrivateBuyout_ShowsBuyoutLabelAndHours_NeverClosedOrError()
    {
        var hours = ParkHours(true, null, null, manualEnabled: false, buyoutEnabled: true, buyoutOpen: new TimeOnly(18, 0), buyoutClose: new TimeOnly(23, 0));

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.PrivateBuyout, hours);

        Assert.Equal("BUYOUT", presentation.CompactStateLabel);
        Assert.Equal("6 PM–11 PM", presentation.CompactHoursLine);
        Assert.DoesNotContain("Closed", presentation.AccessibilityText);
        Assert.DoesNotContain("Error", presentation.AccessibilityText, StringComparison.OrdinalIgnoreCase);
    }

    // Part H: Not Published is informational, never an error, and never fabricates hours.
    [Fact]
    public void Build_NotPublished_ShowsNotPublishedWithNoHours()
    {
        var hours = NotCheckedHours(AssociateVerificationState.NotPublished);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.NotPublished, hours);

        Assert.Equal("NOT PUBLISHED", presentation.CompactStateLabel);
        Assert.Null(presentation.CompactHoursLine);
        Assert.Equal("Hours not published", presentation.AccessibilityText);
    }

    // Part I: Unavailable never surfaces raw exception/HTTP/endpoint detail.
    [Fact]
    public void Build_Unavailable_ShowsConciseTextNoTechnicalDetail()
    {
        var hours = NotCheckedHours(AssociateVerificationState.Error);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Unavailable, hours);

        Assert.Equal("UNAVAILABLE", presentation.CompactStateLabel);
        Assert.Equal("Hours unavailable", presentation.AccessibilityText);
    }

    // Never checked at all — distinct from every other state, no label fabricated.
    [Fact]
    public void Build_NotChecked_ShowsNoLabelAndGenericAccessibilityText()
    {
        var hours = NotCheckedHours(AssociateVerificationState.NotCheckedYet);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.NotChecked, hours);

        Assert.Null(presentation.CompactStateLabel);
        Assert.Null(presentation.CompactHoursLine);
        Assert.Equal("Hours not checked yet", presentation.AccessibilityText);
    }

    // Part D: on-the-hour times omit minutes ("10 AM"); non-hour times keep them ("9:30 AM").
    [Fact]
    public void Build_NonHourTimes_KeepsMinutesInCompactFormat()
    {
        var hours = ParkHours(false, new TimeOnly(9, 30), new TimeOnly(21, 15), manualEnabled: false, buyoutEnabled: false);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Open, hours);

        Assert.Equal("9:30 AM–9:15 PM", presentation.CompactHoursLine);
    }

    // ==================================================================================
    // Phase 13D Part D/E/F — waterpark hours in the same compact cell
    // ==================================================================================

    [Fact]
    public void Build_OpenWithWaterparkPublished_AddsCompactHarborLine()
    {
        var hours = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), manualEnabled: false, buyoutEnabled: false)
            with
        { WaterparkOpening = new TimeOnly(11, 0), WaterparkClosing = new TimeOnly(18, 0) };

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Open, hours);

        Assert.True(presentation.HasCompactWaterparkLine);
        Assert.Equal("Harbor 11 AM–6 PM", presentation.CompactWaterparkLine);
        Assert.Contains("Carolina Harbor 11 AM to 6 PM", presentation.AccessibilityText);
    }

    // Part E: the normal case for most of the year — no waterpark row, no placeholder, no error.
    [Fact]
    public void Build_OpenWithoutWaterparkPublished_OmitsHarborLineCleanly()
    {
        var hours = ParkHours(false, new TimeOnly(10, 0), new TimeOnly(22, 0), manualEnabled: false, buyoutEnabled: false);

        var presentation = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Open, hours);

        Assert.False(presentation.HasCompactWaterparkLine);
        Assert.Null(presentation.CompactWaterparkLine);
        Assert.DoesNotContain("Harbor", presentation.AccessibilityText);
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

    private static AssociateParkHoursView NotCheckedHours(AssociateVerificationState verificationState)
    {
        var effective = ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);
        var verification = new AssociateVerificationInfo(verificationState, null, null, null);
        return new AssociateParkHoursView(effective, verification, OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null);
    }
}
