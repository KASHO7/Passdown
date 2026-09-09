using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The sync-wire shape of one immutable <see cref="FinalizedPassdown"/> (Phase 16E §4) — every field
/// required to reconstruct the exact frozen local row: <see cref="SnapshotJson"/> is the already-frozen,
/// canonical <c>FinalizedPassdownSnapshotSerializer</c> text reused byte-for-byte (never re-derived from
/// current draft/catalog/notes data), <see cref="ContentHash"/> is the same
/// <c>FinalizedPassdownSnapshotSerializer.ComputeContentHash</c> value already stored locally (Phase 16A
/// §B/16E §7 audit: it covers the snapshot JSON only, not the PDF), and <see cref="PdfSha256"/>/
/// <see cref="PdfSizeBytes"/> are the existing PDF-integrity columns Phase 9C already computes — reused
/// here as the sync layer's PDF integrity check (Phase 16E §7) rather than inventing a second hash
/// scheme. The frozen PDF bytes themselves are never embedded in this payload (Phase 16A §D/16E §5) —
/// they travel as the sibling <c>.pdf</c> file at the same sync path.
/// </summary>
public sealed record FinalizedPassdownSyncPayload(
    Guid Id,
    DateOnly PassdownDate,
    DateTimeOffset FinalizedAtUtc,
    int SnapshotSchemaVersion,
    string SnapshotJson,
    string ContentHash,
    FinalizationOrigin Origin,
    string PdfSha256,
    long PdfSizeBytes)
{
    /// <summary>First sync-payload shape for finalized passdowns — independent of <see cref="Finalization.FinalizedPassdownSnapshot.SchemaVersion"/> and of the envelope's own <see cref="SyncEnvelope.SyncSchemaVersion"/>.</summary>
    public const int CurrentSchemaVersion = 1;

    public static FinalizedPassdownSyncPayload FromDomain(FinalizedPassdown record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.HasFrozenPdf)
        {
            // Callers must never export a legacy pre-9C row with no frozen PDF (Phase 16E §4: "do not
            // regenerate its frozen PDF from current draft/catalog data") — ExportHistoryAsync guards
            // this before calling FromDomain; this is a defensive invariant check, not expected reach.
            throw new InvalidOperationException($"Cannot build a sync payload for finalized passdown {record.Id} — it has no frozen PDF.");
        }

        return new FinalizedPassdownSyncPayload(
            record.Id, record.PassdownDate, record.FinalizedAtUtc, record.SnapshotSchemaVersion, record.SnapshotJson,
            record.ContentHash, record.Origin, record.PdfSha256!, record.PdfSizeBytes!.Value);
    }

    /// <summary><paramref name="pdfBytes"/> is the already-validated frozen PDF read from the sync folder's companion file — never regenerated (Phase 16E §11).</summary>
    public FinalizedPassdown ToDomain(byte[] pdfBytes) => new(
        Id, PassdownDate, FinalizedAtUtc, SnapshotSchemaVersion, SnapshotJson, ContentHash, Origin,
        pdfBytes, PdfSha256, PdfSizeBytes);
}

/// <summary>Canonical JSON for <see cref="FinalizedPassdownSyncPayload"/> — used for the exported envelope payload; never for content fingerprinting (History dedup/conflict decisions use the already-authoritative <see cref="FinalizedPassdown.ContentHash"/> directly, Phase 16E §7, not a fresh hash of this wrapper).</summary>
public static class FinalizedPassdownSyncPayloadSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(FinalizedPassdownSyncPayload payload) => JsonSerializer.Serialize(payload, Options);

    public static FinalizedPassdownSyncPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<FinalizedPassdownSyncPayload>(json, Options)
            ?? throw new InvalidOperationException("Finalized passdown sync payload JSON deserialized to null.");

    public static FinalizedPassdownSyncPayload Deserialize(JsonElement payload) =>
        payload.Deserialize<FinalizedPassdownSyncPayload>(Options)
            ?? throw new InvalidOperationException("Finalized passdown sync payload JSON deserialized to null.");
}
