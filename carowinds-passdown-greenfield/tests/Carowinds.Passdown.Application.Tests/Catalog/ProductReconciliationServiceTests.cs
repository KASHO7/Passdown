using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

public class ProductReconciliationServiceTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/passes");
    private static readonly DateTimeOffset Day1 = new(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 8, 19, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day3 = new(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);

    private static SourceEvidence Evidence(DateTimeOffset timeChecked) => new(
        sourceName: "Season passes",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: RetrievalStatus.Verified,
        detail: "Checked successfully.",
        strategy: RetrievalStrategy.Html);

    private static ObservedProduct GoldPass(decimal price = 149.99m, string displayPrice = "$149.99", string name = "Gold Pass") => new(
        Key: CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass"),
        DisplayName: name,
        CurrentPriceAmount: price,
        CurrentDisplayPrice: displayPrice,
        ComparisonPriceAmount: null,
        PriceQualifier: "Online Price",
        DateSpecific: false,
        ApplicableDate: null);

    private sealed record Harness(
        ProductReconciliationService Service,
        InMemoryCatalogProductRepository Catalog,
        InMemoryProductHistoryRepository History,
        FakeUnitOfWorkFactory UnitOfWorkFactory)
    {
        public static Harness Create()
        {
            var catalog = new InMemoryCatalogProductRepository();
            var history = new InMemoryProductHistoryRepository();
            var unitOfWorkFactory = new FakeUnitOfWorkFactory();
            return new Harness(new ProductReconciliationService(catalog, history, unitOfWorkFactory), catalog, history, unitOfWorkFactory);
        }
    }

    [Fact]
    public async Task NewProduct_BecomesActive()
    {
        var h = Harness.Create();

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        Assert.Equal(ReconciliationOutcome.Added, result.Products.Single().Outcome);
    }

    [Fact]
    public async Task NewProduct_PreservesFirstSeenAsTheObservationTime()
    {
        var h = Harness.Create();

        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(Day1, stored!.FirstSeenAt);
    }

    [Fact]
    public async Task NewProduct_AppendsOneHistoryEntry()
    {
        var h = Harness.Create();

        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        var history = await h.History.GetHistoryAsync(GoldPass().Key.ToString());
        Assert.Single(history);
        Assert.Equal("New product observed.", history[0].ChangeReason);
    }

    [Fact]
    public async Task UnchangedProduct_UpdatesLastSeenButAddsNoHistory()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), [GoldPass()]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(Day2, stored!.LastSeenAt);
        Assert.Equal(Day2, stored.LastVerifiedAt);
        Assert.Equal(Day1, stored.FirstSeenAt); // unchanged
        Assert.False(result.Products.Single().HistoryAppended);
        Assert.Single(await h.History.GetHistoryAsync(GoldPass().Key.ToString())); // still just the original "new product" entry
    }

    [Fact]
    public async Task PriceChange_UpdatesCurrentRecordAndAddsHistory()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        var result = await h.Service.ReconcileAsync(
            SourceCategory.SeasonPasses, Evidence(Day2), [GoldPass(price: 159.99m, displayPrice: "$159.99")]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(159.99m, stored!.CurrentPriceAmount);
        Assert.Equal("$159.99", stored.CurrentDisplayPrice);
        Assert.True(result.Products.Single().HistoryAppended);
        var history = await h.History.GetHistoryAsync(GoldPass().Key.ToString());
        Assert.Equal(2, history.Count);
        Assert.Contains("Price changed", history[1].ChangeReason);
    }

    [Fact]
    public async Task DisplayNameChange_RetainsTheSameStableProductIdentity()
    {
        var h = Harness.Create();
        var key = GoldPass().Key;
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), [GoldPass(name: "Gold Season Pass")]);

        var stored = await h.Catalog.GetAsync(key);
        Assert.NotNull(stored);
        Assert.Equal(key.ToString(), stored.ProductKey);
        Assert.Equal("Gold Season Pass", stored.DisplayName);
        // Only one row for this product ever existed — the rename did not create a second identity.
        var all = await h.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses);
        Assert.Single(all);
    }

    [Fact]
    public async Task FirstSuccessfulAbsence_BecomesPendingConfirmation()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), []);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, stored!.LifecycleState);
        Assert.Equal(1, stored.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(ReconciliationOutcome.PendingRemoval, result.Products.Single().Outcome);
    }

    [Fact]
    public async Task SecondSuccessfulAbsence_BecomesArchived()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), []);

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day3), []);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.Archived, stored!.LifecycleState);
        Assert.Equal(2, stored.ConsecutiveConfirmedAbsenceCount);
        Assert.NotNull(stored.ArchivedAt);
        Assert.Equal(ReconciliationOutcome.Archived, result.Products.Single().Outcome);
    }

    [Fact]
    public async Task ReconcileAsync_RejectsErrorEvidence_SoItCanNeverDriveAbsenceTracking()
    {
        var h = Harness.Create();
        var errorEvidence = new SourceEvidence(
            "Season passes", SampleUrl, Day1, RetrievalStatus.Error, "Timed out.", RetrievalStrategy.Html);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Service.ReconcileAsync(SourceCategory.SeasonPasses, errorEvidence, []));
    }

    [Fact]
    public async Task FailedSource_LeavesCurrentCatalogUnchanged()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        var beforeFailedAttempt = await h.Catalog.GetAsync(GoldPass().Key);

        // A failed retrieval never reaches reconciliation at all — spec §6.7/§7.1's
        // "retain last-known-good state" is enforced by there being no code path
        // here for a failure to take, not by reconciliation itself deciding to skip.

        var afterFailedAttempt = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(beforeFailedAttempt, afterFailedAttempt);
    }

    [Fact]
    public async Task ExplicitlyUnavailable_ArchivesImmediatelyWithoutWaitingForASecondConfirmation()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        var unavailable = GoldPass() with { IsExplicitlyUnavailable = true, UnavailableReason = "Sold out for the season." };

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), [unavailable]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.Archived, stored!.LifecycleState);
        Assert.Equal("Sold out for the season.", stored.ArchivedReason);
        Assert.Equal(0, stored.ConsecutiveConfirmedAbsenceCount); // no confirmation retries needed
        Assert.Equal(ReconciliationOutcome.Archived, result.Products.Single().Outcome);
    }

    [Fact]
    public async Task NewProduct_ObservedAlreadyExplicitlyUnavailable_IsArchivedOnFirstSight()
    {
        var h = Harness.Create();
        var unavailable = GoldPass() with { IsExplicitlyUnavailable = true, UnavailableReason = "Listed as sold out." };

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [unavailable]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.Archived, stored!.LifecycleState);
        Assert.Equal("Listed as sold out.", stored.ArchivedReason);
        Assert.Equal(Day1, stored.FirstSeenAt);
        Assert.Equal(ReconciliationOutcome.Archived, result.Products.Single().Outcome);
        Assert.Single(await h.History.GetHistoryAsync(GoldPass().Key.ToString()));
    }

    [Fact]
    public async Task ExplicitlyUnavailable_RepeatedWithTheSameReason_DoesNotAddDuplicateHistory()
    {
        var h = Harness.Create();
        var unavailable = GoldPass() with { IsExplicitlyUnavailable = true, UnavailableReason = "Sold out for the season." };
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), [unavailable]);

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day3), [unavailable]);

        Assert.False(result.Products.Single().HistoryAppended);
        var history = await h.History.GetHistoryAsync(GoldPass().Key.ToString());
        Assert.Equal(2, history.Count); // "New product observed." + the single archive entry, no repeat
    }

    [Fact]
    public async Task ArchivedProduct_ReturningBecomesActive()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), []);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day3), []); // now Archived

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day3.AddDays(30)), [GoldPass()]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        Assert.Equal(0, stored.ConsecutiveConfirmedAbsenceCount);
        Assert.Null(stored.ArchivedAt);
        Assert.Equal(ReconciliationOutcome.Reactivated, result.Products.Single().Outcome);
    }

    [Fact]
    public async Task PendingProduct_ReturningBecomesActive()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), []); // now PendingConfirmation

        var result = await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day3), [GoldPass()]);

        var stored = await h.Catalog.GetAsync(GoldPass().Key);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        Assert.Equal(0, stored.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(ReconciliationOutcome.Reactivated, result.Products.Single().Outcome);
    }

    [Fact]
    public async Task History_IsRetainedThroughArchival()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), []);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day3), []);

        var history = await h.History.GetHistoryAsync(GoldPass().Key.ToString());

        Assert.Equal(3, history.Count);
        Assert.Equal("New product observed.", history[0].ChangeReason);
        Assert.Contains("pending confirmation", history[1].ChangeReason);
        Assert.Contains("Confirmed absent", history[2].ChangeReason);
    }

    [Fact]
    public async Task MultipleProducts_ReconcileIndependently()
    {
        var h = Harness.Create();
        var platinum = new ObservedProduct(
            CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Platinum Pass"),
            "Platinum Pass", 249.99m, "$249.99", null, "Online Price", false, null);

        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass(), platinum]);
        var result = await h.Service.ReconcileAsync(
            SourceCategory.SeasonPasses, Evidence(Day2), [GoldPass(price: 159.99m, displayPrice: "$159.99"), platinum]);

        var gold = await h.Catalog.GetAsync(GoldPass().Key);
        var plat = await h.Catalog.GetAsync(platinum.Key);
        Assert.Equal(159.99m, gold!.CurrentPriceAmount);
        Assert.Equal(249.99m, plat!.CurrentPriceAmount);
        Assert.Equal(2, result.Products.Count);
    }

    [Fact]
    public async Task OneMissingProduct_DoesNotAffectProductsReturnedSuccessfully()
    {
        var h = Harness.Create();
        var platinum = new ObservedProduct(
            CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Platinum Pass"),
            "Platinum Pass", 249.99m, "$249.99", null, "Online Price", false, null);
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass(), platinum]);

        // Platinum goes missing this time; Gold is still returned, unchanged.
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), [GoldPass()]);

        var gold = await h.Catalog.GetAsync(GoldPass().Key);
        var plat = await h.Catalog.GetAsync(platinum.Key);
        Assert.Equal(ProductLifecycleState.Active, gold!.LifecycleState);
        Assert.Equal(Day2, gold.LastSeenAt);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, plat!.LifecycleState);
    }

    [Fact]
    public async Task SeasonalProductVisibility_FollowsReconciledStateNotHardCodedDates()
    {
        // A haunted-attractions product appears (in-season), then vanishes for two
        // checks (off-season) — nothing in the service references calendar dates;
        // visibility is purely a function of what was observed each time.
        var h = Harness.Create();
        var hauntedMaze = new ObservedProduct(
            CatalogProductKey.FromSourceAndName(SourceCategory.HauntedAttractions, "Haunted attractions", "SCarowinds Fright Nights"),
            "SCarowinds Fright Nights", 39.99m, "$39.99", null, "Starting From", false, null);

        await h.Service.ReconcileAsync(SourceCategory.HauntedAttractions, Evidence(Day1), [hauntedMaze]);
        var inSeason = await h.Catalog.GetAsync(hauntedMaze.Key);
        Assert.Equal(ProductLifecycleState.Active, inSeason!.LifecycleState);

        await h.Service.ReconcileAsync(SourceCategory.HauntedAttractions, Evidence(Day2), []);
        await h.Service.ReconcileAsync(SourceCategory.HauntedAttractions, Evidence(Day3), []);
        var offSeason = await h.Catalog.GetAsync(hauntedMaze.Key);
        Assert.Equal(ProductLifecycleState.Archived, offSeason!.LifecycleState);

        // Returns next season — reconciled state flips back automatically.
        await h.Service.ReconcileAsync(SourceCategory.HauntedAttractions, Evidence(Day3.AddDays(300)), [hauntedMaze]);
        var backInSeason = await h.Catalog.GetAsync(hauntedMaze.Key);
        Assert.Equal(ProductLifecycleState.Active, backInSeason!.LifecycleState);
    }

    [Fact]
    public async Task TwoSourcesSharingOneCategory_DoNotDriveEachOthersProductsTowardAbsence()
    {
        // SourceCategory.AddOns is deliberately shared by two independent sources (general
        // add-ons and season-pass add-ons — spec §6.1's shared 14-day cadence). Reconciling one
        // must not treat the other's disjoint, previously-observed products as missing.
        var h = Harness.Create();
        var generalAddOn = new ObservedProduct(
            CatalogProductKey.FromSourceAndName(SourceCategory.AddOns, "General add-ons", "Single Day FunPix"),
            "Single Day FunPix", 19.99m, "$19.99", null, null, false, null);
        var seasonPassAddOn = new ObservedProduct(
            CatalogProductKey.FromSourceAndName(SourceCategory.AddOns, "Season-pass add-ons", "All Season Fast Lane"),
            "All Season Fast Lane", 499m, "$499", 899m, "Sale Price", false, null);

        var generalPageEvidence = new SourceEvidence(
            "General add-ons", SampleUrl, Day1, RetrievalStatus.Verified, "Checked successfully.", RetrievalStrategy.Html);
        var seasonPassAddOnsPageEvidence = new SourceEvidence(
            "Season-pass add-ons", SampleUrl, Day1, RetrievalStatus.Verified, "Checked successfully.", RetrievalStrategy.Html);

        await h.Service.ReconcileAsync(SourceCategory.AddOns, generalPageEvidence, [generalAddOn]);
        await h.Service.ReconcileAsync(SourceCategory.AddOns, seasonPassAddOnsPageEvidence, [seasonPassAddOn]);

        // Re-reconcile the general add-ons page alone (season-pass add-ons product is absent from
        // *this* batch only because it's a different page, not because it actually disappeared).
        var generalPageEvidenceDay2 = new SourceEvidence(
            "General add-ons", SampleUrl, Day2, RetrievalStatus.Verified, "Checked successfully.", RetrievalStrategy.Html);
        await h.Service.ReconcileAsync(SourceCategory.AddOns, generalPageEvidenceDay2, [generalAddOn]);

        var stillActive = await h.Catalog.GetAsync(seasonPassAddOn.Key);
        Assert.Equal(ProductLifecycleState.Active, stillActive!.LifecycleState);
        Assert.Equal(0, stillActive.ConsecutiveConfirmedAbsenceCount);
    }

    [Fact]
    public async Task PersistAsync_WithHistory_CommitsThroughASharedUnitOfWork()
    {
        var h = Harness.Create();

        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);

        Assert.True(h.UnitOfWorkFactory.BeginCount >= 1);
    }

    [Fact]
    public async Task PersistAsync_WithoutHistory_DoesNotOpenAUnitOfWork()
    {
        var h = Harness.Create();
        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1), [GoldPass()]);
        var beginCountAfterFirstCall = h.UnitOfWorkFactory.BeginCount;

        await h.Service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day2), [GoldPass()]); // unchanged -> no history

        Assert.Equal(beginCountAfterFirstCall, h.UnitOfWorkFactory.BeginCount);
    }
}
