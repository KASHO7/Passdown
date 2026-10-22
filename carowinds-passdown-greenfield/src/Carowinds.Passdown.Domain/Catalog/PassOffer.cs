using System.Globalization;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>Phase 25: the two different real products the official "Memberships &amp; Season Passes" page sells per tier.</summary>
public enum PassProductType
{
    /// <summary>A one-time purchase valid for a stated season.</summary>
    SeasonPass,

    /// <summary>A recurring monthly membership — never a Season Pass payment plan.</summary>
    Membership,
}

/// <summary>Phase 25: which published amount of a <see cref="PassProductType"/> an identity tracks.</summary>
public enum PassPriceType
{
    /// <summary>The Season Pass's current new-purchase (one-time) price.</summary>
    NewPurchase,

    /// <summary>A Season Pass renewal offer, only when the source explicitly publishes one. Never a new-customer price.</summary>
    Renewal,

    /// <summary>A Membership's recurring monthly rate.</summary>
    MonthlyRate,

    /// <summary>A Membership's one-time initiation/enrollment fee — separate from the monthly rate.</summary>
    InitiationFee,
}

/// <summary>Phase 25: the unit an official pass amount is quoted in.</summary>
public enum PassPriceUnit
{
    OneTime,
    PerMonth,
}

/// <summary>
/// Phase 25: one official pass offer's identity — park, product type, tier, price type and (Season
/// Pass only) season year. A Gold Season Pass and a Gold Membership are different products; a Season
/// Pass's renewal offer and its new-purchase price are different prices; a Membership's monthly rate
/// and its initiation fee are different prices. Each therefore gets its own deterministic
/// <see cref="CatalogProductKey"/> (<see cref="ToKey"/>), so each keeps its own price history with one
/// unit, and none can ever be merged with another. Built from verified source labels only — never
/// from a card's position on the page, and never from a price.
///
/// Equality is by <see cref="IdentityText"/> (tier compared case-insensitively).
/// </summary>
public sealed class PassOffer : IEquatable<PassOffer>
{
    public const string Park = "carowinds";

    private const string KeyScheme = "pass-offer:";

    private PassOffer(string tier, PassProductType product, PassPriceType priceType, int? seasonYear)
    {
        Tier = tier;
        Product = product;
        PriceType = priceType;
        SeasonYear = seasonYear;
    }

    /// <summary>The official tier name without the word "Tier" ("Gold"). Any official tier is accepted, not only the three known today.</summary>
    public string Tier { get; }

    public PassProductType Product { get; }

    public PassPriceType PriceType { get; }

    /// <summary>The Season Pass's own validity year when the source states it; always null for a Membership (no season is published for it).</summary>
    public int? SeasonYear { get; }

    public PassPriceUnit Unit => PriceType == PassPriceType.MonthlyRate ? PassPriceUnit.PerMonth : PassPriceUnit.OneTime;

    /// <summary>
    /// True only for the Season Pass new-purchase price — the one pass price the Associate PDF and Teams
    /// Copy have always carried. Renewal offers, Membership rates and initiation fees are not part of that
    /// export contract (Phase 25 defers them there; they show in Products and Price History).
    /// </summary>
    public bool IsPassdownExportable => Product == PassProductType.SeasonPass && PriceType == PassPriceType.NewPurchase;

    /// <summary>For a renewal offer its Season Pass; for an initiation fee its Membership; otherwise null.</summary>
    public PassOffer? Parent => PriceType switch
    {
        PassPriceType.Renewal => SeasonPass(Tier, PassPriceType.NewPurchase, SeasonYear),
        PassPriceType.InitiationFee => Membership(Tier, PassPriceType.MonthlyRate),
        _ => null,
    };

    /// <summary>"carowinds|gold|season-pass|new-purchase|2027" — the normalized identity text.</summary>
    public string IdentityText
    {
        get
        {
            var text = $"{Park}|{NormalizeTier(Tier)}|{ProductToken(Product)}|{PriceTypeToken(PriceType)}";
            return SeasonYear is { } year ? $"{text}|{year.ToString(CultureInfo.InvariantCulture)}" : text;
        }
    }

    /// <summary>"Gold Season Pass — 2027", "Gold Season Pass Renewal — 2027", "Gold Membership", "Gold Membership Initiation Fee".</summary>
    public string DisplayName
    {
        get
        {
            var name = (Product, PriceType) switch
            {
                (PassProductType.SeasonPass, PassPriceType.Renewal) => $"{Tier} Season Pass Renewal",
                (PassProductType.SeasonPass, _) => $"{Tier} Season Pass",
                (_, PassPriceType.InitiationFee) => $"{Tier} Membership Initiation Fee",
                _ => $"{Tier} Membership",
            };
            return SeasonYear is { } year ? $"{name} — {year.ToString(CultureInfo.InvariantCulture)}" : name;
        }
    }

