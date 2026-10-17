using System.Text.Json;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Refresh;

/// <summary>
/// Phase 22E.1: Deep Refresh's forward published-schedule discovery — one official month request per
/// month through the horizon, near-term single-date priority, last-known-good and publication rules,
/// coverage, maintenance cadence, Calendar projection and isolation from shows and history.
/// </summary>
public sealed class Phase22E1PublishedScheduleTests
{
    /// <summary>10:00 AM EDT on 2026-10-17 — after the 6:00 AM boundary.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/park-hours");

    // ------------------------------------------------------------------------------ test doubles

    private sealed class FakeMonthSource : IParkHoursScheduleSource
    {
        public string SourceId => "park-hours.official";

        public List<(int Year, int Month)> Requests { get; } = [];

        /// <summary>Per-month answer; a month without an entry answers every date NotPublished (the far-future shape).</summary>
        public Dictionary<(int Year, int Month), Func<PublishedParkHoursMonth>> Months { get; } = [];

        public Func<int, int, CancellationToken, Task>? BeforeAnswer { get; set; }

        public async Task<PublishedParkHoursMonth> RetrieveMonthAsync(int year, int month, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add((year, month));
            }

            if (BeforeAnswer is not null)
            {
                await BeforeAnswer(year, month, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Months.TryGetValue((year, month), out var answer)
                ? answer()
                : WholeMonth(year, month, NotPublished);
        }
    }

    private static SourceEvidence Evidence(DateOnly date, RetrievalStatus status, string detail = "Official.") =>
        new("Carowinds Park Hours", Url, Now, status, detail, RetrievalStrategy.DirectApi, date,
            confirmedDate: status == RetrievalStatus.Error ? null : date,
            technicalDetail: status == RetrievalStatus.Error ? "Simulated." : null);

    private static SourceResult<ParkHoursPayload> Verified(DateOnly date, int open, int close) =>
        SourceResult<ParkHoursPayload>.Success("park-hours.official", Evidence(date, RetrievalStatus.Verified), new ParkHoursPayload(new TimeOnly(open, 0), new TimeOnly(close, 0)));

    private static SourceResult<ParkHoursPayload> Closed(DateOnly date) =>
        SourceResult<ParkHoursPayload>.WithoutPayload("park-hours.official", Evidence(date, RetrievalStatus.Closed));

    private static SourceResult<ParkHoursPayload> NotPublished(DateOnly date) =>
        SourceResult<ParkHoursPayload>.WithoutPayload("park-hours.official", Evidence(date, RetrievalStatus.NotPublished));

    private static SourceResult<ParkHoursPayload> Unreadable(DateOnly date) =>
        SourceResult<ParkHoursPayload>.WithoutPayload("park-hours.official", Evidence(date, RetrievalStatus.Error));

    private static PublishedParkHoursMonth WholeMonth(int year, int month, Func<DateOnly, SourceResult<ParkHoursPayload>> perDate, Func<DateOnly, bool>? listed = null)
    {
        var start = new DateOnly(year, month, 1);
        var dates = new List<SourceResult<ParkHoursPayload>>();
        for (var date = start; date.Month == month; date = date.AddDays(1))
        {
            if (listed?.Invoke(date) ?? true)
            {
                dates.Add(perDate(date));
            }
        }

        return new PublishedParkHoursMonth(year, month, dates, Failure: null);
    }

    private static PublishedParkHoursMonth FailedMonth(int year, int month) =>
        new(year, month, [], Evidence(new DateOnly(year, month, 1), RetrievalStatus.Error, "We couldn't check the published park schedule for this month right now."));

    /// <summary>December 2026 as observed live: WinterFest weekends and Dec 18–23/26–31 open, the rest confirmed closed.</summary>
    private static PublishedParkHoursMonth December(int closeHourOnDec5 = 21) =>
        WholeMonth(2026, 12, date => date.Day switch
        {
            5 => Verified(date, 12, closeHourOnDec5),
            6 or 12 or 13 or (>= 18 and <= 23) or (>= 26 and <= 31) => Verified(date, 12, 20),
            _ => Closed(date),
        });

    private sealed record Env(
        RefreshOrchestrator Orchestrator,
        FakeMonthSource Months,
        InMemoryDatedResultRepository Dated,
        InMemorySourceStateRepository States,
        InMemoryDeepRefreshStatusRepository Deep,
        List<string> DispatchOrder);

