using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Phase 32: one Pass Perk as a compact row (Products panel and the Calendar's selected date).</summary>
public sealed record PassPerkItemPresentation(
    string PerkId,
    string Title,
    string TypeLabel,
    string RequirementText,
    string? KeyDateText,
    string? RowStatusLabel,
    string? NoticeText,
    OfficialDetailsPresentation Details)
{
    /// <summary>Requirement, key date and status as one muted meta line.</summary>
    public string MetaText => string.Join(" · ", new[] { RequirementText, KeyDateText, RowStatusLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string AccessibleSummary => string.Join(". ", new[] { Title, TypeLabel, RequirementText, KeyDateText, RowStatusLabel ?? "Current", NoticeText }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Phase 32: the Products screen's Pass Perks panel for one selected month.</summary>
public sealed record PassPerksPanelPresentation(
    string MonthText,
    string StatusText,
    bool IsStatusWarning,
    string? EmptyStateText,
    string? HistoryNoticeText,
    IReadOnlyList<PassPerkItemPresentation> Items,
    OfficialDetailsPresentation? ProgramTermsDetails);

/// <summary>
/// Phase 32: pure formatting of cached Pass Perks for the Products panel, the Calendar and the View details
/// dialog. Official wording is quoted, never rewritten; derived facts (years assigned from the month list,
/// program-wide rules) are labelled as such; nothing here says a particular guest has a reward.
/// </summary>
public static class PassPerksPresentation
{
    public const string IndividualPassNote =
        "These are publicly advertised rewards. They do not show what a specific pass has earned or received — check the official Six Flags app (Wallet → Pass Perks) for an individual pass.";

    public const string NotCheckedText = "Pass Perks not checked yet. They are checked with the official data refresh (Check Online).";

    public static string MonthText(DateOnly month) => month.ToString("MMMM yyyy", PresentationCulture.UnitedStates);

    public static PassPerksPanelPresentation BuildPanel(PassPerksOverview overview, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(overview);

        var view = overview.ForMonth(month.Year, month.Month);
        var (statusText, warning) = Status(overview.Status);
        var monthText = MonthText(month);

        string? empty = null;
        if (!view.WasObserved)
        {
            empty = overview.Status.LastSuccessAt is { } last
                ? $"Pass Perks for {monthText} have not been verified. Last successful check: {SeasonContextPresentation.When(last)}."
                : $"Pass Perks for {monthText} have not been verified.";
        }
        else if (view.Entries.Count == 0)
        {
            empty = $"No Pass Perks are recorded for {monthText}.";
        }

        string? history = null;
        if (view.WasObserved && !view.IsLatestCampaign && overview.Document?.Campaigns.FirstOrDefault(c => c.Campaign.Key == $"{month.Year:D4}-{month.Month:D2}") is { } record)
        {
            history = $"Earlier month — listed on the official page {Day(record.FirstSeenAt)} to {Day(record.LastSeenAt)}. Shown for reference; not the current list.";
        }

        var items = view.Entries.Select(e => Item(e, overview.ProgramTerms)).ToList();
        return new PassPerksPanelPresentation(monthText, statusText, warning, empty, history, items, ProgramTermsDetails(overview.ProgramTerms));
    }

    public static (string Text, bool Warning) Status(OfficialDocumentStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.Freshness switch
        {
            OfficialDocumentFreshness.NotChecked => (NotCheckedText, true),
            OfficialDocumentFreshness.LastCheckFailed when status.LastSuccessAt is { } success =>
                ($"Unable to refresh; showing previously verified rewards (last successful check {SeasonContextPresentation.When(success)}).", true),
            OfficialDocumentFreshness.LastCheckFailed => ($"Unable to check the official Pass Perks ({SeasonContextPresentation.When(status.LastAttemptAt)}); nothing verified yet.", true),
            _ => ($"Official Carowinds Perks & Play page checked {SeasonContextPresentation.When(status.LastSuccessAt)}.", false),
        };
    }

    public static PassPerkItemPresentation Item(PassPerkEntry entry, PassPerksProgramTerms? terms)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var perk = entry.Perk;
        return new PassPerkItemPresentation(
            perk.PerkId,
            perk.Title,
            TypeLabel(perk.RewardType),
            Requirement(perk),
            KeyDate(perk),
            RowStatus(entry.Assessment.Status),
            perk.Discrepancies.Count > 0
                ? "Official wording disagrees — see details."
                : perk.LastCheckIncomplete ? "The latest check could not read the page fully — see details." : null,
            Details(entry, terms));
    }

    public static string TypeLabel(PassPerkRewardType type) => type switch
    {
        PassPerkRewardType.ActivePassholder => "Monthly perk",
        PassPerkRewardType.VisitBased => "Visit-based reward",
        PassPerkRewardType.LoyaltyCredit => "Loyalty credit",
        PassPerkRewardType.Partnership => "Partner reward",
        PassPerkRewardType.Giveaway => "Giveaway",
        _ => "Passholder offer",
    };

    public static string CategoryText(PassPerkCategory category) => category switch
    {
        PassPerkCategory.FoodAndBeverage => "Food & beverage",
        PassPerkCategory.Retail => "Retail",
        PassPerkCategory.Games => "Games",
        PassPerkCategory.Lodging => "Lodging",
        PassPerkCategory.Admission => "Admission",
        PassPerkCategory.Experience => "Experience",
        _ => "Reward",
    };

    /// <summary>Which of the official earning paths applies — only what this perk's own text confirms.</summary>
    public static string Requirement(OfficialPassPerk perk)
    {
        ArgumentNullException.ThrowIfNull(perk);
        if (perk.RequiredVisits is { } visits)
        {
            return visits == 1 ? "Requires 1 qualifying visit" : $"Requires {visits} qualifying visits";
        }

        if (perk.RequiresQualifyingActivity)
        {
            return "Requires qualifying activity";
        }

        if (perk.PromoCode is { } code)
        {
            return $"Promo code {code}";
        }

        if (perk.NamedPassTypes.Count > 0)
        {
            return $"{string.Join(" and ", perk.NamedPassTypes)} passholders";
        }

        return "Available to eligible passholders";
    }

    /// <summary>The perk's confirmed key dates for a row, using the official month and day ("Redeem Nov 27 – Jan 3").</summary>
    public static string? KeyDate(OfficialPassPerk perk)
    {
        ArgumentNullException.ThrowIfNull(perk);
        var parts = new List<string>();
        if (perk.QualifyingPeriod is { } qualifying)
        {
            parts.Add("Visit by " + Short(qualifying.End));
        }

        if (perk.DateOf(PassPerkDateRole.BookingDeadline) is { } booking)
        {
            parts.Add("Book by " + Short(booking.End));
        }

        if (perk.Redemption is { } redemption)
        {
            parts.Add(PassPerkCalendar.IsMonthLong(perk, redemption)
                ? $"Valid all month ({Short(redemption.Start!.Value)} – {Short(redemption.End)})"
                : redemption.Start is { } start ? $"Redeem {Short(start)} – {Short(redemption.End)}" : "Redeem through " + Short(redemption.End));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static string? RowStatus(PassPerkStatus status) => status switch
    {
        PassPerkStatus.Upcoming => "Upcoming",
        PassPerkStatus.Expired => "Expired",
        PassPerkStatus.NoLongerListed => "No longer listed",
        PassPerkStatus.NotReconfirmed => "Not re-confirmed",
        _ => null,
    };

    public static string CalendarKindText(PassPerkCalendarEntryKind kind) => kind switch
    {
        PassPerkCalendarEntryKind.RedeemableThisDay => "Redeemable this day",
        PassPerkCalendarEntryKind.RedemptionBegins => "Redemption begins",
        PassPerkCalendarEntryKind.RedemptionEnds => "Last day to redeem",
        PassPerkCalendarEntryKind.BookingDeadline => "Last day to book",
        PassPerkCalendarEntryKind.QualifyingEnds => "Last day for qualifying visits",
        _ => "Pass Perk date",
    };

    /// <summary>The View details dialog for one perk.</summary>
    public static OfficialDetailsPresentation Details(PassPerkEntry entry, PassPerksProgramTerms? terms)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var perk = entry.Perk;
        var sections = new List<OfficialDetailsSection>();

        Add(sections, "Reward", perk.OfficialParagraphs.Select(p => new OfficialDetailsLine(p)));

        var who = new List<OfficialDetailsLine>();
        if (perk.NamedPassTypes.Count > 0)
        {
            who.Add(new OfficialDetailsLine("Pass types named in this perk: " + string.Join(", ", perk.NamedPassTypes)));
        }

        who.AddRange(perk.EligibilityLines.Select(l => new OfficialDetailsLine($"“{l}”", Note: "Official wording in this perk")));
        if (terms?.EligibilityStatement is { } eligible)
        {
            who.Add(new OfficialDetailsLine(eligible, Note: "Program terms (apply to every perk)", IsMuted: true));
        }

        if (terms?.IneligibilityStatement is { } ineligible)
        {
            who.Add(new OfficialDetailsLine(ineligible, Note: "Program terms (apply to every perk)", IsMuted: true));
        }

        Add(sections, "Eligible passholders", who);

        var earning = new List<OfficialDetailsLine>();
        switch (perk.RewardType)
        {
            case PassPerkRewardType.VisitBased:
                earning.Add(new OfficialDetailsLine(perk.RequiredVisits is { } n
                    ? $"Requires {n} qualifying {(n == 1 ? "visit" : "visits")} (stated in this perk)."
                    : "Requires qualifying activity (stated in this perk)."));
                if (perk.QualifyingPeriod is { } q)
                {
                    earning.Add(DateLine("Qualifying period", q));
                }

                if (terms?.VisitCountingRule is { } counting)
                {
                    earning.Add(new OfficialDetailsLine(counting, Note: "Program terms: how visits are counted", IsMuted: true));
                }

                break;
            case PassPerkRewardType.ActivePassholder:
                earning.Add(new OfficialDetailsLine("Available to eligible passholders — this perk states no visit requirement."));
                break;
            case PassPerkRewardType.LoyaltyCredit:
                earning.Add(new OfficialDetailsLine("A promotional credit for in-park purchases (Pass Perks Loyalty Rewards)."));
                break;
            case PassPerkRewardType.Partnership:
                earning.Add(new OfficialDetailsLine("Managed by a partner organization; no park visit is required (Pass Perks Partnership Rewards terms)."));
                break;
            case PassPerkRewardType.Giveaway:
                earning.Add(new OfficialDetailsLine("A giveaway — winners are drawn; see the official giveaway rules."));
                break;
            default:
                if (perk.PromoCode is not null)
                {
                    earning.Add(new OfficialDetailsLine("A passholder booking offer used with the promo code below; no visit requirement is stated."));
                }

                break;
        }

        Add(sections, "How it is earned", earning);

        var loading = new List<OfficialDetailsLine>();
        if (perk.RewardType == PassPerkRewardType.VisitBased && terms?.VisitRewardTimingRule is { } timing)
        {
            loading.Add(new OfficialDetailsLine(timing, Note: "Program terms: visit-based rewards", IsMuted: true));
        }
        else if (perk.RewardType == PassPerkRewardType.ActivePassholder && terms?.LoadingNote is { } note)
        {
            loading.Add(new OfficialDetailsLine(note, Note: "Carowinds Perks & Play page", IsMuted: true));
        }

        Add(sections, "Reward loading", loading);

        Add(sections, "Dates", perk.Dates.Select(d => DateLine(RoleText(d.Role), d)));

        var where = perk.LocationLines.Select(l => new OfficialDetailsLine($"“{l}”")).ToList();
        if (perk.PromoCode is { } code)
        {
            where.Add(new OfficialDetailsLine("Promo code: " + code));
        }

        if (terms?.HomeParkRule is { } homePark)
        {
            where.Add(new OfficialDetailsLine(homePark, Note: "Program FAQ", IsMuted: true));
        }

        Add(sections, "Participating locations", where);

        Add(sections, "Usage limits", perk.UsageLimitLines.Select(l => new OfficialDetailsLine($"“{l}”")));
        Add(sections, "Fees, exclusions and conditions", perk.RestrictionLines.Select(l => new OfficialDetailsLine(l)));

        var disagreements = perk.Discrepancies.Concat(terms?.Discrepancies ?? []).ToList();
        if (disagreements.Count > 0)
        {
            sections.Add(new OfficialDetailsSection("Official sources disagree", disagreements.Select(d => new OfficialDetailsLine(d)).ToList(), IsWarning: true));
        }

        if (perk.PreviousVersions.Count > 0)
        {
            sections.Add(new OfficialDetailsSection("Earlier official wording", perk.PreviousVersions
                .Select(v => new OfficialDetailsLine(
                    string.Join(" ", new[] { v.DateText }.Concat(v.OfficialParagraphs).Where(s => !string.IsNullOrWhiteSpace(s))),
                    Note: $"Listed {Day(v.FirstSeenAt)} to {Day(v.LastVerifiedAt)}",
                    IsMuted: true))
                .ToList()));
        }

        sections.Add(new OfficialDetailsSection("Your pass", [new OfficialDetailsLine(IndividualPassNote)]));

        var sources = new List<Uri> { perk.SourceUrl };
        sources.AddRange((terms?.SourceUrls ?? []).Where(u => u != perk.SourceUrl));

        return new OfficialDetailsPresentation(
            perk.Title,
            string.Join(" · ", new[] { TypeLabel(perk.RewardType), CategoryText(perk.Category), perk.RewardText, $"{perk.Campaign.OfficialHeading} ({perk.Campaign.Year})" }.Where(s => !string.IsNullOrWhiteSpace(s))),
            null,
            [],
            $"{StatusWord(entry.Assessment.Status)} — {entry.Assessment.Reason}",
            entry.Assessment.Status != PassPerkStatus.Current,
            sections,
            perk.HasInferredYear || perk.Campaign.YearInferred
                ? $"Years are not printed on the official Carowinds page. The year shown comes from the month list this perk appeared under (“{perk.Campaign.OfficialHeading}”, checked {Day(perk.FirstSeenAt)}); the month and day are official."
                : null,
            null,
            $"First listed {Day(perk.FirstSeenAt)} · Last verified {SeasonContextPresentation.When(perk.LastVerifiedAt)}",
            sources);
    }

    /// <summary>The View details dialog for the program-wide terms (eligibility, loading, visits, home park).</summary>
    public static OfficialDetailsPresentation? ProgramTermsDetails(PassPerksProgramTerms? terms)
    {
        if (terms is null || terms.IsEmpty)
        {
            return null;
        }

        var sections = new List<OfficialDetailsSection>();
        Add(sections, "Eligibility", new[]
        {
            terms.EligibilityStatement is { } e ? new OfficialDetailsLine(e, Note: "Pass Perks Terms and Conditions") : null,
            terms.IneligibilityStatement is { } i ? new OfficialDetailsLine(i, Note: "Pass Perks Terms and Conditions") : null,
            terms.FaqEligibilityAnswer is { } f ? new OfficialDetailsLine(f, Note: "Perks & Play FAQ") : null,
        }.OfType<OfficialDetailsLine>());
        if (terms.Discrepancies.Count > 0)
        {
            sections.Add(new OfficialDetailsSection("Official sources disagree", terms.Discrepancies.Select(d => new OfficialDetailsLine(d)).ToList(), IsWarning: true));
        }

        Add(sections, "Reward loading", new[] { terms.LoadingNote, terms.VisitRewardTimingRule }.OfType<string>().Select(t => new OfficialDetailsLine(t)));
        Add(sections, "Visits and home park", new[] { terms.VisitCountingRule, terms.HomeParkRule }.OfType<string>().Select(t => new OfficialDetailsLine(t)));
        Add(sections, "Reward types defined by the terms", terms.DefinedRewardTypes.Select(t => new OfficialDetailsLine(t)));
        sections.Add(new OfficialDetailsSection("Your pass", [new OfficialDetailsLine(IndividualPassNote)]));

        return new OfficialDetailsPresentation(
            "Pass Perks program terms",
            terms.TermsUpdatedText is { } updated ? $"Terms {updated[0].ToString().ToLowerInvariant()}{updated[1..]}" : null,
            null,
            [],
            null,
            false,
            sections,
            null,
            null,
            terms.VerifiedAt is { } at ? $"Last verified {SeasonContextPresentation.When(at)}" : "Previously verified",
            terms.SourceUrls);
    }

    private static string StatusWord(PassPerkStatus status) => status switch
    {
        PassPerkStatus.Current => "Current",
        PassPerkStatus.Upcoming => "Upcoming",
        PassPerkStatus.Expired => "Expired",
        PassPerkStatus.NoLongerListed => "No longer listed",
        _ => "Previously verified",
    };

    private static string RoleText(PassPerkDateRole role) => role switch
    {
        PassPerkDateRole.Redemption => "Redemption",
        PassPerkDateRole.QualifyingVisits => "Qualifying visits",
        PassPerkDateRole.BookingDeadline => "Book by",
        _ => "Stay dates",
    };

    private static OfficialDetailsLine DateLine(string label, PassPerkDateWindow window) => new(
        window.Start is { } start ? $"{label}: {Day(start)} – {Day(window.End)}" : $"{label}: {Day(window.End)}",
        Note: window.YearInferred
            ? $"Official wording: “{window.OfficialText}” (year assigned from the month list)"
            : $"Official wording: “{window.OfficialText}”");

    private static void Add(List<OfficialDetailsSection> sections, string heading, IEnumerable<OfficialDetailsLine> lines)
    {
        var list = lines.ToList();
        if (list.Count > 0)
        {
            sections.Add(new OfficialDetailsSection(heading, list));
        }
    }

    private static string Short(DateOnly date) => date.ToString("MMM d", PresentationCulture.UnitedStates);

    private static string Day(DateOnly date) => date.ToString("MMM d, yyyy", PresentationCulture.UnitedStates);

    private static string Day(DateTimeOffset instant) => Day(DateOnly.FromDateTime(Application.Time.EasternTimeZone.ConvertFromUtc(instant).DateTime));
}
