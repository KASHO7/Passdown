using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.PassPerks;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 32: official Carowinds Pass Perks adapter — three plain GETs per check, no browser, no login:
/// <list type="number">
/// <item>the park page <see cref="ParkPageUrl"/> (required): month heading, listed rewards, featured
/// visit-based campaign, loading footnote;</item>
/// <item>the program terms <see cref="TermsUrl"/> and FAQ <see cref="FaqUrl"/> (optional): eligibility,
/// visit counting and home-park rules. A failure of either is a warning; their last verified sentences are kept.</item>
/// </list>
/// The park page is the only source of rewards. It is an <see cref="RetrievalStatus.Error"/> (stored perks
/// untouched) when it cannot be fetched, carries no structured data, has no "{Month} Perks" heading, or
/// lists no readable reward — zero parsed rewards is never treated as a confirmed empty month. Individual
/// guests' rewards live behind the Six Flags app login and are never requested.
/// </summary>
public sealed partial class PassPerksSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<PassPerksObservation>
{
    private const string SourceName = "Carowinds Perks & Play";

    public static readonly Uri ParkPageUrl = new("https://www.sixflags.com/carowinds/perks-and-play");
    public static readonly Uri FaqUrl = new("https://www.sixflags.com/perks-and-play");
    public static readonly Uri TermsUrl = new("https://www.sixflags.com/pass-perks-terms");

    public const string EligibilityQuestion = "Who is eligible to earn Pass Perks?";
    public const string HomeParkQuestion = "I have the All-Park Passport. Does that change how I redeem Pass Perks?";

    public string SourceId => "pass-perks.official";

    public SourceCategory Category => SourceCategory.PassPerks;

