using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.HistoricalBackfill;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.HistoricalBackfill;

/// <summary>
/// Phase 20C: the current-year historical park-hours backfill — scope, newest-first order, resume,
/// idempotence, no fabrication, failure pacing, year rollover, Calendar month promotion, and yielding
/// to every kind of operational refresh through the shared gate. Retrieval is a scripted fake; the
/// real adapter and real SQLite are covered by the Infrastructure Phase 20C tests.
/// </summary>
public sealed class HistoricalBackfillServiceTests
{
    /// <summary>12:00 PM Eastern (EDT) on 2026-10-16.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly int PastDatesThisYear = Today.DayNumber - Jan1.DayNumber; // Jan 1 .. Oct 15

    private static readonly HistoricalBackfillOptions FastOptions = new()
    {
        StartDelay = TimeSpan.Zero,
        RequestSpacing = TimeSpan.Zero,
        YieldQuietPeriod = TimeSpan.FromMilliseconds(5),
    };

    private sealed class ScriptedHistorySource : IParkHoursHistorySource
    {
        private int _inFlight;

        public string SourceId => "park-hours.official";

        public List<DateOnly> Requests { get; } = [];

        public Func<DateOnly, HistoricalParkHoursResult> Respond { get; set; } = date => Verified(date);

        /// <summary>Runs before answering (block, record order, cancel the caller…).</summary>
        public Func<DateOnly, CancellationToken, Task>? Before { get; set; }

        public int MaxConcurrent { get; private set; }

        public bool InFlight => Volatile.Read(ref _inFlight) > 0;

        public async Task<HistoricalParkHoursResult> RetrieveHistoricalAsync(DateOnly date, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(date);
            }

            var now = Interlocked.Increment(ref _inFlight);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                if (Before is not null)
                {
                    await Before(date, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return Respond(date);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/park-hours");

    private static SourceEvidence Evidence(DateOnly date, RetrievalStatus status, DateTimeOffset? at = null) =>
        new("Carowinds Park Hours", Url, at ?? Now, status, $"{status}.", RetrievalStrategy.DirectApi, date, confirmedDate: status == RetrievalStatus.Error ? null : date,
            technicalDetail: status == RetrievalStatus.Error ? "Simulated." : null);

    private static HistoricalParkHoursResult Verified(DateOnly date) =>
        new(SourceResult<ParkHoursPayload>.Success("park-hours.official", Evidence(date, RetrievalStatus.Verified), new ParkHoursPayload(new TimeOnly(10, 0), new TimeOnly(19, 0))), false);

    private static HistoricalParkHoursResult Without(DateOnly date, RetrievalStatus status, bool notRetained = false) =>
        new(SourceResult<ParkHoursPayload>.WithoutPayload("park-hours.official", Evidence(date, status)), notRetained);

    private sealed record Harness(
        HistoricalBackfillService Service,
        ScriptedHistorySource Source,
        InMemoryDatedResultRepository Repository,
        OfficialRefreshGate Gate,
        FakeScheduledRefreshLock Lock,
        FakeClock Clock,
        List<DataChange> Changes);

    private static Harness Create(
        DateTimeOffset? now = null,
        InMemoryDatedResultRepository? repository = null,
        Func<Task>? startupCompletion = null,
        HistoricalBackfillOptions? options = null)
    {
        var clock = new FakeClock(now ?? Now);
        var source = new ScriptedHistorySource();
        repository ??= new InMemoryDatedResultRepository();
        var crossProcessLock = new FakeScheduledRefreshLock();
        var gate = new OfficialRefreshGate(() => crossProcessLock);
        var changes = new List<DataChange>();
        var notifier = new DataChangeNotifier();
        notifier.Changed += (_, change) => { lock (changes) { changes.Add(change); } };
        var service = new HistoricalBackfillService(source, repository, gate, clock, startupCompletion, notifier, logger: null, delayAsync: null, options ?? FastOptions);
        return new Harness(service, source, repository, gate, crossProcessLock, clock, changes);
    }

    private static async Task SeedAsync(InMemoryDatedResultRepository repository, DateOnly date, RetrievalStatus status, DateTimeOffset checkedAt)
    {
        var payload = status == RetrievalStatus.Verified ? """{"OpeningTime":"10:00:00","ClosingTime":"19:00:00"}""" : null;
        await repository.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), Evidence(date, status, checkedAt), payload);
    }

