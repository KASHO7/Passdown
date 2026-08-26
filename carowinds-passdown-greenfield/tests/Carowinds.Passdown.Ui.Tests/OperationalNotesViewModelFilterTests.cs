using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 11E Parts A-D: <see cref="OperationalNotesViewModel"/>'s category and priority filters,
/// composed with the existing Phase 11B lifecycle filter, plus the "Clear filters" reset and the
/// filtered-vs-no-notes-at-all empty-state distinction. <see cref="OperationalNotesViewModelTests"/>
/// keeps 11B's own lifecycle-only coverage; this file is additive.
/// </summary>
public class OperationalNotesViewModelFilterTests
{
    private static readonly DateTimeOffset TodayInstant = new(2026, 8, 26, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static OperationalNote MakeNote(
        string title = "Note",
        OperationalNoteCategory category = OperationalNoteCategory.General,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active) =>
        new(
            Guid.NewGuid(), title, null, category, priority,
            effectiveFrom ?? Today, effectiveThrough, status, TodayInstant, TodayInstant,
            status == OperationalNoteStatus.Resolved ? TodayInstant : null);

    private static (OperationalNotesViewModel ViewModel, FakeOperationalNotesService Service) Create(params OperationalNote[] seed)
    {
        var service = new FakeOperationalNotesService();
        service.Seed(seed);
        var viewModel = new OperationalNotesViewModel(service, new FakeClock(TodayInstant));
        return (viewModel, service);
    }

    private static OperationalNoteCategoryFilterOption CategoryOption(OperationalNoteCategory category) =>
        OperationalNoteCategoryFilterOption.All.Single(o => o.Value == category);

    private static OperationalNotePriorityFilterOption PriorityOption(OperationalNotePriority priority) =>
        OperationalNotePriorityFilterOption.All.Single(o => o.Value == priority);

    [Fact]
    public async Task CategoryFilter_DefaultsToAll()
    {
        var (vm, _) = Create();
        await vm.PendingLoad;

        Assert.Equal(OperationalNoteCategoryFilterOption.AllOption, vm.CategoryFilter);
    }

    [Fact]
    public async Task PriorityFilter_DefaultsToAll()
    {
        var (vm, _) = Create();
        await vm.PendingLoad;

        Assert.Equal(OperationalNotePriorityFilterOption.AllOption, vm.PriorityFilter);
    }

    [Theory]
    [InlineData(OperationalNoteCategory.General)]
    [InlineData(OperationalNoteCategory.Staffing)]
    [InlineData(OperationalNoteCategory.Admissions)]
    [InlineData(OperationalNoteCategory.GuestServices)]
    [InlineData(OperationalNoteCategory.Equipment)]
    [InlineData(OperationalNoteCategory.Events)]
    [InlineData(OperationalNoteCategory.Operations)]
    public async Task CategoryFilter_EachCategory_ShowsOnlyMatchingNotes(OperationalNoteCategory category)
    {
        var matching = MakeNote("Matches", category: category);
        var others = Enum.GetValues<OperationalNoteCategory>().Where(c => c != category)
            .Select((c, i) => MakeNote($"Other {i}", category: c)).ToArray();
        var (vm, _) = Create([matching, .. others]);
        await vm.PendingLoad;

        vm.CategoryFilter = CategoryOption(category);

        Assert.Equal(["Matches"], vm.Notes.Select(n => n.Title));
    }

    [Theory]
    [InlineData(OperationalNotePriority.Important)]
    [InlineData(OperationalNotePriority.Normal)]
    public async Task PriorityFilter_EachPriority_ShowsOnlyMatchingNotes(OperationalNotePriority priority)
    {
        var important = MakeNote("Important note", priority: OperationalNotePriority.Important);
        var normal = MakeNote("Normal note", priority: OperationalNotePriority.Normal);
        var (vm, _) = Create(important, normal);
        await vm.PendingLoad;

        vm.PriorityFilter = PriorityOption(priority);

        var expected = priority == OperationalNotePriority.Important ? "Important note" : "Normal note";
        Assert.Equal([expected], vm.Notes.Select(n => n.Title));
    }

    [Fact]
    public async Task PriorityFilter_All_ShowsBothPriorities()
    {
        var important = MakeNote("Important note", priority: OperationalNotePriority.Important);
        var normal = MakeNote("Normal note", priority: OperationalNotePriority.Normal);
        var (vm, _) = Create(important, normal);
        await vm.PendingLoad;

        Assert.Equal(2, vm.Notes.Count);
    }

    [Fact]
    public async Task LifecycleAndCategoryFilters_Compose()
    {
        var activeEquipment = MakeNote("Active Equipment", category: OperationalNoteCategory.Equipment);
        var activeStaffing = MakeNote("Active Staffing", category: OperationalNoteCategory.Staffing);
        var expiredEquipment = MakeNote("Expired Equipment", category: OperationalNoteCategory.Equipment,
            effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1));
        var (vm, _) = Create(activeEquipment, activeStaffing, expiredEquipment);
        await vm.PendingLoad;

        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);

