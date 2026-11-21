using System.Text.Json;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 31B: ERT sync decisions compare ride selections by meaning, never by order or edit time.</summary>
public class Phase31BErtSemanticComparisonTests
{
    private static readonly Guid Local = Guid.Parse("aaaaaaaa-3131-0000-0000-000000000001");
    private static readonly Guid Remote = Guid.Parse("bbbbbbbb-3131-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static MonthlyErtSyncPayload Monthly(IReadOnlyList<string> rides, TimeOnly? start = null, string? note = null, DateTimeOffset? modified = null, ErtConfigurationState state = ErtConfigurationState.Configured) =>
        new(2026, 11, state, start ?? new TimeOnly(9, 0), rides, note, modified ?? Now);

    private static SyncEnvelope Envelope(MonthlyErtSyncPayload payload) => new()
    {
        SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
        EntityType = SyncEntityType.MonthlyErt,
        EntityId = SyncEntityIdentity.ForMonthlyErt(2026, 11),
        DeviceId = Remote,
        Revision = 1,
        ModifiedUtc = Now,
        IsDeleted = false,
        PayloadSchemaVersion = MonthlyErtSyncPayload.CurrentSchemaVersion,
        Payload = JsonSerializer.SerializeToElement(payload, MonthlyErtSyncPayloadSerializer.Options),
    };

    private static SyncImportDecision Decide(MonthlyErtSyncPayload local, MonthlyErtSyncPayload remote, SyncEntityState? state = null) =>
        SyncReconciliation.Decide(
            Local, Envelope(remote), localEntityExists: true,
            SyncContentHash.ComputeMonthlyErtHash(local), state, SyncContentHash.ComputeMonthlyErtHash(remote),
            SyncContentHash.ComputeMonthlyErtSemanticHash(local), SyncContentHash.ComputeMonthlyErtSemanticHash(remote));

    [Fact]
    public void SameRidesInADifferentOrder_OnFirstContact_AreConverged_NotAConflict()
    {
        Assert.Equal(SyncImportDecision.Converged,
            Decide(Monthly(["Copperhead Strike", "Mountain Gliders"]), Monthly(["Mountain Gliders", "Copperhead Strike"], modified: Now.AddHours(2))));
    }

    [Fact]
    public void SameRides_WhileThisDeviceHasALocalEdit_AreStillConverged()
    {
        var local = Monthly(["Copperhead Strike", "Mountain Gliders"]);
        var dirtyState = SyncEntityState.CreateNew(SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 11), Now) with
        {
            SyncedContentHash = SyncContentHash.ComputeMonthlyErtHash(Monthly(["Copperhead Strike"])),
        };

        Assert.Equal(SyncImportDecision.Converged, Decide(local, Monthly([" mountain gliders", "COPPERHEAD STRIKE", "Copperhead Strike"]), dirtyState));
    }

    [Fact]
    public void GenuinelyDifferentRides_StillConflict()
    {
        Assert.Equal(SyncImportDecision.Conflict, Decide(Monthly(["Copperhead Strike"]), Monthly(["Fury 325"])));
    }

    [Fact]
    public void WithoutSemanticHashes_TheOriginalByteComparisonIsUnchanged()
    {
        var local = Monthly(["Copperhead Strike", "Mountain Gliders"]);
        var remote = Monthly(["Mountain Gliders", "Copperhead Strike"]);

        var decision = SyncReconciliation.Decide(Local, Envelope(remote), true,
            SyncContentHash.ComputeMonthlyErtHash(local), null, SyncContentHash.ComputeMonthlyErtHash(remote));

        Assert.Equal(SyncImportDecision.Conflict, decision); // drafts and notes keep exact comparison
    }

    [Fact]
    public void SemanticHash_IgnoresOrderDuplicatesCaseWhitespaceAndEditTime()
    {
        var a = SyncContentHash.ComputeMonthlyErtSemanticHash(Monthly(["Copperhead Strike", "Mountain Gliders"], note: "Holiday  hours"));
        var b = SyncContentHash.ComputeMonthlyErtSemanticHash(Monthly(["mountain gliders ", "Copperhead  Strike", "copperhead strike"], note: " Holiday hours ", modified: Now.AddDays(1)));

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("note")]
    [InlineData("rides")]
    [InlineData("state")]
    public void SemanticHash_StillDistinguishesRealDifferences(string field)
    {
        var baseline = Monthly(["Copperhead Strike"], note: "Holiday hours");
        var changed = field switch
        {
            "start" => baseline with { StartTime = new TimeOnly(8, 30) },
            "note" => baseline with { Note = "Weather permitting" },
            "rides" => baseline with { Rides = ["Copperhead Strike", "Fury 325"] },
            _ => baseline with { State = ErtConfigurationState.NoErt, Rides = [] },
        };

        Assert.NotEqual(SyncContentHash.ComputeMonthlyErtSemanticHash(baseline), SyncContentHash.ComputeMonthlyErtSemanticHash(changed));
    }

    [Fact]
    public void TheStoredDirtyTrackingHash_IsUnchanged_SoExistingSyncStateStaysValid()
    {
        // Byte hashes still see the order (that is what preserves each device's own display order).
        Assert.NotEqual(
            SyncContentHash.ComputeMonthlyErtHash(Monthly(["Copperhead Strike", "Mountain Gliders"])),
            SyncContentHash.ComputeMonthlyErtHash(Monthly(["Mountain Gliders", "Copperhead Strike"])));
    }

    [Fact]
    public void DateOverrideSemanticHash_IsOrderIndependentToo()
    {
        var date = new DateOnly(2026, 11, 28);
        var a = new ErtDateOverrideSyncPayload(date, ErtConfigurationState.Configured, new TimeOnly(8, 30), ["Copperhead Strike", "Mountain Gliders"], "Black Friday", Now);
        var b = a with { Rides = ["Mountain Gliders", "Copperhead Strike"], ModifiedUtc = Now.AddHours(4) };

        Assert.Equal(SyncContentHash.ComputeErtDateOverrideSemanticHash(a), SyncContentHash.ComputeErtDateOverrideSemanticHash(b));
        Assert.NotEqual(SyncContentHash.ComputeErtDateOverrideSemanticHash(a), SyncContentHash.ComputeErtDateOverrideSemanticHash(a with { OperatingDate = date.AddDays(1) }));
    }

    [Fact]
    public void ErtConflictDetail_LabelsAnOrderOnlyRideDifference_AndSuggestsAMergedList()
    {
        var detail = new ErtConflictDetail(
            SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 11),
            new ErtSelectionSnapshot(ErtConfigurationState.Configured, new TimeOnly(9, 0), ["Copperhead Strike", "Mountain Gliders"], null),
            new ErtSelectionSnapshot(ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Mountain Gliders", "Copperhead Strike"], null),
            false, false, Remote, Now, false);

        Assert.False(detail.RidesDiffer);
        Assert.True(detail.OtherFieldsDiffer);
        Assert.Contains(detail.BuildFieldComparisons(), f => f.Label == "Rides (same rides, different order)");
        Assert.Equal(["Copperhead Strike", "Mountain Gliders"], detail.SuggestedMergedRides);
    }
}
