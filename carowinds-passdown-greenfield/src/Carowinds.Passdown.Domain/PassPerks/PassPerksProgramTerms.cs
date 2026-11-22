namespace Carowinds.Passdown.Domain.PassPerks;

/// <summary>
/// Phase 32: program-wide Perks &amp; Play facts, each kept as the official sentence it came from and the page
/// it was read on. These apply to every perk unless a perk states otherwise; they are shown as "program
/// terms", never presented as a specific perk's own condition. When two official pages describe
/// eligibility differently, both statements are kept and <see cref="Discrepancies"/> says so.
/// </summary>
public sealed record PassPerksProgramTerms
{
    /// <summary>The terms page's own revision line ("Updated July 30, 2026"), or null.</summary>
    public string? TermsUpdatedText { get; init; }

    /// <summary>Who may participate, per the terms page ("PASS PERKS PROGRAM IS VALID FOR …").</summary>
    public string? EligibilityStatement { get; init; }

    /// <summary>Who may not participate, per the terms page ("Pre-K Season Pass holders, … ARE NOT ELIGIBLE …").</summary>
    public string? IneligibilityStatement { get; init; }

    /// <summary>The program FAQ's answer to "Who is eligible to earn Pass Perks?".</summary>
    public string? FaqEligibilityAnswer { get; init; }

    /// <summary>The Carowinds page's loading note ("Perks &amp; Play Loyalty Rewards are loaded to Season Passholders who hold an active pass on the first day of the month …").</summary>
    public string? LoadingNote { get; init; }

    /// <summary>How visits are counted (terms: credit for one park per calendar day).</summary>
    public string? VisitCountingRule { get; init; }

    /// <summary>When visit-based rewards are given (terms: "at the conclusion of the promotional period").</summary>
    public string? VisitRewardTimingRule { get; init; }

    /// <summary>Where perks are redeemed (FAQ: "only be redeemed at your designated home park").</summary>
    public string? HomeParkRule { get; init; }

    /// <summary>Reward classifications the terms page defines (e.g. "Loyalty Rewards", "Visit-Based", "Partnership", "Giveaway").</summary>
    public IReadOnlyList<string> DefinedRewardTypes { get; init; } = [];

    public IReadOnlyList<string> Discrepancies { get; init; } = [];

    public IReadOnlyList<Uri> SourceUrls { get; init; } = [];

    public DateTimeOffset? VerifiedAt { get; init; }

    public bool IsEmpty =>
        TermsUpdatedText is null && EligibilityStatement is null && IneligibilityStatement is null &&
        FaqEligibilityAnswer is null && LoadingNote is null && VisitCountingRule is null &&
        VisitRewardTimingRule is null && HomeParkRule is null;

    /// <summary>
    /// Fills gaps from <paramref name="previous"/> (a page that failed this time keeps its last verified
    /// sentences) and recomputes the eligibility discrepancy.
    /// </summary>
    public PassPerksProgramTerms FillFrom(PassPerksProgramTerms? previous)
    {
        var merged = previous is null ? this : this with
        {
            TermsUpdatedText = TermsUpdatedText ?? previous.TermsUpdatedText,
            EligibilityStatement = EligibilityStatement ?? previous.EligibilityStatement,
            IneligibilityStatement = IneligibilityStatement ?? previous.IneligibilityStatement,
            FaqEligibilityAnswer = FaqEligibilityAnswer ?? previous.FaqEligibilityAnswer,
            LoadingNote = LoadingNote ?? previous.LoadingNote,
            VisitCountingRule = VisitCountingRule ?? previous.VisitCountingRule,
            VisitRewardTimingRule = VisitRewardTimingRule ?? previous.VisitRewardTimingRule,
            HomeParkRule = HomeParkRule ?? previous.HomeParkRule,
            DefinedRewardTypes = DefinedRewardTypes.Count > 0 ? DefinedRewardTypes : previous.DefinedRewardTypes,
            SourceUrls = SourceUrls.Concat(previous.SourceUrls).Distinct().ToList(),
        };

        return merged with { Discrepancies = EligibilityDiscrepancies(merged) };
    }

    /// <summary>
    /// The terms page and the FAQ list excluded passes differently (2026: the terms exclude Summer Fun, Haunt
    /// Season and Wild Card; the FAQ excludes KidZPass and Knott's Scary Farm). Reported, never resolved.
    /// </summary>
    public static IReadOnlyList<string> EligibilityDiscrepancies(PassPerksProgramTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.FaqEligibilityAnswer is not { } faq || (terms.IneligibilityStatement ?? terms.EligibilityStatement) is not { } legal)
        {
            return [];
        }

        string[] passNames = ["Pre-K", "KidZPass", "Summer Fun", "Haunt Season", "Wild Card", "Knott's Scary Farm"];
        var legalText = legal + " " + terms.EligibilityStatement;
        var onlyInFaq = passNames.Where(n => Mentions(faq, n) && !Mentions(legalText, n)).ToList();
        var onlyInTerms = passNames.Where(n => Mentions(legalText, n) && !Mentions(faq, n)).ToList();
        if (onlyInFaq.Count == 0 && onlyInTerms.Count == 0)
        {
            return [];
        }

        var parts = new List<string>();
        if (onlyInTerms.Count > 0)
        {
            parts.Add("the Terms and Conditions name " + string.Join(", ", onlyInTerms));
        }

        if (onlyInFaq.Count > 0)
        {
            parts.Add("the FAQ names " + string.Join(", ", onlyInFaq));
        }

        return [$"The official Terms and Conditions and the official FAQ describe ineligible passes differently ({string.Join("; ", parts)}). Both official statements are shown; check with the official Six Flags app or Guest Relations for a specific pass."];
    }

    private static bool Mentions(string text, string name) =>
        text.Replace("’", "'", StringComparison.Ordinal).Contains(name, StringComparison.OrdinalIgnoreCase);
}
