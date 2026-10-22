using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Application.MorningMessage;

/// <summary>
/// Phase 29: the user's remembered morning-message choices on this computer — greeting, section order and
/// visibility, the default copy mode, which official price sections the message's Ticket Prices uses, and
/// whether reminders are bulleted. A device preference like the PDF template and appearance: kept in one
/// small local file, never in the database and never part of Personal Sync (docs/phase29-morning-message-teams-copy.md).
/// </summary>
public sealed record MorningMessagePreferences(
    string Greeting,
    MorningMessageLayout Layout,
    TeamsCopyMode DefaultCopyMode,
    IReadOnlyList<ProductDisplaySection> PriceSections,
    bool RemindersAsBullets)
{
    public const string DefaultGreeting = "Good Morning!";

    /// <summary>Longest greeting kept; anything longer is trimmed when saved.</summary>
    public const int MaxGreetingLength = 200;

    /// <summary>Daily Admission, Fast Lane and Promotions — the everyday prices. Season Passes, Add-Ons and SCarowinds are opt-in.</summary>
    public static IReadOnlyList<ProductDisplaySection> DefaultPriceSections { get; } =
        [ProductDisplaySection.DailyAdmission, ProductDisplaySection.FastLane, ProductDisplaySection.Promotions];

    /// <summary>The official price sections the message can offer. Memberships are outside the Associate export contract (Phase 25).</summary>
    public static IReadOnlyList<ProductDisplaySection> SelectablePriceSections { get; } =
    [
        ProductDisplaySection.DailyAdmission,
        ProductDisplaySection.FastLane,
        ProductDisplaySection.Promotions,
        ProductDisplaySection.SeasonPasses,
        ProductDisplaySection.AddOns,
        ProductDisplaySection.SCarowinds,
    ];

    /// <summary>
    /// Plain text stays the default copy mode until formatted pasting has been validated in Teams on
    /// Windows (Phase 29 Part H); the user can switch it in the composer.
    /// </summary>
    public static MorningMessagePreferences Default { get; } = new(
        DefaultGreeting,
        MorningMessageLayout.Default,
        TeamsCopyMode.PlainText,
        DefaultPriceSections,
        RemindersAsBullets: false);

    /// <summary>A stored value made safe: a blank greeting is allowed (the greeting section is then empty), unknown price sections and copy modes are dropped.</summary>
    public static MorningMessagePreferences Sanitize(
        string? greeting,
        IEnumerable<MorningMessageSectionSetting>? sections,
        TeamsCopyMode copyMode,
        IEnumerable<ProductDisplaySection>? priceSections,
        bool remindersAsBullets)
    {
        var cleanGreeting = (greeting ?? DefaultGreeting).Trim();
        if (cleanGreeting.Length > MaxGreetingLength)
        {
            cleanGreeting = cleanGreeting[..MaxGreetingLength];
        }

        return new MorningMessagePreferences(
            cleanGreeting,
            MorningMessageLayout.Normalize(sections),
            Enum.IsDefined(copyMode) ? copyMode : TeamsCopyMode.PlainText,
            priceSections is null
                ? DefaultPriceSections
                : SelectablePriceSections.Where(section => priceSections.Contains(section)).ToList(),
            remindersAsBullets);
    }

    public bool Equals(MorningMessagePreferences? other) =>
        other is not null
        && Greeting == other.Greeting
        && Layout.Equals(other.Layout)
        && DefaultCopyMode == other.DefaultCopyMode
        && PriceSections.SequenceEqual(other.PriceSections)
        && RemindersAsBullets == other.RemindersAsBullets;

    public override int GetHashCode() => HashCode.Combine(Greeting, Layout, DefaultCopyMode, PriceSections.Count, RemindersAsBullets);
}

