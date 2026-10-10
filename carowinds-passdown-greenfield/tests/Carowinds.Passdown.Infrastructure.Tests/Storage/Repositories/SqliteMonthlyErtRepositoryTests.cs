using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteMonthlyErtRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateTimeOffset ModifiedUtc = new(2026, 8, 26, 6, 0, 0, TimeSpan.Zero);

    private SqliteMonthlyErtRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static MonthlyErtConfiguration Sample(
        int year = 2026,
        int month = 8,
        ErtConfigurationState state = ErtConfigurationState.Configured,
        TimeOnly? startTime = null,
        IReadOnlyList<string>? rides = null,
        string? note = "Note text.",
        DateTimeOffset? modifiedUtc = null) => new(
        year, month, state,
        state == ErtConfigurationState.Configured ? startTime ?? new TimeOnly(9, 30) : null,
        state == ErtConfigurationState.Configured ? rides ?? ["Fury 325", "Copperhead Strike"] : [],
        note,
        modifiedUtc ?? ModifiedUtc);

    [Fact]
    public async Task GetAsync_NoConfiguration_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetAsync(2026, 8));
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTripsRecord()
    {
        var repository = CreateRepository();
        var configuration = Sample();

        await repository.UpsertAsync(configuration);
        var loaded = await repository.GetAsync(2026, 8);

        // Field-by-field — Rides (IReadOnlyList<string>) falls back to reference equality on the
        // record's own compiler-generated Equals, exactly like AssociateDraftPayload's own
        // AssertDraftsEqual helper never compares its list-typed fields via a whole-record Assert.Equal.
        Assert.Equal(configuration.Year, loaded!.Year);
        Assert.Equal(configuration.Month, loaded.Month);
        Assert.Equal(configuration.State, loaded.State);
        Assert.Equal(configuration.StartTime, loaded.StartTime);
        Assert.Equal(configuration.Rides, loaded.Rides);
        Assert.Equal(configuration.Note, loaded.Note);
        Assert.Equal(configuration.ModifiedUtc, loaded.ModifiedUtc);
    }

    [Fact]
    public async Task UpsertAsync_SameMonth_ReplacesExistingRow()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample(startTime: new TimeOnly(9, 30)));

        await repository.UpsertAsync(Sample(startTime: new TimeOnly(10, 0), rides: ["Intimidator"]));
        var loaded = await repository.GetAsync(2026, 8);

        Assert.Equal(new TimeOnly(10, 0), loaded!.StartTime);
        Assert.Equal(["Intimidator"], loaded.Rides);
    }

    // Spec Part 5: September 2026 and September 2027 are different configurations.
    [Fact]
    public async Task UpsertAsync_SameMonthDifferentYear_AreIndependentRows()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample(year: 2026, startTime: new TimeOnly(9, 0)));
        await repository.UpsertAsync(Sample(year: 2027, startTime: new TimeOnly(10, 0)));

        Assert.Equal(new TimeOnly(9, 0), (await repository.GetAsync(2026, 8))!.StartTime);
        Assert.Equal(new TimeOnly(10, 0), (await repository.GetAsync(2027, 8))!.StartTime);
    }

    [Fact]
    public async Task UpsertAsync_NoErtState_RoundTripsNullStartTimeAndEmptyRides()
    {
        var repository = CreateRepository();
        var configuration = Sample(state: ErtConfigurationState.NoErt);

        await repository.UpsertAsync(configuration);
        var loaded = await repository.GetAsync(2026, 8);

        Assert.Equal(ErtConfigurationState.NoErt, loaded!.State);
        Assert.Null(loaded.StartTime);
        Assert.Empty(loaded.Rides);
    }

    [Fact]
    public async Task UpsertAsync_NullNote_RoundTripsAsNull()
    {
        var repository = CreateRepository();
        var configuration = Sample(note: null);

        await repository.UpsertAsync(configuration);
        var loaded = await repository.GetAsync(2026, 8);

        Assert.Null(loaded!.Note);
    }

    [Fact]
    public async Task UpsertAsync_EmptyRideList_RoundTrips()
    {
        var repository = CreateRepository();
        var configuration = Sample(rides: []);

        await repository.UpsertAsync(configuration);
        var loaded = await repository.GetAsync(2026, 8);

        Assert.Empty(loaded!.Rides);
    }

    /// <summary>Phase 17B spec Part 23: the sync engine's tombstone primitive — no product "delete" UI calls it.</summary>
    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample());

        await repository.DeleteAsync(2026, 8);

        Assert.Null(await repository.GetAsync(2026, 8));
    }

    [Fact]
    public async Task DeleteAsync_WhenNoRowExists_IsANoOp()
    {
        var repository = CreateRepository();

        await repository.DeleteAsync(2026, 8);

        Assert.True(true);
    }

    [Fact]
    public void Repository_ExposesExactlyOneDeleteMethod_ForSyncTombstonesOnly_AndNoRemoveMethod()
    {
        var methodNames = typeof(IMonthlyErtRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.Single(methodNames, name => string.Equals(name, nameof(IMonthlyErtRepository.DeleteAsync), StringComparison.Ordinal));
        Assert.DoesNotContain(methodNames, name => name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_NoRecords_ReturnsEmpty()
    {
        var repository = CreateRepository();

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsEveryConfiguration()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample(year: 2026, month: 8));
        await repository.UpsertAsync(Sample(year: 2026, month: 9));

        var all = await repository.ListAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        await writer.UpsertAsync(Sample());

        var reader = new SqliteMonthlyErtRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(2026, 8);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task UpsertAsync_RideNameContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingRide = "Fury 325'; DROP TABLE monthly_ert_configurations; --";
        var configuration = Sample(rides: [maliciousLookingRide]);

        await repository.UpsertAsync(configuration);
        var loaded = await repository.GetAsync(2026, 8);

        Assert.Equal([maliciousLookingRide], loaded!.Rides);
    }
}
