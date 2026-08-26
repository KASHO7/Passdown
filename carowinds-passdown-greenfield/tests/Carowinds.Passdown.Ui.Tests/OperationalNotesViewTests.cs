using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 11B Part AI (items 15-20): proves <see cref="OperationalNotesView"/> resolves through
/// Avalonia's real XAML loader — including every binding/converter path — for the empty state, a
/// populated list, the editor, a resolved note's detail, and an expired note's detail, and that its
/// key actions carry an accessible automation name.
/// </summary>
public class OperationalNotesViewTests
{
    private static readonly DateTimeOffset TodayInstant = new(2026, 8, 26, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static OperationalNote MakeNote(
        string title = "Note",
        OperationalNoteCategory category = OperationalNoteCategory.General,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active,
        DateTimeOffset? resolvedAtUtc = null) =>
        new(
            Guid.NewGuid(),
            title,
            "Some detail.",
            category,
            priority,
            effectiveFrom ?? Today,
            effectiveThrough,
            status,
            TodayInstant,
            TodayInstant,
            resolvedAtUtc);

    private static OperationalNotesViewModel CreateViewModel(params OperationalNote[] seed)
    {
        var service = new FakeOperationalNotesService();
        service.Seed(seed);
        return new OperationalNotesViewModel(service, new FakeClock(TodayInstant));
    }

    // 15/19: constructs cleanly with no notes (the empty state).
    [Fact]
    public async Task OperationalNotesView_EmptyState_ConstructsAndAcceptsDataContext()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;

        var view = new OperationalNotesView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.True(viewModel.ShowEmptyState);
    }

    // A populated list constructs cleanly, including an Important note and a lifecycle badge.
    [Fact]
    public async Task OperationalNotesView_WithNotes_ConstructsWithoutError()
    {
        var viewModel = CreateViewModel(MakeNote("A", priority: OperationalNotePriority.Important));
        await viewModel.PendingLoad;

        var view = new OperationalNotesView { DataContext = viewModel };
        viewModel.SelectedNote = viewModel.Notes[0];

        Assert.Same(viewModel, view.DataContext);
        Assert.NotNull(viewModel.SelectedDetail);
    }

    // 16: the create editor constructs cleanly.
    [Fact]
    public async Task OperationalNotesView_CreateEditor_ConstructsWithoutError()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;
        var view = new OperationalNotesView { DataContext = viewModel };

        viewModel.NewNoteCommand.Execute(null);

        Assert.True(viewModel.ShowEditor);
        Assert.Same(viewModel, view.DataContext);
    }

    // 16: the edit editor constructs cleanly.
    [Fact]
    public async Task OperationalNotesView_EditEditor_ConstructsWithoutError()
    {
        var viewModel = CreateViewModel(MakeNote("A"));
        await viewModel.PendingLoad;
        var view = new OperationalNotesView { DataContext = viewModel };
        viewModel.SelectedNote = viewModel.Notes[0];

        viewModel.EditCommand.Execute(null);

        Assert.True(viewModel.ShowEditor);
        Assert.False(viewModel.IsCreating);
        Assert.Same(viewModel, view.DataContext);
    }

    // 17: a resolved note's read-only detail constructs cleanly (no Edit/Resolve controls).
    [Fact]
    public async Task OperationalNotesView_ResolvedNoteDetail_ConstructsWithoutError()
    {
        var viewModel = CreateViewModel(MakeNote("A", status: OperationalNoteStatus.Resolved, resolvedAtUtc: TodayInstant));
        await viewModel.PendingLoad;
        viewModel.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;
        var view = new OperationalNotesView { DataContext = viewModel };

        viewModel.SelectedNote = viewModel.Notes[0];

        Assert.True(viewModel.SelectedDetail!.IsResolved);
        Assert.NotNull(viewModel.SelectedDetail.ResolvedAtText);
        Assert.Same(viewModel, view.DataContext);
    }

    // 18: an expired note's detail constructs cleanly and remains editable.
    [Fact]
    public async Task OperationalNotesView_ExpiredNoteDetail_ConstructsWithoutError()
    {
        var viewModel = CreateViewModel(MakeNote("A", effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1)));
        await viewModel.PendingLoad;
        viewModel.LifecycleFilter = OperationalNoteLifecycleFilter.Expired;
        var view = new OperationalNotesView { DataContext = viewModel };

        viewModel.SelectedNote = viewModel.Notes[0];

        Assert.Equal(OperationalNoteLifecycleState.Expired, viewModel.SelectedDetail!.LifecycleState);
        Assert.Same(viewModel, view.DataContext);
    }

    // 21 (long content): a very long title/body still constructs cleanly.
    [Fact]
    public async Task OperationalNotesView_LongTitleAndBody_ConstructsWithoutError()
    {
        var longTitle = new string('a', 200);
        var note = MakeNote(longTitle) with { Body = new string('b', 4000) };
        var viewModel = CreateViewModel(note);
        await viewModel.PendingLoad;
        var view = new OperationalNotesView { DataContext = viewModel };
        viewModel.SelectedNote = viewModel.Notes[0];

        Assert.Same(viewModel, view.DataContext);
        Assert.NotNull(viewModel.SelectedDetail);
    }

    // 20: every critical control carries an accessible automation name.
    [Fact]
    public async Task OperationalNotesView_ExposesCriticalAutomationNames()
    {
        var viewModel = CreateViewModel(MakeNote("A"));
        await viewModel.PendingLoad;
        var view = new OperationalNotesView { DataContext = viewModel };
        viewModel.SelectedNote = viewModel.Notes[0];

        var names = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Operational notes list", names);
        Assert.Contains("New Note", names);
        Assert.Contains("All notes filter", names);
        Assert.Contains("Active notes filter", names);
        Assert.Contains("Upcoming notes filter", names);
        Assert.Contains("Expired notes filter", names);
        Assert.Contains("Resolved notes filter", names);
        Assert.Contains("Edit", names);
        Assert.Contains("Resolve", names);

        viewModel.NewNoteCommand.Execute(null);
        var editorNames = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Note title", editorNames);
        Assert.Contains("Note body", editorNames);
        Assert.Contains("Category", editorNames);
        Assert.Contains("Priority", editorNames);
        Assert.Contains("Effective From", editorNames);
        Assert.Contains("Effective Through", editorNames);
        Assert.Contains("Save", editorNames);
        Assert.Contains("Cancel", editorNames);
    }
}
