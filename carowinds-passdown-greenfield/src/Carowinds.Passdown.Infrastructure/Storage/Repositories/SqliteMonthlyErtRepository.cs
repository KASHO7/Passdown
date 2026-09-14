using System.Text.Json;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Ert;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="IMonthlyErtRepository"/> over <c>monthly_ert_configurations</c> (Phase
/// 17B spec Parts 19/20) — mirrors <see cref="SqliteOperationalNoteRepository"/>'s shape (a genuinely
/// mutable row, upsert-in-place, <see cref="DeleteAsync"/> reserved for the sync tombstone primitive).
/// </summary>
public sealed class SqliteMonthlyErtRepository(SqliteConnectionFactory connectionFactory) : IMonthlyErtRepository
{
    private const string SelectColumns = "year_month, year, month, state, start_time, rides_json, note, modified_utc";

    public async Task<MonthlyErtConfiguration?> GetAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM monthly_ert_configurations WHERE year_month = $yearMonth;";
        command.Parameters.AddWithValue("$yearMonth", MonthlyErtConfiguration.FormatKey(year, month));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<MonthlyErtConfiguration>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM monthly_ert_configurations ORDER BY year_month ASC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<MonthlyErtConfiguration>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(MonthlyErtConfiguration configuration, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        const string commandText =
            """
            INSERT INTO monthly_ert_configurations (year_month, year, month, state, start_time, rides_json, note, modified_utc)
            VALUES ($yearMonth, $year, $month, $state, $startTime, $ridesJson, $note, $modifiedUtc)
            ON CONFLICT(year_month) DO UPDATE SET
                state = excluded.state,
                start_time = excluded.start_time,
                rides_json = excluded.rides_json,
                note = excluded.note,
                modified_utc = excluded.modified_utc;
            """;

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            BindParameters(sharedCommand, configuration);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        BindParameters(command, configuration);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The sync engine's tombstone primitive (spec Part 23) — no product "delete" UI calls this; the product-facing "no ERT" is <see cref="ErtConfigurationState.NoErt"/>, not row absence.</summary>
    public async Task DeleteAsync(int year, int month, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        const string commandText = "DELETE FROM monthly_ert_configurations WHERE year_month = $yearMonth;";
        var yearMonth = MonthlyErtConfiguration.FormatKey(year, month);

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            sharedCommand.Parameters.AddWithValue("$yearMonth", yearMonth);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$yearMonth", yearMonth);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindParameters(SqliteCommand command, MonthlyErtConfiguration configuration)
    {
        command.Parameters.AddWithValue("$yearMonth", configuration.YearMonthKey);
        command.Parameters.AddWithValue("$year", configuration.Year);
        command.Parameters.AddWithValue("$month", configuration.Month);
        command.Parameters.AddWithValue("$state", configuration.State.ToString());
        command.Parameters.AddWithValue("$startTime", SqliteConvert.ToParameter(configuration.StartTime));
        command.Parameters.AddWithValue("$ridesJson", JsonSerializer.Serialize(configuration.Rides));
        command.Parameters.AddWithValue("$note", SqliteConvert.ToParameter(configuration.Note));
        command.Parameters.AddWithValue("$modifiedUtc", SqliteConvert.ToText(configuration.ModifiedUtc));
    }

    private static MonthlyErtConfiguration Map(SqliteDataReader reader) => new(
        reader.GetInt32(1),
        reader.GetInt32(2),
        Enum.Parse<ErtConfigurationState>(reader.GetString(3)),
        SqliteConvert.ReadNullableTimeOnly(reader, 4),
        JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? [],
        SqliteConvert.ReadNullableString(reader, 6),
        SqliteConvert.ReadDateTimeOffset(reader, 7));
}
