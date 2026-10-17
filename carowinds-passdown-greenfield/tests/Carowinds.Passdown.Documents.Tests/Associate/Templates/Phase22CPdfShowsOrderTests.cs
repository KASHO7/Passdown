using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using Carowinds.Passdown.Domain;
using UglyToad.PdfPig;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 22C: Shows is the final substantive section of newly generated PDFs in Classic, Compact and
/// Modern — after the summary, Daily Feature, every pricing section and Operational Notes — proven from
/// real glyph positions (page, then vertical position). Show content and selection semantics are
/// unchanged; empty Shows still prints nothing; Teams Copy and frozen History are untouched.
/// </summary>
public sealed class Phase22CPdfShowsOrderTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static readonly ScheduledShow Wake = new("SHOW-SYN-1", "Synthetic Revue", "Synthetic Theatre", [T(19), T(20), T(21, 30)]);
    private static readonly ScheduledShow Band = new("SHOW-SYN-2", "Synthetic Drumline", null, [T(19, 15)]);
    private static readonly ScheduledShow MeetGreet = new("SHOW-SYN-3", "Synthetic Meet and Greet", "Synthetic Gathering Spot",
        [T(11, 15), T(11, 30), T(11, 45), T(12, 15), T(12, 30), T(12, 45), T(14), T(14, 15), T(14, 30), T(15), T(15, 30), T(16), T(16, 30), T(17), T(17, 30), T(17, 45)]);

    private const string NotesMarker = "Notemarkerzz";
    private const string FeatureMarker = "Featuremarkerzz";

    private static AssociateWorkspace WithShows(AssociateWorkspace workspace, params ScheduledShow[] selected)
    {
        var view = new ShowScheduleView(workspace.OperatingDate, ShowScheduleState.Scheduled, selected, false, GeneratedAt, GeneratedAt);
        var selection = new AssociateShowsSelection(true, selected.Select(s => new AssociateShowSelection(s.ShowId, s.Name, s.Venue, s.Times, GeneratedAt)).ToList());
        return workspace with { Draft = workspace.Draft with { Shows = selection }, ShowSchedule = view };
    }

    private static AssociateWorkspace Marked(AssociateWorkspace workspace, string? feature = null) => workspace with
    {
        Draft = workspace.Draft with
        {
            OperationalNotes = [.. workspace.Draft.OperationalNotes, $"{NotesMarker} final synthetic note line."],
            DailyFeatureType = DailyFeatureType.UselessFactOfTheDay,
            DailyFeatureText = feature ?? $"{FeatureMarker} synthetic feature text.",
        },
    };

    private static byte[] Render(AssociateWorkspace workspace, AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        return result.PdfBytes!;
    }

    private sealed record Word(string Text, int Page, double Top, double Left = 0)
    {
        /// <summary>Reading position: later page first, then lower on the page (PDF y grows upward).</summary>
        public (int, double) Position => (Page, -Top);
    }

    private static List<Word> Words(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages()
            .SelectMany(page => page.GetWords().Select(w => new Word(w.Text, page.Number, w.BoundingBox.Top, w.BoundingBox.Left)))
            .ToList();
    }

    /// <summary>
    /// Words regrouped into printed lines (same page, baseline within 3pt), with spaces removed — Modern's
    /// letter-spaced headings come out of PdfPig as fragments ("S", "H", "OWS"), so headings are matched
    /// on the line's compact text ("SHOWS", "OPERATIONALNOTES") rather than on single words.
    /// </summary>
    private static List<Word> Lines(byte[] pdf)
    {
        var groups = new List<List<Word>>();
        foreach (var word in Words(pdf).OrderBy(w => w.Position))
        {
            if (groups.Count > 0 && groups[^1][0].Page == word.Page && Math.Abs(groups[^1][0].Top - word.Top) <= 3)
            {
                groups[^1].Add(word);
            }
            else
            {
                groups.Add([word]);
            }
        }

        // Within a printed line, fragments read left to right.
        return groups.Select(g => g[0] with { Text = string.Concat(g.OrderBy(w => w.Left).Select(w => w.Text)) }).ToList();
    }

    private static Word Heading(List<Word> lines, params string[] compactText) =>
        Assert.Single(lines, l => compactText.Contains(l.Text, StringComparer.Ordinal));

    private static int Compare(Word a, Word b) => a.Position.CompareTo(b.Position);

    private static Word Single(List<Word> words, string text) =>
        Assert.Single(words, w => string.Equals(w.Text, text, StringComparison.Ordinal));

    private static Word First(List<Word> words, string text) =>
        words.Where(w => string.Equals(w.Text, text, StringComparison.Ordinal)).OrderBy(w => w.Position).First();

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Shows_IsTheLastSubstantiveSection_AfterDailyFeatureEveryPriceAndOperationalNotes(AssociatePassdownTemplate template)
    {
        var pdf = Render(WithShows(Marked(AssociateWorkspaceFixtures.NormalDay()), Wake, Band), template);
        var words = Words(pdf);
        var lines = Lines(pdf);
        var showsHeading = Heading(lines, "SHOWS");

        // Daily Feature, every printed price, and Operational Notes all come before the Shows heading.
        Assert.True(Compare(Single(words, FeatureMarker), showsHeading) < 0, "Daily Feature must precede Shows");
        var prices = words.Where(w => w.Text.StartsWith('$')).ToList();
        Assert.NotEmpty(prices);
        Assert.All(prices, price => Assert.True(Compare(price, showsHeading) < 0, $"price {price.Text} printed after Shows"));
        Assert.True(Compare(Single(words, NotesMarker), showsHeading) < 0, "Operational Notes must precede Shows");
        Assert.True(Compare(Heading(lines, "OPERATIONALNOTES", "NOTES"), showsHeading) < 0);

        // Nothing but show rows (and the page footer) follows the heading.
        var footerTop = words.Where(w => w.Page == showsHeading.Page).Min(w => w.Top) + 6;
        var after = lines.Where(l => Compare(l, showsHeading) > 0 && !(l.Page == showsHeading.Page && l.Top <= footerTop)).ToList();
        Assert.All(after, l => Assert.Contains("Synthetic", l.Text));

        // The show rows follow their heading (heading kept with the first row: same page).
        var firstShow = First(words, "Revue");
        Assert.True(Compare(showsHeading, firstShow) < 0);
        Assert.Equal(showsHeading.Page, firstShow.Page);
        Assert.True(Compare(showsHeading, First(words, "Drumline")) < 0);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Shows_FollowsPricing_EvenWithNoOperationalNotes(AssociatePassdownTemplate template)
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), Wake);
        workspace = workspace with { Draft = workspace.Draft with { OperationalNotes = [] } };
        var pdf = Render(workspace, template);
        var words = Words(pdf);
        var lines = Lines(pdf);
        var showsHeading = Heading(lines, "SHOWS");

        Assert.DoesNotContain(lines, l => l.Text is "NOTES" or "OPERATIONALNOTES");
        Assert.All(words.Where(w => w.Text.StartsWith('$')), price => Assert.True(Compare(price, showsHeading) < 0));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void MovingShows_ChangesNothingElse_TheContentWithoutShowsIsIdentical(AssociatePassdownTemplate template)
    {
        var baseline = Marked(AssociateWorkspaceFixtures.NormalDay());
        var withShowsPdf = Render(WithShows(baseline, Wake, Band), template);
        var withShows = Words(withShowsPdf);
        var without = Words(Render(baseline, template));
        var showsHeading = Heading(Lines(withShowsPdf), "SHOWS");

        // Everything printed before the Shows heading is exactly the no-Shows document's content
        // (minus its footer, which is outside the substantive order), in the same reading order.
        var before = withShows.Where(w => Compare(w, showsHeading) < 0).Select(w => w.Text).ToList();
        Assert.Equal(without.Select(w => w.Text).Take(before.Count), before);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void EmptyOrDisabledShows_PrintsNoHeading_AndReservesNoSpace(AssociatePassdownTemplate template)
    {
        var baseline = Marked(AssociateWorkspaceFixtures.NormalDay());
        var disabled = WithShows(baseline, Wake);
        disabled = disabled with { Draft = disabled.Draft with { Shows = disabled.Draft.Shows! with { IncludeInPassdown = false } } };
        var none = WithShows(baseline);

        var baselineWords = Words(Render(baseline, template));
        foreach (var workspace in new[] { disabled, none })
        {
            var pdf = Render(workspace, template);
            var words = Words(pdf);
            Assert.DoesNotContain(Lines(pdf), l => l.Text == "SHOWS");
            Assert.Equal(baselineWords.Select(w => (w.Text, w.Page, Math.Round(w.Top, 2))), words.Select(w => (w.Text, w.Page, Math.Round(w.Top, 2))));
        }
    }

    // A busy passdown: every pricing category, notes, a 16-performance show plus two more, and a Daily
    // Feature with mathematics. It may legitimately take two pages; Shows must still be last, its heading
    // must stay with its first row, and every performance time must print.
    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void BusyPassdown_WithMathAndLongShowList_KeepsShowsLast_Unclipped_AndAtMostTwoPages(AssociatePassdownTemplate template)
    {
        var workspace = WithShows(
            Marked(AssociateWorkspaceFixtures.DenseDay(), $@"{FeatureMarker} the drop takes \(t = \sqrt{{\frac{{2h}}{{g}}}}\) seconds."),
            MeetGreet, Wake, Band);
        var pdf = Render(workspace, template);
        var words = Words(pdf);
        var lines = Lines(pdf);
        var text = PdfInspector.ExtractAllText(pdf);
        var showsHeading = Heading(lines, "SHOWS");

        Assert.InRange(PdfInspector.CountPages(pdf), 1, 2);
        Assert.All(words.Where(w => w.Text.StartsWith('$')), price => Assert.True(Compare(price, showsHeading) < 0));
        Assert.True(Compare(Single(words, NotesMarker), showsHeading) < 0);
        // A math Daily Feature paragraph is drawn as glyph outlines (no extractable words), so its
        // heading line marks its position.
        Assert.True(Compare(Heading(lines, "USELESSFACTOFTHEDAY"), showsHeading) < 0);
        Assert.Equal(showsHeading.Page, First(words, "Meet").Page);
        Assert.DoesNotContain(@"\(", text); // math still rendered, not raw source

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
        foreach (var show in model.Shows)
        {
            PdfTextAssert.ContainsIgnoringWhitespace(show.Name, text);
        }

        PdfTextAssert.ContainsIgnoringWhitespace("5:45", text); // the long list's last performance printed
    }

    [Fact]
    public void ShowContent_AndSelectionSemantics_AreUnchangedByTheMove()
    {
        var workspace = WithShows(AssociateWorkspaceFixtures.NormalDay(), Wake, Band);

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Equal(["Synthetic Revue", "Synthetic Drumline"], model.Shows.Select(s => s.Name));
        Assert.Equal("Synthetic Theatre", model.Shows[0].Venue);
        Assert.Equal("7:00 PM, 8:00 PM, 9:30 PM", model.Shows[0].TimesText);
        Assert.Equal("7:00, 8:00, 9:30 PM", model.Shows[0].CompactTimesText);
        Assert.Null(model.Shows[1].Venue);
    }

    [Fact]
    public void TeamsCopy_IsIdenticalWithOrWithoutShows_ForAllThreeOptions()
    {
        var without = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);
        var with = AssociatePassdownDocumentModelBuilder.Build(WithShows(AssociateWorkspaceFixtures.NormalDay(), Wake, Band), GeneratedAt);

        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            var copied = AssociateTeamsTextFormatter.Format(with, kind);
            Assert.Equal(AssociateTeamsTextFormatter.Format(without, kind), copied);
            Assert.DoesNotContain("Synthetic Revue", copied ?? string.Empty);
        }
    }

    // A newly finalized passdown freezes the new order; existing History PDFs are stored bytes and are
    // never regenerated (FinalizedPassdownPdfRetrievalService returns the stored blob as-is).
    [Fact]
    public void NewlyFinalizedProjection_FreezesTheNewOrder()
    {
        var projection = new FinalizedPassdownSnapshotBuilder().BuildProjection(WithShows(Marked(AssociateWorkspaceFixtures.NormalDay()), Wake), GeneratedAt);
        var words = Words(projection.PdfBytes);

        Assert.True(Compare(Single(words, NotesMarker), Heading(Lines(projection.PdfBytes), "SHOWS")) < 0);
        Assert.NotNull(projection.Snapshot.Shows);
        Assert.Equal(["Synthetic Revue"], projection.Snapshot.Shows.Select(s => s.Name));
    }

    [Fact]
    public void PdfGeneration_HasNoNetworkDependency()
    {
        var referenced = typeof(AssociatePassdownPdfGenerator).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(referenced, name => name.Contains("Http", StringComparison.OrdinalIgnoreCase) || name.Contains("Playwright", StringComparison.OrdinalIgnoreCase));
    }
}