/// <summary>
/// Phase 29: what the user types in the composer for one operating date. Message-only content: it never
/// changes the Associate draft, the PDF, History or any official value, and it is kept in a local file on
/// this computer (not synced). <see cref="DapImagePath"/> is a local file the user chose; nothing is ever
/// downloaded.
/// </summary>
public sealed record MorningMessageContent(
    string Reminders,
    string BreakPlan,
    string DapPlacement,
    bool DapImageReminder,
    string? DapImagePath,
    string OdNotes,
    string PriceNote)
{
    /// <summary>Longest text kept per field — far beyond any real message, small enough to keep the local file tiny.</summary>
    public const int MaxFieldLength = 20000;

    public static MorningMessageContent Empty { get; } = new(string.Empty, string.Empty, string.Empty, false, null, string.Empty, string.Empty);

    public bool IsEmpty => this == Empty || (string.IsNullOrWhiteSpace(Reminders) && string.IsNullOrWhiteSpace(BreakPlan)
        && string.IsNullOrWhiteSpace(DapPlacement) && !DapImageReminder && string.IsNullOrWhiteSpace(DapImagePath)
        && string.IsNullOrWhiteSpace(OdNotes) && string.IsNullOrWhiteSpace(PriceNote));

    /// <summary>Null fields become empty and over-long fields are cut, so a hand-edited file can never break the composer.</summary>
    public MorningMessageContent Sanitized() => new(
        Clip(Reminders), Clip(BreakPlan), Clip(DapPlacement), DapImageReminder,
        string.IsNullOrWhiteSpace(DapImagePath) ? null : DapImagePath.Trim(),
        Clip(OdNotes), Clip(PriceNote));

    private static string Clip(string? value) => value is null ? string.Empty : value.Length > MaxFieldLength ? value[..MaxFieldLength] : value;
}

/// <summary>Phase 29: local persistence for <see cref="MorningMessagePreferences"/>. Never synced.</summary>
public interface IMorningMessagePreferenceStore
{
    /// <summary>Never throws: a missing, unreadable or malformed file returns <see cref="MorningMessagePreferences.Default"/>.</summary>
    MorningMessagePreferences Load();

    Task SaveAsync(MorningMessagePreferences preferences, CancellationToken cancellationToken = default);
}

/// <summary>Phase 29: local persistence for each date's <see cref="MorningMessageContent"/>. Never synced, never in the database.</summary>
public interface IMorningMessageContentStore
{
    /// <summary>Never throws for a missing/unreadable file: returns <see cref="MorningMessageContent.Empty"/>.</summary>
    Task<MorningMessageContent> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    /// <summary>The most recent earlier date with non-blank reminders, for the explicit "Reuse last reminders" action; null when none.</summary>
    Task<(DateOnly Date, string Reminders)?> FindPreviousRemindersAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    Task SaveAsync(DateOnly operatingDate, MorningMessageContent content, CancellationToken cancellationToken = default);
}

/// <summary>Phase 29: process-only preference store — the default when none is supplied (tests, composition-free construction), so nothing touches the real profile.</summary>
public sealed class InMemoryMorningMessagePreferenceStore(MorningMessagePreferences? initial = null) : IMorningMessagePreferenceStore
{
    public MorningMessagePreferences Current { get; private set; } = initial ?? MorningMessagePreferences.Default;

    public int SaveCount { get; private set; }

    public MorningMessagePreferences Load() => Current;

    public Task SaveAsync(MorningMessagePreferences preferences, CancellationToken cancellationToken = default)
    {
        Current = preferences;
        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Phase 29: process-only content store — the default when none is supplied.</summary>
public sealed class InMemoryMorningMessageContentStore : IMorningMessageContentStore
{
    private readonly Dictionary<DateOnly, MorningMessageContent> _entries = [];

    public int SaveCount { get; private set; }

    public Task<MorningMessageContent> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.TryGetValue(operatingDate, out var content) ? content : MorningMessageContent.Empty);

    public Task<(DateOnly Date, string Reminders)?> FindPreviousRemindersAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var match = _entries.Where(entry => entry.Key < operatingDate && !string.IsNullOrWhiteSpace(entry.Value.Reminders))
            .OrderByDescending(entry => entry.Key)
            .Select(entry => ((DateOnly Date, string Reminders)?)(entry.Key, entry.Value.Reminders))
            .FirstOrDefault();
        return Task.FromResult(match);
    }

    public Task SaveAsync(DateOnly operatingDate, MorningMessageContent content, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            _entries.Remove(operatingDate);
        }
        else
        {
            _entries[operatingDate] = content.Sanitized();
        }

        SaveCount++;
        return Task.CompletedTask;
    }
}