    private static Env CreateEnv(int monthsAhead = ParkHoursCoveragePlanner.DefaultDiscoveryMonths, bool withShows = false, bool withAdmission = false, int maxConcurrency = 3)
    {
        var clock = new FakeClock(Now);
        var dated = new InMemoryDatedResultRepository();
        var states = new InMemorySourceStateRepository();
        var deep = new InMemoryDeepRefreshStatusRepository();
        var order = new List<string>();
        var months = new FakeMonthSource
        {
            BeforeAnswer = (y, m, _) =>
            {
                lock (order) { order.Add($"month:{y}-{m:00}"); }
                return Task.CompletedTask;
            },
        };

        DatedSourceRegistration<FakePayload> Daily(string id, SourceCategory category) =>
            new(new FakeOfficialDataSource<FakePayload>(id, category, (request, _) =>
            {
                lock (order) { order.Add($"{id}:{request.RequestedDate:yyyy-MM-dd}"); }
                return Task.FromResult(SourceResult<FakePayload>.Success(id, FakeEvidence.Verified(Now, request.RequestedDate), new FakePayload("ok")));
            }), dated);

        var registrations = new List<SourceRegistration> { Daily("park-hours.official", SourceCategory.ParkHours) };
        if (withAdmission)
        {
            registrations.Add(Daily("dated-admission.official", SourceCategory.DatedAdmissionTickets));
        }

        if (withShows)
        {
            registrations.Add(new CurrentAndFutureDatedSourceRegistration<FakePayload>(
                new FakeOfficialDataSource<FakePayload>("show-schedule.official", SourceCategory.ShowSchedule, (request, _) =>
                {
                    lock (order) { order.Add($"show-schedule.official:{request.RequestedDate:yyyy-MM-dd}"); }
                    return Task.FromResult(SourceResult<FakePayload>.Success("show-schedule.official", FakeEvidence.Verified(Now, request.RequestedDate), new FakePayload("shows")));
                }), dated, clock));
        }

        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var orchestrator = new RefreshOrchestrator(
            registrations,
            new FreshnessPlanner(states, clock, policies),
            new RollingDatePlanner(clock, new RollingWindowOptions()),
            states,
            policies,
            new FakeJobBrowserSessionFactory(),
            clock,
            new RefreshOrchestratorOptions { MaxConcurrency = maxConcurrency },
            deep,
            new ParkHoursScheduleDiscovery(months, dated, monthsAhead));

        return new Env(orchestrator, months, dated, states, deep, order);
    }

    private static async Task<DatedResultRecord?> StoredAsync(Env env, DateOnly date) =>
        await env.Dated.GetAsync(new DatedResultKey(SourceCategory.ParkHours, date));

    private static ParkHoursPayload? Payload(DatedResultRecord? record) =>
        record?.SuccessPayloadJson is { } json ? JsonSerializer.Deserialize<ParkHoursPayload>(json) : null;

