using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="DashboardOperationalNotesPresentation.Build"/> directly (Phase 11C Part G/H/L/M) —
/// no relevance/ordering/business rule is recomputed here, only formatting of an already-bounded,
/// already-ordered <see cref="DashboardOperationalNotesSummary"/>.
/// </summary>
public class DashboardOperationalNotesPresentationTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 8, 26);

    private static DashboardOperationalNoteItem Item(
        string title,
        OperationalNoteCategory category = OperationalNoteCategory.General,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null) =>
        new(Guid.NewGuid(), title, category, priority, effectiveFrom ?? ReferenceDate, effectiveThrough);

    [Fact]
    public void Build_NoItems_ShowsEmptyState()
    {
        var presentation = DashboardOperationalNotesPresentation.Build(DashboardOperationalNotesSummary.Empty, ReferenceDate);

        Assert.True(presentation.ShowEmptyState);
        Assert.False(presentation.HasNotes);
        Assert.False(presentation.LoadFailed);
        Assert.Empty(presentation.Items);
    }

    [Fact]
    public void Build_LoadFailed_NeverShowsEmptyStateInstead()
    {
        var summary = DashboardOperationalNotesSummary.Empty with { LoadFailed = true };

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.True(presentation.LoadFailed);
        Assert.False(presentation.ShowEmptyState);
    }

    [Fact]
    public void Build_CategoryAndPriorityLabels_ReuseCentralizedOptionMapping()
    {
        var item = Item("Group check-in support at North Gate", OperationalNoteCategory.GuestServices, OperationalNotePriority.Important);
        var summary = new DashboardOperationalNotesSummary(1, 1, [item], false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        var row = Assert.Single(presentation.Items);
        Assert.Equal(OperationalNoteCategoryOption.For(OperationalNoteCategory.GuestServices).Label, row.CategoryLabel);
        Assert.Equal(OperationalNotePriorityOption.For(OperationalNotePriority.Important).Label, row.PriorityLabel);
        Assert.True(row.IsImportant);
    }

    [Fact]
    public void Build_NullEffectiveThrough_DateRangeTextSaysUntilResolved()
    {
        var item = Item("Ongoing issue", effectiveFrom: ReferenceDate.AddDays(-2), effectiveThrough: null);
        var summary = new DashboardOperationalNotesSummary(1, 0, [item], false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.Contains("Until resolved", presentation.Items[0].DateRangeText);
    }

    [Fact]
    public void Build_EffectiveThroughPresent_DateRangeTextIncludesThroughDate()
    {
        var item = Item("Temporary closure", effectiveFrom: ReferenceDate.AddDays(-1), effectiveThrough: ReferenceDate.AddDays(2));
        var summary = new DashboardOperationalNotesSummary(1, 0, [item], false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.Contains("–", presentation.Items[0].DateRangeText);
        Assert.DoesNotContain("Until resolved", presentation.Items[0].DateRangeText);
    }

    // Phase 11C Part H: overflow is computed from the true Count vs. the already-bounded Items list —
    // never hidden.
    [Fact]
    public void Build_MoreItemsThanPreviewInSummary_ComputesOverflowText()
    {
        var items = new[] { Item("Note 1"), Item("Note 2"), Item("Note 3") };
        var summary = new DashboardOperationalNotesSummary(7, 1, items, false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.True(presentation.HasOverflow);
        Assert.Contains("4", presentation.OverflowText);
    }

    [Fact]
    public void Build_ItemsMatchCount_NoOverflow()
    {
        var items = new[] { Item("Note 1"), Item("Note 2") };
        var summary = new DashboardOperationalNotesSummary(2, 0, items, false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.False(presentation.HasOverflow);
        Assert.Equal(string.Empty, presentation.OverflowText);
    }

    // Phase 11C Part AH: priority/category/date-range must be represented in text even for a Normal
    // (non-Important) note, via the composed accessible summary.
    [Fact]
    public void Build_NormalNote_AccessibleSummaryOmitsImportantPrefix()
    {
        var item = Item("Routine note", OperationalNoteCategory.Staffing, OperationalNotePriority.Normal);
        var summary = new DashboardOperationalNotesSummary(1, 0, [item], false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.DoesNotContain("Important", presentation.Items[0].AccessibleSummary);
        Assert.Contains("Staffing", presentation.Items[0].AccessibleSummary);
        Assert.Contains("Routine note", presentation.Items[0].AccessibleSummary);
    }

    [Fact]
    public void Build_ImportantNote_AccessibleSummaryIncludesImportantPrefix()
    {
        var item = Item("Urgent note", OperationalNoteCategory.Equipment, OperationalNotePriority.Important);
        var summary = new DashboardOperationalNotesSummary(1, 1, [item], false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.StartsWith("Important.", presentation.Items[0].AccessibleSummary);
    }

    // A long title must survive Build unbounded — truncation/trimming is XAML-side, presentation-only.
    [Fact]
    public void Build_LongTitle_IsNotTruncatedByPresentation()
    {
        var longTitle = new string('a', 300);
        var item = Item(longTitle);
        var summary = new DashboardOperationalNotesSummary(1, 0, [item], false);

        var presentation = DashboardOperationalNotesPresentation.Build(summary, ReferenceDate);

        Assert.Equal(longTitle, presentation.Items[0].Title);
    }
}
