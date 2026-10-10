using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="OperationsCalendarPassdownPresentation"/> (Phase 12C Part A/B/C/D/Y) directly —
/// the single place the Finalized-over-Draft precedence is decided, plus the cell/detail/accessibility
/// text for each of the three passdown states.
/// </summary>
public class OperationsCalendarPassdownPresentationTests
{
    // Part B: Finalized wins over an existing saved draft.
    [Fact]
    public void DeriveStatus_BothFinalizedAndDraftFlagsTrue_ResultIsFinalized()
    {
        var status = OperationsCalendarPassdownPresentation.DeriveStatus(hasFinalizedPassdown: true, hasAssociateDraft: true);

        Assert.Equal(OperationsCalendarPassdownStatus.Finalized, status);
    }

    [Fact]
    public void DeriveStatus_DraftOnly_ResultIsDraftInProgress()
    {
        var status = OperationsCalendarPassdownPresentation.DeriveStatus(hasFinalizedPassdown: false, hasAssociateDraft: true);

        Assert.Equal(OperationsCalendarPassdownStatus.DraftInProgress, status);
    }

    [Fact]
    public void DeriveStatus_NeitherFlag_ResultIsNoDraft()
    {
        var status = OperationsCalendarPassdownPresentation.DeriveStatus(hasFinalizedPassdown: false, hasAssociateDraft: false);

        Assert.Equal(OperationsCalendarPassdownStatus.NoDraft, status);
    }

    // Part C: No Draft is neutral text, never an error-styled label.
    [Fact]
    public void Build_NoDraft_ShowsNoDraftLabelAndEmptyStateNote()
    {
        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.NoDraft);

        Assert.Equal("No Draft", presentation.CellLabel);
        Assert.Equal("No Draft", presentation.DetailLabel);
        Assert.Equal("No passdown started.", presentation.DetailNoteText);
        Assert.Equal("No draft", presentation.AccessibilityText);
    }

    // Part D: the day-cell label is the shortest clear form ("Draft"); the detail label is the fuller
    // "Draft In Progress" — matching Dashboard's own wording (spec Part D).
    [Fact]
    public void Build_DraftInProgress_CellLabelIsShortDetailLabelIsFull()
    {
        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.DraftInProgress);

        Assert.Equal("Draft", presentation.CellLabel);
        Assert.Equal("Draft In Progress", presentation.DetailLabel);
        Assert.Equal("Draft in progress", presentation.AccessibilityText);
    }

    [Fact]
    public void Build_DraftInProgress_WithLastSavedAt_IncludesLastSavedNote()
    {
        var lastSaved = new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero); // 2:00 PM EDT

        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.DraftInProgress, lastSavedAt: lastSaved);

        Assert.NotNull(presentation.DetailNoteText);
        Assert.StartsWith("Last saved", presentation.DetailNoteText);
    }

    [Fact]
    public void Build_DraftInProgress_WithNoLastSavedAt_HasNoDetailNote()
    {
        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.DraftInProgress);

        Assert.Null(presentation.DetailNoteText);
    }

    [Fact]
    public void Build_Finalized_CellAndDetailLabelsAreFinalized()
    {
        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.Finalized);

        Assert.Equal("Finalized", presentation.CellLabel);
        Assert.Equal("Finalized", presentation.DetailLabel);
        Assert.Equal("Finalized", presentation.AccessibilityText);
    }

    [Fact]
    public void Build_Finalized_WithFinalizedAt_IncludesFinalizedTimestampNote()
    {
        var finalizedAt = new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);

        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.Finalized, finalizedAt: finalizedAt);

        Assert.NotNull(presentation.DetailNoteText);
        Assert.StartsWith("Finalized", presentation.DetailNoteText);
    }

    // Part AD: empty-state wording when no exact timestamp is available.
    [Fact]
    public void Build_Finalized_WithNoFinalizedAt_ShowsGenericAvailableInHistoryNote()
    {
        var presentation = OperationsCalendarPassdownPresentation.Build(OperationsCalendarPassdownStatus.Finalized);

        Assert.Equal("Finalized passdown available in History.", presentation.DetailNoteText);
    }
}
