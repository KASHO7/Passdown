using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.History;

/// <summary>
/// Covers <see cref="PassdownHistoryService"/> (spec Phase 9D Part R) — the read-only seam a future
/// History UI uses. Every value must come from the already-finalized <see cref="FinalizedPassdown"/>
/// row (never regenerated, never re-resolved against anything current), and a bad/newer-schema row
/// must degrade to an explained detail rather than throwing (spec Part M/X).
/// </summary>
public class PassdownHistoryServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 25);

    private static FinalizedPassdownSnapshot BuildSnapshot() => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        Date,
        "Tuesday, August 25, 2026",
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "9:00 AM - 9:00 PM", "Open", null, null, "Verified", null),
        new FinalizedAttendanceSnapshot(12000, 300),
        new FinalizedEarlyRideTimeSnapshot(true, "7:30 AM entry", ["Fury 325"]),
        new FinalizedDailyFeatureSnapshot("Ride Spotlight", "Fury 325"),
        ["Gate 3 closed for maintenance."],
        [new FinalizedProductCategorySnapshot("Dated Admission", FinalizedProductSectionTier.KeySelling, [new FinalizedProductRowSnapshot("1-Day Ticket", "$54.99", "$64.99", null, null)])],
        false,
        "8/25/2026 5:58 PM");

    private static FinalizedPassdown BuildRecord(int schemaVersion, string snapshotJson, byte[]? pdfBytes = null) => new(
        Guid.NewGuid(), Date, new DateTimeOffset(2026, 8, 26, 10, 2, 0, TimeSpan.Zero), schemaVersion, snapshotJson, "hash",
        FinalizationOrigin.Scheduled, pdfBytes, pdfBytes is null ? null : "pdfhash", pdfBytes?.LongLength);

    [Fact]
    public async Task ListAsync_PassesThroughRepository_NewestFirst()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        await repository.AddAsync(BuildRecord(1, "{}") with { PassdownDate = new DateOnly(2026, 8, 20) });
        await repository.AddAsync(BuildRecord(1, "{}") with { PassdownDate = new DateOnly(2026, 8, 25) });
        var service = new PassdownHistoryService(repository);

        var results = await service.ListAsync();

        Assert.Equal([new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 20)], results.Select(r => r.PassdownDate));
    }

    [Fact]
    public async Task GetAsync_NoFinalizedRecord_ReturnsNull()
    {
        var service = new PassdownHistoryService(new InMemoryFinalizedPassdownRepository());

        Assert.Null(await service.GetAsync(Date));
    }

    [Fact]
    public async Task GetAsync_CurrentSchemaVersion_ReturnsTypedSnapshot()
    {
        var snapshot = BuildSnapshot();
        var json = FinalizedPassdownSnapshotSerializer.Serialize(snapshot);
        var repository = new InMemoryFinalizedPassdownRepository();
        await repository.AddAsync(BuildRecord(FinalizedPassdownSnapshot.CurrentSchemaVersion, json, [1, 2, 3]));
        var service = new PassdownHistoryService(repository);

        var detail = await service.GetAsync(Date);

        Assert.NotNull(detail);
        Assert.Null(detail!.UnavailableReason);
        Assert.NotNull(detail.Snapshot);
        Assert.Equal(snapshot.OperatingDateText, detail.Snapshot!.OperatingDateText);
        Assert.Equal(snapshot.ProductCategories[0].Products[0].DisplayName, detail.Snapshot.ProductCategories[0].Products[0].DisplayName);
        Assert.True(detail.Summary.HasFrozenPdf);
    }

    [Fact]
    public async Task GetAsync_NewerSchemaVersionThanCurrent_ReturnsMetadataWithoutSnapshot_AndDoesNotThrow()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        await repository.AddAsync(BuildRecord(FinalizedPassdownSnapshot.CurrentSchemaVersion + 1, "{ \"Whatever\": true }", [1, 2, 3]));
        var service = new PassdownHistoryService(repository);

        var detail = await service.GetAsync(Date);

        Assert.NotNull(detail);
        Assert.Null(detail!.Snapshot);
        Assert.Equal("This finalized passdown was created by a newer version of the app.", detail.UnavailableReason);
        // The metadata row (including PDF availability) must still be usable even though the
        // structured snapshot could not be read (spec Part M).
        Assert.True(detail.Summary.HasFrozenPdf);
        Assert.Equal(Date, detail.Summary.PassdownDate);
    }

    [Fact]
    public async Task GetAsync_MalformedSnapshotJson_ReturnsMetadataWithoutSnapshot_AndDoesNotThrow()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        await repository.AddAsync(BuildRecord(FinalizedPassdownSnapshot.CurrentSchemaVersion, "{ not valid json"));
        var service = new PassdownHistoryService(repository);

        var detail = await service.GetAsync(Date);

        Assert.NotNull(detail);
        Assert.Null(detail!.Snapshot);
        Assert.Equal("This finalized passdown's stored data could not be read.", detail.UnavailableReason);
    }

    [Fact]
    public async Task GetAsync_LegacyRecordWithNoFrozenPdf_StillReturnsSnapshot_WithHasFrozenPdfFalse()
    {
        var snapshot = BuildSnapshot();
        var json = FinalizedPassdownSnapshotSerializer.Serialize(snapshot);
        var repository = new InMemoryFinalizedPassdownRepository();
        await repository.AddAsync(BuildRecord(FinalizedPassdownSnapshot.CurrentSchemaVersion, json, pdfBytes: null));
        var service = new PassdownHistoryService(repository);

        var detail = await service.GetAsync(Date);

        Assert.NotNull(detail);
        Assert.NotNull(detail!.Snapshot);
        Assert.False(detail.Summary.HasFrozenPdf);
    }
}
