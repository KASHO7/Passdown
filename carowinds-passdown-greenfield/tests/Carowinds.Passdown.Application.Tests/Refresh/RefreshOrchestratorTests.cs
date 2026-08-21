using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Refresh;

public class RefreshOrchestratorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 20, 15, 0, 0, TimeSpan.Zero); // 11:00 AM EDT

    private sealed record TestEnvironment(
        RefreshOrchestrator Orchestrator,
        InMemorySourceStateRepository SourceStates,
        FakeClock Clock,
        IRollingDatePlanner RollingDatePlanner,
        FakeJobBrowserSessionFactory BrowserSessions);

    private static TestEnvironment CreateEnvironment(
        IReadOnlyList<SourceRegistration> registrations,
        FakeClock? clock = null,
        RollingWindowOptions? windowOptions = null,
        RefreshOrchestratorOptions? orchestratorOptions = null)
    {
        clock ??= new FakeClock(FixedNow);
        var sourceStates = new InMemorySourceStateRepository();
        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var freshnessPlanner = new FreshnessPlanner(sourceStates, clock, policies);
        var rollingDatePlanner = new RollingDatePlanner(clock, windowOptions ?? new RollingWindowOptions());
        var browserSessions = new FakeJobBrowserSessionFactory();

        var orchestrator = new RefreshOrchestrator(
            registrations,
            freshnessPlanner,
            rollingDatePlanner,
            sourceStates,
            policies,
            browserSessions,
            clock,
            orchestratorOptions ?? new RefreshOrchestratorOptions());

        return new TestEnvironment(orchestrator, sourceStates, clock, rollingDatePlanner, browserSessions);
    }

    private static DatedSourceRegistration<FakePayload> DatedRegistration(
        string sourceId,
        SourceCategory category,
        InMemoryDatedResultRepository repository,
        Func<RetrievalRequest, CancellationToken, Task<SourceResult<FakePayload>>> handler,
        bool requiresBrowserSession = false)
    {
        var source = new FakeOfficialDataSource<FakePayload>(sourceId, category, handler);
        return new DatedSourceRegistration<FakePayload>(source, repository, requiresBrowserSession);
    }

    private static DatedSourceRegistration<FakePayload> AlwaysSucceedsDated(string sourceId, SourceCategory category, InMemoryDatedResultRepository repository) =>
        DatedRegistration(sourceId, category, repository, (request, _) => Task.FromResult(
            SourceResult<FakePayload>.Success(sourceId, FakeEvidence.Verified(FixedNow, request.RequestedDate), new FakePayload("ok"))));

    private static CatalogSourceRegistration CatalogRegistration(
        string sourceId,
        SourceCategory category,
        IProductReconciliationService reconciliationService,
        Func<RetrievalRequest, CancellationToken, Task<SourceResult<CatalogPayload>>> handler,
        bool requiresBrowserSession = false)
    {
        var source = new FakeOfficialDataSource<CatalogPayload>(sourceId, category, handler);
        return new CatalogSourceRegistration(source, reconciliationService, requiresBrowserSession);
    }

    private static IProductReconciliationService CreateReconciliationService(InMemoryCatalogProductRepository catalogProducts) =>
        new ProductReconciliationService(catalogProducts, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());

    private static ObservedProduct FakeProduct(SourceCategory category, string name) =>
        new(CatalogProductKey.FromSourceAndName(category, "Fake Source", name), name, 49.99m, "$49.99", null, null, DateSpecific: false, ApplicableDate: null);

    // 1. Only due sources are planned.
    [Fact]
    public async Task RunScheduledRefreshAsync_OnlyPlansDueSources()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var due = AlwaysSucceedsDated("due-source", SourceCategory.ParkHours, datedResults);
        var notDue = AlwaysSucceedsDated("not-due-source", SourceCategory.DatedFastLane, datedResults);

        var env = CreateEnvironment([due, notDue], windowOptions: new RollingWindowOptions { WindowLengthDays = 1 });
        await env.SourceStates.SaveAsync(new SourceStateRecord("not-due-source", SourceCategory.DatedFastLane, env.Clock.UtcNow, env.Clock.UtcNow, null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.OperationsAttempted);
        Assert.All(summary.Results, r => Assert.Equal("due-source", r.SourceId));
    }

    // 2 & 3. Daily dated sources expand across the rolling window, ordered today -> tomorrow -> remaining.
    [Fact]
    public async Task RunScheduledRefreshAsync_ExpandsDatedSourceAcrossRollingWindow_InOrder()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var registration = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var env = CreateEnvironment([registration]);
        var expectedWindow = env.RollingDatePlanner.GetRefreshWindow();

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(7, expectedWindow.Count);
        Assert.Equal(7, summary.OperationsAttempted);
        var orderedRequestedDates = env.RollingDatePlanner.GetRefreshWindow();
        Assert.Equal(expectedWindow[0], orderedRequestedDates[0]); // today
        Assert.Equal(expectedWindow[1], orderedRequestedDates[1]); // tomorrow
        Assert.Equal(expectedWindow.OrderBy(d => d), expectedWindow); // today..+6, ascending
        Assert.Equal(expectedWindow.ToHashSet(), summary.Results.Select(r => r.RequestedDate!.Value).ToHashSet());
    }

    // 4. Season passes are not refreshed before 7 days.
    [Fact]
    public async Task RunScheduledRefreshAsync_SeasonPasses_NotDueBeforeSevenDays()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var registration = CatalogRegistration("season-passes", SourceCategory.SeasonPasses, reconciliation,
            (_, _) => Task.FromResult(SourceResult<CatalogPayload>.Success("season-passes", FakeEvidence.Verified(FixedNow), new CatalogPayload([]))));

        var env = CreateEnvironment([registration]);
        await env.SourceStates.SaveAsync(new SourceStateRecord("season-passes", SourceCategory.SeasonPasses, FixedNow.AddDays(-6), FixedNow.AddDays(-6), null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(0, summary.OperationsAttempted);
    }

    // 5. Add-ons are not refreshed before 14 days.
    [Fact]
    public async Task RunScheduledRefreshAsync_AddOns_NotDueBeforeFourteenDays()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var registration = CatalogRegistration("add-ons", SourceCategory.AddOns, reconciliation,
            (_, _) => Task.FromResult(SourceResult<CatalogPayload>.Success("add-ons", FakeEvidence.Verified(FixedNow), new CatalogPayload([]))));

        var env = CreateEnvironment([registration]);
        await env.SourceStates.SaveAsync(new SourceStateRecord("add-ons", SourceCategory.AddOns, FixedNow.AddDays(-13), FixedNow.AddDays(-13), null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(0, summary.OperationsAttempted);
    }

    // 6. Promotions refresh daily.
    [Fact]
    public async Task RunScheduledRefreshAsync_Promotions_RefreshesDaily()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var registration = CatalogRegistration("promotions", SourceCategory.Promotions, reconciliation,
            (_, _) => Task.FromResult(SourceResult<CatalogPayload>.Success("promotions", FakeEvidence.Verified(FixedNow), new CatalogPayload([]))));

        var env = CreateEnvironment([registration]);
        // Succeeded yesterday, well past today's cadence boundary (6 AM Eastern) -> due again today.
        await env.SourceStates.SaveAsync(new SourceStateRecord("promotions", SourceCategory.Promotions, FixedNow.AddDays(-1), FixedNow.AddDays(-1), null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.OperationsAttempted);
        Assert.Equal(1, summary.Succeeded);
    }

    // 7. Manual single-date refresh targets only the requested dated work.
    // 8. Manual future date outside the rolling window works.
    [Fact]
    public async Task RunManualDateRefreshAsync_TargetsOnlyRequestedDatedWork_EvenOutsideRollingWindow()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var dated = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var catalogSourceCalled = false;
        var catalog = CatalogRegistration("season-passes", SourceCategory.SeasonPasses, reconciliation, (_, _) =>
        {
            catalogSourceCalled = true;
            return Task.FromResult(SourceResult<CatalogPayload>.Success("season-passes", FakeEvidence.Verified(FixedNow), new CatalogPayload([])));
        });

        var env = CreateEnvironment([dated, catalog]);
        var farFutureDate = DateOnly.FromDateTime(FixedNow.DateTime).AddDays(45); // well outside the 7-day rolling window

        var summary = await env.Orchestrator.RunManualDateRefreshAsync(farFutureDate);

        Assert.Equal(1, summary.OperationsAttempted);
        Assert.Equal(farFutureDate, summary.Results.Single().RequestedDate);
        Assert.False(catalogSourceCalled);
        var record = await datedResults.GetAsync(new DatedResultKey(SourceCategory.ParkHours, farFutureDate));
        Assert.NotNull(record?.SuccessEvidence);
    }

    // Manual refresh must never alter scheduled-cadence source state (Part C).
    [Fact]
    public async Task RunManualDateRefreshAsync_DoesNotTouchSourceState()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var dated = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var env = CreateEnvironment([dated]);

        await env.Orchestrator.RunManualDateRefreshAsync(DateOnly.FromDateTime(FixedNow.DateTime).AddDays(2));

        Assert.Null(await env.SourceStates.GetAsync("park-hours"));
    }

    // 9, 10, 11, 23. One source's failure doesn't cancel independent sources; earlier successes
    // persist regardless of a later failure; a failed dated source preserves its prior
    // last-known-good success; the summary represents partial success, not all-or-nothing.
    [Fact]
    public async Task RunScheduledRefreshAsync_PartialFailureAcrossIndependentSources_PreservesEachSourceIndependently()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var windowOptions = new RollingWindowOptions { WindowLengthDays = 1 }; // today only, one operation per source

        var parkHours = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var admission = DatedRegistration("dated-admission", SourceCategory.DatedAdmissionTickets, datedResults,
            (request, _) => Task.FromResult(SourceResult<FakePayload>.WithoutPayload("dated-admission", FakeEvidence.Error(FixedNow, request.RequestedDate))));
        var fastLane = AlwaysSucceedsDated("dated-fast-lane", SourceCategory.DatedFastLane, datedResults);
        var haunted = DatedRegistration("dated-haunted", SourceCategory.HauntedAttractions, datedResults,
            (_, _) => throw new TimeoutException("simulated timeout"));

        var env = CreateEnvironment([parkHours, admission, fastLane, haunted], windowOptions: windowOptions);

        // Give dated-admission a prior successful value so we can prove the new failure doesn't erase it.
        var today = env.RollingDatePlanner.GetRefreshWindow()[0];
        var priorEvidence = FakeEvidence.Verified(FixedNow.AddDays(-1), today);
        await datedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, today), priorEvidence, "\"prior-value\"");

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(4, summary.OperationsAttempted);
        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(0, summary.Cancelled);

        var parkHoursRecord = await datedResults.GetAsync(new DatedResultKey(SourceCategory.ParkHours, today));
        Assert.NotNull(parkHoursRecord?.SuccessEvidence);

        var fastLaneRecord = await datedResults.GetAsync(new DatedResultKey(SourceCategory.DatedFastLane, today));
        Assert.NotNull(fastLaneRecord?.SuccessEvidence);

        var admissionRecord = await datedResults.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, today));
        Assert.Same(priorEvidence, admissionRecord!.SuccessEvidence); // last-known-good preserved, untouched by the new failure
        Assert.Equal(RetrievalStatus.Error, admissionRecord.LastAttemptEvidence.Status);

        var hauntedRecord = await datedResults.GetAsync(new DatedResultKey(SourceCategory.HauntedAttractions, today));
        Assert.Null(hauntedRecord); // never previously succeeded, and the "timeout" never got far enough to write anything
    }

    // 12. Catalog Error does not call reconciliation (no products written / retains state).
    [Fact]
    public async Task CatalogSourceRegistration_Error_DoesNotReconcile()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var registration = CatalogRegistration("promotions", SourceCategory.Promotions, reconciliation,
            (_, _) => Task.FromResult(SourceResult<CatalogPayload>.WithoutPayload("promotions", FakeEvidence.Error(FixedNow))));

        var env = CreateEnvironment([registration]);
        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.Failed);
        Assert.Empty(await catalogProducts.GetByCategoryAsync(SourceCategory.Promotions));
    }

    // 13. Successful catalog result does call reconciliation.
    [Fact]
    public async Task CatalogSourceRegistration_Verified_Reconciles()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var product = FakeProduct(SourceCategory.Promotions, "Fright Fest Combo");
        var registration = CatalogRegistration("promotions", SourceCategory.Promotions, reconciliation,
            (_, _) => Task.FromResult(SourceResult<CatalogPayload>.Success("promotions", FakeEvidence.Verified(FixedNow), new CatalogPayload([product]))));

        var env = CreateEnvironment([registration]);
        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.Succeeded);
        var stored = await catalogProducts.GetByCategoryAsync(SourceCategory.Promotions);
        Assert.Single(stored);
        Assert.Equal(product.Key.ToString(), stored[0].ProductKey);
    }

    // 14, 15, 16. Source-state: last-attempt updates on failure, last-success updates only on
    // success, and the failure count increments/resets correctly across jobs.
    [Fact]
    public async Task RunScheduledRefreshAsync_UpdatesSourceState_AcrossSuccessAndFailure()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        var shouldFail = true;
        var source = new FakeOfficialDataSource<CatalogPayload>("season-passes", SourceCategory.SeasonPasses, (_, _) => Task.FromResult(
            shouldFail
                ? SourceResult<CatalogPayload>.WithoutPayload("season-passes", FakeEvidence.Error(FixedNow))
                : SourceResult<CatalogPayload>.Success("season-passes", FakeEvidence.Verified(FixedNow), new CatalogPayload([]))));
        var registration = new CatalogSourceRegistration(source, reconciliation);

        var env = CreateEnvironment([registration]);

        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterFirstFailure = await env.SourceStates.GetAsync("season-passes");
        Assert.NotNull(afterFirstFailure);
        Assert.Equal(FixedNow, afterFirstFailure!.LastAttemptAt);
        Assert.Null(afterFirstFailure.LastSuccessAt);
        Assert.Equal(1, afterFirstFailure.ConsecutiveFailureCount);

        env.Clock.UtcNow = FixedNow.AddDays(1);
        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterSecondFailure = await env.SourceStates.GetAsync("season-passes");
        Assert.Equal(2, afterSecondFailure!.ConsecutiveFailureCount);
        Assert.Null(afterSecondFailure.LastSuccessAt);

        shouldFail = false;
        env.Clock.UtcNow = FixedNow.AddDays(2);
        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterSuccess = await env.SourceStates.GetAsync("season-passes");
        Assert.Equal(env.Clock.UtcNow, afterSuccess!.LastAttemptAt);
        Assert.Equal(env.Clock.UtcNow, afterSuccess.LastSuccessAt);
        Assert.Equal(0, afterSuccess.ConsecutiveFailureCount); // reset on success
    }

    // 17. Bounded concurrency is respected.
    [Fact]
    public async Task RunScheduledRefreshAsync_RespectsBoundedConcurrency()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var inFlight = 0;
        var maxObserved = 0;
        var gate = new object();

        var registration = DatedRegistration("park-hours", SourceCategory.ParkHours, datedResults, async (request, ct) =>
        {
            lock (gate)
            {
                inFlight++;
                maxObserved = Math.Max(maxObserved, inFlight);
            }

            await Task.Delay(30, ct);

            lock (gate)
            {
                inFlight--;
            }

            return SourceResult<FakePayload>.Success("park-hours", FakeEvidence.Verified(FixedNow, request.RequestedDate), new FakePayload("ok"));
        });

        var env = CreateEnvironment(
            [registration],
            windowOptions: new RollingWindowOptions { WindowLengthDays = 6 },
            orchestratorOptions: new RefreshOrchestratorOptions { MaxConcurrency = 2 });

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(6, summary.OperationsAttempted);
        Assert.Equal(6, summary.Succeeded);
        Assert.True(maxObserved <= 2, $"Expected at most 2 concurrent operations, observed {maxObserved}.");
        Assert.True(maxObserved >= 2, "Expected the bound to actually be exercised (some real concurrency), not fully serialized.");
    }

    // 18, 19. Caller cancellation stops outstanding work without corrupting already-completed results.
    [Fact]
    public async Task RunScheduledRefreshAsync_CallerCancellation_StopsOutstandingWork_WithoutCorruptingCompletedResults()
    {
        var datedResults = new InMemoryDatedResultRepository();
        using var cts = new CancellationTokenSource();

        var registration = DatedRegistration("park-hours", SourceCategory.ParkHours, datedResults, async (request, ct) =>
        {
            if (request.RequestedDate == DateOnly.FromDateTime(FixedNow.DateTime))
            {
                return SourceResult<FakePayload>.Success("park-hours", FakeEvidence.Verified(FixedNow, request.RequestedDate), new FakePayload("ok"));
            }

            // Every other date hangs until cancellation, simulating outstanding in-flight work.
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        var env = CreateEnvironment(
            [registration],
            windowOptions: new RollingWindowOptions { WindowLengthDays = 3 },
            orchestratorOptions: new RefreshOrchestratorOptions { MaxConcurrency = 3 });

        var progress = new SyncProgress<RefreshProgress>(p =>
        {
            if (p.LastCompleted.RequestedDate == DateOnly.FromDateTime(FixedNow.DateTime))
            {
                cts.Cancel();
            }
        });

        var summary = await env.Orchestrator.RunScheduledRefreshAsync(progress, cts.Token);

        Assert.Equal(3, summary.OperationsAttempted);
        Assert.Equal(3, summary.Results.Count); // every planned operation produced a result, none lost
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(2, summary.Cancelled);
        var succeeded = summary.Results.Single(r => r.Outcome == SourceOperationOutcomeKind.Success);
        Assert.Equal(DateOnly.FromDateTime(FixedNow.DateTime), succeeded.RequestedDate);
    }

    // Regression: cancelling one source's operation must not throw out of the whole job and
    // discard the summary for an unrelated source that already finished cleanly (Part G) — nor
    // skip that unrelated source's own source-state bookkeeping.
    [Fact]
    public async Task RunScheduledRefreshAsync_CancellingOneSource_StillCompletesAndPersistsAnUnrelatedCleanSource()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = CreateReconciliationService(catalogProducts);
        using var cts = new CancellationTokenSource();

        var cleanRegistration = CatalogRegistration("season-passes", SourceCategory.SeasonPasses, reconciliation, (_, _) =>
            Task.FromResult(SourceResult<CatalogPayload>.Success("season-passes", FakeEvidence.Verified(FixedNow), new CatalogPayload([]))));
        var hangingRegistration = CatalogRegistration("promotions", SourceCategory.Promotions, reconciliation, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        var env = CreateEnvironment([cleanRegistration, hangingRegistration], orchestratorOptions: new RefreshOrchestratorOptions { MaxConcurrency = 2 });

        var progress = new SyncProgress<RefreshProgress>(p =>
        {
            if (p.LastCompleted.SourceId == "season-passes")
            {
                cts.Cancel(); // the clean source is done; now cancel while promotions is still hanging
            }
        });

        var summary = await env.Orchestrator.RunScheduledRefreshAsync(progress, cts.Token); // must not throw

        Assert.Equal(2, summary.OperationsAttempted);
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Cancelled);

        var cleanSourceState = await env.SourceStates.GetAsync("season-passes");
        Assert.NotNull(cleanSourceState);
        Assert.Equal(FixedNow, cleanSourceState!.LastSuccessAt);

        Assert.Null(await env.SourceStates.GetAsync("promotions")); // cancelled operation: state left untouched
    }

    // 20. No Playwright/browser session is created when no due source needs it.
    [Fact]
    public async Task RunScheduledRefreshAsync_NoDueSourceRequiresBrowser_NeverCreatesASession()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var registration = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults); // RequiresBrowserSession: false
        var env = CreateEnvironment([registration], windowOptions: new RollingWindowOptions { WindowLengthDays = 1 });

        await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(0, env.BrowserSessions.SessionsCreated);
    }

    // 21. A shared browser session is created at most once in a job when a fake browser-requiring source is used.
    [Fact]
    public async Task RunScheduledRefreshAsync_BrowserRequiringSource_CreatesSessionAtMostOnce()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var registration = DatedRegistration(
            "browser-source",
            SourceCategory.ParkHours,
            datedResults,
            (request, _) => Task.FromResult(SourceResult<FakePayload>.Success("browser-source", FakeEvidence.Verified(FixedNow, request.RequestedDate), new FakePayload("ok"))),
            requiresBrowserSession: true);

        var env = CreateEnvironment([registration], windowOptions: new RollingWindowOptions { WindowLengthDays = 4 });

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(4, summary.Succeeded);
        Assert.Equal(1, env.BrowserSessions.SessionsCreated); // one session object for the whole job...
        var session = Assert.Single(env.BrowserSessions.Sessions);
        Assert.True(session.StartCount >= 1); // ...and it was actually used by the browser-requiring operations
        Assert.True(session.Disposed);
    }

    // 22. Progress totals/completed counts are correct.
    [Fact]
    public async Task RunScheduledRefreshAsync_ReportsAccurateProgress()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var registration = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var env = CreateEnvironment([registration], windowOptions: new RollingWindowOptions { WindowLengthDays = 4 });

        var updates = new List<RefreshProgress>();
        var progress = new SyncProgress<RefreshProgress>(updates.Add);

        var summary = await env.Orchestrator.RunScheduledRefreshAsync(progress);

        Assert.Equal(4, updates.Count);
        Assert.All(updates, u => Assert.Equal(4, u.TotalOperations));
        Assert.Equal([1, 2, 3, 4], updates.Select(u => u.CompletedOperations).OrderBy(x => x));
        Assert.Equal(4, updates[^1].CompletedOperations);
        Assert.All(updates, u => Assert.False(u.CancellationRequested));
        Assert.Equal(
            summary.Results.OrderBy(r => r.RequestedDate).ToList(),
            updates.Select(u => u.LastCompleted).OrderBy(r => r.RequestedDate).ToList());
    }

    private sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