    private static DateTimeOffset NoonEasternOn(DateOnly date) => new(date.Year, date.Month, date.Day, 16, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<Persistence.DatedResultRecord>> StoredAsync(Harness h, DateOnly start, DateOnly end) =>
        await h.Repository.GetByCategoryAndDateRangeAsync(SourceCategory.ParkHours, start, end);

    // ------------------------------------------------------------------ scope

    [Theory]
    [InlineData("2026-10-16", "2026-01-01", "2026-10-15")]
    [InlineData("2026-12-31", "2026-01-01", "2026-12-30")]
    [InlineData("2027-01-02", "2027-01-01", "2027-01-01")]
    [InlineData("2026-01-01", null, null)]
    public void TargetRange_IsJanuaryFirstThroughYesterday_OfTheCurrentYearOnly(string today, string? start, string? end)
    {
        var range = HistoricalBackfillPolicy.TargetRange(DateOnly.Parse(today));

        if (start is null)
        {
            Assert.Null(range);
        }
        else
        {
            Assert.Equal((DateOnly.Parse(start), DateOnly.Parse(end!)), range);
        }
    }

    [Fact]
    public async Task FreshInstall_ChecksEveryPastDateOfTheYear_NewestFirst_NeverTodayOrLater()
    {
        var h = Create();

        await h.Service.RunSessionAsync();

        Assert.Equal(PastDatesThisYear, h.Source.Requests.Count);
        Assert.Equal(Today.AddDays(-1), h.Source.Requests[0]); // yesterday first
        Assert.Equal(Today.AddDays(-2), h.Source.Requests[1]);
        Assert.Equal(Jan1, h.Source.Requests[^1]);
        Assert.Equal(h.Source.Requests.OrderByDescending(d => d), h.Source.Requests);
        Assert.All(h.Source.Requests, d => Assert.True(d < Today && d.Year == 2026));
        Assert.Equal(PastDatesThisYear, (await StoredAsync(h, Jan1, Today)).Count);

        var status = h.Service.CurrentStatus;
        Assert.Equal(HistoricalBackfillState.UpToDate, status.State);
        Assert.Equal(PastDatesThisYear, status.DatesComplete);
        Assert.Equal(PastDatesThisYear, status.SessionQueued);
        Assert.Equal(PastDatesThisYear, status.SessionChecked);
        Assert.Equal(0, status.DatesRemaining);
    }

    [Fact]
    public async Task OfficialFeedRetentionBoundary_IsProbedOnce_OlderDatesAreNeverStoredOrGuessed()
    {
        var cutoff = new DateOnly(2026, 3, 25);
        var h = Create();
        h.Source.Respond = d => d < cutoff ? Without(d, RetrievalStatus.Error, notRetained: true) : Verified(d);

        await h.Service.RunSessionAsync();

        var retained = Today.DayNumber - cutoff.DayNumber;
        Assert.Equal(retained + 1, h.Source.Requests.Count); // every retained date + one "Not Found" probe
        Assert.Equal(cutoff.AddDays(-1), h.Source.Requests[^1]);
        Assert.Empty(await StoredAsync(h, Jan1, cutoff.AddDays(-1))); // nothing invented for Jan 1 – Mar 24
        Assert.Equal(retained, (await StoredAsync(h, cutoff, Today)).Count);

        var status = h.Service.CurrentStatus;
        Assert.Equal(HistoricalBackfillState.PartiallyAvailable, status.State);
        Assert.Equal(cutoff.AddDays(-1), status.OldestNotRetainedDate);
        Assert.Equal(0, status.SessionFailed);

        // Re-running converges: the stored dates are complete, only the boundary is re-probed.
        h.Source.Requests.Clear();
        await h.Service.RunSessionAsync();
        Assert.Equal([cutoff.AddDays(-1)], h.Source.Requests);
    }

    [Fact]
    public async Task ExistingInstall_JanuaryThroughMayStored_OnlyJuneOnwardIsChecked_StoredRowsUntouched()
    {
        var repository = new InMemoryDatedResultRepository();
        var june1 = new DateOnly(2026, 6, 1);
        for (var d = Jan1; d < june1; d = d.AddDays(1))
        {
            await SeedAsync(repository, d, d.DayOfWeek == DayOfWeek.Tuesday ? RetrievalStatus.Closed : RetrievalStatus.Verified, NoonEasternOn(d));
        }

        var before = (await repository.GetByCategoryAndDateRangeAsync(SourceCategory.ParkHours, Jan1, june1.AddDays(-1))).ToList();
        var h = Create(repository: repository);

        await h.Service.RunSessionAsync();

        Assert.Equal(Today.DayNumber - june1.DayNumber, h.Source.Requests.Count);
        Assert.All(h.Source.Requests, d => Assert.True(d >= june1));
        Assert.Equal(h.Source.Requests.Count, h.Source.Requests.Distinct().Count());
        Assert.Equal(before, await StoredAsync(h, Jan1, june1.AddDays(-1))); // same records, same timestamps
        Assert.Equal(PastDatesThisYear, (await StoredAsync(h, Jan1, Today)).Count); // one row per date, no duplicates
        Assert.Equal(HistoricalBackfillState.UpToDate, h.Service.CurrentStatus.State);
    }

    [Fact]
    public async Task Interrupted_ThenRestarted_ResumesWithTheRemainingDates_NoRepeatedRequests()
    {
        var repository = new InMemoryDatedResultRepository();
        using var stop = new CancellationTokenSource();
        var first = Create(repository: repository);
        first.Source.Before = (_, _) =>
        {
            if (first.Source.Requests.Count == 40)
            {
                stop.Cancel(); // "user closes the app" right after the 40th answer
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Service.RunSessionAsync(stop.Token));
        var stored = await StoredAsync(first, Jan1, Today);
        Assert.Equal(39, stored.Count); // the 40th request was cut off and wrote nothing

        var second = Create(repository: repository);
        await second.Service.RunSessionAsync();

        var completedFirst = stored.Select(r => r.Key.Date).ToHashSet();
        Assert.DoesNotContain(second.Source.Requests, completedFirst.Contains);
        Assert.Equal(PastDatesThisYear - 39, second.Source.Requests.Count);
        Assert.Equal(PastDatesThisYear, (await StoredAsync(second, Jan1, Today)).Count);
        Assert.Equal(HistoricalBackfillState.UpToDate, second.Service.CurrentStatus.State);

        // A third launch has nothing left to do.
        var third = Create(repository: repository);
        await third.Service.RunSessionAsync();
        Assert.Empty(third.Source.Requests);
    }

    [Fact]
    public async Task ValueCapturedOnlyBeforeItsDate_IsReconfirmedOnce_ValueCheckedOnTheDayIsNot()
    {
        var repository = new InMemoryDatedResultRepository();
        for (var d = Jan1; d < Today; d = d.AddDays(1))
        {
            await SeedAsync(repository, d, RetrievalStatus.Verified, NoonEasternOn(d));
        }

        var forecastOnly = new DateOnly(2026, 10, 10);
        await SeedAsync(repository, forecastOnly, RetrievalStatus.Verified, NoonEasternOn(new DateOnly(2026, 10, 1)));
        var h = Create(repository: repository);

        await h.Service.RunSessionAsync();

        Assert.Equal([forecastOnly], h.Source.Requests);
        Assert.Equal(Now, (await h.Repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, forecastOnly)))!.SuccessEvidence!.TimeChecked);
    }

