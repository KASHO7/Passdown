using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Tests.Dashboard;

/// <summary>
/// Phase 35E Dashboard follow-up: the two new read-only projections behind the approved Admissions Overview —
/// today's official dated price list (<see cref="DashboardPricingSelector"/>) and the cached Pass Perks on the
/// snapshot. Neither retrieves, persists or invents anything. All amounts are synthetic.
/// </summary>
public class Phase35EDashboardProjectionTests
{
    private static readonly DateOnly Date = new(2026, 11, 22);
    private static readonly DateTimeOffset VerifiedAt = new(2026, 11, 22, 12, 44, 0, TimeSpan.Zero);
    private static readonly AssociateVerificationInfo Verified = new(AssociateVerificationState.Verified, VerifiedAt, null, null);

    private static AssociateProduct Exact(string name, decimal price, SourceCategory category = SourceCategory.DatedAdmissionTickets, DateOnly? date = null,
        AssociateVerificationInfo? verification = null, decimal? displayOverride = null, bool isCustom = false) =>
        new($"{category}|{name}|{price}", name, isCustom ? null : category, isCustom, price, null, null, null, DateSpecific: true, date ?? Date,
            verification ?? Verified, IncludeOnPdf: true, DisplayPriceOverride: displayOverride, PriceKind.ExactDated);

    private static AssociateProduct From(string name, decimal price, SourceCategory category = SourceCategory.DatedAdmissionTickets) =>
        new($"{category}|{name}|from", name, category, IsCustom: false, price, null, null, "Starting From", DateSpecific: false, null,
            Verified, IncludeOnPdf: true, DisplayPriceOverride: null, PriceKind.StartingFrom);

