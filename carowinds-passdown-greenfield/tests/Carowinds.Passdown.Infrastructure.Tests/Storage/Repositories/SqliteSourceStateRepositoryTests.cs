using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteSourceStateRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private SqliteSourceStateRepository CreateRepository() => new(_fixture.ConnectionFactory);

    [Fact]
    public async Task GetAsync_MissingSourceId_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync("park-hours");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsAllFields()
    {
        var repository = CreateRepository();
        var lastAttempt = new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);
        var lastSuccess = new DateTimeOffset(2026, 8, 19, 6, 0, 0, TimeSpan.Zero);
        var nextDue = new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero);
        var state = new SourceStateRecord("park-hours", SourceCategory.ParkHours, lastAttempt, lastSuccess, nextDue, "etag-abc123", 2);

        await repository.SaveAsync(state);
        var loaded = await repository.GetAsync("park-hours");

        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_UpsertsRatherThanDuplicating()
    {
        var repository = CreateRepository();
        var first = new SourceStateRecord("park-hours", SourceCategory.ParkHours, null, null, null, null, 0);
        var second = first with { ConsecutiveFailureCount = 3, SourceFingerprint = "changed" };

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        var loaded = await repository.GetAsync("park-hours");

        Assert.Equal(second, loaded);
    }

    [Fact]
    public async Task SaveAsync_WithNullOptionalFields_RoundTripsAsNull()
    {
        var repository = CreateRepository();
        var state = new SourceStateRecord("season-passes", SourceCategory.SeasonPasses, null, null, null, null, 0);

        await repository.SaveAsync(state);
        var loaded = await repository.GetAsync("season-passes");

        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var state = new SourceStateRecord("park-hours", SourceCategory.ParkHours, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "fp", 1);
        await writer.SaveAsync(state);

        var reader = new SqliteSourceStateRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync("park-hours");

        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task SaveAsync_SourceIdWithQuoteCharacter_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingId = "park-hours'; DROP TABLE source_state; --";
        var state = new SourceStateRecord(maliciousLookingId, SourceCategory.ParkHours, null, null, null, null, 0);

        await repository.SaveAsync(state);
        var loaded = await repository.GetAsync(maliciousLookingId);

        Assert.Equal(state, loaded);
        // If parameterization had failed, this second call (or the table itself) would error out.
        Assert.Null(await repository.GetAsync("some-other-source"));
    }
}
