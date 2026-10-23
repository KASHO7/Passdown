using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 16B §7: the deterministic sync-path mapping — every path must be exact, not "close enough".</summary>
public class SyncPathServiceTests
{
    private const string Root = "/Users/associate/OneDrive";

    [Fact]
    public void GetSyncFolderPath_AppendsOwnedSubfolder()
    {
        var expected = Path.Combine(Root, "Carowinds Operations Sync");
        Assert.Equal(expected, SyncPathService.GetSyncFolderPath(Root));
    }

    [Fact]
    public void GetManifestFilePath_IsInsideOwnedSubfolder()
    {
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "manifest.json");
        Assert.Equal(expected, SyncPathService.GetManifestFilePath(Root));
    }

    [Fact]
    public void GetDraftFilePath_UsesInvariantIsoDateFilename()
    {
        var date = new DateOnly(2026, 9, 9);
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "drafts", "2026-09-09.json");
        Assert.Equal(expected, SyncPathService.GetDraftFilePath(Root, date));
    }

    [Fact]
    public void GetOperationalNoteFilePath_UsesLowercaseHyphenatedGuidFilename()
    {
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "notes", "3fa85f64-5717-4562-b3fc-2c963f66afa6.json");
        Assert.Equal(expected, SyncPathService.GetOperationalNoteFilePath(Root, id));
    }

    [Fact]
    public void GetMonthlyErtFilePath_UsesInvariantYearMonthFilename()
    {
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "monthly-ert", "2026-09.json");
        Assert.Equal(expected, SyncPathService.GetMonthlyErtFilePath(Root, 2026, 9));
    }

    [Fact]
    public void GetErtDateOverrideFilePath_UsesInvariantIsoDateFilename()
    {
        var date = new DateOnly(2026, 9, 19);
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "ert-date-overrides", "2026-09-19.json");
        Assert.Equal(expected, SyncPathService.GetErtDateOverrideFilePath(Root, date));
    }

    [Fact]
    public void GetHistoryJsonFilePath_UsesInvariantIsoDatePlusGuidFilename()
    {
        var date = new DateOnly(2026, 1, 5);
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "history", "2026-01-05-3fa85f64-5717-4562-b3fc-2c963f66afa6.json");
        Assert.Equal(expected, SyncPathService.GetHistoryJsonFilePath(Root, date, id));
    }

    [Fact]
    public void GetHistoryPdfFilePath_IsSameStemAsHistoryJson_DifferentExtension()
    {
        var date = new DateOnly(2026, 1, 5);
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "history", "2026-01-05-3fa85f64-5717-4562-b3fc-2c963f66afa6.pdf");
        Assert.Equal(expected, SyncPathService.GetHistoryPdfFilePath(Root, date, id));
    }

    [Fact]
    public void GetHistoryDirectoryPath_IsInsideOwnedSubfolder()
    {
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "history");
        Assert.Equal(expected, SyncPathService.GetHistoryDirectoryPath(Root));
    }

    [Fact]
    public void HistoryJsonAndPdfPaths_TwoDifferentIdsOnSameDate_NeverCollide()
    {
        var date = new DateOnly(2026, 9, 9);
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.NotEqual(SyncPathService.GetHistoryJsonFilePath(Root, date, idA), SyncPathService.GetHistoryJsonFilePath(Root, date, idB));
        Assert.NotEqual(SyncPathService.GetHistoryPdfFilePath(Root, date, idA), SyncPathService.GetHistoryPdfFilePath(Root, date, idB));
    }

    [Fact]
    public void TryParseHistoryFileName_RoundTripsWithGetHistoryPaths()
    {
        var date = new DateOnly(2026, 9, 9);
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var stem = Path.GetFileNameWithoutExtension(SyncPathService.GetHistoryJsonFilePath(Root, date, id));

        Assert.True(SyncPathService.TryParseHistoryFileName(stem, out var parsedDate, out var parsedId));
        Assert.Equal(date, parsedDate);
        Assert.Equal(id, parsedId);
    }

    [Theory]
    [InlineData("2026-09-09")]
    [InlineData("2026-09-09-3fa85f64-5717-4562-b3fc-2c963f66afa6-DESKTOP1")]
    [InlineData("not-a-history-filename")]
    [InlineData("")]
    public void TryParseHistoryFileName_UnexpectedFilename_ReturnsFalse(string stem)
    {
        Assert.False(SyncPathService.TryParseHistoryFileName(stem, out _, out _));
    }

    [Fact]
    public void GetSyncFolderPath_NullOrWhitespaceRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => SyncPathService.GetSyncFolderPath(""));
        Assert.Throws<ArgumentException>(() => SyncPathService.GetSyncFolderPath("   "));
    }

    [Fact]
    public void Phase26_OfficialPriceHistoryPaths_AreOneFilePerDevice_InTheirOwnFolder()
    {
        var device = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var path = SyncPathService.GetOfficialPriceHistoryFilePath("root", device);

        Assert.Equal(Path.Combine("root", SyncPathService.SyncFolderName, "official-price-history", "3fa85f64-5717-4562-b3fc-2c963f66afa6.json"), path);
        Assert.True(SyncPathService.TryParseOfficialPriceHistoryFileName(Path.GetFileName(path), out var parsed, out var canonical));
        Assert.Equal((device, true), (parsed, canonical));
    }

    [Theory]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6-LAPTOP.json", true)]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6 (1).json", true)]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6.json.tmp-0123456789abcdef0123456789abcdef", false)]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6x.json", false)]
    [InlineData("00000000-0000-0000-0000-000000000000.json", false)]
    [InlineData("notes.json", false)]
    [InlineData("../3fa85f64-5717-4562-b3fc-2c963f66afa6.json", false)]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6/../x.json", false)]
    [InlineData("C:3fa85f64-5717-4562-b3fc-2c963f66afa6.json", false)]
    [InlineData("", false)]
    public void Phase26_OfficialPriceHistoryFileNames_AcceptOnlyContributorFilesAndTheirConflictCopies(string name, bool accepted)
    {
        Assert.Equal(accepted, SyncPathService.TryParseOfficialPriceHistoryFileName(name, out _, out var canonical));
        Assert.False(canonical);
    }
}
