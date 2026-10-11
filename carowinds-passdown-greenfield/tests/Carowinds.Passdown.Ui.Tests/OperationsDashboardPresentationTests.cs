using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="OperationsDashboardPresentation.Build"/> directly (Phase 10B Part P/K): the
/// empty-state wording it produces, the park-status badge text for every classification, and the
/// presentation-only truncation applied to Daily Feature/notes preview text. All of this must stay
/// derived from the snapshot's already-computed values — never a second business-rule computation.
/// </summary>
public class OperationsDashboardPresentationTests
{
    private static readonly DateOnly Date = FakeOperationsDashboardService.DefaultDate;

    [Theory]
    [InlineData(DashboardParkStatus.Open, "Open")]
    [InlineData(DashboardParkStatus.Closed, "Closed")]
    [InlineData(DashboardParkStatus.CustomHours, "Custom Hours")]
    [InlineData(DashboardParkStatus.PrivateBuyout, "Private Buyout")]
    [InlineData(DashboardParkStatus.NotPublished, "Not Published")]
    [InlineData(DashboardParkStatus.Unavailable, "Unavailable")]
    [InlineData(DashboardParkStatus.NotChecked, "Not checked yet")]
    public void Build_ParkStatus_ProducesExpectedBadgeText(DashboardParkStatus status, string expectedBadgeText)
    {
        var snapshot = DefaultSnapshot() with { ParkStatus = DefaultSnapshot().ParkStatus with { Status = status } };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal(expectedBadgeText, presentation.ParkStatusBadgeText);
        if (status != DashboardParkStatus.Open)
        {
            Assert.NotEqual("Open", presentation.ParkStatusBadgeText);
        }
    }

    // Phase 10B Part F/G: matches AssociateView's own "Not entered" PlaceholderText — never a
    // different empty-state vocabulary, and never "0" for a value that was never saved.
    [Fact]
    public void Build_AttendanceAndPreSoldsMissing_ShowNotEntered()
    {
        var snapshot = DefaultSnapshot() with { ProjectedAttendance = null, PreSolds = null };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Not entered", presentation.AttendanceText);
        Assert.Equal("Not entered", presentation.PreSoldsText);
    }

    [Fact]
    public void Build_AttendanceAndPreSoldsPresent_ShowFormattedValues()
    {
        var snapshot = DefaultSnapshot() with { ProjectedAttendance = 32500, PreSolds = 4210 };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("32500", presentation.AttendanceText);
        Assert.Equal("4210", presentation.PreSoldsText);
    }

    [Fact]
    public void Build_ZeroAttendance_IsDistinctFromNotEntered()
    {
        var snapshot = DefaultSnapshot() with { ProjectedAttendance = 0 };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("0", presentation.AttendanceText);
    }

    // Phase 17B: the Dashboard reads the same resolver-produced EffectiveErt as every other screen —
    // never park hours/North Gate.
    [Fact]
    public void Build_EarlyRideTimeNotConfigured_ShowsNotConfigured()
    {
        var snapshot = DefaultSnapshot() with { EarlyRideTime = EffectiveErt.NotConfigured };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Not configured", presentation.EarlyRideTimeText);
    }

