using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 18B REL-007: stale sync temp-file cleanup deletes only this app's own aged temp names inside the app-owned sync subtree.</summary>
public class SyncTempFileCleanerTests : IDisposable
{
    private const string Hex = "0123456789abcdef0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-temp-tests-{Guid.NewGuid():N}");
    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-temp-outside-{Guid.NewGuid():N}");

    public SyncTempFileCleanerTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _root, _outside })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private string SyncFolder => SyncPathService.GetSyncFolderPath(_root);

    private static string Write(string path, DateTime? lastWriteUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWriteUtc ?? DateTime.UtcNow.AddDays(-3));
        return path;
    }

    [Fact]
    public void StaleTempsForEveryKnownFolder_AreDeleted()
    {
        var date = new DateOnly(2026, 9, 19);
        var id = Guid.NewGuid();
        string[] temps =
        [
            Write(SyncPathService.GetManifestFilePath(_root) + ".tmp-" + Hex),
            Write(Path.Combine(SyncFolder, $".sync-probe-{Hex}.tmp")),
            Write(SyncPathService.GetDraftFilePath(_root, date) + ".tmp-" + Hex),
            Write(SyncPathService.GetOperationalNoteFilePath(_root, id) + ".tmp-" + Hex),
            Write(SyncPathService.GetHistoryJsonFilePath(_root, date, id) + ".tmp-" + Hex),
            Write(SyncPathService.GetHistoryPdfFilePath(_root, date, id) + ".tmp-" + Hex),
            Write(SyncPathService.GetMonthlyErtFilePath(_root, 2026, 9) + ".tmp-" + Hex),
            Write(SyncPathService.GetErtDateOverrideFilePath(_root, date) + ".tmp-" + Hex),
        ];

        var deleted = SyncTempFileCleaner.CleanStaleTempFiles(_root);

        Assert.Equal(temps.Length, deleted);
        Assert.All(temps, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void LegitimateSyncFiles_AreNeverDeleted_EvenWhenOld()
    {
        var date = new DateOnly(2026, 9, 19);
        var id = Guid.NewGuid();
        string[] legit =
        [
            Write(SyncPathService.GetManifestFilePath(_root)),
            Write(SyncPathService.GetDraftFilePath(_root, date)),
            Write(SyncPathService.GetOperationalNoteFilePath(_root, id)),
            Write(SyncPathService.GetHistoryJsonFilePath(_root, date, id)),
            Write(SyncPathService.GetHistoryPdfFilePath(_root, date, id)),
            Write(SyncPathService.GetMonthlyErtFilePath(_root, 2026, 9)),
            Write(SyncPathService.GetErtDateOverrideFilePath(_root, date)),
        ];

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.All(legit, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void RecentTemp_IsKept_SoAnActiveWriteIsNeverDeleted()
    {
        var recent = Write(SyncPathService.GetDraftFilePath(_root, new DateOnly(2026, 9, 19)) + ".tmp-" + Hex, DateTime.UtcNow.AddMinutes(-5));
        var almost = Write(SyncPathService.GetDraftFilePath(_root, new DateOnly(2026, 9, 20)) + ".tmp-" + Hex, DateTime.UtcNow - SyncTempFileCleaner.StaleAfter + TimeSpan.FromMinutes(10));

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(almost));
    }

    [Theory]
    [InlineData("drafts", "notes.txt.tmp-" + Hex)]                                    // final name not canonical for drafts
    [InlineData("drafts", "2026-9-19.json.tmp-" + Hex)]                               // non-canonical date
    [InlineData("drafts", "2026-09-19 (1).json.tmp-" + Hex)]                          // OneDrive-conflict-like
    [InlineData("drafts", "2026-09-19.json.tmp-0123")]                                // short suffix
    [InlineData("drafts", "2026-09-19.json.TMP-" + Hex)]                              // wrong case marker
    [InlineData("drafts", "2026-09-19.json.tmp-" + Hex + ".bak")]
    [InlineData("drafts", "report.pdf")]
    [InlineData("history", "summary.pdf")]
    [InlineData("history", "2026-09-19.pdf.tmp-" + Hex)]                              // not a history stem
    [InlineData("notes", "0123456789ABCDEF0123456789ABCDEF.json.tmp-" + Hex)]         // N-format guid, not D
    [InlineData("monthly-ert", "2026-13.json.tmp-" + Hex)]
    [InlineData("", "manifest.json.bak")]
    [InlineData("", "2026-09-19.json.tmp-" + Hex)]                                    // draft temp in the wrong folder
    [InlineData("", ".sync-probe-0123.tmp")]
    public void UnrecognizedNames_AreNeverDeleted(string subfolder, string fileName)
    {
        var path = Write(Path.Combine(SyncFolder, subfolder, fileName));

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void FilesOutsideTheAppOwnedSyncFolder_AreNeverTouched()
    {
        // The user's selected root itself and unrelated OneDrive content.
        var inRoot = Write(Path.Combine(_root, "manifest.json.tmp-" + Hex));
        var inRootDrafts = Write(Path.Combine(_root, "drafts", "2026-09-19.json.tmp-" + Hex));
        var nested = Write(Path.Combine(SyncFolder, "drafts", "nested", "2026-09-19.json.tmp-" + Hex));
        Directory.CreateDirectory(SyncFolder);

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(inRoot));
        Assert.True(File.Exists(inRootDrafts));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void MissingSyncFolder_IsANoOp()
    {
        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void SymlinkedSubfolderPointingOutside_IsNotFollowed()
    {
        var outsideTemp = Write(Path.Combine(_outside, "2026-09-19.json.tmp-" + Hex));
        Directory.CreateDirectory(SyncFolder);
        if (!TryCreateDirectoryLink(SyncPathService.GetDraftsDirectoryPath(_root), _outside))
        {
            return; // Creating links needs Developer Mode/admin on Windows; the guard is still exercised on macOS/Linux.
        }

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(outsideTemp));
    }

    [Fact]
    public void SymlinkedTempNameInsideAKnownFolder_IsNotDeleted_AndItsTargetIsUntouched()
    {
        var target = Write(Path.Combine(_outside, "precious.json"));
        var linkPath = SyncPathService.GetDraftFilePath(_root, new DateOnly(2026, 9, 19)) + ".tmp-" + Hex;
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // See SymlinkedSubfolderPointingOutside_IsNotFollowed.
        }

        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(-3));

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(target));
        Assert.NotNull(new FileInfo(linkPath).LinkTarget);
    }

    [Fact]
    public void SymlinkedSyncFolder_IsNotCleaned()
    {
        var outsideTemp = Write(Path.Combine(_outside, "manifest.json.tmp-" + Hex));
        if (!TryCreateDirectoryLink(SyncFolder, _outside))
        {
            return;
        }

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(outsideTemp));
    }

    [Fact]
    public void LockedOrVanishingCandidate_DoesNotThrow_AndOtherCandidatesAreStillCleaned()
    {
        var a = Write(SyncPathService.GetDraftFilePath(_root, new DateOnly(2026, 9, 19)) + ".tmp-" + Hex);
        var b = Write(SyncPathService.GetDraftFilePath(_root, new DateOnly(2026, 9, 20)) + ".tmp-" + Hex);
        using (new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var deleted = SyncTempFileCleaner.CleanStaleTempFiles(_root);

            // Windows refuses to delete an open file; Unix allows it. Either way: no throw, b is gone.
            Assert.InRange(deleted, 1, 2);
        }

        Assert.False(File.Exists(b));
    }

    [Fact]
    public void IsContainedDirectlyIn_AcceptsOnlyDirectChildren()
    {
        var dir = Path.Combine(_root, "drafts");

        Assert.True(SyncTempFileCleaner.IsContainedDirectlyIn(dir, Path.Combine(dir, "a.json.tmp-" + Hex)));
        Assert.True(SyncTempFileCleaner.IsContainedDirectlyIn(dir + Path.DirectorySeparatorChar, Path.Combine(dir, "a")));

        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, Path.Combine(dir, "..", "a.json.tmp-" + Hex)));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, Path.Combine(dir, "sub", "a")));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, "../a"));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, Path.Combine(_outside, "a")));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, Path.GetPathRoot(dir)! + "a"));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, dir + "-sibling" + Path.DirectorySeparatorChar + "a"));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, ""));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, "a\0b"));
        Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, dir + "/../../" + "a"));

        if (OperatingSystem.IsWindows())
        {
            Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, @"\\server\share\a"));
            Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, dir + @"\..\a"));
            Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, dir + "/../a"));
            Assert.False(SyncTempFileCleaner.IsContainedDirectlyIn(dir, @"C:\a"));
        }
    }

    [Fact]
    public void BackslashEscapeNames_OnUnix_AreLiteralNamesThatNeverMatchATempPattern()
    {
        // On Unix a backslash is an ordinary file-name character, so "..\x" is a file directly in the
        // folder — contained, but the name check (no separators allowed) still rejects it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Write(Path.Combine(SyncFolder, "drafts", @"..\2026-09-19.json.tmp-" + Hex));

        Assert.Equal(0, SyncTempFileCleaner.CleanStaleTempFiles(_root));
        Assert.True(File.Exists(path));
    }

    private static bool TryCreateDirectoryLink(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
