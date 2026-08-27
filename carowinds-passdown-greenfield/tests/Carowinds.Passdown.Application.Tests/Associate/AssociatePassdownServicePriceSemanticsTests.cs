using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 13C Part AF/AG: price-semantics precedence tests at the composition layer
/// (<see cref="AssociatePassdownService.LoadAssociateDateAsync"/>), using real production
/// composition code (<c>ComposeDatedProducts</c>/<c>ComposeCatalogProducts</c>/
/// <see cref="PriceSemanticsClassifier"/>) fed representative, non-hardcoded-production fixture
/// data — per CLAUDE.md's "changing the selected date must never trigger online retrieval" and Part
/// F's explicit instruction to test the Aug 29 Bring-A-Friend regression "via fixtures/parsers/
/// composition," not a live adapter.
///
/// Live reconfirmation (2026-08-27) originally found that ordinary (non-Duo) Bring-A-Friend's dated
/// price seemed reachable only after an authenticated passholder login inside the shared
/// <c>SnapWizard</c> checkout. <b>Phase 15A supersedes that finding</b> (docs/phase13-hardening.md
/// Phase 15A section): the anonymous Special Offers page's own "Select a date to begin!" calendar
/// (no login at any point) calls a different, previously-untried keyword —
/// <c>BAFWizardDailyTickets</c> — against the same <c>getkeywordcalendar</c> endpoint, and a real
/// <see cref="SourceCategory.DatedBringAFriend"/> adapter (<c>DatedBringAFriendSource</c>) now exists
/// for it. The <c>LoadAssociateDateAsync_Aug29_ExactDatedBafNeverConfusedWithGenericStartingFrom</c>
/// test below still exercises the generic <see cref="SourceCategory.DatedAdmissionTickets"/>
/// composition path with a representative "Season Pass Bring-A-Friend" payload to prove the
/// precedence *mechanism* in the abstract; the
/// <c>LoadAssociateDateAsync_OrdinaryBringAFriend_*</c> tests further down exercise the real,
/// production <see cref="SourceCategory.DatedBringAFriend"/> merge path
/// (<c>AssociatePassdownService.MergeExactDatedBringAFriend</c>) end to end.
/// </summary>
public class AssociatePassdownServicePriceSemanticsTests
{
    private static readonly DateOnly Aug29 = new(2026, 8, 29);
    private static readonly DateOnly Aug30 = new(2026, 8, 30);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 27, 14, 0, 0, TimeSpan.Zero);
    private static readonly Uri SourceUrl = new("https://example.test/fake-source");

    private static SourceEvidence Evidence(DateOnly? requestedDate = null) =>
        new("Fake Source", SourceUrl, FixedNow, RetrievalStatus.Verified, "Verified.", RetrievalStrategy.DirectApi,
            requestedDate, confirmedDate: requestedDate);

    private static AssociatePassdownService CreateService(
        InMemoryDatedResultRepository datedResults,
        InMemoryCatalogProductRepository catalogProducts)
    {
        var clock = new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var operationalNotes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        return new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, operationalNotes, clock);
    }

    /// <summary>
    /// Phase 13C Part F/AG worked example (locked in docs/retrieval/pricing-semantics.md): for a
    /// passdown selected on Aug 29, 2026, a verified exact dated price of $39.00 (comparison $80.00)
    /// must never be confused with, replaced by, or merged into the generic catalog's "Starting
    /// From" $35.00 (comparison $80.00) value — both must remain visible as two distinct rows, each
    /// carrying its own correct <see cref="PriceKind"/>.
    /// </summary>
    [Fact]
    public async Task LoadAssociateDateAsync_Aug29_ExactDatedBafNeverConfusedWithGenericStartingFrom()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();

        // The exact dated value (representative of the manually-observed checkout price — see class
        // doc comment for why no live adapter produces this).
        await datedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Aug29),
            Evidence(Aug29),
            JsonSerializer.Serialize(new DatedAdmissionPayload(
            [
                new DatedAdmissionProduct("sp-baf-2026-08-29", "Season Pass Bring-A-Friend", 39.00m, 80.00m, null, Aug29),
            ])));

        // The generic, non-date-specific catalog value (real fixture data: fixtures/promotions/published-page.html).
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "449e6fe8-a6ad-4af5-b162-b7835798b8ae"),
            "Bring-A-Friend Ticket", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 35.00m, CurrentDisplayPrice: "$35", ComparisonPriceAmount: 80.00m, PriceQualifier: "Starting From",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);

        var workspace = await service.LoadAssociateDateAsync(Aug29);

        var exactDated = workspace.Products.Single(p => p.DisplayName == "Season Pass Bring-A-Friend");
        Assert.Equal(39.00m, exactDated.OfficialPriceAmount);
        Assert.Equal(80.00m, exactDated.ComparisonPriceAmount);
        Assert.True(exactDated.DateSpecific);
        Assert.Equal(Aug29, exactDated.ApplicableDate);
        Assert.Equal(PriceKind.ExactDated, exactDated.PriceKind);

        var genericCatalog = workspace.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, genericCatalog.OfficialPriceAmount);
        Assert.False(genericCatalog.DateSpecific);
        Assert.Equal(PriceKind.StartingFrom, genericCatalog.PriceKind);

        // Never merged into a single row, never overwriting one another.
        Assert.NotEqual(exactDated.ProductIdentity, genericCatalog.ProductIdentity);
        Assert.Equal(35.00m, genericCatalog.OfficialPriceAmount); // untouched by the $39 exact value
        Assert.Equal(39.00m, exactDated.OfficialPriceAmount); // untouched by the $35 catalog floor
    }

    /// <summary>Selecting a different date must never surface Aug 29's exact value — no cross-date substitution (CLAUDE.md, spec §5.4).</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_DifferentSelectedDate_DoesNotReuseAug29sExactValue()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();

        await datedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Aug29),
            Evidence(Aug29),
            JsonSerializer.Serialize(new DatedAdmissionPayload(
            [
                new DatedAdmissionProduct("sp-baf-2026-08-29", "Season Pass Bring-A-Friend", 39.00m, 80.00m, null, Aug29),
            ])));

        var service = CreateService(datedResults, catalogProducts);

        var otherDate = new DateOnly(2026, 8, 30);
        var workspace = await service.LoadAssociateDateAsync(otherDate);

        Assert.DoesNotContain(workspace.Products, p => p.DisplayName == "Season Pass Bring-A-Friend");
    }

    /// <summary>The catalog "Starting From" value remains visible on its own, independent of any selected date (Part V).</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_CatalogStartingFromValue_RemainsAvailableWithoutAnyDatedValue()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "449e6fe8-a6ad-4af5-b162-b7835798b8ae"),
            "Bring-A-Friend Ticket", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 35.00m, CurrentDisplayPrice: "$35", ComparisonPriceAmount: 80.00m, PriceQualifier: "Starting From",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(new DateOnly(2026, 9, 15));

        var product = workspace.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, product.OfficialPriceAmount);
        Assert.Equal("Starting From", product.PriceQualifier);
        Assert.Equal(PriceKind.StartingFrom, product.PriceKind);
    }

    /// <summary>Sale-price catalog products (e.g. "2027 Gold Pass" — $89, was $115) classify as Sale, not StartingFrom.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_SaleQualifiedCatalogProduct_ClassifiesAsSale()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "9d031bb4-5b2f-46ea-8da0-08d826e7bd11"),
            "2027 Gold Pass", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 89.00m, CurrentDisplayPrice: "$89", ComparisonPriceAmount: 115.00m, PriceQualifier: "Sale Price",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(new DateOnly(2026, 9, 15));

        var product = workspace.Products.Single(p => p.DisplayName == "2027 Gold Pass");
        Assert.Equal(PriceKind.Sale, product.PriceKind);
    }

    /// <summary>A catalog product with no qualifier and no comparison price classifies as StandardCatalog, never fabricated as StartingFrom/Sale.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_PlainCatalogProduct_ClassifiesAsStandardCatalog()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.AddOns, "2026singlephoto"),
            "Single Day FunPix", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 19.99m, CurrentDisplayPrice: "$19.99", ComparisonPriceAmount: null, PriceQualifier: null,
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(new DateOnly(2026, 9, 15));

        var product = workspace.Products.Single(p => p.DisplayName == "Single Day FunPix");
        Assert.Equal(PriceKind.StandardCatalog, product.PriceKind);
    }

    /// <summary>
    /// Phase 13C Part H/I/AH: "Celebrate America! Bring-A-Friend Duo" ($50 for 2 friends, qualifier
    /// "Two Friends Just", no comparison price — real fixture data: fixtures/promotions/daily-tickets-page.html)
    /// classifies as Bundle and derives an explicit, tested per-person breakdown without collapsing
    /// the bundle total into a single-person base price.
    /// </summary>
    [Fact]
    public async Task LoadAssociateDateAsync_BringAFriendDuoBundle_DerivesExplicitPerPersonBreakdown()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "o1gUjW0O273eyMG69L54eV"),
            "Celebrate America! Bring-A-Friend Duo", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 50.00m, CurrentDisplayPrice: "$50", ComparisonPriceAmount: null, PriceQualifier: "Two Friends Just",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(new DateOnly(2026, 9, 15));

        var duo = workspace.Products.Single(p => p.DisplayName == "Celebrate America! Bring-A-Friend Duo");
        Assert.Equal(PriceKind.Bundle, duo.PriceKind);
        Assert.Equal(50.00m, duo.BundleTotalPriceAmount);
        Assert.Equal(2, duo.BundleQuantity);
        Assert.Equal(25.00m, duo.PerPersonPriceAmount);
        // The bundle total itself is untouched — never collapsed into the per-person figure.
        Assert.Equal(50.00m, duo.OfficialPriceAmount);
    }

    /// <summary>The real dated Bring-A-Friend Duo per-person price (accesso keyword SUBAF50) composes as its own distinct row, never colliding with the catalog bundle row.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_DatedBringAFriendDuo_ComposesAsExactDated_DistinctFromCatalogBundleRow()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();

        await datedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.BringAFriendDuo, Aug29),
            Evidence(Aug29),
            JsonSerializer.Serialize(new DatedBringAFriendDuoPayload(
            [
                new DatedBringAFriendDuoProduct("10097207", "Celebrate America! Bring-A-Friend Duo Offer", 25.00m, null, null, Aug29),
            ])));

        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "o1gUjW0O273eyMG69L54eV"),
            "Celebrate America! Bring-A-Friend Duo", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 50.00m, CurrentDisplayPrice: "$50", ComparisonPriceAmount: null, PriceQualifier: "Two Friends Just",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(Aug29);

        var datedRow = workspace.Products.Single(p => p.DisplayName == "Celebrate America! Bring-A-Friend Duo Offer");
        Assert.Equal(PriceKind.ExactDated, datedRow.PriceKind);
        Assert.Equal(25.00m, datedRow.OfficialPriceAmount);
        Assert.Equal(SourceCategory.BringAFriendDuo, datedRow.Category);

        var catalogRow = workspace.Products.Single(p => p.DisplayName == "Celebrate America! Bring-A-Friend Duo");
        Assert.Equal(SourceCategory.Promotions, catalogRow.Category);
        Assert.NotEqual(datedRow.ProductIdentity, catalogRow.ProductIdentity);
    }

    private static async Task SeedOrdinaryBringAFriendCatalogRowAsync(InMemoryCatalogProductRepository catalogProducts) =>
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "449e6fe8-a6ad-4af5-b162-b7835798b8ae"),
            "Bring-A-Friend Ticket", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 35.00m, CurrentDisplayPrice: "$35", ComparisonPriceAmount: 80.00m, PriceQualifier: "Starting From",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

    private static async Task SeedExactDatedBringAFriendAsync(InMemoryDatedResultRepository datedResults, DateOnly date, string packageId, decimal price) =>
        await datedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedBringAFriend, date),
            Evidence(date),
            JsonSerializer.Serialize(new DatedBringAFriendPayload(
            [
                new DatedBringAFriendProduct(packageId, "Bring-A-Friend Ticket", price, date),
            ])));

    /// <summary>
    /// Phase 15A §17 items 1/3/5/8: the real <see cref="SourceCategory.DatedBringAFriend"/> merge path
    /// end to end — Aug 29's live-confirmed exact price ($39.00) wins over the generic catalog's
    /// "Starting From $35.00", the row prints as a single, unified "Bring-A-Friend Ticket" (never two
    /// rows), the official "$80 Gate Price" comparison is retained (never fabricated, never dropped),
    /// and "Starting From" is dropped once an exact value applies.
    /// </summary>
    [Fact]
    public async Task LoadAssociateDateAsync_OrdinaryBringAFriend_Aug29_MergesExactDatedPriceIntoCatalogRow()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await SeedOrdinaryBringAFriendCatalogRowAsync(catalogProducts);
        await SeedExactDatedBringAFriendAsync(datedResults, Aug29, "10094895", 39.00m);

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(Aug29);

        var baf = Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(39.00m, baf.OfficialPriceAmount);
        Assert.Equal(80.00m, baf.ComparisonPriceAmount); // preserved from the catalog row, never fabricated
        Assert.Null(baf.PriceQualifier); // "Starting From" dropped for an Exact Dated price
        Assert.True(baf.DateSpecific);
        Assert.Equal(Aug29, baf.ApplicableDate);
        Assert.Equal(PriceKind.ExactDated, baf.PriceKind);
        Assert.Equal(SourceCategory.Promotions, baf.Category); // identity/category stay the catalog row's own
    }

    /// <summary>Phase 15A §17 item 2: Aug 30's live-confirmed exact price ($35.00) merges the same way.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_OrdinaryBringAFriend_Aug30_MergesExactDatedPrice()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await SeedOrdinaryBringAFriendCatalogRowAsync(catalogProducts);
        await SeedExactDatedBringAFriendAsync(datedResults, Aug30, "10094893", 35.00m);

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(Aug30);

        var baf = Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, baf.OfficialPriceAmount);
        Assert.Equal(80.00m, baf.ComparisonPriceAmount);
        Assert.Equal(PriceKind.ExactDated, baf.PriceKind);
    }

    /// <summary>Phase 15A §17 item 4: Aug 29's exact value never leaks onto Aug 30 — each date's own dated-result row is looked up independently.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_OrdinaryBringAFriend_DatesRemainIsolated()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await SeedOrdinaryBringAFriendCatalogRowAsync(catalogProducts);
        await SeedExactDatedBringAFriendAsync(datedResults, Aug29, "10094895", 39.00m);
        await SeedExactDatedBringAFriendAsync(datedResults, Aug30, "10094893", 35.00m);

        var service = CreateService(datedResults, catalogProducts);

        var workspaceFor29th = await service.LoadAssociateDateAsync(Aug29);
        Assert.Equal(39.00m, workspaceFor29th.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket").OfficialPriceAmount);

        var workspaceFor30th = await service.LoadAssociateDateAsync(Aug30);
        Assert.Equal(35.00m, workspaceFor30th.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket").OfficialPriceAmount);

        // A third, never-recorded date must never inherit either neighbor's exact value.
        var workspaceForUnrecordedDate = await service.LoadAssociateDateAsync(new DateOnly(2026, 9, 15));
        var untouched = workspaceForUnrecordedDate.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, untouched.OfficialPriceAmount); // the generic catalog floor, not $39 or $35-as-exact
        Assert.Equal(PriceKind.StartingFrom, untouched.PriceKind);
    }

    /// <summary>Phase 15A §17 item 6: with no exact dated value available for the selected date, the generic "Starting From" catalog row is left completely untouched — never converted into a fabricated Exact Dated row.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_OrdinaryBringAFriend_NoExactDatedValue_FallsBackToStartingFrom()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await SeedOrdinaryBringAFriendCatalogRowAsync(catalogProducts);
        // No DatedBringAFriend result recorded at all for this date.

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(Aug29);

        var baf = Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, baf.OfficialPriceAmount);
        Assert.Equal("Starting From", baf.PriceQualifier);
        Assert.Equal(PriceKind.StartingFrom, baf.PriceKind);
        Assert.False(baf.DateSpecific);
    }

    /// <summary>Phase 15A §17 item 6 (continued): a failed/errored dated check for the selected date also falls back to the untouched catalog row — never a fabricated exact value from a failed attempt.</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_OrdinaryBringAFriend_FailedDatedCheck_FallsBackToStartingFrom()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await SeedOrdinaryBringAFriendCatalogRowAsync(catalogProducts);
        await datedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedBringAFriend, Aug29),
            new SourceEvidence("Fake Source", SourceUrl, FixedNow, RetrievalStatus.Error, "Unavailable.", RetrievalStrategy.DirectApi, Aug29, confirmedDate: null));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(Aug29);

        var baf = Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, baf.OfficialPriceAmount);
        Assert.Equal(PriceKind.StartingFrom, baf.PriceKind);
    }

    /// <summary>Phase 15A §17 item 10: ordinary Bring-A-Friend's new merge logic never regresses Bring-A-Friend Duo — both remain fully independent (distinct categories, distinct identities, distinct display names).</summary>
    [Fact]
    public async Task LoadAssociateDateAsync_OrdinaryBringAFriendMerge_DoesNotAffectBringAFriendDuo()
    {
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        await SeedOrdinaryBringAFriendCatalogRowAsync(catalogProducts);
        await SeedExactDatedBringAFriendAsync(datedResults, Aug29, "10094895", 39.00m);
        await catalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "o1gUjW0O273eyMG69L54eV"),
            "Celebrate America! Bring-A-Friend Duo", ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 50.00m, CurrentDisplayPrice: "$50", ComparisonPriceAmount: null, PriceQualifier: "Two Friends Just",
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence()));

        var service = CreateService(datedResults, catalogProducts);
        var workspace = await service.LoadAssociateDateAsync(Aug29);

        var baf = Assert.Single(workspace.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(39.00m, baf.OfficialPriceAmount);

        var duo = Assert.Single(workspace.Products, p => p.DisplayName == "Celebrate America! Bring-A-Friend Duo");
        Assert.Equal(50.00m, duo.OfficialPriceAmount);
        Assert.Equal(2, duo.BundleQuantity);
        Assert.Equal(25.00m, duo.PerPersonPriceAmount);
    }
}
