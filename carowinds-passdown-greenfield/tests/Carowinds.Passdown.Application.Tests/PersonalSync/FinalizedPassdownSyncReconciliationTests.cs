using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>
/// Phase 16E §12-§14: the pure decision function behind every finalized-passdown import — the simpler,
/// insert-only/content-hash-dedup conflict model, verified without any filesystem or database.
/// </summary>
public class FinalizedPassdownSyncReconciliationTests
{
    private static readonly Guid LocalDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RemoteDeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Decide_SelfAuthoredEnvelope_IsIgnored()
    {
        var decision = FinalizedPassdownSyncReconciliation.Decide(
            LocalDeviceId, LocalDeviceId, remoteContentHash: "hash-a", localContentHashForSameId: null, localRecordExistsForSameDate: true);

        Assert.Equal(FinalizedPassdownSyncDecision.Ignored, decision);
    }

    [Fact]
    public void Decide_NoLocalRecordAtAllForThisIdOrDate_IsRemoteOnlyImport()
    {
        var decision = FinalizedPassdownSyncReconciliation.Decide(
            LocalDeviceId, RemoteDeviceId, remoteContentHash: "hash-a", localContentHashForSameId: null, localRecordExistsForSameDate: false);

        Assert.Equal(FinalizedPassdownSyncDecision.RemoteOnlyImport, decision);
    }

    [Fact]
    public void Decide_SameIdSameContentHash_Converges()
    {
        var decision = FinalizedPassdownSyncReconciliation.Decide(
            LocalDeviceId, RemoteDeviceId, remoteContentHash: "hash-a", localContentHashForSameId: "hash-a", localRecordExistsForSameDate: false);

        Assert.Equal(FinalizedPassdownSyncDecision.Converged, decision);
    }

    [Fact]
    public void Decide_SameIdDifferentContentHash_IsImmutableConflict()
    {
        // Phase 16E §13: two devices cannot legitimately mint the same Guid for different content —
        // this is a serious integrity condition, never auto-resolved.
        var decision = FinalizedPassdownSyncReconciliation.Decide(
            LocalDeviceId, RemoteDeviceId, remoteContentHash: "hash-remote", localContentHashForSameId: "hash-local", localRecordExistsForSameDate: false);

        Assert.Equal(FinalizedPassdownSyncDecision.ImmutableConflict, decision);
    }

    [Fact]
    public void Decide_NoLocalRecordWithThisId_ButLocalRecordExistsForSameDateUnderDifferentId_IsDualFinalizationConflict()
    {
        // Phase 16E §14: Device A finalizes Sep 9, Device B independently finalizes Sep 9 with a
        // different Guid/content — never silently keep both without surfacing, never silently pick one.
        var decision = FinalizedPassdownSyncReconciliation.Decide(
            LocalDeviceId, RemoteDeviceId, remoteContentHash: "hash-remote", localContentHashForSameId: null, localRecordExistsForSameDate: true);

        Assert.Equal(FinalizedPassdownSyncDecision.DualFinalizationConflict, decision);
    }
}
