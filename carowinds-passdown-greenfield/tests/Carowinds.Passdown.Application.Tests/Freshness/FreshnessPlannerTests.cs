using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Tests.Freshness;

public class FreshnessPlannerTests
{
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);
    private static readonly TimeSpan Edt = TimeSpan.FromHours(-4);

    private static FreshnessPlanner CreatePlanner(InMemorySourceStateRepository repository, FakeClock clock) =>
        new(repository, clock, FreshnessPolicyCatalog.Build(new FreshnessOptions()));

    private static SourceStateRecord StateWithLastSuccess(string sourceId, SourceCategory category, DateTimeOffset lastSuccessAt) =>
        new(sourceId, category, lastSuccessAt, lastSuccessAt, null, null, 0);

    [Fact]
    public async Task EvaluateAsync_NeverAttempted_IsDue()
    {
        var repository = new InMemorySourceStateRepository();
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var planner = CreatePlanner(repository, clock);

        var decision = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.True(decision.IsDue);
        Assert.Null(decision.LastSuccessAt);
    }

    [Fact]
    public async Task EvaluateAsync_DailySource_NotDueBeforeSixAmEastern_WhenLastSuccessWasYesterday()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 19, 22, 0, 0, Est); // yesterday evening Eastern
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        // 5:59 AM Eastern today — still Eastern Daylight Time in August.
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 9, 59, 0, TimeSpan.Zero));
        var planner = CreatePlanner(repository, clock);

        var decision = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.False(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_DailySource_DueAfterSixAmEastern_WhenLastSuccessWasYesterday()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 19, 22, 0, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        // 6:01 AM Eastern today (EDT, UTC-4).
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 10, 1, 0, TimeSpan.Zero));
        var planner = CreatePlanner(repository, clock);

        var decision = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.True(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_DailySource_AlreadySucceededToday_IsNotDueAgainLaterTheSameDay()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 20, 10, 5, 0, Edt); // succeeded today, just after 6 AM
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 22, 0, 0, TimeSpan.Zero)); // later today

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.False(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_MissedSixAmRun_BecomesDueWhenTheAppLaunchesLaterThatDay()
    {
        var repository = new InMemorySourceStateRepository();
        // The 6 AM job never ran today or yesterday's run was the last success.
        var lastSuccess = new DateTimeOffset(2026, 8, 18, 10, 0, 0, Edt); // two days ago
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        // App launches at 2 PM Eastern today, well after the missed 6 AM slot.
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.True(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_AfterCatchUpRunSucceeds_IsNotDueAgainForTheRestOfTheDay()
    {
        // Simulates the missed-run catch-up itself completing: the state that was
        // due is now recorded as succeeded (at the moment the app caught up), and a
        // fresh evaluation later that same day must not ask for it again (spec §6.6
        // "do not run the same full job repeatedly").
        var repository = new InMemorySourceStateRepository();
        var catchUpSuccess = new DateTimeOffset(2026, 8, 20, 18, 0, 0, Edt); // 2 PM Eastern catch-up run
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, catchUpSuccess));
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 23, 0, 0, TimeSpan.Zero)); // later that evening

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.False(decision.IsDue);
    }

    [Theory]
    [InlineData("2026-01-14T23:00:00+00:00", "2026-01-15T11:15:00+00:00", true)]  // winter (EST): past 6 AM Eastern -> due
    [InlineData("2026-01-14T23:00:00+00:00", "2026-01-15T10:45:00+00:00", false)] // winter (EST): before 6 AM Eastern -> not due
    [InlineData("2026-07-14T22:00:00+00:00", "2026-07-15T10:15:00+00:00", true)]  // summer (EDT): past 6 AM Eastern -> due
    [InlineData("2026-07-14T22:00:00+00:00", "2026-07-15T09:45:00+00:00", false)] // summer (EDT): before 6 AM Eastern -> not due
    public async Task EvaluateAsync_IsDstSafe_UsesTheActualSeasonalOffsetNotAFixedOne(
        string lastSuccessUtcText, string nowUtcText, bool expectedDue)
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = DateTimeOffset.Parse(lastSuccessUtcText);
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        var clock = new FakeClock(DateTimeOffset.Parse(nowUtcText));

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.Equal(expectedDue, decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_SeasonPasses_NotDueBeforeSevenDaysElapse()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 14, 10, 0, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("season-passes", SourceCategory.SeasonPasses, lastSuccess));
        var clock = new FakeClock(lastSuccess.AddDays(6).ToUniversalTime());

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("season-passes", SourceCategory.SeasonPasses);

        Assert.False(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_SeasonPasses_DueOnceSevenDaysElapse()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 14, 10, 0, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("season-passes", SourceCategory.SeasonPasses, lastSuccess));
        var clock = new FakeClock(lastSuccess.AddDays(7).ToUniversalTime());

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("season-passes", SourceCategory.SeasonPasses);

        Assert.True(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_AddOns_NotDueBeforeFourteenDaysElapse()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 1, 10, 0, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("add-ons", SourceCategory.AddOns, lastSuccess));
        var clock = new FakeClock(lastSuccess.AddDays(13).ToUniversalTime());

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("add-ons", SourceCategory.AddOns);

        Assert.False(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_AddOns_DueOnceFourteenDaysElapse()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 1, 10, 0, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("add-ons", SourceCategory.AddOns, lastSuccess));
        var clock = new FakeClock(lastSuccess.AddDays(14).ToUniversalTime());

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("add-ons", SourceCategory.AddOns);

        Assert.True(decision.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_ComputesNextDueAt_ForADailySource()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 20, 10, 5, 0, Edt); // succeeded just after 6 AM today
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 22, 0, 0, TimeSpan.Zero));

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.Equal(new DateTimeOffset(2026, 8, 21, 6, 0, 0, Edt), decision.NextDueAt);
    }

    [Fact]
    public async Task EvaluateAsync_ComputesNextDueAt_ForARollingIntervalSource()
    {
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 14, 10, 0, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("season-passes", SourceCategory.SeasonPasses, lastSuccess));
        var clock = new FakeClock(lastSuccess.AddDays(1).ToUniversalTime());

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("season-passes", SourceCategory.SeasonPasses);

        Assert.Equal(lastSuccess.AddDays(7), decision.NextDueAt);
    }

    [Fact]
    public async Task EvaluateAsync_NeverAttempted_HasNoNextDueAt()
    {
        var repository = new InMemorySourceStateRepository();
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

        var decision = await CreatePlanner(repository, clock).EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.Null(decision.NextDueAt);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotRefreshMerelyBecauseADateWasSelected()
    {
        // The planner's surface never accepts a "selected date" at all — its
        // decision depends only on the source's own persisted state and cadence
        // (spec §6.3). Evaluating the same source id repeatedly with unrelated
        // "selected date" changes elsewhere in the app cannot change the answer.
        var repository = new InMemorySourceStateRepository();
        var lastSuccess = new DateTimeOffset(2026, 8, 20, 10, 5, 0, Edt);
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, lastSuccess));
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 22, 0, 0, TimeSpan.Zero));
        var planner = CreatePlanner(repository, clock);

        var first = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);
        var second = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.Equal(first.IsDue, second.IsDue);
        Assert.False(second.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentSourceIds_AreEvaluatedIndependently()
    {
        var repository = new InMemorySourceStateRepository();
        var now = new DateTimeOffset(2026, 8, 20, 10, 31, 0, TimeSpan.Zero); // 6:31 AM EDT
        // park-hours succeeded today already, at 6:01 AM EDT; dated-fast-lane never has.
        await repository.SaveAsync(StateWithLastSuccess("park-hours", SourceCategory.ParkHours, now.AddMinutes(-30)));
        var clock = new FakeClock(now);
        var planner = CreatePlanner(repository, clock);

        var parkHours = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);
        var fastLane = await planner.EvaluateAsync("dated-fast-lane", SourceCategory.DatedFastLane);

        Assert.False(parkHours.IsDue);
        Assert.True(fastLane.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_StableCatalogCadence_IsUnaffectedByAnUnrelatedDailySourceBeingDue()
    {
        var repository = new InMemorySourceStateRepository();
        var seasonPassSuccess = new DateTimeOffset(2026, 8, 14, 10, 0, 0, Edt); // within its 7-day window
        await repository.SaveAsync(StateWithLastSuccess("season-passes", SourceCategory.SeasonPasses, seasonPassSuccess));
        // park-hours has never succeeded, so it's due — this must not affect season-passes.
        var clock = new FakeClock(seasonPassSuccess.AddDays(2).ToUniversalTime());
        var planner = CreatePlanner(repository, clock);

        var seasonPasses = await planner.EvaluateAsync("season-passes", SourceCategory.SeasonPasses);
        var parkHours = await planner.EvaluateAsync("park-hours", SourceCategory.ParkHours);

        Assert.False(seasonPasses.IsDue);
        Assert.True(parkHours.IsDue);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownCategory_ThrowsRatherThanSilentlyDefaulting()
    {
        var repository = new InMemorySourceStateRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var planner = new FreshnessPlanner(repository, clock, new Dictionary<SourceCategory, Domain.Retrieval.FreshnessPolicy>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.EvaluateAsync("park-hours", SourceCategory.ParkHours));
    }
}
