using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 21A composition tests (docs/phase21a-pricing-historical-integrity.md): Military/First
/// Responder exact-date precedence over the catalog "Starting From" minimum, and the historical
/// selected-date rule — a past date shows only official prices recorded for that exact date, never
/// the current catalog. Amounts are representative fixture values (the live 2026-10-16 observations),
/// not constants anywhere in production code. "Now" is 2026-10-16 10:00 Eastern unless stated.
/// </summary>
public class Phase21APricingIntegrityTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly Tomorrow = new(2026, 10, 17);
    private static readonly DateOnly AprilDate = new(2026, 4, 18);
    private static readonly DateOnly Aug29 = new(2026, 8, 29);
    private static readonly Uri SourceUrl = new("https://example.test/fake-source");

    private const string MilitaryCatalogIdentity = "catalog|MilitaryFirstResponder:id:militarywizard";

    private sealed record Env(
        AssociatePassdownService Service,
        InMemoryDatedResultRepository Dated,
        InMemoryCatalogProductRepository Catalog,
        InMemoryAssociateDraftRepository Drafts,
        FakeRefreshOrchestrator Orchestrator,
        FakeClock Clock);

    private static Env Create(DateTimeOffset? now = null)
    {
        var clock = new FakeClock(now ?? FixedNow);
        var dated = new InMemoryDatedResultRepository();
        var catalog = new InMemoryCatalogProductRepository();
        var drafts = new InMemoryAssociateDraftRepository();
        var orchestrator = new FakeRefreshOrchestrator();
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        return new Env(new AssociatePassdownService(drafts, dated, catalog, orchestrator, notes, clock), dated, catalog, drafts, orchestrator, clock);
    }

    private static SourceEvidence Evidence(DateOnly? date = null, RetrievalStatus status = RetrievalStatus.Verified) =>
        new("Fake Source", SourceUrl, FixedNow, status, "Detail.", RetrievalStrategy.DirectApi, date, confirmedDate: status == RetrievalStatus.Verified ? date : null);

    private static Task UpsertCatalogAsync(
        InMemoryCatalogProductRepository catalog, SourceCategory category, string officialId, string name, decimal price,
        string? qualifier = null, decimal? comparison = null, ProductLifecycleState state = ProductLifecycleState.Active) =>
        catalog.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(category, officialId), name, state, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0,
            ArchivedAt: state == ProductLifecycleState.Archived ? FixedNow : null,
            ArchivedReason: state == ProductLifecycleState.Archived ? "Retired." : null,
            CurrentPriceAmount: price, CurrentDisplayPrice: $"${price:0}", ComparisonPriceAmount: comparison, PriceQualifier: qualifier,
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

    private static Task SeedMilitaryCatalogAsync(InMemoryCatalogProductRepository catalog) =>
        UpsertCatalogAsync(catalog, SourceCategory.MilitaryFirstResponder, "MilitaryWizard", "Military & First Responder Tickets", 40m, "Starting From", 80m);

    private static Task SeedMilitaryGoldPassAsync(InMemoryCatalogProductRepository catalog) =>
        UpsertCatalogAsync(catalog, SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027", "2027 Military Gold Pass", 89m, comparison: 110m);

    private static Task SeedMilitaryDatedAsync(InMemoryDatedResultRepository dated, DateOnly date, decimal military, decimal firstResponder) =>
        dated.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedMilitaryFirstResponder, date),
            Evidence(date),
            JsonSerializer.Serialize(new DatedMilitaryFirstResponderPayload(
            [
                new DatedMilitaryFirstResponderProduct($"m-{date:MMdd}", "Military Ticket", MilitaryFirstResponderEligibility.Military, military, date),
                new DatedMilitaryFirstResponderProduct($"f-{date:MMdd}", "First Responder Ticket", MilitaryFirstResponderEligibility.FirstResponder, firstResponder, date),
            ])));

    private static Task SeedAdmissionAsync(InMemoryDatedResultRepository dated, DateOnly date, decimal price) =>
        dated.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, date),
            Evidence(date),
            JsonSerializer.Serialize(new DatedAdmissionPayload([new DatedAdmissionProduct($"park-{date:MMdd}", "Park Ticket", price, null, null, date)])));

    private static Task SeedBafDatedAsync(InMemoryDatedResultRepository dated, DateOnly date, decimal price) =>
        dated.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedBringAFriend, date),
            Evidence(date),
            JsonSerializer.Serialize(new DatedBringAFriendPayload([new DatedBringAFriendProduct("10094895", "Bring-A-Friend Ticket", price, date)])));

    /// <summary>A representative current catalog: a season pass, an add-on, a current SCarowinds promotion and the Military card.</summary>
    private static async Task SeedCurrentCatalogAsync(InMemoryCatalogProductRepository catalog)
    {
        await UpsertCatalogAsync(catalog, SourceCategory.SeasonPasses, "550e3db6-5bad-45f8-8404-5d608c367617", "2027 Gold Pass", 99m);
        await UpsertCatalogAsync(catalog, SourceCategory.AddOns, "9Jhv1XTnFkL57pqg96gnpY", "All Season Fast Lane", 150m);
        await UpsertCatalogAsync(catalog, SourceCategory.Promotions, "conjuring-1", "The Conjuring: Beyond Fear Bundle", 70m, "Starting From");
        await SeedMilitaryCatalogAsync(catalog);
    }

    // ---------------------------------------------------------------- Military / First Responder

    [Fact]
    public async Task Military_ExactSelectedDatePrice_ReplacesFromMinimumInPlace()
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);
        await SeedMilitaryDatedAsync(env.Dated, Today, 55m, 55m);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var row = Assert.Single(workspace.Products, p => p.Category == SourceCategory.MilitaryFirstResponder);
        Assert.Equal(MilitaryCatalogIdentity, row.ProductIdentity); // same identity → draft choices survive
        Assert.Equal("Military & First Responder Tickets", row.DisplayName);
        Assert.Equal(55m, row.OfficialPriceAmount);
        Assert.Equal(PriceKind.ExactDated, row.PriceKind);
        Assert.Null(row.PriceQualifier);
        Assert.True(row.DateSpecific);
        Assert.Equal(Today, row.ApplicableDate);
        Assert.Equal(80m, row.ComparisonPriceAmount); // official gate price kept, never fabricated
        Assert.Null(row.PriceHistoryProductKey); // a daily ticket never enters season price history
    }

    [Fact]
    public async Task Military_OnlyGenericMinimum_StaysStartingFromNeverExact()
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var row = Assert.Single(workspace.Products, p => p.Category == SourceCategory.MilitaryFirstResponder);
        Assert.Equal(40m, row.OfficialPriceAmount);
        Assert.Equal(PriceKind.StartingFrom, row.PriceKind);
        Assert.Equal("Starting From", row.PriceQualifier);
        Assert.False(row.DateSpecific);
    }

    [Fact]
    public async Task Military_FutureDates_EachUseTheirOwnExactPrice_UnrecordedDateFallsBackToFrom()
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);
        await SeedMilitaryDatedAsync(env.Dated, Today, 55m, 55m);
        await SeedMilitaryDatedAsync(env.Dated, Tomorrow, 60m, 60m);

        Assert.Equal(55m, (await env.Service.LoadAssociateDateAsync(Today)).Products.Single(p => p.Category == SourceCategory.MilitaryFirstResponder).OfficialPriceAmount);
        Assert.Equal(60m, (await env.Service.LoadAssociateDateAsync(Tomorrow)).Products.Single(p => p.Category == SourceCategory.MilitaryFirstResponder).OfficialPriceAmount);

        var unrecorded = (await env.Service.LoadAssociateDateAsync(new DateOnly(2026, 10, 22))).Products.Single(p => p.Category == SourceCategory.MilitaryFirstResponder);
        Assert.Equal(PriceKind.StartingFrom, unrecorded.PriceKind);
        Assert.Equal(40m, unrecorded.OfficialPriceAmount);
    }

    [Theory]
    [InlineData(RetrievalStatus.NotPublished)]
    [InlineData(RetrievalStatus.Unavailable)]
    [InlineData(RetrievalStatus.Closed)]
    [InlineData(RetrievalStatus.Error)]
    public async Task Military_NoVerifiedExactPrice_KeepsTruthfulFromAndNeverFabricates(RetrievalStatus status)
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);
        await env.Dated.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.DatedMilitaryFirstResponder, Tomorrow), Evidence(Tomorrow, status));

        var workspace = await env.Service.LoadAssociateDateAsync(Tomorrow);

        var row = Assert.Single(workspace.Products, p => p.Category == SourceCategory.MilitaryFirstResponder);
        Assert.Equal(PriceKind.StartingFrom, row.PriceKind);
        Assert.Equal(40m, row.OfficialPriceAmount);
    }

    [Fact]
    public async Task Military_EligibilitiesPricedDifferently_ShowsOneExactRowEachAndDropsTheMinimum()
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);
        await SeedMilitaryGoldPassAsync(env.Catalog);
        await SeedMilitaryDatedAsync(env.Dated, Today, 55m, 50m);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        Assert.DoesNotContain(workspace.Products, p => p.ProductIdentity == MilitaryCatalogIdentity);
        var military = Assert.Single(workspace.Products, p => p.DisplayName == "Military Ticket");
        var firstResponder = Assert.Single(workspace.Products, p => p.DisplayName == "First Responder Ticket");
        Assert.Equal((55m, PriceKind.ExactDated), (military.OfficialPriceAmount!.Value, military.PriceKind));
        Assert.Equal((50m, PriceKind.ExactDated), (firstResponder.OfficialPriceAmount!.Value, firstResponder.PriceKind));
        Assert.Equal(SourceCategory.MilitaryFirstResponder, military.Category);
        Assert.Equal(ProductDisplaySection.DailyAdmission, ProductDisplaySectionResolver.Resolve(military));

        // The 2027 Military Gold Pass on the same page is never touched by daily ticket pricing.
        var pass = Assert.Single(workspace.Products, p => p.DisplayName == "2027 Military Gold Pass");
        Assert.Equal(89m, pass.OfficialPriceAmount);
        Assert.False(pass.DateSpecific);
    }

    [Fact]
    public async Task Military_ExactPriceWithoutCatalogCard_StillShownAsOfficialPackageRows()
    {
        var env = Create();
        await SeedMilitaryDatedAsync(env.Dated, Today, 55m, 55m);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        Assert.Equal(
            new[] { "First Responder Ticket", "Military Ticket" },
            workspace.Products.Where(p => p.Category == SourceCategory.MilitaryFirstResponder).Select(p => p.DisplayName).Order());
        Assert.All(workspace.Products, p => Assert.Equal(PriceKind.ExactDated, p.PriceKind));
    }

    [Fact]
    public async Task Military_DraftOverrideOnTheCatalogRow_SurvivesTheExactMerge()
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);
        await SeedMilitaryDatedAsync(env.Dated, Today, 55m, 55m);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [MilitaryCatalogIdentity] = new(IncludeOnPdf: false, DisplayPriceOverride: 52m) },
        };
        await env.Service.SaveDraftAsync(Today, draft);

        var row = (await env.Service.LoadAssociateDateAsync(Today)).Products.Single(p => p.ProductIdentity == MilitaryCatalogIdentity);

        Assert.False(row.IncludeOnPdf);
        Assert.Equal(52m, row.EffectiveDisplayPrice);
        Assert.Equal(55m, row.OfficialPriceAmount); // manual override never touches the official value
    }

    // ---------------------------------------------------------------- Historical selected-date pricing

    [Fact]
    public async Task Historical_PastDateWithRecordedAdmission_ShowsOnlyThatAndWithholdsTheCurrentCatalog()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);
        await SeedAdmissionAsync(env.Dated, AprilDate, 49m);

        var workspace = await env.Service.LoadAssociateDateAsync(AprilDate);

        Assert.Equal(AssociatePricingTimeframe.Historical, workspace.PricingTimeframe);
        var only = Assert.Single(workspace.Products);
        Assert.Equal("Park Ticket", only.DisplayName);
        Assert.Equal(49m, only.OfficialPriceAmount);
        Assert.Equal(AprilDate, only.ApplicableDate);
        Assert.Equal(4, workspace.WithheldCurrentCatalogProductCount);
        Assert.False(workspace.IsHistoricalPricingUnavailable);
    }

    [Fact]
    public async Task Historical_CurrentSCarowindsProduct_NeverShownOnAnOldRegularSeasonDate_ButStillShownToday()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);

        var april = await env.Service.LoadAssociateDateAsync(AprilDate);
        var today = await env.Service.LoadAssociateDateAsync(Today);

        Assert.DoesNotContain(april.Products, p => p.DisplayName.Contains("Conjuring", StringComparison.Ordinal));
        Assert.Contains(today.Products, p => p.DisplayName.Contains("Conjuring", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Historical_NoRecordedPricing_IsExplicitlyUnavailable_WhileKnownHoursStayVisible()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);
        await env.Dated.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, AprilDate), Evidence(AprilDate), payloadJson: null);

        var workspace = await env.Service.LoadAssociateDateAsync(AprilDate);

        Assert.Empty(workspace.Products);
        Assert.True(workspace.IsHistoricalPricingUnavailable);
        Assert.Equal(AssociateVerificationState.Verified, workspace.ParkHours.Verification.State);
        // Never captured and no longer retrievable is not a retrieval failure.
        Assert.DoesNotContain(workspace.CategoryStatuses, s => s.State == AssociateVerificationState.Error);
    }

    [Fact]
    public async Task Historical_ExpiredPromotionWithRecordedObservation_StaysVisibleOnItsDate()
    {
        var env = Create();
        await UpsertCatalogAsync(env.Catalog, SourceCategory.Promotions, "449e6fe8-a6ad-4af5-b162-b7835798b8ae", "Bring-A-Friend Ticket", 35m, "Starting From", 80m, ProductLifecycleState.Archived);
        await SeedBafDatedAsync(env.Dated, Aug29, 39m);

        var workspace = await env.Service.LoadAssociateDateAsync(Aug29);

        var baf = Assert.Single(workspace.Products);
        Assert.Equal("Bring-A-Friend Ticket", baf.DisplayName);
        Assert.Equal(39m, baf.OfficialPriceAmount);
        Assert.Equal(PriceKind.ExactDated, baf.PriceKind);
        Assert.Equal(ProductDisplaySection.Promotions, ProductDisplaySectionResolver.Resolve(baf));
        Assert.Null(baf.ComparisonPriceAmount);
    }

    [Fact]
    public async Task Historical_ExpiredPromotionWithoutObservation_IsNeverReconstructed()
    {
        var env = Create();
        await UpsertCatalogAsync(env.Catalog, SourceCategory.Promotions, "449e6fe8-a6ad-4af5-b162-b7835798b8ae", "Bring-A-Friend Ticket", 35m, "Starting From", 80m, ProductLifecycleState.Archived);

        var workspace = await env.Service.LoadAssociateDateAsync(Aug29);

        Assert.Empty(workspace.Products);
        Assert.True(workspace.IsHistoricalPricingUnavailable);
    }

    [Fact]
    public async Task Historical_MergedExactRowKeepsItsDatedPrice_ButNotTodaysCatalogComparison()
    {
        var env = Create();
        await UpsertCatalogAsync(env.Catalog, SourceCategory.Promotions, "449e6fe8-a6ad-4af5-b162-b7835798b8ae", "Bring-A-Friend Ticket", 35m, "Starting From", 80m);
        await SeedMilitaryCatalogAsync(env.Catalog);
        await SeedBafDatedAsync(env.Dated, Aug29, 39m);
        await SeedMilitaryDatedAsync(env.Dated, Aug29, 45m, 45m);

        var workspace = await env.Service.LoadAssociateDateAsync(Aug29);

        var baf = Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(39m, baf.OfficialPriceAmount);
        Assert.Null(baf.ComparisonPriceAmount);
        var military = Assert.Single(workspace.Products, p => p.ProductIdentity == MilitaryCatalogIdentity);
        Assert.Equal(45m, military.OfficialPriceAmount);
        Assert.Null(military.ComparisonPriceAmount);
    }

    [Fact]
    public async Task Historical_MilitaryWithoutRecordedExactPrice_NeverFallsBackToTodaysMinimum()
    {
        var env = Create();
        await SeedMilitaryCatalogAsync(env.Catalog);

        var workspace = await env.Service.LoadAssociateDateAsync(AprilDate);

        Assert.DoesNotContain(workspace.Products, p => p.Category == SourceCategory.MilitaryFirstResponder);
    }

    [Fact]
    public async Task Historical_CustomProductsFromTheDraft_StayWithTheirDate()
    {
        var env = Create();
        await env.Service.SaveDraftAsync(AprilDate, AssociateDraftPayload.CreateDefault() with
        {
            CustomProducts = [new CustomProductEntry("c1", "Group rate", 30m, null, true)],
        });

        var workspace = await env.Service.LoadAssociateDateAsync(AprilDate);

        Assert.Single(workspace.Products, p => p.IsCustom);
        Assert.True(workspace.IsHistoricalPricingUnavailable); // custom entries are not official pricing
    }

    [Fact]
    public async Task TodayAndFuture_AreUnaffected_CurrentCatalogStillComposed()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);

        foreach (var date in new[] { Today, Tomorrow, new DateOnly(2026, 12, 31) })
        {
            var workspace = await env.Service.LoadAssociateDateAsync(date);
            Assert.Equal(AssociatePricingTimeframe.Current, workspace.PricingTimeframe);
            Assert.Equal(4, workspace.Products.Count);
            Assert.Equal(0, workspace.WithheldCurrentCatalogProductCount);
        }
    }

    [Fact]
    public async Task OvernightWindow_YesterdayStaysCurrentUntilItsSixAmCutoff()
    {
        var yesterday = new DateOnly(2026, 10, 15);
        var beforeCutoff = Create(new DateTimeOffset(2026, 10, 16, 5, 59, 0, TimeSpan.FromHours(-4)));
        await SeedCurrentCatalogAsync(beforeCutoff.Catalog);
        var atCutoff = Create(new DateTimeOffset(2026, 10, 16, 6, 0, 0, TimeSpan.FromHours(-4)));
        await SeedCurrentCatalogAsync(atCutoff.Catalog);

        Assert.Equal(AssociatePricingTimeframe.Current, (await beforeCutoff.Service.LoadAssociateDateAsync(yesterday)).PricingTimeframe);
        Assert.Equal(AssociatePricingTimeframe.Historical, (await atCutoff.Service.LoadAssociateDateAsync(yesterday)).PricingTimeframe);
    }

    /// <summary>Finalization freezes the passdown as it stood at the end of its day — the historical-view rule never strips a snapshot.</summary>
    [Fact]
    public async Task FinalizationLoad_ForAPastDate_StillComposesTheStoredCatalog()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);
        var yesterday = new DateOnly(2026, 10, 15);

        var workspace = await env.Service.LoadAssociateDateForFinalizationAsync(yesterday);

        Assert.Equal(AssociatePricingTimeframe.Current, workspace.PricingTimeframe);
        Assert.Equal(4, workspace.Products.Count);
    }

    [Fact]
    public async Task Historical_CalendarDateSwitching_IsLocalOnly_AndNeverTriggersRetrieval()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);
        await SeedAdmissionAsync(env.Dated, AprilDate, 49m);

        foreach (var date in new[] { AprilDate, Aug29, Today, AprilDate, new DateOnly(2026, 1, 2) })
        {
            await env.Service.LoadAssociateDateAsync(date);
        }

        Assert.Empty(env.Orchestrator.ManualDateRefreshCalls);
        Assert.Equal(0, env.Orchestrator.ScheduledRefreshCallCount);
        Assert.Empty(env.Orchestrator.DeepRefreshCalls);
    }

    [Fact]
    public async Task Historical_LaterCatalogChanges_NeverRewriteARecordedHistoricalResult()
    {
        var env = Create();
        await SeedCurrentCatalogAsync(env.Catalog);
        await SeedAdmissionAsync(env.Dated, AprilDate, 49m);

        // A newer current catalog (price change, new product) arrives.
        await UpsertCatalogAsync(env.Catalog, SourceCategory.Promotions, "new-promo", "Holiday Bundle", 60m);
        await UpsertCatalogAsync(env.Catalog, SourceCategory.SeasonPasses, "550e3db6-5bad-45f8-8404-5d608c367617", "2027 Gold Pass", 109m);

        var workspace = await env.Service.LoadAssociateDateAsync(AprilDate);

        var only = Assert.Single(workspace.Products);
        Assert.Equal(49m, only.OfficialPriceAmount);
        var stored = await env.Dated.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, AprilDate));
        Assert.NotNull(stored!.SuccessEvidence);
    }

    [Fact]
    public void Policy_UsesTheFinalizationCutoff_NotTheHostDateOrAMonthRule()
    {
        Assert.Equal(AssociatePricingTimeframe.Current, HistoricalPricingPolicy.TimeframeFor(Today, FixedNow));
        Assert.Equal(AssociatePricingTimeframe.Current, HistoricalPricingPolicy.TimeframeFor(new DateOnly(2027, 4, 18), FixedNow));
        Assert.Equal(AssociatePricingTimeframe.Historical, HistoricalPricingPolicy.TimeframeFor(new DateOnly(2026, 1, 1), FixedNow));
    }

    [Fact]
    public void SourceCapabilities_CoverEveryPricingCategory_AndNoneClaimsPastDateRetrieval()
    {
        // Phase 21C: ShowSchedule is operational information, not a pricing source.
        var pricingCategories = Enum.GetValues<SourceCategory>().Where(c => c is not SourceCategory.ParkHours and not SourceCategory.ShowSchedule);

        Assert.Equal(pricingCategories.Order(), HistoricalPricingSourceCapabilities.All.Select(c => c.Category).Order());
        Assert.DoesNotContain(HistoricalPricingSourceCapabilities.All, c => c.Capability == HistoricalPricingCapability.RetrievesPastDates);
    }
}
