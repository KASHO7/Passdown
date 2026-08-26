using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="DashboardDataHealthPresentation.Build"/> directly (Phase 10D Part R/S/T): the
/// overall headline/detail vocabulary, the count-chip list, the "Last verified"/"Last checked"
/// timestamp fallback rule, and that no raw exception/URL/HTTP-status text is ever exposed. All of
/// this must stay derived from the already-computed <see cref="DashboardDataHealthSummary"/> —
/// never a second business-rule computation.
/// </summary>
public class DashboardDataHealthPresentationTests
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

    private static DashboardDataHealthSummary AllCurrentSummary(DateTimeOffset verifiedAt) => new(
        DashboardOverallDataStatus.Current,
        CurrentCount: 7, StaleCount: 0, UnavailableCount: 0, VerificationIssueCount: 0, NotPublishedCount: 0, NotCheckedCount: 0,
        Sources: AllCategories.Select(c => new DashboardSourceStatusEntry(c, DashboardSourceStatus.Current, verifiedAt, null)).ToList(),
        LatestSuccessfulVerification: verifiedAt);

    [Theory]
    [InlineData(DashboardOverallDataStatus.Current, "Current")]
    [InlineData(DashboardOverallDataStatus.SomeStale, "Some information is stale")]
    [InlineData(DashboardOverallDataStatus.SomeUnavailable, "Some data unavailable")]
    [InlineData(DashboardOverallDataStatus.VerificationIssue, "Verification issue")]
    [InlineData(DashboardOverallDataStatus.NoPublishedData, "No published data yet")]
    public void Build_OverallStatus_ProducesExpectedHeadline(DashboardOverallDataStatus status, string expectedHeadline)
    {
        var summary = AllCurrentSummary(DateTimeOffset.UtcNow) with { OverallStatus = status };

        var presentation = DashboardDataHealthPresentation.Build(summary);

        Assert.Equal(expectedHeadline, presentation.OverallHeadline);
    }

    // Phase 10D Part T: plain, nontechnical vocabulary only — never the raw enum name. "Current" is
    // the one legitimate coincidence (the enum member and the display word are the same English
    // word), so it is excluded here rather than treated as a leak.
    [Theory]
    [InlineData(DashboardOverallDataStatus.SomeStale)]
    [InlineData(DashboardOverallDataStatus.SomeUnavailable)]
    [InlineData(DashboardOverallDataStatus.VerificationIssue)]
    [InlineData(DashboardOverallDataStatus.NoPublishedData)]
    public void Build_OverallHeadline_NeverExposesRawEnumName(DashboardOverallDataStatus status)
    {
        var summary = AllCurrentSummary(DateTimeOffset.UtcNow) with { OverallStatus = status };
        var presentation = DashboardDataHealthPresentation.Build(summary);

        Assert.DoesNotContain(status.ToString(), presentation.OverallHeadline, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AllCurrent_CountsShowOnlyCurrentChip()
    {
        var summary = AllCurrentSummary(DateTimeOffset.UtcNow);

        var presentation = DashboardDataHealthPresentation.Build(summary);

        var chip = Assert.Single(presentation.Counts);
        Assert.Equal("Current", chip.Label);
        Assert.Equal("7", chip.CountText);
    }

    // Phase 10D Part K: zero-count buckets are omitted from the compact chip row entirely.
    [Fact]
    public void Build_MixedCounts_OnlyNonZeroBucketsAppear()
    {
        var entries = AllCategories.Select((c, i) => new DashboardSourceStatusEntry(
            c, i == 0 ? DashboardSourceStatus.Stale : DashboardSourceStatus.Current, DateTimeOffset.UtcNow, null)).ToList();
        var summary = new DashboardDataHealthSummary(
            DashboardOverallDataStatus.SomeStale, CurrentCount: 6, StaleCount: 1, UnavailableCount: 0,
            VerificationIssueCount: 0, NotPublishedCount: 0, NotCheckedCount: 0, entries, DateTimeOffset.UtcNow);

        var presentation = DashboardDataHealthPresentation.Build(summary);

        Assert.Equal(2, presentation.Counts.Count);
        Assert.Contains(presentation.Counts, c => c.Label == "Current" && c.CountText == "6");
        Assert.Contains(presentation.Counts, c => c.Label == "Stale" && c.CountText == "1");
        Assert.DoesNotContain(presentation.Counts, c => c.Label is "Unavailable" or "Not published" or "Issues" or "Not checked");
    }

    [Fact]
    public void Build_NoLatestSuccessfulVerification_OmitsLastVerifiedText()
    {
        var summary = AllCurrentSummary(DateTimeOffset.UtcNow) with { LatestSuccessfulVerification = null };

        var presentation = DashboardDataHealthPresentation.Build(summary);

        Assert.Null(presentation.LastVerifiedText);
    }

    [Fact]
    public void Build_HasLatestSuccessfulVerification_ShowsLastVerifiedText()
    {
        var summary = AllCurrentSummary(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));

        var presentation = DashboardDataHealthPresentation.Build(summary);

        Assert.NotNull(presentation.LastVerifiedText);
        Assert.StartsWith("Last verified", presentation.LastVerifiedText);
    }

    // Phase 10D Part J: a source with only an attempt-based timestamp is labeled "Last checked",
    // never "Last verified" — the two are never conflated.
    [Fact]
    public void Build_SourceWithOnlyLastCheckedAt_UsesLastCheckedLabel()
    {
        var entry = new DashboardSourceStatusEntry(SourceCategory.DatedFastLane, DashboardSourceStatus.VerificationIssue, LastVerifiedAt: null, LastCheckedAt: DateTimeOffset.UtcNow);
        var summary = WithSingleEntry(entry);

        var presentation = DashboardDataHealthPresentation.Build(summary);

        var row = presentation.OperationalSources.Single(r => r.CategoryLabel == "Fast Lane");
        Assert.NotNull(row.TimestampText);
        Assert.StartsWith("Last checked", row.TimestampText);
    }

    [Fact]
    public void Build_SourceWithNoTimestamp_OmitsTimestampText()
    {
        var entry = new DashboardSourceStatusEntry(SourceCategory.DatedFastLane, DashboardSourceStatus.NotChecked, null, null);
        var summary = WithSingleEntry(entry);

        var presentation = DashboardDataHealthPresentation.Build(summary);

        var row = presentation.OperationalSources.Single(r => r.CategoryLabel == "Fast Lane");
        Assert.Null(row.TimestampText);
    }

    // Phase 10D Part I: sources split into the same two groups the Data Status card renders.
    [Fact]
    public void Build_SplitsSourcesIntoOperationalAndProductGroups()
    {
        var summary = AllCurrentSummary(DateTimeOffset.UtcNow);

        var presentation = DashboardDataHealthPresentation.Build(summary);

        Assert.Equal(4, presentation.OperationalSources.Count);
        Assert.Equal(3, presentation.ProductSources.Count);
        Assert.Contains(presentation.OperationalSources, r => r.CategoryLabel == "Park Hours");
        Assert.Contains(presentation.ProductSources, r => r.CategoryLabel == "Season Passes");
    }

    // Phase 10D Part T: status text is plain language, never a raw exception/URL/HTTP status —
    // this presentation type has no field capable of carrying one in the first place.
    [Fact]
    public void Build_StatusText_NeverContainsTechnicalDetail()
    {
        foreach (var status in Enum.GetValues<DashboardSourceStatus>())
        {
            var entry = new DashboardSourceStatusEntry(SourceCategory.ParkHours, status, null, null);
            var summary = WithSingleEntry(entry);

            var presentation = DashboardDataHealthPresentation.Build(summary);

            var row = presentation.OperationalSources.Single(r => r.CategoryLabel == "Park Hours");
            Assert.DoesNotContain("http", row.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", row.StatusText, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static DashboardDataHealthSummary WithSingleEntry(DashboardSourceStatusEntry replacement)
    {
        var entries = AllCategories
            .Select(c => c == replacement.Category ? replacement : new DashboardSourceStatusEntry(c, DashboardSourceStatus.Current, DateTimeOffset.UtcNow, null))
            .ToList();

        return new DashboardDataHealthSummary(
            DashboardOverallDataStatus.Current, CurrentCount: 6, StaleCount: 0, UnavailableCount: 0,
            VerificationIssueCount: 0, NotPublishedCount: 0, NotCheckedCount: 0, entries, DateTimeOffset.UtcNow);
    }
}
