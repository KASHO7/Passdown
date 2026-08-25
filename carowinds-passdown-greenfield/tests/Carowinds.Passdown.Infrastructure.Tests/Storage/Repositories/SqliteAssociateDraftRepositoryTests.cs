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

    [Fact]
    public async Task ListSavedDraftDatesAsync_NoDraftsSaved_ReturnsEmpty()
    {
        var repository = CreateRepository();

        var dates = await repository.ListSavedDraftDatesAsync();

        Assert.Empty(dates);
    }

    [Fact]
    public async Task ListSavedDraftDatesAsync_ReturnsEverySavedDate_AscendingOrder()
    {
        var repository = CreateRepository();
        var earlier = new DateOnly(2026, 8, 24);
        var later = new DateOnly(2026, 8, 26);
        await repository.SaveAsync(new AssociateDraft(Halloween, 1, "{}", DateTimeOffset.UtcNow));
        await repository.SaveAsync(new AssociateDraft(later, 1, "{}", DateTimeOffset.UtcNow));
        await repository.SaveAsync(new AssociateDraft(earlier, 1, "{}", DateTimeOffset.UtcNow));

        var dates = await repository.ListSavedDraftDatesAsync();

        Assert.Equal([earlier, later, Halloween], dates);
    }

    [Fact]
    public async Task ListSavedDraftDatesAsync_UpsertedDate_AppearsOnlyOnce()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(new AssociateDraft(Halloween, 1, "{}", new DateTimeOffset(2026, 8, 19, 6, 0, 0, TimeSpan.Zero)));
        await repository.SaveAsync(new AssociateDraft(Halloween, 2, """{"notes":"updated"}""", new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero)));

        var dates = await repository.ListSavedDraftDatesAsync();

        Assert.Equal([Halloween], dates);
    }
}
