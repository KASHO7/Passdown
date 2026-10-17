using System.Globalization;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22E: Operational Notes and History presentation — denser note rows with the same facts, the
/// restrained Important treatment, created/updated times, "no important notes" wording, and History rows
/// that are easy to scan while finalized records stay read-only. Presentation, view-model and markup only.
/// </summary>
public sealed class Phase22ENotesHistoryTests
{
    private static readonly DateTimeOffset TodayInstant = new(2026, 10, 17, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static OperationalNote Note(
        string title = "Note",
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        int fromOffset = 0,
        int? throughOffset = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active,
        DateTimeOffset? created = null,
        DateTimeOffset? updated = null,
        OperationalNoteCategory category = OperationalNoteCategory.General) =>
        new(Guid.NewGuid(), title, null, category, priority, Today.AddDays(fromOffset),
            throughOffset is { } t ? Today.AddDays(t) : null, status,
            created ?? TodayInstant.AddDays(-3), updated ?? created ?? TodayInstant.AddDays(-3),
            status == OperationalNoteStatus.Resolved ? TodayInstant : null);

    // ---------------------------------------------------------------- note rows

    [Fact]
    public void ActiveRangeRow_ReadsLifecycleThenRange_WithoutRepeatingTheEndDate()
    {
        var row = OperationalNoteRowPresentation.Build(Note(fromOffset: -2, throughOffset: 5), Today);

        Assert.Equal("Through Oct 22", row.CarryForwardText); // still computed, as before
        Assert.Equal("Active · Oct 15 – Oct 22", row.ScheduleText);
    }

    [Theory]
    [InlineData(0, "Active · Oct 17 – Oct 17 · Expires today")]
    [InlineData(1, "Active · Oct 17 – Oct 18 · Expires tomorrow")]
    public void ExpiringSoon_KeepsTheRelativePhrase(int throughOffset, string expected) =>
        Assert.Equal(expected, OperationalNoteRowPresentation.Build(Note(throughOffset: throughOffset), Today).ScheduleText);

    [Fact]
    public void UntilResolved_IsSaidOnce() =>
        Assert.Equal("Active · Oct 15 – Until resolved", OperationalNoteRowPresentation.Build(Note(fromOffset: -2), Today).ScheduleText);

    [Theory]
    [InlineData(1, "Upcoming · Starts Oct 18 (tomorrow)")]
    [InlineData(4, "Upcoming · Starts Oct 21")]
    public void Upcoming_ShowsTheStartDate(int fromOffset, string expected) =>
        Assert.Equal(expected, OperationalNoteRowPresentation.Build(Note(fromOffset: fromOffset, throughOffset: fromOffset + 2), Today).ScheduleText);

    [Fact]
    public void Expired_AndResolved_ReadAsTheirLifecycle()
    {
        Assert.Equal("Expired · Oct 10 – Oct 14", OperationalNoteRowPresentation.Build(Note(fromOffset: -7, throughOffset: -3), Today).ScheduleText);
        Assert.StartsWith("Resolved · ", OperationalNoteRowPresentation.Build(Note(status: OperationalNoteStatus.Resolved), Today).ScheduleText, StringComparison.Ordinal);
    }

    [Fact]
    public void Importance_IsTheSupervisorsSetting_SaidInWords()
    {
        var important = OperationalNoteRowPresentation.Build(Note("Scanner down", OperationalNotePriority.Important, throughOffset: 2), Today);
        var normal = OperationalNoteRowPresentation.Build(Note("URGENT: scanner down", OperationalNotePriority.Normal, throughOffset: 2), Today);

        Assert.True(important.IsImportant);
        Assert.Equal("Scanner down, Important, General, Active · Oct 17 – Oct 19", important.AccessibleSummary);

        // Text is never used to classify importance.
        Assert.False(normal.IsImportant);
        Assert.Equal("URGENT: scanner down, General, Normal, Active · Oct 17 – Oct 19", normal.AccessibleSummary);
    }

    // ---------------------------------------------------------------- timestamps

    private static string Stamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt", PresentationCulture.UnitedStates);

    [Fact]
    public void UneditedNote_ShowsOnlyItsCreatedTime()
    {
        var created = new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);
        var detail = OperationalNoteDetailPresentation.Build(Note(created: created, updated: created.AddSeconds(20)), Today);

        Assert.Equal($"Created {Stamp(created)}", detail.CreatedText);
        Assert.Null(detail.UpdatedText);
        Assert.Equal(detail.CreatedText, detail.RecordTimesText);
    }

