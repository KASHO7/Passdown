using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteManagerDraftRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateOnly Halloween = new(2026, 10, 31);

    private SqliteManagerDraftRepository CreateRepository() => new(_fixture.ConnectionFactory);

    [Fact]
    public async Task GetAsync_MissingDraft_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync(Halloween, "day");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsDraft()
    {
        var repository = CreateRepository();
        var draft = new ManagerDraft(Halloween, "day", 1, """{"staffing":"full"}""", new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync(draft);
        var loaded = await repository.GetAsync(Halloween, "day");

        Assert.Equal(draft, loaded);
    }

    [Fact]
    public async Task SameDate_DifferentShifts_AreIsolated()
    {
        var repository = CreateRepository();
        var dayDraft = new ManagerDraft(Halloween, "day", 1, """{"shift":"day"}""", DateTimeOffset.UtcNow);
        var nightDraft = new ManagerDraft(Halloween, "night", 1, """{"shift":"night"}""", DateTimeOffset.UtcNow);

        await repository.SaveAsync(dayDraft);
        await repository.SaveAsync(nightDraft);

        var loadedDay = await repository.GetAsync(Halloween, "day");
        var loadedNight = await repository.GetAsync(Halloween, "night");

        Assert.Equal(dayDraft, loadedDay);
        Assert.Equal(nightDraft, loadedNight);
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceForSameDateAndShift_UpsertsRatherThanDuplicating()
    {
        var repository = CreateRepository();
        var first = new ManagerDraft(Halloween, "day", 1, "{}", new DateTimeOffset(2026, 8, 19, 6, 0, 0, TimeSpan.Zero));
        var second = new ManagerDraft(Halloween, "day", 2, """{"staffing":"updated"}""", new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        var loaded = await repository.GetAsync(Halloween, "day");

        Assert.Equal(second, loaded);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var draft = new ManagerDraft(Halloween, "day", 1, "{}", DateTimeOffset.UtcNow);
        await writer.SaveAsync(draft);

        var reader = new SqliteManagerDraftRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(Halloween, "day");

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task SaveAsync_ShiftContainingQuoteCharacter_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingShift = "day'; DROP TABLE manager_drafts; --";
        var draft = new ManagerDraft(Halloween, maliciousLookingShift, 1, "{}", DateTimeOffset.UtcNow);

        await repository.SaveAsync(draft);
        var loaded = await repository.GetAsync(Halloween, maliciousLookingShift);

        Assert.Equal(draft, loaded);
    }
}
