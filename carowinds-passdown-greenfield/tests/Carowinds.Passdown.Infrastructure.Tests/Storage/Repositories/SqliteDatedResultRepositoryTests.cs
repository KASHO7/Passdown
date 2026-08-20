using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteDatedResultRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/park-hours");
    private static readonly DateOnly Halloween = new(2026, 10, 31);

    private SqliteDatedResultRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static SourceEvidence Evidence(
        RetrievalStatus status,
        DateTimeOffset timeChecked,
        DateOnly? requestedDate = null,
        DateOnly? confirmedDate = null) => new(
        sourceName: "Park hours",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: status,
        detail: status == RetrievalStatus.Error ? "Timed out." : "Checked successfully.",
        strategy: RetrievalStrategy.Html,
        requestedDate: requestedDate,
        confirmedDate: confirmedDate,
        technicalDetail: status == RetrievalStatus.Error ? "System.TimeoutException" : null);

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync(new DatedResultKey(SourceCategory.ParkHours, Halloween));

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordSuccessAsync_ThenGetAsync_RoundTripsSuccessAndLastAttempt()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var checkedAt = new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
        var evidence = Evidence(RetrievalStatus.Verified, checkedAt, Halloween, Halloween);

        await repository.RecordSuccessAsync(key, evidence, """{"open":"10:00","close":"22:00"}""");
        var loaded = await repository.GetAsync(key);

        Assert.NotNull(loaded);
        Assert.Equal(key, loaded.Key);
        Assert.Equal(evidence, loaded.SuccessEvidence);
        Assert.Equal(evidence, loaded.LastAttemptEvidence);
        Assert.Equal("""{"open":"10:00","close":"22:00"}""", loaded.SuccessPayloadJson);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_AfterSuccess_DoesNotOverwritePriorSuccess()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var successAt = new DateTimeOffset(2026, 8, 19, 6, 0, 0, TimeSpan.Zero);
        var successEvidence = Evidence(RetrievalStatus.Verified, successAt, Halloween, Halloween);
        await repository.RecordSuccessAsync(key, successEvidence, "payload-v1");

        var failureAt = new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
        var failureEvidence = Evidence(RetrievalStatus.Error, failureAt, Halloween);
        await repository.RecordFailedAttemptAsync(key, failureEvidence);

        var loaded = await repository.GetAsync(key);

        Assert.NotNull(loaded);
        Assert.Equal(successEvidence, loaded.SuccessEvidence);
        Assert.Equal("payload-v1", loaded.SuccessPayloadJson);
        Assert.Equal(failureEvidence, loaded.LastAttemptEvidence);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_WithNoPriorRow_LeavesSuccessNull()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var failureEvidence = Evidence(RetrievalStatus.Error, DateTimeOffset.UtcNow, Halloween);

        await repository.RecordFailedAttemptAsync(key, failureEvidence);
        var loaded = await repository.GetAsync(key);

        Assert.NotNull(loaded);
        Assert.Null(loaded.SuccessEvidence);
        Assert.Null(loaded.SuccessPayloadJson);
        Assert.Equal(failureEvidence, loaded.LastAttemptEvidence);
    }

    [Fact]
    public async Task RecordSuccessAsync_RejectsNonSuccessfulEvidence()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var errorEvidence = Evidence(RetrievalStatus.Error, DateTimeOffset.UtcNow, Halloween);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.RecordSuccessAsync(key, errorEvidence, null));
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_RejectsSuccessfulEvidence()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var verifiedEvidence = Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.RecordFailedAttemptAsync(key, verifiedEvidence));
    }

    [Fact]
    public async Task DifferentCategoriesSameDate_AreIsolated()
    {
        var repository = CreateRepository();
        var hoursKey = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var fastLaneKey = new DatedResultKey(SourceCategory.DatedFastLane, Halloween);

        await repository.RecordSuccessAsync(hoursKey, Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween), "hours-payload");
        await repository.RecordSuccessAsync(fastLaneKey, Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween), "fast-lane-payload");

        var hours = await repository.GetAsync(hoursKey);
        var fastLane = await repository.GetAsync(fastLaneKey);

        Assert.Equal("hours-payload", hours!.SuccessPayloadJson);
        Assert.Equal("fast-lane-payload", fastLane!.SuccessPayloadJson);
    }

    [Fact]
    public async Task SameCategoryDifferentDates_AreIsolated()
    {
        var repository = CreateRepository();
        var novemberFirst = new DateOnly(2026, 11, 1);
        var halloweenKey = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var novemberKey = new DatedResultKey(SourceCategory.ParkHours, novemberFirst);

        await repository.RecordSuccessAsync(halloweenKey, Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween), "halloween-payload");
        await repository.RecordSuccessAsync(novemberKey, Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, novemberFirst, novemberFirst), "november-payload");

        Assert.Equal("halloween-payload", (await repository.GetAsync(halloweenKey))!.SuccessPayloadJson);
        Assert.Equal("november-payload", (await repository.GetAsync(novemberKey))!.SuccessPayloadJson);
    }

    [Fact]
    public async Task GetByRequestedDateAsync_ReturnsOnlyResultsForThatDate()
    {
        var repository = CreateRepository();
        var novemberFirst = new DateOnly(2026, 11, 1);

        await repository.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Halloween), Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween), "hours");
        await repository.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedFastLane, Halloween), Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween), "fast-lane");
        await repository.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, novemberFirst), Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, novemberFirst, novemberFirst), "november");

        var results = await repository.GetByRequestedDateAsync(Halloween);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(Halloween, r.Key.Date));
    }

    [Fact]
    public async Task NonVerifiedSuccessfulCompletions_AreStoredWithoutAConfirmedDate()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.HauntedAttractions, Halloween);
        var evidence = Evidence(RetrievalStatus.NotPublished, DateTimeOffset.UtcNow, Halloween, confirmedDate: null);

        await repository.RecordSuccessAsync(key, evidence, null);
        var loaded = await repository.GetAsync(key);

        Assert.Equal(RetrievalStatus.NotPublished, loaded!.SuccessEvidence!.Status);
        Assert.Null(loaded.SuccessEvidence.ConfirmedDate);
        Assert.Null(loaded.SuccessPayloadJson);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        var evidence = Evidence(RetrievalStatus.Verified, DateTimeOffset.UtcNow, Halloween, Halloween);
        await writer.RecordSuccessAsync(key, evidence, "payload");

        var reader = new SqliteDatedResultRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(key);

        Assert.Equal("payload", loaded!.SuccessPayloadJson);
    }

    [Fact]
    public async Task RecordSuccessAsync_DetailContainingQuotesAndSemicolons_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        var key = new DatedResultKey(SourceCategory.ParkHours, Halloween);
        const string maliciousLookingDetail = "Gates open 10 AM'; DROP TABLE dated_results; --";
        var evidence = new SourceEvidence(
            sourceName: "Park hours",
            sourceUrl: SampleUrl,
            timeChecked: DateTimeOffset.UtcNow,
            status: RetrievalStatus.Verified,
            detail: maliciousLookingDetail,
            strategy: RetrievalStrategy.Html,
            requestedDate: Halloween,
            confirmedDate: Halloween);

        await repository.RecordSuccessAsync(key, evidence, null);
        var loaded = await repository.GetAsync(key);

        Assert.Equal(maliciousLookingDetail, loaded!.SuccessEvidence!.Detail);
    }
}
