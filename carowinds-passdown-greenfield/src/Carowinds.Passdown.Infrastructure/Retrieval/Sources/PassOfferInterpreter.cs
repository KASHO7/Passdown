using System.Globalization;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 25: turns the published tier options (<see cref="PassTierPageParser"/>) into one
/// <see cref="ObservedProduct"/> per verified official offer, each under its own
/// <see cref="PassOffer"/> identity:
/// <list type="bullet">
/// <item>"Season Pass" option → the one-time new-purchase price (crossed-out <c>pricePretense</c> kept
/// only as the reference/comparison amount), plus a separate renewal offer when a label such as
/// "Renewals Just $89" explicitly publishes one;</item>
/// <item>"Membership" option → the recurring monthly rate (only when the frequency says monthly), plus
/// a separate initiation fee when a label such as "$20 initiation fee due today" publishes one; the
/// published billing terms ("Auto-renews After 12mo") are kept verbatim as the qualifier;</item>
/// <item>a locked option ("Not Available") → an unavailable, unpriced row — never a $0 price.</item>
/// </list>
/// Every amount is taken from the field its label belongs to — never "the first/largest number in
/// the card". Processing fees and taxes in the official disclosure are never added to any price.
/// A field that is present but cannot be read makes that offer <i>unconfirmed</i> (its last-known-good
/// value is kept, nothing is guessed) instead of failing the whole source.
/// </summary>
internal static class PassOfferInterpreter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex Money = new(
        @"^\$\s?(?<amount>\d{1,3}(?:,\d{3})+|\d+)(?:\.(?<cents>\d{2}))?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex EmbeddedMoney = new(
        @"\$\s?(?<amount>\d{1,3}(?:,\d{3})+|\d+)(?:\.(?<cents>\d{2}))?",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex RenewalLabel = new(@"\brenew\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex InitiationLabel = new(@"\b(?:initiation|enrollment|enrolment)\s+fee\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex KeywordYear = new(@"(?<!\d)(?<year>20\d{2})$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex TierTitle = new(@"^(?<tier>.+?)\s+tier$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly string[] KnownTiers = ["Silver", "Gold", "Prestige"];

    public static PassOfferInterpretation Interpret(IReadOnlyList<ParsedPassTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        var observed = new Dictionary<PassOffer, ObservedProduct>();
        var order = new List<PassOffer>();
        var unconfirmed = new HashSet<PassOffer>();
        var warnings = new List<string>();
        var disclosures = new List<string>();

        void Emit(PassOffer offer, ObservedProduct product)
        {
            if (unconfirmed.Contains(offer))
            {
                return;
            }

            if (observed.TryGetValue(offer, out var existing))
            {
                if (existing != product)
                {
                    // The same offer repeated with different values (e.g. a comparison table disagreeing
                    // with the tier card): neither is trusted over the other.
                    observed.Remove(offer);
                    unconfirmed.Add(offer);
                    warnings.Add($"{offer.DisplayName}: the page shows conflicting values; kept the last verified price.");
                }

                return; // an identical repeat of the same offer within one retrieval is one observation.
            }

            observed[offer] = product;
            order.Add(offer);
        }

        void Unconfirm(PassOffer offer, string reason)
        {
            observed.Remove(offer);
            if (unconfirmed.Add(offer))
            {
                warnings.Add($"{offer.DisplayName}: {reason}; kept the last verified price.");
            }
        }

        foreach (var tier in tiers)
        {
            if (!string.IsNullOrWhiteSpace(tier.FeesDisclosure) && !disclosures.Contains(tier.FeesDisclosure))
            {
                disclosures.Add(tier.FeesDisclosure);
            }

            var tierName = TierNameOf(tier.Title);
            if (tierName is null)
            {
                warnings.Add($"A pass card without a recognizable tier title ('{tier.Title ?? "(none)"}') was ignored.");
                continue;
            }

            if (!tier.HasPaymentOptions)
            {
                warnings.Add($"{tierName} Tier has no purchase options in the page data and was ignored.");
                continue;
            }

            if (!KnownTiers.Contains(tierName, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add($"New official tier found: {tierName}.");
            }

            foreach (var option in tier.Options)
            {
                InterpretOption(tierName, option, Emit, Unconfirm, warnings);
            }
        }

        return new PassOfferInterpretation(
            order.Where(observed.ContainsKey).Select(offer => observed[offer]).ToList(),
            unconfirmed.Select(offer => offer.ToKey()).ToList(),
            warnings,
            disclosures);
    }

    private static void InterpretOption(
        string tier,
        ParsedPassOption option,
        Action<PassOffer, ObservedProduct> emit,
        Action<PassOffer, string> unconfirm,
        List<string> warnings)
    {
        var product = ProductTypeOf(option.Title);
        if (product is null)
        {
            warnings.Add($"{tier} Tier: unrecognized purchase option '{option.Title ?? "(untitled)"}' was ignored.");
            return;
        }

        if (product == PassProductType.SeasonPass)
        {
            InterpretSeasonPass(tier, option, emit, unconfirm);
        }
        else
        {
            InterpretMembership(tier, option, emit, unconfirm);
        }
    }

    private static void InterpretSeasonPass(string tier, ParsedPassOption option, Action<PassOffer, ObservedProduct> emit, Action<PassOffer, string> unconfirm)
    {
        var year = SeasonYearOf(option.AccessoKeyword);
        var offer = PassOffer.SeasonPass(tier, PassPriceType.NewPurchase, year);
        var renewal = PassOffer.SeasonPass(tier, PassPriceType.Renewal, year);

        if (IsLocked(option))
        {
            emit(offer, Unavailable(offer, option.LockedText!));
            return;
        }

        if (option.HasUnresolvedField || ParseMoney(option.Price) is not { } amount)
        {
            unconfirm(offer, "the price could not be read");
            unconfirm(renewal, "the price could not be read");
            return;
        }

        if (IsMonthly(option.PriceFrequency))
        {
            // A Season Pass is a one-time purchase; a monthly frequency means the option was not read correctly.
            unconfirm(offer, "the price is shown as a monthly amount");
            unconfirm(renewal, "the price is shown as a monthly amount");
            return;
        }

        emit(offer, new ObservedProduct(
            Key: offer.ToKey(),
            DisplayName: offer.DisplayName,
            CurrentPriceAmount: amount,
            CurrentDisplayPrice: option.Price,
            ComparisonPriceAmount: ParseMoney(option.ReferencePrice),
            PriceQualifier: option.BeforePriceText,
            DateSpecific: false,
            ApplicableDate: null));

        switch (FindLabeledAmount(RenewalLabel, option.Savings, option.Highlight, option.Subtitle))
        {
            case LabeledAmount.Found found:
                emit(renewal, new ObservedProduct(
                    Key: renewal.ToKey(),
                    DisplayName: renewal.DisplayName,
                    CurrentPriceAmount: found.Amount,
                    CurrentDisplayPrice: found.DisplayAmount,
                    ComparisonPriceAmount: null,
                    PriceQualifier: null,
                    DateSpecific: false,
                    ApplicableDate: null));
                break;
            case LabeledAmount.Unreadable:
                unconfirm(renewal, "the renewal price could not be read");
                break;
        }
    }

    private static void InterpretMembership(string tier, ParsedPassOption option, Action<PassOffer, ObservedProduct> emit, Action<PassOffer, string> unconfirm)
    {
        var offer = PassOffer.Membership(tier, PassPriceType.MonthlyRate);
        var initiation = PassOffer.Membership(tier, PassPriceType.InitiationFee);

        if (IsLocked(option))
        {
            emit(offer, Unavailable(offer, option.LockedText!));
            return;
        }

        if (option.HasUnresolvedField || ParseMoney(option.Price) is not { } amount)
        {
            unconfirm(offer, "the monthly rate could not be read");
            unconfirm(initiation, "the monthly rate could not be read");
            return;
        }

        if (!IsMonthly(option.PriceFrequency))
        {
            // Never present an amount as a monthly rate unless the source says it is one.
            unconfirm(offer, "the billing frequency is not published as monthly");
            unconfirm(initiation, "the billing frequency is not published as monthly");
            return;
        }

        emit(offer, new ObservedProduct(
            Key: offer.ToKey(),
            DisplayName: offer.DisplayName,
            CurrentPriceAmount: amount,
            CurrentDisplayPrice: $"{option.Price}/mo",
            ComparisonPriceAmount: ParseMoney(option.ReferencePrice),
            PriceQualifier: BillingTermsOf(option),
            DateSpecific: false,
            ApplicableDate: null));

        var initiationText = new[] { option.Subtitle, option.Highlight, option.Savings }
            .FirstOrDefault(text => text is not null && InitiationLabel.IsMatch(text));
        switch (FindLabeledAmount(InitiationLabel, option.Subtitle, option.Highlight, option.Savings))
        {
            case LabeledAmount.Found found:
                emit(initiation, new ObservedProduct(
                    Key: initiation.ToKey(),
                    DisplayName: initiation.DisplayName,
                    CurrentPriceAmount: found.Amount,
                    CurrentDisplayPrice: found.DisplayAmount,
                    ComparisonPriceAmount: null,
                    PriceQualifier: initiationText,
                    DateSpecific: false,
                    ApplicableDate: null));
                break;
            case LabeledAmount.Unreadable:
                unconfirm(initiation, "the initiation fee could not be read");
                break;
        }
    }

    private static ObservedProduct Unavailable(PassOffer offer, string lockedText) => new(
        Key: offer.ToKey(),
        DisplayName: offer.DisplayName,
        CurrentPriceAmount: null,
        CurrentDisplayPrice: lockedText,
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        DateSpecific: false,
        ApplicableDate: null);

    /// <summary>Locked ("Not Available") with no price: the option is listed but cannot be bought — not $0, and not "sold out".</summary>
    private static bool IsLocked(ParsedPassOption option) =>
        !string.IsNullOrWhiteSpace(option.LockedText) && string.IsNullOrWhiteSpace(option.Price);

    /// <summary>The billing terms as published ("Auto-renews After 12mo") — a savings/highlight line with no dollar amount that talks about renewal or billing. Never parsed into a contract length.</summary>
    private static string? BillingTermsOf(ParsedPassOption option) =>
        new[] { option.Savings, option.Highlight }
            .FirstOrDefault(text => text is not null && !text.Contains('$', StringComparison.Ordinal) && RenewalLabel.IsMatch(text));

    private static bool IsMonthly(string? frequency) =>
        frequency is not null &&
        (frequency.Contains("/mo", StringComparison.OrdinalIgnoreCase) || frequency.Contains("month", StringComparison.OrdinalIgnoreCase));

    private static PassProductType? ProductTypeOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var isPass = title.Contains("Season Pass", StringComparison.OrdinalIgnoreCase);
        var isMembership = title.Contains("Membership", StringComparison.OrdinalIgnoreCase);
        return (isPass, isMembership) switch
        {
            (true, false) => PassProductType.SeasonPass,
            (false, true) => PassProductType.Membership,
            _ => null,
        };
    }

    /// <summary>"Gold Tier" → "Gold". A title without the word "Tier" is not a tier card.</summary>
    private static string? TierNameOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = TierTitle.Match(title.Trim());
        if (!match.Success)
        {
            return null;
        }

        var tier = match.Groups["tier"].Value.Trim();
        return tier.Length == 0 || tier.Contains('|', StringComparison.Ordinal) ? null : tier;
    }

    /// <summary>The season year the official checkout keyword names ("FallGoldSP2027" → 2027), or null — the observation date is never used.</summary>
    private static int? SeasonYearOf(string? accessoKeyword)
    {
        if (accessoKeyword is null)
        {
            return null;
        }

        var match = KeywordYear.Match(accessoKeyword.Trim());
        return match.Success ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture) : null;
    }

    internal static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Money.Match(text.Trim());
        return match.Success ? ToAmount(match) : null;
    }

    private static decimal ToAmount(Match match)
    {
        var whole = match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        var cents = match.Groups["cents"].Success ? match.Groups["cents"].Value : "00";
        return decimal.Parse($"{whole}.{cents}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The one dollar amount a text labeled by <paramref name="label"/> publishes ("Renewals Just $89").
    /// <see cref="LabeledAmount.Unreadable"/> when the label is present but no single amount is (none,
    /// or two different ones); <see cref="LabeledAmount.Absent"/> when no text carries the label.
    /// </summary>
    private static LabeledAmount FindLabeledAmount(Regex label, params string?[] texts)
    {
        var labeled = texts.Where(text => text is not null && label.IsMatch(text)).ToList();
        if (labeled.Count == 0)
        {
            return LabeledAmount.Absent.Instance;
        }

        var amounts = labeled
            .SelectMany(text => EmbeddedMoney.Matches(text!).Select(match => (Amount: ToAmount(match), Display: match.Value.Replace(" ", string.Empty, StringComparison.Ordinal))))
            .ToList();
        if (amounts.Count == 0 || amounts.Select(a => a.Amount).Distinct().Count() != 1)
        {
            return LabeledAmount.Unreadable.Instance;
        }

        return new LabeledAmount.Found(amounts[0].Amount, amounts[0].Display);
    }

    private abstract record LabeledAmount
    {
        public sealed record Found(decimal Amount, string DisplayAmount) : LabeledAmount;

        public sealed record Absent : LabeledAmount
        {
            public static Absent Instance { get; } = new();
        }

        public sealed record Unreadable : LabeledAmount
        {
            public static Unreadable Instance { get; } = new();
        }
    }
}

/// <param name="Products">Verified offers, in page order, one per identity.</param>
/// <param name="UnconfirmedProductKeys">Offers the page listed but whose values could not be verified — their last-known-good values must be kept, not counted absent.</param>
/// <param name="Warnings">Plain-language parser findings for diagnostics.</param>
/// <param name="FeesDisclosures">The official fee/tax disclosure text, verbatim (never added to a price).</param>
internal sealed record PassOfferInterpretation(
    IReadOnlyList<ObservedProduct> Products,
    IReadOnlyList<CatalogProductKey> UnconfirmedProductKeys,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FeesDisclosures);
