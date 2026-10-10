using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Adds frozen-PDF storage to <c>finalized_passdowns</c> (spec Phase 9C Parts B/C): <c>pdf_bytes</c>
/// (the exact PDF bytes produced at finalization), <c>pdf_sha256</c> (lowercase-hex SHA-256 of those
/// bytes), and <c>pdf_size_bytes</c>. Purely additive — this does not touch
/// <see cref="Migration0008CreateFinalizedPassdownsTable"/> or the table's identity/uniqueness. All
/// three columns are nullable at the database level specifically so pre-9C rows already present in a
/// development/test database keep applying cleanly with <c>NULL</c> PDF columns (spec Part J) rather
/// than requiring a backfill this migration cannot honestly provide — a PDF regenerated now would use
/// current styling/code and would not be the exact historical PDF. From this migration forward,
/// <c>FinalizedPassdownFinalizationService</c> always populates all three columns as part of the same
/// insert (spec Part H's atomicity) — only rows finalized before this migration existed have them
/// NULL.
/// </summary>
public sealed class Migration0009AddFinalizedPassdownPdfColumns : IMigration
{
    public int Version => 9;

    public string Description => "Add frozen PDF columns to finalized_passdowns";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            ALTER TABLE finalized_passdowns ADD COLUMN pdf_bytes BLOB;
            ALTER TABLE finalized_passdowns ADD COLUMN pdf_sha256 TEXT;
            ALTER TABLE finalized_passdowns ADD COLUMN pdf_size_bytes INTEGER;
            """;
        command.ExecuteNonQuery();
    }
}
