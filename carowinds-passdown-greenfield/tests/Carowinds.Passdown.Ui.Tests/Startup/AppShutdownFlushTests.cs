using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests.Startup;

/// <summary>
/// Phase 15D: the Phase 15D audit of draft autosave under "app close shortly after edit" found no
/// flush-on-shutdown path existed — an edit made inside the Associate screen's 800ms debounce window
/// would be silently lost if the process exited before the debounce fired. <see cref="App.AttachShutdownFlush"/>
/// (wired in <c>OnFrameworkInitializationCompleted</c>) closes that gap; this exercises the actual
/// drain work, <see cref="App.FlushPendingAutosaveBeforeShutdownAsync"/>, directly against a real
/// <see cref="AssociateViewModel"/> — no real Avalonia desktop lifetime is needed for this part, only
/// the Avalonia-independent flush logic the shutdown handler calls into.
/// </summary>
public class AppShutdownFlushTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static MainViewModel CreateMainViewModel(FakeAssociatePassdownService associateService)
    {
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(FixedNow));
        var associate = new AssociateViewModel(associateService, new FakeClock(FixedNow));
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new FakeClock(FixedNow));
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Today, FixedNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        var history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncService(), new FakePersonalSyncConflictResolutionService());
        return new MainViewModel(dashboard, calendar, associate, notes, history, settings);
    }

    /// <summary>
    /// The exact scenario the audit flagged: an edit is made (scheduling the real 800ms debounce, not
    /// yet elapsed — this test never waits for or elapses it), then shutdown is requested immediately
    /// after. The committed value must reach <see cref="FakeAssociatePassdownService.SaveCalls"/>
    /// without the test ever waiting out the debounce, proving the flush drains synchronously rather
    /// than depending on the timer.
    /// </summary>
    [Fact]
    public async Task FlushPendingAutosaveBeforeShutdownAsync_EditMadeJustBeforeShutdown_IsNotLost()
    {
        var service = new FakeAssociatePassdownService();
        var mainViewModel = CreateMainViewModel(service);
        await mainViewModel.Associate.PendingLoad;

        mainViewModel.Associate.ProjectedAttendanceText = "9200";

        await App.FlushPendingAutosaveBeforeShutdownAsync(mainViewModel, NullLogger.Instance);

        Assert.Equal(9200, service.SaveCalls.Single().Draft.ProjectedAttendance);
    }

    [Fact]
    public async Task FlushPendingAutosaveBeforeShutdownAsync_NoPendingEdit_DoesNothing()
    {
        var service = new FakeAssociatePassdownService();
        var mainViewModel = CreateMainViewModel(service);
        await mainViewModel.Associate.PendingLoad;

        await App.FlushPendingAutosaveBeforeShutdownAsync(mainViewModel, NullLogger.Instance);

        Assert.Empty(service.SaveCalls);
    }

    /// <summary>
    /// §13's "DB busy/locked" scenario, hit at the worst possible moment (mid-shutdown-flush):
    /// <see cref="FakeAssociatePassdownService.SaveGates"/> holds the save open forever, simulating a
    /// database that never becomes available again. Without the timeout this task added,
    /// <c>AssociateViewModel.FlushPendingAutosaveAsync</c>'s retry loop would await that save forever
    /// and shutdown would never complete — this proves the bounded timeout actually returns instead,
    /// using a short override so the test itself doesn't wait out the real 5-second production budget.
    /// </summary>
    [Fact]
    public async Task FlushPendingAutosaveBeforeShutdownAsync_SaveNeverCompletes_TimesOutRatherThanHangingForever()
    {
        var service = new FakeAssociatePassdownService();
        service.SaveGates.Enqueue(new TaskCompletionSource<bool>()); // never completed
        var mainViewModel = CreateMainViewModel(service);
        await mainViewModel.Associate.PendingLoad;

        mainViewModel.Associate.ProjectedAttendanceText = "9200";

        var flushTask = App.FlushPendingAutosaveBeforeShutdownAsync(mainViewModel, NullLogger.Instance, timeout: TimeSpan.FromMilliseconds(50));
        var completed = await Task.WhenAny(flushTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(flushTask, completed);
        await flushTask; // must not throw — the timeout path is swallowed, not propagated
    }
}
