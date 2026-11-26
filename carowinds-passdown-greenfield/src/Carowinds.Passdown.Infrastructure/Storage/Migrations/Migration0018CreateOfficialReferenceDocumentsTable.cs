using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Phase 31: creates <c>official_reference_documents</c> — one last-known-good official reference
/// document per source (the event schedule, the deals snapshot), with the last successful payload and
/// the last attempt kept in separate columns so a failed check never replaces verified data. Purely
/// additive: no existing table, row, product identity or price-history record is touched. A local
/// cache only; Personal Sync does not read or write it. Forward-only like every migration (an older
/// build refuses a version-18 database with its unsupported-schema message rather than misreading it).
/// </summary>
public sealed class Migration0018CreateOfficialReferenceDocumentsTable : IMigration
{
    public int Version => 18;

    public string Description => "Create official_reference_documents table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS official_reference_documents (
                source_id TEXT PRIMARY KEY,
                success_checked_at_utc TEXT NULL,
                success_source_url TEXT NULL,
                success_detail TEXT NULL,
                success_payload_json TEXT NULL,
                last_attempt_status TEXT NOT NULL,
                last_attempt_checked_at_utc TEXT NOT NULL,
                last_attempt_detail TEXT NOT NULL,
                last_attempt_technical_detail TEXT NULL,
                last_attempt_source_url TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
