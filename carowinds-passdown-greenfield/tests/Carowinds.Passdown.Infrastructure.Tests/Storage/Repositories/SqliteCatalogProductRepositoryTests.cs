using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteCatalogProductRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/passes");
    private static readonly CatalogProductKey GoldPassKey =
        CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass");

    private SqliteCatalogProductRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset timeChecked) => new(
        sourceName: "Season passes",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: status,
        detail: "Checked successfully.",
        strategy: RetrievalStrategy.Html);

    private static CatalogProductUpsert Build(
        CatalogProductKey key,
        ProductLifecycleState state = ProductLifecycleState.Active,
        int absenceCount = 0,
        DateTimeOffset? archivedAt = null,
        string? archivedReason = null) => new(
        Key: key,
        DisplayName: "Gold Pass",
        LifecycleState: state,
        FirstSeenAt: new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        LastSeenAt: new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero),
        LastVerifiedAt: new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero),
        ConsecutiveConfirmedAbsenceCount: absenceCount,
        ArchivedAt: archivedAt,
        ArchivedReason: archivedReason,
        CurrentPriceAmount: 149.99m,
        CurrentDisplayPrice: "$149.99",
        ComparisonPriceAmount: 199.99m,
        PriceQualifier: "Online Price",
        DateSpecific: false,
        ApplicableDate: null,
        LastEvidence: Evidence(RetrievalStatus.Verified, new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync(GoldPassKey);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTripsAllFields()
    {
        var repository = CreateRepository();
        var upsert = Build(GoldPassKey);

        await repository.UpsertAsync(upsert);
        var loaded = await repository.GetAsync(GoldPassKey);

        Assert.NotNull(loaded);
        Assert.Equal(GoldPassKey.ToString(), loaded.ProductKey);
        Assert.Equal(SourceCategory.SeasonPasses, loaded.Category);
        Assert.Equal(upsert.DisplayName, loaded.DisplayName);
        Assert.Equal(upsert.LifecycleState, loaded.LifecycleState);
        Assert.Equal(upsert.FirstSeenAt, loaded.FirstSeenAt);
        Assert.Equal(upsert.LastSeenAt, loaded.LastSeenAt);
        Assert.Equal(upsert.LastVerifiedAt, loaded.LastVerifiedAt);
        Assert.Equal(upsert.CurrentPriceAmount, loaded.CurrentPriceAmount);
        Assert.Equal(upsert.CurrentDisplayPrice, loaded.CurrentDisplayPrice);
        Assert.Equal(upsert.ComparisonPriceAmount, loaded.ComparisonPriceAmount);
        Assert.Equal(upsert.PriceQualifier, loaded.PriceQualifier);
        Assert.Equal(upsert.DateSpecific, loaded.DateSpecific);
        Assert.Equal(upsert.ApplicableDate, loaded.ApplicableDate);
        Assert.Equal(upsert.LastEvidence, loaded.LastEvidence);
    }

    [Fact]
    public async Task UpsertAsync_LifecycleFields_RoundTripThroughPendingConfirmationAndArchived()
    {
        var repository = CreateRepository();

        await repository.UpsertAsync(Build(GoldPassKey, ProductLifecycleState.PendingConfirmation, absenceCount: 1));
        var pending = await repository.GetAsync(GoldPassKey);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, pending!.LifecycleState);
        Assert.Equal(1, pending.ConsecutiveConfirmedAbsenceCount);
        Assert.Null(pending.ArchivedAt);

        var archivedAt = new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero);
        await repository.UpsertAsync(Build(GoldPassKey, ProductLifecycleState.Archived, absenceCount: 2, archivedAt: archivedAt, archivedReason: "Confirmed absent on 2 consecutive successful checks."));
        var archived = await repository.GetAsync(GoldPassKey);

        Assert.Equal(ProductLifecycleState.Archived, archived!.LifecycleState);
        Assert.Equal(2, archived.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(archivedAt, archived.ArchivedAt);
        Assert.Equal("Confirmed absent on 2 consecutive successful checks.", archived.ArchivedReason);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwice_ReplacesRatherThanDuplicating()
    {
        var repository = CreateRepository();

        await repository.UpsertAsync(Build(GoldPassKey));
        await repository.UpsertAsync(Build(GoldPassKey) with { DisplayName = "Gold Season Pass" });

        var products = await repository.GetByCategoryAsync(SourceCategory.SeasonPasses);

        Assert.Single(products);
        Assert.Equal("Gold Season Pass", products[0].DisplayName);
    }

    [Fact]
    public async Task GetByCategoryAsync_FiltersByCategory()
    {
        var repository = CreateRepository();
        var addOnKey = CatalogProductKey.FromSourceAndName(SourceCategory.AddOns, "Add-ons", "Meal Plan");

        await repository.UpsertAsync(Build(GoldPassKey));
        await repository.UpsertAsync(Build(addOnKey) with { DisplayName = "Meal Plan" });

        var seasonPasses = await repository.GetByCategoryAsync(SourceCategory.SeasonPasses);

        Assert.Single(seasonPasses);
        Assert.Equal(GoldPassKey.ToString(), seasonPasses[0].ProductKey);
    }

    [Fact]
    public async Task GetByCategoryAsync_FiltersByLifecycleStateWhenProvided()
    {
        var repository = CreateRepository();
        var platinumKey = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Platinum Pass");

        await repository.UpsertAsync(Build(GoldPassKey, ProductLifecycleState.Active));
        await repository.UpsertAsync(Build(platinumKey, ProductLifecycleState.Archived, absenceCount: 2) with { DisplayName = "Platinum Pass" });

        var active = await repository.GetByCategoryAsync(SourceCategory.SeasonPasses, ProductLifecycleState.Active);

        Assert.Single(active);
        Assert.Equal(GoldPassKey.ToString(), active[0].ProductKey);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        await writer.UpsertAsync(Build(GoldPassKey));

        var reader = new SqliteCatalogProductRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(GoldPassKey);

        Assert.NotNull(loaded);
        Assert.Equal("Gold Pass", loaded.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_DisplayNameContainingQuotes_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingName = "Gold Pass' OR '1'='1";

        await repository.UpsertAsync(Build(GoldPassKey) with { DisplayName = maliciousLookingName });
        var loaded = await repository.GetAsync(GoldPassKey);

        Assert.Equal(maliciousLookingName, loaded!.DisplayName);
    }
}
