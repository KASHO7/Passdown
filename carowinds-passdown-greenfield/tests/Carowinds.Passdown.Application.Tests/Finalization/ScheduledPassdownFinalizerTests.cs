using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Application.Time;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Application.Tests.Finalization;

/// <summary>
/// Exercises <see cref="ScheduledPassdownFinalizer"/> against the real <see cref="AssociatePassdownService"/>
/// (so "which date's local state gets finalized" runs through the exact same composition the
/// Associate screen uses — spec Phase 9B Part N) wired to in-memory fakes only, plus the real
/// <see cref="FinalizedPassdownFinalizationService"/> over an in-memory finalized-passdown
/// repository and a fake snapshot builder (avoids any dependency on the Documents project). No
/// SQLite, HTTP, or Playwright is constructible in this test class at all (Part M/U).
/// </summary>
public class ScheduledPassdownFinalizerTests
{
    private static readonly DateOnly Date0825 = new(2026, 8, 25); // a Tuesday; cutoff 2026-08-26 06:00 EDT.

    private sealed record TestEnvironment(
        ScheduledPassdownFinalizer Finalizer,
        InMemoryAssociateDraftRepository Drafts,
        InMemoryFinalizedPassdownRepository FinalizedRepository,
        FakeFinalizedPassdownSnapshotBuilder SnapshotBuilder,
        FakeRefreshOrchestrator RefreshOrchestrator,
        FakeClock Clock);

    private static TestEnvironment CreateEnvironment(DateTimeOffset now)
    {
        var clock = new FakeClock(now);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var associateService = new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, clock);

        var finalizedRepository = new InMemoryFinalizedPassdownRepository();
        var snapshotBuilder = new FakeFinalizedPassdownSnapshotBuilder();
        var finalizationService = new FinalizedPassdownFinalizationService(finalizedRepository, snapshotBuilder);

        var finalizer = new ScheduledPassdownFinalizer(
            drafts, associateService, finalizedRepository, finalizationService, clock, NullLogger<ScheduledPassdownFinalizer>.Instance);