    public static PassOffer SeasonPass(string tier, PassPriceType priceType, int? seasonYear)
    {
        if (priceType is not (PassPriceType.NewPurchase or PassPriceType.Renewal))
        {
            throw new ArgumentException("A Season Pass has only a new-purchase or a renewal price.", nameof(priceType));
        }

        if (seasonYear is < 2000 or > 2099)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonYear), seasonYear, "A season year must be a plausible four-digit year.");
        }

        return new PassOffer(CleanTier(tier), PassProductType.SeasonPass, priceType, seasonYear);
    }

    public static PassOffer Membership(string tier, PassPriceType priceType)
    {
        if (priceType is not (PassPriceType.MonthlyRate or PassPriceType.InitiationFee))
        {
            throw new ArgumentException("A Membership has only a monthly rate or an initiation fee.", nameof(priceType));
        }

        return new PassOffer(CleanTier(tier), PassProductType.Membership, priceType, seasonYear: null);
    }

    public CatalogProductKey ToKey() => CatalogProductKey.FromPassOffer(this);

    /// <summary>The offer a stored/observed key names, or null when the key is not a Phase 25 pass-offer key.</summary>
    public static PassOffer? FromKey(string? productKey)
    {
        if (string.IsNullOrWhiteSpace(productKey))
        {
            return null;
        }

        var expectedPrefix = $"{SourceCategory.SeasonPasses}:{KeyScheme}";
        if (!productKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = productKey[expectedPrefix.Length..].Split('|');
        if (parts.Length is < 4 or > 5 || parts[0] != Park || parts[1].Length == 0)
        {
            return null;
        }

        var tier = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[1]);
        int? year = null;
        if (parts.Length == 5)
        {
            if (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear) || parsedYear is < 2000 or > 2099)
            {
                return null;
            }

            year = parsedYear;
        }

        return (parts[2], parts[3]) switch
        {
            ("season-pass", "new-purchase") => SeasonPass(tier, PassPriceType.NewPurchase, year),
            ("season-pass", "renewal") => SeasonPass(tier, PassPriceType.Renewal, year),
            ("membership", "monthly") when year is null => Membership(tier, PassPriceType.MonthlyRate),
            ("membership", "initiation-fee") when year is null => Membership(tier, PassPriceType.InitiationFee),
            _ => null,
        };
    }

    /// <summary>
    /// The Season Pass a pre-Phase 25 official pass name denotes ("2027 Gold Pass" → Gold Season Pass,
    /// new purchase, 2027) — and the same for a name in the Phase 25 form ("Gold Season Pass — 2027"),
    /// which a legacy-keyed product carries once it is observed again. Deliberately strict: only a
    /// known tier word, the word "Pass" (optionally "Season Pass") and an optional year — so "Military
    /// Gold Pass", any membership, renewal, add-on or promotional name is never matched. Every legacy
    /// Season Passes record was a one-time pass (the old page sold nothing else), which is why a match
    /// is always <see cref="PassPriceType.NewPurchase"/>.
    /// </summary>
    public static PassOffer? FromLegacyName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var match = LegacyPassName.Match(string.Join(' ', displayName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        if (!match.Success)
        {
            return null;
        }

        var leadingYear = match.Groups["lead"].Success ? int.Parse(match.Groups["lead"].Value, CultureInfo.InvariantCulture) : (int?)null;
        var trailingYear = match.Groups["trail"].Success ? int.Parse(match.Groups["trail"].Value, CultureInfo.InvariantCulture) : (int?)null;
        if (leadingYear is { } lead && trailingYear is { } trail && lead != trail)
        {
            return null; // two different years: ambiguous, never guessed.
        }

        var year = leadingYear ?? trailingYear;
        if (year is < 2000 or > 2099)
        {
            return null;
        }

        var tier = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups["tier"].Value.ToLowerInvariant());
        return SeasonPass(tier, PassPriceType.NewPurchase, year);
    }

    /// <summary>
    /// The offer a stored or composed Season Passes product denotes: its pass-offer key when it has one,
    /// otherwise the legacy-name rule. Null for every other category and for any unrecognized name.
    /// </summary>
    public static PassOffer? Resolve(SourceCategory? category, string? productKey, string? displayName) =>
        category == SourceCategory.SeasonPasses ? FromKey(productKey) ?? FromLegacyName(displayName) : null;

    public bool Equals(PassOffer? other) => other is not null && string.Equals(IdentityText, other.IdentityText, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PassOffer);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(IdentityText);

    public override string ToString() => IdentityText;

    private static readonly Regex LegacyPassName = new(
        @"^(?:(?<lead>\d{4})\s+)?(?<tier>Silver|Gold|Prestige)\s+(?:Season\s+)?Pass(?:\s*[—–-]\s*(?<trail>\d{4}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static string CleanTier(string tier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tier);
        var cleaned = string.Join(' ', tier.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Contains('|', StringComparison.Ordinal))
        {
            throw new ArgumentException("A tier name cannot contain '|'.", nameof(tier));
        }

        return cleaned;
    }

    private static string NormalizeTier(string tier) => tier.ToLowerInvariant().Replace(' ', '-');

    private static string ProductToken(PassProductType product) => product switch
    {
        PassProductType.SeasonPass => "season-pass",
        _ => "membership",
    };

    private static string PriceTypeToken(PassPriceType priceType) => priceType switch
    {
        PassPriceType.NewPurchase => "new-purchase",
        PassPriceType.Renewal => "renewal",
        PassPriceType.MonthlyRate => "monthly",
        _ => "initiation-fee",
    };
}
