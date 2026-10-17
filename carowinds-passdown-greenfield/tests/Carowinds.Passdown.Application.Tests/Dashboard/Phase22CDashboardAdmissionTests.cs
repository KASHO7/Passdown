using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Dashboard;

/// <summary>
/// Phase 22C: the Dashboard's Daily Admission policy (<see cref="DashboardAdmissionSelector"/>). Only the
/// ordinary "Park Ticket" is ever headlined; exact dated beats advertised "From"; ambiguity, bundles,
/// Military/First Responder, Fast Lane, custom rows, manual overrides, other dates and historical dates
/// never produce a price. All amounts are synthetic.
/// </summary>
public class Phase22CDashboardAdmissionTests
{
    private static readonly DateOnly Date = new(2026, 10, 16);
    private static readonly DateTimeOffset VerifiedAt = new(2026, 10, 16, 10, 5, 0, TimeSpan.Zero);
    private static readonly AssociateVerificationInfo Verified = new(AssociateVerificationState.Verified, VerifiedAt, null, null);

    private static AssociateProduct Exact(string name, decimal price, DateOnly? date = null, SourceCategory category = SourceCategory.DatedAdmissionTickets, AssociateVerificationInfo? verification = null) =>
        new($"{category}|{name}|{price}", name, category, IsCustom: false, price, null, null, null, DateSpecific: true, date ?? Date,
            verification ?? Verified, IncludeOnPdf: true, DisplayPriceOverride: null, PriceKind.ExactDated);

    private static AssociateProduct From(string name, decimal price, SourceCategory category = SourceCategory.Promotions) =>
        new($"{category}|{name}|from", name, category, IsCustom: false, price, null, null, "Starting From", DateSpecific: false, null,
            Verified, IncludeOnPdf: true, DisplayPriceOverride: null, PriceKind.StartingFrom);