        return new TestEnvironment(finalizer, drafts, finalizedRepository, snapshotBuilder, refreshOrchestrator, clock);
    }

    private static Task SaveDraftAsync(InMemoryAssociateDraftRepository drafts, DateOnly date, DateTimeOffset updatedAt) =>
        drafts.SaveAsync(new AssociateDraft(
            date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault()), updatedAt));

    // #1: before the cutoff instant, a saved draft is not yet eligible.
    [Fact]
    public async Task FinalizeEligibleAsync_SavedDraftBeforeCutoff_DoesNotFinalize()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff.AddSeconds(-1));
        await SaveDraftAsync(env.Drafts, Date0825, cutoff.AddDays(-1));

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(0, summary.EligibleCount);
        Assert.Equal(0, summary.FinalizedCount);
        Assert.False(await env.FinalizedRepository.ExistsForDateAsync(Date0825));
    }

    // #2: exactly at the cutoff instant, the saved draft becomes eligible and is finalized.
    [Fact]
    public async Task FinalizeEligibleAsync_SavedDraftAtCutoff_Finalizes()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff);
        await SaveDraftAsync(env.Drafts, Date0825, cutoff.AddDays(-1));

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(1, summary.EligibleCount);
        Assert.Equal(1, summary.FinalizedCount);
        var finalized = await env.FinalizedRepository.GetByDateAsync(Date0825);
        Assert.NotNull(finalized);
        Assert.Equal(FinalizationOrigin.Scheduled, finalized!.Origin);
        Assert.Equal(cutoff, finalized.FinalizedAtUtc); // actual completion time, not a nominal 6:00.
    }

    // #3: no saved draft row at all — never fabricated as a finalized record.
    [Fact]
    public async Task FinalizeEligibleAsync_NoSavedDraft_DoesNotFinalize()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff.AddDays(3)); // comfortably past cutoff, if it were eligible

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(0, summary.EligibleCount);
        Assert.False(await env.FinalizedRepository.ExistsForDateAsync(Date0825));
    }

    // #4: a workspace being loadable (every date is) is not the eligibility signal — only a
    // persisted draft row is (spec Phase 9B Part D, resolving docs/phase9-finalization.md's open
    // decision).
    [Fact]
    public async Task FinalizeEligibleAsync_LoadableButUnsavedWorkspace_DoesNotFinalize()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff.AddDays(3));

        // Composes a full default AssociateWorkspace for Date0825 without ever saving a draft row —
        // mirrors what merely displaying the Associate screen for this date would do.
        var associateService = new AssociatePassdownService(
            env.Drafts, new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(), env.RefreshOrchestrator, env.Clock);
        _ = await associateService.LoadAssociateDateAsync(Date0825);

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(0, summary.EligibleCount);
        Assert.False(await env.FinalizedRepository.ExistsForDateAsync(Date0825));
    }

    // #5: an already-finalized date is skipped, not duplicated.
    [Fact]
    public async Task FinalizeEligibleAsync_AlreadyFinalizedDate_SkipsWithoutDuplicate()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff.AddDays(1));
        await SaveDraftAsync(env.Drafts, Date0825, cutoff.AddDays(-1));
        await env.FinalizedRepository.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date0825, cutoff, FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash", FinalizationOrigin.Scheduled));

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(1, summary.EligibleCount);
        Assert.Equal(0, summary.FinalizedCount);
        Assert.Equal(1, summary.AlreadyFinalizedCount);
        var summaries = await env.FinalizedRepository.ListAsync();
        Assert.Single(summaries);
    }

    // #11/#12: multi-day downtime finalizes every eligible date, oldest first.
    [Fact]
    public async Task FinalizeEligibleAsync_MultiDayDowntime_FinalizesAllEligibleChronologically()
    {
        var dates = new[] { new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 27) };
        var now = FinalizationEligibility.EligibleForFinalizationAt(dates[^1]).AddHours(3); // 2026-08-28 ~09:00 Eastern
        var env = CreateEnvironment(now);
        foreach (var date in dates)
        {
            await SaveDraftAsync(env.Drafts, date, now.AddDays(-5));
        }

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(4, summary.EligibleCount);
        Assert.Equal(4, summary.FinalizedCount);
        Assert.Equal(dates, env.SnapshotBuilder.ReceivedWorkspaces.Select(w => w.OperatingDate).ToArray());
    }

    // #12 variant: among a multi-day gap, a date already finalized earlier is left alone while the
    // remaining eligible dates are still finalized.
    [Fact]
    public async Task FinalizeEligibleAsync_SomeAlreadyFinalizedAmongMultiDay_OnlyFinalizesRemaining()
    {
        var dates = new[] { new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 27) };
        var now = FinalizationEligibility.EligibleForFinalizationAt(dates[^1]).AddHours(3);
        var env = CreateEnvironment(now);
        foreach (var date in dates)
        {
            await SaveDraftAsync(env.Drafts, date, now.AddDays(-5));
        }

        await env.FinalizedRepository.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), new DateOnly(2026, 8, 25), now.AddDays(-1), FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash", FinalizationOrigin.Recovery));

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(4, summary.EligibleCount);
        Assert.Equal(3, summary.FinalizedCount);
        Assert.Equal(1, summary.AlreadyFinalizedCount);
        Assert.Equal(
            new[] { new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 27) },
            env.SnapshotBuilder.ReceivedWorkspaces.Select(w => w.OperatingDate).ToArray());
    }

    // #13: an old missed date — its cutoff calendar date is before today's Eastern calendar date —
    // is finalized with Recovery origin.
    [Fact]
    public async Task FinalizeEligibleAsync_OldMissedDate_UsesRecoveryOrigin()
    {
        var oldDate = new DateOnly(2026, 8, 24); // cutoff calendar date 2026-08-25.
        var now = FinalizationEligibility.EligibleForFinalizationAt(oldDate).AddDays(3); // today Eastern = 2026-08-28.
        var env = CreateEnvironment(now);
        await SaveDraftAsync(env.Drafts, oldDate, now.AddDays(-5));

        await env.Finalizer.FinalizeEligibleAsync();

        var finalized = await env.FinalizedRepository.GetByDateAsync(oldDate);
        Assert.Equal(FinalizationOrigin.Recovery, finalized!.Origin);
    }

    // #14: a normal previous-day cutoff — cutoff calendar date equals today's Eastern calendar
    // date — is finalized with Scheduled origin.
    [Fact]
    public async Task FinalizeEligibleAsync_NormalPreviousDayCutoff_UsesScheduledOrigin()
    {
        var now = FinalizationEligibility.EligibleForFinalizationAt(Date0825).AddMinutes(1); // just after 06:00 Eastern the next day.
        var env = CreateEnvironment(now);
        await SaveDraftAsync(env.Drafts, Date0825, now.AddDays(-1));

        await env.Finalizer.FinalizeEligibleAsync();

        var finalized = await env.FinalizedRepository.GetByDateAsync(Date0825);
        Assert.Equal(FinalizationOrigin.Scheduled, finalized!.Origin);
    }

    // #15: today's and future drafts are never finalized, even though they're saved.
    [Fact]
    public async Task FinalizeEligibleAsync_FutureOrTodayDrafts_AreNotFinalized()
    {
        var today = new DateOnly(2026, 8, 26);
        var tomorrow = today.AddDays(1);
        var now = new DateTimeOffset(2026, 8, 26, 13, 0, 0, TimeSpan.FromHours(-4)); // 2026-08-26 09:00 Eastern
        var env = CreateEnvironment(now);
        await SaveDraftAsync(env.Drafts, today, now);
        await SaveDraftAsync(env.Drafts, tomorrow, now);

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(0, summary.EligibleCount);
        Assert.False(await env.FinalizedRepository.ExistsForDateAsync(today));
        Assert.False(await env.FinalizedRepository.ExistsForDateAsync(tomorrow));
    }

    // #16: DST-aware cutoff remains correct through the finalizer, not just the pure helper.
    [Fact]
    public async Task FinalizeEligibleAsync_DstAwareCutoffRemainsCorrect()
    {
        var decemberDate = new DateOnly(2026, 12, 24); // cutoff falls under EST (UTC-5).
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(decemberDate);
        Assert.Equal(TimeSpan.FromHours(-5), cutoff.Offset);

        var beforeEnv = CreateEnvironment(cutoff.AddSeconds(-1));
        await SaveDraftAsync(beforeEnv.Drafts, decemberDate, cutoff.AddDays(-1));
        var beforeSummary = await beforeEnv.Finalizer.FinalizeEligibleAsync();
        Assert.Equal(0, beforeSummary.EligibleCount);

        var atEnv = CreateEnvironment(cutoff);
        await SaveDraftAsync(atEnv.Drafts, decemberDate, cutoff.AddDays(-1));
        var atSummary = await atEnv.Finalizer.FinalizeEligibleAsync();
        Assert.Equal(1, atSummary.FinalizedCount);
    }

    // #17: zero network/source calls — LoadAssociateDateAsync never reaches IRefreshOrchestrator.
    [Fact]
    public async Task FinalizeEligibleAsync_PerformsZeroNetworkCalls()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff);
        await SaveDraftAsync(env.Drafts, Date0825, cutoff.AddDays(-1));

        await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(0, env.RefreshOrchestrator.ScheduledRefreshCallCount);
        Assert.Empty(env.RefreshOrchestrator.ManualDateRefreshCalls);
    }

    // #18: the exact PassdownDate is used, not "today" or any other date.
    [Fact]
    public async Task FinalizeEligibleAsync_UsesExactPassdownDate()
    {
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(Date0825);
        var env = CreateEnvironment(cutoff);
        await SaveDraftAsync(env.Drafts, Date0825, cutoff.AddDays(-1));

        await env.Finalizer.FinalizeEligibleAsync();

        Assert.Single(env.SnapshotBuilder.ReceivedWorkspaces);
        Assert.Equal(Date0825, env.SnapshotBuilder.ReceivedWorkspaces[0].OperatingDate);
    }

    // Summary counts always reconcile: eligible == finalized + already finalized.
    [Fact]
    public async Task FinalizeEligibleAsync_SummaryCounts_AreAccurate()
    {
        var earlier = new DateOnly(2026, 8, 24);
        var later = new DateOnly(2026, 8, 25);
        var now = FinalizationEligibility.EligibleForFinalizationAt(later).AddHours(1);
        var env = CreateEnvironment(now);
        await SaveDraftAsync(env.Drafts, earlier, now.AddDays(-3));
        await SaveDraftAsync(env.Drafts, later, now.AddDays(-3));
        await env.FinalizedRepository.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), earlier, now.AddHours(-1), FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash", FinalizationOrigin.Recovery));

        var summary = await env.Finalizer.FinalizeEligibleAsync();

        Assert.Equal(2, summary.EligibleCount);
        Assert.Equal(1, summary.FinalizedCount);
        Assert.Equal(1, summary.AlreadyFinalizedCount);
        Assert.Equal(summary.EligibleCount, summary.FinalizedCount + summary.AlreadyFinalizedCount);
    }
}
