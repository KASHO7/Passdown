using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 11B Part AH: <see cref="OperationalNotesViewModel"/> behavior — load/filter/select,
/// create/edit/save/cancel, resolve, empty states, and the dirty-editor guard. Uses
/// <see cref="FakeOperationalNotesService"/> (never SQLite) for fast, deterministic scripting of
/// every lifecycle bucket; a smaller set of tests in <see cref="OperationalNotesServiceIntegrationTests"/>
/// exercises the real <c>OperationalNotesService</c> + an in-memory repository instead (Part AJ).
/// </summary>
public class OperationalNotesViewModelTests
{
    // Noon UTC is safely within the same Eastern calendar day regardless of EST/EDT.
    private static readonly DateTimeOffset TodayInstant = new(2026, 8, 26, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static OperationalNote MakeNote(
        string title = "Note",
        OperationalNoteCategory category = OperationalNoteCategory.General,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? resolvedAtUtc = null) =>
        new(
            Guid.NewGuid(),
            title,
            null,
            category,
            priority,
            effectiveFrom ?? Today,
            effectiveThrough,
            status,
            createdAtUtc ?? TodayInstant,
            createdAtUtc ?? TodayInstant,
            resolvedAtUtc);

    private static (OperationalNotesViewModel ViewModel, FakeOperationalNotesService Service) Create(params OperationalNote[] seed)
    {
        var service = new FakeOperationalNotesService();
        service.Seed(seed);
        var viewModel = new OperationalNotesViewModel(service, new FakeClock(TodayInstant));
        return (viewModel, service);
    }

    // 1: initial load.
    [Fact]
    public async Task Construction_LoadsNotes()
    {
        var (vm, _) = Create(MakeNote("A"));
        await vm.PendingLoad;

        Assert.Single(vm.Notes);
        Assert.Equal("A", vm.Notes[0].Title);
    }

    // 2: Active is the default filter.
    [Fact]
    public async Task DefaultFilter_IsActive()
    {
        var (vm, _) = Create();
        await vm.PendingLoad;

        Assert.Equal(OperationalNoteLifecycleFilter.Active, vm.LifecycleFilter);
        Assert.True(vm.IsActiveFilterSelected);
    }

    // 3/4/5: switching filters shows only the matching lifecycle bucket.
    [Fact]
    public async Task SwitchingFilters_ShowsOnlyMatchingLifecycleBucket()
    {
        var active = MakeNote("Active note", effectiveFrom: Today);
        var upcoming = MakeNote("Upcoming note", effectiveFrom: Today.AddDays(5));
        var expired = MakeNote("Expired note", effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1));
        var resolved = MakeNote("Resolved note", status: OperationalNoteStatus.Resolved, resolvedAtUtc: TodayInstant);
        var (vm, _) = Create(active, upcoming, expired, resolved);
        await vm.PendingLoad;

        Assert.Equal(["Active note"], vm.Notes.Select(n => n.Title));

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Upcoming;
        Assert.Equal(["Upcoming note"], vm.Notes.Select(n => n.Title));

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Expired;
        Assert.Equal(["Expired note"], vm.Notes.Select(n => n.Title));

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;
        Assert.Equal(["Resolved note"], vm.Notes.Select(n => n.Title));

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.All;
        Assert.Equal(4, vm.Notes.Count);
    }

    // 6: default list order (Active before Resolved; Important before Normal; newest first) is reused, not recomputed.
    [Fact]
    public async Task ListOrder_MatchesSharedDefaultOrdering()
    {
        var older = MakeNote("Older important", priority: OperationalNotePriority.Important, createdAtUtc: TodayInstant.AddHours(-2));
        var newer = MakeNote("Newer important", priority: OperationalNotePriority.Important, createdAtUtc: TodayInstant.AddHours(-1));
        var normal = MakeNote("Normal", priority: OperationalNotePriority.Normal, createdAtUtc: TodayInstant);
        var resolved = MakeNote("Resolved", status: OperationalNoteStatus.Resolved, resolvedAtUtc: TodayInstant, createdAtUtc: TodayInstant.AddHours(1));
        var (vm, _) = Create(normal, resolved, older, newer);
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.All;

        Assert.Equal(["Newer important", "Older important", "Normal", "Resolved"], vm.Notes.Select(n => n.Title));
    }

