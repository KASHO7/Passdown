using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

public class SqliteFinalizedPassdownRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateOnly Halloween = new(2026, 10, 31);

    private SqliteFinalizedPassdownRepository CreateRepository() => new(_fixture.ConnectionFactory);

    // PdfBytes/PdfSha256/PdfSizeBytes default to null here (a legacy pre-9C-shaped row) so existing
    // full-record equality assertions below stay valid (null == null) — PDF-specific tests construct
    // their own sample with explicit PDF fields instead (spec Phase 9C Part J).
    private static FinalizedPassdown Sample(DateOnly? date = null, FinalizationOrigin origin = FinalizationOrigin.Scheduled) => new(
        Guid.NewGuid(),
        date ?? Halloween,
        new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero),
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        """{"PassdownDate":"2026-10-31"}""",
        "abc123def456",
        origin,
        null,
        null,
        null);

    private static FinalizedPassdown SampleWithPdf(DateOnly? date = null, byte[]? pdfBytes = null)
    {
        var bytes = pdfBytes ?? [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]; // "%PDF-1.4"
        return Sample(date) with
        {
            PdfBytes = bytes,
            PdfSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            PdfSizeBytes = bytes.LongLength,
        };
    }

    [Fact]
    public async Task GetByDateAsync_MissingDate_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetByDateAsync(Halloween));
    }

    [Fact]
    public async Task ExistsForDateAsync_MissingDate_ReturnsFalse()
    {
        var repository = CreateRepository();

        Assert.False(await repository.ExistsForDateAsync(Halloween));
    }

    [Fact]
    public async Task AddAsync_ThenGetByDateAsync_RoundTripsRecord()
    {
        var repository = CreateRepository();
        var record = Sample();

        await repository.AddAsync(record);
        var loaded = await repository.GetByDateAsync(Halloween);

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task AddAsync_ThenExistsForDateAsync_ReturnsTrue()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Sample());

        Assert.True(await repository.ExistsForDateAsync(Halloween));
    }

    [Fact]
    public async Task AddAsync_DuplicatePassdownDate_ThrowsRatherThanOverwriting()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Sample());

        await Assert.ThrowsAsync<SqliteException>(() => repository.AddAsync(Sample(origin: FinalizationOrigin.Manual)));

        // The original row must be untouched by the rejected second insert.
        var loaded = await repository.GetByDateAsync(Halloween);
        Assert.Equal(FinalizationOrigin.Scheduled, loaded!.Origin);
    }

    [Fact]
    public async Task Repository_ExposesNoUpdateOrDeleteMethod()
    {
        var methodNames = typeof(IFinalizedPassdownRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain(methodNames, name => name.Contains("Update", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methodNames, name => name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_NoRecords_ReturnsEmpty()
    {
        var repository = CreateRepository();

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task ListAsync_MultipleDates_OrdersMostRecentPassdownDateFirst()
    {
        var repository = CreateRepository();
        var earlier = new DateOnly(2026, 10, 30);
        var later = new DateOnly(2026, 11, 1);

        await repository.AddAsync(Sample(Halloween));
        await repository.AddAsync(Sample(earlier));
        await repository.AddAsync(Sample(later));

        var summaries = await repository.ListAsync();

        Assert.Equal([later, Halloween, earlier], summaries.Select(s => s.PassdownDate));
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        await writer.AddAsync(Sample());

        var reader = new SqliteFinalizedPassdownRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetByDateAsync(Halloween);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task AddAsync_SnapshotContainingQuotesAndSqlSyntax_IsStoredVerbatimViaParameterBinding()
    {
        var repository = CreateRepository();
        const string maliciousLookingJson = """{"OperationalNotes":["Gate opens at 10'; DROP TABLE finalized_passdowns; --"]}""";
        var record = Sample() with { SnapshotJson = maliciousLookingJson };

        await repository.AddAsync(record);
        var loaded = await repository.GetByDateAsync(Halloween);

        Assert.Equal(maliciousLookingJson, loaded!.SnapshotJson);
    }

    // Phase 9C Part M: the exact PDF bytes generated at finalization round-trip byte-for-byte — no
    // re-generation, just storage and retrieval.
    [Fact]
    public async Task AddAsync_ThenGetByDateAsync_RoundTripsPdfBytesExactly()
    {
        var repository = CreateRepository();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00, 0xFF, 0x10, 0x20 };
        var record = SampleWithPdf(pdfBytes: pdfBytes);

        await repository.AddAsync(record);
        var loaded = await repository.GetByDateAsync(Halloween);

        Assert.NotNull(loaded);
        Assert.True(loaded!.HasFrozenPdf);
        Assert.Equal(pdfBytes, loaded.PdfBytes);
        Assert.Equal(record.PdfSha256, loaded.PdfSha256);
        Assert.Equal(record.PdfSizeBytes, loaded.PdfSizeBytes);
    }

    // Phase 9C Part J: a pre-9C row (finalized before the PDF columns existed) reads back safely with
    // every PDF field null, and is clearly distinguishable via HasFrozenPdf rather than silently
    // treated as having an exact frozen PDF.
    [Fact]
    public async Task GetByDateAsync_LegacyRowWithNoPdfColumns_ReadsSafelyWithHasFrozenPdfFalse()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Sample()); // PdfBytes/PdfSha256/PdfSizeBytes all null by default.

        var loaded = await repository.GetByDateAsync(Halloween);

        Assert.NotNull(loaded);
        Assert.False(loaded!.HasFrozenPdf);
        Assert.Null(loaded.PdfBytes);
        Assert.Null(loaded.PdfSha256);
        Assert.Null(loaded.PdfSizeBytes);
    }

    // Phase 9C Part L: history-list metadata exposes HasFrozenPdf/PdfSizeBytes without ever selecting
    // the pdf_bytes blob column (see SqliteFinalizedPassdownRepository.ListAsync).
    [Fact]
    public async Task ListAsync_MixOfLegacyAndFrozenPdfRows_ExposesHasFrozenPdfAndSizePerRow()
    {
        var repository = CreateRepository();
        var withPdf = new DateOnly(2026, 11, 1);
        var legacy = new DateOnly(2026, 11, 2);
        var pdfBytes = new byte[] { 1, 2, 3, 4, 5 };
        await repository.AddAsync(SampleWithPdf(withPdf, pdfBytes));
        await repository.AddAsync(Sample(legacy));

        var summaries = await repository.ListAsync();

        var withPdfSummary = summaries.Single(s => s.PassdownDate == withPdf);
        var legacySummary = summaries.Single(s => s.PassdownDate == legacy);
        Assert.True(withPdfSummary.HasFrozenPdf);
        Assert.Equal(pdfBytes.LongLength, withPdfSummary.PdfSizeBytes);
        Assert.False(legacySummary.HasFrozenPdf);
        Assert.Null(legacySummary.PdfSizeBytes);
    }

    // Phase 16E §2/§11: GetByIdAsync is Personal Sync's own lookup — the surrogate id, not the date, is
    // the sync identity, and it must tell "no record with this id" apart from "a record exists under a
    // different id for this date" (the dual-finalization case).
    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsRecord()
    {
        var repository = CreateRepository();
        var record = Sample();

        await repository.AddAsync(record);
        var loaded = await repository.GetByIdAsync(record.Id);

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotMatchADifferentRecordsIdEvenOnTheSameDate()
    {
        var repository = CreateRepository();
        var record = Sample();
        await repository.AddAsync(record);

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    // Phase 16E §4/§23: ListForSyncAsync is the cheap per-pass enumeration Personal Sync's export loop
    // walks — id/date/content-hash only, never the snapshot text or PDF blob.
    [Fact]
    public async Task ListForSyncAsync_NoRecords_ReturnsEmpty()
    {
        var repository = CreateRepository();

        Assert.Empty(await repository.ListForSyncAsync());
    }

    [Fact]
    public async Task ListForSyncAsync_ReturnsIdDateAndContentHash_ForEveryRecord()
    {
        var repository = CreateRepository();
        var record = SampleWithPdf();
        await repository.AddAsync(record);

        var metadata = Assert.Single(await repository.ListForSyncAsync());

        Assert.Equal(record.Id, metadata.Id);
        Assert.Equal(record.PassdownDate, metadata.PassdownDate);
        Assert.Equal(record.ContentHash, metadata.ContentHash);
    }
}
