using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// Projects a persisted <see cref="DatedResultRecord"/> back into the Domain's
/// <see cref="LastKnownGood{TValue}"/> semantics (spec §6.7), so presentation code
/// gets <c>EffectiveStatus</c>/<c>IsStale</c>/<c>Age</c> without duplicating that
/// logic. Reconstruction here is exact and lossless — unlike the lifecycle
/// aggregates in Phase 2C, <see cref="LastKnownGood{TValue}"/> only ever holds at
/// most two evidence snapshots, and <see cref="LastKnownGood{TValue}.RecordAttempt"/>
/// replays them deterministically with no hidden state to reconstruct.
/// </summary>
public static class DatedResultProjectionExtensions
{
    public static LastKnownGood<string> ToLastKnownGood(this DatedResultRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var projection = LastKnownGood<string>.Empty;
        if (record.SuccessEvidence is { } successEvidence)
        {
            projection = projection.RecordAttempt(successEvidence, record.SuccessPayloadJson);
        }

        // A distinct latest attempt only exists when it differs from the success
        // evidence (value equality) — otherwise the most recent attempt *was* the
        // success already recorded above, and replaying it again would wrongly
        // overwrite SuccessValue with null.
        if (record.LastAttemptEvidence != record.SuccessEvidence)
        {
            projection = projection.RecordAttempt(record.LastAttemptEvidence, null);
        }

        return projection;
    }
}
