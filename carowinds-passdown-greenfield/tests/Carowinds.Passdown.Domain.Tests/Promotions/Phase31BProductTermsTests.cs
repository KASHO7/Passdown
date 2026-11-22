using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Domain.Tests.Promotions;

/// <summary>Phase 31B: official product terms — classification, separate deadlines, footnotes, and status by date.</summary>
public class Phase31BProductTermsTests
{
    private static readonly DateOnly ObservedOn = new(2026, 11, 21);

    private static readonly CampaignDeadline BlackFriday = new(
        "Black Friday Deal", new OfficialDateStatement(new DateOnly(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true));

    // The official shared fine-print modal, verbatim (captured 2026-11-21).
    private static readonly string[] SharedFinePrint =
    [
        "Pass benefits are subject to change without notice. Pricing shown is available online only. Passholder benefits may vary by park. Contact the park you are planning to visit for any questions regarding applicable benefits",
        "*Valid for general parking at Canada's Wonderland & Knott's Berry Farm and preferred parking at all other Six Flags parks with the purchase of a Prestige Pass.",
        "**Must first visit the park at which the pass was originally issued, otherwise known as your Home Park, before visiting other parks. Not valid for Knott's Scary Farm, special ticket events, or park buyout admission.",
        "^Purchase a Season Pass during the Black Friday Deal and receive one (1) Free Bring-A-Friend at Carowinds, valid on any public operating day through 01/03/2027.",
        "+Enroll in a Membership during the Black Friday Deal and receive one (1) Free Bring-A-Friend to be used at Carowinds on any public operating day through 01/03/2027.",
    ];

    private static IReadOnlyList<ProductDetailStatement> Read(string? description, string[] labels, string[]? fees = null, string[]? finePrint = null) =>
        OfficialProductTermsReader.Read(new ProductTermsSource(description, null, labels, fees ?? [], finePrint ?? [], [BlackFriday]), ObservedOn);

    [Fact]
    public void BlackFridayPassPerk_KeepsThePurchaseDeadlineAndTheRedemptionDeadlineSeparate()
    {
        var statements = Read("For the practical guest who wants park access and parking.\n\nBuy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.", []);

        var perk = Assert.Single(statements);
        Assert.Equal(ProductDetailKind.BringAFriend, perk.Kind);
        Assert.Equal("Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.", perk.Text);
        Assert.Equal(new DateOnly(2026, 11, 27), perk.PurchaseDeadline!.Date);
        Assert.Equal(DeadlineSource.CampaignBanner, perk.PurchaseDeadline.Source);
        Assert.Equal(new DateOnly(2027, 1, 3), perk.RedemptionDeadline!.Date);
    }

    [Fact]
    public void FootnotedFinePrint_IsAttachedOnlyWhenTheCardReferencesTheMarker()
    {
        var statements = Read("Exclusive season pass savings for active and retired military.", ["Admission (All East Coast Parks)**", "Free General Parking*", "Bring-A-Friend Discount"], finePrint: SharedFinePrint);

        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Restriction, FootnoteMarker: "**" });
        Assert.Contains(statements, s => s.FootnoteMarker == "*");
        Assert.DoesNotContain(statements, s => s.FootnoteMarker is "^" or "+");
        Assert.DoesNotContain(statements, s => s.RedemptionDeadline is not null);
        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.BringAFriend, Text: "Bring-A-Friend Discount" });
        Assert.Contains(statements, s => s.Kind == ProductDetailKind.Eligibility);
    }

    [Fact]
    public void ACardReferencingTheCaretFootnote_GetsThatPerk_WithItsRedemptionDate()
    {
        var statements = Read(null, ["Bring-A-Friend^"], finePrint: SharedFinePrint);

        var perk = Assert.Single(statements, s => s.FootnoteMarker == "^");
        Assert.Equal(ProductDetailKind.BringAFriend, perk.Kind);
        Assert.Equal(new DateOnly(2027, 1, 3), perk.RedemptionDeadline!.Date);
        Assert.Equal(new DateOnly(2026, 11, 27), perk.PurchaseDeadline!.Date); // "during the Black Friday Deal"
        Assert.DoesNotContain(statements, s => s.FootnoteMarker == "+");
    }

    [Fact]
    public void ResortTerms_ReadABookByDeadline_AStayWindow_AndBlackoutRestrictions()
    {
        var statements = Read(null, ["Stays 11/2/26 – 12/30/27 "],
            fees: ["Terms and conditions apply. See Booking Portal for details. Book by November 30, 2026. Valid on select nights November 2, 2026 – December 30, 2027. Subject to availability. Blackout dates may apply."]);

        var booking = Assert.Single(statements, s => s.PurchaseDeadline is not null);
        Assert.Equal(new DateOnly(2026, 11, 30), booking.PurchaseDeadline!.Date);
        Assert.False(booking.PurchaseDeadline.YearInferred);
        Assert.Equal(new OfficialDateRange(new DateOnly(2026, 11, 2), new DateOnly(2027, 12, 30), "November 2, 2026 – December 30, 2027"), booking.ValidityWindow);
        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Restriction, Text: "Blackout dates may apply." });
        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Restriction, Text: "Subject to availability." });
        Assert.Contains(statements, s => s.Kind == ProductDetailKind.VisitValidity && s.ValidityWindow is { End.Year: 2027 });
    }

    [Fact]
    public void FeesAndEligibility_AreClassified_AndOrdinaryBenefitsAreNotImportantDetails()
    {
        var statements = Read("Exclusive discount for season passholders.", ["Park Admission", "Passholder Exclusive"],
            fees: ["*Plus processing fee of up to $9.99 & applicable taxes. Must be accompanied by a valid Season Passholder."]);

        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Fee, Text: "Plus processing fee of up to $9.99 & applicable taxes." });
        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Eligibility, Text: "Must be accompanied by a valid Season Passholder." });
        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Eligibility, Text: "Passholder Exclusive" });
        Assert.DoesNotContain(statements, s => s.Text == "Park Admission");
    }

    [Fact]
    public void FootnoteMarkersInsideADisclosure_AreNotShownInTheStatementText()
    {
        var statements = Read(null, [], fees: ["*Plus a processing fee of up to $9.99 & applicable taxes. Offer available online only. †Haunted mazes are an additional fee. Haunted Attraction Pass required."]);

        Assert.Contains(statements, s => s is { Kind: ProductDetailKind.Fee, Text: "Haunted mazes are an additional fee." });
        Assert.DoesNotContain(statements, s => s.Text.StartsWith('†') || s.Text.StartsWith('*'));
    }

    [Fact]
    public void MissingTerms_ProduceNoStatements_AndNoDateIsEverInvented()
    {
        Assert.Empty(Read("A great day out with family and friends.", ["Park Admission", "Waterpark Admission"]));

        var undated = Assert.Single(Read("Buy during the Spring Sale and get a FREE Bring-A-Friend ticket.", []));
        Assert.Null(undated.PurchaseDeadline); // no banner for "Spring Sale" — nothing borrowed from Black Friday
        Assert.Null(undated.RedemptionDeadline);
    }

    [Theory]
    [InlineData("Use by 1/3/27.", 2027, 1, 3)]
    [InlineData("Redeem by January 3, 2027.", 2027, 1, 3)]
    [InlineData("valid on any public operating day through 01/03/2027.", 2027, 1, 3)]
    [InlineData("to be used at Carowinds on any public operating day through 01/03/2027.", 2027, 1, 3)]
    public void RedemptionDeadline_IsReadFromOfficialUseWording(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), OfficialOfferText.FindRedemptionDeadline(text, ObservedOn)!.Date);
    }

    [Fact]
    public void ABookByDate_IsNeverARedemptionDeadline()
    {
        Assert.Null(OfficialOfferText.FindRedemptionDeadline("Book by November 30, 2026.", ObservedOn));
    }

    // ----- status by date -----

    private static readonly DateTimeOffset VerifiedAt = new(2026, 11, 21, 16, 0, 0, TimeSpan.FromHours(-5));

    private static OfficialProductDetails Details(params ProductDetailStatement[] statements) => new()
    {
        DetailsId = "details:SeasonPasses:gold",
        Title = "Gold Tier",
        PrimarySourceUrl = new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"),
        Statements = statements,
        FirstSeenAt = VerifiedAt,
        LastVerifiedAt = VerifiedAt,
    };

    private static ProductDetailStatement Perk => new()
    {
        Kind = ProductDetailKind.BringAFriend,
        Text = "Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.",
        PurchaseDeadline = BlackFriday.End,
        RedemptionDeadline = new OfficialDateStatement(new DateOnly(2027, 1, 3), "Use by 1/3/27.", DeadlineSource.OfferTerms, false),
    };

    [Fact]
    public void BeforeTheDeadline_ThePerkIsCurrent_AndTheDeadlineDayItselfIsStillValid()
    {
        var details = Details(Perk);
        Assert.Equal(ProductDetailStatus.Current, ProductDetailStatusEvaluator.Evaluate(details, Perk, ObservedOn, VerifiedAt, VerifiedAt.AddHours(1)).Status);
        Assert.Equal(ProductDetailStatus.Current, ProductDetailStatusEvaluator.Evaluate(details, Perk, new DateOnly(2026, 11, 27), VerifiedAt, VerifiedAt.AddHours(1)).Status);
    }

    [Fact]
    public void AfterAnInferredPurchaseDeadline_ThePerkIsNotConfirmed_NeverExpiredOnThatEvidenceAlone()
    {
        var assessment = ProductDetailStatusEvaluator.Evaluate(Details(Perk), Perk, new DateOnly(2026, 11, 28), VerifiedAt, VerifiedAt.AddHours(1));

        Assert.Equal(ProductDetailStatus.Stale, assessment.Status);
        Assert.Contains("not confirmed as ended", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("inferred from the page's campaign end date", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("January 3, 2027", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterAConfirmedPurchaseDeadline_NewPurchasesNoLongerQualify_ButEarnedTicketsStayUsable()
    {
        var confirmed = Perk with
        {
            PurchaseDeadline = new OfficialDateStatement(new DateOnly(2026, 11, 27), "Buy by November 27, 2026.", DeadlineSource.OfferTerms),
        };

        var assessment = ProductDetailStatusEvaluator.Evaluate(Details(confirmed), confirmed, new DateOnly(2026, 11, 28), VerifiedAt, VerifiedAt.AddHours(1));

        Assert.Equal(ProductDetailStatus.Expired, assessment.Status);
        Assert.Contains("No longer offered with new purchases", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("January 3, 2027", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterAnInferredPurchaseDeadline_ALaterCheckThatNoLongerListsTheCard_IsExpired()
    {
        var nov28 = new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.FromHours(-5));

        var assessment = ProductDetailStatusEvaluator.Evaluate(Details(Perk), Perk, new DateOnly(2026, 11, 28), nov28, nov28.AddHours(1));

        Assert.Equal(ProductDetailStatus.Expired, assessment.Status);
        Assert.Contains("No longer listed", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInferredUseByDate_NeverExpiresTheStatementByItself()
    {
        var inferredUse = Perk with
        {
            PurchaseDeadline = null,
            RedemptionDeadline = new OfficialDateStatement(new DateOnly(2027, 1, 3), "Use by 1/3.", DeadlineSource.OfferTerms, YearInferred: true),
        };

        var assessment = ProductDetailStatusEvaluator.Evaluate(Details(inferredUse), inferredUse, new DateOnly(2027, 1, 4), VerifiedAt, VerifiedAt.AddHours(1));

        Assert.Equal(ProductDetailStatus.Stale, assessment.Status);
        Assert.Contains("use-by date January 3, 2027 (year not stated; inferred)", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterTheRedemptionDeadline_ThePerkIsExpired()
    {
        var assessment = ProductDetailStatusEvaluator.Evaluate(Details(Perk), Perk, new DateOnly(2027, 1, 4), VerifiedAt, VerifiedAt.AddHours(1));

        Assert.Equal(ProductDetailStatus.Expired, assessment.Status);
        Assert.Contains("use-by date", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleUnverifiedAndNoLongerListed_AreDistinguished()
    {
        var plain = new ProductDetailStatement { Kind = ProductDetailKind.Fee, Text = "Plus processing fee of up to $9.99 & applicable taxes." };

        Assert.Equal(ProductDetailStatus.Stale,
            ProductDetailStatusEvaluator.Evaluate(Details(plain), plain, ObservedOn, VerifiedAt, VerifiedAt.AddDays(3)).Status);
        Assert.Equal(ProductDetailStatus.Unverified,
            ProductDetailStatusEvaluator.Evaluate(Details(plain) with { LastCheckUnreadable = true }, plain, ObservedOn, VerifiedAt, VerifiedAt.AddHours(1)).Status);
        Assert.Equal(ProductDetailStatus.Expired,
            ProductDetailStatusEvaluator.Evaluate(Details(plain), plain, ObservedOn, VerifiedAt.AddDays(1), VerifiedAt.AddDays(1)).Status);
    }
}
