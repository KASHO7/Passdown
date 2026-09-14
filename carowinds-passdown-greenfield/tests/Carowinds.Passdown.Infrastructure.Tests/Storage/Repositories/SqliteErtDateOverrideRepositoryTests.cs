using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteErtDateOverrideRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateOnly Today = new(2026, 8, 19);
    private static readonly DateTimeOffset ModifiedUtc = new(2026, 8, 19, 6, 0, 0, TimeSpan.Zero);

    private SqliteErtDateOverrideRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static ErtDateOverride Sample(
        DateOnly? operatingDate = null,
        ErtConfigurationState state = ErtConfigurationState.Configured,
        TimeOnly? startTime = null,
        IReadOnlyList<string>? rides = null,
        string? reason = "Fury maintenance",
        DateTimeOffset? modifiedUtc = null) => new(
        operatingDate ?? Today, state,
        state == ErtConfigurationState.Configured ? startTime ?? new TimeOnly(10, 0) : null,
        state == ErtConfigurationState.Configured ? rides ?? ["Copperhead Strike"] : [],
        reason,
        modifiedUtc ?? ModifiedUtc);

    [Fact]
    public async Task GetAsync_NoOverride_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetAsync(Today));
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTripsRecord()
    {
        var repository = CreateRepository();
        var dateOverride = Sample();

        await repository.UpsertAsync(dateOverride);
        var loaded = await repository.GetAsync(Today);

        // Field-by-field — see SqliteMonthlyErtRepositoryTests' identical note on Rides equality.
        Assert.Equal(dateOverride.OperatingDate, loaded!.OperatingDate);
        Assert.Equal(dateOverride.State, loaded.State);
        Assert.Equal(dateOverride.StartTime, loaded.StartTime);
        Assert.Equal(dateOverride.Rides, loaded.Rides);
        Assert.Equal(dateOverride.Reason, loaded.Reason);
        Assert.Equal(dateOverride.ModifiedUtc, loaded.ModifiedUtc);
    }

    [Fact]
    public async Task UpsertAsync_SameDate_ReplacesExistingRow()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample(startTime: new TimeOnly(10, 0)));

        await repository.UpsertAsync(Sample(startTime: new TimeOnly(11, 0), reason: "Weather delay"));
        var loaded = await repository.GetAsync(Today);

        Assert.Equal(new TimeOnly(11, 0), loaded!.StartTime);
        Assert.Equal("Weather delay", loaded.Reason);
    }

    [Fact]
    public async Task UpsertAsync_DifferentDates_AreIndependentRows()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample(operatingDate: Today, startTime: new TimeOnly(10, 0)));
        await repository.UpsertAsync(Sample(operatingDate: Today.AddDays(1), startTime: new TimeOnly(11, 0)));

        Assert.Equal(new TimeOnly(10, 0), (await repository.GetAsync(Today))!.StartTime);
        Assert.Equal(new TimeOnly(11, 0), (await repository.GetAsync(Today.AddDays(1)))!.StartTime);
    }

    [Fact]
    public async Task UpsertAsync_NoErtState_RoundTripsNullStartTimeAndEmptyRides()
    {
        var repository = CreateRepository();
        var dateOverride = Sample(state: ErtConfigurationState.NoErt);

        await repository.UpsertAsync(dateOverride);
        var loaded = await repository.GetAsync(Today);

        Assert.Equal(ErtConfigurationState.NoErt, loaded!.State);
        Assert.Null(loaded.StartTime);
        Assert.Empty(loaded.Rides);
    }

    [Fact]
    public async Task UpsertAsync_NullReason_RoundTripsAsNull()
    {
        var repository = CreateRepository();
        var dateOverride = Sample(reason: null);

        await repository.UpsertAsync(dateOverride);
        var loaded = await repository.GetAsync(Today);

        Assert.Null(loaded!.Reason);
    }

    /// <summary>
    /// Phase 17B spec Part 8/23/28: unlike the monthly repository's Delete (sync-only), this method IS
    /// the product-facing "Remove override" action's persistence step, in addition to being the sync
    /// tombstone primitive — both roles are legitimate for this one repository.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_RemovesTheRow_SoTheDateFallsBackToTheMonthlyDefault()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample());

        await repository.DeleteAsync(Today);

        Assert.Null(await repository.GetAsync(Today));
    }

    [Fact]
    public async Task DeleteAsync_WhenNoRowExists_IsANoOp()
    {
        var repository = CreateRepository();

        await repository.DeleteAsync(Today);

        Assert.True(true);
    }

    [Fact]
    public async Task ListAsync_NoRecords_ReturnsEmpty()
    {
        var repository = CreateRepository();

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsEveryOverride()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(Sample(operatingDate: Today));
        await repository.UpsertAsync(Sample(operatingDate: Today.AddDays(1)));

        var all = await repository.ListAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        await writer.UpsertAsync(Sample());

        var reader = new SqliteErtDateOverrideRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(Today);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task UpsertAsync_ReasonContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingReason = "Rain delay'; DROP TABLE ert_date_overrides; --";
        var dateOverride = Sample(reason: maliciousLookingReason);

        await repository.UpsertAsync(dateOverride);
        var loaded = await repository.GetAsync(Today);

        Assert.Equal(maliciousLookingReason, loaded!.Reason);
    }
}
