using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// Phase 26: an official price observation merged in through Personal Sync
/// (<c>synced_price_observations</c>). <see cref="ImportedAtUtc"/> is only when this computer first
/// stored it — never shown as, or confused with, the observation's own official timestamps.
/// </summary>
public sealed record SyncedPriceObservation(OfficialPriceObservation Observation, DateTimeOffset ImportedAtUtc);

/// <summary>What one batched merge did. Counts only — never prices or product names.</summary>
/// <param name="Added">Observations this computer did not have before.</param>
/// <param name="Extended">Known observations whose contributor has since confirmed the same price again (last-confirmed moved forward).</param>
/// <param name="Unchanged">Known observations with nothing new.</param>
/// <param name="Conflicts">Same identity, different immutable content — the stored record is kept, the incoming one is not written.</param>
public sealed record SyncedPriceObservationMergeResult(int Added, int Extended, int Unchanged, int Conflicts)
{
    public static SyncedPriceObservationMergeResult Empty { get; } = new(0, 0, 0, 0);

    public bool ChangedHistory => Added > 0 || Extended > 0;
}

/// <summary>
/// Phase 26: storage for imported official price observations. Merge-only: there is no delete, no
/// replace and no way to move a timestamp backward — the absence of a record on another computer is
/// never a deletion. Local <c>season_price_history</c> rows are never touched by this repository.
/// </summary>
public interface ISyncedPriceObservationRepository
{
    /// <summary>
    /// Merges <paramref name="observations"/> (already validated) in one transaction: inserts new
    /// identities, moves a known identity's last-confirmed time forward (never backward), and leaves a
    /// conflicting identity's stored record untouched. A failure rolls the whole batch back.
    /// </summary>
    Task<SyncedPriceObservationMergeResult> MergeAsync(
        IReadOnlyList<OfficialPriceObservation> observations,
        DateTimeOffset importedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncedPriceObservation>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncedPriceObservation>> GetForProductAsync(string productKey, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
