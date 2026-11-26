using System.Net;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.PassPerks;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 32: <see cref="PassPerksSource"/> against fixtures trimmed from the official Carowinds Perks &amp; Play
/// page, the program FAQ and the Pass Perks terms as published on 2026-11-21 (fixtures/phase32). Variants
/// (failed pages, untitled rewards, a later month) are derived here by text substitution. No network access.
/// </summary>
public class Phase32PassPerksSourceTests
{
    private static readonly DateTimeOffset Nov21 = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private static string Park => FixtureFile.ReadText("phase32/carowinds-perks-and-play.html");

    private static RetrievalHttpResponse Html(string body) => new(HttpStatusCode.OK, "text/html", body);

    private static RetrievalHttpResponse Fixture(string name) => Html(FixtureFile.ReadText("phase32/" + name));

    private static RetrievalHttpResponse Status(HttpStatusCode code) => new(code, "text/html", "<html></html>");

    /// <summary>Park page, then terms, then FAQ — the source's request order.</summary>
    private static StubHttpClient Client(RetrievalHttpResponse? park = null, RetrievalHttpResponse? terms = null, RetrievalHttpResponse? faq = null) => new(
    [
        park ?? Html(Park),
        terms ?? Fixture("pass-perks-terms.html"),
        faq ?? Fixture("perks-and-play-faq.html"),
    ]);

    private static Task<SourceResult<PassPerksObservation>> RetrieveAsync(StubHttpClient client, DateTimeOffset? now = null) =>
        new PassPerksSource(client, new FakeClock(now ?? Nov21)).RetrieveAsync(new RetrievalRequest("pass-perks.official", SourceCategory.PassPerks), CancellationToken.None);

    /// <summary>The fixture's JSON lives inside a JS string, so its quotes are escaped as \".</summary>
    private static string Q(string json) => json.Replace("\"", "\\\"", StringComparison.Ordinal);

    [Fact]
    public async Task OfficialPage_YieldsTheNovember2026Perks()
    {
        var client = Client();

        var result = await RetrieveAsync(client);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var observation = result.Payload!;
        Assert.Equal(new PassPerkCampaign(2026, 11, "November Perks", YearInferred: true), observation.Campaign);
        Assert.True(observation.IsComplete);
        Assert.Equal(
            [
                "BOGO 50% Off Churro",
                "BOGO 50% Off Second Night at Carowinds Camp Wilderness",
                "BOGO 50% Off Soft Pretzel",
                "Buy Two, Get One FREE Hot Chocolate Mug",
                "Earn 2 FREE Bring-A-Friend Tickets",
                "Save 20% When You Spend $50+ On Retail",
                "20% Off Game Play",
            ],
            observation.Perks.Select(p => p.Title).Order(StringComparer.Ordinal).ToArray().OrderBy(t => char.IsDigit(t[0])).ToArray());
        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(PassPerksSource.ParkPageUrl, client.Requests[0].Uri);
        Assert.Equal("November Perks: 7 Pass Perks verified.", result.Evidence.Detail);
        Assert.All(observation.Perks, p => Assert.Equal(PassPerksSource.ParkPageUrl, p.SourceUrl));
    }

