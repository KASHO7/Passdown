using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.ProductDetails;

/// <summary>Phase 31B: product details documents (last-known-good, history) and matching details to Products rows.</summary>
public class Phase31BProductDetailsTests
{
    private static readonly DateTimeOffset Checked = new(2026, 11, 21, 16, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Uri SeasonPage = new("https://www.sixflags.com/carowinds/memberships-and-season-passes");
    private static readonly Uri MilitaryPage = new("https://www.sixflags.com/carowinds/military-discounts");

    private static ProductDetailStatement Statement(string text, ProductDetailKind kind = ProductDetailKind.General) => new() { Kind = kind, Text = text };

    private static OfficialProductDetails Card(string id, string title, OfficialProductChannel channel, DateTimeOffset at, params ProductDetailStatement[] statements) => new()
    {
        DetailsId = $"details:{channel}:{id}",
        Title = title,
        Channel = channel,
        OfficialId = id,
        TierName = title.EndsWith(" Tier", StringComparison.Ordinal) ? title[..^5] : null,
        Statements = statements,
        PrimarySourceUrl = channel == OfficialProductChannel.MilitaryFirstResponder ? MilitaryPage : SeasonPage,
        SourceUrls = [channel == OfficialProductChannel.MilitaryFirstResponder ? MilitaryPage : SeasonPage],
        FirstSeenAt = at,
        LastVerifiedAt = at,
    };

    // ----- document merge -----

    [Fact]
    public void ChangedOfficialTerms_ReplaceTheCurrentOnes_AndTheEarlierTermsAreKeptWithTheirVerifiedWindow()
    {
        var first = OfficialProductDetailsDocument.Empty.Merge(
            new(OfficialProductChannel.SeasonPasses, [Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked, Statement("Buy during the Fall Sale and get a FREE Bring-A-Friend ticket.", ProductDetailKind.BringAFriend))]),
            Checked);
        var later = Checked.AddDays(5);

        var second = first.Merge(
            new(OfficialProductChannel.SeasonPasses, [Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, later, Statement("Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.", ProductDetailKind.BringAFriend))]),
            later);

        var gold = Assert.Single(second.Products);
        Assert.Equal(Checked, gold.FirstSeenAt);
        Assert.Equal(later, gold.LastVerifiedAt);
        Assert.Contains("Black Friday", gold.Statements[0].Text, StringComparison.Ordinal);
        var earlier = Assert.Single(gold.PreviousTerms);
        Assert.Contains("Fall Sale", earlier.Statements[0].Text, StringComparison.Ordinal);
        Assert.Equal((Checked, Checked), (earlier.FirstSeenAt, earlier.LastVerifiedAt));

