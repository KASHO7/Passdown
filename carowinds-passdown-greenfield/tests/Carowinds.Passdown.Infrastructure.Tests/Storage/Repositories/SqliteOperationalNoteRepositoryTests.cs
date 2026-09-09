using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteOperationalNoteRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateOnly Today = new(2026, 8, 26);

    private SqliteOperationalNoteRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static OperationalNote Sample(
        Guid? id = null,
        string title = "South Gate scanner #3 out of service",
        string? body = "IT ticket submitted. Use scanner #2 until repaired.",
        OperationalNoteCategory category = OperationalNoteCategory.Equipment,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? updatedAtUtc = null,
        DateTimeOffset? resolvedAtUtc = null) => new(
        id ?? Guid.NewGuid(),
        title,
        body,
        category,
        priority,
        effectiveFrom ?? Today,
        effectiveThrough,
        status,
        createdAtUtc ?? new DateTimeOffset(2026, 8, 26, 6, 0, 0, TimeSpan.Zero),
        updatedAtUtc ?? createdAtUtc ?? new DateTimeOffset(2026, 8, 26, 6, 0, 0, TimeSpan.Zero),
        resolvedAtUtc);

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsRecord()
    {
        var repository = CreateRepository();
        var note = Sample();

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(note, loaded);
    }

    /// <summary>Phase 16D §13/§14: the sync engine's tombstone primitive — never wired to any product UI (§12), only exercised directly here.</summary>
    [Fact]
    public async Task DeleteAsync_RemovesTheNoteRow()
    {
        var repository = CreateRepository();
        var note = Sample();
        await repository.AddAsync(note);

        await repository.DeleteAsync(note.Id);

        Assert.Null(await repository.GetByIdAsync(note.Id));
    }

    [Fact]
    public async Task DeleteAsync_WhenNoRowExists_IsANoOp()
    {
        var repository = CreateRepository();

        await repository.DeleteAsync(Guid.NewGuid());

        Assert.True(true);
    }

    [Fact]
    public async Task AddAsync_NullEffectiveThrough_RoundTripsAsNull()
    {
        var repository = CreateRepository();
        var note = Sample(effectiveThrough: null);

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Null(loaded!.EffectiveThrough);
    }

    [Fact]
    public async Task AddAsync_NonNullEffectiveThrough_RoundTrips()
    {
        var repository = CreateRepository();
        var note = Sample(effectiveThrough: Today.AddDays(4));

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(Today.AddDays(4), loaded!.EffectiveThrough);
    }

    [Fact]
    public async Task AddAsync_NullBody_RoundTripsAsNull()
    {
        var repository = CreateRepository();
        var note = Sample(body: null);

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Null(loaded!.Body);
    }

    [Theory]
    [InlineData(OperationalNoteCategory.General)]
    [InlineData(OperationalNoteCategory.Staffing)]
    [InlineData(OperationalNoteCategory.Admissions)]
    [InlineData(OperationalNoteCategory.GuestServices)]
    [InlineData(OperationalNoteCategory.Equipment)]
    [InlineData(OperationalNoteCategory.Events)]
    [InlineData(OperationalNoteCategory.Operations)]
    public async Task AddAsync_EveryCategory_RoundTrips(OperationalNoteCategory category)
    {
        var repository = CreateRepository();
        var note = Sample(category: category);

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(category, loaded!.Category);
    }

    [Theory]
    [InlineData(OperationalNotePriority.Normal)]
    [InlineData(OperationalNotePriority.Important)]
    public async Task AddAsync_EveryPriority_RoundTrips(OperationalNotePriority priority)
    {
        var repository = CreateRepository();
        var note = Sample(priority: priority);

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(priority, loaded!.Priority);
    }

    [Fact]
    public async Task AddAsync_TimestampsRoundTripExactly()
    {
        var repository = CreateRepository();
        var createdAt = new DateTimeOffset(2026, 8, 20, 13, 45, 30, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 8, 21, 9, 15, 0, TimeSpan.Zero);
        var note = Sample(createdAtUtc: createdAt, updatedAtUtc: updatedAt);

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(createdAt, loaded!.CreatedAtUtc);
        Assert.Equal(updatedAt, loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_ChangesStoredRow_WithoutChangingId()
    {
        var repository = CreateRepository();
        var note = Sample();
        await repository.AddAsync(note);

        var updated = note with { Title = "South Gate scanner #3 repaired", UpdatedAtUtc = note.UpdatedAtUtc.AddHours(1) };
        await repository.UpdateAsync(updated);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(note.Id, loaded!.Id);
        Assert.Equal("South Gate scanner #3 repaired", loaded.Title);
    }

    [Fact]
    public async Task UpdateAsync_ResolvingANote_PersistsStatusAndResolvedAtUtc()
    {
        var repository = CreateRepository();
        var note = Sample();
        await repository.AddAsync(note);

        var resolvedAt = note.UpdatedAtUtc.AddDays(1);
        var resolved = note with { Status = OperationalNoteStatus.Resolved, ResolvedAtUtc = resolvedAt, UpdatedAtUtc = resolvedAt };
        await repository.UpdateAsync(resolved);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(OperationalNoteStatus.Resolved, loaded!.Status);
        Assert.Equal(resolvedAt, loaded.ResolvedAtUtc);
    }

    [Fact]
    public async Task ListAsync_NoRecords_ReturnsEmpty()
    {
        var repository = CreateRepository();

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsEveryNote_ActiveAndResolvedAlike()
    {
        var repository = CreateRepository();
        var active = Sample();
        var resolved = Sample(status: OperationalNoteStatus.Resolved, resolvedAtUtc: DateTimeOffset.UtcNow);
        await repository.AddAsync(active);
        await repository.AddAsync(resolved);

        var all = await repository.ListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, n => n.Id == active.Id);
        Assert.Contains(all, n => n.Id == resolved.Id);
    }

    /// <summary>
    /// Updated for Phase 16D §13: the repository now exposes exactly one delete primitive,
    /// <see cref="IOperationalNoteRepository.DeleteAsync"/> — added solely as the sync engine's
    /// tombstone-import/resolution primitive (no product "delete note" action exists or calls it; see
    /// <c>docs/personal-sync-architecture.md</c> Phase 16D §12/§13). The original invariant this test
    /// protects — no casual "Remove"-style convenience method a normal application code path could call
    /// to silently drop a historical note row — still holds.
    /// </summary>
    [Fact]
    public void Repository_ExposesExactlyOneDeleteMethod_ForSyncTombstonesOnly_AndNoRemoveMethod()
    {
        var methodNames = typeof(IOperationalNoteRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.Single(methodNames, name => string.Equals(name, nameof(IOperationalNoteRepository.DeleteAsync), StringComparison.Ordinal));
        Assert.DoesNotContain(methodNames, name => name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    // ---- ListRelevantForDateAsync (spec Part M) ----

    [Fact]
    public async Task ListRelevantForDateAsync_ActiveWithinRange_IsIncluded()
    {
        var repository = CreateRepository();
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: Today.AddDays(1));
        await repository.AddAsync(note);

        var relevant = await repository.ListRelevantForDateAsync(Today);

        Assert.Contains(relevant, n => n.Id == note.Id);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_ResolvedNote_IsExcluded()
    {
        var repository = CreateRepository();
        var note = Sample(status: OperationalNoteStatus.Resolved, resolvedAtUtc: DateTimeOffset.UtcNow);
        await repository.AddAsync(note);

        var relevant = await repository.ListRelevantForDateAsync(Today);

        Assert.Empty(relevant);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_BeforeEffectiveFrom_IsExcluded()
    {
        var repository = CreateRepository();
        var note = Sample(effectiveFrom: Today.AddDays(2));
        await repository.AddAsync(note);

        var relevant = await repository.ListRelevantForDateAsync(Today);

        Assert.Empty(relevant);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_AfterEffectiveThrough_IsExcluded()
    {
        var repository = CreateRepository();
        var note = Sample(effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1));
        await repository.AddAsync(note);

        var relevant = await repository.ListRelevantForDateAsync(Today);

        Assert.Empty(relevant);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_OpenEndedEffectiveThrough_IsIncludedFarInTheFuture()
    {
        var repository = CreateRepository();
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: null);
        await repository.AddAsync(note);

        var relevant = await repository.ListRelevantForDateAsync(Today.AddYears(1));

        Assert.Contains(relevant, n => n.Id == note.Id);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var note = Sample();
        await writer.AddAsync(note);

        var reader = new SqliteOperationalNoteRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetByIdAsync(note.Id);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task AddAsync_TitleAndBodyContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingBody = "Gate opens at 10'; DROP TABLE operational_notes; --";
        var note = Sample(body: maliciousLookingBody);

        await repository.AddAsync(note);
        var loaded = await repository.GetByIdAsync(note.Id);

        Assert.Equal(maliciousLookingBody, loaded!.Body);
    }

    // ---- Deterministic ordering, exercised through the repository's own SQL ORDER BY (spec Part W) ----

    [Fact]
    public async Task ListAsync_OrdersByCreatedAtDescending()
    {
        var repository = CreateRepository();
        var older = Sample(createdAtUtc: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero), updatedAtUtc: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));
        var newer = Sample(createdAtUtc: new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero), updatedAtUtc: new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));
        await repository.AddAsync(older);
        await repository.AddAsync(newer);

        var all = await repository.ListAsync();

        Assert.Equal([newer.Id, older.Id], all.Select(n => n.Id));
    }
}
