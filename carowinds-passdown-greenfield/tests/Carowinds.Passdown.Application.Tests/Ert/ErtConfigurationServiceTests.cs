using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Tests.Ert;

/// <summary>Covers <see cref="ErtConfigurationService"/> — validation/normalization (spec Part 33) and sync-change-tracking (spec Part 21).</summary>
public class ErtConfigurationServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed record Environment(
        ErtConfigurationService Service,
        InMemoryMonthlyErtRepository MonthlyRepository,
        InMemoryErtDateOverrideRepository OverrideRepository,
        RecordingSyncChangeTracker SyncChangeTracker);

    private sealed class RecordingSyncChangeTracker : ISyncChangeTracker
    {
        public List<(SyncEntityType EntityType, string EntityId)> Calls { get; } = [];

        public Task MarkLocalChangeAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
        {
            Calls.Add((entityType, entityId));
            return Task.CompletedTask;
        }
    }

    private static Environment CreateEnvironment()
    {
        var monthlyRepository = new InMemoryMonthlyErtRepository();
        var overrideRepository = new InMemoryErtDateOverrideRepository();
        var syncChangeTracker = new RecordingSyncChangeTracker();
        var service = new ErtConfigurationService(monthlyRepository, overrideRepository, new FakeClock(FixedNow), syncChangeTracker);
        return new Environment(service, monthlyRepository, overrideRepository, syncChangeTracker);
    }

    // ===================== Monthly =====================

    [Fact]
    public async Task SaveMonthlyAsync_Configured_PersistsAndTracksChange()
    {
        var env = CreateEnvironment();

        var saved = await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], "Note."));

        Assert.Equal(new TimeOnly(9, 30), saved.StartTime);
        Assert.Equal(["Fury 325", "Copperhead Strike"], saved.Rides);
        Assert.Equal(FixedNow, saved.ModifiedUtc);

        var reloaded = await env.Service.GetMonthlyAsync(2026, 8);
        Assert.Equal(saved, reloaded);

        var call = Assert.Single(env.SyncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.MonthlyErt, call.EntityType);
        Assert.Equal("2026-08", call.EntityId);
    }

    // Spec Part 33: NoErt always clears start time/rides, even if a caller supplied both.
    [Fact]
    public async Task SaveMonthlyAsync_NoErt_NormalizesStartTimeAndRidesToEmpty()
    {
        var env = CreateEnvironment();

        var saved = await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.NoErt, new TimeOnly(9, 30), ["Fury 325"], null));

        Assert.Equal(ErtConfigurationState.NoErt, saved.State);
        Assert.Null(saved.StartTime);
        Assert.Empty(saved.Rides);
    }

    // Phase 17D Part 1: a routine ERT start time is no longer required — associates configure rides
    // only. StartTime remains a nullable, never-required field for backward compatibility.
    [Fact]
    public async Task SaveMonthlyAsync_ConfiguredWithoutStartTime_Succeeds()
    {
        var env = CreateEnvironment();

        var saved = await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.Configured, null, ["Fury 325"], null));

        Assert.Equal(ErtConfigurationState.Configured, saved.State);
        Assert.Null(saved.StartTime);
        Assert.Equal(["Fury 325"], saved.Rides);
    }

    // Phase 17D compatibility: a caller that still supplies a start time (e.g. a pre-17D remote sync
    // payload) has it preserved rather than rejected or silently stripped.
    [Fact]
    public async Task SaveMonthlyAsync_ConfiguredWithLegacyStartTime_IsPreservedButNotRequired()
    {
        var env = CreateEnvironment();

        var saved = await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null));

        Assert.Equal(new TimeOnly(9, 30), saved.StartTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task SaveMonthlyAsync_InvalidMonth_Throws(int month)
    {
        var env = CreateEnvironment();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, month, ErtConfigurationState.Configured, new TimeOnly(9, 0), [], null)));
    }

    // Spec Part 33: duplicate rides collapse (case-insensitive), blank entries dropped.
    [Fact]
    public async Task SaveMonthlyAsync_DuplicateAndBlankRides_NormalizesToDistinctTrimmedList()
    {
        var env = CreateEnvironment();

        var saved = await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(
            2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 0),
            ["Fury 325", " fury 325 ", "", "  ", "Copperhead Strike"], null));

        Assert.Equal(["Fury 325", "Copperhead Strike"], saved.Rides);
    }

    // Spec Part 33: an empty ride list is allowed — a start time alone is still meaningful.
    [Fact]
    public async Task SaveMonthlyAsync_EmptyRideList_IsAllowed()
    {
        var env = CreateEnvironment();

        var saved = await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 0), [], null));

        Assert.Empty(saved.Rides);
    }

    [Fact]
    public async Task GetMonthlyAsync_NoConfiguration_ReturnsNull()
    {
        var env = CreateEnvironment();

        Assert.Null(await env.Service.GetMonthlyAsync(2026, 8));
    }

    // Spec Part 5/30: year/month together are identity — same month number, different year, independent.
    [Fact]
    public async Task SaveMonthlyAsync_SameMonthDifferentYear_AreIndependentRecords()
    {
        var env = CreateEnvironment();
        await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 0), ["A"], null));
        await env.Service.SaveMonthlyAsync(new MonthlyErtDraft(2027, 8, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["B"], null));

        var year2026 = await env.Service.GetMonthlyAsync(2026, 8);
        var year2027 = await env.Service.GetMonthlyAsync(2027, 8);

        Assert.Equal(new TimeOnly(9, 0), year2026!.StartTime);
        Assert.Equal(new TimeOnly(10, 0), year2027!.StartTime);
    }

    // ===================== Override =====================

    [Fact]
    public async Task SaveOverrideAsync_Configured_PersistsAndTracksChange()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 19);

        var saved = await env.Service.SaveOverrideAsync(new ErtDateOverrideDraft(date, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance"));

        Assert.Equal(new TimeOnly(10, 0), saved.StartTime);
        Assert.Equal("Fury maintenance", saved.Reason);

        var call = Assert.Single(env.SyncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.ErtDateOverride, call.EntityType);
        Assert.Equal("2026-08-19", call.EntityId);
    }

    // Spec Part 38.I: an override may be saved even when no monthly configuration exists.
    [Fact]
    public async Task SaveOverrideAsync_WithoutMonthlyConfiguration_Succeeds()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 19);

        var saved = await env.Service.SaveOverrideAsync(new ErtDateOverrideDraft(date, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], null));

        Assert.Null(await env.Service.GetMonthlyAsync(2026, 8));
        Assert.Equal(date, saved.OperatingDate);
    }

    [Fact]
    public async Task SaveOverrideAsync_NoErt_NormalizesStartTimeAndRidesToEmpty()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 19);

        var saved = await env.Service.SaveOverrideAsync(new ErtDateOverrideDraft(date, ErtConfigurationState.NoErt, new TimeOnly(10, 0), ["X"], "Rain"));

        Assert.Null(saved.StartTime);
        Assert.Empty(saved.Rides);
        Assert.Equal("Rain", saved.Reason); // reason/note is preserved even for NoErt.
    }

    // Spec Part 8/28: removing an override falls back to the monthly default and tracks the change (tombstone export relies on this).
    [Fact]
    public async Task RemoveOverrideAsync_ExistingOverride_RemovesAndTracksChange()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 19);
        await env.Service.SaveOverrideAsync(new ErtDateOverrideDraft(date, ErtConfigurationState.Configured, new TimeOnly(10, 0), [], null));
        env.SyncChangeTracker.Calls.Clear();

        await env.Service.RemoveOverrideAsync(date);

        Assert.Null(await env.Service.GetOverrideAsync(date));
        var call = Assert.Single(env.SyncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.ErtDateOverride, call.EntityType);
    }

    [Fact]
    public async Task RemoveOverrideAsync_NoExistingOverride_IsANoOp()
    {
        var env = CreateEnvironment();

        await env.Service.RemoveOverrideAsync(new DateOnly(2026, 8, 19));

        Assert.Empty(env.SyncChangeTracker.Calls);
    }
}
