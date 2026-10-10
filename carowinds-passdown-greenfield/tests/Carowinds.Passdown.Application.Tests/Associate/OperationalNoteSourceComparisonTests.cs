using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 11E Parts G-N: <see cref="OperationalNoteSourceComparison"/> in isolation, independent of
/// <see cref="AssociatePassdownService"/>'s own composition wiring (covered separately in
/// <see cref="AssociatePassdownServiceTests"/>). Exercises every <see cref="OperationalNoteSourceComparisonState"/>
/// and proves backward compatibility is automatic (Part N) — a "legacy" selection is just a selection
/// whose fields happen to already equal the source, since no new metadata field was ever added
/// (Part M's schema decision).
/// </summary>
public class OperationalNoteSourceComparisonTests
{
    private static readonly DateOnly Date = new(2026, 8, 21);
    private static readonly DateOnly OtherDate = new(2026, 8, 22);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    private static OperationalNote MakeSource(
        Guid? id = null,
        string title = "South Gate scanner #3 out of service",
        string? body = "Direct guests to gate 2.",
        OperationalNoteCategory category = OperationalNoteCategory.Equipment,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active) =>
        new(
            id ?? Guid.NewGuid(), title, body, category, priority,
            effectiveFrom ?? Date, effectiveThrough, status, FixedNow, FixedNow,
            status == OperationalNoteStatus.Resolved ? FixedNow : null);

    private static AssociateOperationalNoteSelection MakeSelection(OperationalNote source) =>
        new(source.Id, source.Title, source.Body, source.Category, source.Priority, source.EffectiveFrom, source.EffectiveThrough, FixedNow);

    [Fact]
    public void Compute_IdenticalContent_ReturnsUnchanged()
    {
        var source = MakeSource();
        var selection = MakeSelection(source);

        Assert.Equal(OperationalNoteSourceComparisonState.Unchanged, OperationalNoteSourceComparison.Compute(selection, source, Date));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("body")]
    [InlineData("category")]
    [InlineData("priority")]
    [InlineData("effectiveThrough")]
    public void Compute_AnySingleDisplayFieldDiffers_ReturnsContentChanged(string changedField)
    {
        var source = MakeSource();
        var selection = MakeSelection(source);

        var edited = changedField switch
        {
            "title" => source with { Title = "Scanner repaired" },
            "body" => source with { Body = "All clear." },
            "category" => source with { Category = OperationalNoteCategory.Operations },
            "priority" => source with { Priority = OperationalNotePriority.Important },
            "effectiveThrough" => source with { EffectiveThrough = Date.AddDays(5) },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField)),
        };

        Assert.Equal(OperationalNoteSourceComparisonState.ContentChanged, OperationalNoteSourceComparison.Compute(selection, edited, Date));
    }

    [Fact]
    public void Compute_SourceResolved_ReturnsResolved_EvenWithUnchangedContent()
    {
        var source = MakeSource();
        var selection = MakeSelection(source);
        var resolved = source with { Status = OperationalNoteStatus.Resolved, ResolvedAtUtc = FixedNow };

        Assert.Equal(OperationalNoteSourceComparisonState.Resolved, OperationalNoteSourceComparison.Compute(selection, resolved, Date));
    }

    [Fact]
    public void Compute_ResolvedTakesPrecedenceOverContentChanged()
    {
        var source = MakeSource();
        var selection = MakeSelection(source);
        var resolvedAndEdited = source with { Title = "Scanner repaired", Status = OperationalNoteStatus.Resolved, ResolvedAtUtc = FixedNow };

        Assert.Equal(OperationalNoteSourceComparisonState.Resolved, OperationalNoteSourceComparison.Compute(selection, resolvedAndEdited, Date));
    }

    [Fact]
    public void Compute_SourceDateRangeNoLongerCoversOperatingDate_ReturnsNoLongerApplicable()
    {
        var source = MakeSource(effectiveFrom: Date, effectiveThrough: Date);
        var selection = MakeSelection(source);
        var movedForward = source with { EffectiveFrom = OtherDate, EffectiveThrough = OtherDate };

        Assert.Equal(OperationalNoteSourceComparisonState.NoLongerApplicable, OperationalNoteSourceComparison.Compute(selection, movedForward, Date));
    }

    [Fact]
    public void Compute_SourceIsNull_ReturnsMissing()
    {
        var source = MakeSource();
        var selection = MakeSelection(source);

        Assert.Equal(OperationalNoteSourceComparisonState.Missing, OperationalNoteSourceComparison.Compute(selection, source: null, Date));
    }

    // Part N: nothing here special-cases an "older" selection — every field the comparison needs
    // (Title/Body/Category/Priority/EffectiveFrom/EffectiveThrough) already existed on schema 3, so a
    // pre-11E selection compares exactly like a post-11E one.
    [Fact]
    public void Compute_BehavesIdenticallyForAnyAgeOfSelection_NoSpecialCasingNeeded()
    {
        var source = MakeSource();
        var oldSelection = new AssociateOperationalNoteSelection(
            source.Id, source.Title, source.Body, source.Category, source.Priority,
            source.EffectiveFrom, source.EffectiveThrough, SelectedAtUtc: FixedNow.AddDays(-30));

        Assert.Equal(OperationalNoteSourceComparisonState.Unchanged, OperationalNoteSourceComparison.Compute(oldSelection, source, Date));
    }

    [Fact]
    public void ComputeAll_NoSelections_ReturnsEmptyWithoutTouchingSourceList()
    {
        var result = OperationalNoteSourceComparison.ComputeAll([], [], Date);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeAll_MultipleSelections_KeyedBySourceOperationalNoteId()
    {
        var unchangedSource = MakeSource(title: "Note A");
        var changedSource = MakeSource(title: "Note B");
        var selections = new[] { MakeSelection(unchangedSource), MakeSelection(changedSource) };
        var liveSources = new[] { unchangedSource, changedSource with { Title = "Note B (edited)" } };

        var result = OperationalNoteSourceComparison.ComputeAll(selections, liveSources, Date);

        Assert.Equal(OperationalNoteSourceComparisonState.Unchanged, result[unchangedSource.Id]);
        Assert.Equal(OperationalNoteSourceComparisonState.ContentChanged, result[changedSource.Id]);
    }
}
