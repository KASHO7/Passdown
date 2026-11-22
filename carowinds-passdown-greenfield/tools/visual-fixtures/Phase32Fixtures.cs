using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain.PassPerks;

// Phase 32: synthetic Pass Perks for the Products panel, the View details dialog and the Calendar markers.
// Shaped like the official monthly list (month-long perks, a visit-based campaign, a booking code, a short
// weekend window, two perks sharing a date); every name and value is invented.
internal static class Phase32Fixtures
{
    private static readonly Uri ParkPage = new("https://www.sixflags.com/carowinds/perks-and-play");
    private static readonly DateTimeOffset Checked = new(2026, 10, 17, 7, 0, 0, TimeSpan.FromHours(-4));
    private static readonly PassPerkCampaign October = new(2026, 10, "October Perks", YearInferred: true);

    private static OfficialPassPerk Perk(string title, string? dateLine, string body, bool monthly = true) =>
        PassPerkText.Interpret(title, dateLine, [body], October, ParkPage, Checked, monthly);

    public static IPassPerksService Service()
    {
        OfficialPassPerk[] perks =
        [
            Perk("Earn a FREE Synthetic Souvenir Cup", null,
                "Visit three times between October 1 and October 25 to receive a FREE Synthetic Souvenir Cup, valid November 1 through November 30.", monthly: false),
            Perk("BOGO 50% Off Synthetic Funnel Cake", "October 1 - October 31",
                "Passholders and members can redeem one (1) BOGO 50% Off Synthetic Funnel Cake by scanning your season pass or membership at any participating location. Offer valid at your home park. Pass Perks Terms & Conditions apply."),
            Perk("Save 15% On Synthetic Retail", "October 1 - October 31",
                "Passholders and members can redeem one (1) Save 15% On Synthetic Retail by scanning your season pass or membership at any participating location. Offer valid at your home park. Pass Perks Terms & Conditions apply."),
            Perk("20% Off Synthetic Game Play", "October 1 - October 31",
                "Passholders and members can redeem one (1) 20% Off Synthetic Game Play by scanning your season pass or membership at any participating location. Offer valid at your home park."),
            Perk("Synthetic Passholder Appreciation Weekend", "October 24 - October 25",
                "Passholders and members can redeem one (1) synthetic appreciation gift at Guest Relations. Offer valid at your home park. While supplies last."),
            Perk("BOGO 50% Off Second Night at Synthetic Campground", "Book by October 31",
                "New Reservations. BOGO 50% Off Second Night when you stay at the Synthetic Campground from October 1 to March 31. Promo code SYNPASS."),
        ];

        var terms = new PassPerksProgramTerms
        {
            TermsUpdatedText = "Updated July 30, 2026",
            EligibilityStatement = "SYNTHETIC PROGRAM IS VALID FOR (I) REGULAR, GOLD, AND PRESTIGE SEASON PASSHOLDERS AND (II) MEMBERSHIPS.",
            IneligibilityStatement = "Pre-K Season Pass holders, SUMMER FUN PASS, HAUNT SEASON PASS, WILD CARD AND TICKET HOLDERS ARE NOT ELIGIBLE.",
            FaqEligibilityAnswer = "All Season Passholders and Memberships, excluding the Pre-K Pass, KidZPass, and the Knott's Scary Farm Pass.",
            LoadingNote = "Synthetic rewards are loaded to Season Passholders who hold an active pass on the first day of the month.",
            VisitCountingRule = "Passholders will only receive credit for visiting one Park per calendar day.",
            VisitRewardTimingRule = "Qualifying Passholders and Members will receive Pass Perks at the conclusion of the promotional period.",
            HomeParkRule = "Pass Perks can only be redeemed at your designated home park.",
            SourceUrls = [ParkPage, new Uri("https://www.sixflags.com/pass-perks-terms"), new Uri("https://www.sixflags.com/perks-and-play")],
            VerifiedAt = Checked,
        }.FillFrom(null);

        var document = PassPerksDocument.Empty.Merge(new PassPerksObservation(October, perks) { ProgramTerms = terms }, Checked);
        var status = new OfficialDocumentStatus(OfficialDocumentFreshness.Current, Checked, Checked, null, ParkPage);
        return new StaticService(new PassPerksOverview(document, status, new DateOnly(2026, 10, 17), Checked.AddHours(4)));
    }

    private sealed class StaticService(PassPerksOverview overview) : IPassPerksService
    {
        public Task<PassPerksOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(overview);
    }

    public sealed class MemoryCalendarStore(bool show) : ICalendarDisplayPreferenceStore
    {
        public Task<CalendarDisplayPreferences> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new CalendarDisplayPreferences(show));

        public Task SaveAsync(CalendarDisplayPreferences preferences, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
