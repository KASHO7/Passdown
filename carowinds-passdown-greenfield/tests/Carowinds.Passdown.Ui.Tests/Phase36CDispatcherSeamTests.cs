using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 36C (docs/phase36c-ert-editor-maintainability.md): the last three direct <c>Dispatcher.UIThread.Post</c> calls in
/// view models — the Associate and Operational Notes Personal Sync reloads and the Settings automatic-sync status — go
/// through the existing <see cref="IUiDispatcher"/> seam. In the app that seam is <see cref="AvaloniaUiDispatcher"/>, the
/// same <c>Dispatcher.UIThread.Post</c>, so production marshalling, priority and order are unchanged. These tests use a
/// queueing dispatcher to prove each background notification posts exactly one UI action, in order, and that nothing
/// changes until the UI thread runs it.
/// </summary>
public class Phase36CDispatcherSeamTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 21);

    /// <summary>Holds posted work until the test runs it, as the UI thread's queue would.</summary>
    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();

        public int Pending { get { lock (_queue) { return _queue.Count; } } }

        public void Post(Action action)
        {
            lock (_queue)
            {
                _queue.Enqueue(action);
            }
        }

        public int RunAll()
        {
            var ran = 0;
            while (true)
            {
                Action next;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        return ran;
                    }

                    next = _queue.Dequeue();
                }

                next();
                ran++;
            }
        }
    }

    /// <summary>
    /// Raises a notification on a thread-pool thread, as Personal Sync does, and waits for it synchronously so the test
    /// itself stays on this assembly's one test thread (AssemblyInfo.cs) — the queued UI action must not have run yet.
    /// </summary>
    private static void OnBackgroundThread(Action raise) => Task.Run(raise).Wait();

    private static void PublishFromBackgroundThread(SyncLocalChangeNotifier notifier, params SyncEntityKey[] keys) =>
        OnBackgroundThread(() => notifier.Publish(keys));

    [Fact]
    public void App_RegistersTheAvaloniaDispatcher_SoProductionStillPostsToTheUiThread()
    {
        var provider = App.BuildServiceProvider();

        Assert.IsType<AvaloniaUiDispatcher>(provider.GetRequiredService<IUiDispatcher>());
    }

    [Fact]
    public async Task Associate_SyncChange_PostsOneReloadThroughTheInjectedDispatcher_AndRunsOnlyWhenDispatched()
    {
        var notifier = new SyncLocalChangeNotifier();
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), syncLocalChangeNotifier: notifier, uiDispatcher: dispatcher);
        dispatcher.RunAll(); // anything the constructor itself posted
        await viewModel.PendingLoad;
        var loadsBefore = service.LoadCalls.Count;

        PublishFromBackgroundThread(notifier, new SyncEntityKey(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Today)));

        Assert.Equal(1, dispatcher.Pending);                 // exactly one UI action per notification
        Assert.Equal(loadsBefore, service.LoadCalls.Count);  // nothing ran on the publishing thread

        Assert.Equal(1, dispatcher.RunAll());
        Assert.Equal(loadsBefore + 1, service.LoadCalls.Count);
        Assert.Equal(Today, service.LoadCalls[^1]);
        Assert.Equal(Today, viewModel.SelectedDate);         // the reload never moves the one selected date
    }

    [Fact]
    public async Task Associate_TwoSyncChanges_PostTwoActionsInPublishOrder_WithNoDuplicate()
    {
        var notifier = new SyncLocalChangeNotifier();
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), syncLocalChangeNotifier: notifier, uiDispatcher: dispatcher);
        dispatcher.RunAll();
        await viewModel.PendingLoad;
        var loadsBefore = service.LoadCalls.Count;

        // An unrelated entity first (ignored once it runs), then the selected date's draft.
        PublishFromBackgroundThread(notifier, new SyncEntityKey(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Today.AddDays(5))));
        PublishFromBackgroundThread(notifier, new SyncEntityKey(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Today)));

        Assert.Equal(2, dispatcher.Pending);
        Assert.Equal(2, dispatcher.RunAll());
        Assert.Equal(loadsBefore + 1, service.LoadCalls.Count); // only the selected date's change reloads
    }

    [Fact]
    public async Task OperationalNotes_SyncChange_PostsOneReloadThroughTheInjectedDispatcher()
    {
        var notifier = new SyncLocalChangeNotifier();
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeOperationalNotesService();
        var viewModel = new OperationalNotesViewModel(service, new FakeClock(FixedNow), syncLocalChangeNotifier: notifier, uiDispatcher: dispatcher);
        await viewModel.PendingLoad;
        var listsBefore = service.ListCallCount;

        PublishFromBackgroundThread(notifier, new SyncEntityKey(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(Guid.NewGuid())));

        Assert.Equal(1, dispatcher.Pending);
        Assert.Equal(listsBefore, service.ListCallCount);

        Assert.Equal(1, dispatcher.RunAll());
        await viewModel.PendingLoad;
        Assert.Equal(listsBefore + 1, service.ListCallCount);
    }

    [Fact]
    public void PersonalSyncSettings_StatusChange_IsAppliedOnlyWhenDispatched_InOrder()
    {
        var coordinator = new FakePersonalSyncCoordinator();
        var dispatcher = new QueueingUiDispatcher();
        var viewModel = new PersonalSyncSettingsViewModel(
            new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator,
            uiDispatcher: dispatcher);
        dispatcher.RunAll();
        var initial = viewModel.AutomationStatus;
        var first = PersonalSyncAutomationSnapshot.Initial with { IsSyncRunning = true };
        var second = PersonalSyncAutomationSnapshot.Initial with { IsSyncRunning = false, ConflictCount = 2 };

        OnBackgroundThread(() => coordinator.RaiseStatusChanged(first));
        OnBackgroundThread(() => coordinator.RaiseStatusChanged(second));

        Assert.Equal(2, dispatcher.Pending);
        Assert.Same(initial, viewModel.AutomationStatus); // unchanged until the UI thread runs the posts

        Assert.Equal(2, dispatcher.RunAll());
        Assert.Equal(second, viewModel.AutomationStatus);  // applied in publish order: the latest status wins
    }
}
