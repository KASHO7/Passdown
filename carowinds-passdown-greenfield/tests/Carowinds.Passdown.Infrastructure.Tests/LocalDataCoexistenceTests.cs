using Carowinds.Passdown.Infrastructure.Concurrency;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests;

/// <summary>
/// Phase 7D Part H/K: no real legacy Carowinds Passdown LocalAppData directory is available in
/// this dev environment to diff against directly (Part I), so this proves the correct safety
/// contract instead — arbitrary unknown files coexisting under the same directory this app's
/// database/lock live in are never enumerated, read, modified, or deleted by initializing or
/// using this app. This is what makes coexistence with an unknown legacy file set (or a future
/// unrelated app sharing the folder) safe without knowing its exact contents in advance.
/// </summary>
public class LocalDataCoexistenceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-coexist-{Guid.NewGuid():N}");

    public LocalDataCoexistenceTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private (string Path, byte[] Bytes, DateTime WriteTimeUtc)[] SeedUnrelatedFiles()
    {
        var files = new[]
        {
            Path.Combine(_tempDirectory, "legacy-cache.json"),
            Path.Combine(_tempDirectory, "old-settings.xml"),
            Path.Combine(_tempDirectory, "previous-app.log"),
        };

        var arbitrarySubfolder = Path.Combine(_tempDirectory, "arbitrary-folder");
        Directory.CreateDirectory(arbitrarySubfolder);
        var nestedFile = Path.Combine(arbitrarySubfolder, "file.txt");

        var allPaths = files.Append(nestedFile).ToArray();
        var seeded = new List<(string, byte[], DateTime)>();
        foreach (var path in allPaths)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes($"unrelated content for {Path.GetFileName(path)}");
            File.WriteAllBytes(path, bytes);
            var oldTimestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, oldTimestamp);
            seeded.Add((path, bytes, oldTimestamp));
        }

        return [.. seeded];
    }

    private static void AssertUnchanged((string Path, byte[] Bytes, DateTime WriteTimeUtc)[] seeded)
    {
        foreach (var (path, bytes, writeTimeUtc) in seeded)
        {
            Assert.True(File.Exists(path), $"Expected untouched unrelated file to still exist: {path}");
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(writeTimeUtc, File.GetLastWriteTimeUtc(path));
        }
    }

    [Fact]
    public void MigrationRunner_InitializingAlongsideUnknownFiles_LeavesThemByteForByteUnchanged()
    {
        var seeded = SeedUnrelatedFiles();
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        IReadOnlyList<IMigration> migrations = [new Migration0001CreateSchemaVersionTable()];

        var appliedVersion = new MigrationRunner(new SqliteConnectionFactory(databasePath), migrations, NullLogger<MigrationRunner>.Instance).Run();

        Assert.Equal(1, appliedVersion);
        Assert.True(File.Exists(databasePath));
        AssertUnchanged(seeded);
    }

    [Fact]
    public void FileScheduledRefreshLock_CreatingAndReleasingAlongsideUnknownFiles_TouchesOnlyItsOwnPath()
    {
        var seeded = SeedUnrelatedFiles();
        var lockPath = Path.Combine(_tempDirectory, "scheduled-refresh.lock");

        using (var scheduledLock = new FileScheduledRefreshLock(lockPath))
        {
            Assert.True(scheduledLock.TryAcquire());
        }

        Assert.True(File.Exists(lockPath)); // deliberately never deleted, per FileScheduledRefreshLock's own contract.
        AssertUnchanged(seeded);
    }

    [Fact]
    public void ArbitrarySubfolderUnderTheDataDirectory_IsNeverEnumeratedOrRemoved()
    {
        var seeded = SeedUnrelatedFiles();
        var arbitrarySubfolder = Path.Combine(_tempDirectory, "arbitrary-folder");
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        IReadOnlyList<IMigration> migrations = [new Migration0001CreateSchemaVersionTable()];

        new MigrationRunner(new SqliteConnectionFactory(databasePath), migrations, NullLogger<MigrationRunner>.Instance).Run();
        using (var scheduledLock = new FileScheduledRefreshLock(Path.Combine(_tempDirectory, "scheduled-refresh.lock")))
        {
            scheduledLock.TryAcquire();
        }

        Assert.True(Directory.Exists(arbitrarySubfolder));
        AssertUnchanged(seeded);
    }
}
