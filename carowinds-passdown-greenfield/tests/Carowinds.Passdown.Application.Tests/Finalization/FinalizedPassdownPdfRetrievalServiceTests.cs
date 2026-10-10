using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.Finalization;

/// <summary>
/// Covers <see cref="FinalizedPassdownPdfRetrievalService"/> (spec Phase 9C Part V) — the smallest
/// seam a future History UI needs to fetch an already-frozen PDF. By construction it depends only on
/// <see cref="IFinalizedPassdownRepository"/> — there is no PDF generator/document-model-builder
/// dependency for it to call, so "retrieval never regenerates" is provable from its constructor
/// signature alone, not just from test behavior (spec Part Q).
/// </summary>
public class FinalizedPassdownPdfRetrievalServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 25);

    [Fact]
    public async Task GetPdfAsync_NotFinalized_ReturnsNull()
    {
        var service = new FinalizedPassdownPdfRetrievalService(new InMemoryFinalizedPassdownRepository());

        Assert.Null(await service.GetPdfAsync(Date));
    }

    [Fact]
    public async Task GetPdfAsync_LegacyFinalizedRowWithNoFrozenPdf_ReturnsNull()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        await repository.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date, DateTimeOffset.UtcNow, FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash", FinalizationOrigin.Scheduled,
            null, null, null));
        var service = new FinalizedPassdownPdfRetrievalService(repository);

        Assert.Null(await service.GetPdfAsync(Date));
    }

    [Fact]
    public async Task GetPdfAsync_FrozenPdfPresent_ReturnsStoredBytesAndMetadata()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        await repository.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date, DateTimeOffset.UtcNow, FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash", FinalizationOrigin.Scheduled,
            pdfBytes, "deadbeef", pdfBytes.LongLength));
        var service = new FinalizedPassdownPdfRetrievalService(repository);

        var result = await service.GetPdfAsync(Date);

        Assert.NotNull(result);
        Assert.Equal(pdfBytes, result!.PdfBytes);
        Assert.Equal("deadbeef", result.PdfSha256);
        Assert.Equal(pdfBytes.LongLength, result.PdfSizeBytes);
        Assert.Equal("Carowinds-Operations-2026-08-25.pdf", result.SuggestedFileName);
    }
}