    private static AssociateWorkspace Workspace(IReadOnlyList<AssociateProduct> products, AssociatePricingTimeframe timeframe = AssociatePricingTimeframe.Current) =>
        new(Date,
            AssociateDraftPayload.CreateDefault(),
            new AssociateParkHoursView(ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(18, 0), false, null, null, false, null, null), Verified, false, new TimeOnly(11, 0), new TimeOnly(18, 0)),
            products,
            [],
            AssociateSaveStatus.NeverSaved)
        {
            PricingTimeframe = timeframe,
        };

    // ------------------------------------------------------------------ pricing policy

    [Fact]
    public void Pricing_ListsTodaysExactDatedTicketAndFastLanePrices_TicketsFirst_InWorkspaceOrder()
    {
        var summary = DashboardPricingSelector.Select(Workspace(
        [
            Exact("Fast Lane Reserve", 45m, SourceCategory.DatedFastLane),
            Exact("Park Ticket", 50m),
            Exact("Ticket + Meal Bundle", 65m),
            Exact("Fast Lane Ultimate", 65m, SourceCategory.DatedFastLane),
        ]));

        Assert.Equal(["Park Ticket", "Ticket + Meal Bundle", "Fast Lane Reserve", "Fast Lane Ultimate"], summary.Items.Select(i => i.ProductName));
        Assert.Equal([50m, 65m, 45m, 65m], summary.Items.Select(i => i.OfficialPriceAmount));
        Assert.Equal(4, summary.TotalCount);
        Assert.All(summary.Items, item => Assert.Equal(VerifiedAt, item.LastVerifiedAt));
    }

    [Fact]
    public void Pricing_NeverListsAStartingFromPrice_AnotherDate_ACustomRow_OrOtherSections()
    {
        var summary = DashboardPricingSelector.Select(Workspace(
        [
            From("Park Ticket", 39.99m),
            Exact("Park Ticket", 50m, date: Date.AddDays(1)),
            Exact("Supervisor Special", 10m, isCustom: true),
            Exact("Season Pass", 99m, SourceCategory.SeasonPasses),
            Exact("All Season Dining", 150m, SourceCategory.AddOns),
            Exact("Bring-A-Friend Ticket", 30m, SourceCategory.Promotions),
        ]));

        Assert.Empty(summary.Items);
        Assert.Equal(0, summary.TotalCount);
        Assert.False(summary.HasItems);
    }

    [Fact]
    public void Pricing_AlwaysShowsTheOfficialAmount_NeverADraftOverride()
    {
        var summary = DashboardPricingSelector.Select(Workspace([Exact("Park Ticket", 50m, displayOverride: 1m)]));

        Assert.Equal(50m, Assert.Single(summary.Items).OfficialPriceAmount);
    }

    [Fact]
    public void Pricing_HistoricalWorkspace_ListsNothing()
    {
        var summary = DashboardPricingSelector.Select(Workspace([Exact("Park Ticket", 50m)], AssociatePricingTimeframe.Historical));

        Assert.Empty(summary.Items);
    }

    [Fact]
    public void Pricing_KeepsAStaleRowMarkedStale()
    {
        var stale = new AssociateVerificationInfo(AssociateVerificationState.Stale, VerifiedAt.AddDays(-2), null, null);
        var summary = DashboardPricingSelector.Select(Workspace([Exact("Park Ticket", 50m, verification: stale)]));

        Assert.Equal(AssociateVerificationState.Stale, Assert.Single(summary.Items).State);
    }

    [Fact]
    public void Pricing_ListsAnExactDuplicateOnce_IsBounded_AndKeepsTheTrueCount()
    {
        var products = new List<AssociateProduct> { Exact("Park Ticket", 50m), Exact("PARK TICKET ", 50m) };
        products.AddRange(Enumerable.Range(1, 6).Select(i => Exact($"Fast Lane {i}", 40m + i, SourceCategory.DatedFastLane)));

        var summary = DashboardPricingSelector.Select(Workspace(products));

        Assert.Equal(DashboardPricingSummary.MaxItems, summary.Items.Count);
        Assert.Equal(7, summary.TotalCount);
        Assert.Single(summary.Items, item => item.ProductName.Equals("Park Ticket", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ snapshot wiring (real AssociatePassdownService, in-memory stores)

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 14, 0, 0, TimeSpan.Zero); // 9:00 AM EST

    private static (OperationsDashboardService Service, FakeRefreshOrchestrator Orchestrator) CreateService(IPassPerksService? passPerks)
    {
        var clock = new FakeClock(Now);
        var operationalNotes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var orchestrator = new FakeRefreshOrchestrator();
        var associate = new AssociatePassdownService(new InMemoryAssociateDraftRepository(), new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(), orchestrator, operationalNotes, clock);
        return (new OperationsDashboardService(associate, new InMemoryFinalizedPassdownRepository(), operationalNotes, clock, passPerksService: passPerks), orchestrator);
    }

    private sealed class StubPassPerks(Func<PassPerksOverview> factory) : IPassPerksService
    {
        public int Calls { get; private set; }

        public Task<PassPerksOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(factory());
        }
    }

    [Fact]
    public async Task Snapshot_CarriesTheCachedPassPerks_ForItsOwnOperatingDate_WithOneLocalRead_AndNoRetrieval()
    {
        var overview = PassPerksOverview.NotChecked(Date, Now);
        var stub = new StubPassPerks(() => overview);
        var (service, orchestrator) = CreateService(stub);

        var snapshot = await service.GetTodayAsync();

        Assert.Equal(Date, snapshot.OperatingDate);
        Assert.Same(overview, snapshot.PassPerks);
        Assert.False(snapshot.PassPerksLoadFailed);
        Assert.Equal(1, stub.Calls);
        Assert.Empty(orchestrator.ManualDateRefreshCalls);
        Assert.Empty(orchestrator.DeepRefreshCalls);
    }

    [Fact]
    public async Task Snapshot_ForAnotherDate_NeverShowsPerksEvaluatedForToday()
    {
        var (service, _) = CreateService(new StubPassPerks(() => PassPerksOverview.NotChecked(Date, Now)));

        var snapshot = await service.LoadAsync(Date.AddDays(-1));

        Assert.Null(snapshot.PassPerks);
        Assert.False(snapshot.PassPerksLoadFailed);
    }

    [Fact]
    public async Task PassPerksReadFailure_DegradesOnlyThatSection()
    {
        var (service, _) = CreateService(new StubPassPerks(() => throw new InvalidOperationException("cache unreadable")));

        var snapshot = await service.GetTodayAsync();

        Assert.Null(snapshot.PassPerks);
        Assert.True(snapshot.PassPerksLoadFailed);
        Assert.Equal(Date, snapshot.OperatingDate); // the rest of the snapshot still loads
    }

    [Fact]
    public async Task WithoutAPassPerksService_TheSnapshotHasNoPerks_AndNoFailure()
    {
        var (service, _) = CreateService(passPerks: null);

        var snapshot = await service.GetTodayAsync();

        Assert.Null(snapshot.PassPerks);
        Assert.False(snapshot.PassPerksLoadFailed);
        Assert.Empty(snapshot.Pricing.Items); // an empty catalog lists no prices
    }

    [Fact]
    public void OfficialDocumentStatus_NotChecked_IsWhatAnUncheckedOverviewCarries()
    {
        Assert.Equal(OfficialDocumentFreshness.NotChecked, PassPerksOverview.NotChecked(Date, Now).Status.Freshness);
    }

    // ------------------------------------------------------------------ Eastern operating date

    [Theory]
    [InlineData("2026-11-01T05:30:00Z", "2026-11-01")] // 1:30 AM EDT, the night daylight saving ends
    [InlineData("2026-11-01T06:30:00Z", "2026-11-01")] // 1:30 AM EST, the repeated hour
    [InlineData("2027-03-14T06:30:00Z", "2027-03-14")] // 1:30 AM EST, just before clocks spring forward
    [InlineData("2027-03-14T07:30:00Z", "2027-03-14")] // 3:30 AM EDT, just after
    [InlineData("2027-01-01T04:59:00Z", "2026-12-31")] // 11:59 PM EST on New Year's Eve (UTC is already 2027)
    [InlineData("2027-01-01T05:00:00Z", "2027-01-01")] // midnight EST
    [InlineData("2026-11-23T03:00:00Z", "2026-11-22")] // 10:00 PM EST; the UTC/computer date is already the 23rd
    public async Task Dashboard_Today_IsTheCarowindsEasternDate_NeverTheComputersOrUtc(string utc, string expected)
    {
        var clock = new FakeClock(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));
        var operationalNotes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var associate = new AssociatePassdownService(new InMemoryAssociateDraftRepository(), new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(), operationalNotes, clock);
        var service = new OperationsDashboardService(associate, new InMemoryFinalizedPassdownRepository(), operationalNotes, clock);

        var snapshot = await service.GetTodayAsync();

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), snapshot.OperatingDate);
    }
}
