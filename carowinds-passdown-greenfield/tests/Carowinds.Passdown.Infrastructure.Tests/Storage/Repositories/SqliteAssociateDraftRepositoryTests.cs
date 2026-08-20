using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteAssociateDraftRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateOnly Halloween = new(2026, 10, 31);

    private SqliteAssociateDraftRepository CreateRepository() => new(_fixture.ConnectionFactory);

    [Fact]
    public async Task GetAsync_MissingOperatingDate_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync(Halloween);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsDraft()
    {
        var repository = CreateRepository();
        var draft = new AssociateDraft(Halloween, 1, """{"notes":"Gate opens at 10"}""", new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync(draft);
        var loaded = await repository.GetAsync(Halloween);

        Assert.Equal(draft, loaded);
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceForSameDate_UpsertsRatherThanDuplicating()
    {
        var repository = CreateRepository();
        var first = new AssociateDraft(Halloween, 1, "{}", new DateTimeOffset(2026, 8, 19, 6, 0, 0, TimeSpan.Zero));
        var second = new AssociateDraft(Halloween, 2, """{"notes":"updated"}""", new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        var loaded = await repository.GetAsync(Halloween);

        Assert.Equal(second, loaded);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var draft = new AssociateDraft(Halloween, 1, "{}", DateTimeOffset.UtcNow);
        await writer.SaveAsync(draft);

        var reader = new SqliteAssociateDraftRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(Halloween);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task SaveAsync_PayloadContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingPayload = """{"notes":"Gate opens at 10'; DROP TABLE associate_drafts; --"}""";
        var draft = new AssociateDraft(Halloween, 1, maliciousLookingPayload, DateTimeOffset.UtcNow);

        await repository.SaveAsync(draft);
        var loaded = await repository.GetAsync(Halloween);

        Assert.Equal(maliciousLookingPayload, loaded!.PayloadJson);
    }
}
