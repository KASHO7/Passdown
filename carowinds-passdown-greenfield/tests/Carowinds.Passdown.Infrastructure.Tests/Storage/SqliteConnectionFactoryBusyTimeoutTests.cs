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
