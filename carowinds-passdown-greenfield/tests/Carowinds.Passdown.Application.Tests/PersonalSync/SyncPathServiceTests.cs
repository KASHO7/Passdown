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
    public void GetHistoryJsonFilePath_UsesInvariantIsoDateFilename()
    {
        var date = new DateOnly(2026, 1, 5);
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "history", "2026-01-05.json");
        Assert.Equal(expected, SyncPathService.GetHistoryJsonFilePath(Root, date));
    }

    [Fact]
    public void GetHistoryPdfFilePath_IsSameStemAsHistoryJson_DifferentExtension()
    {
        var date = new DateOnly(2026, 1, 5);
        var expected = Path.Combine(Root, "Carowinds Operations Sync", "history", "2026-01-05.pdf");
        Assert.Equal(expected, SyncPathService.GetHistoryPdfFilePath(Root, date));
    }

    [Fact]
    public void GetSyncFolderPath_NullOrWhitespaceRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => SyncPathService.GetSyncFolderPath(""));
        Assert.Throws<ArgumentException>(() => SyncPathService.GetSyncFolderPath("   "));
    }
}
