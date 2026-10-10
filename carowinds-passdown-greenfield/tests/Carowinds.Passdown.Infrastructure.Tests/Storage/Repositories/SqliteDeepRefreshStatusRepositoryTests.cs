using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

/// <summary>Phase 13D Part U: contract tests for <see cref="SqliteDeepRefreshStatusRepository"/> over the single-row <c>deep_refresh_status</c> table.</summary>
public class SqliteDeepRefreshStatusRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private SqliteDeepRefreshStatusRepository CreateRepository() => new(_fixture.ConnectionFactory);

    [Fact]
    public async Task GetAsync_NeverSaved_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsAllFields()
    {
        var repository = CreateRepository();
        var lastAttempt = new DateTimeOffset(2026, 8, 27, 6, 3, 0, TimeSpan.Zero);
        var lastSuccess = new DateTimeOffset(2026, 8, 27, 6, 3, 0, TimeSpan.Zero);
        var record = new DeepRefreshStatusRecord(lastAttempt, lastSuccess, DeepRefreshOutcome.Success, new DateOnly(2026, 9, 30), 12, 0);

        await repository.SaveAsync(record);
        var loaded = await repository.GetAsync();

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_UpsertsRatherThanDuplicating()
    {
        var repository = CreateRepository();
        var first = new DeepRefreshStatusRecord(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DeepRefreshOutcome.Success, new DateOnly(2026, 9, 30), 12, 0);
        var second = first with { Outcome = DeepRefreshOutcome.Partial, SourceFailureCount = 2, LastSuccessAtUtc = null };

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        var loaded = await repository.GetAsync();

        Assert.Equal(second, loaded);
    }

    // Part U: a fully failed run truthfully records no success timestamp and no coverage.
    [Fact]
    public async Task SaveAsync_FailedOutcome_RoundTripsNullSuccessAndNullCoverage()
    {
        var repository = CreateRepository();
        var record = new DeepRefreshStatusRecord(DateTimeOffset.UtcNow, LastSuccessAtUtc: null, DeepRefreshOutcome.Failed, CoverageThrough: null, 0, 5);

        await repository.SaveAsync(record);
        var loaded = await repository.GetAsync();

        Assert.Equal(record, loaded);
        Assert.Null(loaded!.LastSuccessAtUtc);
        Assert.Null(loaded.CoverageThrough);
    }
}
