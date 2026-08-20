using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteProductHistoryRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private const string GoldPassKey = "SeasonPasses:name:season passes|gold pass";

    private SqliteProductHistoryRepository CreateRepository() => new(_fixture.ConnectionFactory);

    [Fact]
    public async Task GetHistoryAsync_NoEntries_ReturnsEmptyList()
    {
        var repository = CreateRepository();

        var history = await repository.GetHistoryAsync(GoldPassKey);

        Assert.Empty(history);
    }

    [Fact]
    public async Task AppendAsync_ThenGetHistoryAsync_RoundTripsEntry()
    {
        var repository = CreateRepository();
        var entry = new ProductHistoryEntry(
            ProductKey: GoldPassKey,
            RecordedAt: new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero),
            DisplayName: "Gold Pass",
            LifecycleState: ProductLifecycleState.Active,
            CurrentPriceAmount: 149.99m,
            CurrentDisplayPrice: "$149.99",
            ComparisonPriceAmount: 199.99m,
            PriceQualifier: "Online Price",
            ChangeReason: "First observed");

        await repository.AppendAsync(entry);
        var history = await repository.GetHistoryAsync(GoldPassKey);

        Assert.Equal([entry], history);
    }

    [Fact]
    public async Task AppendAsync_MultipleEntries_AreReturnedOldestFirst()
    {
        var repository = CreateRepository();
        var first = new ProductHistoryEntry(GoldPassKey, new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero), "Gold Pass", ProductLifecycleState.Active, 149.99m, "$149.99", null, null, "First observed");
        var second = new ProductHistoryEntry(GoldPassKey, new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero), "Gold Pass", ProductLifecycleState.PendingConfirmation, 149.99m, "$149.99", null, null, "Missing from catalog");

        await repository.AppendAsync(second);
        await repository.AppendAsync(first);

        var history = await repository.GetHistoryAsync(GoldPassKey);

        Assert.Equal([first, second], history);
    }

    [Fact]
    public async Task GetHistoryAsync_OnlyReturnsEntriesForTheRequestedProductKey()
    {
        var repository = CreateRepository();
        const string otherKey = "AddOns:name:add-ons|meal plan";
        await repository.AppendAsync(new ProductHistoryEntry(GoldPassKey, DateTimeOffset.UtcNow, "Gold Pass", ProductLifecycleState.Active, null, null, null, null, null));
        await repository.AppendAsync(new ProductHistoryEntry(otherKey, DateTimeOffset.UtcNow, "Meal Plan", ProductLifecycleState.Active, null, null, null, null, null));

        var history = await repository.GetHistoryAsync(GoldPassKey);

        Assert.Single(history);
        Assert.Equal(GoldPassKey, history[0].ProductKey);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        await writer.AppendAsync(new ProductHistoryEntry(GoldPassKey, DateTimeOffset.UtcNow, "Gold Pass", ProductLifecycleState.Active, null, null, null, null, null));

        var reader = new SqliteProductHistoryRepository(_fixture.ReopenConnectionFactory());
        var history = await reader.GetHistoryAsync(GoldPassKey);

        Assert.Single(history);
    }

    [Fact]
    public async Task AppendAsync_ChangeReasonContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingReason = "Price changed to $0.00'; DROP TABLE product_history; --";
        var entry = new ProductHistoryEntry(
            GoldPassKey, DateTimeOffset.UtcNow, "Gold Pass", ProductLifecycleState.Active,
            0m, "$0.00", null, null, maliciousLookingReason);

        await repository.AppendAsync(entry);
        var history = await repository.GetHistoryAsync(GoldPassKey);

        Assert.Equal(maliciousLookingReason, history.Single().ChangeReason);
    }
}
