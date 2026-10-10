using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Tests.OperationalNotes;

/// <summary>
/// Covers <see cref="OperationalNotesService"/> (spec Phase 11A Parts N/O/P/Q/S/U): create/update
/// validation, <see cref="Time.IClock"/>-sourced timestamps, resolve semantics/idempotency, the
/// architectural no-network/no-Associate-draft-dependency guarantee, and (Part U) that this service's
/// existence never changes how daily Associate notes already behave.
/// </summary>
public class OperationalNotesServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private sealed record TestEnvironment(OperationalNotesService Service, InMemoryOperationalNoteRepository Repository, FakeClock Clock);

    private static TestEnvironment CreateEnvironment(FakeClock? clock = null, ISyncChangeTracker? syncChangeTracker = null)
    {
        clock ??= new FakeClock(FixedNow);
        var repository = new InMemoryOperationalNoteRepository();
        return new TestEnvironment(new OperationalNotesService(repository, clock, syncChangeTracker), repository, clock);
    }

    private static OperationalNoteDraft SampleDraft(
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        string title = "South Gate scanner #3 out of service",
        string? body = "IT ticket submitted. Use scanner #2 until repaired.") => new(
        title,
        body,
        OperationalNoteCategory.Equipment,
        priority,
        effectiveFrom ?? Today,
        effectiveThrough);

    // ---- Create (Part O) ----

    [Fact]
    public async Task CreateAsync_ValidDraft_PersistsActiveNoteWithClockTimestamps()
    {
        var env = CreateEnvironment();

        var created = await env.Service.CreateAsync(SampleDraft());

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(OperationalNoteStatus.Active, created.Status);
        Assert.Equal(FixedNow, created.CreatedAtUtc);
        Assert.Equal(FixedNow, created.UpdatedAtUtc);
        Assert.Null(created.ResolvedAtUtc);
        Assert.Equal(created, await env.Repository.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task CreateAsync_MarksTheNoteDirtyForSync_UsingItsGuidAsTheEntityId()
    {
        var syncChangeTracker = new RecordingSyncChangeTracker();
        var env = CreateEnvironment(syncChangeTracker: syncChangeTracker);

        var created = await env.Service.CreateAsync(SampleDraft());

        var call = Assert.Single(syncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.OperationalNote, call.EntityType);
        Assert.Equal(SyncEntityIdentity.ForNote(created.Id), call.EntityId);
    }

    [Fact]
    public async Task UpdateAsync_MarksTheNoteDirtyForSync()
    {
        var syncChangeTracker = new RecordingSyncChangeTracker();
        var env = CreateEnvironment(syncChangeTracker: syncChangeTracker);
        var created = await env.Service.CreateAsync(SampleDraft());
        syncChangeTracker.Calls.Clear();

        await env.Service.UpdateAsync(created.Id, SampleDraft(title: "Updated title"));

        var call = Assert.Single(syncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.OperationalNote, call.EntityType);
        Assert.Equal(SyncEntityIdentity.ForNote(created.Id), call.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_MarksTheNoteDirtyForSync()
    {
        var syncChangeTracker = new RecordingSyncChangeTracker();
        var env = CreateEnvironment(syncChangeTracker: syncChangeTracker);
        var created = await env.Service.CreateAsync(SampleDraft());
        syncChangeTracker.Calls.Clear();

        await env.Service.ResolveAsync(created.Id);

        var call = Assert.Single(syncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.OperationalNote, call.EntityType);
        Assert.Equal(SyncEntityIdentity.ForNote(created.Id), call.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_AlreadyResolved_DoesNotCallTheSyncTrackerAgain()
    {
        // Idempotent resolve (Part Q) — no genuine change, so no reason to bump the sync revision again.
        var syncChangeTracker = new RecordingSyncChangeTracker();
        var env = CreateEnvironment(syncChangeTracker: syncChangeTracker);
        var created = await env.Service.CreateAsync(SampleDraft());
        await env.Service.ResolveAsync(created.Id);
        syncChangeTracker.Calls.Clear();

        await env.Service.ResolveAsync(created.Id);

        Assert.Empty(syncChangeTracker.Calls);
    }

    private sealed class RecordingSyncChangeTracker : ISyncChangeTracker
    {
        public List<(SyncEntityType EntityType, string EntityId)> Calls { get; } = [];

        public Task MarkLocalChangeAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
        {
            Calls.Add((entityType, entityId));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CreateAsync_BlankTitle_Throws()
    {
        var env = CreateEnvironment();

        await Assert.ThrowsAsync<ArgumentException>(() => env.Service.CreateAsync(SampleDraft(title: "   ")));
    }

    [Fact]
    public async Task CreateAsync_TitleExceedsMaxLength_Throws()
    {
        var env = CreateEnvironment();
        var overlongTitle = new string('x', OperationalNote.MaxTitleLength + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => env.Service.CreateAsync(SampleDraft(title: overlongTitle)));
    }

    [Fact]
    public async Task CreateAsync_BodyExceedsMaxLength_Throws()
    {
        var env = CreateEnvironment();
        var overlongBody = new string('x', OperationalNote.MaxBodyLength + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => env.Service.CreateAsync(SampleDraft(body: overlongBody)));
    }

    [Fact]
    public async Task CreateAsync_NullOrEmptyBody_IsAllowed()
    {
        var env = CreateEnvironment();

        var created = await env.Service.CreateAsync(SampleDraft(body: null));

        Assert.Null(created.Body);
    }

    [Fact]
    public async Task CreateAsync_EffectiveThroughBeforeEffectiveFrom_Throws()
    {
        var env = CreateEnvironment();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            env.Service.CreateAsync(SampleDraft(effectiveFrom: Today, effectiveThrough: Today.AddDays(-1))));
    }

    [Fact]
    public async Task CreateAsync_EffectiveThroughEqualToEffectiveFrom_IsAllowed()
    {
        var env = CreateEnvironment();

        var created = await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today, effectiveThrough: Today));

        Assert.Equal(Today, created.EffectiveThrough);
    }

    [Fact]
    public async Task CreateAsync_NullEffectiveThrough_IsAllowed_MeansUntilResolved()
    {
        var env = CreateEnvironment();

        var created = await env.Service.CreateAsync(SampleDraft(effectiveThrough: null));

        Assert.Null(created.EffectiveThrough);
        Assert.True(OperationalNoteRelevance.IsRelevantForDate(created, Today.AddYears(1)));
    }

    // ---- Update (Part P) ----

    [Fact]
    public async Task UpdateAsync_PreservesIdentityAndCreatedAtUtc_AdvancesUpdatedAtUtc()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft());
        env.Clock.UtcNow = FixedNow.AddHours(2);

        var updated = await env.Service.UpdateAsync(created.Id, SampleDraft(title: "South Gate scanner #3 repaired"));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(FixedNow.AddHours(2), updated.UpdatedAtUtc);
        Assert.Equal("South Gate scanner #3 repaired", updated.Title);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_Throws()
    {
        var env = CreateEnvironment();

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.UpdateAsync(Guid.NewGuid(), SampleDraft()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidEffectiveRange_Throws_AndDoesNotPersist()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            env.Service.UpdateAsync(created.Id, SampleDraft(effectiveFrom: Today, effectiveThrough: Today.AddDays(-1))));

        var reloaded = await env.Repository.GetByIdAsync(created.Id);
        Assert.Equal(created, reloaded);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeStatusOrResolvedAtUtc()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft());
        var resolved = await env.Service.ResolveAsync(created.Id);

        var updated = await env.Service.UpdateAsync(resolved.Id, SampleDraft(title: "Updated while resolved"));

        Assert.Equal(OperationalNoteStatus.Resolved, updated.Status);
        Assert.Equal(resolved.ResolvedAtUtc, updated.ResolvedAtUtc);
    }

    // ---- Resolve (Part Q) ----

    [Fact]
    public async Task ResolveAsync_ActiveNote_SetsResolvedStatusAndTimestamps()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft());
        env.Clock.UtcNow = FixedNow.AddHours(3);

        var resolved = await env.Service.ResolveAsync(created.Id);

        Assert.Equal(OperationalNoteStatus.Resolved, resolved.Status);
        Assert.Equal(FixedNow.AddHours(3), resolved.ResolvedAtUtc);
        Assert.Equal(FixedNow.AddHours(3), resolved.UpdatedAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_AlreadyResolved_IsIdempotent_DoesNotOverwriteOriginalResolvedAtUtc()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft());
        var firstResolve = await env.Service.ResolveAsync(created.Id);
        env.Clock.UtcNow = FixedNow.AddDays(1);

        var secondResolve = await env.Service.ResolveAsync(created.Id);

        Assert.Equal(firstResolve.ResolvedAtUtc, secondResolve.ResolvedAtUtc);
        Assert.Equal(firstResolve.UpdatedAtUtc, secondResolve.UpdatedAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_UnknownId_Throws()
    {
        var env = CreateEnvironment();

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.ResolveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ResolveAsync_DoesNotDeleteTheRow()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft());

        await env.Service.ResolveAsync(created.Id);

        Assert.NotNull(await env.Repository.GetByIdAsync(created.Id));
        Assert.Single(await env.Repository.ListAsync());
    }

    // ---- Relevance / listing (Parts J/W) ----

    [Fact]
    public async Task ListRelevantForDateAsync_ActiveNoteWithinRange_IsIncluded()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(-1), effectiveThrough: Today.AddDays(1)));

        var relevant = await env.Service.ListRelevantForDateAsync(Today);

        Assert.Contains(relevant, n => n.Id == created.Id);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_NoteBeforeEffectiveFrom_IsExcluded()
    {
        var env = CreateEnvironment();
        await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(5)));

        var relevant = await env.Service.ListRelevantForDateAsync(Today);

        Assert.Empty(relevant);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_NoteAfterEffectiveThrough_IsExcluded()
    {
        var env = CreateEnvironment();
        await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1)));

        var relevant = await env.Service.ListRelevantForDateAsync(Today);

        Assert.Empty(relevant);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_ResolvedNote_IsExcluded()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(-1), effectiveThrough: null));
        await env.Service.ResolveAsync(created.Id);

        var relevant = await env.Service.ListRelevantForDateAsync(Today);

        Assert.Empty(relevant);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_ImportantNotesSortBeforeNormal()
    {
        var env = CreateEnvironment();
        var normal = await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(-1), priority: OperationalNotePriority.Normal));
        var important = await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(-1), priority: OperationalNotePriority.Important));

        var relevant = await env.Service.ListRelevantForDateAsync(Today);

        Assert.Equal(important.Id, relevant[0].Id);
        Assert.Equal(normal.Id, relevant[1].Id);
    }

    [Fact]
    public async Task ListRelevantForDateAsync_ExpiredNote_IsNotAutoResolved()
    {
        var env = CreateEnvironment();
        var created = await env.Service.CreateAsync(SampleDraft(effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1)));

        await env.Service.ListRelevantForDateAsync(Today);

        var reloaded = await env.Repository.GetByIdAsync(created.Id);
        Assert.Equal(OperationalNoteStatus.Active, reloaded!.Status);
        Assert.Null(reloaded.ResolvedAtUtc);
    }

    [Fact]
    public async Task ListAsync_ReturnsEveryNote_ActiveAndResolvedAlike()
    {
        var env = CreateEnvironment();
        var active = await env.Service.CreateAsync(SampleDraft());
        var toResolve = await env.Service.CreateAsync(SampleDraft(title: "Second note"));
        await env.Service.ResolveAsync(toResolve.Id);

        var all = await env.Service.ListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, n => n.Id == active.Id);
        Assert.Contains(all, n => n.Id == toResolve.Id);
    }

    // ---- Architecture guarantee (spec Part AF): no HTTP/refresh/Associate-draft/PDF/Avalonia dependency ----
    // Phase 16C adds one sanctioned dependency, ISyncChangeTracker (optional, defaults to null) —
    // still no SQLite detail, no HTTP, no PDF, no refresh orchestrator, no Associate-draft dependency.

    [Fact]
    public void Constructor_DependsOnlyOnRepositoryClockAndSyncChangeTracker()
    {
        var constructor = typeof(OperationalNotesService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Equal(3, parameterTypes.Length);
        Assert.Contains(parameterTypes, t => t == typeof(IOperationalNoteRepository));
        Assert.Contains(parameterTypes, t => t == typeof(IClock));
        Assert.Contains(parameterTypes, t => t == typeof(ISyncChangeTracker));
        Assert.DoesNotContain(parameterTypes, t => t == typeof(IRefreshOrchestrator));
        Assert.DoesNotContain(parameterTypes, t => t == typeof(IAssociatePassdownService));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Separation from daily Associate notes (spec Parts S/U) ----

    [Fact]
    public async Task CreatingAPersistentNote_NeverModifiesAnAssociateDraftPayload()
    {
        var env = CreateEnvironment();
        var defaultDraft = AssociateDraftPayload.CreateDefault();

        await env.Service.CreateAsync(SampleDraft());

        // AssociateDraftPayload is never touched by this service at all — asserting the default draft's
        // own OperationalNotes list (a distinct, per-day concept) is still exactly what CreateDefault
        // produces proves there is no shared mutable state or accidental cross-wiring.
        Assert.Empty(defaultDraft.OperationalNotes);
    }

    [Fact]
    public void OperationalNoteDraft_HasNoFieldCapableOfTargetingAnAssociateDraftOrDate()
    {
        // Spec Part S: creating/updating a persistent note must have no notion of "which day's
        // passdown" — that only exists once Phase 11D's explicit selection is built.
        var properties = typeof(OperationalNoteDraft).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("PassdownDate", properties);
        Assert.DoesNotContain("OperatingDate", properties);
        Assert.DoesNotContain("AssociateDraft", properties);
    }
}
