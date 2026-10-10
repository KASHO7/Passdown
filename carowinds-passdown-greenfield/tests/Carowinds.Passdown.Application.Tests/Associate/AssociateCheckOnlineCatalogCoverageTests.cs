using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 13B Part AF #6: an end-to-end proof — using the real <see cref="RefreshOrchestrator"/>, not
/// <see cref="FakeRefreshOrchestrator"/> — that <see cref="IAssociatePassdownService.CheckAssociateDateOnlineAsync"/>
/// (the one shared explicit-refresh pathway both Dashboard's and Associate's own Check Online actions
/// call) actually force-refreshes every registered catalog family alongside the requested date's dated
/// sources, closing the exact gap Phase 13A found (Season Passes/Add-Ons/Promotions previously never
/// force-refreshed by either "Check Online" surface).
/// </summary>
public class AssociateCheckOnlineCatalogCoverageTests
{
    private static readonly DateOnly Date = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_WithRealOrchestrator_ForceRefreshesDatedAndEveryCatalogFamily()
    {
        var clock = new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var sourceStates = new InMemorySourceStateRepository();
        var operationalNotes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);

        var parkHoursSource = new FakeOfficialDataSource<FakePayload>(
            "park-hours", SourceCategory.ParkHours,
            (request, _) => Task.FromResult(SourceResult<FakePayload>.Success("park-hours", FakeEvidence.Verified(FixedNow, request.RequestedDate), new FakePayload("ok"))));

        var reconciliation = new ProductReconciliationService(catalogProducts, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());
        var catalogCallCounts = new Dictionary<string, int>();
        CatalogSourceRegistration CatalogSource(string sourceId, SourceCategory category, string productName) =>
            new(
                new FakeOfficialDataSource<CatalogPayload>(sourceId, category, (_, _) =>
                {
                    catalogCallCounts[sourceId] = catalogCallCounts.GetValueOrDefault(sourceId) + 1;
                    var product = new ObservedProduct(
                        CatalogProductKey.FromSourceAndName(category, sourceId, productName),
                        productName, 49.99m, "$49.99", null, null, DateSpecific: false, ApplicableDate: null);
                    return Task.FromResult(SourceResult<CatalogPayload>.Success(sourceId, FakeEvidence.Verified(FixedNow), new CatalogPayload([product])));
                }),
                reconciliation);

        var registrations = new List<SourceRegistration>
        {
            new DatedSourceRegistration<FakePayload>(parkHoursSource, datedResults),
            CatalogSource("season-passes", SourceCategory.SeasonPasses, "Gold Pass"),
            CatalogSource("season-pass-add-ons", SourceCategory.AddOns, "Meal Plan Add-On"),
            CatalogSource("general-add-ons", SourceCategory.AddOns, "Parking Pass"),
            CatalogSource("promotions", SourceCategory.Promotions, "Bring-A-Friend"),
        };

        // Every source just succeeded moments ago — well within cadence for every category — proving
        // the force refresh below runs regardless of freshness (Part G).
        foreach (var registration in registrations)
        {
            await sourceStates.SaveAsync(new SourceStateRecord(registration.SourceId, registration.Category, FixedNow, FixedNow, null, null, 0));
        }

        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var freshnessPlanner = new FreshnessPlanner(sourceStates, clock, policies);
        var rollingDatePlanner = new RollingDatePlanner(clock, new RollingWindowOptions());
        var orchestrator = new RefreshOrchestrator(
            registrations, freshnessPlanner, rollingDatePlanner, sourceStates, policies,
            new FakeJobBrowserSessionFactory(), clock, new RefreshOrchestratorOptions());

        var service = new AssociatePassdownService(drafts, datedResults, catalogProducts, orchestrator, operationalNotes, clock);

        var result = await service.CheckAssociateDateOnlineAsync(Date);

        Assert.Equal(1, catalogCallCounts.GetValueOrDefault("season-passes"));
        Assert.Equal(1, catalogCallCounts.GetValueOrDefault("season-pass-add-ons"));
        Assert.Equal(1, catalogCallCounts.GetValueOrDefault("general-add-ons"));
        Assert.Equal(1, catalogCallCounts.GetValueOrDefault("promotions"));
        Assert.Equal(5, result.Summary.OperationsAttempted);
        Assert.Equal(5, result.Summary.Succeeded);

        // The recomposed workspace actually reflects the newly force-refreshed catalog products —
        // not just that the orchestrator ran them.
        var productNames = result.Workspace.Products.Select(p => p.DisplayName).ToHashSet();
        Assert.Contains("Gold Pass", productNames);
        Assert.Contains("Meal Plan Add-On", productNames);
        Assert.Contains("Parking Pass", productNames);
        Assert.Contains("Bring-A-Friend", productNames);
    }

    private sealed record FakePayload(string Value);
}
