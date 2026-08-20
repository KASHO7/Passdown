using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>dated_results</c>: one row per exact (category, requested date)
/// identity (spec §6.2, mirrors <c>Carowinds.Passdown.Domain.Retrieval.DatedResultKey</c>),
/// with the composite primary key enforcing that identity at the storage layer.
///
/// Each row carries two independent evidence/value snapshots, mirroring
/// <c>Carowinds.Passdown.Domain.Retrieval.LastKnownGood{T}</c>: the <c>success_*</c>
/// columns hold the most recent successful completion (Verified/Closed/NotPublished/
/// Unavailable) and its payload, while <c>last_attempt_*</c> holds the most recent
/// attempt of any outcome. A failed attempt updates only the latter, so a previously
/// verified value is never overwritten by a later error (spec §6.7).
///
/// <c>success_payload_json</c> is an opaque JSON blob — sufficient to round-trip
/// whatever typed dated data (hours, prices, etc.) later phases introduce, without
/// this migration needing to know their shape.
/// </summary>
public sealed class Migration0003CreateDatedResultsTable : IMigration
{
    public int Version => 3;

    public string Description => "Create dated_results table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS dated_results (
                category TEXT NOT NULL,
                requested_date TEXT NOT NULL,

                success_status TEXT NULL,
                success_confirmed_date TEXT NULL,
                success_checked_at_utc TEXT NULL,
                success_strategy TEXT NULL,
                success_detail TEXT NULL,
                success_technical_detail TEXT NULL,
                success_source_name TEXT NULL,
                success_source_url TEXT NULL,
                success_payload_json TEXT NULL,

                last_attempt_status TEXT NOT NULL,
                last_attempt_confirmed_date TEXT NULL,
                last_attempt_checked_at_utc TEXT NOT NULL,
                last_attempt_strategy TEXT NOT NULL,
                last_attempt_detail TEXT NOT NULL,
                last_attempt_technical_detail TEXT NULL,
                last_attempt_source_name TEXT NOT NULL,
                last_attempt_source_url TEXT NOT NULL,

                PRIMARY KEY (category, requested_date)
            );

            CREATE INDEX IF NOT EXISTS ix_dated_results_requested_date ON dated_results (requested_date);
            """;
        command.ExecuteNonQuery();
    }
}
