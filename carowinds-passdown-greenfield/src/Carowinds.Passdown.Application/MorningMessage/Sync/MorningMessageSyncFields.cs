using System.Security.Cryptography;
using System.Text;
using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Application.MorningMessage.Sync;

/// <summary>
/// Phase 30: which Morning Message values synchronize, and their one canonical text form. Every synchronized
/// value is a string so one reconciliation rule (<see cref="MorningMessageFieldReconciliation"/>) serves them
/// all and the shared file stays readable in Notepad.
/// <para>
/// Daily content synchronizes everything typed in the composer for a date except
/// <see cref="MorningMessageContent.DapImagePath"/>, which is a file on this computer (a path is meaningless,
/// and possibly personal, on another computer). Preferences synchronize the greeting, section order and
/// visibility, the message's price sections, reminders-as-bullets and the default copy mode. Nothing here is
/// official data: hours, attendance, pre-solds, prices and the Daily Feature are always read on each computer
/// from their own sources, and no rendered message is ever synchronized.
/// </para>
/// Field names are persisted in the shared file, so they may be added but never renamed.
/// </summary>
public static class MorningMessageSyncFields
{
    public const string Reminders = "Reminders";
    public const string BreakPlan = "BreakPlan";
    public const string DapPlacement = "DapPlacement";
    public const string DapImageReminder = "DapImageReminder";
    public const string OdNotes = "OdNotes";
    public const string PriceNote = "PriceNote";

    public const string Greeting = "Greeting";
    public const string Sections = "Sections";
    public const string PriceSections = "PriceSections";
    public const string RemindersAsBullets = "RemindersAsBullets";
    public const string DefaultCopyMode = "DefaultCopyMode";

    public static IReadOnlyList<string> ContentFields { get; } = [Reminders, BreakPlan, DapPlacement, DapImageReminder, OdNotes, PriceNote];

    public static IReadOnlyList<string> PreferenceFields { get; } = [Greeting, Sections, PriceSections, RemindersAsBullets, DefaultCopyMode];

    /// <summary>The words the conflict screen uses for each field.</summary>
    public static string Label(string field) => field switch
    {
        Reminders => "Reminders",
        BreakPlan => "Tentative break plan",
        DapPlacement => "DAP / Placement",
        DapImageReminder => "Attach-image reminder",
        OdNotes => "OD Notes",
        PriceNote => "Ticket-price note",
        Greeting => "Greeting",
        Sections => "Section order and visibility",
        PriceSections => "Ticket price sections",
        RemindersAsBullets => "Show every reminder as a bullet",
        DefaultCopyMode => "Default copy mode",
        _ => field,
    };

    // ----- Daily content -----