    public async Task<SourceResult<PassPerksObservation>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);

        RetrievalHttpResponse park;
        try
        {
            park = await httpClient.GetAsync(ParkPageUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(timeChecked, ex);
        }

        if (!park.IsSuccessStatusCode)
        {
            return Error(timeChecked, new InvalidOperationException($"GET {ParkPageUrl} returned {(int)park.StatusCode} {park.StatusCode}."));
        }

        var page = PassPerksPageParser.ParseParkPage(park.Body);
        if (!page.HadStructuredData || page.MonthHeading is null)
        {
            return Error(timeChecked, new FormatException(
                "The official Carowinds Perks & Play page no longer contains a recognizable monthly perks list — the page structure may have changed."));
        }

        var campaign = PassPerkText.CampaignFromHeading(page.MonthHeading, DateOnly.FromDateTime(timeChecked.DateTime));
        if (campaign is null)
        {
            return Error(timeChecked, new FormatException($"The month heading \"{page.MonthHeading}\" could not be read."));
        }

        var warnings = new List<string>();
        var perks = BuildPerks(page.Items, campaign, timeChecked, warnings, out var unreadable);
        if (perks.Count == 0)
        {
            return Error(timeChecked, new FormatException(
                $"\"{page.MonthHeading}\" was found but no reward could be read from it; previously verified perks are kept."));
        }

        var terms = await ReadProgramTermsAsync(page.LoadingNote, timeChecked, warnings, cancellationToken).ConfigureAwait(false);

        var detail = $"{page.MonthHeading}: {perks.Count} Pass {(perks.Count == 1 ? "Perk" : "Perks")} verified" +
            (unreadable > 0 ? $"; {unreadable} listed {(unreadable == 1 ? "reward" : "rewards")} could not be read." : ".");
        return SourceResult<PassPerksObservation>.Success(
            SourceId,
            new SourceEvidence(
                SourceName,
                ParkPageUrl,
                timeChecked,
                RetrievalStatus.Verified,
                detail,
                RetrievalStrategy.Html,
                requestedDate: null,
                technicalDetail: warnings.Count == 0 ? null : string.Join(" ", warnings)),
            new PassPerksObservation(campaign, perks) { UnreadableCount = unreadable, ProgramTerms = terms, Warnings = warnings });
    }

    /// <summary>
    /// Interprets the listed rewards and settles duplicate identities: an exact repeat of the same reward is
    /// kept once; two different rewards with the same name keep separate identities ("…-2") and a warning.
    /// </summary>
    internal static IReadOnlyList<OfficialPassPerk> BuildPerks(
        IReadOnlyList<PassPerksPageParser.RawPerk> items,
        PassPerkCampaign campaign,
        DateTimeOffset checkedAt,
        List<string> warnings,
        out int unreadable)
    {
        unreadable = 0;
        var perks = new List<OfficialPassPerk>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                unreadable++;
                continue;
            }

            var perk = PassPerkText.Interpret(item.Title, item.DateLine, item.Paragraphs, campaign, ParkPageUrl, checkedAt, item.IsMonthlyListing);
            var sameId = perks.Where(p => p.PerkId == perk.PerkId || p.PerkId.StartsWith(perk.PerkId + "-", StringComparison.Ordinal)).ToList();
            if (sameId.Any(p => p.HasSameTermsAs(perk)))
            {
                continue;
            }

            if (sameId.Count > 0)
            {
                warnings.Add($"Two different rewards are both named \"{perk.Title}\"; both are kept.");
                perk = perk with { PerkId = $"{perk.PerkId}-{sameId.Count + 1}" };
            }

            perks.Add(perk);
        }

        if (unreadable > 0)
        {
            warnings.Add($"{unreadable} listed {(unreadable == 1 ? "reward was" : "rewards were")} unreadable (no name).");
        }

        return perks;
    }

    private async Task<PassPerksProgramTerms> ReadProgramTermsAsync(string? loadingNote, DateTimeOffset timeChecked, List<string> warnings, CancellationToken cancellationToken)
    {
        var sources = new List<Uri> { ParkPageUrl };
        var terms = new PassPerksProgramTerms { LoadingNote = loadingNote, VerifiedAt = timeChecked };

        if (await TryGetAsync(TermsUrl, warnings, cancellationToken).ConfigureAwait(false) is { } termsHtml)
        {
            var sentences = PassPerksPageParser.TermsParagraphs(termsHtml).SelectMany(SplitSentences).ToList();
            var paragraphs = PassPerksPageParser.TermsParagraphs(termsHtml);
            terms = terms with
            {
                TermsUpdatedText = paragraphs.Select(p => UpdatedRegex().Match(p)).FirstOrDefault(m => m.Success)?.Value,
                EligibilityStatement = sentences.FirstOrDefault(s => s.Contains("PROGRAM IS VALID FOR", StringComparison.OrdinalIgnoreCase)),
                IneligibilityStatement = sentences.FirstOrDefault(s => s.Contains("NOT ELIGIBLE", StringComparison.OrdinalIgnoreCase)),
                VisitCountingRule = sentences.FirstOrDefault(s => s.Contains("one Park per calendar day", StringComparison.OrdinalIgnoreCase)),
                VisitRewardTimingRule = sentences.FirstOrDefault(s => s.Contains("at the conclusion of the promotional period", StringComparison.OrdinalIgnoreCase)),
                DefinedRewardTypes = DefinedTypes(paragraphs),
            };
            if (terms.EligibilityStatement is null && terms.TermsUpdatedText is null)
            {
                warnings.Add($"{TermsUrl} had no recognizable terms text.");
            }
            else
            {
                sources.Add(TermsUrl);
            }
        }

        if (await TryGetAsync(FaqUrl, warnings, cancellationToken).ConfigureAwait(false) is { } faqHtml)
        {
            var eligibility = PassPerksPageParser.FaqAnswer(faqHtml, EligibilityQuestion);
            var homePark = PassPerksPageParser.FaqAnswer(faqHtml, HomeParkQuestion);
            terms = terms with
            {
                FaqEligibilityAnswer = eligibility.Count > 0 ? string.Join(" ", eligibility) : null,
                HomeParkRule = homePark.Count > 0 ? string.Join(" ", homePark) : null,
            };
            if (eligibility.Count == 0 && homePark.Count == 0)
            {
                warnings.Add($"{FaqUrl} had no recognizable FAQ answers.");
            }
            else
            {
                sources.Add(FaqUrl);
            }
        }

        return terms with { SourceUrls = sources };
    }

    private async Task<string?> TryGetAsync(Uri url, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response.Body;
            }

            warnings.Add($"{url} returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"{url} could not be checked: {ex.Message}");
        }

        return null;
    }

    private static IReadOnlyList<string> DefinedTypes(IReadOnlyList<string> paragraphs)
    {
        var types = new List<string>();
        void AddIf(string heading, string label)
        {
            if (paragraphs.Any(p => p.StartsWith(heading, StringComparison.OrdinalIgnoreCase)))
            {
                types.Add(label);
            }
        }

        AddIf("PASS PERKS LOYALTY REWARDS", "Loyalty Rewards");
        AddIf("PASS PERKS VISIT-BASED", "Visit-Based Rewards");
        AddIf("PASS PERKS PARTNERSHIP", "Partnership Rewards");
        AddIf("PASS PERKS GIVEAWAY", "Giveaway");
        return types;
    }

    private static IEnumerable<string> SplitSentences(string paragraph) =>
        SentenceRegex().Split(paragraph).Select(s => s.Trim()).Where(s => s.Length > 0);

    private SourceResult<PassPerksObservation> Error(DateTimeOffset timeChecked, Exception exception) =>
        SourceResult<PassPerksObservation>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                ParkPageUrl,
                timeChecked,
                RetrievalStrategy.Html,
                "We couldn't check the official Pass Perks right now.",
                exception));

    [GeneratedRegex(@"\bUpdated\s+[A-Z][a-z]+\s+\d{1,2},\s+\d{4}")]
    private static partial Regex UpdatedRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z(])")]
    private static partial Regex SentenceRegex();
}
