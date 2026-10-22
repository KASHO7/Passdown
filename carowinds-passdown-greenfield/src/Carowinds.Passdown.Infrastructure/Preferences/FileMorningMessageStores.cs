using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 29: <see cref="IMorningMessagePreferenceStore"/> backed by one small local JSON file
/// (<c>morning-message.json</c>) under <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same
/// flat-file convention and atomic writer as the appearance and PDF-template preferences; no SQLite table,
/// no migration, never inside the Personal Sync folder (Phase 30 sync publishes copies of its values only
/// when Sync Morning Messages is on). Enums are stored by name. A missing, unreadable or
/// malformed file means <see cref="MorningMessagePreferences.Default"/>, with one non-sensitive warning.
/// </summary>
public sealed class FileMorningMessagePreferenceStore(string configFilePath, ILogger<FileMorningMessagePreferenceStore>? logger = null) : IMorningMessagePreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "morning-message.json");

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public string ConfigFilePath { get; } = configFilePath;

    public MorningMessagePreferences Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return MorningMessagePreferences.Default;
        }

        try
        {
            var file = JsonSerializer.Deserialize<PreferenceFile>(File.ReadAllText(ConfigFilePath), Options);
            if (file is null)
            {
                return MorningMessagePreferences.Default;
            }

            return MorningMessagePreferences.Sanitize(
                file.Greeting,
                file.Sections?.Select(section => new MorningMessageSectionSetting(section.Id, section.Enabled)),
                file.DefaultCopyMode,
                file.PriceSections,
                file.RemindersAsBullets);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Morning message preferences could not be read ({ExceptionType}); using defaults.", ex.GetType().Name);
            return MorningMessagePreferences.Default;
        }
    }

    public async Task SaveAsync(MorningMessagePreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var file = new PreferenceFile(
            preferences.Greeting,
            preferences.Layout.Sections.Select(section => new SectionEntry(section.Id, section.Enabled)).ToList(),
            preferences.DefaultCopyMode,
            preferences.PriceSections.ToList(),
            preferences.RemindersAsBullets);

        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, JsonSerializer.Serialize(file, Options), cancellationToken).ConfigureAwait(false);
    }

    private sealed record SectionEntry(MorningMessageSectionId Id, bool Enabled);

    private sealed record PreferenceFile(
        string? Greeting,
        List<SectionEntry>? Sections,
        TeamsCopyMode DefaultCopyMode,
        List<ProductDisplaySection>? PriceSections,
        bool RemindersAsBullets);
}

/// <summary>
/// Phase 29: <see cref="IMorningMessageContentStore"/> backed by one local JSON file
/// (<c>morning-message-content.json</c>) holding each recent date's message-only text, keyed by ISO date.
/// Kept outside the database deliberately: this is composer scratch content that must never reach the
/// Associate draft, the PDF or History, and storing it this way needs no schema change
/// (docs/phase29-morning-message-teams-copy.md). Phase 30 Morning Message sync reads and writes it only when
/// Sync Morning Messages is on. Only the <see cref="RetainedDates"/> most recent dates are kept. A malformed
/// file reads as empty and is replaced by the next save.
/// </summary>
public sealed class FileMorningMessageContentStore(string filePath, ILogger<FileMorningMessageContentStore>? logger = null) : IMorningMessageContentStore
{
    public const int RetainedDates = 60;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "morning-message-content.json");

    public string FilePath { get; } = filePath;

    public async Task<MorningMessageContent> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return entries.TryGetValue(Key(operatingDate), out var content) && content is not null
            ? content.Sanitized()
            : MorningMessageContent.Empty;
    }

    public async Task<(DateOnly Date, string Reminders)?> FindPreviousRemindersAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var match = entries
            .Select(entry => (Ok: DateOnly.TryParseExact(entry.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date), Date: date, entry.Value))
            .Where(entry => entry.Ok && entry.Date < operatingDate && !string.IsNullOrWhiteSpace(entry.Value?.Reminders))
            .OrderByDescending(entry => entry.Date)
            .FirstOrDefault();

        return match.Ok ? (match.Date, match.Value!.Reminders) : null;
    }

    public async Task SaveAsync(DateOnly operatingDate, MorningMessageContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var key = Key(operatingDate);
            if (content.IsEmpty && !entries.ContainsKey(key))
            {
                return; // nothing was ever written for this date — opening it must not create an entry
            }

            // Phase 30: clearing a date keeps an empty entry, so Morning Message sync can tell "cleared on
            // this computer" from "never written" or "aged out".
            entries[key] = content.IsEmpty ? MorningMessageContent.Empty : content.Sanitized();
            await WriteRetainedAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<DateOnly, MorningMessageContent>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<DateOnly, MorningMessageContent>();
        foreach (var (key, content) in entries)
        {
            if (DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                result[date] = (content ?? MorningMessageContent.Empty).Sanitized();
            }
        }

        return result;
    }

    public async Task SaveManyAsync(IReadOnlyDictionary<DateOnly, MorningMessageContent> contents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Sync never replaces a local file it could not read: that text may still be recoverable.
            var entries = await ReadAsync(cancellationToken, throwIfUnreadable: true).ConfigureAwait(false);
            foreach (var (date, content) in contents)
            {
                entries[Key(date)] = content.IsEmpty ? MorningMessageContent.Empty : content.Sanitized();
            }

            await WriteRetainedAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Keeps the <see cref="RetainedDates"/> most recent dates and replaces the file in one atomic write.</summary>
    private Task WriteRetainedAsync(Dictionary<string, MorningMessageContent?> entries, CancellationToken cancellationToken)
    {
        var retained = entries
            .OrderByDescending(entry => entry.Key, StringComparer.Ordinal)
            .Take(RetainedDates)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        var json = JsonSerializer.Serialize(new ContentFile(retained), FileMorningMessagePreferenceStore.Options);
        return AtomicFileWriter.WriteTextAsync(FilePath, json, cancellationToken);
    }

    private async Task<Dictionary<string, MorningMessageContent?>> ReadAsync(CancellationToken cancellationToken, bool throwIfUnreadable = false)
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<string, MorningMessageContent?>(StringComparer.Ordinal);
        }

        try
        {
            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<ContentFile>(json, FileMorningMessagePreferenceStore.Options);
            return new Dictionary<string, MorningMessageContent?>(file?.Dates ?? [], StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (throwIfUnreadable)
            {
                throw new IOException("The morning message file on this computer could not be read, so it was not replaced.", ex);
            }

            logger?.LogWarning("Morning message content could not be read ({ExceptionType}); starting empty.", ex.GetType().Name);
            return new Dictionary<string, MorningMessageContent?>(StringComparer.Ordinal);
        }
    }

    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record ContentFile(Dictionary<string, MorningMessageContent?>? Dates);
}
