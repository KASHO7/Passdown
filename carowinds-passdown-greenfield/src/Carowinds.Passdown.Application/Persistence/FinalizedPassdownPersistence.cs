using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// One immutable finalized Associate Passdown, keyed by <see cref="PassdownDate"/> (spec Phase 9
/// Parts A/B). Distinct from <see cref="AssociateDraft"/>: a draft is the editable working state for
/// a date; a <see cref="FinalizedPassdown"/> is the historical record of exactly what was finalized
/// for it, and never changes after <see cref="IFinalizedPassdownRepository.AddAsync"/> returns.
/// <see cref="SnapshotJson"/> is opaque to persistence, exactly like <see cref="AssociateDraft.PayloadJson"/>
/// — <see cref="FinalizedPassdownSnapshotSerializer"/> is the only code that knows its shape,
/// gated by <see cref="SnapshotSchemaVersion"/>. <see cref="Id"/> is a surrogate key independent of
/// <see cref="PassdownDate"/> so a future revision design (explicitly not built yet — spec Part B)
/// would not require redefining this table's identity.
/// </summary>
/// <param name="PdfBytes">
/// The exact frozen PDF bytes produced at finalization time (spec Phase 9C Parts A/E/M), derived from
/// the same canonical document-model build as <see cref="SnapshotJson"/> — never regenerated later.
/// Null only for a legacy pre-9C row finalized before this column existed (spec Part J); every row
/// created by <see cref="Finalization.FinalizedPassdownFinalizationService"/> from Phase 9C onward
/// always populates it. The repository is responsible for never letting a caller's later mutation of
/// this array reach a previously-stored row: <c>GetByDateAsync</c> always materializes a fresh array
/// from SQLite rather than returning any cached/shared buffer.
/// </param>
/// <param name="PdfSha256">Lowercase-hex SHA-256 of <see cref="PdfBytes"/> (spec Part C) — integrity/diagnostic metadata only, never security/authentication. Null exactly when <see cref="PdfBytes"/> is null.</param>
/// <param name="PdfSizeBytes">Byte length of <see cref="PdfBytes"/> (spec Part C), stored alongside it for diagnostics/history display without re-measuring the blob. Null exactly when <see cref="PdfBytes"/> is null.</param>
public sealed record FinalizedPassdown(
    Guid Id,
    DateOnly PassdownDate,
    DateTimeOffset FinalizedAtUtc,
    int SnapshotSchemaVersion,
    string SnapshotJson,
    string ContentHash,
    FinalizationOrigin Origin,
    byte[]? PdfBytes,
    string? PdfSha256,
    long? PdfSizeBytes)
{
    /// <summary>True for a complete post-9C row with an exact frozen PDF; false for a legacy pre-9C row (spec Part J) — never backfilled by regenerating a PDF under current styling, since that would not be the exact historical PDF.</summary>
    public bool HasFrozenPdf => PdfBytes is not null;
}

/// <summary>Lightweight metadata for one finalized passdown (spec Phase 9 Part T; PDF fields added Phase 9C Part L) — enough for a future History UI list without deserializing every row's <see cref="FinalizedPassdown.SnapshotJson"/> or loading its <see cref="FinalizedPassdown.PdfBytes"/> blob.</summary>
public sealed record FinalizedPassdownSummary(
    DateOnly PassdownDate,
    DateTimeOffset FinalizedAtUtc,
    FinalizationOrigin Origin,
    bool HasFrozenPdf,
    long? PdfSizeBytes);

/// <summary>
/// The identity/dirty-check facts Personal Sync's export loop needs to enumerate every local finalized
/// passdown (Phase 16E §4/§23) without loading every row's <see cref="FinalizedPassdown.SnapshotJson"/>
/// or <see cref="FinalizedPassdown.PdfBytes"/> blob — mirrors <see cref="FinalizedPassdownSummary"/>'s
/// own "cheap listing" rationale, but keyed by <see cref="Id"/> (the sync entity id, Phase 16A §B) and
/// carrying <see cref="ContentHash"/> (the one fact export dirtiness is decided from) rather than
/// display-oriented fields.
/// </summary>
public sealed record FinalizedPassdownSyncMetadata(Guid Id, DateOnly PassdownDate, string ContentHash);

/// <summary>
/// Narrow, insert-only persistence contract for finalized passdowns (spec Phase 9 Part M). Exposes
/// no update or delete — the model is immutable by convention; correcting a bad historical row, if
/// ever necessary, is a support/database concern, not a normal application operation.
/// </summary>
public interface IFinalizedPassdownRepository
{
    Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up by the surrogate <see cref="FinalizedPassdown.Id"/> rather than
    /// <see cref="FinalizedPassdown.PassdownDate"/> — needed only by Personal Sync's History import
    /// path (Phase 16E §2/§11), which must tell "a local record with this exact remote id already
    /// exists" apart from "a local record exists for this date under a different id" (the
    /// dual-finalization case, Phase 16E §14).
    /// </summary>
    Task<FinalizedPassdown?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new finalized passdown. Throws if <paramref name="finalizedPassdown"/>'s <see cref="FinalizedPassdown.PassdownDate"/> is already finalized (spec Part B's one-record-per-date uniqueness) — callers should check <see cref="ExistsForDateAsync"/> first when idempotent no-op behavior is wanted.</summary>
    Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default);

    /// <summary>All finalized passdowns, most recent <see cref="FinalizedPassdown.PassdownDate"/> first (spec Part T's future History UI listing).</summary>
    Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Every finalized passdown's sync-relevant identity/hash (Phase 16E §4/§23), in no particular guaranteed order — the cheap enumeration Personal Sync's export loop walks each pass, including on first-ever bootstrap (Phase 16E §10).</summary>
    Task<IReadOnlyList<FinalizedPassdownSyncMetadata>> ListForSyncAsync(CancellationToken cancellationToken = default);
}
