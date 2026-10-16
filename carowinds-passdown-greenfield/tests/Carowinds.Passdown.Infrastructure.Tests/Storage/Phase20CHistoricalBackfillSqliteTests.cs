using System.Globalization;
using System.Net;
using System.Text;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.HistoricalBackfill;
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
/// Phase 20C: the current-year historical backfill end to end against the real
/// <see cref="ParkHoursSource"/> (fed scripted responses shaped like the live feed, including its
/// ~28 KB per-date <c>venues</c> detail) and a real, fully migrated SQLite database — what is stored,
/// how big it is, that nothing raw is kept, and that no schema is added.
/// </summary>
public sealed class Phase20CHistoricalBackfillSqliteTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>12:00 PM Eastern on 2026-10-16.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Yesterday = new(2026, 10, 15);

    /// <summary>The oldest date the live feed still served when investigated (2026-10-16); earlier dates answered 404.</summary>
    private static readonly DateOnly ObservedRetentionStart = new(2026, 3, 25);

    private const string RawMarker = "RAW-VENUE-DETAIL-MARKER";

    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>Answers each daily request like the live CloudFront feed: open/closed JSON with a large venues array, or its 404 JSON before the retention start.</summary>
    private sealed class FeedLikeHttpClient(DateOnly retentionStart) : IRetrievalHttpClient
    {
        private static readonly string Venues = BuildVenues();

        public List<DateOnly> Requests { get; } = [];

        public bool Offline { get; set; }

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = requestUri.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("date=", StringComparison.Ordinal))[5..];
            var date = DateOnly.ParseExact(query, "yyyyMMdd", CultureInfo.InvariantCulture);
            Requests.Add(date);

            if (Offline)
            {
                throw new HttpRequestException("No such host is known.");
            }

            if (date < retentionStart)
            {
                return Task.FromResult(new RetrievalHttpResponse(HttpStatusCode.NotFound, "application/json",
                    """{"error":"Not Found","message":"The requested resource could not be found."}"""));
            }

            return Task.FromResult(new RetrievalHttpResponse(HttpStatusCode.OK, "application/json", DayJson(date)));
        }

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <summary>Spring/fall weekdays closed, summer open daily with the water park — the shape of a real season.</summary>
        private static string DayJson(DateOnly date)
        {
            var summer = date.Month is >= 6 and <= 8;
            var closed = !summer && date.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday;
            var operatings = closed
                ? "[]"
                : summer
                    ? """[{"operatingTypeId":24,"operatingTypeName":"Park","items":[{"assignmentDisplayName":"Park","assignmentDescription":"","timeFrom":"10:00","timeTo":"22:00","isBuyout":false}]},{"operatingTypeId":70,"operatingTypeName":"Waterpark","items":[{"assignmentDisplayName":"Waterpark","assignmentDescription":"","timeFrom":"11:00","timeTo":"18:00","isBuyout":false}]}]"""
                    : """[{"operatingTypeId":24,"operatingTypeName":"Park","items":[{"assignmentDisplayName":"Park","assignmentDescription":"","timeFrom":"10:00","timeTo":"19:00","isBuyout":false}]}]""";
            return $$"""{"parkId":30,"parkAbbreviation":"CA","parkName":"Carowinds","dates":[{"date":"{{date:MM/dd/yyyy}}","isParkClosed":{{(closed ? "true" : "false")}},"venues":{{Venues}},"shows":[],"events":[],"operatings":{{operatings}}}]}""";
        }

        /// <summary>~28 KB, like the live feed's per-ride/restaurant detail the adapter deliberately ignores.</summary>
        private static string BuildVenues()
        {
            var sb = new StringBuilder("[");
            for (var i = 0; i < 120; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(CultureInfo.InvariantCulture, $$"""{"venueId":{{i}},"venueName":"{{RawMarker}} ride {{i}}","detailHours":[{"operatingTimeFrom":"09:30","operatingTimeTo":"21:45","note":"rope drop and evening hours detail text"}]}""");
            }

            return sb.Append(']').ToString();
        }
    }

    private (HistoricalBackfillService Service, FeedLikeHttpClient Http, SqliteDatedResultRepository Repository) Create(DateOnly retentionStart)
    {
        var http = new FeedLikeHttpClient(retentionStart);
        var clock = new FakeClock(Now);
        var repository = new SqliteDatedResultRepository(_fixture.ConnectionFactory);
        var service = new HistoricalBackfillService(
            new ParkHoursSource(http, clock), repository, new OfficialRefreshGate(), clock,
            options: new HistoricalBackfillOptions { StartDelay = TimeSpan.Zero, RequestSpacing = TimeSpan.Zero, YieldQuietPeriod = TimeSpan.FromMilliseconds(5) });
        return (service, http, repository);
    }

    /// <summary>Folds the WAL into the main file so the file size is the real on-disk footprint.</summary>
    private long CheckpointedDatabaseSize()
    {
        using (var connection = _fixture.ConnectionFactory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        return new FileInfo(_fixture.ConnectionFactory.DatabasePath).Length;
    }

    private T Scalar<T>(string sql)
    {
        using var connection = _fixture.ConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("2026-03-25", "live retention window (as observed)")]
    [InlineData("2026-01-01", "every past date retained (full-year projection)")]
    public async Task FullCurrentYearBackfill_StoresOnlyNormalizedRows_StorageMeasured(string retentionStart, string scenario)
    {
        var retention = DateOnly.Parse(retentionStart, CultureInfo.InvariantCulture);
        var (service, http, _) = Create(retention);
        var tablesBefore = Scalar<long>("SELECT COUNT(*) FROM sqlite_master;");
        var sizeBefore = CheckpointedDatabaseSize();

        await service.RunSessionAsync();

        var sizeAfter = CheckpointedDatabaseSize();
        var rows = Scalar<long>("SELECT COUNT(*) FROM dated_results WHERE category = 'ParkHours';");
        var expectedRows = Yesterday.DayNumber - retention.DayNumber + 1;
        Assert.Equal(expectedRows, rows);
        Assert.Equal(expectedRows + (retention > Jan1 ? 1 : 0), http.Requests.Count); // + the single "Not Found" probe

        var added = sizeAfter - sizeBefore;
        var bytesPerRow = (double)added / rows;
        var rowBytes = Scalar<long>(
            """
            SELECT SUM(LENGTH(category) + LENGTH(requested_date) + IFNULL(LENGTH(success_status),0) + IFNULL(LENGTH(success_confirmed_date),0)
                + IFNULL(LENGTH(success_checked_at_utc),0) + IFNULL(LENGTH(success_strategy),0) + IFNULL(LENGTH(success_detail),0)
                + IFNULL(LENGTH(success_technical_detail),0) + IFNULL(LENGTH(success_source_name),0) + IFNULL(LENGTH(success_source_url),0)
                + IFNULL(LENGTH(success_payload_json),0) + LENGTH(last_attempt_status) + IFNULL(LENGTH(last_attempt_confirmed_date),0)
                + LENGTH(last_attempt_checked_at_utc) + LENGTH(last_attempt_strategy) + LENGTH(last_attempt_detail)
                + IFNULL(LENGTH(last_attempt_technical_detail),0) + LENGTH(last_attempt_source_name) + LENGTH(last_attempt_source_url))
            FROM dated_results;
            """);
        output.WriteLine($"Scenario: {scenario}");
        output.WriteLine($"DB size before: {sizeBefore:N0} bytes; after: {sizeAfter:N0} bytes; added: {added:N0} bytes ({added / 1024.0:F1} KB)");
        output.WriteLine($"Rows added: {rows}; file bytes per date: {bytesPerRow:F0}; stored column bytes per date: {(double)rowBytes / rows:F0}");
        output.WriteLine($"Projected full year (365 dates): {bytesPerRow * 365 / 1024.0:F0} KB");

        // Regression guard against accidental raw-response storage: one live response is ~28 KB, a
        // normalized row is well under 1 KB. 2 KB per date leaves room for page overhead and indexes.
        Assert.True(bytesPerRow < 2048, $"{bytesPerRow:F0} bytes per stored date — is a raw response being kept?");
        Assert.Equal(tablesBefore, Scalar<long>("SELECT COUNT(*) FROM sqlite_master;")); // no new table or index

        var maxPayload = Scalar<long>("SELECT IFNULL(MAX(LENGTH(success_payload_json)), 0) FROM dated_results;");
        Assert.True(maxPayload < 200, $"payload JSON {maxPayload} bytes");
        Assert.Equal(0L, Scalar<long>($"SELECT COUNT(*) FROM dated_results WHERE IFNULL(success_payload_json,'') LIKE '%venue%' OR IFNULL(success_technical_detail,'') LIKE '%{RawMarker}%' OR IFNULL(last_attempt_technical_detail,'') LIKE '%{RawMarker}%';"));
        var fileBytes = await File.ReadAllBytesAsync(_fixture.ConnectionFactory.DatabasePath);
        Assert.DoesNotContain(RawMarker, Encoding.UTF8.GetString(fileBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredHistory_IsTheSameNormalizedShapeAsOperationalRows_ClosedAndOpenAndWaterpark()
    {
        var (service, _, repository) = Create(ObservedRetentionStart);

        await service.RunSessionAsync();

        var summer = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 7, 4)));
        Assert.Equal(RetrievalStatus.Verified, summer!.SuccessEvidence!.Status);
        Assert.Contains("\"OpeningTime\":\"10:00:00\"", summer.SuccessPayloadJson);
        Assert.Contains("\"WaterparkOpeningTime\":\"11:00:00\"", summer.SuccessPayloadJson);
        Assert.Equal(new DateOnly(2026, 7, 4), summer.SuccessEvidence.ConfirmedDate);

        var closedWeekday = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 9, 15)));
        Assert.Equal(RetrievalStatus.Closed, closedWeekday!.SuccessEvidence!.Status);
        Assert.Null(closedWeekday.SuccessPayloadJson);

        Assert.Null(await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, ObservedRetentionStart.AddDays(-1)))); // the 404 probe stored nothing
    }

    [Fact]
    public async Task Rerun_AfterRestart_IsIdempotent_OnlyTheRetentionBoundaryIsReprobed()
    {
        var (first, _, _) = Create(ObservedRetentionStart);
        await first.RunSessionAsync();
        var rowsAfterFirst = Scalar<long>("SELECT COUNT(*) FROM dated_results;");
        var sizeAfterFirst = CheckpointedDatabaseSize();

        var (second, http, _) = Create(ObservedRetentionStart); // "next launch", same database
        await second.RunSessionAsync();

        Assert.Equal([ObservedRetentionStart.AddDays(-1)], http.Requests);
        Assert.Equal(rowsAfterFirst, Scalar<long>("SELECT COUNT(*) FROM dated_results;"));
        Assert.Equal(sizeAfterFirst, CheckpointedDatabaseSize());
    }

    /// <summary>
    /// Phase 21A (Jan 1–3, 2026 investigation): the live feed answered 404 for 2026-01-01..03 and every
    /// date before 2026-03-25 on 2026-10-16. Those dates are in scope (current calendar year, even
    /// though they belong to the previous WinterFest season), are probed, and stay without hours — no
    /// value is inferred from a neighboring date.
    /// </summary>
    [Fact]
    public async Task Phase21A_EarlyJanuaryNotRetainedByTheFeed_StaysUnavailableAndIsNeverInferred()
    {
        var (service, http, repository) = Create(ObservedRetentionStart);

        await service.RunSessionAsync();

        Assert.Contains(ObservedRetentionStart.AddDays(-1), http.Requests); // the boundary probe reaches before March 25
        foreach (var day in new[] { 1, 2, 3 })
        {
            Assert.Null(await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 1, day))));
        }

        Assert.Equal(ObservedRetentionStart.AddDays(-1), service.CurrentStatus.OldestNotRetainedDate);
        Assert.NotNull((await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, ObservedRetentionStart)))!.SuccessEvidence);
    }

    /// <summary>Phase 21A: the boundary is whatever the feed answers — not a hard-coded March 25. If the feed served early January, those verified hours are stored like any other date.</summary>
    [Fact]
    public async Task Phase21A_FeedRetainingEarlyJanuary_StoresVerifiedJanuaryHours_NoFixedCutoff()
    {
        var (service, _, repository) = Create(new DateOnly(2026, 1, 2));

        await service.RunSessionAsync();

        Assert.Null(await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, Jan1)));
        var jan2 = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 1, 2)));
        Assert.Equal(new DateOnly(2026, 1, 2), jan2!.SuccessEvidence!.ConfirmedDate);
        Assert.NotNull(await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 3, 1))));
        Assert.Equal(0L, Scalar<long>("SELECT COUNT(*) FROM dated_results WHERE requested_date < '2026-01-01';")); // never a prior calendar year
    }

    [Fact]
    public async Task Offline_WritesNothing_AndKeepsAnExistingValue()
    {
        var repository = new SqliteDatedResultRepository(_fixture.ConnectionFactory);
        var date = new DateOnly(2026, 10, 15);
        var forecast = new SourceEvidence("Carowinds Park Hours", new Uri("https://www.sixflags.com/carowinds/park-hours"),
            new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero), RetrievalStatus.Verified, "Open.", RetrievalStrategy.DirectApi, date, confirmedDate: date);
        await repository.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), forecast, """{"OpeningTime":"10:00:00","ClosingTime":"19:00:00"}""");
        var (service, http, _) = Create(ObservedRetentionStart);
        http.Offline = true;

        await service.RunSessionAsync();

        Assert.Equal(3, http.Requests.Count);
        Assert.Equal(1L, Scalar<long>("SELECT COUNT(*) FROM dated_results;"));
        var kept = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, date));
        Assert.Equal(RetrievalStatus.Verified, kept!.SuccessEvidence!.Status);
        Assert.Equal(RetrievalStatus.Verified, kept.LastAttemptEvidence.Status); // not even the latest-attempt record was touched
        Assert.Equal(HistoricalBackfillState.PartiallyAvailable, service.CurrentStatus.State);
    }
}
