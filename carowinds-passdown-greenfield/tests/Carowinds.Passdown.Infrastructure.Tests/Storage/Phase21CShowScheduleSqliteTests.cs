using System.Net;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 21C end to end against real SQLite (DB schema 16, no migration): official fixture → adapter →
/// registration → <c>dated_results</c> → <see cref="ShowScheduleService"/>. Also measures storage growth.
/// </summary>
public class Phase21CShowScheduleSqliteTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>2026-10-16 3:00 PM Eastern.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Oct17 = new(2026, 10, 17);

    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private sealed class SwitchableClient(RetrievalHttpResponse initial) : IRetrievalHttpClient
    {
        public RetrievalHttpResponse? Response { get; set; } = initial;

        public Exception? Failure { get; set; }

        public int Gets { get; private set; }

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
        {
            Gets++;
            return Failure is not null ? Task.FromException<RetrievalHttpResponse>(Failure) : Task.FromResult(Response!);
        }

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static RetrievalHttpResponse Fixture(string name) => new(HttpStatusCode.OK, "application/json", FixtureFile.ReadText($"show-schedules/{name}"));

    private (CurrentAndFutureDatedSourceRegistration<ShowSchedulePayload> Registration, ShowScheduleService Service, SwitchableClient Http) Build(string fixture)
    {
        var http = new SwitchableClient(Fixture(fixture));
        var clock = new FakeClock(Now);
        var repository = new SqliteDatedResultRepository(_fixture.ConnectionFactory);
        var registration = new CurrentAndFutureDatedSourceRegistration<ShowSchedulePayload>(new ShowScheduleSource(http, clock), repository, clock);
        return (registration, new ShowScheduleService(new SqliteDatedResultRepository(_fixture.ReopenConnectionFactory()), clock), http);
    }

    [Fact]
    public async Task VerifiedSchedule_PersistsAndReadsBackAsNormalizedPerformances()
    {
        var (registration, service, _) = Build("scarowinds-2026-10-17.json");

        var result = await registration.ExecuteAsync(Oct17, CancellationToken.None);
        var view = await service.GetAsync(Oct17);

        Assert.Equal(SourceOperationOutcomeKind.Success, result.Outcome);
        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.Equal(11, view.Shows.Count);
        Assert.Equal(Now, view.LastVerifiedAt);
        Assert.False(view.LastCheckFailed);
    }

    [Fact]
    public async Task FailedRefresh_KeepsLastKnownGood_AndMarksTheLatestCheckFailed()
    {
        var (registration, service, http) = Build("scarowinds-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);

        http.Failure = new HttpRequestException("offline");
        var failed = await registration.ExecuteAsync(Oct17, CancellationToken.None);
        var view = await service.GetAsync(Oct17);

        Assert.Equal(SourceOperationOutcomeKind.Failure, failed.Outcome);
        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.Equal(11, view.Shows.Count);
        Assert.True(view.LastCheckFailed);
    }

    [Fact]
    public async Task InvalidResponse_NeverErasesVerifiedShows()
    {
        var (registration, service, http) = Build("scarowinds-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);

        http.Response = Fixture("hand-authored-unparseable-time-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);

        Assert.Equal(11, (await service.GetAsync(Oct17)).Shows.Count);
    }

    [Fact]
    public async Task SuccessfulRefresh_ThatRemovesPerformances_UpdatesTheSchedule()
    {
        var (registration, service, http) = Build("scarowinds-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);

        http.Response = Fixture("hand-authored-changed-and-removed-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);
        var view = await service.GetAsync(Oct17);

        var only = Assert.Single(view.Shows);
        Assert.Equal([new TimeOnly(19, 30), new TimeOnly(21, 30), new TimeOnly(22, 30)], only.Times);
    }

    [Fact]
    public async Task PastDate_IsNeverRequested_AndItsRecordedScheduleIsKept()
    {
        var (registration, service, http) = Build("scarowinds-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);

        // Once Oct 17 is history: a Check Online for it must neither fetch nor overwrite.
        var nextDayClock = new FakeClock(Now.AddDays(2)); // Oct 18, Eastern
        var laterRegistration = new CurrentAndFutureDatedSourceRegistration<ShowSchedulePayload>(
            new ShowScheduleSource(http, nextDayClock), new SqliteDatedResultRepository(_fixture.ConnectionFactory), nextDayClock);
        var gets = http.Gets;

        var result = await laterRegistration.ExecuteAsync(Oct17, CancellationToken.None);
        var view = await new ShowScheduleService(new SqliteDatedResultRepository(_fixture.ConnectionFactory), nextDayClock).GetAsync(Oct17);

        Assert.Equal(RetrievalStatus.Unavailable, result.Status);
        Assert.Equal(gets, http.Gets);
        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.Equal(11, view.Shows.Count);
    }

    [Fact]
    public async Task PastDateWithNothingRecorded_IsHistoricalNotAvailable_NeverTodaysLineup()
    {
        var (registration, service, _) = Build("scarowinds-2026-10-17.json");
        await registration.ExecuteAsync(Oct17, CancellationToken.None);

        var view = await service.GetAsync(new DateOnly(2026, 10, 10));

        Assert.Equal(ShowScheduleState.HistoricalNotAvailable, view.State);
        Assert.Empty(view.Shows);
    }

    [Fact]
    public async Task StorageMeasurement_AYearOfSchedules_StaysCompact_AndStoresNoRawResponse()
    {
        var repository = new SqliteDatedResultRepository(_fixture.ConnectionFactory);
        var sizeBefore = DatabaseBytes();
        var payload = (await new ShowScheduleSource(new SwitchableClient(Fixture("scarowinds-2026-10-17.json")), new FakeClock(Now))
            .RetrieveAsync(new RetrievalRequest("show-schedule.official", SourceCategory.ShowSchedule, Oct17), CancellationToken.None)).Payload!;
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        var performancesPerDay = payload.Shows.Sum(show => show.Times.Count);

        for (var i = 0; i < 365; i++)
        {
            var date = Oct17.AddDays(i);
            var evidence = new SourceEvidence("Carowinds Show Schedule", new Uri("https://www.sixflags.com/carowinds/shows"), Now, RetrievalStatus.Verified,
                "11 shows scheduled.", RetrievalStrategy.DirectApi, requestedDate: date, confirmedDate: date);
            await repository.RecordSuccessAsync(new DatedResultKey(SourceCategory.ShowSchedule, date), evidence, payloadJson);
        }

        var sizeAfter = DatabaseBytes();
        var growth = sizeAfter - sizeBefore;
        var performances = performancesPerDay * 365;
        output.WriteLine($"Shows/day {payload.Shows.Count}; performances/day {performancesPerDay}; payload {payloadJson.Length} bytes/day.");
        output.WriteLine($"DB before {sizeBefore:N0} B; after 365 days {sizeAfter:N0} B; growth {growth:N0} B; ≈{(double)growth / performances:F1} B/performance.");

        Assert.DoesNotContain("venues", payloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operatings", payloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(growth < 2_000_000, $"A year of show schedules grew the database by {growth:N0} bytes.");
    }

    private long DatabaseBytes()
    {
        using var connection = new SqliteConnection($"Data Source={_fixture.ConnectionFactory.DatabasePath}");
        connection.Open();
        using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        checkpoint.ExecuteNonQuery();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
        return (long)command.ExecuteScalar()!;
    }
}