    public static IReadOnlyDictionary<string, string> FromContent(MorningMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var clean = content.Sanitized();
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Reminders] = clean.Reminders,
            [BreakPlan] = clean.BreakPlan,
            [DapPlacement] = clean.DapPlacement,
            [DapImageReminder] = EncodeBool(clean.DapImageReminder),
            [OdNotes] = clean.OdNotes,
            [PriceNote] = clean.PriceNote,
        };
    }

    /// <summary><paramref name="current"/> with the given synchronized fields replaced; its local image path is always kept.</summary>
    public static MorningMessageContent ApplyToContent(MorningMessageContent current, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(values);
        string Text(string field, string fallback) => values.TryGetValue(field, out var value) ? value : fallback;

        return new MorningMessageContent(
            Text(Reminders, current.Reminders),
            Text(BreakPlan, current.BreakPlan),
            Text(DapPlacement, current.DapPlacement),
            values.TryGetValue(DapImageReminder, out var reminder) && TryDecodeBool(reminder, out var flag) ? flag : current.DapImageReminder,
            current.DapImagePath,
            Text(OdNotes, current.OdNotes),
            Text(PriceNote, current.PriceNote)).Sanitized();
    }

    /// <summary>
    /// True for a value that says nothing yet — blank text or an unchecked reminder. On a computer that has
    /// never synchronized a field, such a value is not offered as an edit, so first-time enablement imports
    /// the other computer's text instead of reporting a conflict against an empty box.
    /// </summary>
    public static bool IsUnsetContentValue(string field, string value) =>
        field == DapImageReminder ? value == EncodeBool(false) : string.IsNullOrWhiteSpace(value);

    // ----- Preferences -----

    public static IReadOnlyDictionary<string, string> FromPreferences(MorningMessagePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Greeting] = preferences.Greeting,
            [Sections] = EncodeSections(preferences.Layout),
            [PriceSections] = string.Join(",", preferences.PriceSections.Select(section => section.ToString())),
            [RemindersAsBullets] = EncodeBool(preferences.RemindersAsBullets),
            [DefaultCopyMode] = preferences.DefaultCopyMode.ToString(),
        };
    }

    /// <summary><paramref name="current"/> with the given synchronized fields replaced. Callers pass only values that passed <see cref="IsValidValue"/>.</summary>
    public static MorningMessagePreferences ApplyToPreferences(MorningMessagePreferences current, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(values);

        var greeting = values.TryGetValue(Greeting, out var g) ? g : current.Greeting;
        var layout = values.TryGetValue(Sections, out var s) && TryDecodeSections(s, out var decodedLayout) ? decodedLayout : current.Layout;
        var prices = values.TryGetValue(PriceSections, out var p) && TryDecodePriceSections(p, out var decodedPrices) ? decodedPrices : current.PriceSections;
        var bullets = values.TryGetValue(RemindersAsBullets, out var b) && TryDecodeBool(b, out var flag) ? flag : current.RemindersAsBullets;
        var mode = values.TryGetValue(DefaultCopyMode, out var m) && Enum.TryParse<TeamsCopyMode>(m, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : current.DefaultCopyMode;

        return MorningMessagePreferences.Sanitize(greeting, layout.Sections, mode, prices, bullets);
    }

    /// <summary>True when the preference still has its fresh-install value (see <see cref="IsUnsetContentValue"/> for why that matters).</summary>
    public static bool IsDefaultPreferenceValue(string field, string value) =>
        FromPreferences(MorningMessagePreferences.Default).TryGetValue(field, out var defaultValue) && defaultValue == value;

    // ----- Validation -----

    /// <summary>Whether <paramref name="value"/> is an acceptable value for <paramref name="field"/>. Unknown fields are never valid.</summary>
    public static bool IsValidValue(string field, string? value)
    {
        if (value is null)
        {
            return false;
        }

        return field switch
        {
            Reminders or BreakPlan or DapPlacement or OdNotes or PriceNote => value.Length <= MorningMessageContent.MaxFieldLength,
            DapImageReminder or RemindersAsBullets => TryDecodeBool(value, out _),
            Greeting => value.Length <= MorningMessagePreferences.MaxGreetingLength && value == value.Trim(),
            Sections => TryDecodeSections(value, out _),
            PriceSections => TryDecodePriceSections(value, out var sections) && string.Join(",", sections) == value,
            DefaultCopyMode => Enum.TryParse<TeamsCopyMode>(value, ignoreCase: false, out var mode) && Enum.IsDefined(mode) && mode.ToString() == value,
            _ => false,
        };
    }

    public static bool IsContentField(string field) => ContentFields.Contains(field, StringComparer.Ordinal);

    public static bool IsPreferenceField(string field) => PreferenceFields.Contains(field, StringComparer.Ordinal);

    /// <summary>SHA-256 of the UTF-8 value, lowercase hex — how the local sync state remembers a value without storing it twice.</summary>
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // ----- Encodings -----

    private static string EncodeBool(bool value) => value ? "true" : "false";

    private static bool TryDecodeBool(string value, out bool result)
    {
        result = value == "true";
        return value is "true" or "false";
    }

    /// <summary>"Greeting,Operations,-DapPlacement,…" — every section once, in order; a leading '-' means not included.</summary>
    private static string EncodeSections(MorningMessageLayout layout) =>
        string.Join(",", layout.Sections.Select(section => (section.Enabled ? string.Empty : "-") + section.Id));

    /// <summary>Strict: every defined section exactly once, nothing unknown — a list this version cannot represent faithfully is rejected rather than reshaped.</summary>
    private static bool TryDecodeSections(string value, out MorningMessageLayout layout)
    {
        layout = MorningMessageLayout.Default;
        var settings = new List<MorningMessageSectionSetting>();
        foreach (var token in value.Split(','))
        {
            var enabled = !token.StartsWith('-');
            var name = enabled ? token : token[1..];
            if (!Enum.TryParse<MorningMessageSectionId>(name, ignoreCase: false, out var id) || !Enum.IsDefined(id) || id.ToString() != name
                || settings.Any(existing => existing.Id == id))
            {
                return false;
            }

            settings.Add(new MorningMessageSectionSetting(id, enabled));
        }

        if (settings.Count != Enum.GetValues<MorningMessageSectionId>().Length)
        {
            return false;
        }

        layout = MorningMessageLayout.Normalize(settings);
        return true;
    }

    private static bool TryDecodePriceSections(string value, out IReadOnlyList<ProductDisplaySection> sections)
    {
        sections = [];
        if (value.Length == 0)
        {
            return true;
        }

        var list = new List<ProductDisplaySection>();
        foreach (var token in value.Split(','))
        {
            if (!Enum.TryParse<ProductDisplaySection>(token, ignoreCase: false, out var section) || section.ToString() != token
                || !MorningMessagePreferences.SelectablePriceSections.Contains(section) || list.Contains(section))
            {
                return false;
            }

            list.Add(section);
        }

        // Stored in the selectable order so two computers with the same choice produce the same text.
        sections = MorningMessagePreferences.SelectablePriceSections.Where(list.Contains).ToList();
        return true;
    }
}
