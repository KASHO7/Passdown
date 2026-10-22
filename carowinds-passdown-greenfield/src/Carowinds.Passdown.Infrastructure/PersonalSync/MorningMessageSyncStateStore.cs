using System.Text.Json;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>A conflict waiting for review: the other computer's version of one field, kept until the user chooses.</summary>
public sealed record MorningMessageStoredConflict(DateOnly? Date, string Field, MorningMessageFieldVersion Other, DateTimeOffset DetectedUtc);

/// <summary>
/// Phase 30: this computer's Morning Message sync bookkeeping — per synchronized field, the hash and version
/// of the value at its last pass; this computer's edit counter; pending conflicts (with the other computer's
/// text preserved); and the last pass's outcome for the status line. Device state: lives in
/// <c>%LOCALAPPDATA%\Carowinds Passdown</c>, never in the shared folder, and survives turning the option off,
/// which is what lets a re-enabled computer recognize edits it made while sync was off.
/// </summary>
public sealed class MorningMessageSyncState
{
    public long Counter { get; set; }

    public Dictionary<string, MorningMessageFieldState> Preferences { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<DateOnly, Dictionary<string, MorningMessageFieldState>> Dates { get; set; } = [];

    public List<MorningMessageStoredConflict> Conflicts { get; set; } = [];

    public DateTimeOffset? LastSyncedUtc { get; set; }

    /// <summary>The last attempt could not reach or write the shared folder.</summary>
    public bool LastPassFailed { get; set; }

    /// <summary>Changes held back by the last pass because the composer was open.</summary>
    public int DeferredCount { get; set; }

    public long NextCounter() => ++Counter;

    public MorningMessageFieldState? Get(DateOnly? date, string field) =>
        date is { } d
            ? Dates.TryGetValue(d, out var fields) && fields.TryGetValue(field, out var state) ? state : null
            : Preferences.TryGetValue(field, out var preference) ? preference : null;

    public void Set(DateOnly? date, string field, MorningMessageFieldState? state)
    {
        var fields = date is { } d
            ? Dates.TryGetValue(d, out var existing) ? existing : Dates[d] = new Dictionary<string, MorningMessageFieldState>(StringComparer.Ordinal)
            : Preferences;

        if (state is null)
        {
            fields.Remove(field);
        }
        else
        {
            fields[field] = state;
        }

        if (date is { } key && fields.Count == 0)
        {
            Dates.Remove(key);
        }
    }

    public MorningMessageStoredConflict? GetConflict(DateOnly? date, string field) =>
        Conflicts.FirstOrDefault(conflict => conflict.Date == date && conflict.Field == field);

    public void SetConflict(DateOnly? date, string field, MorningMessageFieldVersion? other, DateTimeOffset nowUtc)
    {
        var existing = GetConflict(date, field);
        Conflicts.RemoveAll(conflict => conflict.Date == date && conflict.Field == field);
        if (other is not null)
        {
            // The first detection time is kept while the same conflict stays open.
            Conflicts.Add(new MorningMessageStoredConflict(date, field, other, existing?.DetectedUtc ?? nowUtc));
        }
    }
}

/// <summary>Phase 30: local JSON persistence for <see cref="MorningMessageSyncState"/>, written with <see cref="AtomicFileWriter"/>.</summary>
public sealed class MorningMessageSyncStateStore(string filePath, ILogger<MorningMessageSyncStateStore>? logger = null)
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "morning-message-sync-state.json");

    public const int FormatVersion = 1;

    public string FilePath { get; } = filePath;

    /// <summary>
    /// A missing file is a fresh start. An unreadable or newer-format file is also treated as a fresh start —
    /// safe, because without remembered versions every difference is reported as a conflict rather than
    /// resolved — and is replaced by the next completed pass.
    /// </summary>
    public async Task<MorningMessageSyncState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new MorningMessageSyncState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<StateFile>(json, MorningMessageSyncFileSerializer.Options);
            if (file is null || file.FormatVersion != FormatVersion)
            {
                logger?.LogWarning("Morning Message sync state has an unsupported format; starting fresh.");
                return new MorningMessageSyncState();
            }

            return FromFile(file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            logger?.LogWarning("Morning Message sync state could not be read ({ExceptionType}); starting fresh.", ex.GetType().Name);
            return new MorningMessageSyncState();
        }
    }

    public Task SaveAsync(MorningMessageSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.Serialize(ToFile(state), MorningMessageSyncFileSerializer.Options);
        return AtomicFileWriter.WriteTextAsync(FilePath, json, cancellationToken);
    }

    private static StateFile ToFile(MorningMessageSyncState state) => new(
        FormatVersion,
        state.Counter,
        state.Preferences.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => ToEntry(e.Value), StringComparer.Ordinal),
        state.Dates.OrderBy(e => e.Key).ToDictionary(
            e => MorningMessageSyncFileSerializer.FormatDateKey(e.Key),
            e => e.Value.OrderBy(f => f.Key, StringComparer.Ordinal).ToDictionary(f => f.Key, f => ToEntry(f.Value), StringComparer.Ordinal),
            StringComparer.Ordinal),
        state.Conflicts.Select(c => new ConflictEntry(
            c.Date is { } d ? MorningMessageSyncFileSerializer.FormatDateKey(d) : null,
            c.Field,
            new MorningMessageSyncFileField(c.Other.Value, MorningMessageSyncFileSerializer.ToJson(c.Other.Version)),
            c.DetectedUtc)).ToList(),
        state.LastSyncedUtc,
        state.LastPassFailed,
        state.DeferredCount);

    private static StateEntry ToEntry(MorningMessageFieldState state) => new(state.Hash, MorningMessageSyncFileSerializer.ToJson(state.Version));

    private static MorningMessageSyncState FromFile(StateFile file)
    {
        var state = new MorningMessageSyncState
        {
            Counter = Math.Max(0, file.Counter),
            LastSyncedUtc = file.LastSyncedUtc,
            LastPassFailed = file.LastPassFailed,
            DeferredCount = Math.Max(0, file.DeferredCount),
        };

        foreach (var (field, entry) in file.Preferences ?? [])
        {
            if (FromEntry(entry) is { } parsed)
            {
                state.Preferences[field] = parsed;
            }
        }

        foreach (var (key, fields) in file.Dates ?? [])
        {
            if (!MorningMessageSyncFileSerializer.TryParseDateKey(key, out var date))
            {
                continue;
            }

            foreach (var (field, entry) in fields ?? [])
            {
                if (FromEntry(entry) is { } parsed)
                {
                    state.Set(date, field, parsed);
                }
            }
        }

        foreach (var conflict in file.Conflicts ?? [])
        {
            DateOnly? date = null;
            if (conflict?.Date is { } key)
            {
                if (!MorningMessageSyncFileSerializer.TryParseDateKey(key, out var parsedDate))
                {
                    continue;
                }

                date = parsedDate;
            }

            if (conflict?.Field is { } field && MorningMessageSyncFileSerializer.FromJson(conflict.Other?.Version) is { } version
                && MorningMessageSyncFields.IsValidValue(field, conflict.Other?.Value))
            {
                state.Conflicts.Add(new MorningMessageStoredConflict(date, field, new MorningMessageFieldVersion(conflict.Other!.Value!, version), conflict.DetectedUtc));
            }
        }

        return state;
    }

    private static MorningMessageFieldState? FromEntry(StateEntry? entry) =>
        entry?.Hash is { Length: 64 } hash && MorningMessageSyncFileSerializer.FromJson(entry.Version) is { } version
            ? new MorningMessageFieldState(hash, version)
            : null;

    private sealed record StateEntry(string? Hash, Dictionary<string, long>? Version);

    private sealed record ConflictEntry(string? Date, string? Field, MorningMessageSyncFileField? Other, DateTimeOffset DetectedUtc);

    private sealed record StateFile(
        int FormatVersion,
        long Counter,
        Dictionary<string, StateEntry>? Preferences,
        Dictionary<string, Dictionary<string, StateEntry>>? Dates,
        List<ConflictEntry>? Conflicts,
        DateTimeOffset? LastSyncedUtc,
        bool LastPassFailed,
        int DeferredCount);
}
