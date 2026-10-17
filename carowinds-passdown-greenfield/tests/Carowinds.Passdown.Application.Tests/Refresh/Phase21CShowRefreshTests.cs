using System.Text.Json;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Refresh;

/// <summary>
/// Phase 21C: the show schedule rides the existing refresh orchestrator (startup/four-hour comprehensive
/// check, Check Online, Deep Refresh, date checks) — no second polling engine — and its failures never
/// touch park hours or pricing.
/// </summary>
public class Phase21CShowRefreshTests
{
    /// <summary>2026-10-16 3:00 PM Eastern.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);

    private sealed record Environment(
        RefreshOrchestrator Orchestrator,
        InMemoryDatedResultRepository Dated,
        FakeOfficialDataSource<FakePayload> Hours,
        FakeOfficialDataSource<ShowSchedulePayload> Shows);

    private static Environment Create(Func<RetrievalRequest, SourceResult<ShowSchedulePayload>> showHandler)
    {
        var clock = new FakeClock(Now);
        var dated = new InMemoryDatedResultRepository();
        var hours = new FakeOfficialDataSource<FakePayload>("park-hours.official", SourceCategory.ParkHours, (request, _) =>
            Task.FromResult(SourceResult<FakePayload>.Success("park-hours.official", FakeEvidence.Verified(Now, request.RequestedDate), new FakePayload("hours"))));
        var shows = new FakeOfficialDataSource<ShowSchedulePayload>("show-schedule.official", SourceCategory.ShowSchedule, (request, _) => Task.FromResult(showHandler(request)));

        IReadOnlyList<SourceRegistration> registrations =
        [
            new DatedSourceRegistration<FakePayload>(hours, dated),
            new CurrentAndFutureDatedSourceRegistration<ShowSchedulePayload>(shows, dated, clock),
        ];

        var sourceStates = new InMemorySourceStateRepository();
        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var orchestrator = new RefreshOrchestrator(
            registrations, new FreshnessPlanner(sourceStates, clock, policies), new RollingDatePlanner(clock, new RollingWindowOptions()),
            sourceStates, policies, new FakeJobBrowserSessionFactory(), clock, new RefreshOrchestratorOptions());
        return new Environment(orchestrator, dated, hours, shows);
    }

    private static SourceResult<ShowSchedulePayload> Verified(RetrievalRequest request) =>
        SourceResult<ShowSchedulePayload>.Success("show-schedule.official", FakeEvidence.Verified(Now, request.RequestedDate),
            new ShowSchedulePayload([new ScheduledShow("SHOW-030-00122", "Wake the Dead", "Carowinds Theatre", [new TimeOnly(19, 0)])]));

    private static SourceResult<ShowSchedulePayload> Failed(RetrievalRequest request) =>
        SourceResult<ShowSchedulePayload>.WithoutPayload("show-schedule.official", FakeEvidence.Error(Now, request.RequestedDate));

    [Fact]
    public async Task CheckOnline_RetrievesTheSelectedDatesShows_AlongsideEverythingElse()
    {
        var env = Create(Verified);

        var summary = await env.Orchestrator.RunUserForcedRefreshAsync(Today.AddDays(1));

        Assert.Equal(0, summary.Failed);
        Assert.Equal([Today.AddDays(1)], env.Shows.Requests.Select(r => r.RequestedDate!.Value));
        var record = await env.Dated.GetAsync(new DatedResultKey(SourceCategory.ShowSchedule, Today.AddDays(1)));
        Assert.Equal(RetrievalStatus.Verified, record!.SuccessEvidence!.Status);
    }

    [Fact]
    public async Task ShowFailure_IsIsolated_ParkHoursStillSucceed()
    {
        var env = Create(Failed);

        var summary = await env.Orchestrator.RunUserForcedRefreshAsync(Today);

        Assert.Single(summary.Results, r => r.Category == SourceCategory.ShowSchedule && r.Outcome == SourceOperationOutcomeKind.Failure);
        Assert.Single(summary.Results, r => r.Category == SourceCategory.ParkHours && r.Outcome == SourceOperationOutcomeKind.Success);
        Assert.Equal(RetrievalStatus.Verified, (await env.Dated.GetAsync(new DatedResultKey(SourceCategory.ParkHours, Today)))!.SuccessEvidence!.Status);
    }