    // 7: selection builds SelectedDetail from the full note.
    [Fact]
    public async Task SelectingNote_BuildsSelectedDetail()
    {
        var (vm, _) = Create(MakeNote("A", category: OperationalNoteCategory.Equipment));
        await vm.PendingLoad;

        vm.SelectedNote = vm.Notes[0];

        Assert.NotNull(vm.SelectedDetail);
        Assert.Equal("A", vm.SelectedDetail!.Title);
        Assert.Equal("Equipment", vm.SelectedDetail.CategoryLabel);
    }

    // 8: New Note starts create mode with sensible defaults.
    [Fact]
    public async Task NewNoteCommand_StartsCreateMode()
    {
        var (vm, _) = Create();
        await vm.PendingLoad;

        vm.NewNoteCommand.Execute(null);

        Assert.Equal(OperationalNoteEditorMode.Create, vm.EditorMode);
        Assert.True(vm.IsCreating);
        Assert.Equal(string.Empty, vm.EditorTitle);
        Assert.Equal(Today, vm.EditorEffectiveFrom);
        Assert.True(vm.EditorUntilResolved);
        Assert.Null(vm.EditorEffectiveThrough);
    }

    // 9: Edit starts edit mode populated from the selected note.
    [Fact]
    public async Task EditCommand_StartsEditModeWithPopulatedFields()
    {
        var note = MakeNote("A", category: OperationalNoteCategory.Staffing, priority: OperationalNotePriority.Important);
        var (vm, _) = Create(note);
        await vm.PendingLoad;
        vm.SelectedNote = vm.Notes[0];

        vm.EditCommand.Execute(null);

        Assert.Equal(OperationalNoteEditorMode.Edit, vm.EditorMode);
        Assert.Equal("A", vm.EditorTitle);
        Assert.Equal(OperationalNoteCategory.Staffing, vm.EditorCategory.Value);
        Assert.Equal(OperationalNotePriority.Important, vm.EditorPriority.Value);
    }

    // 10: saving a new note persists it and selects it.
    [Fact]
    public async Task SaveCommand_Create_PersistsAndSelectsNewNote()
    {
        var (vm, service) = Create();
        await vm.PendingLoad;
        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "New note";
        vm.EditorCategory = OperationalNoteCategoryOption.For(OperationalNoteCategory.Equipment);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(OperationalNoteEditorMode.None, vm.EditorMode);
        Assert.NotNull(vm.SelectedNote);
        Assert.Equal("New note", vm.SelectedNote!.Title);
        Assert.Single(await service.ListAsync());
    }

    // 11: saving an edit updates the existing note in place.
    [Fact]
    public async Task SaveCommand_Edit_UpdatesExistingNote()
    {
        var note = MakeNote("Original");
        var (vm, service) = Create(note);
        await vm.PendingLoad;
        vm.SelectedNote = vm.Notes[0];
        vm.EditCommand.Execute(null);
        vm.EditorTitle = "Updated";

        await vm.SaveCommand.ExecuteAsync(null);

        var stored = await service.ListAsync();
        Assert.Single(stored);
        Assert.Equal("Updated", stored[0].Title);
        Assert.Equal(note.Id, stored[0].Id);
        Assert.Equal(note.CreatedAtUtc, stored[0].CreatedAtUtc);
    }

    // 12: Cancel on a create discards the in-progress note.
    [Fact]
    public async Task CancelCommand_Create_DiscardsNewNote()
    {
        var (vm, service) = Create();
        await vm.PendingLoad;
        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "Never saved";

        vm.CancelCommand.Execute(null);

        Assert.Equal(OperationalNoteEditorMode.None, vm.EditorMode);
        Assert.Empty(await service.ListAsync());
    }

