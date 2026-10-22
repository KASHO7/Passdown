using System.Text;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Tests.PersonalSync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 27: a synthetic, representative RC.8 profile (database schema 16) in a temporary folder — official
/// prices and price history, a current catalog price, an Associate draft, an operational note, a frozen History
/// PDF, sync state, and the local appearance / Personal Sync files beside the database. Never the real profile.
/// </summary>
internal static class Rc8ProfileBuilder
{
    public const int Rc8SchemaVersion = 16;

    public static readonly byte[] HistoryPdf = Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic frozen History PDF\n%%EOF");

    public const string AppearanceJson = "{ \"Appearance\": \"Dark\" }";

    public const string SyncConfigJson = "{ \"enabled\": true, \"folder\": \"D:\\\\Sync test\" }";

    public static SqliteConnectionFactory Create(string profileFolder)
    {
        Directory.CreateDirectory(profileFolder);
        var factory = new SqliteConnectionFactory(Path.Combine(profileFolder, "carowinds-passdown.db"));
        new MigrationRunner(factory, Phase26OfficialPriceHistorySyncTests.AllMigrations().Take(Rc8SchemaVersion).ToArray(), NullLogger<MigrationRunner>.Instance).Run();

        using (var connection = factory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO season_price_history (product_key, sequence, amount, price_kind, display_price, price_qualifier, display_name, source_name, first_observed_at_utc, last_confirmed_at_utc, origin)
                VALUES ('season-pass:gold:2027', 1, 99.0, 'StandardCatalog', '$99.00', NULL, 'Gold Season Pass — 2027', 'Carowinds Memberships & Season Passes', '2026-04-01T12:00:00.0000000+00:00', '2026-06-30T12:00:00.0000000+00:00', 'Backfilled'),
                       ('season-pass:gold:2027', 2, 104.0, 'StandardCatalog', '$104.00', NULL, 'Gold Season Pass — 2027', 'Carowinds Memberships & Season Passes', '2026-07-01T12:00:00.0000000+00:00', '2026-10-20T12:00:00.0000000+00:00', 'Observed');
                INSERT INTO catalog_products (product_key, category, display_name, lifecycle_state, first_seen_at_utc, last_seen_at_utc, last_verified_at_utc, current_price_amount, current_display_price, last_evidence_status, last_evidence_checked_at_utc, last_evidence_strategy, last_evidence_source_name, last_evidence_source_url, last_evidence_detail)
                VALUES ('season-pass:gold:2027', 'SeasonPass', 'Gold Season Pass — 2027', 'Active', '2026-04-01T12:00:00.0000000+00:00', '2026-10-20T12:00:00.0000000+00:00', '2026-10-20T12:00:00.0000000+00:00', 104.0, '$104.00', 'Verified', '2026-10-20T12:00:00.0000000+00:00', 'Http', 'Carowinds Memberships & Season Passes', 'https://www.sixflags.com/carowinds/memberships-and-season-passes', 'Verified');
                INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc)
                VALUES ('2026-10-21', 4, '{"note":"Unsent RC.8 draft text"}', '2026-10-21T18:00:00.0000000+00:00');
                INSERT INTO operational_notes (id, title, body, category, priority, effective_from, effective_through, status, created_at_utc, updated_at_utc, resolved_at_utc)
                VALUES ('aaaaaaaa-0000-0000-0000-000000000001', 'Gate 3 closed', 'Use Gate 2 until further notice', 'Operations', 'High', '2026-10-20', NULL, 'Active', '2026-10-20T10:00:00.0000000+00:00', '2026-10-20T10:00:00.0000000+00:00', NULL);
                INSERT INTO sync_entity_state (entity_type, entity_id, local_revision, synced_revision, conflict_state, updated_at_utc)
                VALUES ('OperationalNote', 'aaaaaaaa-0000-0000-0000-000000000001', 2, 2, 'None', '2026-10-20T10:05:00.0000000+00:00');
                """;
            command.ExecuteNonQuery();
        }

        using (var connection = factory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO finalized_passdowns (id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin, pdf_bytes, pdf_sha256, pdf_size_bytes)
                VALUES ('11111111-2222-3333-4444-555555555555', '2026-10-01', '2026-10-01T23:00:00.0000000+00:00', 2, '{}', 'hash', 'Local', $pdf, 'sha', $size);
                """;
            command.Parameters.AddWithValue("$pdf", HistoryPdf);
            command.Parameters.AddWithValue("$size", HistoryPdf.Length);
            command.ExecuteNonQuery();
        }

        File.WriteAllText(Path.Combine(profileFolder, "appearance.json"), AppearanceJson);
        File.WriteAllText(Path.Combine(profileFolder, "sync-config.json"), SyncConfigJson);
        SqliteConnection.ClearAllPools();
        return factory;
    }

    /// <summary>Every row of every table (minus <paramref name="excludedTables"/>) as text, in a stable order.</summary>
    public static string Dump(string databasePath, params string[] excludedTables)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var text = new StringBuilder();
        foreach (var table in tables.Except(excludedTables))
        {
            text.Append("## ").AppendLine(table);
            using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\";";
            using var reader = select.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                var row = new StringBuilder();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row.Append(value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                }

                rows.Add(row.ToString());
            }

            rows.Sort(StringComparer.Ordinal);
            rows.ForEach(row => text.AppendLine(row));
        }

        return text.ToString();
    }

    public static int SchemaVersion(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        return SqliteDatabaseBackup.ReadSchemaVersion(databasePath) ?? -1;
    }
}
