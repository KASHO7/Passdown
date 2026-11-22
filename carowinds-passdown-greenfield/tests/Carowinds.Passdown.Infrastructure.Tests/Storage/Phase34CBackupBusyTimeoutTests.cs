using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Updates.Installation;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 34C: <see cref="SqliteDatabaseBackup"/>'s busy timeout became injectable so one updater safety test no
/// longer waits out the 5 s production timeout. Production keeps 5 s; the update guard reads the schema with its
/// backup's timeout; a real exclusive lock still makes the read fail (never a guessed version) and the same
/// guard reads the committed version again once the lock is gone. No timing is asserted.
/// </summary>
public sealed class Phase34CBackupBusyTimeoutTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "carowinds-34c-busy-" + Guid.NewGuid().ToString("N"));

    public Phase34CBackupBusyTimeoutTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ProductionDefault_StaysFiveSeconds()
    {
        Assert.Equal(5, SqliteDatabaseBackup.DefaultBusyTimeoutSeconds);
        Assert.Equal(5, new SqliteDatabaseBackup().BusyTimeoutSeconds);
        Assert.Equal(5, new SqliteDatabaseBackup(_ => null).BusyTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ATimeoutBelowOneSecond_IsRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteDatabaseBackup(busyTimeoutSeconds: seconds));
    }

    [Fact]
    public void UpdateGuard_UnderARealExclusiveLock_CannotReadTheSchema_ThenReadsItOnceTheLockIsGone()
    {
        var path = Path.Combine(_workspace, "carowinds-passdown.db");
        using (var create = SqliteDatabaseBackup.Open(path, SqliteOpenMode.ReadWriteCreate))
        using (var command = create.CreateCommand())
        {
            command.CommandText = "CREATE TABLE t (x INTEGER); PRAGMA user_version = 18;";
            command.ExecuteNonQuery();
        }

        var guard = SqliteUpdateDatabaseGuard.ForDatabase(path, new SqliteDatabaseBackup(busyTimeoutSeconds: 1));
        using (var holder = SqliteDatabaseBackup.Open(path, SqliteOpenMode.ReadWrite))
        using (var lockCommand = holder.CreateCommand())
        {
            lockCommand.CommandText = "BEGIN EXCLUSIVE;";
            lockCommand.ExecuteNonQuery();

            Assert.False(guard.TryReadSchemaVersion(out var locked));
            Assert.Null(locked);
        }

        Assert.True(guard.TryReadSchemaVersion(out var version));
        Assert.Equal(18, version);
    }
}