    [Fact]
    public async Task ClosedAndNotPublishedAnswers_AreStoredWithTheirExistingSemantics()
    {
        var h = Create(now: new DateTimeOffset(2026, 1, 4, 17, 0, 0, TimeSpan.Zero));
        h.Source.Respond = d => d.Day switch
        {
            1 => Without(d, RetrievalStatus.Closed),
            2 => Without(d, RetrievalStatus.NotPublished),
            _ => Verified(d),
        };

        await h.Service.RunSessionAsync();

        var stored = (await StoredAsync(h, Jan1, new DateOnly(2026, 1, 3))).ToDictionary(r => r.Key.Date.Day);
        Assert.Equal(RetrievalStatus.Closed, stored[1].SuccessEvidence!.Status);
        Assert.Null(stored[1].SuccessPayloadJson);
        Assert.Equal(RetrievalStatus.NotPublished, stored[2].SuccessEvidence!.Status);
        Assert.Equal(RetrievalStatus.Verified, stored[3].SuccessEvidence!.Status);
        Assert.Contains("\"OpeningTime\"", stored[3].SuccessPayloadJson);
    }

    [Fact]
    public async Task WeakerPastDateAnswer_NeverReplacesStoredHours()
    {
        var repository = new InMemoryDatedResultRepository();
        var date = new DateOnly(2026, 10, 10);
        await SeedAsync(repository, date, RetrievalStatus.Verified, NoonEasternOn(new DateOnly(2026, 10, 1))); // forecast, so eligible for re-check
        var original = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, date));
        var h = Create(repository: repository);
        h.Source.Respond = d => d == date ? Without(d, RetrievalStatus.NotPublished) : Verified(d);