    [Fact]
    public void EditedNote_ShowsCreatedAndUpdated()
    {
        var created = new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);
        var updated = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var detail = OperationalNoteDetailPresentation.Build(Note(created: created, updated: updated), Today);

        Assert.Equal($"Created {Stamp(created)} · Updated {Stamp(updated)}", detail.RecordTimesText);
    }

    [Fact]
    public void Timestamps_UseUsConventions_WhateverTheHostCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var created = new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);
            var detail = OperationalNoteDetailPresentation.Build(Note(created: created), Today);
            Assert.Matches(@"^Created Oct 12 at \d{1,2}:\d{2} (AM|PM)$", detail.CreatedText!);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task SavingAnEdit_ShowsTheUpdatedTime_AndKeepsTheStoredCreatedTime()
    {
        var created = new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);
        var note = Note("Original", throughOffset: 3, created: created);
        var service = new FakeOperationalNotesService { UtcNowFactory = () => TodayInstant };
        service.Seed(note);
        var viewModel = new OperationalNotesViewModel(service, new FakeClock(TodayInstant));
        await viewModel.PendingLoad;
        viewModel.SelectedNote = viewModel.Notes.Single();

        viewModel.EditCommand.Execute(null);
        viewModel.EditorTitle = "Edited title";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Edited title", viewModel.SelectedDetail!.Title);
        Assert.Equal($"Created {Stamp(created)} · Updated {Stamp(TodayInstant)}", viewModel.SelectedDetail.RecordTimesText);
        Assert.Equal("Active · Oct 17 – Oct 20", viewModel.Notes.Single().ScheduleText);
    }

    // ---------------------------------------------------------------- filters and empty states

    private static async Task<OperationalNotesViewModel> NotesAsync(params OperationalNote[] notes)
    {
        var service = new FakeOperationalNotesService();
        service.Seed(notes);
        var viewModel = new OperationalNotesViewModel(service, new FakeClock(TodayInstant));
        await viewModel.PendingLoad;
        return viewModel;
    }

    [Fact]
    public async Task ImportantFilter_WithNoMatches_SaysNoImportantNotes()
    {
        var viewModel = await NotesAsync(Note("Ordinary", throughOffset: 2));
        viewModel.PriorityFilter = OperationalNotePriorityFilterOption.All.Single(o => o.Value == OperationalNotePriority.Important);

        Assert.True(viewModel.ShowFilteredEmptyState);
        Assert.Equal("No active important operational notes. Try clearing filters.", viewModel.FilteredEmptyStateMessage);

        viewModel.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.All);
        Assert.Equal("No important operational notes. Try clearing filters.", viewModel.FilteredEmptyStateMessage);
    }

    [Fact]
    public async Task OtherEmptyStates_AreUnchanged()
    {
        var empty = await NotesAsync();
        Assert.True(empty.ShowEmptyState);
        Assert.False(empty.ShowFilteredEmptyState);

        var some = await NotesAsync(Note("Later", fromOffset: 3, throughOffset: 4));
        Assert.Equal("No active operational notes.", some.FilteredEmptyStateMessage);
        some.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.Resolved);
        Assert.Equal("No resolved operational notes.", some.FilteredEmptyStateMessage);
        some.PriorityFilter = OperationalNotePriorityFilterOption.All.Single(o => o.Value == OperationalNotePriority.Normal);
        Assert.Equal("No resolved operational notes. Try clearing filters.", some.FilteredEmptyStateMessage);
    }

    [Fact]
    public async Task Filters_StillSelectTheSameNotes()
    {
        var viewModel = await NotesAsync(
            Note("Active important", OperationalNotePriority.Important, -1, 2),
            Note("Active normal", OperationalNotePriority.Normal, -1, null),
            Note("Upcoming", OperationalNotePriority.Important, 3, 5),
            Note("Expired", OperationalNotePriority.Normal, -9, -2),
            Note("Resolved", OperationalNotePriority.Normal, -2, 4, OperationalNoteStatus.Resolved));

        Assert.Equal(["Active important", "Active normal"], viewModel.Notes.Select(n => n.Title).Order());
        viewModel.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.All);
        Assert.Equal(5, viewModel.Notes.Count);
        viewModel.PriorityFilter = OperationalNotePriorityFilterOption.All.Single(o => o.Value == OperationalNotePriority.Important);
        Assert.Equal(["Active important", "Upcoming"], viewModel.Notes.Select(n => n.Title).Order());
        viewModel.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.Upcoming);
        Assert.Equal(["Upcoming"], viewModel.Notes.Select(n => n.Title));
    }

    // ---------------------------------------------------------------- Dashboard → Notes navigation

    [Fact]
    public async Task DashboardViewNotes_OpensTheManager_WithItsFiltersAndAssociateDateUntouched()
    {
        var shell = await Phase22ENavigationShell.CreateAsync();
        shell.Notes.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.Resolved);
        var associateDate = shell.Associate.SelectedDate;

        shell.Dashboard.OpenOperationalNotesCommand.Execute(null);

        Assert.Equal(AppSection.OperationalNotes, shell.Main.SelectedSection);
        Assert.Equal(OperationalNoteLifecycleFilter.Resolved, shell.Notes.LifecycleFilter);
        Assert.Equal(associateDate, shell.Associate.SelectedDate);
    }

    [Fact]
    public async Task DashboardOpenNotes_OpensTodaysPassdownNotes()
    {
        var shell = await Phase22ENavigationShell.CreateAsync();
        shell.Associate.SelectedDate = Phase22ENavigationShell.Today.AddDays(7);
        await shell.Associate.PendingLoad;

        shell.Dashboard.OpenNotesCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(AppSection.AssociatePassdown, shell.Main.SelectedSection);
        Assert.Equal(AssociateSection.Notes, shell.Associate.SelectedSection);
        Assert.Equal(Phase22ENavigationShell.Today, shell.Associate.SelectedDate);
    }

    // ---------------------------------------------------------------- notes markup

    private static string Markup(string view) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}"));

    private static string Template(string markup, string dataType)
    {
        var start = markup.IndexOf($"<DataTemplate x:DataType=\"{dataType}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"template {dataType} not found");
        var template = markup[start..];
        return template[..template.IndexOf("</DataTemplate>", StringComparison.Ordinal)];
    }

    [Fact]
    public void NotesRow_ShowsEveryFact_AsDividedRowsWithoutChips()
    {
        var row = Template(Markup("OperationalNotesView.axaml"), "presentation:OperationalNoteRowPresentation");

        foreach (var binding in new[] { "{Binding Title}", "{Binding CategoryLabel}", "{Binding PriorityLabel}", "{Binding ScheduleText}", "{Binding AccessibleSummary}" })
        {
            Assert.Contains(binding, row);
        }

        Assert.Contains("Classes=\"importanceRule\" Classes.important=\"{Binding IsImportant}\"", row);
        Assert.Contains("Classes=\"priorityText\" Classes.important=\"{Binding IsImportant}\"", row);
        Assert.DoesNotContain("Classes=\"chip\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesScreen_KeepsItsFiltersEditorAndActions()
    {
        var markup = Markup("OperationalNotesView.axaml");

        foreach (var expected in new[]
        {
            "All notes filter", "Active notes filter", "Upcoming notes filter", "Expired notes filter", "Resolved notes filter",
            "Category filter", "Priority filter", "ClearFiltersCommand", "NewNoteCommand", "EditCommand", "RequestResolveCommand",
            "ConfirmResolveCommand", "SaveCommand", "CancelCommand", "EditorTitle", "EditorBody", "EditorPriority", "EditorCategory",
            "EditorUntilResolved", "SelectedDetail.RecordTimesText", "SelectedDetail.ResolvedAtText", "FilteredEmptyStateMessage",
            "No operational notes yet.", "spell:SpellCheck.IsEnabled=\"True\"",
        })
        {
            Assert.Contains(expected, markup);
        }

        Assert.Contains("<Style Selector=\"Border.importanceRule.important\">", markup);
        Assert.Contains("{DynamicResource PassdownAccentForegroundBrush}", markup);
    }

    // ---------------------------------------------------------------- History rows

    private static FinalizedPassdownSummary Summary(DateOnly date, DateTimeOffset finalizedUtc, bool hasPdf = true, FinalizationOrigin origin = FinalizationOrigin.Scheduled) =>
        new(date, finalizedUtc, origin, hasPdf, hasPdf ? 180_000 : null);

    [Fact]
    public void HistoryRow_LeadsWithTheWeekday_ThenFinalizedTimeAndOrigin()
    {
        var row = PassdownHistorySummaryPresentation.Build(Summary(new DateOnly(2026, 10, 16), new DateTimeOffset(2026, 10, 17, 10, 2, 0, TimeSpan.Zero)));

        Assert.Equal("Friday, October 16, 2026", row.ListTitleText);
        Assert.Equal("Finalized Oct 17, 6:02 AM · Scheduled", row.ListDetailText);
        Assert.Equal("PDF", row.PdfTagText);
        Assert.Equal("Friday, October 16, 2026, finalized Oct 17, 6:02 AM, Scheduled, PDF available", row.AccessibleSummary);

        // The pre-22E fields are unchanged.
        Assert.Equal("October 16, 2026", row.PassdownDateText);
        Assert.Equal("Finalized Oct 17, 2026 at 6:02 AM", row.FinalizedText);
        Assert.Equal("PDF available", row.PdfAvailabilityText);
    }

    [Fact]
    public void HistoryRow_ShowsTheFinalizedYear_OnlyAcrossAYearBoundary()
    {
        var row = PassdownHistorySummaryPresentation.Build(Summary(new DateOnly(2026, 12, 31), new DateTimeOffset(2027, 1, 1, 11, 2, 0, TimeSpan.Zero), origin: FinalizationOrigin.Manual));

        Assert.Equal("Finalized Jan 1, 2027, 6:02 AM · Manual", row.ListDetailText);
    }

    [Fact]
    public void HistoryRow_WithoutAFrozenPdf_SaysSo()
    {
        var row = PassdownHistorySummaryPresentation.Build(Summary(new DateOnly(2026, 10, 11), new DateTimeOffset(2026, 10, 12, 10, 2, 0, TimeSpan.Zero), hasPdf: false));

        Assert.Equal("No PDF", row.PdfTagText);
        Assert.EndsWith("Exact historical PDF unavailable", row.AccessibleSummary, StringComparison.Ordinal);
    }

    private static FinalizedPassdownSnapshot Snapshot(DateOnly date) => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion, date, date.ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates),
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "6:00 PM - 12:00 AM", "Open", null, null, "Verified", null),
        new FinalizedAttendanceSnapshot(38500, 21240),
        new FinalizedEarlyRideTimeSnapshot(false, null, []),
        new FinalizedDailyFeatureSnapshot("Question of the Day", "Text"),
        ["A frozen note."],
        [new FinalizedProductCategorySnapshot("Daily Admission", FinalizedProductSectionTier.KeySelling,
            [new FinalizedProductRowSnapshot("Military / First Responder", "From $40.00", null, null, null)])],
        false,
        "10/17/2026 6:00 AM");

    [Fact]
    public async Task SelectingAndOpeningARecord_ServesTheStoredBytes_AndOnlyEverReads()
    {
        var date = new DateOnly(2026, 10, 16);
        var service = new FakePassdownHistoryService { Summaries = [Summary(date, new DateTimeOffset(2026, 10, 17, 10, 2, 0, TimeSpan.Zero))] };
        var snapshot = Snapshot(date);
        service.Details[date] = new PassdownHistoryDetail(service.Summaries[0], snapshot, null);
        var stored = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        var retrieval = new FakeFinalizedPassdownPdfRetrievalService
        {
            ResultFactory = d => new FinalizedPassdownPdfRetrievalResult(stored, $"Carowinds-Operations-{d:yyyy-MM-dd}.pdf",
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stored)), stored.LongLength),
        };
        var launcher = new FakeFinalizedPdfLauncher();
        var viewModel = new PassdownHistoryViewModel(service, retrieval, pdfLauncher: launcher);
        await viewModel.PendingLoad;

        viewModel.SelectedItem = viewModel.HistoryItems.Single();
        await viewModel.PendingDetailLoad;
        await viewModel.OpenFinalPdfCommand.ExecuteAsync(null);

        Assert.Same(snapshot, viewModel.SelectedDetail!.Snapshot);
        Assert.Equal("From $40.00", viewModel.SelectedDetail.Snapshot!.ProductCategories[0].Products[0].EffectivePriceText);
        var opened = Assert.Single(launcher.Calls);
        Assert.Same(stored, opened.Bytes);
        Assert.Equal([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37], opened.Bytes);
        Assert.Equal([date], retrieval.Calls);
        Assert.Equal([date], service.GetCalls);
    }

    [Fact]
    public void HistorySeams_AreReadOnly_SoNothingHereCanRefinalizeOrRewrite()
    {
        Assert.Equal(["GetAsync", "ListAsync"], typeof(IPassdownHistoryService).GetMethods().Select(m => m.Name).Order());
        Assert.Equal(["GetPdfAsync"], typeof(IFinalizedPassdownPdfRetrievalService).GetMethods().Select(m => m.Name));
        Assert.DoesNotContain(typeof(PassdownHistoryViewModel).GetConstructors().Single().GetParameters(),
            p => p.ParameterType.Name.Contains("Finaliz", StringComparison.Ordinal) && !p.ParameterType.Name.Contains("Retrieval", StringComparison.Ordinal)
                 && !p.ParameterType.Name.Contains("Launcher", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- History markup

    [Fact]
    public void HistoryList_IsTwoLineRows_WithATextPdfTag_AndNoChips()
    {
        var row = Template(Markup("HistoryView.axaml"), "presentation:PassdownHistorySummaryPresentation");

        Assert.Contains("{Binding ListTitleText}", row);
        Assert.Contains("{Binding ListDetailText}", row);
        Assert.Contains("Text=\"{Binding PdfTagText}\" Classes=\"pdfTag\" Classes.missing=\"{Binding !HasFrozenPdf}\"", row);
        Assert.Contains("ToolTip.Tip=\"{Binding PdfAvailabilityText}\"", row);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleSummary}\"", row);
        Assert.DoesNotContain("Classes=\"chip\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryDetail_KeepsEveryFrozenField_AndPromotesOpenFinalPdf()
    {
        var markup = Markup("HistoryView.axaml");

        foreach (var binding in new[]
        {
            "SelectedItem.PassdownDateText", "SelectedItem.FinalizedText", "SelectedItem.OriginText", "SelectedItem.PdfAvailabilityText",
            "SelectedDetail.Snapshot.ParkHours.HeadlineText", "SelectedDetail.Snapshot.ParkHours.BadgeText", "SelectedDetail.Snapshot.ParkHours.NorthGateText",
            "SelectedDetail.Snapshot.ParkHours.AvenueText", "SelectedDetail.Snapshot.ParkHours.VerificationText", "SelectedDetail.Snapshot.ParkHours.PrivateBuyoutDetails",
            "SelectedDetail.Snapshot.Attendance.ProjectedAttendance", "SelectedDetail.Snapshot.Attendance.PreSolds",
            "SelectedDetail.Snapshot.DailyFeature.TypeLabel", "SelectedDetail.Snapshot.DailyFeature.Text",
            "SelectedDetail.Snapshot.EarlyRideTime.StartTimeText", "SelectedDetail.Snapshot.EarlyRideTime.Details", "SelectedDetail.Snapshot.EarlyRideTime.ParticipatingAttractions",
            "SelectedDetail.Snapshot.HasUnverifiedProductPricing", "SelectedDetail.Snapshot.ProductCategories", "{Binding EffectivePriceText}", "{Binding ComparisonPriceText",
            "{Binding Qualifier}", "{Binding Notes}", "SelectedDetail.Snapshot.Shows", "SelectedDetail.Snapshot.OperationalNotes",
            "DetailErrorMessage", "PdfActionMessage", "OpenFinalPdfCommand", "SavePdfCopyCommand", "Finalized — Read Only",
            "No finalized passdowns yet.",
        })
        {
            Assert.Contains(binding, markup);
        }

        Assert.Contains("<Button Content=\"Open Final PDF\" Classes=\"primaryAction\"", markup);

        // With no records at all, only the list's own empty state shows — no "select from the list" prompt.
        var prompt = markup[markup.IndexOf("Select a finalized passdown from the list", StringComparison.Ordinal)..];
        prompt = prompt[..prompt.IndexOf("</TextBlock>", StringComparison.Ordinal)];
        Assert.Contains("<Binding Path=\"!HasSelection\" />", prompt);
        Assert.Contains("<Binding Path=\"HasHistory\" />", prompt);
        Assert.True(markup.IndexOf("OpenFinalPdfCommand", StringComparison.Ordinal) < markup.IndexOf("SelectedDetail.Snapshot.ParkHours.HeadlineText", StringComparison.Ordinal),
            "the PDF actions sit with the record's identity, above the snapshot detail");
    }
}

/// <summary>A minimal app shell for the Phase 22E Dashboard → Notes navigation tests (fakes only, no window).</summary>
internal sealed class Phase22ENavigationShell
{
    public static readonly DateOnly Today = new(2026, 10, 17);

    public required MainViewModel Main { get; init; }

    public required OperationsDashboardViewModel Dashboard { get; init; }

    public required AssociateViewModel Associate { get; init; }

    public required OperationalNotesViewModel Notes { get; init; }

    public static async Task<Phase22ENavigationShell> CreateAsync()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero));
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var associate = new AssociateViewModel(new FakeAssociatePassdownService(), clock);
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), clock);
        var main = new MainViewModel(dashboard, new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock), associate, notes,
            new PassdownHistoryViewModel(new FakePassdownHistoryService(), new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
        await dashboard.PendingLoad;
        await associate.PendingLoad;
        await notes.PendingLoad;
        return new Phase22ENavigationShell { Main = main, Dashboard = dashboard, Associate = associate, Notes = notes };
    }
}
