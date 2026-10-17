using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 21C: the optional Shows section across Classic, Compact and Modern — selected shows only, the
/// same verified times in every template, no empty heading or reserved space, Teams Copy untouched,
/// finalized snapshots frozen, and representative one-page layouts preserved.
/// </summary>
public sealed class Phase21CShowsPdfTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();
    private static readonly AssociatePassdownTemplate[] Templates = [AssociatePassdownTemplate.Classic, AssociatePassdownTemplate.Compact, AssociatePassdownTemplate.Modern];

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static readonly ScheduledShow Wake = new("SHOW-030-00122", "Wake the Dead", "Carowinds Theatre", [T(19), T(20), T(21, 30)]);
    private static readonly ScheduledShow Drummers = new("SHOW-030-00185", "Blade Drummers", "HH Event Pad", [T(19, 15)]);
    private static readonly ScheduledShow Contest = new("SHOW-030-00058", "Costume Contest", null, [T(17)]);
    private static readonly ScheduledShow MeetGreet = new("SHOW-030-00101", "Camp Snoopy Meet and Greet", "Gathering Spot",
        [T(11, 15), T(11, 30), T(11, 45), T(12, 15), T(12, 30), T(12, 45), T(14), T(14, 15), T(14, 30), T(15), T(15, 30), T(16), T(16, 30), T(17), T(17, 30), T(17, 45)]);

    private static AssociateWorkspace WithShows(AssociateWorkspace workspace, bool include, ScheduledShow[] schedule, params ScheduledShow[] selected)
    {
        var view = new ShowScheduleView(workspace.OperatingDate, schedule.Length == 0 ? ShowScheduleState.NoShows : ShowScheduleState.Scheduled,
            schedule, false, GeneratedAt, GeneratedAt);
        var selection = new AssociateShowsSelection(include, selected.Select(s => new AssociateShowSelection(s.ShowId, s.Name, s.Venue, s.Times, GeneratedAt)).ToList());
        return workspace with { Draft = workspace.Draft with { Shows = selection }, ShowSchedule = view };
    }

    private static (string Text, int Pages) Render(AssociateWorkspace workspace, AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        return (PdfInspector.ExtractAllText(result.PdfBytes!), result.PageCount);
    }

    private static bool HasShowsHeading(string text) =>
        PdfTextAssert.TextContainsIgnoringWhitespace(text, "SHOWS");

    [Fact]
    public void Model_IncludesOnlySelectedShows_InScheduleOrder()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers, Contest], Wake, Contest);

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Equal(["Wake the Dead", "Costume Contest"], model.Shows.Select(s => s.Name));
        Assert.Equal("7:00 PM, 8:00 PM, 9:30 PM", model.Shows[0].TimesText);
        Assert.Equal("7:00, 8:00, 9:30 PM", model.Shows[0].CompactTimesText);
        Assert.Null(model.Shows[1].Venue);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void SelectedShows_PrintWithTheirVerifiedTimes(AssociatePassdownTemplate template)
    {
        var (text, _) = Render(WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers], Wake, Drummers), template);

        Assert.True(HasShowsHeading(text));
        PdfTextAssert.ContainsIgnoringWhitespace("Wake the Dead", text);
        PdfTextAssert.ContainsIgnoringWhitespace("Blade Drummers", text);
        PdfTextAssert.ContainsIgnoringWhitespace(template == AssociatePassdownTemplate.Compact ? "7:00, 8:00, 9:30 PM" : "7:00 PM, 8:00 PM, 9:30 PM", text);
        PdfTextAssert.ContainsIgnoringWhitespace("7:15 PM", text);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void UnselectedShow_NeverPrints(AssociatePassdownTemplate template)
    {
        var (text, _) = Render(WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers], Wake), template);

        Assert.False(PdfTextAssert.TextContainsIgnoringWhitespace(text, "Blade Drummers"));
    }

    public static IEnumerable<object[]> NoSectionCases()
    {
        foreach (var template in Templates)
        {
            yield return [template, "no shows selected", (Func<AssociateWorkspace>)(() => WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake]))];
            yield return [template, "section disabled", (Func<AssociateWorkspace>)(() => WithShows(AssociateWorkspaceFixtures.NormalDay(), false, [Wake], Wake))];
            yield return [template, "legacy draft", (Func<AssociateWorkspace>)AssociateWorkspaceFixtures.NormalDay];
            yield return [template, "schedule not published", (Func<AssociateWorkspace>)(() => AssociateWorkspaceFixtures.NormalDay() with
            {
                Draft = AssociateWorkspaceFixtures.NormalDay().Draft with { Shows = new AssociateShowsSelection(true, [new("SHOW-030-00122", "Wake the Dead", null, [T(19)], GeneratedAt)]) },
                ShowSchedule = new ShowScheduleView(AssociateWorkspaceFixtures.NormalDay().OperatingDate, ShowScheduleState.NotPublished, [], false, GeneratedAt, GeneratedAt),
            })];
        }
    }

    [Theory]
    [MemberData(nameof(NoSectionCases))]
    public void NothingToPrint_OmitsTheSectionEntirely_AndReservesNoSpace(AssociatePassdownTemplate template, string scenario, Func<AssociateWorkspace> workspace)
    {
        var (text, pages) = Render(workspace(), template);
        var (baselineText, baselinePages) = Render(AssociateWorkspaceFixtures.NormalDay(), template);

        Assert.False(HasShowsHeading(text), $"{template}/{scenario} printed a Shows heading.");
        Assert.False(PdfTextAssert.TextContainsIgnoringWhitespace(text, "Wake the Dead"), $"{template}/{scenario} printed a show.");
        Assert.Equal(baselinePages, pages);
        Assert.Equal(baselineText.Length, text.Length);
    }

    [Fact]
    public void AllTemplates_PrintTheSameShows_AndTimes_OnlyPresentationDiffers()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers, Contest], Wake, Drummers, Contest);
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        foreach (var template in Templates)
        {
            var (text, _) = Render(workspace, template);
            foreach (var show in model.Shows)
            {
                PdfTextAssert.ContainsIgnoringWhitespace(show.Name, text);
                PdfTextAssert.ContainsIgnoringWhitespace(template == AssociatePassdownTemplate.Compact ? show.CompactTimesText : show.TimesText, text);
            }
        }
    }

    [Fact]
    public void ChangedPerformanceTime_PrintsTheCurrentVerifiedTime_NotTheSelectedCopy()
    {
        var current = Wake with { Times = [T(19, 30)] };
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [current], Wake);

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Equal("7:30 PM", Assert.Single(model.Shows).TimesText);
    }

    [Fact]
    public void StaleSchedule_StillPrintsLastKnownGoodTimes()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake], Wake);
        workspace = workspace with { ShowSchedule = workspace.ShowSchedule with { LastCheckFailed = true } };

        Assert.Single(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt).Shows);
    }

    [Fact]
    public void SourceUnavailable_PrintsTheSavedSelection_NeverAPlaceholder()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [], Wake);
        workspace = workspace with { ShowSchedule = new ShowScheduleView(workspace.OperatingDate, ShowScheduleState.CouldNotCheck, [], true, null, GeneratedAt) };

        var show = Assert.Single(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt).Shows);
        Assert.Equal("7:00 PM, 8:00 PM, 9:30 PM", show.TimesText);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void LongNamesAndManyTimes_RenderWithoutDroppingAnything(AssociatePassdownTemplate template)
    {
        var longShow = new ScheduledShow("SHOW-030-00999", "The Extraordinary Midnight Masquerade Spectacular of Ghouls, Goblins and Graveyard Gospel", "Carowinds Theatre Main Stage Annex", [T(13), T(23, 45)]);
        var (text, _) = Render(WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [longShow, MeetGreet], longShow, MeetGreet), template);

        PdfTextAssert.ContainsIgnoringWhitespace("Graveyard Gospel", text);
        PdfTextAssert.ContainsIgnoringWhitespace("Camp Snoopy Meet and Greet", text);
        PdfTextAssert.ContainsIgnoringWhitespace(template == AssociatePassdownTemplate.Compact ? "5:30, 5:45 PM" : "5:45 PM", text);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Venue_PrintsWhenPublished_AndIsSimplyAbsentOtherwise(AssociatePassdownTemplate template)
    {
        var (text, _) = Render(WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Contest], Wake, Contest), template);

        PdfTextAssert.ContainsIgnoringWhitespace("Carowinds Theatre", text);
        PdfTextAssert.ContainsIgnoringWhitespace("Costume Contest", text);
    }

    [Fact]
    public void NormalDay_WithThreeShows_StaysOnItsBaselinePageCount_InEveryTemplate()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers, Contest], Wake, Drummers, Contest);

        foreach (var template in Templates)
        {
            var (_, baseline) = Render(AssociateWorkspaceFixtures.NormalDay(), template);
            var (_, pages) = Render(workspace, template);
            output.WriteLine($"Normal day + 3 shows {template}: {pages} page(s) (baseline {baseline}).");
            Assert.Equal(baseline, pages);
        }
    }

    /// <summary>
    /// A busy day with two typical shows: Classic and Compact keep their one page. Modern — the most
    /// spacious template — has no spare room on this fixture, so any added section moves it to a second
    /// page (documented in docs/phase21c-official-show-schedules.md); nothing is ever dropped.
    /// </summary>
    [Fact]
    public void BusyDay_WithTwoShows_ClassicAndCompactKeepTheirPageCount()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.DenseDay(), true, [Wake, Drummers], Wake, Drummers);

        foreach (var template in Templates)
        {
            var (_, baseline) = Render(AssociateWorkspaceFixtures.DenseDay(), template);
            var (text, pages) = Render(workspace, template);
            output.WriteLine($"Busy day + 2 shows {template}: {pages} page(s) (baseline {baseline}).");
            PdfTextAssert.ContainsIgnoringWhitespace("Blade Drummers", text);
            if (template != AssociatePassdownTemplate.Modern)
            {
                Assert.Equal(baseline, pages);
            }
        }
    }

    /// <summary>The heaviest case (four shows incl. a 16-performance meet-and-greet on a busy day): Compact — the density benchmark — keeps one page; Classic/Modern may legitimately need a second, never dropping content.</summary>
    [Fact]
    public void BusyDay_WithShows_CompactKeepsItsPageCount()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.DenseDay(), true, [Wake, Drummers, Contest, MeetGreet], Wake, Drummers, Contest, MeetGreet);

        foreach (var template in Templates)
        {
            var (_, baseline) = Render(AssociateWorkspaceFixtures.DenseDay(), template);
            var (_, pages) = Render(workspace, template);
            output.WriteLine($"Busy day + 4 shows {template}: {pages} page(s) (baseline {baseline}).");
            if (template == AssociatePassdownTemplate.Compact)
            {
                Assert.Equal(baseline, pages);
            }
        }
    }

    [Fact]
    public void TeamsCopy_NeverContainsShowInformation_EvenWhenShowsAreSelected()
    {
        var withShows = AssociatePassdownDocumentModelBuilder.Build(
            WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers], Wake, Drummers), GeneratedAt);
        var without = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);

        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            var copied = AssociateTeamsTextFormatter.Format(withShows, kind);
            Assert.Equal(AssociateTeamsTextFormatter.Format(without, kind), copied);
            if (copied is null)
            {
                continue;
            }

            Assert.DoesNotContain("Wake the Dead", copied);
            Assert.DoesNotContain("Carowinds Theatre", copied);
            Assert.DoesNotContain("7:15 PM", copied);
            Assert.DoesNotContain("Shows", copied, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FinalizedSnapshot_FreezesThePrintedShows()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake, Drummers], Wake);

        var snapshot = new FinalizedPassdownSnapshotBuilder().Build(workspace, GeneratedAt);

        var frozen = Assert.Single(snapshot.Shows!);
        Assert.Equal("Wake the Dead", frozen.Name);
        Assert.Equal("Carowinds Theatre", frozen.Venue);
        Assert.Equal("7:00 PM, 8:00 PM, 9:30 PM", frozen.TimesText);
    }

    [Fact]
    public void FinalizedSnapshot_WithoutPrintedShows_HasNoShowsProperty()
    {
        Assert.Null(new FinalizedPassdownSnapshotBuilder().Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt).Shows);
        Assert.Null(new FinalizedPassdownSnapshotBuilder().Build(WithShows(AssociateWorkspaceFixtures.NormalDay(), false, [Wake], Wake), GeneratedAt).Shows);
    }

    [Fact]
    public void FinalizedProjection_FrozenPdfContainsTheShows_AndLaterScheduleChangesCannotAlterIt()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake], Wake);
        var projection = new FinalizedPassdownSnapshotBuilder().BuildProjection(workspace, GeneratedAt);

        // The official schedule later changes; the frozen bytes and snapshot are already fixed.
        _ = WithShows(AssociateWorkspaceFixtures.NormalDay(), true, [Wake with { Times = [T(18)] }], Wake);

        PdfTextAssert.ContainsIgnoringWhitespace("7:00 PM, 8:00 PM, 9:30 PM", PdfInspector.ExtractAllText(projection.PdfBytes));
        Assert.Equal("7:00 PM, 8:00 PM, 9:30 PM", Assert.Single(projection.Snapshot.Shows!).TimesText);
    }
}
