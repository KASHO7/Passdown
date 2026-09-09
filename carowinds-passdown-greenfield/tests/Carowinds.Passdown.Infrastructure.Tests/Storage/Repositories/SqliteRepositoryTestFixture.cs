using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

/// <summary>
/// A fresh temp SQLite database with the full Phase 2 schema applied, for
/// repository tests. One instance per test class instance (xUnit creates a new
/// test class instance per test method), so tests never share state.
/// </summary>
internal sealed class SqliteRepositoryTestFixture : IDisposable
{
    public SqliteConnectionFactory ConnectionFactory { get; }

    public SqliteRepositoryTestFixture()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-repo-tests-{Guid.NewGuid():N}.db");
        ConnectionFactory = new SqliteConnectionFactory(databasePath);

        IReadOnlyList<IMigration> migrations =
        [
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
            new Migration0009AddFinalizedPassdownPdfColumns(),
            new Migration0010CreateOperationalNotesTable(),
            new Migration0011CreateDeepRefreshStatusTable(),
            new Migration0012CreateSyncEntityStateTable(),
            new Migration0013CreateSyncConflictsTable(),
        ];
        new MigrationRunner(ConnectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();
    }

    /// <summary>A second factory over the same database file, simulating a reopened connection (e.g. after an app restart).</summary>
    public SqliteConnectionFactory ReopenConnectionFactory() => new(ConnectionFactory.DatabasePath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(ConnectionFactory.DatabasePath))
        {
            File.Delete(ConnectionFactory.DatabasePath);
        }
    }
}