        // Re-observing the same terms adds no history.
        var third = second.Merge(new(OfficialProductChannel.SeasonPasses, [gold with { LastVerifiedAt = later.AddDays(1), PreviousTerms = [] }]), later.AddDays(1));
        Assert.Single(Assert.Single(third.Products).PreviousTerms);
    }

    [Fact]
    public void AnUnreadableCard_KeepsItsLastVerifiedDetails_FlaggedUnverified()
    {
        var doc = OfficialProductDetailsDocument.Empty.Merge(new(OfficialProductChannel.MilitaryFirstResponder, [Card("gold", "2027 Military Gold Pass", OfficialProductChannel.MilitaryFirstResponder, Checked, Statement("Exclusive season pass savings for active and retired military.", ProductDetailKind.Eligibility))]), Checked);

        var merged = doc.Merge(new(OfficialProductChannel.MilitaryFirstResponder, []) { UnreadableDetailIds = ["details:MilitaryFirstResponder:gold"] }, Checked.AddDays(1));

        var kept = Assert.Single(merged.Products);
        Assert.True(kept.LastCheckUnreadable);
        Assert.Equal(Checked, kept.LastVerifiedAt);
    }

    [Fact]
    public void ACardOnlyOnAnUnreachablePage_IsKeptUnverified_AndANoLongerListedCardIsKeptForHistoryUntilRetentionEnds()
    {
        var doc = OfficialProductDetailsDocument.Empty.Merge(new(OfficialProductChannel.SeasonPasses, [Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked)]), Checked);

        var unreachable = doc.Merge(new(OfficialProductChannel.SeasonPasses, []) { UnreachablePageUrls = [SeasonPage] }, Checked.AddDays(1));
        Assert.True(Assert.Single(unreachable.Products).LastCheckUnreadable);

        var notListed = doc.Merge(new(OfficialProductChannel.SeasonPasses, []), Checked.AddDays(1));
        Assert.False(Assert.Single(notListed.Products).LastCheckUnreadable);

        Assert.Empty(doc.Merge(new(OfficialProductChannel.SeasonPasses, []), Checked.AddDays(401)).Products);
    }

    // ----- registration (refresh) -----

    private static CatalogSourceRegistration Registration(
        Func<SourceResult<CatalogPayload>> result, InMemoryOfficialReferenceDocumentRepository documents, OfficialProductChannel channel = OfficialProductChannel.MilitaryFirstResponder)
    {
        var source = new FakeOfficialDataSource<CatalogPayload>("military-first-responder.official", SourceCategory.MilitaryFirstResponder, (_, _) => Task.FromResult(result()));
        var reconciliation = new ProductReconciliationService(new InMemoryCatalogProductRepository(), new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());
        return new CatalogSourceRegistration(source, reconciliation, productDetailsDocuments: documents, productDetailsChannel: channel);
    }

    [Fact]
    public async Task ASuccessfulCheck_StoresTheChannelsDetails_AndAFailedCheckNeverOverwritesThem()
    {
        var documents = new InMemoryOfficialReferenceDocumentRepository();
        var observation = new OfficialProductDetailsObservation(OfficialProductChannel.MilitaryFirstResponder,
            [Card("gold", "2027 Military Gold Pass", OfficialProductChannel.MilitaryFirstResponder, Checked, Statement("Exclusive season pass savings for active and retired military.", ProductDetailKind.Eligibility))]);

        await Registration(() => SourceResult<CatalogPayload>.Success("military-first-responder.official", FakeEvidence.Verified(Checked),
            new CatalogPayload([]) { ProductDetails = observation }), documents).ExecuteAsync(null, CancellationToken.None);
        var saved = await documents.GetAsync(OfficialProductDetailsDocumentIds.MilitaryFirstResponder);
        Assert.NotNull(saved?.SuccessPayloadJson);

        await Registration(() => SourceResult<CatalogPayload>.WithoutPayload("military-first-responder.official", FakeEvidence.Error(Checked.AddHours(1))), documents)
            .ExecuteAsync(null, CancellationToken.None);

        var afterFailure = await documents.GetAsync(OfficialProductDetailsDocumentIds.MilitaryFirstResponder);
        Assert.Equal(saved!.SuccessPayloadJson, afterFailure!.SuccessPayloadJson);
        Assert.True(afterFailure.LastAttemptFailed);
        var stored = OfficialSeasonJson.TryDeserialize<OfficialProductDetailsDocument>(afterFailure.SuccessPayloadJson)!;
        Assert.Equal("2027 Military Gold Pass", Assert.Single(stored.Products).Title);
    }

    [Fact]
    public async Task DetailsForAnotherChannel_AreNeverStoredUnderThisChannel()
    {
        var documents = new InMemoryOfficialReferenceDocumentRepository();
        var wrong = new OfficialProductDetailsObservation(OfficialProductChannel.SeasonPasses, [Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked)]);

        await Registration(() => SourceResult<CatalogPayload>.Success("military-first-responder.official", FakeEvidence.Verified(Checked),
            new CatalogPayload([]) { ProductDetails = wrong }), documents).ExecuteAsync(null, CancellationToken.None);

        Assert.Null(await documents.GetAsync(OfficialProductDetailsDocumentIds.MilitaryFirstResponder));
        Assert.Null(await documents.GetAsync(OfficialProductDetailsDocumentIds.SeasonPasses));
    }

    // ----- matching to Products rows -----

    private static AssociateProduct Product(string identity, string name, SourceCategory category, PassOffer? offer = null) =>
        new(identity, name, category, false, 99m, "$99", null, null, false, null, AssociateVerificationInfo.NotCheckedYet, true, null) { PassOffer = offer };

    private static ProductImportantDetailsIndex Index(params OfficialProductDetails[] cards) =>
        new(cards.GroupBy(c => c.Channel).ToDictionary(
                g => g.Key,
                g => (new OfficialProductDetailsDocument(g.ToList()), new OfficialDocumentStatus(OfficialDocumentFreshness.Current, Checked, Checked, null, null))),
            new DateOnly(2026, 11, 21), Checked.AddHours(1));

    [Fact]
    public void ASeasonPassAndItsMembershipRow_MatchTheirOwnTiersCard_OnTheSeasonPassesPage()
    {
        var gold = Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked, Statement("Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.", ProductDetailKind.BringAFriend));
        var silver = Card("silver", "Silver Tier", OfficialProductChannel.SeasonPasses, Checked);
        var index = Index(gold, silver);

        var goldPass = Product("catalog|" + CatalogProductKey.FromPassOffer(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027)), "Gold Season Pass", SourceCategory.SeasonPasses,
            PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027));
        var goldMembership = Product("catalog|" + CatalogProductKey.FromPassOffer(PassOffer.Membership("Gold", PassPriceType.MonthlyRate)), "Gold Membership", SourceCategory.SeasonPasses,
            PassOffer.Membership("Gold", PassPriceType.MonthlyRate));

        Assert.Equal("Gold Tier", index.Find(goldPass)!.Source.Title);
        Assert.Equal("Gold Tier", index.Find(goldMembership)!.Source.Title);
        Assert.Equal(ProductDetailStatus.Current, index.Find(goldPass)!.Lines.Single().Assessment.Status);
    }

    [Fact]
    public void TheMilitaryGoldPass_MatchesOnlyTheMilitaryCard_NeverTheRegularGoldTier()
    {
        var regularGold = Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked, Statement("Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket.", ProductDetailKind.BringAFriend));
        var militaryGold = Card("1d4e3b95", "2027 Military Gold Pass", OfficialProductChannel.MilitaryFirstResponder, Checked, Statement("Exclusive season pass savings for active and retired military.", ProductDetailKind.Eligibility)) with
        {
            Keyword = "FallMilitaryGoldSP2027",
        };
        var index = Index(regularGold, militaryGold);

        var row = Product("catalog|" + CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027"), "2027 Military Gold Pass", SourceCategory.MilitaryFirstResponder);
        var found = index.Find(row)!;

        Assert.Equal("2027 Military Gold Pass", found.Source.Title);
        Assert.DoesNotContain(found.Lines, l => l.Kind == ProductDetailKind.BringAFriend);
    }

    [Fact]
    public void ThePerDateMilitaryTicketRows_MatchTheMilitaryTicketCard()
    {
        var tickets = Card("5ca87cf6", "Military & First Responder Ticket", OfficialProductChannel.MilitaryFirstResponder, Checked, Statement("ID.me Verified", ProductDetailKind.Eligibility)) with { Keyword = "MilitaryWizard" };
        var row = Product("dated|DatedMilitaryFirstResponder|Military Ticket", "Military Ticket", SourceCategory.DatedMilitaryFirstResponder);

        Assert.Equal("Military & First Responder Ticket", Index(tickets).Find(row)!.Source.Title);
    }

    [Fact]
    public void APromotionOptionRow_MatchesItsCardByTheOptionKeyword()
    {
        var deal = Card("35dfd3cd", "Black Friday Deal", OfficialProductChannel.Promotions, Checked) with { RelatedKeywords = ["WinterSilverSP2027", "SingleDay"] };
        var row = Product("catalog|" + CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "WinterSilverSP2027"), "Black Friday Deal: Silver Pass", SourceCategory.Promotions);

        Assert.Equal("Black Friday Deal", Index(deal).Find(row)!.Source.Title);
    }

    [Fact]
    public void CustomProductsAndUncoveredCategories_HaveNoDetails()
    {
        var index = Index(Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked));

        Assert.Null(index.Find(new AssociateProduct("custom|1", "Gold Tier", null, true, 5m, "$5", null, null, false, null, AssociateVerificationInfo.NotCheckedYet, true, null)));
        Assert.Null(index.Find(Product("catalog|AddOns:id:x", "Gold Tier", SourceCategory.AddOns)));
        Assert.Null(ProductImportantDetailsIndex.Empty.Find(Product("catalog|SeasonPasses:id:y", "Gold Season Pass", SourceCategory.SeasonPasses, PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027))));
    }

    [Fact]
    public void DeadlineLinesAreListedFirst()
    {
        var card = Card("gold", "Gold Tier", OfficialProductChannel.SeasonPasses, Checked,
            Statement("Plus processing fee of up to $9.99 & applicable taxes.", ProductDetailKind.Fee),
            new ProductDetailStatement
            {
                Kind = ProductDetailKind.BringAFriend,
                Text = "Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.",
                RedemptionDeadline = new OfficialDateStatement(new DateOnly(2027, 1, 3), "Use by 1/3/27.", DeadlineSource.OfferTerms),
            });
        var row = Product("catalog|x", "Gold Season Pass", SourceCategory.SeasonPasses, PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027));

        var found = Index(card).Find(row)!;

        Assert.Equal(ProductDetailKind.BringAFriend, found.Lines[0].Kind);
        Assert.Single(found.Deadlines);
    }
}