    [Fact]
    public async Task VisitBasedCampaign_KeepsItsSeparateEarningAndRedemptionWindows()
    {
        var perk = (await RetrieveAsync(Client())).Payload!.Perks.Single(p => p.Title == "Earn 2 FREE Bring-A-Friend Tickets");

        Assert.Equal(PassPerkRewardType.VisitBased, perk.RewardType);
        Assert.Equal(PassPerkCategory.Admission, perk.Category);
        Assert.Equal(2, perk.RequiredVisits);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 22)), (perk.QualifyingPeriod!.Start, perk.QualifyingPeriod.End));
        Assert.Equal((new DateOnly(2026, 12, 5), new DateOnly(2027, 1, 3)), (perk.Redemption!.Start, perk.Redemption.End));
        Assert.True(perk.Redemption.YearInferred);
        Assert.Null(perk.DateText); // a featured block has no separate date line
        Assert.Equal("perk:2026-11:earn-2-free-bring-a-friend-tickets", perk.PerkId);
    }

    [Fact]
    public async Task MonthlyItems_AreActivePassholderPerksWithTheirOwnDates()
    {
        var perks = (await RetrieveAsync(Client())).Payload!.Perks;

        var pretzel = perks.Single(p => p.Title == "BOGO 50% Off Soft Pretzel");
        Assert.Equal(PassPerkRewardType.ActivePassholder, pretzel.RewardType);
        Assert.Equal(PassPerkCategory.FoodAndBeverage, pretzel.Category);
        Assert.Equal("November 1 - November 30", pretzel.DateText);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), (pretzel.Redemption!.Start, pretzel.Redemption.End));
        Assert.Null(pretzel.RequiredVisits);
        Assert.Contains("one (1)", pretzel.UsageLimitLines);
        Assert.Contains("at your home park", pretzel.LocationLines, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Pass Perks Terms & Conditions apply.", pretzel.RestrictionLines);
        Assert.Equal("BOGO 50% Off", pretzel.RewardText);

        var mug = perks.Single(p => p.Title == "Buy Two, Get One FREE Hot Chocolate Mug");
        Assert.Equal((new DateOnly(2026, 11, 27), new DateOnly(2027, 1, 3)), (mug.Redemption!.Start, mug.Redemption.End)); // crosses the year

        Assert.Equal(PassPerkCategory.Retail, perks.Single(p => p.Title.StartsWith("Save 20%", StringComparison.Ordinal)).Category);
        Assert.Equal(PassPerkCategory.Games, perks.Single(p => p.Title == "20% Off Game Play").Category);
    }

    [Fact]
    public async Task LodgingPerk_HasABookingDeadlineAndStayWindowButNoRedemptionWindow()
    {
        var camp = (await RetrieveAsync(Client())).Payload!.Perks.Single(p => p.Category == PassPerkCategory.Lodging);

        Assert.Equal("Book by November 30", camp.DateText);
        Assert.Equal(new DateOnly(2026, 11, 30), camp.DateOf(PassPerkDateRole.BookingDeadline)!.End);
        Assert.Equal((new DateOnly(2026, 10, 31), new DateOnly(2027, 4, 25)), (camp.DateOf(PassPerkDateRole.StayWindow)!.Start, camp.DateOf(PassPerkDateRole.StayWindow)!.End));
        Assert.Null(camp.Redemption);
        Assert.Equal("NOVPASS", camp.PromoCode);
        Assert.Equal(PassPerkRewardType.Other, camp.RewardType); // a booking code, not a pass-loaded reward
        Assert.Contains("New Reservations", camp.EligibilityLines);
        Assert.DoesNotContain(camp.OfficialParagraphs, p => p == "Book Now"); // bare link label dropped
    }

    [Fact]
    public async Task YearRoundBenefitsAndAppPromotion_AreNotPassPerks()
    {
        var perks = (await RetrieveAsync(Client())).Payload!.Perks;

        Assert.DoesNotContain(perks, p => p.Title.Contains("Parking", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(perks, p => p.Title.Contains("Everyday Savings", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(perks, p => p.Title.StartsWith("Now Packed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProgramTerms_KeepOfficialSentencesAndShowTheEligibilityDiscrepancy()
    {
        var terms = (await RetrieveAsync(Client())).Payload!.ProgramTerms!.FillFrom(null);

        Assert.Equal("Updated July 30, 2026", terms.TermsUpdatedText);
        Assert.Contains("REGULAR, GOLD, AND PRESTIGE", terms.EligibilityStatement, StringComparison.Ordinal);
        Assert.Contains("NOT ELIGIBLE", terms.IneligibilityStatement, StringComparison.Ordinal);
        Assert.Equal("All Season Passholders and Memberships, excluding the Pre-K Pass, KidZPass, and the Knott's Scary Farm Pass.", terms.FaqEligibilityAnswer);
        Assert.StartsWith("Perks & Play Loyalty Rewards are loaded to Season Passholders who hold an active pass on the first day of the month.", terms.LoadingNote, StringComparison.Ordinal);
        Assert.Contains("one Park per calendar day", terms.VisitCountingRule, StringComparison.Ordinal);
        Assert.Contains("at the conclusion of the promotional period", terms.VisitRewardTimingRule, StringComparison.Ordinal);
        Assert.Equal("Pass Perks can only be redeemed at your designated home park.", terms.HomeParkRule);
        Assert.Equal(["Loyalty Rewards", "Visit-Based Rewards", "Partnership Rewards", "Giveaway"], terms.DefinedRewardTypes.OrderBy(t => t switch { "Loyalty Rewards" => 0, "Visit-Based Rewards" => 1, "Partnership Rewards" => 2, _ => 3 }));
        var discrepancy = Assert.Single(terms.Discrepancies);
        Assert.Contains("KidZPass", discrepancy, StringComparison.Ordinal);
        Assert.Contains("Summer Fun", discrepancy, StringComparison.Ordinal);
        Assert.Equal([PassPerksSource.ParkPageUrl, PassPerksSource.TermsUrl, PassPerksSource.FaqUrl], terms.SourceUrls);
    }

    [Fact]
    public async Task ParkPageFailure_IsAnErrorAndStopsTheCheck()
    {
        var client = Client(park: Status(HttpStatusCode.InternalServerError));

        var result = await RetrieveAsync(client);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task NetworkException_IsAnError()
    {
        var result = await RetrieveAsync(new StubHttpClient(new HttpRequestException("offline")));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task PageWithoutStructuredData_IsAnError()
    {
        var result = await RetrieveAsync(Client(park: Html("<html><body>Perks & Play</body></html>")));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Contains("structure may have changed", result.Evidence.TechnicalDetail ?? result.Evidence.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZeroReadableRewards_IsNeverAConfirmedEmptyMonth()
    {
        // Every reward name removed and the featured campaign's visit goal reworded: the heading is still there.
        var body = Park.Replace(Q("\"heading\":\""), Q("\"label\":\""), StringComparison.Ordinal)
            .Replace("Visit twice between", "Stop by between", StringComparison.Ordinal);

        var result = await RetrieveAsync(Client(park: Html(body)));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task OneUnreadableReward_IsAPartialObservation()
    {
        var body = Park.Replace(Q("\"heading\":\"BOGO 50% Off Churro\""), Q("\"heading\":null"), StringComparison.Ordinal);

        var result = await RetrieveAsync(Client(park: Html(body)));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(1, result.Payload!.UnreadableCount);
        Assert.False(result.Payload.IsComplete);
        Assert.Equal(6, result.Payload.Perks.Count);
        Assert.Contains("1 listed reward could not be read", result.Evidence.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TermsAndFaqFailures_AreWarningsOnly()
    {
        var result = await RetrieveAsync(Client(terms: Status(HttpStatusCode.NotFound), faq: Status(HttpStatusCode.ServiceUnavailable)));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var terms = result.Payload!.ProgramTerms!;
        Assert.Null(terms.EligibilityStatement);
        Assert.Null(terms.FaqEligibilityAnswer);
        Assert.NotNull(terms.LoadingNote); // still read from the park page
        Assert.Contains(result.Payload.Warnings, w => w.Contains("404", StringComparison.Ordinal));
        Assert.Contains(result.Payload.Warnings, w => w.Contains("503", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaterMonthHeading_IsADifferentCampaignWithDifferentIdentities()
    {
        var body = Park.Replace("November Perks", "December Perks", StringComparison.Ordinal);

        var result = await RetrieveAsync(Client(park: Html(body)), new DateTimeOffset(2026, 11, 30, 18, 0, 0, TimeSpan.Zero));

        Assert.Equal((2026, 12), (result.Payload!.Campaign.Year, result.Payload.Campaign.Month));
        Assert.All(result.Payload.Perks, p => Assert.StartsWith("perk:2026-12:", p.PerkId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaleNovemberPage_CheckedInJanuary_StaysNovember2026()
    {
        var result = await RetrieveAsync(Client(), new DateTimeOffset(2027, 1, 4, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal((2026, 11), (result.Payload!.Campaign.Year, result.Payload.Campaign.Month)); // never relabelled as the current month
    }

    [Fact]
    public void DuplicateIdentities_ExactRepeatKeptOnce_DifferentRewardKeptSeparately()
    {
        var campaign = new PassPerkCampaign(2026, 11, "November Perks", YearInferred: true);
        var warnings = new List<string>();
        PassPerksPageParser.RawPerk Item(string body) => new("BOGO 50% Off Churro", "November 1 - November 30", [body], IsMonthlyListing: true);

        var perks = PassPerksSource.BuildPerks(
            [Item("Redeem one (1)."), Item("Redeem one (1)."), Item("Redeem two (2).")],
            campaign,
            Nov21,
            warnings,
            out var unreadable);

        Assert.Equal(0, unreadable);
        Assert.Equal(["perk:2026-11:bogo-50-off-churro", "perk:2026-11:bogo-50-off-churro-2"], perks.Select(p => p.PerkId));
        Assert.Single(warnings, w => w.Contains("both named", StringComparison.Ordinal));
    }
}
