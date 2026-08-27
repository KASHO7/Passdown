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
/// Live reconfirmation (2026-08-27, docs/retrieval/special-ticket-sources.md) found that ordinary
/// (non-Duo) Bring-A-Friend's passholder-exclusive dated price is only ever revealed after an
/// authenticated passholder login inside the shared <c>SnapWizard</c> checkout — not reachable by
/// any anonymous official request this app is allowed to make (CLAUDE.md prohibits entering
/// credentials). No adapter for it exists for exactly that reason. This test therefore exercises
/// the real <see cref="SourceCategory.DatedAdmissionTickets"/> composition path (already wired,
/// already production code) with a representative payload shaped like the documented manual
/// observation, to prove the precedence *mechanism* the spec requires — not to claim a live
/// "Season Pass Bring-A-Friend" adapter exists.
/// </summary>
public class AssociatePassdownServicePriceSemanticsTests
{
    private static readonly DateOnly Aug29 = new(2026, 8, 29);
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
}