    [Fact]
    public async Task ShowFailure_AfterVerified_PreservesLastKnownGood()
    {
        var failNext = false;
        var env = Create(request => failNext ? Failed(request) : Verified(request));
        await env.Orchestrator.RunUserForcedRefreshAsync(Today);

        failNext = true;
        await env.Orchestrator.RunUserForcedRefreshAsync(Today);

        var view = ShowScheduleView.Compose(Today, await env.Dated.GetAsync(new DatedResultKey(SourceCategory.ShowSchedule, Today)), Now);
        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.True(view.LastCheckFailed);
    }

    [Fact]
    public async Task CheckOnline_ForAPastDate_NeverRequestsShows_AndNeverOverwritesTheRecord()
    {
        var env = Create(Verified);
        var past = Today.AddDays(-3);
        await env.Dated.RecordSuccessAsync(new DatedResultKey(SourceCategory.ShowSchedule, past), FakeEvidence.Verified(Now.AddDays(-3), past),
            JsonSerializer.Serialize(new ShowSchedulePayload([new ScheduledShow("SHOW-030-00058", "Costume Contest", null, [new TimeOnly(17, 0)])])));

        var summary = await env.Orchestrator.RunUserForcedRefreshAsync(past);

        Assert.Empty(env.Shows.Requests);
        Assert.Single(summary.Results, r => r.Category == SourceCategory.ShowSchedule && r.Outcome == SourceOperationOutcomeKind.Success);
        var view = ShowScheduleView.Compose(past, await env.Dated.GetAsync(new DatedResultKey(SourceCategory.ShowSchedule, past)), Now);
        Assert.Equal("Costume Contest", Assert.Single(view.Shows).Name);
    }

    [Fact]
    public async Task ComprehensiveCheck_IsBoundedToTheRollingWindow_TodayFirst()
    {
        var env = Create(Verified);

        await env.Orchestrator.RunComprehensiveRefreshAsync();

        var dates = env.Shows.Requests.Select(r => r.RequestedDate!.Value).ToList();
        Assert.Equal(RollingDatePlanner.DefaultWindowLengthDays, dates.Count);
        Assert.Equal(Today, dates.Min());
        Assert.Equal(Today.AddDays(RollingDatePlanner.DefaultWindowLengthDays - 1), dates.Max());
    }

    [Fact]
    public async Task DeepRefresh_IncludesShows_ForTheNormalWindowOnly()
    {
        var env = Create(Verified);

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: true);

        Assert.Equal(RollingDatePlanner.DefaultWindowLengthDays, env.Shows.Requests.Count);
        Assert.True(env.Hours.Requests.Count >= env.Shows.Requests.Count);
    }

    [Fact]
    public void DateCheckFreshness_MissingShowSchedule_MakesTheDateStale()
    {
        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var hoursOnly = new DatedResultRecord(new DatedResultKey(SourceCategory.ParkHours, Today), FakeEvidence.Verified(Now, Today), "{}", FakeEvidence.Verified(Now, Today));

        Assert.True(DateCheckFreshness.IsMissingOrStale(Today, [hoursOnly], [SourceCategory.ParkHours, SourceCategory.ShowSchedule], policies, Now));
        Assert.False(DateCheckFreshness.IsMissingOrStale(Today, [hoursOnly], [SourceCategory.ParkHours], policies, Now));
    }

    [Fact]
    public void FreshnessPolicy_CoversTheShowSchedule()
    {
        Assert.True(FreshnessPolicyCatalog.Build(new FreshnessOptions()).ContainsKey(SourceCategory.ShowSchedule));
    }
}