        await h.Service.RunSessionAsync();

        Assert.Contains(date, h.Source.Requests);
        Assert.Equal(original, await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, date)));
    }

    // ------------------------------------------------------------------ failures / offline

    [Fact]
    public async Task Offline_StopsAfterThreeFailedChecks_WritesNothing_StatusIsPartial()
    {
        var h = Create();
        h.Source.Respond = d => Without(d, RetrievalStatus.Error);

        await h.Service.RunSessionAsync();

        Assert.Equal(3, h.Source.Requests.Count); // no rapid retry loop; no date retried
        Assert.Equal(3, h.Source.Requests.Distinct().Count());
        Assert.Empty(await StoredAsync(h, Jan1, Today));
        Assert.Empty(h.Changes);
        var status = h.Service.CurrentStatus;
        Assert.Equal(HistoricalBackfillState.PartiallyAvailable, status.State);
        Assert.Equal(3, status.SessionFailed);

        // A later session (next app start, back online) resumes from the same dates.
        h.Source.Respond = d => Verified(d);
        h.Source.Requests.Clear();
        await h.Service.RunSessionAsync();
        Assert.Equal(PastDatesThisYear, h.Source.Requests.Count);
    }

    [Fact]
    public async Task OneFailedDate_IsSkippedForThisSession_AndNeverDisplacesAStoredValue()
    {
        var repository = new InMemoryDatedResultRepository();
        var failing = new DateOnly(2026, 10, 12);
        await SeedAsync(repository, failing, RetrievalStatus.Verified, NoonEasternOn(new DateOnly(2026, 10, 2)));
        var original = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, failing));
        var h = Create(repository: repository);
        h.Source.Respond = d => d == failing ? Without(d, RetrievalStatus.Error) : Verified(d);

        await h.Service.RunSessionAsync();

        Assert.Single(h.Source.Requests, failing);
        Assert.Equal(PastDatesThisYear, h.Source.Requests.Count);
        Assert.Equal(original, await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, failing)));
        Assert.Equal(HistoricalBackfillState.PartiallyAvailable, h.Service.CurrentStatus.State);
        Assert.Equal(1, h.Service.CurrentStatus.SessionFailed);
    }

    // ------------------------------------------------------------------ year rollover

    [Fact]
    public async Task NewYear_TargetsOnlyTheNewYear_PreviousYearKeptAndNotRefilled()
    {
        var repository = new InMemoryDatedResultRepository();
        var dec = new DateOnly(2026, 12, 20);
        await SeedAsync(repository, dec, RetrievalStatus.Verified, NoonEasternOn(new DateOnly(2026, 12, 1))); // incomplete 2026 date
        var h = Create(now: new DateTimeOffset(2027, 1, 5, 17, 0, 0, TimeSpan.Zero), repository: repository);

        await h.Service.RunSessionAsync();

        Assert.Equal([new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 3), new DateOnly(2027, 1, 2), new DateOnly(2027, 1, 1)], h.Source.Requests);
        Assert.NotNull(await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, dec))); // retained, never deleted
        Assert.Equal(2027, h.Service.CurrentStatus.TargetYear);
    }

    [Fact]
    public async Task JanuaryFirst_HasNothingToBackfill()
    {
        var h = Create(now: new DateTimeOffset(2027, 1, 1, 17, 0, 0, TimeSpan.Zero));

        await h.Service.RunSessionAsync();

        Assert.Empty(h.Source.Requests);
        Assert.Equal(HistoricalBackfillState.NotApplicable, h.Service.CurrentStatus.State);
    }

    [Fact]
    public async Task DecemberThirtyFirst_StillTargetsTheCurrentYear()
    {
        var h = Create(now: new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero));

        await h.Service.RunSessionAsync();

        Assert.Equal(new DateOnly(2026, 12, 30), h.Source.Requests[0]);
        Assert.Equal(Jan1, h.Source.Requests[^1]);
        Assert.Equal(364, h.Source.Requests.Count);
    }

    // ------------------------------------------------------------------ Calendar month priority

    [Fact]
    public async Task ViewedCalendarMonth_IsCheckedNext_NewestFirstWithinIt()
    {
        var h = Create();
        h.Service.PrioritizeMonth(new DateOnly(2026, 3, 1));

        await h.Service.RunSessionAsync();

        Assert.Equal(Enumerable.Range(0, 31).Select(i => new DateOnly(2026, 3, 31).AddDays(-i)), h.Source.Requests.Take(31));
        Assert.Equal(Today.AddDays(-1), h.Source.Requests[31]); // then back to newest-first
        Assert.Equal(PastDatesThisYear, h.Source.Requests.Distinct().Count());
    }

    [Fact]
    public async Task FutureOrOtherYearMonth_DoesNotChangeTheOrder()
    {
        var h = Create();
        h.Service.PrioritizeMonth(new DateOnly(2026, 11, 1));

        await h.Service.RunSessionAsync();

        Assert.Equal(Today.AddDays(-1), h.Source.Requests[0]);
    }

    // ------------------------------------------------------------------ priority / gate

    [Fact]
    public async Task Start_WaitsForTheOperationalStartupWork_BeforeAnyHistoricalRequest()
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = Create(startupCompletion: () => startup.Task);

        h.Service.Start();
        await Task.Delay(100);
        Assert.Empty(h.Source.Requests);

        startup.SetResult();
        await h.Service.SessionTask!;

        Assert.Equal(PastDatesThisYear, h.Source.Requests.Count);
    }

    [Theory]
    [InlineData(OfficialRefreshKind.DateCheck)] // Check Online / selected-date auto-check
    [InlineData(OfficialRefreshKind.DeepRefresh)]
    [InlineData(OfficialRefreshKind.Comprehensive)] // automatic four-hour / startup check
    [InlineData(OfficialRefreshKind.StartupMaintenance)]
    public async Task OperationalWork_PreemptsAnInFlightHistoricalRequest_WithoutEverRunningConcurrently(OfficialRefreshKind kind)
    {
        var h = Create();
        var firstInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var yesterday = Today.AddDays(-1);
        h.Source.Before = async (date, token) =>
        {
            if (date == yesterday && h.Source.Requests.Count(d => d == yesterday) == 1)
            {
                firstInFlight.TrySetResult();
                await Task.Delay(Timeout.Infinite, token); // a slow request — only cancellation ends it
            }
        };

        var session = Task.Run(() => h.Service.RunSessionAsync());
        await firstInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Operational work asks for the gate and gets it promptly, without waiting out the slow request.
        using (var entry = await h.Gate.TryEnterAsync(new OfficialRefreshActivity(kind, Today), waitForInProcessWork: true).WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.NotNull(entry);
            Assert.False(h.Source.InFlight); // the historical request was cancelled before operational work started
            var requestsWhileHeld = h.Source.Requests.Count;
            await Task.Delay(50);
            Assert.Equal(requestsWhileHeld, h.Source.Requests.Count); // history does not start while operational work runs
            Assert.Null(await h.Repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, yesterday))); // a cancelled request wrote nothing
        }

        await session.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, h.Source.Requests.Count(d => d == yesterday)); // re-checked after yielding
        Assert.Equal(PastDatesThisYear, (await StoredAsync(h, Jan1, Today)).Count);
        Assert.Equal(1, h.Source.MaxConcurrent);
    }

    [Fact]
    public async Task HistoricalWork_NeverStartsWhileOperationalWorkHoldsTheGate()
    {
        var h = Create();
        var held = await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);

        var session = Task.Run(() => h.Service.RunSessionAsync());
        await Task.Delay(100);
        Assert.Empty(h.Source.Requests);

        held!.Dispose();
        await session.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PastDatesThisYear, h.Source.Requests.Count);
    }

    [Fact]
    public async Task ScheduledTaskHoldingTheCrossProcessLock_HistoryWaits()
    {
        var h = Create();
        h.Lock.AcquireSucceeds = false;

        var session = Task.Run(() => h.Service.RunSessionAsync());
        await Task.Delay(100);
        Assert.Empty(h.Source.Requests);

        h.Lock.AcquireSucceeds = true;
        await session.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PastDatesThisYear, h.Source.Requests.Count);
    }

    [Fact]
    public async Task Changes_AreHistoricalOnly_BatchedPerMonth_AndNameAPastDate()
    {
        var h = Create();

        await h.Service.RunSessionAsync();

        Assert.NotEmpty(h.Changes);
        Assert.All(h.Changes, c =>
        {
            Assert.Equal(DataChangeKind.HistoricalOfficialData, c.Kind);
            Assert.NotNull(c.OperatingDate);
            Assert.True(c.OperatingDate < Today);
        });
        Assert.True(h.Changes.Count < PastDatesThisYear / 3, $"expected batched notifications, got {h.Changes.Count}");
        Assert.Contains(h.Changes, c => c.OperatingDate!.Value.Month == 1); // the last (oldest) batch is published too
    }

    // ------------------------------------------------------------------ gate contract

    [Fact]
    public async Task Gate_LowPriorityNeverQueues_AndIsAskedToYieldWhenOperationalWorkArrives()
    {
        var gate = new OfficialRefreshGate();
        var history = new OfficialRefreshActivity(OfficialRefreshKind.HistoricalBackfill, Today.AddDays(-1));
        Assert.True(history.IsLowPriority);
        Assert.False(history.IsComprehensive);

        var held = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DateCheck, Today), waitForInProcessWork: true);
        Assert.Null(await gate.TryEnterAsync(history, waitForInProcessWork: true)); // never waits, even when asked to
        held!.Dispose();

        var yieldRequests = 0;
        gate.PriorityWorkWaiting += (_, _) => yieldRequests++;
        var historyEntry = await gate.TryEnterAsync(history, waitForInProcessWork: false);
        Assert.NotNull(historyEntry);

        var operational = gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DeepRefresh), waitForInProcessWork: true);
        Assert.Equal(1, yieldRequests);
        Assert.True(gate.HasPriorityWorkWaiting);
        Assert.Null(await gate.TryEnterAsync(history, waitForInProcessWork: false));

        historyEntry!.Dispose();
        using var operationalEntry = await operational.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(operationalEntry);
        Assert.False(gate.HasPriorityWorkWaiting);
    }
}
