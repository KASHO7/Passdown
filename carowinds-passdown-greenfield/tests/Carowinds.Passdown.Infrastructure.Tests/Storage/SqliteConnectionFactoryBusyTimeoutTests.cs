using Carowinds.Passdown.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 6B Part H: the desktop UI's autosave and a scheduled/startup-catch-up refresh are
/// expected to open short-lived connections against the same database file concurrently. Proves
/// the configured busy timeout actually makes a second writer retry and succeed rather than
/// throwing SQLITE_BUSY the instant it collides with a still-open write transaction elsewhere.
/// </summary>
public class SqliteConnectionFactoryBusyTimeoutTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-busy-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task OpenConnection_SecondWriter_WaitsOutABriefLockInsteadOfFailingImmediately()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        using (var setup = factory.OpenConnection())
        {
            using var create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY);";
            create.ExecuteNonQuery();
        }

        using var holder = factory.OpenConnection();
        using var holderTransaction = holder.BeginTransaction();
        using (var write = holder.CreateCommand())
        {
            write.Transaction = holderTransaction;
            write.CommandText = "INSERT INTO t (id) VALUES (1);";
            write.ExecuteNonQuery();
        }

        var releaseAfter = Task.Delay(TimeSpan.FromMilliseconds(300));
        var releaseTask = Task.Run(async () =>
        {
            await releaseAfter;
            holderTransaction.Commit();
        });

        using var second = factory.OpenConnection();
        using var secondWrite = second.CreateCommand();
        secondWrite.CommandText = "INSERT INTO t (id) VALUES (2);";

        // Would throw SqliteException (SQLITE_BUSY) immediately without a busy timeout configured;
        // with one configured it blocks until the first connection commits, then succeeds.
        var rowsAffected = secondWrite.ExecuteNonQuery();

        await releaseTask;
        Assert.Equal(1, rowsAffected);
    }
}

/// <summary>
/// Phase 18A REL-002: the database path is a value, never connection-string syntax. Pre-18A the path
/// was concatenated into "Data Source=...;", so a folder name containing ';' split the path and was
/// parsed as additional keywords (here: an unknown keyword, which throws) instead of opening the file.
/// </summary>
public class SqliteConnectionFactoryPathQuotingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-quoting-{Guid.NewGuid():N}", "Local;Weird Folder=1");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var root = Path.GetDirectoryName(_directory)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OpenConnection_PathContainingConnectionStringDelimiters_OpensExactlyThatFile()
    {
        var databasePath = Path.Combine(_directory, "carowinds-passdown.db");
        var factory = new SqliteConnectionFactory(databasePath);

        using (var connection = factory.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE probe (id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        Assert.True(File.Exists(databasePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_directory)!, "*", SearchOption.AllDirectories));
    }
}
