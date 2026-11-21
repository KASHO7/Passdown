using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Application.Tests.Seasons;

/// <summary>
/// Phase 31: season context and past-season products on the Associate workspace, the deals document
/// (merge, grouping, last-known-good), and both refresh registrations. Uses the official 2026 dates
/// verified on 2026-11-21 as fixtures.
/// </summary>
public class Phase31SeasonsAndDealsTests
{
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Nov21Utc = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Nov21Eastern = new(2026, 11, 21, 10, 0, 0, Est);
    private static readonly DateTimeOffset Oct15Utc = new(2026, 10, 15, 17, 31, 0, TimeSpan.Zero);
    private static readonly DateOnly Nov21 = new(2026, 11, 21);
    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/events");

    private static OfficialOperatingEvent Event(OperatingEventKind kind, string title, DateOnly start, DateOnly end, string label) =>
        new($"{kind}:{start:yyyy-MM-dd}", title, kind, start, end, IsSelectDates: true, label, Url, Nov21Eastern, Nov21Eastern);

    private static readonly OperatingEventSchedule Schedule2026 = new(
    [
        Event(OperatingEventKind.SCarowinds, "SCarowinds", new(2026, 9, 11), new(2026, 11, 7), "Select Nights. September 11 - November 7, 2026"),
        Event(OperatingEventKind.Oktoberfest, "Oktoberfest Presented by Cash App", new(2026, 11, 7), new(2026, 11, 22), "Select Days, November 7-22, 2026"),
        Event(OperatingEventKind.WinterFest, "WinterFest", new(2026, 11, 27), new(2027, 1, 3), "Select Dates, Nov 27 - Jan 3, 2027"),
    ]);

    private static SourceEvidence Verified(DateTimeOffset at, string source = "Carowinds Promotions") =>
        new(source, new Uri("https://www.sixflags.com/carowinds/special-offers"), at, RetrievalStatus.Verified, "Verified.", RetrievalStrategy.Html);

    private static SourceEvidence Failed(DateTimeOffset at) =>
        new("Carowinds Promotions", new Uri("https://www.sixflags.com/carowinds/special-offers"), at, RetrievalStatus.Error, "We couldn't check promotions right now.", RetrievalStrategy.Html, technicalDetail: "HTTP 503");

    private sealed record Env(
        AssociatePassdownService Service,
        InMemoryCatalogProductRepository Catalog,
        InMemoryOfficialReferenceDocumentRepository Documents,
        InMemoryAssociateDraftRepository Drafts,
        FakeRefreshOrchestrator Orchestrator);

    private static async Task<Env> CreateAsync(bool withSchedule = true)
    {
        var clock = new FakeClock(Nov21Utc);
        var catalog = new InMemoryCatalogProductRepository();
        var documents = new InMemoryOfficialReferenceDocumentRepository();
        var drafts = new InMemoryAssociateDraftRepository();
        var orchestrator = new FakeRefreshOrchestrator();
        if (withSchedule)
        {
            await documents.SaveSuccessAsync(OfficialReferenceDocumentIds.OperatingEvents, Verified(Nov21Eastern, "Carowinds Events"), OfficialSeasonJson.Serialize(Schedule2026));
        }

        var service = new AssociatePassdownService(
            drafts, new InMemoryDatedResultRepository(), catalog, orchestrator,
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock,
            seasonService: new OperatingSeasonService(documents));
        return new Env(service, catalog, documents, drafts, orchestrator);
    }

