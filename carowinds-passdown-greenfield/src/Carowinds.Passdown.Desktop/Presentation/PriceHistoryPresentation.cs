using System.Globalization;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One row of the Price History table (Phase 20A), oldest first.</summary>
public sealed record PriceHistoryRowPresentation(
    string FirstSeenText,
    string LastConfirmedText,
    string PriceText,
    string ChangeText,
    string? NoteText,
    bool IsIncrease,
    bool IsDecrease,
    string? ProvenanceText = null)
{
    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);

    /// <summary>Phase 26: shown only for rows that rest (partly) on another computer's checks.</summary>
    public bool HasProvenance => !string.IsNullOrWhiteSpace(ProvenanceText);

    /// <summary>Screen-reader summary of the row, so the meaning never depends on column position or color.</summary>
    public string AutomationName
    {
        get
        {
            var text = $"First seen {FirstSeenText}, {PriceText}, change {ChangeText}.";
            if (HasNote)
            {
                text = $"{text} {NoteText}";
            }

            return HasProvenance ? $"{text} {ProvenanceText}" : text;
        }
    }
}

/// <summary>
/// Phase 20A formatting for season price history: US currency and dates (Eastern park time), the
/// Exact/From distinction, and changes derived from the previous interval. No business rules — the
/// intervals and changes come from <see cref="ISeasonPriceHistoryService"/>.
/// </summary>
public static class PriceHistoryPresentation
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    public const string NoChangesYetText = "No price changes recorded yet.";
    public const string NoStatesText = "No official price has been recorded for this product yet. It will appear after the next successful official check.";
    public const string NoProductsText = "No Season Pass, Membership or Season Pass Add-On prices have been recorded yet. History starts with the next successful official check.";
    public const string LoadErrorText = "Price history couldn't be loaded. Try again later; nothing else is affected.";
    public const string TimingNoteText = "\"First seen\" is the first successful official check that showed a price. The actual change happened between the previous row's last-confirmed date and that date.";
    public const string ImportedOnlyDetailText = "Imported history";

    public static string FormatAmount(decimal amount) => amount.ToString("C2", UsCulture);

    /// <summary>"$105.00", "From $105.00", "$89.00 (sale price)" or "$50.00 (bundle)".</summary>
    public static string FormatPrice(decimal amount, PriceKind kind) => FormatPrice(amount, kind, PassPriceUnit.OneTime);

    /// <summary>Phase 25: with the series' unit — a Membership rate reads "$7.00/month", never like a one-time price.</summary>
    public static string FormatPrice(decimal amount, PriceKind kind, PassPriceUnit unit)
    {
        var money = unit == PassPriceUnit.PerMonth ? $"{FormatAmount(amount)}/month" : FormatAmount(amount);
        return kind switch
        {
            PriceKind.StartingFrom => $"From {money}",
            PriceKind.Sale => $"{money} (sale price)",
            PriceKind.Bundle => $"{money} (bundle)",
            _ => money,
        };
    }

    /// <summary>"Sep 19, 2026" — the Eastern (park) calendar date of a stored UTC timestamp.</summary>
    public static string FormatDate(DateTimeOffset timestamp) =>
        EasternTimeZone.ConvertFromUtc(timestamp.ToUniversalTime()).ToString("MMM d, yyyy", UsCulture);

    /// <summary>"—" for a baseline; "+$6.00 (+6.1%)" / "-$10.00 (-9.5%)"; "$0.00" when only the kind changed.</summary>
    public static string FormatChange(SeasonPriceChange? change) => FormatChange(change, PassPriceUnit.OneTime);

    /// <summary>Phase 25: a Membership series' change reads per month ("+$2.00/month (+28.6%)").</summary>
    public static string FormatChange(SeasonPriceChange? change, PassPriceUnit unit)
    {
        var suffix = unit == PassPriceUnit.PerMonth ? "/month" : string.Empty;
        if (change is null)
        {
            return "—";
        }

        if (!change.AmountChanged)
        {
            return FormatAmount(0m) + suffix;
        }

        var sign = change.AmountDelta > 0 ? "+" : "-";
        var amountText = $"{sign}{FormatAmount(Math.Abs(change.AmountDelta))}{suffix}";
        return change.PercentChange is { } percent ? $"{amountText} ({FormatPercent(percent)})" : amountText;
    }

    /// <summary>"+6.1%" / "-9.5%"; a nonzero change too small to show at one decimal is "+&lt;0.1%".</summary>
    public static string FormatPercent(decimal percent)
    {
        var sign = percent > 0 ? "+" : "-";
        var magnitude = Math.Round(Math.Abs(percent), 1, MidpointRounding.AwayFromZero);
        return magnitude == 0m
            ? $"{sign}<0.1%"
            : $"{sign}{magnitude.ToString("0.0", UsCulture)}%";
    }

    public static IReadOnlyList<PriceHistoryRowPresentation> BuildRows(IReadOnlyList<PriceHistoryEntry> entries) =>
        BuildRows(entries, PassPriceUnit.OneTime);

    /// <param name="unit">Phase 25: the unit of this product's whole series (one product key = one unit).</param>
    public static IReadOnlyList<PriceHistoryRowPresentation> BuildRows(IReadOnlyList<PriceHistoryEntry> entries, PassPriceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rows = new List<PriceHistoryRowPresentation>(entries.Count);
        PriceHistoryEntry? previous = null;
        foreach (var entry in entries)
        {
            var interval = entry.Interval;
            var change = entry.ChangeFromPrevious;
            rows.Add(new PriceHistoryRowPresentation(
                FirstSeenText: FormatDate(interval.FirstObservedAt),
                LastConfirmedText: FormatDate(interval.LastConfirmedAt),
                PriceText: FormatPrice(interval.Amount, interval.PriceKind, unit),
                ChangeText: FormatChange(change, unit),
                NoteText: BuildNote(previous, entry, unit),
                IsIncrease: change is { AmountDelta: > 0 },
                IsDecrease: change is { AmountDelta: < 0 },
                ProvenanceText: ProvenanceText(entry.Provenance)));
            previous = entry;
        }

        return rows;
    }

    /// <summary>Makes a semantic-only change or a reappearance visible even when the amount is unchanged.</summary>
    private static string? BuildNote(PriceHistoryEntry? previous, PriceHistoryEntry entry, PassPriceUnit unit)
    {
        if (previous is null || entry.ChangeFromPrevious is not { } change)
        {
            return null;
        }

        if (entry.IsReappearanceAtSamePrice)
        {
            return "Published again after being retired.";
        }

        if (change.KindChanged)
        {
            var before = FormatPrice(previous.Interval.Amount, previous.Interval.PriceKind, unit);
            var after = FormatPrice(entry.Interval.Amount, entry.Interval.PriceKind, unit);
            return $"Price type changed: {before} → {after}.";
        }

        return null;
    }

    public const string ImportedProvenanceText = "Imported through Personal Sync from another computer's official checks.";
    public const string MixedProvenanceText = "Collected on this computer and imported through Personal Sync.";

    /// <summary>
    /// Phase 26: a row from this computer's own checks needs no label (every pre-Phase 26 row); one that
    /// rests on another computer's checks says so — it is official history, not proof this computer
    /// checked the website then.
    /// </summary>
    public static string? ProvenanceText(PriceHistoryProvenance provenance) => provenance switch
    {
        PriceHistoryProvenance.ImportedThroughSync => ImportedProvenanceText,
        PriceHistoryProvenance.CollectedHere | PriceHistoryProvenance.ImportedThroughSync => MixedProvenanceText,
        _ => null,
    };

    public static string AvailabilityText(IReadOnlyList<PriceHistoryEntry> entries) =>
        entries.Count == 0
            ? string.Empty
            : $"Price history available starting from {FormatDate(entries[0].Interval.FirstObservedAt)}.";

    public static string FamilyText(PriceHistoryFamily family) => family switch
    {
        PriceHistoryFamily.SeasonPassAddOn => "Season Pass Add-On",
        PriceHistoryFamily.SeasonPassRenewal => "Season Pass renewal offer",
        PriceHistoryFamily.Membership => "Membership · monthly rate",
        PriceHistoryFamily.MembershipInitiationFee => "Membership · initiation fee",
        _ => "Season Pass",
    };

    /// <summary>Phase 25: the Price History "Price type" filter choices, in display order.</summary>
    public static IReadOnlyList<PriceHistoryFamilyFilter> FamilyFilters { get; } =
    [
        new(null, "All price types"),
        new(PriceHistoryFamily.SeasonPass, "Season Passes"),
        new(PriceHistoryFamily.SeasonPassRenewal, "Season Pass renewal offers"),
        new(PriceHistoryFamily.Membership, "Memberships (monthly rates)"),
        new(PriceHistoryFamily.MembershipInitiationFee, "Membership initiation fees"),
        new(PriceHistoryFamily.SeasonPassAddOn, "Season Pass Add-Ons"),
    ];
}

/// <summary>Phase 25: one Price History "Price type" filter choice; a null family shows every type.</summary>
public sealed record PriceHistoryFamilyFilter(PriceHistoryFamily? Family, string DisplayName)
{
    public override string ToString() => DisplayName;
}
