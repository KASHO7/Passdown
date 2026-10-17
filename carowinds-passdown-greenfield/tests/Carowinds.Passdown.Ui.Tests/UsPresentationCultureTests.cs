using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 18B product decision: every user-facing date, weekday/month name, time, and price renders in
/// en-US no matter which Windows region the PC uses. Each test renders under several non-US cultures
/// (including th-TH, whose default calendar is Buddhist) and compares against fixed en-US text.
/// </summary>
public class UsPresentationCultureTests
{
    // InlineData, not MemberData: a MemberData theory in this assembly changes xunit discovery enough to
    // trigger the documented Avalonia headless "different thread owns it" failure in the isolated
    // real-Window partitions (OperationsCalendarViewTests etc.) deterministically.
    private static T Under<T>(string cultureName, Func<T> render)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            return render();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    private static AssociateParkHoursView Hours(TimeOnly open, TimeOnly close)
    {
        var effective = ParkHoursCalculator.Calculate(false, open, close, false, null, null, false, null, null);
        return new AssociateParkHoursView(effective, AssociateVerificationInfo.NotCheckedYet, false, open, close);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void Calendar_MonthTitle_SelectedDate_Hours_AndPrice_AreEnUs(string culture)
    {
        var selected = new OperationsCalendarSelectedDateSummary(
            new DateOnly(2026, 9, 19), IsToday: false, OperationsCalendarPassdownStatus.Finalized,
            LastSavedAt: null, FinalizedAt: new DateTimeOffset(2026, 9, 20, 10, 5, 0, TimeSpan.Zero), RelevantOperationalNoteCount: 0,
            OperationsCalendarOperatingState.Open, Hours(new TimeOnly(10, 30), new TimeOnly(18, 0)), ImportantOperationalNoteCount: 0)
        {
            DailyAdmission = new OperationsCalendarDailyAdmissionPrice(OperationsCalendarDailyAdmissionStatus.Available, "Single Day Ticket", 55m, Domain.Catalog.PriceKind.ExactDated, null),
        };
        var snapshot = new OperationsCalendarSnapshot(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), selected.Date, [], selected, DateTimeOffset.UtcNow);

        var presentation = Under(culture, () => OperationsCalendarPresentation.Build(snapshot));

        Assert.Equal("October 2026", presentation.DisplayedMonthText);
        Assert.Equal("Saturday, September 19, 2026", presentation.SelectedDateText);
        Assert.Equal("$55.00", presentation.DailyAdmissionPriceText);
        Assert.Contains("10:30 AM", presentation.EffectiveHoursText);
        Assert.Contains("6:00 PM", presentation.EffectiveHoursText);
        Assert.Equal("Finalized Sep 20, 2026 at 6:05 AM ET", presentation.PassdownDetailNoteText); // Eastern (EDT), unchanged semantics
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void Calendar_CompactDayCellHours_UseAmPm(string culture)
    {
        var presentation = Under(culture, () => OperationsCalendarOperatingStatePresentation.Build(
            OperationsCalendarOperatingState.Open, Hours(new TimeOnly(10, 0), new TimeOnly(22, 30))));

        Assert.Equal("10 AM–10:30 PM", presentation.CompactHoursLine);
        Assert.Equal("Open, 10 AM to 10:30 PM", presentation.AccessibilityText);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void Dashboard_OperatingDay_History_AndCounts_AreEnUs(string culture)
    {
        var date = FakeOperationsDashboardService.DefaultDate;
        var summary = new FinalizedPassdownSummary(new DateOnly(2026, 9, 19), new DateTimeOffset(2026, 9, 20, 19, 25, 0, TimeSpan.Zero), FinalizationOrigin.Manual, true, 12345);
        var snapshot = FakeOperationsDashboardService.DefaultSnapshot(date) with { ProjectedAttendance = 32500, PreSolds = 4210, LatestFinalized = summary };

        var presentation = Under(culture, () => OperationsDashboardPresentation.Build(snapshot));
        var expectedDay = date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates);

        Assert.Equal(expectedDay, presentation.OperatingDayLabel);
        Assert.Matches(@"^(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday), (January|February|March|April|May|June|July|August|September|October|November|December) \d{1,2}, 20\d\d$", presentation.OperatingDayLabel);
        Assert.Equal("32,500", presentation.AttendanceText); // Phase 22D: en-US grouping on every host culture
        Assert.Equal("4,210", presentation.PreSoldsText);
        Assert.Equal("September 19, 2026", presentation.LatestFinalized!.PassdownDateText);
        Assert.Equal("Finalized Sep 20, 2026 at 3:25 PM ET", presentation.LatestFinalized.FinalizedText);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void History_Row_IsEnUs(string culture)
    {
        var summary = new FinalizedPassdownSummary(new DateOnly(2026, 9, 19), new DateTimeOffset(2026, 9, 19, 14, 30, 0, TimeSpan.Zero), FinalizationOrigin.Recovery, true, 1);

        var presentation = Under(culture, () => PassdownHistorySummaryPresentation.Build(summary));

        Assert.Equal("September 19, 2026", presentation.PassdownDateText);
        Assert.Equal("Finalized Sep 19, 2026 at 10:30 AM ET", presentation.FinalizedText);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void LocalWallClockTimes_UseAmPm_AndEnglishMonthNames(string culture)
    {
        var instant = new DateTimeOffset(2026, 9, 15, 19, 25, 0, TimeSpan.Zero);
        // Phase 22F: operational timestamps are the Eastern wall clock with an "ET" label (was the host's local zone).
        var eastern = EasternTimeZone.ConvertFromUtc(instant);
        var expectedTime = eastern.ToString("h:mm tt", PresentationCulture.UnitedStates) + " ET";
        var expectedStamp = eastern.ToString("MMM d 'at' h:mm tt", PresentationCulture.UnitedStates) + " ET";

        var save = Under(culture, () => SaveStatusPresentation.Build(new AssociateSaveStatus(AssociateSaveState.Saved, instant)).Text);
        var lastSynced = Under(culture, () => PersonalSyncAutomationStatusPresentation.BuildLastSyncedText(instant));
        var deepRefresh = Under(culture, () => DeepRefreshStatusPresentation.Build(new DeepRefreshStatusView(
            new DeepRefreshStatusRecord(instant, instant, DeepRefreshOutcome.Success, new DateOnly(2026, 10, 31), 10, 0), NeedsDeepRefresh: false)));

        Assert.Contains(expectedTime, save);
        Assert.Matches(@"\d{1,2}:\d\d (AM|PM)", save);
        Assert.Equal($"Last synced: {expectedStamp}", lastSynced);
        Assert.Contains("Park hours available through Oct 31", deepRefresh.CoverageText);
        Assert.Equal($"✓ {expectedStamp}", deepRefresh.HeadlineText);
        Assert.Matches(@"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) ", expectedStamp);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void Prices_AreAlwaysUsdWithDollarSign(string culture)
    {
        var (exact, whole, big) = Under(culture, () => (PriceFormatting.Format(54.99m), PriceFormatting.Format(55m), PriceFormatting.Format(1234.5m)));

        Assert.Equal("$54.99", exact);
        Assert.Equal("$55.00", whole);
        Assert.Equal("$1234.50", big); // existing UI style: no grouping separator
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void PriceParsing_StaysInvariant_AndUnaffectedByCulture(string culture)
    {
        var (ok, value, error) = Under(culture, () =>
        {
            var parsed = PriceFormatting.TryParse("$24.99", out var v, out var e);
            return (parsed, v, e);
        });

        Assert.True(ok);
        Assert.Equal(24.99m, value);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    public void OperationalNoteDates_AreEnUs(string culture)
    {
        var reference = new DateOnly(2026, 9, 1);

        var (sameYear, otherYear) = Under(culture, () => (
            OperationalNoteDateRangeFormatter.FormatDate(new DateOnly(2026, 9, 19), reference),
            OperationalNoteDateRangeFormatter.FormatDate(new DateOnly(2027, 1, 2), reference)));

        Assert.Equal("Sep 19", sameYear);
        Assert.Equal("Jan 2, 2027", otherYear);
    }

    [Fact]
    public void PresentationCulture_IsTheBuiltInEnUs_WithoutUserOverrides()
    {
        var culture = PresentationCulture.UnitedStates;

        Assert.Equal("en-US", culture.Name);
        Assert.True(culture.IsReadOnly);
        Assert.False(culture.UseUserOverride);
        Assert.Equal("AM", culture.DateTimeFormat.AMDesignator);
        Assert.Equal("PM", culture.DateTimeFormat.PMDesignator);
        Assert.IsType<GregorianCalendar>(culture.Calendar);
    }
}
