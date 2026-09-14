using System.Text.Json;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Ert;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="IErtDateOverrideRepository"/> over <c>ert_date_overrides</c> (Phase 17B
/// spec Parts 19/20) — mirrors <see cref="SqliteMonthlyErtRepository"/>, keyed by
/// <see cref="ErtDateOverride.OperatingDate"/> instead of a year/month pair.
/// </summary>
public sealed class SqliteErtDateOverrideRepository(SqliteConnectionFactory connectionFactory) : IErtDateOverrideRepository
{
    private const string SelectColumns = "operating_date, state, start_time, rides_json, reason, modified_utc";

    public async Task<ErtDateOverride?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ert_date_overrides WHERE operating_date = $operatingDate;";
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(operatingDate));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ErtDateOverride>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ert_date_overrides ORDER BY operating_date ASC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ErtDateOverride>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(ErtDateOverride dateOverride, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dateOverride);

        const string commandText =
            """
            INSERT INTO ert_date_overrides (operating_date, state, start_time, rides_json, reason, modified_utc)
            VALUES ($operatingDate, $state, $startTime, $ridesJson, $reason, $modifiedUtc)
            ON CONFLICT(operating_date) DO UPDATE SET
                state = excluded.state,
                start_time = excluded.start_time,
                rides_json = excluded.rides_json,
                reason = excluded.reason,
                modified_utc = excluded.modified_utc;
            """;

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            BindParameters(sharedCommand, dateOverride);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        BindParameters(command, dateOverride);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The sync engine's tombstone primitive (spec Part 23/28) — also what a user-initiated "Remove override" action calls, since removing an override is a genuine deletion (falls back to the monthly default), not a state toggle.</summary>
    public async Task DeleteAsync(DateOnly operatingDate, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        const string commandText = "DELETE FROM ert_date_overrides WHERE operating_date = $operatingDate;";
        var operatingDateText = SqliteConvert.ToText(operatingDate);

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            sharedCommand.Parameters.AddWithValue("$operatingDate", operatingDateText);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$operatingDate", operatingDateText);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindParameters(SqliteCommand command, ErtDateOverride dateOverride)
    {
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(dateOverride.OperatingDate));
        command.Parameters.AddWithValue("$state", dateOverride.State.ToString());
        command.Parameters.AddWithValue("$startTime", SqliteConvert.ToParameter(dateOverride.StartTime));
        command.Parameters.AddWithValue("$ridesJson", JsonSerializer.Serialize(dateOverride.Rides));
        command.Parameters.AddWithValue("$reason", SqliteConvert.ToParameter(dateOverride.Reason));
        command.Parameters.AddWithValue("$modifiedUtc", SqliteConvert.ToText(dateOverride.ModifiedUtc));
    }

    private static ErtDateOverride Map(SqliteDataReader reader) => new(
        SqliteConvert.ReadDateOnly(reader, 0),
        Enum.Parse<ErtConfigurationState>(reader.GetString(1)),
        SqliteConvert.ReadNullableTimeOnly(reader, 2),
        JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
        SqliteConvert.ReadNullableString(reader, 4),
        SqliteConvert.ReadDateTimeOffset(reader, 5));
}
