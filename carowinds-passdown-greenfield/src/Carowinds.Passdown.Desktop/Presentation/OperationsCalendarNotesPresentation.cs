namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display-only text for a date's relevant persistent Operational Notes count (Phase 12C Part Z) — the
/// one place <c>"2 Notes"</c>/<c>"2 Notes · 1 Important"</c>-style compact text, the selected-date
/// panel's "active"/"Important" lines, and empty-state wording are built. Never recomputes relevance
/// (spec Part E/F/O: <see cref="Application.OperationalNotes.OperationalNoteRelevance"/> is the sole
/// authority; this type only formats already-counted totals) and never carries Important as an alarm
/// severity (Part F: text emphasis only, never red).
/// </summary>
public sealed record OperationsCalendarNotesPresentation(
    string? CellText,
    string DetailCountText,
    string? DetailImportantText,
    string AccessibilityText)
{
    public static OperationsCalendarNotesPresentation Build(int relevantCount, int importantCount)
    {
        // Part F: a zero-note date shows no line at all in a day cell — never a meaningless "0 Notes".
        string? cellText = relevantCount == 0
            ? null
            : importantCount > 0
                ? $"{NoteCountWord(relevantCount)} · {importantCount} Important"
                : NoteCountWord(relevantCount);

        var detailCountText = relevantCount switch
        {
            0 => "No active operational notes for this date.",
            1 => "1 active",
            _ => $"{relevantCount} active",
        };

        var detailImportantText = importantCount > 0
            ? (importantCount == 1 ? "1 Important" : $"{importantCount} Important")
            : null;

        var accessibilityText = relevantCount == 0
            ? "no active operational notes"
            : (relevantCount == 1 ? "1 active operational note" : $"{relevantCount} active operational notes")
                + (importantCount > 0 ? $", {importantCount} Important" : string.Empty);

        return new OperationsCalendarNotesPresentation(cellText, detailCountText, detailImportantText, accessibilityText);
    }

    private static string NoteCountWord(int count) => count == 1 ? "1 Note" : $"{count} Notes";
}
