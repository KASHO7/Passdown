using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Phase 35D: synthetic persistent notes available on the Associate date (in memory only).</summary>
internal static class Phase35DFixtures
{
    public static void SeedPersistentNotes(FakeAssociatePassdownService service)
    {
        OperationalNote N(string title, OperationalNoteCategory c, OperationalNotePriority p, int from, int? through, string? body = null) =>
            new(Guid.NewGuid(), title, body, c, p, Fixtures.Today.AddDays(from), through is { } t ? Fixtures.Today.AddDays(t) : null,
                OperationalNoteStatus.Active, Fixtures.Now.AddDays(-5), Fixtures.Now.AddHours(-3), null);

        List<OperationalNote> notes =
        [
            N("Synthetic: South Gate scanner #3 out of service", OperationalNoteCategory.General, OperationalNotePriority.Important, -2, 3, "Use scanners 1, 2 and 4."),
            N("Synthetic: Season pass processing moved to Guest Services window 4", OperationalNoteCategory.General, OperationalNotePriority.Normal, 0, 0),
            N("Synthetic: Stroller rental relocated for SCarowinds", OperationalNoteCategory.General, OperationalNotePriority.Normal, -10, null),
        ];
        service.RelevantOperationalNotesByDate[Fixtures.Today] = notes;
        service.AllOperationalNotes.AddRange(notes);
    }

    /// <summary>
    /// The fake's scripted workspace ignores its own saved selections, so the "included" capture scripts the first
    /// available note as already selected (a frozen copy in the draft, as the real service stores it).
    /// </summary>
    public static void IncludeFirstPersistentNote(FakeAssociatePassdownService service)
    {
        var inner = service.WorkspaceFactory!;
        var source = service.RelevantOperationalNotesByDate[Fixtures.Today][0];
        service.WorkspaceFactory = date =>
        {
            var workspace = inner(date);
            var selection = new AssociateOperationalNoteSelection(source.Id, source.Title, source.Body, source.Category, source.Priority,
                source.EffectiveFrom, source.EffectiveThrough, Fixtures.Now);
            return workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [selection] } };
        };
        service.RelevantOperationalNotesByDate[Fixtures.Today] = [.. service.RelevantOperationalNotesByDate[Fixtures.Today].Skip(1)];
    }
}
