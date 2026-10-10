using System.Text.Json;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.History;

/// <summary>
/// One finalized passdown's read-only detail (spec Phase 9D Part S). <see cref="Snapshot"/> is the
/// typed, frozen content when it could be read; <see cref="UnavailableReason"/> explains why it
/// couldn't (an unsupported/future <see cref="FinalizedPassdown.SnapshotSchemaVersion"/>, or corrupt
/// <see cref="FinalizedPassdown.SnapshotJson"/>) without <see cref="IPassdownHistoryService.GetAsync"/>
/// ever throwing for one bad historical row (spec Part M/X) — <see cref="Summary"/> and PDF
/// availability stay visible either way, since they come from repository columns, not the JSON blob.
/// </summary>
public sealed record PassdownHistoryDetail(
    FinalizedPassdownSummary Summary,
    FinalizedPassdownSnapshot? Snapshot,
    string? UnavailableReason);

/// <summary>
/// The read-only Application-layer seam the Phase 9D History UI uses (spec Part R) — combines
/// <see cref="IFinalizedPassdownRepository"/>'s stored row with <see cref="FinalizedPassdownSnapshotSerializer"/>
/// to expose a clean, immutable read model. Never generates a PDF, never touches the current Associate
/// draft, and never re-resolves anything against the current product catalog — every value returned
/// here comes from an already-finalized row, exactly as stored (spec Part D).
/// </summary>
public interface IPassdownHistoryService
{
    /// <summary>Every finalized passdown's lightweight metadata, most recent <see cref="FinalizedPassdownSummary.PassdownDate"/> first (spec Part B) — a thin pass-through of <see cref="IFinalizedPassdownRepository.ListAsync"/>'s metadata-only query; never loads a PDF blob (spec Part L).</summary>
    Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The full read-only detail for one finalized passdown, or null when no finalized record exists for <paramref name="passdownDate"/>.</summary>
    Task<PassdownHistoryDetail?> GetAsync(DateOnly passdownDate, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPassdownHistoryService"/>
public sealed class PassdownHistoryService(IFinalizedPassdownRepository repository) : IPassdownHistoryService
{
    public Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public async Task<PassdownHistoryDetail?> GetAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
    {
        var record = await repository.GetByDateAsync(passdownDate, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var summary = new FinalizedPassdownSummary(record.PassdownDate, record.FinalizedAtUtc, record.Origin, record.HasFrozenPdf, record.PdfSizeBytes);

        // Spec Part M: a schema version newer than this build understands must never be guessed at —
        // the metadata row (and PDF, if frozen) must still be usable, only the structured detail is
        // unavailable.
        if (record.SnapshotSchemaVersion > FinalizedPassdownSnapshot.CurrentSchemaVersion)
        {
            return new PassdownHistoryDetail(summary, null, "This finalized passdown was created by a newer version of the app.");
        }

        try
        {
            var snapshot = FinalizedPassdownSnapshotSerializer.Deserialize(record.SnapshotJson);
            return new PassdownHistoryDetail(summary, snapshot, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Spec Part X: a malformed historical row must never crash the History screen — only its
            // own detail becomes unavailable.
            return new PassdownHistoryDetail(summary, null, "This finalized passdown's stored data could not be read.");
        }
    }
}
