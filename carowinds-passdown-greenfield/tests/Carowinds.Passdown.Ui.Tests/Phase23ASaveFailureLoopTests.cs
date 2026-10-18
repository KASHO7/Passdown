using System.Diagnostics;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 23A: the RC.1–RC.7 freeze. A failed draft save left the editor dirty, and every action that
/// flushed pending edits first (date change, Check Online, a note change, PDF, close) looped
/// <c>while (dirty) save()</c>; SQLite (and this fake) fail synchronously, so the loop never yielded
/// and pinned the UI thread. Every test here runs with <see cref="FakeAssociatePassdownService.SaveLoopGuardLimit"/>
/// set, so the old behavior fails fast with a <see cref="SaveLoopGuardException"/> instead of hanging
/// the test run. ViewModel-only (no real window); runs in the mixed Ui partition.
/// </summary>
public sealed class Phase23ASaveFailureLoopTests(ITestOutputHelper output)
{
    private const string FailedText = "Save failed — changes not saved";
    private const int LoopGuard = 500;

    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        AssociateViewModel ViewModel,
        FakeAssociatePassdownService Service,
        ManualDebounceScheduler Scheduler,
        FakeAssociatePassdownPdfExportService PdfExport);

    private static async Task<Harness> LoadedAsync(Action<FakeAssociatePassdownService>? configure = null)
    {
        var service = new FakeAssociatePassdownService { SaveLoopGuardLimit = LoopGuard };
        configure?.Invoke(service);
        var scheduler = new ManualDebounceScheduler();
        var pdfExport = new FakeAssociatePassdownPdfExportService();
        var viewModel = new AssociateViewModel(
            service,
            new FakeClock(FixedNow),
            autosaveDelay: scheduler.Delay,
            pdfExportService: pdfExport,
            pdfSavePicker: new FakePdfSaveFilePicker());
        await viewModel.PendingLoad;
        return new Harness(viewModel, service, scheduler, pdfExport);
    }

    /// <summary>Loads, makes an edit, and lets its autosave fail once with a persistent storage error.</summary>
    private static async Task<Harness> FailedAsync(string attendance = "38,500", Action<FakeAssociatePassdownService>? configure = null)
    {
        var h = await LoadedAsync(configure);
        h.Service.SaveException = new IOException("disk I/O error");
        h.ViewModel.ProjectedAttendanceText = attendance;
        h.Scheduler.ElapseLatest();
        await h.ViewModel.PendingAutosave;
        Assert.Equal(AssociateAutosaveState.SaveFailed, h.ViewModel.AutosaveState);
        Assert.Single(h.Service.SaveCalls);
        return h;
    }

    private static OperationalNote ScannerNote() => new(
        Guid.NewGuid(), "South Gate scanner #3 out of service", "Direct guests to gate 2.",
        OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
        Today, null, OperationalNoteStatus.Active, FixedNow.AddDays(-1), FixedNow.AddDays(-1), null);

    private static void AssertStillFailedWithEdits(Harness h, string attendance)
    {
        Assert.Equal(AssociateAutosaveState.SaveFailed, h.ViewModel.AutosaveState);
        Assert.Equal(FailedText, h.ViewModel.SaveStatusText);
        Assert.Equal("disk I/O error", h.ViewModel.SaveFailureReason);
        Assert.Equal(attendance, h.ViewModel.ProjectedAttendanceText);
        Assert.Null(h.Service.StoredDraft(Today)); // nothing was ever persisted
    }

    // ---- Date navigation ----------------------------------------------------------------------------

    [Fact]
    public async Task DateChange_AfterAFailedSave_MakesOneAttempt_NotALoop()
    {
        var h = await FailedAsync();

        h.ViewModel.NextDayCommand.Execute(null);
        await h.ViewModel.PendingLoad;

        Assert.Equal(2, h.Service.SaveCalls.Count); // the original failure + exactly one flush attempt
    }

    [Fact]
    public async Task DateChange_AfterAFailedSave_IsRefused_AndTheEditsStayOnTheirOwnDate()
    {
        var h = await FailedAsync();
        var loadsBefore = h.Service.LoadCalls.Count;

        h.ViewModel.NextDayCommand.Execute(null);
        await h.ViewModel.PendingLoad;

        Assert.Equal(Today, h.ViewModel.SelectedDate);
        Assert.Equal(Today, h.ViewModel.Workspace!.OperatingDate);
        Assert.Equal(loadsBefore, h.Service.LoadCalls.Count); // the other date was never loaded over the edits
        Assert.Equal(AssociateViewModel.DateSwitchBlockedMessage, h.ViewModel.SaveBlockedMessage);
        AssertStillFailedWithEdits(h, "38,500");
        Assert.All(h.Service.SaveCalls, call => Assert.Equal(Today, call.OperatingDate)); // never another date's record
    }

    [Fact]
    public async Task RepeatedDateNavigation_CostsAtMostOneAttemptPerClick_AndNeverResetsTheFailure()
    {
        var h = await FailedAsync();

        for (var i = 0; i < 25; i++)
        {
            if (i % 2 == 0)
            {
                h.ViewModel.NextDayCommand.Execute(null);
            }
            else
            {
                h.ViewModel.PreviousDayCommand.Execute(null);
            }

            await h.ViewModel.PendingLoad;
        }

        Assert.Equal(1 + 25, h.Service.SaveCalls.Count);
        Assert.Equal(Today, h.ViewModel.SelectedDate);
        AssertStillFailedWithEdits(h, "38,500");
    }

    [Fact]
    public async Task TodayAndPickerNavigation_AreRefusedTheSameWay()
    {
        var h = await FailedAsync();

        h.ViewModel.SelectedDateForPicker = new DateTime(2026, 10, 30);
        await h.ViewModel.PendingLoad;
        Assert.Equal(Today, h.ViewModel.SelectedDate);

        h.ViewModel.SelectedDate = Today.AddDays(-3);
        await h.ViewModel.PendingLoad;
        h.ViewModel.GoToTodayCommand.Execute(null);
        await h.ViewModel.PendingLoad;

        Assert.Equal(Today, h.ViewModel.SelectedDate);
        AssertStillFailedWithEdits(h, "38,500");
    }

    [Fact]
    public async Task DateChange_WithAPendingButUnfailedEdit_StillFlushesAndSwitches()
    {
        var h = await LoadedAsync();
        h.ViewModel.ProjectedAttendanceText = "9,200";

        h.ViewModel.NextDayCommand.Execute(null);
        await h.ViewModel.PendingLoad;

        Assert.Equal(Today.AddDays(1), h.ViewModel.SelectedDate);
        Assert.Equal(9200, h.Service.StoredDraft(Today)!.ProjectedAttendance);
        Assert.Null(h.ViewModel.SaveBlockedMessage);
    }

    // ---- Check Online / official data -----------------------------------------------------------------

    [Fact]
    public async Task CheckOnline_DuringAFailure_MakesNoNetworkRequest_AndKeepsTheEdits()
    {
        var h = await FailedAsync();

        await h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);
        await h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Empty(h.Service.CheckOnlineCalls);
        Assert.Equal(3, h.Service.SaveCalls.Count); // one attempt per press
        Assert.Equal(AssociateViewModel.CheckOnlineBlockedMessage, h.ViewModel.SaveBlockedMessage);
        Assert.False(h.ViewModel.IsCheckingOnline);
        Assert.True(h.ViewModel.CheckOnlineCommand.CanExecute(null));
        AssertStillFailedWithEdits(h, "38,500");
    }

    [Fact]
    public async Task CheckOnline_AfterRecovery_RunsNormally()
    {
        var h = await FailedAsync();
        await h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);
        h.Service.SaveException = null;

        await h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Single(h.Service.CheckOnlineCalls);
        Assert.Equal(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
        Assert.Null(h.ViewModel.SaveBlockedMessage);
        Assert.Equal(38500, h.Service.StoredDraft(Today)!.ProjectedAttendance);
    }

    [Fact]
    public async Task NewerOfficialData_DuringAFailure_DoesNotReplaceTheEdits_OrClearTheFailure()
    {
        var h = await FailedAsync();
        var loadsBefore = h.Service.LoadCalls.Count;

        await h.ViewModel.HandleOfficialDataChangedAsync(new DataChange(DataChangeKind.OfficialData, Today));

        Assert.Equal(loadsBefore, h.Service.LoadCalls.Count);
        Assert.Single(h.Service.SaveCalls); // a refresh never triggers a save
        AssertStillFailedWithEdits(h, "38,500");
    }

    // ---- Notes ----------------------------------------------------------------------------------------

    [Fact]
    public async Task NotesEditing_DuringAFailure_KeepsTheText_AndRetriesOncePerDebounce()
    {
        var h = await FailedAsync();

        h.ViewModel.OperationalNotesText = "Gate 2 scanner down";
        h.ViewModel.OperationalNotesText = "Gate 2 scanner down — use gate 3";
        h.Scheduler.ElapseLatest();
        await h.ViewModel.PendingAutosave;

        Assert.Equal(2, h.Service.SaveCalls.Count); // two keystrokes, one debounced attempt
        Assert.Equal("Gate 2 scanner down — use gate 3", h.ViewModel.OperationalNotesText);
        AssertStillFailedWithEdits(h, "38,500");
    }

    [Fact]
    public async Task AddingAPersistentNote_DuringAFailure_IsHeldBack_NotLooped()
    {
        var note = ScannerNote();
        var h = await FailedAsync(configure: s => s.RelevantOperationalNotesByDate[Today] = [note]);

        await h.ViewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);

        Assert.Empty(h.Service.SelectOperationalNoteCalls);
        Assert.Equal(2, h.Service.SaveCalls.Count);
        Assert.Equal(AssociateViewModel.NoteChangeBlockedMessage, h.ViewModel.SaveBlockedMessage);
        AssertStillFailedWithEdits(h, "38,500");
    }

    [Fact]
    public async Task AddingAPersistentNote_AfterRecovery_Works_AndKeepsTheEarlierEdit()
    {
        var note = ScannerNote();
        var h = await FailedAsync(configure: s => s.RelevantOperationalNotesByDate[Today] = [note]);
        h.Service.SaveException = null;

        await h.ViewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);

        Assert.Single(h.Service.SelectOperationalNoteCalls);
        Assert.Single(h.ViewModel.SelectedOperationalNotes);
        Assert.Equal(38500, h.Service.StoredDraft(Today)!.ProjectedAttendance);
        Assert.Equal(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
    }

    // ---- Other edits: attendance, pre-solds, Shows --------------------------------------------------

    [Fact]
    public async Task AttendancePreSoldsAndShows_EditedDuringAFailure_AreAllKept_AndSavedTogetherOnRecovery()
    {
        var h = await FailedAsync();

        h.ViewModel.PreSoldsText = "1,250";
        h.ViewModel.IncludeShows = !h.ViewModel.IncludeShows;
        var includeShows = h.ViewModel.IncludeShows;
        h.ViewModel.ProjectedAttendanceText = "40,000";
        h.Scheduler.ElapseLatest();
        await h.ViewModel.PendingAutosave;
        Assert.Equal(2, h.Service.SaveCalls.Count);
        Assert.Equal("1,250", h.ViewModel.PreSoldsText);

        h.Service.SaveException = null;
        await h.ViewModel.RetrySaveCommand.ExecuteAsync(null);

        var stored = h.Service.StoredDraft(Today)!;
        Assert.Equal(40000, stored.ProjectedAttendance);
        Assert.Equal(1250, stored.PreSolds);
        Assert.Equal(includeShows, stored.Shows?.IncludeInPassdown ?? false);
    }

    // ---- Explicit retry and recovery ------------------------------------------------------------------

    [Fact]
    public async Task RetrySave_IsOfferedOnlyAfterAFailure()
    {
        var h = await LoadedAsync();
        Assert.False(h.ViewModel.RetrySaveCommand.CanExecute(null));
        Assert.False(h.ViewModel.IsSaveFailed);

        h.ViewModel.ProjectedAttendanceText = "1";
        Assert.False(h.ViewModel.RetrySaveCommand.CanExecute(null)); // an ordinary pending edit is not a failure

        h.Service.SaveException = new IOException("disk I/O error");
        h.Scheduler.ElapseLatest();
        await h.ViewModel.PendingAutosave;

        Assert.True(h.ViewModel.RetrySaveCommand.CanExecute(null));
        Assert.True(h.ViewModel.IsSaveFailed);
    }

    [Fact]
    public async Task RetrySave_WhileStillFailing_MakesExactlyOneAttemptPerPress()
    {
        var h = await FailedAsync();

        for (var i = 0; i < 10; i++)
        {
            await h.ViewModel.RetrySaveCommand.ExecuteAsync(null);
        }

        Assert.Equal(11, h.Service.SaveCalls.Count);
        AssertStillFailedWithEdits(h, "38,500");
    }

    [Fact]
    public async Task RetrySave_AfterTheStorageRecovers_PersistsAndClearsEverything()
    {
        var h = await FailedAsync();
        h.ViewModel.NextDayCommand.Execute(null);
        await h.ViewModel.PendingLoad;
        Assert.NotNull(h.ViewModel.SaveBlockedMessage);

        h.Service.SaveException = null;
        await h.ViewModel.RetrySaveCommand.ExecuteAsync(null);

        Assert.Equal(38500, h.Service.StoredDraft(Today)!.ProjectedAttendance);
        Assert.Equal(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
        Assert.Null(h.ViewModel.SaveFailureReason);
        Assert.Null(h.ViewModel.SaveStatusDetailText);
        Assert.Null(h.ViewModel.SaveBlockedMessage);
        Assert.False(h.ViewModel.RetrySaveCommand.CanExecute(null));

        h.ViewModel.NextDayCommand.Execute(null); // and navigation works again
        await h.ViewModel.PendingLoad;
        Assert.Equal(Today.AddDays(1), h.ViewModel.SelectedDate);
    }

    [Fact]
    public async Task ANewEdit_AfterTheStorageRecovers_RetriesThroughTheNormalDebounce()
    {
        var h = await FailedAsync();
        h.Service.SaveException = null;

        h.ViewModel.ProjectedAttendanceText = "38,600";
        Assert.Equal(FailedText, h.ViewModel.SaveStatusText); // still failed until the save is confirmed
        h.Scheduler.ElapseLatest();
        await h.ViewModel.PendingAutosave;

        Assert.Equal(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
        Assert.Equal(38600, h.Service.StoredDraft(Today)!.ProjectedAttendance);
    }

    [Fact]
    public async Task TheFailure_StaysVisible_ThroughNavigationRefreshAndSectionSwitches()
    {
        var h = await FailedAsync();

        h.ViewModel.NextDayCommand.Execute(null);
        await h.ViewModel.PendingLoad;
        await h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);
        h.ViewModel.ShowProductsSectionCommand.Execute(null);
        h.ViewModel.ShowNotesSectionCommand.Execute(null);
        h.ViewModel.ShowOverviewSectionCommand.Execute(null);

        AssertStillFailedWithEdits(h, "38,500");
        Assert.Contains("disk I/O error", h.ViewModel.SaveStatusDetailText, StringComparison.Ordinal);
        Assert.Contains("Retry Save", h.ViewModel.SaveStatusDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSwitching_NeverTriggersASave()
    {
        var h = await FailedAsync();

        for (var i = 0; i < 20; i++)
        {
            h.ViewModel.ShowProductsSectionCommand.Execute(null);
            h.ViewModel.ShowNotesSectionCommand.Execute(null);
            h.ViewModel.ShowOverviewSectionCommand.Execute(null);
        }

        Assert.Single(h.Service.SaveCalls);
    }

    // ---- Concurrency and stale completions --------------------------------------------------------

    [Fact]
    public async Task ConcurrentSaveTriggers_AreSerialized_AndBounded()
    {
        var h = await LoadedAsync();
        var gate = new TaskCompletionSource<bool>();
        h.Service.SaveGates.Enqueue(gate);
        h.Service.SaveException = new IOException("database is locked");
        h.ViewModel.ProjectedAttendanceText = "100";
        h.Scheduler.ElapseLatest(); // save #1 now held in flight

        // Everything else that can trigger a save, while #1 is still writing.
        h.ViewModel.ProjectedAttendanceText = "200";
        h.Scheduler.ElapseLatest();
        var pending = h.ViewModel.PendingAutosave;
        var check = h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);
        var pdf = h.ViewModel.CreatePdfCommand.ExecuteAsync(null);
        Assert.Single(h.Service.SaveCalls); // nobody overlapped the in-flight write

        gate.SetResult(true);
        await Task.WhenAll(pending, check, pdf);

        Assert.InRange(h.Service.SaveCalls.Count, 2, 4); // one per trigger at most — never a loop
        Assert.Empty(h.Service.CheckOnlineCalls);
        Assert.Empty(h.PdfExport.Calls);
        Assert.Equal("200", h.ViewModel.ProjectedAttendanceText);
        Assert.Equal(AssociateAutosaveState.SaveFailed, h.ViewModel.AutosaveState);
    }

    [Fact]
    public async Task AFailedInFlightSave_NeverOverwritesANewerSuccessfulEdit()
    {
        var h = await LoadedAsync();
        var gate = new TaskCompletionSource<bool>();
        h.Service.SaveGates.Enqueue(gate);
        h.Service.SaveException = new IOException("database is locked");
        h.ViewModel.ProjectedAttendanceText = "100";
        h.Scheduler.ElapseLatest(); // save of "100" held in flight; it will fail
        var inFlight = h.ViewModel.PendingAutosave;

        h.ViewModel.ProjectedAttendanceText = "200"; // newer edit typed meanwhile (its debounce never elapses here)
        gate.SetResult(true);
        await inFlight;
        Assert.Equal(AssociateAutosaveState.SaveFailed, h.ViewModel.AutosaveState);

        h.Service.SaveException = null;
        await h.ViewModel.RetrySaveCommand.ExecuteAsync(null);

        Assert.Equal(200, h.Service.StoredDraft(Today)!.ProjectedAttendance);
        Assert.Equal(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
        Assert.Equal(200, h.Service.SaveCalls[^1].Draft.ProjectedAttendance); // the last write was the newest edit
    }

    [Fact]
    public async Task AnEditTypedDuringASuccessfulSave_StaysDirty_AndIsNotMarkedSaved()
    {
        var h = await LoadedAsync();
        var gate = new TaskCompletionSource<bool>();
        h.Service.SaveGates.Enqueue(gate);
        h.ViewModel.ProjectedAttendanceText = "100";
        h.Scheduler.ElapseLatest();
        var inFlight = h.ViewModel.PendingAutosave;

        h.ViewModel.ProjectedAttendanceText = "200";
        gate.SetResult(true);
        await inFlight;

        Assert.NotEqual(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
        Assert.Equal(100, h.Service.StoredDraft(Today)!.ProjectedAttendance);
    }

    // ---- PDF ------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreatePdf_DuringAFailure_IsHeldBack_RatherThanOmittingTheEdits()
    {
        var h = await FailedAsync();

        await h.ViewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Empty(h.PdfExport.Calls);
        Assert.Equal(AssociateViewModel.PdfBlockedMessage, h.ViewModel.SaveBlockedMessage);
        Assert.False(h.ViewModel.IsExportingPdf);
        Assert.Equal(2, h.Service.SaveCalls.Count);
    }

    // ---- Closing --------------------------------------------------------------------------------------

    private static MainViewModel CreateMainViewModel(AssociateViewModel associate)
    {
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(FixedNow));
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new FakeClock(FixedNow));
        var history = new PassdownHistoryViewModel(
            new FakePassdownHistoryService { Summaries = [new FinalizedPassdownSummary(Today, FixedNow, FinalizationOrigin.Scheduled, true, 100)] },
            new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        return new MainViewModel(dashboard, calendar, associate, notes, history, settings);
    }

    [Fact]
    public async Task ShutdownFlush_DuringAPersistentFailure_ReturnsPromptly_AndReportsNotSaved()
    {
        var h = await FailedAsync();
        var main = CreateMainViewModel(h.ViewModel);

        var flush = App.FlushPendingAutosaveBeforeShutdownAsync(main, NullLogger.Instance);
        var completed = await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(flush, completed);
        Assert.False(await flush);
        Assert.Equal(2, h.Service.SaveCalls.Count); // one final attempt, not a loop
        Assert.Equal("38,500", h.ViewModel.ProjectedAttendanceText);
    }

    [Fact]
    public async Task ShutdownFlush_AfterRecovery_SavesAndReportsSaved()
    {
        var h = await FailedAsync();
        h.Service.SaveException = null;
        var main = CreateMainViewModel(h.ViewModel);

        Assert.True(await App.FlushPendingAutosaveBeforeShutdownAsync(main, NullLogger.Instance));
        Assert.Equal(38500, h.Service.StoredDraft(Today)!.ProjectedAttendance);
    }

    [Fact]
    public async Task ShutdownFlush_WithNothingPending_ReportsSaved()
    {
        var h = await LoadedAsync();
        var main = CreateMainViewModel(h.ViewModel);

        Assert.True(await App.FlushPendingAutosaveBeforeShutdownAsync(main, NullLogger.Instance));
        Assert.Empty(h.Service.SaveCalls);
    }

    [Fact]
    public async Task CloseWarning_ShowsTheAssociateScreen_WithAnHonestMessage_AndKeepsRetryAvailable()
    {
        var h = await FailedAsync();
        var main = CreateMainViewModel(h.ViewModel);
        main.SelectedSection = AppSection.Dashboard;

        main.ShowCloseBlockedBySaveFailure();

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateViewModel.CloseBlockedMessage, h.ViewModel.SaveBlockedMessage);
        Assert.Contains("could not be saved", h.ViewModel.SaveBlockedMessage, StringComparison.Ordinal);
        Assert.True(h.ViewModel.RetrySaveCommand.CanExecute(null));
        AssertStillFailedWithEdits(h, "38,500");
    }

    // ---- Markup -------------------------------------------------------------------------------------

    [Fact]
    public void AssociateMarkup_OffersRetrySave_AndShowsWhatWasHeldBack()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        Assert.Contains("x:Name=\"RetrySaveButton\" Content=\"Retry Save\"", markup);
        Assert.Contains("Command=\"{Binding RetrySaveCommand}\"", markup);
        Assert.Contains("IsVisible=\"{Binding IsSaveFailed}\"", markup);
        Assert.Contains("x:Name=\"SaveBlockedMessageText\" Text=\"{Binding SaveBlockedMessage}\"", markup);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", markup);
    }

    // ---- Stress / performance -------------------------------------------------------------------------

    /// <summary>
    /// A short, controlled version of the reported 400-second freeze: a storage failure that never
    /// recovers while the associate keeps working — 300 mixed actions (date changes, Check Online, note
    /// adds, PDF, edits, Retry Save). Save attempts must grow at most linearly with actions (never
    /// unboundedly), no debounce timers may accumulate, the network is never touched, and the whole run
    /// must finish quickly. Counts are written to the test output for the Phase 23A document.
    /// </summary>
    [Fact]
    public async Task Stress_PersistentFailure_UnderRepeatedActions_StaysBoundedAndResponsive()
    {
        var note = ScannerNote();
        var h = await FailedAsync(configure: s => s.RelevantOperationalNotesByDate[Today] = [note]);
        var stopwatch = Stopwatch.StartNew();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        const int Actions = 300;

        for (var i = 0; i < Actions; i++)
        {
            switch (i % 6)
            {
                case 0:
                    h.ViewModel.NextDayCommand.Execute(null);
                    await h.ViewModel.PendingLoad;
                    break;
                case 1:
                    await h.ViewModel.CheckOnlineCommand.ExecuteAsync(null);
                    break;
                case 2:
                    await h.ViewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);
                    break;
                case 3:
                    await h.ViewModel.CreatePdfCommand.ExecuteAsync(null);
                    break;
                case 4:
                    h.ViewModel.OperationalNotesText = $"note {i}";
                    h.Scheduler.ElapseLatest();
                    await h.ViewModel.PendingAutosave;
                    break;
                default:
                    await h.ViewModel.RetrySaveCommand.ExecuteAsync(null);
                    break;
            }
        }

        stopwatch.Stop();
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
        output.WriteLine($"actions={Actions} saveAttempts={h.Service.SaveCalls.Count} debounceTimersScheduled={h.Scheduler.ScheduleCount} " +
                         $"onlineChecks={h.Service.CheckOnlineCalls.Count} noteSelections={h.Service.SelectOperationalNoteCalls.Count} " +
                         $"pdfs={h.PdfExport.Calls.Count} elapsedMs={stopwatch.ElapsedMilliseconds} processCpuMs={cpu.TotalMilliseconds:F0}");

        Assert.True(h.Service.SaveCalls.Count <= 1 + Actions, $"save attempts {h.Service.SaveCalls.Count} exceeded one per action");
        Assert.True(h.Scheduler.ScheduleCount <= 1 + Actions / 6 + 1, $"debounce timers {h.Scheduler.ScheduleCount}");
        Assert.Empty(h.Service.CheckOnlineCalls);
        Assert.Empty(h.Service.SelectOperationalNoteCalls);
        Assert.Empty(h.PdfExport.Calls);
        Assert.Equal(Today, h.ViewModel.SelectedDate);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        Assert.Equal(AssociateAutosaveState.SaveFailed, h.ViewModel.AutosaveState);

        // ...and once the storage comes back, a single Retry Save recovers everything on screen.
        h.Service.SaveException = null;
        await h.ViewModel.RetrySaveCommand.ExecuteAsync(null);
        Assert.Equal(AssociateAutosaveState.Saved, h.ViewModel.AutosaveState);
        Assert.Equal(38500, h.Service.StoredDraft(Today)!.ProjectedAttendance);
        Assert.Equal([$"note {Actions - 2}"], h.Service.StoredDraft(Today)!.OperationalNotes);
    }

    /// <summary>
    /// With no user action at all, a persistent failure must schedule nothing further: no retry timer,
    /// no further save, however long the app sits there.
    /// </summary>
    [Fact]
    public async Task PersistentFailure_WithNoFurtherActions_SchedulesNoRetries()
    {
        var h = await FailedAsync();
        var timersAfterFailure = h.Scheduler.ScheduleCount;

        h.Scheduler.ElapseLatest(); // nothing pending to elapse
        await Task.Delay(50);
        await h.ViewModel.PendingAutosave;

        Assert.Single(h.Service.SaveCalls);
        Assert.Equal(timersAfterFailure, h.Scheduler.ScheduleCount);
    }
}
