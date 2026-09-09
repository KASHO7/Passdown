using System.Text.Json;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>
/// Phase 16C §10-§13/§31: the pure decision function behind every import — every conflict/idempotence
/// scenario the phase spec calls out (§30/§31 A-G), verified without any filesystem or database.
/// </summary>
public class SyncReconciliationTests
{
    private static readonly Guid LocalDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RemoteDeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement;
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

    private static SyncEnvelope Envelope(Guid deviceId, int revision) => new()
    {
        SyncSchemaVersion = 1,
        EntityType = SyncEntityType.AssociateDraft,
        EntityId = "2026-09-09",
        DeviceId = deviceId,
        Revision = revision,
        ModifiedUtc = Now,
        IsDeleted = false,
        PayloadSchemaVersion = 3,
        Payload = EmptyPayload,
    };

    [Fact]
    public void Decide_SelfAuthoredEnvelope_IsIgnored()
    {
        var envelope = Envelope(LocalDeviceId, revision: 5);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "hashA", state: null, remoteContentHash: "hashB");

        Assert.Equal(SyncImportDecision.Ignored, decision);
    }

    [Fact]
    public void Decide_NoLocalEntity_IsRemoteOnlyImport()
    {
        var envelope = Envelope(RemoteDeviceId, revision: 1);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: false, localContentHash: null, state: null, remoteContentHash: "hashA");

        Assert.Equal(SyncImportDecision.RemoteOnlyImport, decision);
    }

    [Fact]
    public void Decide_LocalAndRemoteIdentical_FirstEverContact_Converges()
    {
        // Bootstrap Case B (§17): both devices independently created the exact same content before ever syncing.
        var envelope = Envelope(RemoteDeviceId, revision: 1);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "sameHash", state: null, remoteContentHash: "sameHash");

        Assert.Equal(SyncImportDecision.Converged, decision);
    }

    [Fact]
    public void Decide_LocalAndRemoteDiffer_NoSharedBaseline_IsConflict()
    {
        // Bootstrap Case B, divergent content: neither side may be silently overwritten (§12/§17).
        var envelope = Envelope(RemoteDeviceId, revision: 1);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "localHash", state: null, remoteContentHash: "remoteHash");

        Assert.Equal(SyncImportDecision.Conflict, decision);
    }

    [Fact]
    public void Decide_LocalUnchangedSinceLastSync_RemoteAdvanced_IsSafeRemoteUpdate()
    {
        // §12's worked example: Device B imported revision 4, no local edits, A now produces revision 5.
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now) with
        {
            LocalRevision = 4,
            SyncedRevision = 4,
            SyncedContentHash = "baseHash",
            LastRemoteDeviceId = RemoteDeviceId,
            LastRemoteRevision = 4,
        };
        var envelope = Envelope(RemoteDeviceId, revision: 5);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "baseHash", state, remoteContentHash: "newRemoteHash");

        Assert.Equal(SyncImportDecision.SafeRemoteUpdate, decision);
    }

    [Fact]
    public void Decide_LocalDivergedSinceLastSync_RemoteAlsoAdvanced_IsConflict()
    {
        // §12's worked example: Device B imported revision 4, then edited locally; A also produces revision 5 — conflict.
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now) with
        {
            LocalRevision = 5,
            SyncedRevision = 4,
            SyncedContentHash = "baseHash",
            LastRemoteDeviceId = RemoteDeviceId,
            LastRemoteRevision = 4,
        };
        var envelope = Envelope(RemoteDeviceId, revision: 5);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "locallyEditedHash", state, remoteContentHash: "newRemoteHash");

        Assert.Equal(SyncImportDecision.Conflict, decision);
    }

    [Fact]
    public void Decide_LocalDivergedButRemoteMatchesLocal_Converges()
    {
        // Local edited such that it now happens to byte-match remote — convergence, not a conflict (§12/§13).
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now) with
        {
            LocalRevision = 5,
            SyncedRevision = 4,
            SyncedContentHash = "baseHash",
            LastRemoteDeviceId = RemoteDeviceId,
            LastRemoteRevision = 4,
        };
        var envelope = Envelope(RemoteDeviceId, revision: 5);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "sameAsRemote", state, remoteContentHash: "sameAsRemote");

        Assert.Equal(SyncImportDecision.Converged, decision);
    }

    [Fact]
    public void Decide_SameOrOlderRevisionAlreadyIncorporatedFromSameDevice_IsIgnored()
    {
        // Idempotence (§15/§30): re-scanning the same file twice, or an older file appearing after a newer one was already applied, must not re-process it.
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now) with
        {
            LocalRevision = 5,
            SyncedRevision = 5,
            SyncedContentHash = "currentHash",
            LastRemoteDeviceId = RemoteDeviceId,
            LastRemoteRevision = 5,
        };
        var sameRevision = Envelope(RemoteDeviceId, revision: 5);
        var olderRevision = Envelope(RemoteDeviceId, revision: 4);

        Assert.Equal(SyncImportDecision.Ignored, SyncReconciliation.Decide(LocalDeviceId, sameRevision, true, "currentHash", state, "currentHash"));
        Assert.Equal(SyncImportDecision.Ignored, SyncReconciliation.Decide(LocalDeviceId, olderRevision, true, "currentHash", state, "someOldHash"));
    }

    [Fact]
    public void Decide_NewRevisionFromADifferentRemoteDevice_IsNotShortCircuited_AndFallsBackToContentComparison()
    {
        // A third device's envelope must still be compared on content, even though state only tracks "the last remote device" (16C's 2-device scope; §12).
        var otherRemoteDeviceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now) with
        {
            LocalRevision = 4,
            SyncedRevision = 4,
            SyncedContentHash = "baseHash",
            LastRemoteDeviceId = RemoteDeviceId,
            LastRemoteRevision = 4,
        };
        var envelope = Envelope(otherRemoteDeviceId, revision: 1);

        var decision = SyncReconciliation.Decide(LocalDeviceId, envelope, localEntityExists: true, "baseHash", state, remoteContentHash: "thirdDeviceHash");

        Assert.Equal(SyncImportDecision.SafeRemoteUpdate, decision);
    }
}
