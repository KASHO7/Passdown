using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 11D: Associate → Notes' explicit persistent-note inclusion workflow, exercised entirely
/// through <see cref="AssociateViewModel"/> against <see cref="FakeAssociatePassdownService"/> (its
/// per-date draft store plus scripted <see cref="FakeAssociatePassdownService.RelevantOperationalNotesByDate"/>
/// lets these tests prove real select/deselect/reload round trips, not just that the ViewModel calls
/// the right method). Pure C# — no Avalonia rendering; see <see cref="AssociateViewAccessibilityTests"/>
/// for the XAML-level automation-name assertions.
/// </summary>
public class AssociateViewModelOperationalNoteSelectionTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateOnly OtherDate = new(2026, 8, 22);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <paramref name="configure"/> runs against <see cref="FakeAssociatePassdownService"/> before
    /// <see cref="AssociateViewModel"/> is constructed — the constructor's own initial
    /// <c>SelectedDate</c> assignment synchronously triggers the very first load (the fake has no
    /// gate, so nothing here truly suspends), so any scripted <c>RelevantOperationalNotesByDate</c>
    /// must already be in place before that first load runs, not assigned afterward.
    /// </summary>
    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler) CreateViewModel(
        Action<FakeAssociatePassdownService>? configure = null)
    {
        var service = new FakeAssociatePassdownService();
        configure?.Invoke(service);
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay);
        return (viewModel, service, scheduler);
    }

    private static OperationalNote ScannerNote(DateOnly effectiveFrom, DateOnly? effectiveThrough = null) => new(
        Guid.NewGuid(), "South Gate scanner #3 out of service", "Direct guests to gate 2.",
        OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
        effectiveFrom, effectiveThrough, OperationalNoteStatus.Active,
        FixedNow.AddDays(-1), FixedNow.AddDays(-1), null);

    // 1/3: relevant suggestions load automatically after a date load, offered for Add.
    [Fact]
    public async Task DateLoad_RelevantNote_AppearsAsAvailableSuggestion()
    {
        var note = ScannerNote(Today);
        var (viewModel, _, _) = CreateViewModel(s => s.RelevantOperationalNotesByDate[Today] = [note]);

        await viewModel.PendingLoad;

        var suggestion = Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
        Assert.Equal(note.Id, suggestion.Id);
        Assert.Equal("South Gate scanner #3 out of service", suggestion.Title);
        Assert.True(viewModel.HasAvailableOperationalNoteSuggestions);
    }

    [Fact]
    public async Task NoRelevantNotes_EmptyAvailableState()
    {
        var (viewModel, _, _) = CreateViewModel();

        await viewModel.PendingLoad;

        Assert.Empty(viewModel.AvailableOperationalNoteSuggestions);
        Assert.False(viewModel.HasAvailableOperationalNoteSuggestions);
    }

    [Fact]
    public async Task NoSelectedNotes_EmptySelectedState()
    {
        var (viewModel, _, _) = CreateViewModel();

        await viewModel.PendingLoad;

        Assert.Empty(viewModel.SelectedOperationalNotes);
        Assert.False(viewModel.HasSelectedOperationalNotes);
    }

    // 5/6: Add command selects, persists, and moves the note out of Available into Selected.
    [Fact]
    public async Task AddCommand_SelectsNote_MovesFromAvailableToSelected()
    {
        var note = ScannerNote(Today);
        var (viewModel, service, _) = CreateViewModel(s => s.RelevantOperationalNotesByDate[Today] = [note]);
        await viewModel.PendingLoad;

        var suggestion = Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
        await suggestion.AddCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.AvailableOperationalNoteSuggestions);
        var selected = Assert.Single(viewModel.SelectedOperationalNotes);
        Assert.Equal(note.Id, selected.SourceOperationalNoteId);
        Assert.Contains((Today, note.Id), service.SelectOperationalNoteCalls);
    }

    [Fact]
    public async Task RemoveCommand_DeselectsNote_MovesBackToAvailable()
    {
        var note = ScannerNote(Today);
        var (viewModel, service, _) = CreateViewModel(s => s.RelevantOperationalNotesByDate[Today] = [note]);
        await viewModel.PendingLoad;
        await viewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);

        await viewModel.SelectedOperationalNotes.Single().RemoveCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SelectedOperationalNotes);
        var suggestion = Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
        Assert.Equal(note.Id, suggestion.Id);
        Assert.Contains((Today, note.Id), service.DeselectOperationalNoteCalls);
    }

    // 7/8: Add/Remove survive a date switch away and back (the draft store, not ViewModel memory, is authoritative).
    [Fact]
    public async Task Add_SurvivesDateSwitchAwayAndBack()
    {
        var note = ScannerNote(Today);
        var (viewModel, _, _) = CreateViewModel(s => s.RelevantOperationalNotesByDate[Today] = [note]);
        await viewModel.PendingLoad;
        await viewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);

        viewModel.SelectedDate = OtherDate;
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        var selected = Assert.Single(viewModel.SelectedOperationalNotes);
        Assert.Equal(note.Id, selected.SourceOperationalNoteId);
    }

    // Part Z: selections/suggestions are date-specific — no cross-date leakage in either direction.
    [Fact]
    public async Task SwitchingDate_ShowsOnlyThatDatesSelectionsAndSuggestions()
    {
        var noteForToday = ScannerNote(Today);
        var noteForOtherDate = new OperationalNote(
            Guid.NewGuid(), "Group check-in support at North Gate", null,
            OperationalNoteCategory.GuestServices, OperationalNotePriority.Normal,
            OtherDate, OtherDate, OperationalNoteStatus.Active, FixedNow, FixedNow, null);
        var (viewModel, _, _) = CreateViewModel(s =>
        {
            s.RelevantOperationalNotesByDate[Today] = [noteForToday];
            s.RelevantOperationalNotesByDate[OtherDate] = [noteForOtherDate];
        });

        await viewModel.PendingLoad;
        await viewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);

        viewModel.SelectedDate = OtherDate;
        await viewModel.PendingLoad;

        Assert.Empty(viewModel.SelectedOperationalNotes); // Today's selection never leaks onto OtherDate.
        var suggestion = Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
        Assert.Equal(noteForOtherDate.Id, suggestion.Id);
    }

    // Part AA: selecting/deselecting a persistent note never touches the manual notes text.
    [Fact]
    public async Task AddAndRemove_NeverChangeManualOperationalNotesText()
    {
        var note = ScannerNote(Today);
        var (viewModel, _, _) = CreateViewModel(s => s.RelevantOperationalNotesByDate[Today] = [note]);
        await viewModel.PendingLoad;

        // Setting the text schedules a debounced autosave that is never elapsed here; the Add
        // command below flushes it synchronously via FlushPendingAutosaveAsync before selecting,
        // exactly as it would for a real in-progress edit.
        viewModel.OperationalNotesText = "A manual note";

        await viewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);
        Assert.Equal("A manual note", viewModel.OperationalNotesText);

        await viewModel.SelectedOperationalNotes.Single().RemoveCommand.ExecuteAsync(null);
        Assert.Equal("A manual note", viewModel.OperationalNotesText);
    }

    [Fact]
    public async Task AccessibleSummaries_AreNeverEmpty()
    {
        var important = ScannerNote(Today) with { Priority = OperationalNotePriority.Important };
        var (viewModel, _, _) = CreateViewModel(s => s.RelevantOperationalNotesByDate[Today] = [important]);
        await viewModel.PendingLoad;

        var suggestion = Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
        Assert.False(string.IsNullOrWhiteSpace(suggestion.AccessibleSummary));

        await suggestion.AddCommand.ExecuteAsync(null);
        var selected = Assert.Single(viewModel.SelectedOperationalNotes);
        Assert.False(string.IsNullOrWhiteSpace(selected.AccessibleSummary));
    }
}
