using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="OperationsCalendarNotesPresentation"/> (Phase 12C Part E/F/Z) directly — the
/// compact "N Notes"/"N Notes · M Important" cell text, the selected-date panel's "active"/"Important"
/// lines, and the empty-state sentence, for relevant counts of 0/1/many and Important counts of 0/1/many.
/// </summary>
public class OperationsCalendarNotesPresentationTests
{
    [Fact]
    public void Build_ZeroRelevantNotes_CellTextIsNullAndDetailIsEmptyStateSentence()
    {
        var presentation = OperationsCalendarNotesPresentation.Build(relevantCount: 0, importantCount: 0);

        Assert.Null(presentation.CellText);
        Assert.Equal("No active operational notes for this date.", presentation.DetailCountText);
        Assert.Null(presentation.DetailImportantText);
        Assert.Equal("no active operational notes", presentation.AccessibilityText);
    }

    [Fact]
    public void Build_OneRelevantNoteNoImportant_ShowsSingularCompactText()
    {
        var presentation = OperationsCalendarNotesPresentation.Build(relevantCount: 1, importantCount: 0);

        Assert.Equal("1 Note", presentation.CellText);
        Assert.Equal("1 active", presentation.DetailCountText);
        Assert.Null(presentation.DetailImportantText);
        Assert.Equal("1 active operational note", presentation.AccessibilityText);
    }

    // Part F: a zero Important count never shows a meaningless "0 Important" line.
    [Fact]
    public void Build_MultipleRelevantNotesNoImportant_ShowsPluralCompactTextWithNoImportantLine()
    {
        var presentation = OperationsCalendarNotesPresentation.Build(relevantCount: 3, importantCount: 0);

        Assert.Equal("3 Notes", presentation.CellText);
        Assert.Equal("3 active", presentation.DetailCountText);
        Assert.Null(presentation.DetailImportantText);
        Assert.Equal("3 active operational notes", presentation.AccessibilityText);
    }

    // Part F: when Important > 0, the compact cell text includes it.
    [Fact]
    public void Build_RelevantNotesWithImportant_ShowsCombinedCompactText()
    {
        var presentation = OperationsCalendarNotesPresentation.Build(relevantCount: 3, importantCount: 1);

        Assert.Equal("3 Notes · 1 Important", presentation.CellText);
        Assert.Equal("3 active", presentation.DetailCountText);
        Assert.Equal("1 Important", presentation.DetailImportantText);
        Assert.Equal("3 active operational notes, 1 Important", presentation.AccessibilityText);
    }

    [Fact]
    public void Build_AllNotesImportant_ShowsPluralImportantText()
    {
        var presentation = OperationsCalendarNotesPresentation.Build(relevantCount: 2, importantCount: 2);

        Assert.Equal("2 Notes · 2 Important", presentation.CellText);
        Assert.Equal("2 Important", presentation.DetailImportantText);
    }
}