    // 13: Cancel on an edit leaves the stored note unchanged.
    [Fact]
    public async Task CancelCommand_Edit_LeavesNoteUnchanged()
    {
        var note = MakeNote("Original");
        var (vm, service) = Create(note);
        await vm.PendingLoad;
        vm.SelectedNote = vm.Notes[0];
        vm.EditCommand.Execute(null);
        vm.EditorTitle = "Changed but not saved";

        vm.CancelCommand.Execute(null);

        Assert.Equal(OperationalNoteEditorMode.None, vm.EditorMode);
        var stored = await service.ListAsync();
        Assert.Equal("Original", stored[0].Title);
    }

    // 14: validation errors are shown as friendly text, and the service is never called.
    [Fact]
    public async Task SaveCommand_BlankTitle_ShowsValidationMessage()
    {
        var (vm, service) = Create();
        await vm.PendingLoad;
        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Enter a title.", vm.EditorValidationMessage);
        Assert.Equal(OperationalNoteEditorMode.Create, vm.EditorMode);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task SaveCommand_EffectiveThroughBeforeEffectiveFrom_ShowsValidationMessage()
    {
        var (vm, service) = Create();
        await vm.PendingLoad;
        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "A note";
        vm.EditorUntilResolved = false;
        vm.EditorEffectiveFrom = Today;
        vm.EditorEffectiveThrough = Today.AddDays(-1);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Effective Through cannot be before Effective From.", vm.EditorValidationMessage);
        Assert.Empty(await service.ListAsync());
    }

    // 15: resolving an active note calls the service and preserves the record.
    [Fact]
    public async Task ConfirmResolve_ResolvesSelectedNote()
    {
        var note = MakeNote("A");
        var (vm, service) = Create(note);
        await vm.PendingLoad;
        vm.SelectedNote = vm.Notes[0];

        vm.RequestResolveCommand.Execute(null);
        Assert.True(vm.IsConfirmingResolve);

        await vm.ConfirmResolveCommand.ExecuteAsync(null);

        Assert.False(vm.IsConfirmingResolve);
        var stored = await service.ListAsync();
        Assert.Equal(OperationalNoteStatus.Resolved, stored[0].Status);
        Assert.NotNull(stored[0].ResolvedAtUtc);
        // Moved out of the (default Active) filtered view.
        Assert.DoesNotContain(vm.Notes, n => n.Id == note.Id);
    }

    // 16: a resolved note has no Edit/Resolve affordance.
    [Fact]
    public async Task ResolvedNote_HasNoEditOrResolveCommandAvailable()
    {
        var note = MakeNote("A", status: OperationalNoteStatus.Resolved, resolvedAtUtc: TodayInstant);
        var (vm, _) = Create(note);
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;
        vm.SelectedNote = vm.Notes[0];

        Assert.True(vm.SelectedDetail!.IsResolved);
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.RequestResolveCommand.CanExecute(null));
    }

    // 17: an expired note (persisted Active, past EffectiveThrough) can still be edited to extend its range.
    [Fact]
    public async Task ExpiredNote_CanBeEditedToExtendEffectiveThrough()
    {
        var note = MakeNote("Expired", effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1));
        var (vm, service) = Create(note);
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Expired;
        vm.SelectedNote = vm.Notes[0];
        Assert.Equal(OperationalNoteLifecycleState.Expired, vm.SelectedDetail!.LifecycleState);
        Assert.True(vm.EditCommand.CanExecute(null));

        vm.EditCommand.Execute(null);
        vm.EditorUntilResolved = false;
        vm.EditorEffectiveThrough = Today.AddDays(5);
        await vm.SaveCommand.ExecuteAsync(null);