    private static Task UpsertPromotionAsync(InMemoryCatalogProductRepository catalog, string id, string name, decimal price, DateTimeOffset verifiedAt) =>
        catalog.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, id), name, ProductLifecycleState.Active, verifiedAt, verifiedAt, verifiedAt,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: price, CurrentDisplayPrice: $"${price:0}", ComparisonPriceAmount: null, PriceQualifier: "Starting From",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Verified(verifiedAt)));

    // ------------------------------------------------------------------ Associate season context

    [Fact]
    public async Task Workspace_CarriesTheSelectedDatesOfficialSeason_FromCacheOnly()
    {
        var env = await CreateAsync();

        var workspace = await env.Service.LoadAssociateDateAsync(Nov21);

        Assert.True(workspace.Season.ScheduleKnown);
        Assert.True(workspace.Season.Resolution.Offers(OperatingEventKind.Oktoberfest));
        Assert.False(workspace.Season.Resolution.Offers(OperatingEventKind.SCarowinds));
        Assert.Equal(OfficialDocumentFreshness.Current, workspace.Season.ScheduleStatus.Freshness);
        Assert.Equal(0, env.Orchestrator.ManualDateRefreshCalls.Count); // date navigation never goes online
    }

    [Fact]
    public async Task FutureWinterFestDate_ShowsWinterFest()
    {
        var env = await CreateAsync();

        var workspace = await env.Service.LoadAssociateDateAsync(new DateOnly(2026, 12, 12));

        Assert.True(workspace.Season.Resolution.Offers(OperatingEventKind.WinterFest));
    }

    [Fact]
    public async Task NoScheduleYet_IsUnknown_NotNoEvent()
    {
        var env = await CreateAsync(withSchedule: false);

        var workspace = await env.Service.LoadAssociateDateAsync(Nov21);

        Assert.False(workspace.Season.ScheduleKnown);
        Assert.Equal(OfficialDocumentFreshness.NotChecked, workspace.Season.ScheduleStatus.Freshness);
    }

    [Fact]
    public async Task ExpiredSCarowindsBundle_IsPastSeason_NotCurrent_AndNotExported_ButStoredUntouched()
    {
        var env = await CreateAsync();
        await UpsertPromotionAsync(env.Catalog, "scarowinds-bundles", "SCarowinds Bundles", 55m, Oct15Utc);
        await UpsertPromotionAsync(env.Catalog, "baf", "Bring-A-Friend Ticket", 40m, Oct15Utc);

        var workspace = await env.Service.LoadAssociateDateAsync(Nov21);

        var bundle = Assert.Single(workspace.Products, p => p.DisplayName == "SCarowinds Bundles");
        Assert.True(bundle.IsPastSeason);
        Assert.False(bundle.IsPassdownExportable);
        Assert.Contains("SCarowinds 2026 ended November 7, 2026", bundle.PastSeasonNote, StringComparison.Ordinal);
        Assert.False(Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket").IsPastSeason);

        var stored = await env.Catalog.GetAsync(CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "scarowinds-bundles"));
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState); // history/record preserved
    }

    [Fact]
    public async Task SCarowindsProduct_StillListedAfterTheEventEnded_StaysCurrent()
    {
        var env = await CreateAsync();
        await UpsertPromotionAsync(env.Catalog, "scarowinds-bundles", "SCarowinds Bundles", 55m, Nov21Utc.AddHours(-2));

        var workspace = await env.Service.LoadAssociateDateAsync(Nov21);

        Assert.False(Assert.Single(workspace.Products, p => p.DisplayName == "SCarowinds Bundles").IsPastSeason);
    }

    [Fact]
    public async Task UnknownSchedule_NeverHidesAProduct()
    {
        var env = await CreateAsync(withSchedule: false);
        await UpsertPromotionAsync(env.Catalog, "scarowinds-bundles", "SCarowinds Bundles", 55m, Oct15Utc);

        var workspace = await env.Service.LoadAssociateDateAsync(Nov21);

        Assert.False(Assert.Single(workspace.Products, p => p.DisplayName == "SCarowinds Bundles").IsPastSeason);
    }

    [Fact]
    public async Task SavedSectionSelections_AreNeverChangedBySeasonResolution()
    {
        var env = await CreateAsync();
        var before = await env.Service.LoadAssociateDateAsync(Nov21);
        Assert.False(before.Draft.SectionSelection.IncludeHauntedAttractions); // Phase 17D default; season resolution never selects it

        var after = await env.Service.LoadAssociateDateAsync(Nov21);

        Assert.Equal(before.Draft.SectionSelection, after.Draft.SectionSelection);
        Assert.Null(await env.Drafts.GetAsync(Nov21)); // composing never writes a draft
    }

    // ------------------------------------------------------------------ Deals document

    private static OfficialPromotion Promo(string id, string title, DateTimeOffset verifiedAt, Uri? page = null) => new()
    {
        PromotionId = id,
        Title = title,
        PrimarySourceUrl = page ?? new Uri("https://www.sixflags.com/carowinds/special-offers"),
        SourceUrls = [page ?? new Uri("https://www.sixflags.com/carowinds/special-offers")],
        FirstSeenAt = verifiedAt,
        LastVerifiedAt = verifiedAt,
    };

    [Fact]
    public void Merge_DelistedOfferIsKeptForHistory_AndGroupsAsExpired()
    {
        var day1 = Nov21Eastern;
        var day2 = Nov21Eastern.AddDays(1);
        var document = OfficialDealsDocument.Empty
            .Merge(new OfficialDealsObservation([Promo("offer:a", "Black Friday Deal", day1), Promo("offer:b", "Bring-A-Friend Ticket", day1)]), day1)
            .Merge(new OfficialDealsObservation([Promo("offer:b", "Bring-A-Friend Ticket", day2)]), day2);

        var overview = OfficialDealsService.Group(document, new OfficialDocumentStatus(OfficialDocumentFreshness.Current, day2, day2, null, null), new DateOnly(2026, 11, 22), day2);

        Assert.Equal("Bring-A-Friend Ticket", Assert.Single(overview.Current).Promotion.Title);
        var expired = Assert.Single(overview.Expired);
        Assert.Equal("Black Friday Deal", expired.Promotion.Title);
        Assert.Contains("No longer listed", expired.Assessment.Reason, StringComparison.Ordinal);
        Assert.Equal(day1, Assert.Single(document.Promotions, p => p.PromotionId == "offer:b").FirstSeenAt);
    }

    [Fact]
    public void Merge_UnreadableOrUnreachableOffers_AreKept_NotExpired()
    {
        var campaignPage = new Uri("https://www.sixflags.com/carowinds/black-friday-deal");
        var day1 = Nov21Eastern;
        var day2 = Nov21Eastern.AddDays(1);
        var document = OfficialDealsDocument.Empty
            .Merge(new OfficialDealsObservation([Promo("offer:a", "Unreadable later", day1), Promo("offer:c", "Campaign only", day1, campaignPage)]), day1)
            .Merge(new OfficialDealsObservation([]) { UnreadableOfferIds = ["offer:a"], UnreachablePageUrls = [campaignPage] }, day2);

        var overview = OfficialDealsService.Group(document, new OfficialDocumentStatus(OfficialDocumentFreshness.Current, day2, day2, null, null), new DateOnly(2026, 11, 22), day2);

        Assert.Empty(overview.Expired);
        Assert.Equal(2, overview.PreviouslyVerified.Count);
    }

    [Fact]
    public async Task DealsNotCheckedYet_IsAnHonestEmptyState()
    {
        var overview = await new OfficialDealsService(new InMemoryOfficialReferenceDocumentRepository(), new FakeClock(Nov21Utc)).GetOverviewAsync();

        Assert.False(overview.HasAnyOffers);
        Assert.Equal(OfficialDocumentFreshness.NotChecked, overview.Status.Freshness);
    }

    // ------------------------------------------------------------------ Refresh registrations

    private static CatalogSourceRegistration PromotionsRegistration(
        InMemoryCatalogProductRepository catalog, InMemoryOfficialReferenceDocumentRepository documents, Func<SourceResult<CatalogPayload>> result) =>
        new(
            new FakeOfficialDataSource<CatalogPayload>("promotions.official", SourceCategory.Promotions, (_, _) => Task.FromResult(result())),
            new ProductReconciliationService(catalog, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory()),
            dealsDocuments: documents);

    [Fact]
    public async Task PromotionsRefresh_StoresDeals_AndAFailureKeepsLastKnownGood()
    {
        var catalog = new InMemoryCatalogProductRepository();
        var documents = new InMemoryOfficialReferenceDocumentRepository();
        var observed = new ObservedProduct(CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "bundle"), "2027 Silver Pass Bundle", 159m, "$159", 287m, "Only", false, null);

        var ok = await PromotionsRegistration(catalog, documents, () => SourceResult<CatalogPayload>.Success(
                "promotions.official", Verified(Nov21Eastern),
                new CatalogPayload([observed]) { Deals = new OfficialDealsObservation([Promo("offer:bundle", "2027 Silver Pass Bundle", Nov21Eastern)]) }))
            .ExecuteAsync(null, CancellationToken.None);
        Assert.Equal(SourceOperationOutcomeKind.Success, ok.Outcome);

        var failed = await PromotionsRegistration(catalog, documents, () => SourceResult<CatalogPayload>.WithoutPayload("promotions.official", Failed(Nov21Eastern.AddDays(1))))
            .ExecuteAsync(null, CancellationToken.None);
        Assert.NotEqual(SourceOperationOutcomeKind.Success, failed.Outcome);

        var record = await documents.GetAsync(OfficialReferenceDocumentIds.Deals);
        Assert.True(record!.LastAttemptFailed);
        var document = OfficialSeasonJson.TryDeserialize<OfficialDealsDocument>(record.SuccessPayloadJson);
        Assert.Equal("2027 Silver Pass Bundle", Assert.Single(document!.Promotions).Title);
        Assert.Equal(ProductLifecycleState.Active, (await catalog.GetAsync(observed.Key))!.LifecycleState);

        var overview = OfficialDealsService.Group(document, OfficialDocumentStatus.From(record), Nov21, Nov21Eastern.AddDays(1));
        Assert.Equal(OfficialDocumentFreshness.LastCheckFailed, overview.Status.Freshness);
        Assert.Equal("We couldn't check promotions right now.", overview.Status.LastError);
    }

    [Fact]
    public async Task EventsRefresh_MergesIntoLastKnownGood_AndFailureRecordsOnlyTheAttempt()
    {
        var documents = new InMemoryOfficialReferenceDocumentRepository();
        var scarowinds = Schedule2026.Events[0];
        var oktoberfest = Schedule2026.Events[1];

        async Task<SourceOperationResult> RunAsync(SourceResult<OperatingEventsPayload> result) =>
            await new OperatingEventsSourceRegistration(
                    new FakeOfficialDataSource<OperatingEventsPayload>("operating-events.official", SourceCategory.OperatingEvents, (_, _) => Task.FromResult(result)),
                    documents)
                .ExecuteAsync(null, CancellationToken.None);

        await RunAsync(SourceResult<OperatingEventsPayload>.Success("operating-events.official", Verified(Nov21Eastern, "Carowinds Events"), new OperatingEventsPayload([scarowinds, oktoberfest])));
        // SCarowinds drops off the listing after it ends — not treated as cancelled.
        await RunAsync(SourceResult<OperatingEventsPayload>.Success("operating-events.official", Verified(Nov21Eastern.AddDays(1), "Carowinds Events"), new OperatingEventsPayload([oktoberfest])));
        await RunAsync(SourceResult<OperatingEventsPayload>.WithoutPayload("operating-events.official", Failed(Nov21Eastern.AddDays(2))));

        var record = await documents.GetAsync(OfficialReferenceDocumentIds.OperatingEvents);
        var schedule = OfficialSeasonJson.TryDeserialize<OperatingEventSchedule>(record!.SuccessPayloadJson);
        Assert.Equal(2, schedule!.Events.Count);
        Assert.True(record.LastAttemptFailed);
    }

    [Fact]
    public void DamagedCachedDocument_ReadsAsUnknown_NotAnException()
    {
        Assert.Null(OfficialSeasonJson.TryDeserialize<OperatingEventSchedule>("{not json"));
        Assert.Null(OfficialSeasonJson.TryDeserialize<OfficialDealsDocument>(null));
    }

    [Fact]
    public void DocumentsRoundTripThroughJson()
    {
        var promo = Promo("offer:resort", "Resorts Black Friday Deal", Nov21Eastern) with
        {
            AdvertisedPriceText = "3rd Night FREE",
            PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms),
            RedemptionWindow = new OfficialDateRange(new(2026, 11, 2), new(2027, 12, 30), "November 2, 2026 – December 30, 2027"),
            PriceOptions = [new PromotionPriceOption("Silver Pass", null, "$69", 69m, "/ea*", null, "WinterSilverSP2027")],
        };

        var json = OfficialSeasonJson.Serialize(new OfficialDealsDocument([promo]));
        var roundTripped = Assert.Single(OfficialSeasonJson.TryDeserialize<OfficialDealsDocument>(json)!.Promotions);

        Assert.Equal(promo.PurchaseDeadline, roundTripped.PurchaseDeadline);
        Assert.Equal(promo.RedemptionWindow, roundTripped.RedemptionWindow);
        Assert.Equal(69m, Assert.Single(roundTripped.PriceOptions).PriceAmount);
        Assert.Equal(Schedule2026.Events.Count, OfficialSeasonJson.TryDeserialize<OperatingEventSchedule>(OfficialSeasonJson.Serialize(Schedule2026))!.Events.Count);
    }
}
