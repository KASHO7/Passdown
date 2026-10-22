using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.MorningMessage.Sync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>One synchronized value as stored in a shared file: its text and its version vector (device id → edit counter).</summary>
public sealed record MorningMessageSyncFileField(string? Value, Dictionary<string, long>? Version);

/// <summary>
/// Phase 30: the shared file each computer writes at <c>morning-message/&lt;device-id&gt;.json</c>. Not a
/// <c>SyncEnvelope</c>: an independently versioned format (<see cref="MorningMessageSyncFileSerializer.FormatVersion"/>)
/// that leaves <c>SyncSchemaVersion</c> 1 and every existing folder untouched. Holds only Morning Message
/// content and preferences (<see cref="MorningMessageSyncFields"/>) — no rendered message, no official data,
/// no local image path, no user or machine name; the device id is the existing anonymous Personal Sync id.
/// </summary>
public sealed record MorningMessageSyncFile(
    string? Format,
    int FormatVersion,
    string? DeviceId,
    Dictionary<string, MorningMessageSyncFileField>? Preferences,
    Dictionary<string, Dictionary<string, MorningMessageSyncFileField>>? Dates);

/// <summary>A shared file after validation: only values that passed every check.</summary>
public sealed record MorningMessageSyncFileContent(
    Guid DeviceId,
    IReadOnlyDictionary<string, MorningMessageFieldVersion> Preferences,
    IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<string, MorningMessageFieldVersion>> Dates,
    int RejectedValues);

public static class MorningMessageSyncFileSerializer
{
    public const string FormatName = "carowinds-morning-message";

    public const int FormatVersion = 1;

    /// <summary>Generous for 120 dates of long text; anything larger is skipped unread.</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    /// <summary>Twice the 60 dates a computer keeps.</summary>
    public const int MaxDatesPerFile = 120;

    /// <summary>Shared files read per pass — one per computer, with room for sync-provider conflict copies.</summary>
    public const int MaxFilesPerPass = 64;

    /// <summary>A file with more bad values than this is not trusted at all.</summary>
    public const int MaxRejectedValuesPerFile = 50;

    private static readonly DateOnly EarliestDate = new(2000, 1, 1);
    private static readonly DateOnly LatestDate = new(2100, 12, 31);

    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        MaxDepth = 16,
    };

    public static bool TryParseDateKey(string key, out DateOnly date) =>
        DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date >= EarliestDate && date <= LatestDate;

    public static string FormatDateKey(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Deterministic text: sorted dates, fields and devices, so an unchanged state is never rewritten.</summary>
    public static string Serialize(
        Guid deviceId,
        IReadOnlyDictionary<string, MorningMessageFieldVersion> preferences,
        IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<string, MorningMessageFieldVersion>> dates)
    {
        static Dictionary<string, MorningMessageSyncFileField> Fields(IReadOnlyDictionary<string, MorningMessageFieldVersion> fields) =>
            fields.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => new MorningMessageSyncFileField(entry.Value.Value, ToJson(entry.Value.Version)), StringComparer.Ordinal);

        var file = new MorningMessageSyncFile(
            FormatName,
            FormatVersion,
            deviceId.ToString("D", CultureInfo.InvariantCulture),
            Fields(preferences),
            dates.Where(entry => entry.Value.Count > 0)
                .OrderBy(entry => entry.Key)
                .ToDictionary(entry => FormatDateKey(entry.Key), entry => Fields(entry.Value), StringComparer.Ordinal));

        return JsonSerializer.Serialize(file, Options);
    }

    public static Dictionary<string, long> ToJson(MorningMessageVersion version) =>
        version.Counters.ToDictionary(entry => entry.Key.ToString("D", CultureInfo.InvariantCulture), entry => entry.Value, StringComparer.Ordinal);

    public static MorningMessageVersion? FromJson(Dictionary<string, long>? version)
    {
        if (version is null || version.Count == 0 || version.Count > MorningMessageVersion.MaxEntries)
        {
            return null;
        }

        var parsed = new List<KeyValuePair<Guid, long>>();
        foreach (var (key, counter) in version)
        {
            if (!Guid.TryParseExact(key, "D", out var device))
            {
                return null;
            }

            parsed.Add(new KeyValuePair<Guid, long>(device, counter));
        }

        return MorningMessageVersion.TryCreate(parsed);
    }

    public enum ReadProblem
    {
        None,
        NotThisFormat,
        UnsupportedVersion,
        WrongDevice,
        TooLarge,
        TooManyInvalidValues,
    }

    /// <summary>
    /// Validates a deserialized file. Whole-file problems (another format, a newer version, a device id that
    /// does not match the file name, too many dates, too many bad values) reject the file; an individual bad
    /// date key, unknown field, over-long text or invalid version drops just that value.
    /// </summary>
    public static (MorningMessageSyncFileContent? Content, ReadProblem Problem) Validate(MorningMessageSyncFile? file, Guid expectedDeviceId)
    {
        if (file is null || !string.Equals(file.Format, FormatName, StringComparison.Ordinal))
        {
            return (null, ReadProblem.NotThisFormat);
        }

        if (file.FormatVersion != FormatVersion)
        {
            return (null, file.FormatVersion > FormatVersion ? ReadProblem.UnsupportedVersion : ReadProblem.NotThisFormat);
        }

        if (!Guid.TryParseExact(file.DeviceId, "D", out var deviceId) || deviceId == Guid.Empty || deviceId != expectedDeviceId)
        {
            return (null, ReadProblem.WrongDevice);
        }

        if (file.Dates is { Count: > MaxDatesPerFile })
        {
            return (null, ReadProblem.TooLarge);
        }

        var rejected = 0;
        var preferences = ReadFields(file.Preferences, MorningMessageSyncFields.IsPreferenceField, ref rejected);

        var dates = new Dictionary<DateOnly, IReadOnlyDictionary<string, MorningMessageFieldVersion>>();
        foreach (var (key, fields) in file.Dates ?? [])
        {
            if (!TryParseDateKey(key, out var date))
            {
                rejected++;
                continue;
            }

            var valid = ReadFields(fields, MorningMessageSyncFields.IsContentField, ref rejected);
            if (valid.Count > 0)
            {
                dates[date] = valid;
            }
        }

        if (rejected > MaxRejectedValuesPerFile)
        {
            return (null, ReadProblem.TooManyInvalidValues);
        }

        return (new MorningMessageSyncFileContent(deviceId, preferences, dates, rejected), ReadProblem.None);
    }

    private static Dictionary<string, MorningMessageFieldVersion> ReadFields(
        Dictionary<string, MorningMessageSyncFileField>? fields, Func<string, bool> isKnownField, ref int rejected)
    {
        var result = new Dictionary<string, MorningMessageFieldVersion>(StringComparer.Ordinal);
        foreach (var (name, field) in fields ?? [])
        {
            if (!isKnownField(name))
            {
                continue; // a field a newer version added — ignored, never an error
            }

            var version = FromJson(field?.Version);
            if (field is null || version is null || !MorningMessageSyncFields.IsValidValue(name, field.Value))
            {
                rejected++;
                continue;
            }

            result[name] = new MorningMessageFieldVersion(field.Value!, version);
        }

        return result;
    }
}
