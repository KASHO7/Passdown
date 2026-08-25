using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteFinalizedPassdownRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateOnly Halloween = new(2026, 10, 31);

    private SqliteFinalizedPassdownRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static FinalizedPassdown Sample(DateOnly? date = null, FinalizationOrigin origin = FinalizationOrigin.Scheduled) => new(
        Guid.NewGuid(),
        date ?? Halloween,
        new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero),
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        """{"PassdownDate":"2026-10-31"}""",
        "abc123def456",
        origin);

    [Fact]
    public async Task GetByDateAsync_MissingDate_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetByDateAsync(Halloween));
    }

    [Fact]
    public async Task ExistsForDateAsync_MissingDate_ReturnsFalse()
    {
        var repository = CreateRepository();

        Assert.False(await repository.ExistsForDateAsync(Halloween));
    }

    [Fact]
    public async Task AddAsync_ThenGetByDateAsync_RoundTripsRecord()
    {
        var repository = CreateRepository();
        var record = Sample();

        await repository.AddAsync(record);
        var loaded = await repository.GetByDateAsync(Halloween);

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task AddAsync_ThenExistsForDateAsync_ReturnsTrue()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Sample());

        Assert.True(await repository.ExistsForDateAsync(Halloween));
    }

    [Fact]
    public async Task AddAsync_DuplicatePassdownDate_ThrowsRatherThanOverwriting()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Sample());

        await Assert.ThrowsAsync<SqliteException>(() => repository.AddAsync(Sample(origin: FinalizationOrigin.Manual)));

        // The original row must be untouched by the rejected second insert.
        var loaded = await repository.GetByDateAsync(Halloween);
        Assert.Equal(FinalizationOrigin.Scheduled, loaded!.Origin);
    }

    [Fact]
    public async Task Repository_ExposesNoUpdateOrDeleteMethod()
    {
        var methodNames = typeof(IFinalizedPassdownRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain(methodNames, name => name.Contains("Update", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_NoRecords_ReturnsEmpty()
    {
        var repository = CreateRepository();

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task ListAsync_MultipleDates_OrdersMostRecentPassdownDateFirst()
    {
        var repository = CreateRepository();
        var earlier = new DateOnly(2026, 10, 30);
        var later = new DateOnly(2026, 11, 1);

        await repository.AddAsync(Sample(Halloween));
        await repository.AddAsync(Sample(earlier));
        await repository.AddAsync(Sample(later));

        var summaries = await repository.ListAsync();

        Assert.Equal([later, Halloween, earlier], summaries.Select(s => s.PassdownDate));
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        await writer.AddAsync(Sample());

        var reader = new SqliteFinalizedPassdownRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetByDateAsync(Halloween);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task AddAsync_SnapshotContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingJson = """{"OperationalNotes":["Gate opens at 10'; DROP TABLE finalized_passdowns; --"]}""";
        var record = Sample() with { SnapshotJson = maliciousLookingJson };

        await repository.AddAsync(record);
        var loaded = await repository.GetByDateAsync(Halloween);

        Assert.Equal(maliciousLookingJson, loaded!.SnapshotJson);
    }
}
