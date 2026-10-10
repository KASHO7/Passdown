using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

public class SqliteUnitOfWorkTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteUnitOfWorkTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-uow-tests-{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_databasePath);

        IReadOnlyList<IMigration> migrations =
        [
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
        ];
        new MigrationRunner(_connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/passes");
    private static readonly CatalogProductKey Key =
        CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass");

    private static SourceEvidence Evidence(DateTimeOffset timeChecked) => new(
        sourceName: "Season passes",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: RetrievalStatus.Verified,
        detail: "Checked successfully.",
        strategy: RetrievalStrategy.Html);

    private static CatalogProductUpsert Upsert(DateTimeOffset now) => new(
        Key: Key,
        DisplayName: "Gold Pass",
        LifecycleState: ProductLifecycleState.Active,
        FirstSeenAt: now,
        LastSeenAt: now,
        LastVerifiedAt: now,
        ConsecutiveConfirmedAbsenceCount: 0,
        ArchivedAt: null,
        ArchivedReason: null,
        CurrentPriceAmount: 149.99m,
        CurrentDisplayPrice: "$149.99",
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        DateSpecific: false,
        ApplicableDate: null,
        LastEvidence: Evidence(now));

    private static ProductHistoryEntry HistoryEntry(DateTimeOffset now) => new(
        ProductKey: Key.ToString(),
        RecordedAt: now,
        DisplayName: "Gold Pass",
        LifecycleState: ProductLifecycleState.Active,
        CurrentPriceAmount: 149.99m,
        CurrentDisplayPrice: "$149.99",
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        ChangeReason: "New product observed.");

    [Fact]
    public async Task CommittedUnitOfWork_PersistsWritesToBothTables()
    {
        var now = DateTimeOffset.UtcNow;
        var catalogRepo = new SqliteCatalogProductRepository(_connectionFactory);
        var historyRepo = new SqliteProductHistoryRepository(_connectionFactory);
        var uowFactory = new SqliteUnitOfWorkFactory(_connectionFactory);

        await using (var unitOfWork = await uowFactory.BeginAsync())
        {
            await catalogRepo.UpsertAsync(Upsert(now), unitOfWork);
            await historyRepo.AppendAsync(HistoryEntry(now), unitOfWork);
            await unitOfWork.CommitAsync();
        }

        var product = await catalogRepo.GetAsync(Key);
        var history = await historyRepo.GetHistoryAsync(Key.ToString());

        Assert.NotNull(product);
        Assert.Single(history);
    }

    [Fact]
    public async Task DisposedWithoutCommit_RollsBackWritesToBothTables()
    {
        var now = DateTimeOffset.UtcNow;
        var catalogRepo = new SqliteCatalogProductRepository(_connectionFactory);
        var historyRepo = new SqliteProductHistoryRepository(_connectionFactory);
        var uowFactory = new SqliteUnitOfWorkFactory(_connectionFactory);

        await using (var unitOfWork = await uowFactory.BeginAsync())
        {
            await catalogRepo.UpsertAsync(Upsert(now), unitOfWork);
            await historyRepo.AppendAsync(HistoryEntry(now), unitOfWork);
            // Deliberately not committed.
        }

        var product = await catalogRepo.GetAsync(Key);
        var history = await historyRepo.GetHistoryAsync(Key.ToString());

        Assert.Null(product);
        Assert.Empty(history);
    }

    [Fact]
    public async Task WritesInsideAnUncommittedUnitOfWork_AreNotVisibleToAConnectionOutsideIt()
    {
        var now = DateTimeOffset.UtcNow;
        var catalogRepo = new SqliteCatalogProductRepository(_connectionFactory);
        var uowFactory = new SqliteUnitOfWorkFactory(_connectionFactory);

        await using var unitOfWork = await uowFactory.BeginAsync();
        await catalogRepo.UpsertAsync(Upsert(now), unitOfWork);

        // A read through a plain (non-unit-of-work) call opens its own connection
        // and must not see the uncommitted write.
        var productBeforeCommit = await catalogRepo.GetAsync(Key);
        Assert.Null(productBeforeCommit);

        await unitOfWork.CommitAsync();
        var productAfterCommit = await catalogRepo.GetAsync(Key);
        Assert.NotNull(productAfterCommit);
    }

    [Fact]
    public async Task UpsertAsync_WithoutAUnitOfWork_StillWorksAgainstItsOwnConnection()
    {
        var now = DateTimeOffset.UtcNow;
        var catalogRepo = new SqliteCatalogProductRepository(_connectionFactory);

        await catalogRepo.UpsertAsync(Upsert(now));
        var product = await catalogRepo.GetAsync(Key);

        Assert.NotNull(product);
    }
}