        Assert.Equal(["Active Equipment"], vm.Notes.Select(n => n.Title));
    }

    [Fact]
    public async Task LifecycleAndPriorityFilters_Compose()
    {
        var activeImportant = MakeNote("Active Important", priority: OperationalNotePriority.Important);
        var activeNormal = MakeNote("Active Normal", priority: OperationalNotePriority.Normal);
        var expiredImportant = MakeNote("Expired Important", priority: OperationalNotePriority.Important,
            effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1));
        var (vm, _) = Create(activeImportant, activeNormal, expiredImportant);
        await vm.PendingLoad;

        vm.PriorityFilter = PriorityOption(OperationalNotePriority.Important);

        Assert.Equal(["Active Important"], vm.Notes.Select(n => n.Title));
    }

    [Fact]
    public async Task CategoryAndPriorityFilters_Compose()
    {
        var match = MakeNote("Match", category: OperationalNoteCategory.Equipment, priority: OperationalNotePriority.Important);
        var wrongCategory = MakeNote("Wrong category", category: OperationalNoteCategory.Staffing, priority: OperationalNotePriority.Important);
        var wrongPriority = MakeNote("Wrong priority", category: OperationalNoteCategory.Equipment, priority: OperationalNotePriority.Normal);
        var (vm, _) = Create(match, wrongCategory, wrongPriority);
        await vm.PendingLoad;

        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);
        vm.PriorityFilter = PriorityOption(OperationalNotePriority.Important);

        Assert.Equal(["Match"], vm.Notes.Select(n => n.Title));
    }

    [Fact]
    public async Task AllThreeFilters_Compose()
    {
        var match = MakeNote("Match", category: OperationalNoteCategory.Equipment, priority: OperationalNotePriority.Important);
        var wrongLifecycle = MakeNote("Wrong lifecycle", category: OperationalNoteCategory.Equipment, priority: OperationalNotePriority.Important,
            effectiveFrom: Today.AddDays(5));
        var wrongCategory = MakeNote("Wrong category", category: OperationalNoteCategory.Staffing, priority: OperationalNotePriority.Important);
        var wrongPriority = MakeNote("Wrong priority", category: OperationalNoteCategory.Equipment, priority: OperationalNotePriority.Normal);
        var (vm, _) = Create(match, wrongLifecycle, wrongCategory, wrongPriority);
        await vm.PendingLoad;

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Active;
        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);
        vm.PriorityFilter = PriorityOption(OperationalNotePriority.Important);

        Assert.Equal(["Match"], vm.Notes.Select(n => n.Title));
    }

    [Fact]
    public async Task Filters_HidingEveryNote_ShowsFilteredEmptyState_NotNoNotesAtAllState()
    {
        var (vm, _) = Create(MakeNote("Only note", category: OperationalNoteCategory.Staffing));
        await vm.PendingLoad;

        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);

        Assert.True(vm.ShowFilteredEmptyState);
        Assert.False(vm.ShowEmptyState);
        Assert.Contains("clearing filters", vm.FilteredEmptyStateMessage);
    }

    [Fact]
    public async Task NoNotesAtAll_ShowsEmptyState_NotFilteredEmptyState()
    {
        var (vm, _) = Create();
        await vm.PendingLoad;

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowFilteredEmptyState);
    }

    [Fact]
    public async Task HasActiveSecondaryFilters_TrueOnlyWhenCategoryOrPriorityIsNotAll()
    {
        var (vm, _) = Create(MakeNote());
        await vm.PendingLoad;

        Assert.False(vm.HasActiveSecondaryFilters);

        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);
        Assert.True(vm.HasActiveSecondaryFilters);

        vm.CategoryFilter = OperationalNoteCategoryFilterOption.AllOption;
        vm.PriorityFilter = PriorityOption(OperationalNotePriority.Important);
        Assert.True(vm.HasActiveSecondaryFilters);
    }

    [Fact]
    public async Task ClearFilters_ResetsCategoryAndPriority_ButNotLifecycle()
    {
        var (vm, _) = Create(MakeNote());
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.All;
        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);
        vm.PriorityFilter = PriorityOption(OperationalNotePriority.Important);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal(OperationalNoteCategoryFilterOption.AllOption, vm.CategoryFilter);
        Assert.Equal(OperationalNotePriorityFilterOption.AllOption, vm.PriorityFilter);
        Assert.Equal(OperationalNoteLifecycleFilter.All, vm.LifecycleFilter);
        Assert.False(vm.HasActiveSecondaryFilters);
    }

    // Part C: filter state is preserved for the lifetime of the (session-long) ViewModel instance —
    // there is no reset-on-reload/reset-on-navigation behavior to prove beyond "nothing clears it".
    [Fact]
    public async Task FilterState_SurvivesReload()
    {
        var (vm, _) = Create(MakeNote("Equipment note", category: OperationalNoteCategory.Equipment));
        await vm.PendingLoad;
        vm.CategoryFilter = CategoryOption(OperationalNoteCategory.Equipment);

        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(CategoryOption(OperationalNoteCategory.Equipment), vm.CategoryFilter);
    }

    // ===================== Carry-forward labels (Parts E/F) =====================

    [Fact]
    public async Task CarryForward_ActiveWithNoEffectiveThrough_ShowsUntilResolved()
    {
        var (vm, _) = Create(MakeNote("A", effectiveThrough: null));
        await vm.PendingLoad;

        Assert.Equal("Until resolved", vm.Notes[0].CarryForwardText);
    }

    [Fact]
    public async Task CarryForward_ActiveExpiringToday_ShowsExpiresToday()
    {
        var (vm, _) = Create(MakeNote("A", effectiveThrough: Today));
        await vm.PendingLoad;

        Assert.Equal("Expires today", vm.Notes[0].CarryForwardText);
    }

    [Fact]
    public async Task CarryForward_ActiveExpiringTomorrow_ShowsExpiresTomorrow()
    {
        var (vm, _) = Create(MakeNote("A", effectiveThrough: Today.AddDays(1)));
        await vm.PendingLoad;

        Assert.Equal("Expires tomorrow", vm.Notes[0].CarryForwardText);
    }

    [Fact]
    public async Task CarryForward_ActiveExpiringFurtherOut_ShowsThroughDate()
    {
        var (vm, _) = Create(MakeNote("A", effectiveThrough: Today.AddDays(4)));
        await vm.PendingLoad;

        Assert.Equal("Through Aug 30", vm.Notes[0].CarryForwardText);
    }

    [Fact]
    public async Task CarryForward_UpcomingStartingTomorrow_ShowsStartsTomorrow()
    {
        var (vm, _) = Create(MakeNote("A", effectiveFrom: Today.AddDays(1)));
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Upcoming;

        Assert.Equal("Starts tomorrow", vm.Notes[0].CarryForwardText);
    }

    [Fact]
    public async Task CarryForward_Resolved_IsNull()
    {
        var (vm, _) = Create(MakeNote("A", status: OperationalNoteStatus.Resolved));
        await vm.PendingLoad;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;

        Assert.Null(vm.Notes[0].CarryForwardText);
    }

    [Fact]
    public async Task CarryForward_AppearsInDetailPresentationToo()
    {
        var (vm, _) = Create(MakeNote("A", effectiveThrough: Today));
        await vm.PendingLoad;

        vm.SelectedNote = vm.Notes[0];

        Assert.Equal("Expires today", vm.SelectedDetail!.CarryForwardText);
    }
}