    [Fact]
    public void Build_EarlyRideTimeNoErt_ShowsNone()
    {
        var snapshot = DefaultSnapshot() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.NoErt, EffectiveErtSource.Monthly, null, [], null),
        };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("None", presentation.EarlyRideTimeText);
    }

    // Independent of park hours (spec Part 31) — a known-open park with a different opening time
    // must not influence what the ERT line shows.
    [Fact]
    public void Build_EarlyRideTimeConfigured_ShowsResolvedStart_IndependentOfParkHours()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var hours = new AssociateParkHoursView(effective, new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UtcNow, null, null), false, new TimeOnly(10, 0), new TimeOnly(22, 0));
        var snapshot = DefaultSnapshot() with
        {
            ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.Open, hours),
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(8, 30), ["Fury 325"], null),
        };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Starts 8:30 AM", presentation.EarlyRideTimeText); // 9:30 AM (North Gate) would be the old, now-removed derivation.
    }

    // Machine-specific Dashboard failure: Phase 17D+ saves rides-only ERT (StartTime = null) for a
    // Configured state. The presentation must never dereference a missing start time — it shows the
    // ride list, the same rule the Associate screen's own Early Ride line uses.
    [Fact]
    public void Build_EarlyRideTimeConfiguredRidesOnly_NoStartTime_ShowsRides()
    {
        var snapshot = DefaultSnapshot() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, StartTime: null, ["Fury 325", "Copperhead Strike"], null),
        };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Fury 325, Copperhead Strike", presentation.EarlyRideTimeText);
    }

    [Fact]
    public void Build_EarlyRideTimeConfigured_NoStartTimeNoRides_ShowsConfigured()
    {
        var snapshot = DefaultSnapshot() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Override, StartTime: null, [], null),
        };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Configured", presentation.EarlyRideTimeText);
    }

    [Fact]
    public void Build_DailyFeatureMissing_ShowsNotEntered()
    {
        var snapshot = DefaultSnapshot() with { DailyFeatureType = null, DailyFeatureText = null };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Not set", presentation.DailyFeatureLabel);
        Assert.Equal("Not entered", presentation.DailyFeatureText);
    }

    [Fact]
    public void Build_DailyFeaturePresent_ShowsLabelAndContent()
    {
        var snapshot = DefaultSnapshot() with
        {
            DailyFeatureType = DailyFeatureType.QuestionOfTheDay,
            DailyFeatureText = "What is your favorite coaster?",
        };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Question of the Day", presentation.DailyFeatureLabel);
        Assert.Equal("What is your favorite coaster?", presentation.DailyFeatureText);
    }

    // Phase 10B Part K: presentation-only truncation — the underlying draft value is untouched (this
    // test only ever inspects the presentation string), and the result stays comfortably bounded.
    [Fact]
    public void Build_LongDailyFeatureText_IsTruncatedWithEllipsis()
    {
        var longText = new string('x', 500);
        var snapshot = DefaultSnapshot() with { DailyFeatureType = DailyFeatureType.QuoteOfTheDay, DailyFeatureText = longText };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.True(presentation.DailyFeatureText.Length <= 141);
        Assert.EndsWith("…", presentation.DailyFeatureText);
    }

    [Fact]
    public void Build_ShortDailyFeatureText_IsNotTruncated()
    {
        var snapshot = DefaultSnapshot() with { DailyFeatureType = DailyFeatureType.QuoteOfTheDay, DailyFeatureText = "Short quote." };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("Short quote.", presentation.DailyFeatureText);
    }

    [Fact]
    public void Build_NoOperationalNotes_ShowsEmptyStateText()
    {
        var snapshot = DefaultSnapshot() with { Notes = DashboardNotesSummary.Empty };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal("No operational notes entered", presentation.NotesSummaryText);
        Assert.Empty(presentation.NotesPreviewLines);
    }

    [Fact]
    public void Build_LongNotesPreviewLine_IsTruncatedWithEllipsis()
    {
        var longLine = new string('y', 400);
        var snapshot = DefaultSnapshot() with { Notes = new DashboardNotesSummary(true, [longLine], 1) };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        var line = Assert.Single(presentation.NotesPreviewLines);
        Assert.True(line.Length <= 101);
        Assert.EndsWith("…", line);
    }

    [Fact]
    public void Build_ShortNotesPreviewLines_AreNotTruncated()
    {
        var snapshot = DefaultSnapshot() with
        {
            Notes = new DashboardNotesSummary(true, ["South Gate opens at 9:45.", "Group arrival expected at 11:00."], 2),
        };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal(["South Gate opens at 9:45.", "Group arrival expected at 11:00."], presentation.NotesPreviewLines);
    }

    // Phase 10C Part M: no finalized history -> LatestFinalized presentation is null, so the view can
    // render the "No finalized passdowns yet." empty state.
    [Fact]
    public void Build_NoLatestFinalized_PresentationIsNull()
    {
        var snapshot = DefaultSnapshot() with { LatestFinalized = null };

        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Null(presentation.LatestFinalized);
    }

    // Phase 10C Part B/S: LatestFinalized is built by reusing PassdownHistorySummaryPresentation.Build
    // directly, so its date/finalized/origin text matches History's own list-row formatting exactly.
    [Fact]
    public void Build_LatestFinalizedPresent_MatchesHistorySummaryFormatting()
    {
        var finalizedDate = new DateOnly(2026, 8, 20);
        var finalizedAt = new DateTimeOffset(2026, 8, 21, 10, 1, 0, TimeSpan.Zero);
        var summary = new FinalizedPassdownSummary(finalizedDate, finalizedAt, FinalizationOrigin.Recovery, true, 12345);
        var snapshot = DefaultSnapshot() with { LatestFinalized = summary };

        var presentation = OperationsDashboardPresentation.Build(snapshot);
        var expected = PassdownHistorySummaryPresentation.Build(summary);

        Assert.NotNull(presentation.LatestFinalized);
        Assert.Equal(expected, presentation.LatestFinalized);
    }

    private static OperationsDashboardSnapshot DefaultSnapshot() => FakeOperationsDashboardService.DefaultSnapshot(Date);
}
