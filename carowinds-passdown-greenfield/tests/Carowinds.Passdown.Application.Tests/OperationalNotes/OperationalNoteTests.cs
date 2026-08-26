using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Application.Tests.OperationalNotes;

/// <summary>
/// Covers the pure, deterministic domain rules for persistent Operational Notes (spec Phase 11A Parts
/// J/W/X/Y): relevance-for-date, derived lifecycle state, and list ordering. None of these depend on
/// persistence, <see cref="Time.IClock"/>, or any other side effect.
/// </summary>
public class OperationalNoteTests
{
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static OperationalNote Sample(
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveThrough = null,
        OperationalNoteStatus status = OperationalNoteStatus.Active,
        OperationalNotePriority priority = OperationalNotePriority.Normal,
        DateTimeOffset? createdAtUtc = null) => new(
        Guid.NewGuid(),
        "South Gate scanner #3 out of service",
        "IT ticket submitted. Use scanner #2 until repaired.",
        OperationalNoteCategory.Equipment,
        priority,
        effectiveFrom ?? Today,
        effectiveThrough,
        status,
        createdAtUtc ?? new DateTimeOffset(2026, 8, 26, 6, 0, 0, TimeSpan.Zero),
        UpdatedAtUtc: createdAtUtc ?? new DateTimeOffset(2026, 8, 26, 6, 0, 0, TimeSpan.Zero),
        ResolvedAtUtc: status == OperationalNoteStatus.Resolved ? new DateTimeOffset(2026, 8, 26, 7, 0, 0, TimeSpan.Zero) : null);

    // ---- IsRelevantForDate (Part J) ----

    [Fact]
    public void IsRelevantForDate_ActiveWithinOpenEndedRange_IsRelevant()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: null);

        Assert.True(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    [Fact]
    public void IsRelevantForDate_ActiveWithinClosedRange_IsRelevant()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: Today.AddDays(1));

        Assert.True(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    [Fact]
    public void IsRelevantForDate_BeforeEffectiveFrom_IsNotRelevant()
    {
        var note = Sample(effectiveFrom: Today.AddDays(1));

        Assert.False(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    [Fact]
    public void IsRelevantForDate_AfterEffectiveThrough_IsNotRelevant()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-5), effectiveThrough: Today.AddDays(-1));

        Assert.False(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    [Fact]
    public void IsRelevantForDate_OnExactEffectiveFromBoundary_IsRelevant()
    {
        var note = Sample(effectiveFrom: Today);

        Assert.True(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    [Fact]
    public void IsRelevantForDate_OnExactEffectiveThroughBoundary_IsRelevant()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: Today);

        Assert.True(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    [Fact]
    public void IsRelevantForDate_ResolvedNote_IsNeverRelevant()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: null, status: OperationalNoteStatus.Resolved);

        Assert.False(OperationalNoteRelevance.IsRelevantForDate(note, Today));
    }

    // ---- Derived lifecycle state (Part Y) ----

    [Fact]
    public void Lifecycle_ResolvedStatus_IsAlwaysResolvedRegardlessOfDates()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(10), status: OperationalNoteStatus.Resolved);

        Assert.Equal(OperationalNoteLifecycleState.Resolved, OperationalNoteLifecycle.Derive(note, Today));
    }

    [Fact]
    public void Lifecycle_BeforeEffectiveFrom_IsUpcoming()
    {
        var note = Sample(effectiveFrom: Today.AddDays(3));

        Assert.Equal(OperationalNoteLifecycleState.Upcoming, OperationalNoteLifecycle.Derive(note, Today));
    }

    [Fact]
    public void Lifecycle_WithinRange_IsActive()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: Today.AddDays(1));

        Assert.Equal(OperationalNoteLifecycleState.Active, OperationalNoteLifecycle.Derive(note, Today));
    }

    [Fact]
    public void Lifecycle_OpenEndedAndStarted_IsActive()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-1), effectiveThrough: null);

        Assert.Equal(OperationalNoteLifecycleState.Active, OperationalNoteLifecycle.Derive(note, Today));
    }

    [Fact]
    public void Lifecycle_PastEffectiveThrough_IsExpired_ButStatusStaysActive()
    {
        var note = Sample(effectiveFrom: Today.AddDays(-10), effectiveThrough: Today.AddDays(-1));

        Assert.Equal(OperationalNoteLifecycleState.Expired, OperationalNoteLifecycle.Derive(note, Today));
        Assert.Equal(OperationalNoteStatus.Active, note.Status);
    }

    // ---- Ordering (Part W) ----

    [Fact]
    public void ByDefaultListOrder_ActiveBeforeResolved_ThenImportantBeforeNormal_ThenNewestCreatedFirst()
    {
        var resolvedImportant = Sample(status: OperationalNoteStatus.Resolved, priority: OperationalNotePriority.Important, createdAtUtc: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));
        var activeNormalOlder = Sample(priority: OperationalNotePriority.Normal, createdAtUtc: new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        var activeNormalNewer = Sample(priority: OperationalNotePriority.Normal, createdAtUtc: new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));
        var activeImportant = Sample(priority: OperationalNotePriority.Important, createdAtUtc: new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

        var ordered = OperationalNoteOrdering.ByDefaultListOrder([resolvedImportant, activeNormalOlder, activeNormalNewer, activeImportant]);

        Assert.Equal([activeImportant, activeNormalNewer, activeNormalOlder, resolvedImportant], ordered);
    }

    [Fact]
    public void ByRelevance_ImportantFirst_ThenEffectiveFromAscending_ThenCreatedAtAscending()
    {
        var normalEarlier = Sample(effectiveFrom: Today.AddDays(-3), priority: OperationalNotePriority.Normal, createdAtUtc: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));
        var importantLater = Sample(effectiveFrom: Today.AddDays(-1), priority: OperationalNotePriority.Important, createdAtUtc: new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        var importantEarliest = Sample(effectiveFrom: Today.AddDays(-5), priority: OperationalNotePriority.Important, createdAtUtc: new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));

        var ordered = OperationalNoteOrdering.ByRelevance([normalEarlier, importantLater, importantEarliest]);

        Assert.Equal([importantEarliest, importantLater, normalEarlier], ordered);
    }

    [Fact]
    public void ByRelevance_TieOnEverything_BreaksByIdDeterministically()
    {
        var sharedCreatedAt = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var a = Sample(createdAtUtc: sharedCreatedAt);
        var b = Sample(createdAtUtc: sharedCreatedAt);
        var expected = new[] { a, b }.OrderBy(n => n.Id).ToList();

        var ordered = OperationalNoteOrdering.ByRelevance([b, a]);

        Assert.Equal(expected, ordered);
    }
}