        var stored = (await service.ListAsync()).Single();
        Assert.Equal(OperationalNoteLifecycleState.Active, OperationalNoteLifecycle.Derive(stored, Today));
    }

    // 18: an upcoming note is editable (never treated as inactive/error).
    [Fact]
    public async Task UpcomingNote_IsEditable()
    {
        var note = MakeNote("Upcoming", effectiveFrom: Today.AddDays(3));
        var (vm, _) = Create(note);
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Upcoming;
        vm.SelectedNote = vm.Notes[0];

        Assert.Equal(OperationalNoteLifecycleState.Upcoming, vm.SelectedDetail!.LifecycleState);
        Assert.True(vm.EditCommand.CanExecute(null));
    }

    // 19: Reload only rereads local data — no network dependency exists on the service seam it calls.
    [Fact]
    public async Task ReloadCommand_OnlyRereadsLocalNotes()
    {
        var (vm, service) = Create(MakeNote("A"));
        await vm.PendingLoad;
        var callsBefore = service.ListCallCount;

        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(callsBefore + 1, service.ListCallCount);
        Assert.Single(vm.Notes);
    }

    // 20: no notes at all shows the "create your first note" empty state.
    [Fact]
    public async Task NoNotes_ShowsEmptyState()
    {
        var (vm, _) = Create();
        await vm.PendingLoad;

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowFilteredEmptyState);
    }

    // 21: notes exist, but none match the current filter.
    [Fact]
    public async Task NotesExistButNoneMatchFilter_ShowsFilteredEmptyState()
    {
        var (vm, _) = Create(MakeNote("Resolved", status: OperationalNoteStatus.Resolved, resolvedAtUtc: TodayInstant));
        await vm.PendingLoad; // default filter is Active

        Assert.False(vm.ShowEmptyState);
        Assert.True(vm.ShowFilteredEmptyState);
        Assert.Equal("No active operational notes.", vm.FilteredEmptyStateMessage);
    }

    // 22: Important priority is presented distinctly and never color-only (the label text itself says "Important").
    [Fact]
    public async Task ImportantNote_PresentationIncludesImportantLabel()
    {
        var (vm, _) = Create(MakeNote("A", priority: OperationalNotePriority.Important));
        await vm.PendingLoad;

        Assert.True(vm.Notes[0].IsImportant);
        Assert.Equal("Important", vm.Notes[0].PriorityLabel);
    }

    // 23: category enum values map to user-friendly labels, not raw enum formatting.
    [Fact]
    public void CategoryOptions_MapToFriendlyLabels()
    {
        Assert.Equal("Guest Services", OperationalNoteCategoryOption.For(OperationalNoteCategory.GuestServices).Label);
        Assert.Equal("General", OperationalNoteCategoryOption.For(OperationalNoteCategory.General).Label);
    }

    // 24: date-range presentation is friendly, not a raw ISO value.
    [Fact]
    public async Task DateRangeText_IsFriendlyNotRaw()
    {
        var untilResolved = MakeNote("Open ended", effectiveFrom: new DateOnly(2026, 8, 26));
        var bounded = MakeNote("Bounded", effectiveFrom: new DateOnly(2026, 8, 26), effectiveThrough: new DateOnly(2026, 8, 30));
        var (vm, _) = Create(untilResolved, bounded);
        await vm.PendingLoad;

        Assert.Contains(vm.Notes, n => n.Title == "Open ended" && n.DateRangeText == "Aug 26 – Until resolved");
        Assert.Contains(vm.Notes, n => n.Title == "Bounded" && n.DateRangeText == "Aug 26 – Aug 30");
    }

    // 25: while editing, Reload/New Note/Edit/Resolve are all disabled — the dirty-editor guard.
    [Fact]
    public async Task WhileEditing_ReloadNewNoteEditResolveAreDisabled()
    {
        var note = MakeNote("A");
        var (vm, _) = Create(note);
        await vm.PendingLoad;
        vm.SelectedNote = vm.Notes[0];

        vm.NewNoteCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.False(vm.ReloadCommand.CanExecute(null));
        Assert.False(vm.NewNoteCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.RequestResolveCommand.CanExecute(null));
    }
}
