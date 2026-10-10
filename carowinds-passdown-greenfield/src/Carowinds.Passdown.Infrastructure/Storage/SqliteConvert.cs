using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// Consistent, round-trip text encoding for values stored across the Phase 2
/// tables (spec §6.2's "models are versioned" / storage consistency intent).
/// Every timestamp is stored as UTC ISO-8601 text, matching the convention
/// <see cref="MigrationRunner"/> already uses for <c>schema_version.applied_at_utc</c>.
/// Every date-only value is stored as an ISO <c>yyyy-MM-dd</c> string.
/// </summary>
internal static class SqliteConvert
{
    public static string ToText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static object ToParameter(DateTimeOffset? value) =>
        value is { } present ? ToText(present) : DBNull.Value;

    public static DateTimeOffset ToDateTimeOffset(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ToDateTimeOffset(reader.GetString(ordinal));

    public static DateTimeOffset ReadDateTimeOffset(SqliteDataReader reader, int ordinal) =>
        ToDateTimeOffset(reader.GetString(ordinal));

    public static string ToText(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static object ToParameter(DateOnly? value) =>
        value is { } present ? ToText(present) : DBNull.Value;

    public static DateOnly ToDateOnly(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? ReadNullableDateOnly(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ToDateOnly(reader.GetString(ordinal));

    public static DateOnly ReadDateOnly(SqliteDataReader reader, int ordinal) => ToDateOnly(reader.GetString(ordinal));

    public static string ToText(TimeOnly value) => value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static object ToParameter(TimeOnly? value) =>
        value is { } present ? ToText(present) : DBNull.Value;

    public static TimeOnly ToTimeOnly(string text) => TimeOnly.ParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture);

    public static TimeOnly? ReadNullableTimeOnly(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ToTimeOnly(reader.GetString(ordinal));

    public static string? ReadNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    public static object ToParameter(string? value) => (object?)value ?? DBNull.Value;

    public static object ToParameter(decimal? value) => value is { } present ? present : DBNull.Value;

    public static object ToParameter(bool value) => value ? 1 : 0;

    public static bool ReadBool(SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal) != 0;
}
