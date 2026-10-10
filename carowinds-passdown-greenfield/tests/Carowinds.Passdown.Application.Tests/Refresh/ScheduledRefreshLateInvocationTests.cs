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

/// <summary>
/// Phase 6B Parts B/M (spec §17 Phase 6 "test offline and failure scenarios"; #7/#8/#24 in the
/// task's test list): a scheduled refresh invoked late — because the machine was asleep, off, or
/// Task Scheduler simply missed the nominal 6:00 AM Eastern time — must still do the correct
/// due-only work, and must not run an already-fresh weekly/biweekly catalog just because the
/// process happened to run. This adds no new logic: <see cref="IFreshnessPlanner"/>/
/// <see cref="FreshnessPolicy"/> already key due-ness off the last successful attempt and the
/// current instant, never off when the process happens to invoke (Phase 2) — these tests exercise
/// the existing <see cref="RefreshOrchestrator"/> pipeline with clocks pinned to specific "late"
/// wall-clock times rather than adding a second due-ness mechanism.
/// </summary>
public class ScheduledRefreshLateInvocationTests
{
    // 2026-08-21 is a Friday, comfortably outside any DST transition (Part L).
    private static readonly DateTimeOffset SixAmEastern = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero); // 6:00 AM EDT (UTC-4)

    private sealed record TestEnvironment(RefreshOrchestrator Orchestrator, InMemorySourceStateRepository SourceStates);

    private static TestEnvironment CreateEnvironment(IReadOnlyList<SourceRegistration> registrations, FakeClock clock)
    {
        var sourceStates = new InMemorySourceStateRepository();
        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var freshnessPlanner = new FreshnessPlanner(sourceStates, clock, policies);
        var rollingDatePlanner = new RollingDatePlanner(clock, new RollingWindowOptions { WindowLengthDays = 1 });
        var browserSessions = new FakeJobBrowserSessionFactory();

        var orchestrator = new RefreshOrchestrator(
            registrations, freshnessPlanner, rollingDatePlanner, sourceStates, policies, browserSessions, clock, new RefreshOrchestratorOptions());

        return new TestEnvironment(orchestrator, sourceStates);
    }

    private static DatedSourceRegistration<FakePayload> AlwaysSucceedsDated(string sourceId, SourceCategory category, InMemoryDatedResultRepository repository) =>
        new(new FakeOfficialDataSource<FakePayload>(sourceId, category, (request, _) => Task.FromResult(
                SourceResult<FakePayload>.Success(sourceId, FakeEvidence.Verified(DateTimeOffset.UtcNow, request.RequestedDate), new FakePayload("ok")))),
            repository);

    private static CatalogSourceRegistration AlwaysSucceedsCatalog(string sourceId, SourceCategory category, IProductReconciliationService reconciliation) =>
        new(new FakeOfficialDataSource<CatalogPayload>(sourceId, category, (_, _) => Task.FromResult(
                SourceResult<CatalogPayload>.Success(sourceId, FakeEvidence.Verified(DateTimeOffset.UtcNow), new CatalogPayload([])))),
            reconciliation);

    private static IProductReconciliationService CreateReconciliationService() =>
        new ProductReconciliationService(new InMemoryCatalogProductRepository(), new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());

    // Last success was 5:00 AM yesterday — before yesterday's own 6 AM boundary, and certainly
    // before today's. A process invoked at 8:30 AM (machine woke up late) must still find this due.
    [Fact]
    public async Task LateMorningInvocation_AfterMissedSixAm_StillRefreshesDueDailySource()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var clock = new FakeClock(SixAmEastern.AddHours(2).AddMinutes(30)); // 8:30 AM Eastern
        var registration = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var env = CreateEnvironment([registration], clock);
        var lastSuccess = SixAmEastern.AddDays(-1).AddHours(-1); // 5:00 AM yesterday
        await env.SourceStates.SaveAsync(new SourceStateRecord("park-hours", SourceCategory.ParkHours, lastSuccess, lastSuccess, null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.OperationsAttempted);
        Assert.Equal(1, summary.Succeeded);
    }

    // Same missed-run scenario, but discovered even later — afternoon, well past the 6 AM window.
    [Fact]
    public async Task AfternoonInvocation_AfterMissedSixAm_StillRefreshesDueDailySource()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var clock = new FakeClock(SixAmEastern.AddHours(9)); // 3:00 PM Eastern
        var registration = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var env = CreateEnvironment([registration], clock);
        var lastSuccess = SixAmEastern.AddDays(-1).AddHours(-1); // 5:00 AM yesterday
        await env.SourceStates.SaveAsync(new SourceStateRecord("park-hours", SourceCategory.ParkHours, lastSuccess, lastSuccess, null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.OperationsAttempted);
        Assert.Equal(1, summary.Succeeded);
    }

    // No prior run has ever succeeded (e.g. first launch the day after install) — always due,
    // regardless of what time the process happens to be invoked.
    [Fact]
    public async Task NextDayInvocation_WithNoPriorRunAtAll_IsDue()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var clock = new FakeClock(SixAmEastern.AddHours(9)); // 3:00 PM Eastern, never run before
        var registration = AlwaysSucceedsDated("park-hours", SourceCategory.ParkHours, datedResults);
        var env = CreateEnvironment([registration], clock);

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(1, summary.OperationsAttempted);
        Assert.Equal(1, summary.Succeeded);
    }

    // Phase 13C Part R: season passes moved from a 7-day rolling cadence to the same daily cadence
    // as every other commercial category — a late invocation must still not refresh them merely
    // because the process ran, if they already succeeded earlier today (after today's 6 AM).
    [Fact]
    public async Task LateInvocation_SeasonPassesAlreadySucceededEarlierToday_StaysSkipped()
    {
        var reconciliation = CreateReconciliationService();
        var clock = new FakeClock(SixAmEastern.AddHours(9)); // 3:00 PM Eastern, late invocation
        var registration = AlwaysSucceedsCatalog("season-passes", SourceCategory.SeasonPasses, reconciliation);
        var env = CreateEnvironment([registration], clock);
        var lastSuccess = SixAmEastern.AddHours(1); // 7:00 AM Eastern today — already ran today
        await env.SourceStates.SaveAsync(new SourceStateRecord("season-passes", SourceCategory.SeasonPasses, lastSuccess, lastSuccess, null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(0, summary.OperationsAttempted);
    }

    // Same guarantee for add-ons, also moved to daily cadence.
    [Fact]
    public async Task LateInvocation_AddOnsAlreadySucceededEarlierToday_StaysSkipped()
    {
        var reconciliation = CreateReconciliationService();
        var clock = new FakeClock(SixAmEastern.AddHours(9)); // 3:00 PM Eastern, late invocation
        var registration = AlwaysSucceedsCatalog("add-ons", SourceCategory.AddOns, reconciliation);
        var env = CreateEnvironment([registration], clock);
        var lastSuccess = SixAmEastern.AddHours(1); // 7:00 AM Eastern today — already ran today
        await env.SourceStates.SaveAsync(new SourceStateRecord("add-ons", SourceCategory.AddOns, lastSuccess, lastSuccess, null, null, 0));

        var summary = await env.Orchestrator.RunScheduledRefreshAsync();

        Assert.Equal(0, summary.OperationsAttempted);
    }

    // Part K/N (#18/#19): the exact spec sequence — success, then two consecutive failures, then
    // recovery — proving last-known-good survives the failures and a later success both replaces
    // the LKG value and resets the failure count. Uses a catalog source (state keyed by source id
    // alone, spec §6.2) rather than a dated one, so every run targets the same persisted record —
    // a dated source's rolling window advances to a new calendar date each day, which would make
    // "the same key across four daily runs" a different, unrelated assertion. Phase 3G's
    // RunScheduledRefreshAsync_UpdatesSourceState_AcrossSuccessAndFailure already proves the
    // failure-increment/reset mechanics starting from a fresh source; this walks the sequence spec
    // §17 Phase 6 describes starting from a genuine prior success, and additionally checks that the
    // reconciled catalog product itself (not just source_state timestamps) survives the failures.
    [Fact]
    public async Task RepeatedFailureThenRecovery_PreservesLastKnownGood_AndResetsFailureCountOnRecovery()
    {
        var catalogProducts = new InMemoryCatalogProductRepository();
        var reconciliation = new ProductReconciliationService(catalogProducts, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());
        var shouldSucceed = true;
        var product = new ObservedProduct(
            CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Fake Source", "Gold Pass"),
            "Gold Pass", 199.99m, "$199.99", null, null, DateSpecific: false, ApplicableDate: null);
        var source = new FakeOfficialDataSource<CatalogPayload>("season-passes", SourceCategory.SeasonPasses, (_, _) => Task.FromResult(
            shouldSucceed
                ? SourceResult<CatalogPayload>.Success("season-passes", FakeEvidence.Verified(DateTimeOffset.UtcNow), new CatalogPayload([product]))
                : SourceResult<CatalogPayload>.WithoutPayload("season-passes", FakeEvidence.Error(DateTimeOffset.UtcNow))));
        var registration = new CatalogSourceRegistration(source, reconciliation);
        var clock = new FakeClock(SixAmEastern);
        var env = CreateEnvironment([registration], clock);

        // Run 1: success.
        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterRun1 = await env.SourceStates.GetAsync("season-passes");
        Assert.Equal(0, afterRun1!.ConsecutiveFailureCount);
        Assert.NotNull(afterRun1.LastSuccessAt);
        Assert.Single(await catalogProducts.GetByCategoryAsync(SourceCategory.SeasonPasses));

        // Run 2: fails.
        shouldSucceed = false;
        clock.UtcNow = SixAmEastern.AddDays(8); // past the 7-day cadence, so it's due again
        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterRun2 = await env.SourceStates.GetAsync("season-passes");
        Assert.Equal(1, afterRun2!.ConsecutiveFailureCount);
        Assert.Equal(afterRun1.LastSuccessAt, afterRun2.LastSuccessAt); // untouched by the failure
        Assert.Single(await catalogProducts.GetByCategoryAsync(SourceCategory.SeasonPasses)); // LKG product preserved

        // Run 3: fails again. Cadence keeps this source due every subsequent run because it never
        // recorded a new success to restart the 7-day clock from.
        clock.UtcNow = SixAmEastern.AddDays(9);
        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterRun3 = await env.SourceStates.GetAsync("season-passes");
        Assert.Equal(2, afterRun3!.ConsecutiveFailureCount);
        Assert.Equal(afterRun1.LastSuccessAt, afterRun3.LastSuccessAt);
        Assert.Single(await catalogProducts.GetByCategoryAsync(SourceCategory.SeasonPasses)); // still preserved

        // Run 4: recovers.
        shouldSucceed = true;
        clock.UtcNow = SixAmEastern.AddDays(10);
        await env.Orchestrator.RunScheduledRefreshAsync();
        var afterRun4 = await env.SourceStates.GetAsync("season-passes");
        Assert.Equal(0, afterRun4!.ConsecutiveFailureCount); // reset on recovery
        Assert.Equal(clock.UtcNow, afterRun4.LastSuccessAt); // LKG timestamp replaced by the new success
        Assert.Single(await catalogProducts.GetByCategoryAsync(SourceCategory.SeasonPasses));
    }
}
