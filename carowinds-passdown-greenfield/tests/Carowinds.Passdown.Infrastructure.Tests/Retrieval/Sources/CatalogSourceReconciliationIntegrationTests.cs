using System.Net;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Focused integration tests proving every Phase 3F catalog adapter's products feed
/// <see cref="ProductReconciliationService"/> without any adapter-invented fields — a real SQLite
/// database (via <see cref="SqliteRepositoryTestFixture"/>, the same fixture the repository test
/// suite uses), not a mock. Originally added for <see cref="PromotionsCatalogSource"/> and
/// <see cref="GeneralAddOnsCatalogSource"/> (Phase 3F4/3F5 Part E); extended in the Phase 3F6 review
/// to also cover <see cref="SeasonPassCatalogSource"/> and <see cref="SeasonPassAddOnsCatalogSource"/>
/// (Phase 3F2/3F3), which had contract-test coverage but no adapter-to-reconciliation integration
/// proof until now. Exercises the lifecycle transitions spec §7.3 requires, not any new rule: new
/// product becomes Active, a first confirmed successful absence becomes PendingConfirmation, and a
/// source <see cref="RetrievalStatus.Error"/> is rejected rather than silently reconciled as a
/// successful empty catalog.
/// </summary>
public class CatalogSourceReconciliationIntegrationTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private ProductReconciliationService BuildReconciliationService() => new(
        new SqliteCatalogProductRepository(_fixture.ConnectionFactory),
        new SqliteProductHistoryRepository(_fixture.ConnectionFactory),
        new SqliteUnitOfWorkFactory(_fixture.ConnectionFactory));

    private static RetrievalHttpResponse HtmlResponse(string fixtureRelativePath) =>
        new(HttpStatusCode.OK, "text/html", FixtureFile.ReadText(fixtureRelativePath));

    [Fact]
    public async Task PromotionsProducts_NewlyObserved_BecomeActiveWithTheAdaptersOwnFields()
    {
        var source = new PromotionsCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("promotions/published-page.html")), new FakeClock(SampleUtcNow));
        var reconciliation = BuildReconciliationService();

        var retrieval = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);
        var result = await reconciliation.ReconcileAsync(source.Category, retrieval.Evidence, retrieval.Payload!.Products);

        Assert.Equal(2, result.Products.Count);
        Assert.All(result.Products, p => Assert.Equal(ReconciliationOutcome.Added, p.Outcome));

        var goldPassKey = retrieval.Payload.Products.Single(p => p.DisplayName == "2027 Gold Pass").Key;
        var stored = await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(goldPassKey);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        // Exactly the price/qualifier the adapter parsed from the fixture — nothing invented by reconciliation.
        Assert.Equal(89.00m, stored.CurrentPriceAmount);
        Assert.Equal(115.00m, stored.ComparisonPriceAmount);
        Assert.Equal("Sale Price", stored.PriceQualifier);
    }

    [Fact]
    public async Task GeneralAddOnsProducts_NewlyObserved_BecomeActiveWithTheAdaptersOwnFields()
    {
        var source = new GeneralAddOnsCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("general-add-ons/published-page.html")), new FakeClock(SampleUtcNow));
        var reconciliation = BuildReconciliationService();

        var retrieval = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);
        var result = await reconciliation.ReconcileAsync(source.Category, retrieval.Evidence, retrieval.Payload!.Products);

        Assert.Equal(2, result.Products.Count);
        Assert.All(result.Products, p => Assert.Equal(ReconciliationOutcome.Added, p.Outcome));

        var singleDayKey = retrieval.Payload.Products.Single(p => p.DisplayName == "Single Day FunPix").Key;
        var stored = await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(singleDayKey);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        Assert.Equal(19.99m, stored.CurrentPriceAmount);
        // No comparison price/qualifier markup exists on this page (docs) — reconciliation must not invent one.
        Assert.Null(stored.ComparisonPriceAmount);
        Assert.Null(stored.PriceQualifier);
    }

    [Fact]
    public async Task SeasonPassesProducts_NewlyObserved_BecomeActiveWithTheAdaptersOwnFields()
    {
        var source = new SeasonPassCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("season-passes/published-page.html")), new FakeClock(SampleUtcNow));
        var reconciliation = BuildReconciliationService();

        var retrieval = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);
        var result = await reconciliation.ReconcileAsync(source.Category, retrieval.Evidence, retrieval.Payload!.Products);

        Assert.Equal(2, result.Products.Count);
        Assert.All(result.Products, p => Assert.Equal(ReconciliationOutcome.Added, p.Outcome));

        var goldPassKey = retrieval.Payload.Products.Single(p => p.DisplayName == "2027 Gold Pass").Key;
        var stored = await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(goldPassKey);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        Assert.Equal(89.00m, stored.CurrentPriceAmount);
        Assert.Equal(115.00m, stored.ComparisonPriceAmount);
        Assert.Equal("Sale Price", stored.PriceQualifier);
    }

    [Fact]
    public async Task SeasonPassAddOnsProducts_NewlyObserved_BecomeActiveWithTheAdaptersOwnFields()
    {
        var source = new SeasonPassAddOnsCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("season-pass-add-ons/published-page.html")), new FakeClock(SampleUtcNow));
        var reconciliation = BuildReconciliationService();

        var retrieval = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);
        var result = await reconciliation.ReconcileAsync(source.Category, retrieval.Evidence, retrieval.Payload!.Products);

        Assert.Equal(3, result.Products.Count);
        Assert.All(result.Products, p => Assert.Equal(ReconciliationOutcome.Added, p.Outcome));

        var fastLaneKey = retrieval.Payload.Products.Single(p => p.DisplayName == "All Season Fast Lane").Key;
        var stored = await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(fastLaneKey);
        Assert.Equal(ProductLifecycleState.Active, stored!.LifecycleState);
        Assert.Equal(499.00m, stored.CurrentPriceAmount);
        Assert.Equal(899.00m, stored.ComparisonPriceAmount);
        Assert.Equal("Only", stored.PriceQualifier);
    }

    [Fact]
    public async Task PromotionsProducts_MissingFromASubsequentSuccessfulRetrieval_BecomePendingConfirmation()
    {
        var firstSource = new PromotionsCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("promotions/published-page.html")), new FakeClock(SampleUtcNow));
        var reconciliation = BuildReconciliationService();

        var firstRetrieval = await firstSource.RetrieveAsync(new RetrievalRequest(firstSource.SourceId, firstSource.Category), CancellationToken.None);
        await reconciliation.ReconcileAsync(firstSource.Category, firstRetrieval.Evidence, firstRetrieval.Payload!.Products);

        // A later successful retrieval of the same page that genuinely returns zero cards.
        var secondSource = new PromotionsCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("catalog-source/no-products-page.html")), new FakeClock(SampleUtcNow.AddDays(1)));
        var secondRetrieval = await secondSource.RetrieveAsync(new RetrievalRequest(secondSource.SourceId, secondSource.Category), CancellationToken.None);
        var secondResult = await reconciliation.ReconcileAsync(secondSource.Category, secondRetrieval.Evidence, secondRetrieval.Payload!.Products);

        Assert.Equal(2, secondResult.Products.Count);
        Assert.All(secondResult.Products, p => Assert.Equal(ReconciliationOutcome.PendingRemoval, p.Outcome));

        var goldPassKey = firstRetrieval.Payload.Products.Single(p => p.DisplayName == "2027 Gold Pass").Key;
        var stored = await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(goldPassKey);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, stored!.LifecycleState);
    }

    [Fact]
    public async Task GeneralAddOnsSourceError_MustNotBeReconciledAsASuccessfulEmptyCatalog()
    {
        var source = new GeneralAddOnsCatalogSource(
            new FakeRetrievalHttpClient(HtmlResponse("general-add-ons/malformed-card.html")), new FakeClock(SampleUtcNow));
        var reconciliation = BuildReconciliationService();

        var retrieval = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, retrieval.Evidence.Status);
        Assert.Null(retrieval.Payload);

        // Guard already enforced by ProductReconciliationService (spec §6.7/§7.1): an Error result
        // must never be handed to reconciliation as if it were a confirmed-absent retrieval.
        await Assert.ThrowsAsync<ArgumentException>(
            () => reconciliation.ReconcileAsync(source.Category, retrieval.Evidence, []));
    }
}