    private static Task SeedAsync(Env env, SourceResult<ParkHoursPayload> result) =>
        env.Dated.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, result.Evidence.RequestedDate!.Value),
            result.Evidence,
            result.Payload is null ? null : JsonSerializer.Serialize(result.Payload));

    // ------------------------------------------------------------------------- planning / bounds

    [Fact]
    public async Task DeepRefresh_RollingWindowByDate_ThenOneRequestPerMonthThroughTwelveMonths()
    {
        var env = CreateEnv();

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        var daily = summary.Results.Where(r => r.CoveredDates is null).Select(r => r.RequestedDate!.Value).Order().ToList();
        Assert.Equal(Enumerable.Range(0, 7).Select(Today.AddDays), daily);
        Assert.Equal(13, env.Months.Requests.Count);
        Assert.Equal((2026, 10), env.Months.Requests.Min());
        Assert.Equal((2027, 10), env.Months.Requests.Max());
        Assert.Equal(20, summary.OperationsAttempted); // previously 45 single-date requests (Oct 17 → Nov 30)
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(1, 2)]
    [InlineData(100, ParkHoursCoveragePlanner.MaxDiscoveryMonths + 1)]
    public async Task DeepRefresh_MonthRequestsBoundedByConfiguredHorizon(int monthsAhead, int expectedMonthRequests)
    {
        var env = CreateEnv(monthsAhead);

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        Assert.Equal(expectedMonthRequests, env.Months.Requests.Count);
        Assert.Equal(env.Months.Requests.Count, env.Months.Requests.Distinct().Count());
    }

    [Fact]
    public async Task DeepRefresh_TodayAndNearTermDatesDispatchBeforeAnyMonthRequest()
    {
        var env = CreateEnv(withAdmission: true, maxConcurrency: 1);

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: true);

        var firstMonth = env.DispatchOrder.FindIndex(s => s.StartsWith("month:", StringComparison.Ordinal));
        Assert.Equal($"park-hours.official:{Today:yyyy-MM-dd}", env.DispatchOrder[0]);
        Assert.True(env.DispatchOrder.IndexOf($"dated-admission.official:{Today:yyyy-MM-dd}") < firstMonth);
        Assert.All(env.DispatchOrder.Take(firstMonth), s => Assert.DoesNotContain("month:", s));
        Assert.Equal(13, env.DispatchOrder.Count(s => s.StartsWith("month:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DeepRefresh_NeverRequestsAPastMonth()
    {
        var env = CreateEnv();

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: true);

        Assert.All(env.Months.Requests, m => Assert.True(new DateOnly(m.Year, m.Month, 1) >= new DateOnly(Today.Year, Today.Month, 1)));
    }

    [Fact]
    public async Task DeepRefresh_FocusDateBeyondHorizon_GetsItsOwnSingleDateRequest()
    {
        var env = CreateEnv(monthsAhead: 2);
        var focus = new DateOnly(2027, 3, 1);

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: true, focusDate: focus);

        Assert.Contains(summary.Results, r => r.CoveredDates is null && r.RequestedDate == focus);
    }

    // ---------------------------------------------------------------- December while it is October

    [Fact]
    public async Task DeepRefresh_InOctober_StoresDecemberPublishedHoursAndClosures()
    {
        var env = CreateEnv();
        env.Months.Months[(2026, 12)] = () => December();

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        var dec5 = await StoredAsync(env, new DateOnly(2026, 12, 5));
        Assert.Equal(RetrievalStatus.Verified, dec5!.SuccessEvidence!.Status);
        Assert.Equal(new ParkHoursPayload(new TimeOnly(12, 0), new TimeOnly(21, 0)), Payload(dec5));
        Assert.Equal(RetrievalStatus.Closed, (await StoredAsync(env, new DateOnly(2026, 12, 25)))!.SuccessEvidence!.Status);
        Assert.Equal(RetrievalStatus.NotPublished, (await StoredAsync(env, new DateOnly(2027, 3, 10)))!.SuccessEvidence!.Status);
    }

    [Fact]
    public async Task DeepRefresh_MonthResultsNeverOverwriteTheRollingWindow()
    {
        var env = CreateEnv();
        env.Months.Months[(2026, 10)] = () => WholeMonth(2026, 10, Closed, d => d > Today);

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: true);

        for (var date = Today; date <= Today.AddDays(6); date = date.AddDays(1))
        {
            Assert.Equal("Verified.", (await StoredAsync(env, date))!.SuccessEvidence!.Detail); // the daily request's value
        }

        Assert.Equal(RetrievalStatus.Closed, (await StoredAsync(env, Today.AddDays(7)))!.SuccessEvidence!.Status);
    }

    [Fact]
    public async Task DeepRefresh_CoverageReachesTheHorizon_AndRecordsSuccess()
    {
        var env = CreateEnv();
        env.Months.Months[(2027, 10)] = () => WholeMonth(2027, 10, NotPublished, d => d.Day <= 17); // the feed ends one year out

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        var status = await env.Deep.GetAsync();
        Assert.Equal(new DateOnly(2027, 10, 17), status!.CoverageThrough);
        Assert.Equal(DeepRefreshOutcome.Success, status.Outcome);
        Assert.False(DeepRefreshCoverage.NeedsScheduleDiscovery(status, Today));
        Assert.False(DeepRefreshCoverage.NeedsDeepRefresh(status, Today));
    }

    // ------------------------------------------------------------------ later publication / changes

    [Fact]
    public async Task FutureMonthPublishedLater_AppearsOnTheNextRun()
    {
        var env = CreateEnv();

        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);
        Assert.Equal(RetrievalStatus.NotPublished, (await StoredAsync(env, new DateOnly(2027, 1, 2)))!.SuccessEvidence!.Status);

        env.Months.Months[(2027, 1)] = () => WholeMonth(2027, 1, d => d.Day <= 3 ? Verified(d, 12, 20) : Closed(d));
        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        Assert.Equal(new TimeOnly(20, 0), Payload(await StoredAsync(env, new DateOnly(2027, 1, 2)))!.ClosingTime);
        Assert.Equal(RetrievalStatus.Closed, (await StoredAsync(env, new DateOnly(2027, 1, 10)))!.SuccessEvidence!.Status);
    }

    [Fact]
    public async Task OfficialScheduleChangedLater_UpdatesHoursAndNewClosures()
    {
        var env = CreateEnv();
        env.Months.Months[(2026, 12)] = () => December();
        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        env.Months.Months[(2026, 12)] = () => WholeMonth(2026, 12, date => date.Day switch
        {
            5 => Verified(date, 12, 22),
            12 => Closed(date),
            _ => December().Dates.Single(r => r.Evidence.RequestedDate == date),
        });
        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        Assert.Equal(new TimeOnly(22, 0), Payload(await StoredAsync(env, new DateOnly(2026, 12, 5)))!.ClosingTime);
        Assert.Equal(RetrievalStatus.Closed, (await StoredAsync(env, new DateOnly(2026, 12, 12)))!.SuccessEvidence!.Status);
    }

    [Fact]
    public async Task NotPublishedInAMonthListing_NeverErasesVerifiedHoursOrConfirmedClosure()
    {
        var env = CreateEnv();
        await SeedAsync(env, Verified(new DateOnly(2026, 12, 19), 12, 21));
        await SeedAsync(env, Closed(new DateOnly(2026, 12, 24)));
        env.Months.Months[(2026, 12)] = () => WholeMonth(2026, 12, NotPublished);

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        Assert.Equal(new TimeOnly(21, 0), Payload(await StoredAsync(env, new DateOnly(2026, 12, 19)))!.ClosingTime);
        Assert.Equal(RetrievalStatus.Closed, (await StoredAsync(env, new DateOnly(2026, 12, 24)))!.SuccessEvidence!.Status);
        Assert.Equal(RetrievalStatus.NotPublished, (await StoredAsync(env, new DateOnly(2026, 12, 20)))!.SuccessEvidence!.Status);
        var december = summary.Results.Single(r => r.RequestedDate == new DateOnly(2026, 12, 1) && r.CoveredDates is not null);
        Assert.Contains("2 kept their earlier verified result", december.Detail, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- partial / failure / LKG

    [Fact]
    public async Task PartialMonth_MissingDatesUntouchedNeverClosed_AndCoverageStopsAtTheGap()
    {
        var env = CreateEnv();
        await SeedAsync(env, Verified(new DateOnly(2026, 11, 14), 11, 18));
        env.Months.Months[(2026, 11)] = () => WholeMonth(2026, 11, d => Verified(d, 11, 19), d => d.Day is not (14 or 15));

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        Assert.Equal(new TimeOnly(18, 0), Payload(await StoredAsync(env, new DateOnly(2026, 11, 14)))!.ClosingTime); // untouched
        Assert.Equal(new TimeOnly(19, 0), Payload(await StoredAsync(env, new DateOnly(2026, 11, 13)))!.ClosingTime);
        Assert.Null(await StoredAsync(env, new DateOnly(2026, 11, 15)));
        Assert.Equal(new DateOnly(2026, 11, 13), (await env.Deep.GetAsync())!.CoverageThrough);
        var november = summary.Results.Single(r => r.RequestedDate == new DateOnly(2026, 11, 1) && r.CoveredDates is not null);
        Assert.Contains("2 not listed by the official source", november.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedMonth_KeepsLastKnownGood_AndOtherMonthsStillStored()
    {
        var env = CreateEnv();
        await SeedAsync(env, Verified(new DateOnly(2026, 12, 5), 12, 21));
        env.Months.Months[(2026, 12)] = () => FailedMonth(2026, 12);
        env.Months.Months[(2027, 1)] = () => WholeMonth(2027, 1, Closed);

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        var dec5 = await StoredAsync(env, new DateOnly(2026, 12, 5));
        Assert.Equal(new TimeOnly(21, 0), Payload(dec5)!.ClosingTime);
        Assert.Equal(RetrievalStatus.Verified, dec5!.LastAttemptEvidence.Status); // nothing written for a failed month
        Assert.Null(await StoredAsync(env, new DateOnly(2026, 12, 6)));
        Assert.Equal(RetrievalStatus.Closed, (await StoredAsync(env, new DateOnly(2027, 1, 9)))!.SuccessEvidence!.Status);

        var status = await env.Deep.GetAsync();
        Assert.Equal(DeepRefreshOutcome.Partial, status!.Outcome);
        Assert.Equal(new DateOnly(2026, 11, 30), status.CoverageThrough);
        Assert.Equal(1, summary.Failed);
        var parkState = await env.States.GetAsync("park-hours.official");
        Assert.Equal(1, parkState!.ConsecutiveFailureCount); // stays due for a retry
    }

    [Fact]
    public async Task UnreadableEntry_RecordsAFailedAttemptOnly_KeepingThatDatesValue()
    {
        var env = CreateEnv();
        await SeedAsync(env, Verified(new DateOnly(2026, 12, 6), 12, 20));
        env.Months.Months[(2026, 12)] = () => WholeMonth(2026, 12, d => d.Day == 6 ? Unreadable(d) : December().Dates.Single(r => r.Evidence.RequestedDate == d));

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        var dec6 = await StoredAsync(env, new DateOnly(2026, 12, 6));
        Assert.Equal(new TimeOnly(20, 0), Payload(dec6)!.ClosingTime);
        Assert.Equal(RetrievalStatus.Error, dec6!.LastAttemptEvidence.Status);
        Assert.Equal(new TimeOnly(21, 0), Payload(await StoredAsync(env, new DateOnly(2026, 12, 5)))!.ClosingTime);
        var december = summary.Results.Single(r => r.RequestedDate == new DateOnly(2026, 12, 1) && r.CoveredDates is not null);
        Assert.Equal(SourceOperationOutcomeKind.Failure, december.Outcome);
        Assert.Equal(new DateOnly(2026, 12, 5), (await env.Deep.GetAsync())!.CoverageThrough);
    }

    [Fact]
    public async Task Cancellation_LeavesStoredValues_AndSkipsSourceStateUpdate()
    {
        var env = CreateEnv(maxConcurrency: 1);
        await SeedAsync(env, Verified(new DateOnly(2027, 2, 6), 10, 18));
        using var cts = new CancellationTokenSource();
        env.Months.BeforeAnswer = (y, m, _) =>
        {
            if ((y, m) == (2027, 1))
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        };

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false, cancellationToken: cts.Token);

        Assert.True(summary.Cancelled > 0);
        Assert.Equal(new TimeOnly(18, 0), Payload(await StoredAsync(env, new DateOnly(2027, 2, 6)))!.ClosingTime);
        Assert.Null(await env.States.GetAsync("park-hours.official"));
    }

    // ------------------------------------------------------------------------ show isolation

    [Fact]
    public async Task ShowSchedule_StaysOnTheRollingWindow_NeverTheParkHoursHorizon()
    {
        var env = CreateEnv(withShows: true);

        var summary = await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: true);

        var showDates = summary.Results.Where(r => r.Category == SourceCategory.ShowSchedule).Select(r => r.RequestedDate!.Value).ToList();
        Assert.Equal(7, showDates.Count);
        Assert.True(showDates.Max() <= Today.AddDays(6));
        Assert.Empty(await env.Dated.GetByCategoryAndDateRangeAsync(SourceCategory.ShowSchedule, Today.AddDays(7), Today.AddMonths(12)));
    }

    // ----------------------------------------------------------------- maintenance cadence

    private static FreshnessPolicy DailyPolicy => FreshnessPolicyCatalog.Build(new FreshnessOptions())[SourceCategory.ParkHours];

    private static DeepRefreshStatusRecord Status(DateTimeOffset attempt, DateOnly? coverage) =>
        new(attempt, attempt, DeepRefreshOutcome.Success, coverage, 10, 0);

    [Fact]
    public void Maintenance_OldEndOfNextMonthCoverage_DueOnceFourHoursPassed()
    {
        var oldCoverage = ParkHoursCoveragePlanner.TargetHorizon(Today); // Nov 30 — what Phase 13D maintained
        var ranAt = new DateTimeOffset(2026, 10, 17, 10, 5, 0, TimeSpan.Zero); // 6:05 AM EDT

        Assert.False(OfficialRefreshPolicy.IsDailyMaintenanceDue(Status(ranAt, oldCoverage), DailyPolicy, ranAt.AddHours(3.9)));
        Assert.True(OfficialRefreshPolicy.IsDailyMaintenanceDue(Status(ranAt, oldCoverage), DailyPolicy, ranAt.AddHours(4)));
        Assert.False(DeepRefreshCoverage.NeedsDeepRefresh(Status(ranAt, oldCoverage), Today)); // Check Online stays single-date
    }

    [Fact]
    public void Maintenance_FullCoverage_NotDueUntilTheNextDailyBoundary()
    {
        var ranAt = new DateTimeOffset(2026, 10, 17, 10, 5, 0, TimeSpan.Zero);
        var full = ParkHoursCoveragePlanner.DiscoveryHorizon(Today);

        Assert.False(OfficialRefreshPolicy.IsDailyMaintenanceDue(Status(ranAt, full), DailyPolicy, ranAt.AddHours(12)));
        Assert.True(OfficialRefreshPolicy.IsDailyMaintenanceDue(Status(ranAt, full), DailyPolicy, new DateTimeOffset(2026, 10, 18, 10, 1, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Maintenance_HorizonFollowsConfiguredMonths()
    {
        var ranAt = new DateTimeOffset(2026, 10, 17, 10, 5, 0, TimeSpan.Zero);
        var threeMonths = ParkHoursCoveragePlanner.DiscoveryHorizon(Today, 3);

        Assert.False(OfficialRefreshPolicy.IsDailyMaintenanceDue(Status(ranAt, threeMonths), DailyPolicy, ranAt.AddHours(5), discoveryMonths: 3));
        Assert.True(OfficialRefreshPolicy.IsDailyMaintenanceDue(Status(ranAt, threeMonths), DailyPolicy, ranAt.AddHours(5)));
    }

    [Fact]
    public void DiscoveryHorizon_IsOneYearAndClamped()
    {
        Assert.Equal(new DateOnly(2027, 10, 17), ParkHoursCoveragePlanner.DiscoveryHorizon(Today));
        Assert.Equal(Today.AddMonths(1), ParkHoursCoveragePlanner.DiscoveryHorizon(Today, 0));
        Assert.Equal(Today.AddMonths(ParkHoursCoveragePlanner.MaxDiscoveryMonths), ParkHoursCoveragePlanner.DiscoveryHorizon(Today, 99));
        Assert.Equal(13, ParkHoursCoveragePlanner.MonthsBetween(Today, ParkHoursCoveragePlanner.DiscoveryHorizon(Today)).Count);
    }

    // --------------------------------------------------------------------- Calendar projection

    [Fact]
    public async Task Calendar_ShowsDiscoveredDecemberWithoutAnyRequest_AndUnlistedDatesAreNotChecked()
    {
        var env = CreateEnv();
        env.Months.Months[(2026, 12)] = () => WholeMonth(2026, 12, d => December().Dates.Single(r => r.Evidence.RequestedDate == d), d => d.Day != 30);
        await env.Orchestrator.RunDeepRefreshAsync(bypassFreshness: false);

        var clock = new FakeClock(Now);
        var refresh = new FakeRefreshOrchestrator();
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var drafts = new InMemoryAssociateDraftRepository();
        var associate = new AssociatePassdownService(drafts, env.Dated, new InMemoryCatalogProductRepository(), refresh, notes, clock);
        var calendar = new OperationsCalendarService(associate, drafts, env.Dated, new InMemoryFinalizedPassdownRepository(), notes, clock);

        var snapshot = await calendar.LoadAsync(new DateOnly(2026, 12, 1), Today);

        OperationsCalendarDay Day(int day) => snapshot.Days.Single(d => d.Date == new DateOnly(2026, 12, day));
        Assert.Equal(OperationsCalendarOperatingState.Open, Day(5).OperatingState);
        Assert.Equal(new TimeOnly(21, 0), Day(5).ParkHours.OfficialClosing);
        Assert.Equal(OperationsCalendarOperatingState.Closed, Day(25).OperatingState);
        Assert.Equal(OperationsCalendarOperatingState.NotChecked, Day(30).OperatingState); // unlisted: never inferred closed
        Assert.Empty(refresh.ManualDateRefreshCalls); // the Calendar never goes online
        Assert.Empty(refresh.DeepRefreshCalls);
    }
}
