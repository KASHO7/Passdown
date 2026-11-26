using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35F: the Operational Notes, Passdown History and Settings redesign. View-model and markup contracts only
/// (real-window layout lives in the run-alone Phase15ERealWindowLayoutTests). Every new behaviour is presentation:
/// in-memory search / filters over already-loaded lists, and the same saved settings shown as switches.
/// </summary>
public class Phase35FNotesHistorySettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static string Markup(string view) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));

    // ---------------------------------------------------------------- Operational Notes

    private static OperationalNote Note(string title, string? body = null, OperationalNoteCategory category = OperationalNoteCategory.General,
        int from = -1, int? through = null, bool resolved = false, OperationalNotePriority priority = OperationalNotePriority.Normal) =>
        new(Guid.NewGuid(), title, body, category, priority, Today.AddDays(from), through is { } t ? Today.AddDays(t) : null,
            resolved ? OperationalNoteStatus.Resolved : OperationalNoteStatus.Active, Now.AddDays(-3), Now.AddDays(-3), resolved ? Now.AddHours(-1) : null);

    private static async Task<(OperationalNotesViewModel Vm, FakeOperationalNotesService Service)> Notes(params OperationalNote[] notes)
    {
        var service = new FakeOperationalNotesService { UtcNowFactory = () => Now };
        service.Seed(notes);
        var vm = new OperationalNotesViewModel(service, new FakeClock(Now));
        await vm.PendingLoad;
        return (vm, service);
    }

    [Fact]
    public async Task NotesSearch_MatchesTitleTextAndCategory_EveryWord_CaseInsensitive_WithoutReadingAgain()
    {
        var (vm, service) = await Notes(
            Note("South Gate scanner down", "Use scanners 1 and 2."),
            Note("Extra staffing", "Gate 1 after 6 PM", OperationalNoteCategory.Staffing),
            Note("Locker kiosk", null, OperationalNoteCategory.GuestServices));
        var reads = service.ListCallCount;

        vm.SearchText = "SCANNER";
        Assert.Equal(["South Gate scanner down"], vm.Notes.Select(n => n.Title));

        vm.SearchText = "gate 6";
        Assert.Equal(["Extra staffing"], vm.Notes.Select(n => n.Title)); // "gate" in the text, "6" in the text

        vm.SearchText = "staffing";
        Assert.Equal(["Extra staffing"], vm.Notes.Select(n => n.Title));

        vm.SearchText = "guest services";
        Assert.Equal(["Locker kiosk"], vm.Notes.Select(n => n.Title)); // category label

        vm.SearchText = "nothing like this";
        Assert.Empty(vm.Notes);
        Assert.True(vm.ShowFilteredEmptyState);
        Assert.EndsWith("Try clearing filters.", vm.FilteredEmptyStateMessage, StringComparison.Ordinal);

        Assert.Equal(reads, service.ListCallCount); // searching never reads storage again
    }

    [Fact]
    public async Task NotesSearch_ComposesWithLifecycle_AndClearFiltersResetsIt()
    {
        var (vm, _) = await Notes(Note("Scanner active"), Note("Scanner resolved", resolved: true), Note("Other"));

        vm.SearchText = "scanner";
        Assert.Equal(["Scanner active"], vm.Notes.Select(n => n.Title)); // Active is the default lifecycle view
        vm.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.All);
        Assert.Equal(2, vm.Notes.Count);
        Assert.True(vm.HasActiveSecondaryFilters);

        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(3, vm.Notes.Count);
        Assert.Equal(OperationalNoteLifecycleFilter.All, vm.LifecycleFilter); // the lifecycle tab is not a secondary filter
    }

    [Fact]
    public async Task NotesSearch_KeepsTheSelection_WhileItStillMatches_AndNeverChangesANote()
    {
        var scanner = Note("Scanner down", "Body text");
        var (vm, service) = await Notes(scanner, Note("Other"));
        vm.SelectedNote = vm.Notes.Single(n => n.Title == "Scanner down");

        vm.SearchText = "scan";
        Assert.Equal(scanner.Id, vm.SelectedNote?.Id);
        Assert.Equal("Scanner down", vm.SelectedDetail?.Title);

        vm.SearchText = "other";
        Assert.Null(vm.SelectedNote); // no longer in the list: deselected rather than shown beside a list without it

        var stored = await service.ListAsync();
        Assert.Equal(scanner, stored.Single(n => n.Id == scanner.Id)); // record unchanged
        Assert.Empty(service.EditorConflicts);
    }

    [Fact]
    public async Task NotesList_HeadingAndCount_FollowTheFilters()
    {
        var (vm, _) = await Notes(Note("A"), Note("B"), Note("C", resolved: true));
        Assert.Equal("Active notes", vm.ListHeadingText);
        Assert.Equal("2 notes", vm.NoteCountText);

        vm.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.Resolved);
        Assert.Equal("Resolved notes", vm.ListHeadingText);
        Assert.Equal("1 note", vm.NoteCountText);

        vm.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.All);
        Assert.Equal("All notes", vm.ListHeadingText);
        Assert.Equal("3 notes", vm.NoteCountText);
    }

    [Theory]
    [InlineData(null, "No additional detail entered.", false)]
    [InlineData("   ", "No additional detail entered.", false)]
    [InlineData("Line one.\nLine two.", "Line one. Line two.", true)]
    public void NoteRow_Excerpt_CollapsesWhitespace_OrSaysThereIsNoDetail(string? body, string expected, bool hasExcerpt)
    {
        var row = OperationalNoteRowPresentation.Build(Note("T", body), Today);
        Assert.Equal(expected, row.ExcerptText);
        Assert.Equal(hasExcerpt, row.HasExcerpt);
    }

    [Fact]
    public void NoteRow_Excerpt_IsBounded_AndTheScheduleLineDropsTheLifecycleWordTheRowShowsAsAPill()
    {
        var row = OperationalNoteRowPresentation.Build(Note("T", new string('x', 400), from: -2, through: 5), Today);
        Assert.Equal(OperationalNoteRowPresentation.ExcerptLength + 1, row.ExcerptText.Length);
        Assert.EndsWith("…", row.ExcerptText, StringComparison.Ordinal);

        Assert.StartsWith("Active · ", row.ScheduleText, StringComparison.Ordinal);
        Assert.Equal(row.ScheduleText["Active · ".Length..], row.ScheduleDetailText);
        Assert.False(row.IsResolvedLifecycle);
        Assert.True(OperationalNoteRowPresentation.Build(Note("R", resolved: true), Today).IsResolvedLifecycle);
    }

    [Fact]
    public async Task NotesEditor_CancelWithChanges_SavesNothing_AndResolveNeedsConfirmation()
    {
        var note = Note("Original");
        var (vm, service) = await Notes(note);
        vm.SelectedNote = vm.Notes.Single();

        vm.EditCommand.Execute(null);
        vm.EditorTitle = "Changed but not saved";
        vm.CancelCommand.Execute(null);
        Assert.False(vm.IsEditing);
        Assert.Equal("Original", (await service.ListAsync()).Single().Title);

        vm.RequestResolveCommand.Execute(null); // asks first; nothing is resolved yet
        Assert.True(vm.IsConfirmingResolve);
        Assert.Equal(OperationalNoteStatus.Active, (await service.ListAsync()).Single().Status);
        vm.CancelResolveCommand.Execute(null);
        Assert.Equal(OperationalNoteStatus.Active, (await service.ListAsync()).Single().Status);
    }

    [Fact]
    public void NotesMarkup_EditorIsAnInPageDialog_ThatEscapeCancels_AndThePageIsDisabledBehindIt()
    {
        var markup = Markup("OperationalNotesView");

        Assert.Contains("<Grid x:Name=\"NotesPage\" RowDefinitions=\"Auto,Auto,*\" IsEnabled=\"{Binding !IsEditing}\">", markup);
        Assert.Contains("<Border x:Name=\"NoteEditorOverlay\" Classes=\"modalScrim\" IsVisible=\"{Binding ShowEditor}\">", markup);
        Assert.Contains("<KeyBinding Gesture=\"Escape\" Command=\"{Binding CancelCommand}\" />", markup);
        Assert.Contains("AutomationProperties.Name=\"Close editor without saving\"", markup);
        Assert.Contains("<Button Content=\"Save Note\" Classes=\"primaryAction\" Command=\"{Binding SaveCommand}\"", markup);
        Assert.Contains("x:Name=\"EditorTitleBox\"", markup);
        Assert.Contains("Text=\"{Binding SearchText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Search operational notes\"", markup);

        // Reopen is not offered: the service has no reopen operation (spec Part R deferred it) — documented in 35F.
        Assert.DoesNotContain("Reopen", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Reopen", string.Join(' ', typeof(IOperationalNotesService).GetMethods().Select(m => m.Name)), StringComparison.Ordinal);

        // Focus moves into the dialog and back (presentation only).
        var codeBehind = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/OperationalNotesView.axaml.cs"));
        Assert.Contains("EditorTitleBox.Focus", codeBehind);
        Assert.DoesNotContain("Command", codeBehind.Replace("CommandParameter", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OperationalNotesView")]
    [InlineData("HistoryView")]
    [InlineData("PersonalSyncSettingsView")]
    public void Views_NeverUseLineBreakInlines(string view) =>
        // A LineBreak inline sends the headless test font stack into unbounded memory growth (seen at ~63 GB in 35F).
        Assert.DoesNotContain("<LineBreak", Markup(view), StringComparison.Ordinal);

    // ---------------------------------------------------------------- Passdown History

    private static FinalizedPassdownSummary Summary(DateOnly date, FinalizationOrigin origin = FinalizationOrigin.Scheduled, bool pdf = true) =>
        new(date, new DateTimeOffset(date.AddDays(1).ToDateTime(new TimeOnly(11, 2)), TimeSpan.Zero), origin, pdf, pdf ? 4 : null);

    private static async Task<(PassdownHistoryViewModel Vm, FakePassdownHistoryService Service)> History(params FinalizedPassdownSummary[] summaries)
    {
        var service = new FakePassdownHistoryService { Summaries = [.. summaries] };
        foreach (var s in summaries)
        {
            service.Details[s.PassdownDate] = new PassdownHistoryDetail(s, null, null);
        }

        var vm = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService(), pdfSavePicker: new FakePdfSaveFilePicker(), pdfLauncher: new FakeFinalizedPdfLauncher());
        await vm.PendingLoad;
        return (vm, service);
    }

    [Fact]
    public async Task HistoryFilters_SearchOriginAndYear_NarrowInMemory_WithoutAnyRead()
    {
        var (vm, service) = await History(
            Summary(new DateOnly(2026, 10, 16)),
            Summary(new DateOnly(2026, 10, 10), FinalizationOrigin.Recovery),
            Summary(new DateOnly(2025, 12, 31), FinalizationOrigin.Manual, pdf: false));
        var lists = service.ListCallCount;

        Assert.Equal(["All years", "2026", "2025"], vm.YearOptions.Select(o => o.Label));
        Assert.Equal("3 records", vm.RecordCountText);
        Assert.True(vm.HasManualRecords);

        vm.SearchText = "Oct 10, 2026";
        Assert.Equal([new DateOnly(2026, 10, 10)], vm.HistoryItems.Select(i => i.PassdownDate));
        vm.SearchText = "friday october";
        Assert.Equal([new DateOnly(2026, 10, 16)], vm.HistoryItems.Select(i => i.PassdownDate));
        vm.SearchText = "pdf unavailable";
        Assert.Equal([new DateOnly(2025, 12, 31)], vm.HistoryItems.Select(i => i.PassdownDate));
        vm.SearchText = "2026-10-16";
        Assert.Single(vm.HistoryItems);
        vm.SearchText = string.Empty;

        vm.SetOriginFilterCommand.Execute(FinalizationOrigin.Recovery);
        Assert.True(vm.IsRecoverySelected);
        Assert.Equal([new DateOnly(2026, 10, 10)], vm.HistoryItems.Select(i => i.PassdownDate));
        vm.SetOriginFilterCommand.Execute(null);
        Assert.True(vm.IsAllOriginsSelected);

        vm.YearFilter = vm.YearOptions.Single(o => o.Year == 2025);
        Assert.Equal("1 record", vm.RecordCountText);
        vm.SetOriginFilterCommand.Execute(FinalizationOrigin.Scheduled);
        Assert.Empty(vm.HistoryItems);
        Assert.True(vm.ShowFilteredEmptyState);
        Assert.False(vm.ShowEmptyState);
        Assert.True(vm.HasHistory);

        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(3, vm.HistoryItems.Count);
        Assert.False(vm.HasActiveFilters);

        Assert.Equal(lists, service.ListCallCount);
        Assert.Empty(service.GetCalls); // filtering never opens a record
    }

    [Fact]
    public async Task HistoryFilters_HidingTheSelectedRecord_DeselectsIt_AndTheCalendarRouteClearsFiltersToShowIt()
    {
        var a = new DateOnly(2026, 10, 16);
        var b = new DateOnly(2026, 10, 10);
        var (vm, _) = await History(Summary(a), Summary(b, FinalizationOrigin.Recovery));
        vm.SelectedItem = vm.HistoryItems[0];
        await vm.PendingDetailLoad;

        vm.SetOriginFilterCommand.Execute(FinalizationOrigin.Recovery);
        Assert.Null(vm.SelectedItem);

        await vm.SelectByDateAsync(a); // the Calendar's "View Finalized History" for a date the filter hides
        Assert.False(vm.HasActiveFilters);
        Assert.Equal(a, vm.SelectedItem?.PassdownDate);
    }

    [Fact]
    public async Task HistorySaveCopy_HandsThePickerTheStoredBytesUnchanged()
    {
        var date = new DateOnly(2026, 10, 16);
        var stored = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        var service = new FakePassdownHistoryService { Summaries = [Summary(date)] };
        service.Details[date] = new PassdownHistoryDetail(service.Summaries[0], null, null);
        var retrieval = new FakeFinalizedPassdownPdfRetrievalService
        {
            ResultFactory = d => new FinalizedPassdownPdfRetrievalResult(stored, "x.pdf",
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stored)), stored.LongLength),
        };
        var picker = new FakePdfSaveFilePicker();
        var vm = new PassdownHistoryViewModel(service, retrieval, pdfSavePicker: picker, pdfLauncher: new FakeFinalizedPdfLauncher());
        await vm.PendingLoad;
        vm.SelectedItem = vm.HistoryItems.Single();
        await vm.PendingDetailLoad;

        await vm.SavePdfCopyCommand.ExecuteAsync(null);

        var call = Assert.Single(picker.Calls);
        Assert.Same(stored, call.Bytes);
        Assert.Equal([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37], call.Bytes);
        Assert.Equal("PDF saved.", vm.PdfActionMessage);
    }

    [Fact]
    public void HistoryMarkup_OffersOnlyTheStoredPdf_NoRegenerateOrEdit()
    {
        var markup = Markup("HistoryView");
        foreach (var forbidden in new[] { "Regenerate", "Rebuild", "Generate PDF", "Create PDF", "Edit", "Refinalize", "FinalizeCommand", "TextBox Text=\"{Binding Selected" })
        {
            Assert.DoesNotContain(forbidden, markup.Replace("Read Only", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        Assert.Equal(2, Regex.Matches(markup, "Command=\"\\{Binding (OpenFinalPdfCommand|SavePdfCopyCommand)\\}\"").Count);
        Assert.Contains("Text=\"{Binding SearchText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Search finalized passdowns\"", markup);
        Assert.Contains("ItemsSource=\"{Binding YearOptions}\" SelectedItem=\"{Binding YearFilter}\"", markup);
        Assert.Contains("Read-only historical snapshot.", markup);
    }

    // ---------------------------------------------------------------- Settings

    [Fact]
    public async Task AnimationsSwitch_WritesTheSameSavedChoice_AndWindowsReducedMotionNeverFlipsIt()
    {
        var (controller, store, os) = Phase35AInterfaceMotionTests.Create(InterfaceAnimationPreference.On);
        var appearance = new AppearanceSettingsViewModel(motion: controller);
        Assert.True(appearance.AreAnimationsOn);

        appearance.AreAnimationsOn = false;
        await appearance.PendingMotionSave;
        Assert.Equal(InterfaceAnimationPreference.Off, store.Stored);
        Assert.True(appearance.IsAnimationsOffSelected);

        appearance.AreAnimationsOn = true;
        await appearance.PendingMotionSave;
        Assert.Equal(InterfaceAnimationPreference.On, store.Stored);

        os.Value = SystemReducedMotion.Requested;
        appearance.RefreshSystemMotion();
        Assert.True(appearance.AreAnimationsOn); // the saved choice stays On
        Assert.False(appearance.IsMotionEffective);
        Assert.Equal(InterfaceAnimationPreference.On, store.Stored);
        Assert.Equal(2, store.Saves.Count);
    }

    [Fact]
    public void SettingsMarkup_SectionListPointsAtRealGroups_InPageOrder_AndScrollsOnePage()
    {
        var markup = Markup("PersonalSyncSettingsView");
        var targets = Regex.Matches(markup, "Classes=\"settingsNavItem[^\"]*\" Content=\"([^\"]+)\" Tag=\"([^\"]+)\"").Select(m => (Label: m.Groups[1].Value, Name: m.Groups[2].Value)).ToList();

        Assert.Equal(["Appearance", "Passdown Defaults", "Calendar", "Data Refresh", "Personal Sync", "Diagnostics", "Application Updates", "About"], targets.Select(t => t.Label));
        var positions = targets.Select(t => markup.IndexOf($"x:Name=\"{t.Name}\" Classes=\"panel\"", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.Single(Regex.Matches(markup, "<ScrollViewer "));
        Assert.Contains("<Setter Property=\"AutomationProperties.ItemStatus\" Value=\"Current section\" />", markup);

        // The section list only scrolls; it never touches a setting.
        var codeBehind = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml.cs"));
        foreach (var forbidden in new[] { "PersonalSyncEnabled", "ShowPassPerks", "Appearance.", "Save", "Command" })
        {
            Assert.DoesNotContain(forbidden, codeBehind.Replace("AppearanceOptions", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsMarkup_SwitchesBindTheExistingSettings_AndSyncStatusIsNeverAssumed()
    {
        var markup = Markup("PersonalSyncSettingsView");
        Assert.Contains("IsChecked=\"{Binding PersonalSyncEnabled}\"", markup);
        Assert.Contains("IsChecked=\"{Binding CalendarDisplay.ShowPassPerks}\"", markup);
        Assert.Contains("IsChecked=\"{Binding Appearance.AreAnimationsOn}\"", markup);
        Assert.Equal(3, Regex.Matches(markup, "<ToggleSwitch ").Count);

        // Status text only from the real folder / coordinator state — no literal "Connected" claim.
        Assert.DoesNotContain("Connected", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding StatusPresentation.Text}", markup);
        Assert.Contains("{Binding AutomationStatusPresentation.Text}", markup);
    }
}