    private static AssociateWorkspace Workspace(IReadOnlyList<AssociateProduct> products, AssociateVerificationState admissionState = AssociateVerificationState.Verified, AssociatePricingTimeframe timeframe = AssociatePricingTimeframe.Current) =>
        new(Date,
            AssociateDraftPayload.CreateDefault(),
            new AssociateParkHoursView(ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null), Verified, false, new TimeOnly(10, 0), new TimeOnly(22, 0)),
            products,
            [new AssociateProductCategoryStatus(SourceCategory.DatedAdmissionTickets, admissionState, VerifiedAt, null)],
            AssociateSaveStatus.NeverSaved)
        {
            PricingTimeframe = timeframe,
        };

    [Fact]
    public void ExactDatedParkTicket_IsTheHeadline_WithItsProductNameDateAndVerification()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Park Ticket", 64.99m)]));

        Assert.Equal(DashboardAdmissionKind.ExactDated, summary.Kind);
        Assert.Equal("Park Ticket", summary.ProductName);
        Assert.Equal(64.99m, summary.PriceAmount);
        Assert.Equal(Date, summary.ApplicableDate);
        Assert.Equal(AssociateVerificationState.Verified, summary.PriceState);
        Assert.Equal(VerifiedAt, summary.LastVerifiedAt);
    }

    [Fact]
    public void Bundles_AreNeverChosen_EvenWhenCheaper_AndNeverCombined()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace(
        [
            Exact("Ticket + Meal, Snack & Drink Bundle", 49.00m),
            Exact("Ticket + Fast Lane Reserve Bundle", 39.00m),
            Exact("Park Ticket", 64.99m),
        ]));

        Assert.Equal("Park Ticket", summary.ProductName);
        Assert.Equal(64.99m, summary.PriceAmount);
    }

    [Fact]
    public void OnlyBundlesListed_NoGeneralAdmissionPrice()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Ticket + Meal, Snack & Drink Bundle", 49.00m)]));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
        Assert.Equal(DashboardAdmissionUnavailableReason.NoProduct, summary.UnavailableReason);
        Assert.Null(summary.PriceAmount);
    }

    [Theory]
    [InlineData(SourceCategory.MilitaryFirstResponder)]
    [InlineData(SourceCategory.DatedMilitaryFirstResponder)]
    [InlineData(SourceCategory.DatedFastLane)]
    [InlineData(SourceCategory.AddOns)]
    [InlineData(SourceCategory.SeasonPasses)]
    public void EligibilityRestrictedOrAddOnCategories_AreNeverGeneralAdmission_EvenWhenNamedParkTicket(SourceCategory category)
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Park Ticket", 40m, category: category), From("Park Ticket", 35m, category)]));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
    }

    [Fact]
    public void ExactPriceForAnotherDate_IsNeverTodaysPrice()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Park Ticket", 59m, Date.AddDays(1))]));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
    }

    [Fact]
    public void NoExactPrice_FallsBackToTheSameProductsAdvertisedFromPrice_NeverRelabeledAsToday()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([From("Park Ticket", 44.99m), From("Season Pass", 99m, SourceCategory.SeasonPasses)], AssociateVerificationState.NotPublished));

        Assert.Equal(DashboardAdmissionKind.StartingFrom, summary.Kind);
        Assert.Equal(44.99m, summary.PriceAmount);
        Assert.Null(summary.ApplicableDate);
        Assert.Equal(AssociateVerificationState.NotPublished, summary.CategoryState);
    }

    [Fact]
    public void ExactPrice_WinsOverAnAdvertisedFromPrice()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([From("Park Ticket", 44.99m), Exact("Park Ticket", 64.99m)]));

        Assert.Equal(DashboardAdmissionKind.ExactDated, summary.Kind);
        Assert.Equal(64.99m, summary.PriceAmount);
    }

    [Fact]
    public void TwoDifferentParkTicketPrices_AreAmbiguous_AndNeverResolvedByPickingTheLowest()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Park Ticket", 49m), Exact("Park Ticket", 55m)]));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
        Assert.Equal(DashboardAdmissionUnavailableReason.Ambiguous, summary.UnavailableReason);
        Assert.Null(summary.PriceAmount);
    }

    [Fact]
    public void ManualDisplayOverride_IsNeverPresentedAsTheOfficialPrice()
    {
        var product = Exact("Park Ticket", 64.99m) with { DisplayPriceOverride = 10m };

        var summary = DashboardAdmissionSelector.Select(Workspace([product]));

        Assert.Equal(64.99m, summary.PriceAmount);
    }

    [Fact]
    public void CustomProduct_NamedParkTicket_IsNeverOfficial()
    {
        var custom = new AssociateProduct("custom|1", "Park Ticket", null, IsCustom: true, null, null, null, null, false, null,
            AssociateVerificationInfo.NotCheckedYet, true, 12m);

        var summary = DashboardAdmissionSelector.Select(Workspace([custom]));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
    }

    [Fact]
    public void HistoricalWorkspace_NeverYieldsAPrice()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Park Ticket", 64.99m)], timeframe: AssociatePricingTimeframe.Historical));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
        Assert.Equal(DashboardAdmissionUnavailableReason.Historical, summary.UnavailableReason);
    }

    [Fact]
    public void StaleExactPrice_KeepsItsStaleStateAndSuccessTimestamp()
    {
        var stale = new AssociateVerificationInfo(AssociateVerificationState.Stale, VerifiedAt.AddDays(-1), TimeSpan.FromDays(1), null);

        var summary = DashboardAdmissionSelector.Select(Workspace([Exact("Park Ticket", 64.99m, verification: stale)], AssociateVerificationState.Stale));

        Assert.Equal(DashboardAdmissionKind.ExactDated, summary.Kind);
        Assert.Equal(AssociateVerificationState.Stale, summary.PriceState);
        Assert.Equal(VerifiedAt.AddDays(-1), summary.LastVerifiedAt);
    }

    [Theory]
    [InlineData("Park Ticket", true)]
    [InlineData("  park   TICKET ", true)]
    [InlineData("Carowinds Park Ticket", true)]
    [InlineData("Park Ticket + Parking", false)]
    [InlineData("Military Park Ticket", false)]
    [InlineData("2-Day Park Ticket", false)]
    public void GeneralAdmissionName_IsAnExactIdentity_NotASubstringMatch(string name, bool expected) =>
        Assert.Equal(expected, DashboardAdmissionSelector.IsGeneralAdmissionName(name));

    [Fact]
    public void NothingChecked_ReportsTheSourceStateHonestly()
    {
        var summary = DashboardAdmissionSelector.Select(Workspace([], AssociateVerificationState.NotCheckedYet));

        Assert.Equal(DashboardAdmissionKind.NotAvailable, summary.Kind);
        Assert.Equal(AssociateVerificationState.NotCheckedYet, summary.CategoryState);
    }

    // End to end through the real AssociatePassdownService composition: the official dated payload
    // (Park Ticket + two bundles) yields the Park Ticket, read from local data only.
    [Fact]
    public async Task DashboardService_ProjectsTheDatedParkTicket_FromLocalData()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero)); // 2:00 PM EDT
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalog = new InMemoryCatalogProductRepository();
        var refresh = new FakeRefreshOrchestrator();
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var associate = new AssociatePassdownService(drafts, datedResults, catalog, refresh, notes, clock);
        var service = new OperationsDashboardService(associate, new InMemoryFinalizedPassdownRepository(), notes, clock);

        var payload = new DatedAdmissionPayload(
        [
            new DatedAdmissionProduct("1", "Park Ticket", 64.99m, null, null, Date),
            new DatedAdmissionProduct("2", "Ticket + Meal, Snack & Drink Bundle", 89.99m, null, null, Date),
            new DatedAdmissionProduct("3", "Ticket + Fast Lane Reserve Bundle", 129.99m, null, null, Date),
        ]);
        var evidence = new SourceEvidence("Synthetic Dated Admission", new Uri("https://example.test/synthetic-admission"), clock.UtcNow.AddHours(-4), RetrievalStatus.Verified, "Verified.", RetrievalStrategy.DirectApi, Date, Date);
        await datedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date), evidence, JsonSerializer.Serialize(payload));

        var snapshot = await service.GetTodayAsync();

        Assert.Equal(DashboardAdmissionKind.ExactDated, snapshot.Admission.Kind);
        Assert.Equal("Park Ticket", snapshot.Admission.ProductName);
        Assert.Equal(64.99m, snapshot.Admission.PriceAmount);
        Assert.Equal(AssociateVerificationState.Verified, snapshot.Admission.CategoryState);
        Assert.Empty(refresh.ManualDateRefreshCalls);
    }
}
