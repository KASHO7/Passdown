using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 16E §4/§7: the finalized-passdown sync payload round-trips exactly, and never claims to wrap a record with no frozen PDF.</summary>
public class FinalizedPassdownSyncPayloadTests
{
    private static FinalizedPassdown CreateRecord(byte[]? pdfBytes = null) => new(
        Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
        new DateOnly(2026, 9, 9),
        new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.Zero),
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        """{"PassdownDate":"2026-09-09"}""",
        "abc123contenthash",
        FinalizationOrigin.Manual,
        pdfBytes,
        pdfBytes is null ? null : "abc123pdfhash",
        pdfBytes?.LongLength);

    [Fact]
    public void FromDomain_ThenToDomain_RoundTripsEveryField()
    {
        var record = CreateRecord([0x25, 0x50, 0x44, 0x46]);

        var payload = FinalizedPassdownSyncPayload.FromDomain(record);
        var rebuilt = payload.ToDomain(record.PdfBytes!);

        Assert.Equal(record.Id, rebuilt.Id);
        Assert.Equal(record.PassdownDate, rebuilt.PassdownDate);
        Assert.Equal(record.FinalizedAtUtc, rebuilt.FinalizedAtUtc);
        Assert.Equal(record.SnapshotSchemaVersion, rebuilt.SnapshotSchemaVersion);
        Assert.Equal(record.SnapshotJson, rebuilt.SnapshotJson);
        Assert.Equal(record.ContentHash, rebuilt.ContentHash);
        Assert.Equal(record.Origin, rebuilt.Origin);
        Assert.Equal(record.PdfSha256, rebuilt.PdfSha256);
        Assert.Equal(record.PdfSizeBytes, rebuilt.PdfSizeBytes);
        Assert.Equal(record.PdfBytes, rebuilt.PdfBytes);
    }

    [Fact]
    public void FromDomain_RecordWithNoFrozenPdf_Throws()
    {
        var record = CreateRecord(pdfBytes: null);

        Assert.Throws<InvalidOperationException>(() => FinalizedPassdownSyncPayload.FromDomain(record));
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsExactly()
    {
        var payload = FinalizedPassdownSyncPayload.FromDomain(CreateRecord([0x25, 0x50, 0x44, 0x46]));

        var json = FinalizedPassdownSyncPayloadSerializer.Serialize(payload);
        var deserialized = FinalizedPassdownSyncPayloadSerializer.Deserialize(json);

        Assert.Equal(payload, deserialized);
    }

    [Fact]
    public void Serialize_OriginIsWrittenAsEnumName_NotAnInteger()
    {
        var payload = FinalizedPassdownSyncPayload.FromDomain(CreateRecord([0x25, 0x50, 0x44, 0x46]));

        var json = FinalizedPassdownSyncPayloadSerializer.Serialize(payload);

        Assert.Contains("\"Manual\"", json);
    }
}
