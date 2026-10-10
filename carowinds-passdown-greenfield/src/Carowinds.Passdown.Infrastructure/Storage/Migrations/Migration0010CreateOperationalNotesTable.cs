using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>operational_notes</c>: persistent, supervisor-managed operational notes that may span
/// multiple days (spec Phase 11A Parts A/K), entirely separate from the per-operating-date editable
/// <c>associate_drafts</c> row. Nothing in this table is ever automatically copied into a daily
/// passdown — future inclusion (Phase 11D) is an explicit, user-selected operation, not a join or a
/// background job. A single composite index on <c>(status, effective_from)</c> supports the common
/// "notes relevant to a date" query without over-indexing a small local database (spec Part L) — a
/// second index on <c>effective_through</c> alone was evaluated and skipped, since every relevance
/// query already filters on <c>status</c> first.
/// </summary>
public sealed class Migration0010CreateOperationalNotesTable : IMigration
{
    public int Version => 10;

    public string Description => "Create operational_notes table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS operational_notes (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                body TEXT,
                category TEXT NOT NULL,
                priority TEXT NOT NULL,
                effective_from TEXT NOT NULL,
                effective_through TEXT,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                resolved_at_utc TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_operational_notes_status_effective_from ON operational_notes (status, effective_from);
            """;
        command.ExecuteNonQuery();
    }
}
