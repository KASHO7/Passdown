using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Associate;

public class AssociatePassdownServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 21);
    private static readonly DateOnly OtherDate = new(2026, 8, 22);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);
    private static readonly Uri SourceUrl = new("https://example.test/fake-source");

    private sealed record TestEnvironment(
        AssociatePassdownService Service,
        InMemoryAssociateDraftRepository Drafts,
        InMemoryDatedResultRepository DatedResults,
        InMemoryCatalogProductRepository CatalogProducts,
        FakeRefreshOrchestrator RefreshOrchestrator,
        FakeClock Clock);

    private static TestEnvironment CreateEnvironment(FakeClock? clock = null)
    {
        clock ??= new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var service = new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, clock);
        return new TestEnvironment(service, drafts, datedResults, catalogProducts, refreshOrchestrator, clock);
    }

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset timeChecked, DateOnly? requestedDate = null) =>
        new("Fake Source", SourceUrl, timeChecked, status, $"{status}.", RetrievalStrategy.DirectApi,
            requestedDate, confirmedDate: status == RetrievalStatus.Verified ? requestedDate : null);

    private static Task SeedParkHoursAsync(InMemoryDatedResultRepository repo, DateOnly date, TimeOnly opening, TimeOnly closing, DateTimeOffset timeChecked) =>
        repo.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, date),
            Evidence(RetrievalStatus.Verified, timeChecked, date),
            JsonSerializer.Serialize(new ParkHoursPayload(opening, closing)));

    private static Task SeedParkHoursClosedAsync(InMemoryDatedResultRepository repo, DateOnly date, DateTimeOffset timeChecked) =>
        repo.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), Evidence(RetrievalStatus.Closed, timeChecked, date), payloadJson: null);

    private static Task SeedParkHoursFailedAsync(InMemoryDatedResultRepository repo, DateOnly date, DateTimeOffset timeChecked) =>
        repo.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ParkHours, date), Evidence(RetrievalStatus.Error, timeChecked, date));

    private static Task SeedAdmissionAsync(InMemoryDatedResultRepository repo, DateOnly date, DateTimeOffset timeChecked, params DatedAdmissionProduct[] products) =>
        repo.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, date),
            Evidence(RetrievalStatus.Verified, timeChecked, date),
            JsonSerializer.Serialize(new DatedAdmissionPayload(products)));

    private static Task SeedHauntedAsync(InMemoryDatedResultRepository repo, DateOnly date, DateTimeOffset timeChecked, params DatedHauntedProduct[] products) =>
        repo.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.HauntedAttractions, date),
            Evidence(RetrievalStatus.Verified, timeChecked, date),
            JsonSerializer.Serialize(new DatedHauntedPayload(products)));

    private static string SeedCatalogProduct(
        InMemoryCatalogProductRepository repo,
        SourceCategory category,
        string name,
        decimal price,
        DateTimeOffset seenAt,
        decimal? comparisonPrice = null,
        string? qualifier = null,
        ProductLifecycleState lifecycle = ProductLifecycleState.Active)
    {
        var key = CatalogProductKey.FromSourceAndName(category, "Fake Source", name);
        repo.UpsertAsync(new CatalogProductUpsert(
            key, name, lifecycle, seenAt, seenAt, seenAt, ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: price, CurrentDisplayPrice: $"${price}", ComparisonPriceAmount: comparisonPrice, PriceQualifier: qualifier,
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence(RetrievalStatus.Verified, seenAt)));
        return AssociateProductIdentity.ForCatalogProduct(key.ToString());
    }

    /// <summary>
    /// Field-by-field equality rather than <c>Assert.Equal(expected, actual)</c> on the whole
    /// record: <see cref="AssociateDraftPayload"/>'s compiler-generated <c>Equals</c> compares its
    /// <c>IReadOnlyList</c>/<c>IReadOnlyDictionary</c> properties via <c>EqualityComparer&lt;T&gt;.Default</c>,
    /// which falls back to reference equality for concrete types like <see cref="List{T}"/> that
    /// don't override <c>Equals</c> — so two drafts with identical content (e.g. round-tripped
    /// through JSON, or even two separate <see cref="AssociateDraftPayload.CreateDefault"/> calls)
    /// never compare equal that way. Asserting each property directly instead uses xUnit's own
    /// deep/sequence-aware comparer, which is what actually proves the content round-tripped.
    /// </summary>
    private static void AssertDraftsEqual(AssociateDraftPayload expected, AssociateDraftPayload actual)
    {
        Assert.Equal(expected.ManualHoursOverride, actual.ManualHoursOverride);
        Assert.Equal(expected.PrivateBuyout, actual.PrivateBuyout);
        Assert.Equal(expected.ProjectedAttendance, actual.ProjectedAttendance);
        Assert.Equal(expected.PreSolds, actual.PreSolds);
        Assert.Equal(expected.EarlyRideTime.Details, actual.EarlyRideTime.Details);
        Assert.Equal(expected.EarlyRideTime.ParticipatingAttractions, actual.EarlyRideTime.ParticipatingAttractions);
        Assert.Equal(expected.OperationalNotes, actual.OperationalNotes);
        Assert.Equal(expected.DailyFunType, actual.DailyFunType);
        Assert.Equal(expected.DailyFunText, actual.DailyFunText);
        Assert.Equal(expected.ProductOverrides, actual.ProductOverrides);
        Assert.Equal(expected.CustomProducts, actual.CustomProducts);
    }

    // ===================== Draft behavior (1-5) =====================

    [Fact]
    public async Task LoadAssociateDateAsync_NoSavedDraft_ReturnsDefaultDraftAndNeverSavedStatus()
    {
        var env = CreateEnvironment();

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(Date, workspace.OperatingDate);
        AssertDraftsEqual(AssociateDraftPayload.CreateDefault(), workspace.Draft);
        Assert.Equal(AssociateSaveStatus.NeverSaved, workspace.SaveStatus);
    }

    [Fact]
    public async Task LoadAssociateDateAsync_DifferentOperatingDates_HaveIndependentDrafts()
    {
        var env = CreateEnvironment();
        var draftForDate = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 5000 };
        await env.Service.SaveDraftAsync(Date, draftForDate);

        var workspaceForDate = await env.Service.LoadAssociateDateAsync(Date);
        var workspaceForOtherDate = await env.Service.LoadAssociateDateAsync(OtherDate);

        Assert.Equal(5000, workspaceForDate.Draft.ProjectedAttendance);
        Assert.Null(workspaceForOtherDate.Draft.ProjectedAttendance);
        Assert.Equal(AssociateSaveState.NeverSaved, workspaceForOtherDate.SaveStatus.State);
    }

    [Fact]
    public async Task SaveDraftAsync_ThenLoad_RoundTripsAllScalarAndOverrideFields()
    {
        var env = CreateEnvironment();
        var draft = new AssociateDraftPayload(
            ManualHoursOverride: new ManualHoursOverride(true, new TimeOnly(11, 0), new TimeOnly(20, 0)),
            PrivateBuyout: PrivateBuyoutOverride.Disabled,
            ProjectedAttendance: 12345,
            PreSolds: 678,
            EarlyRideTime: new EarlyRideTimeDetails("Gates open early", ["Fury 325", "Intimidator"]),
            OperationalNotes: ["Note one", "Note two"],
            DailyFunType: DailyFunType.FactOfTheDay,
            DailyFunText: "Carowinds opened in 1973.",
            ProductOverrides: new Dictionary<string, ProductDraftOverride>
            {
                ["dated|DatedAdmissionTickets|PKG1"] = new ProductDraftOverride(false, 42.50m),
            },
            CustomProducts: [new CustomProductEntry("custom-1", "Group Rate", 10m, "Ask manager", true)]);

        await env.Service.SaveDraftAsync(Date, draft);
        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        AssertDraftsEqual(draft, workspace.Draft);
    }

    [Fact]
    public async Task SaveDraftAsync_UsesInjectedClockForSavedTimestamp()
    {
        var clock = new FakeClock(FixedNow);
        var env = CreateEnvironment(clock);

        var firstStatus = await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());
        Assert.Equal(FixedNow, firstStatus.LastSavedAt);

        clock.UtcNow = FixedNow.AddHours(3);
        var secondStatus = await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());
        Assert.Equal(FixedNow.AddHours(3), secondStatus.LastSavedAt);
    }

    [Fact]
    public async Task SaveDraftAsync_NeverWritesToOfficialDatedOrCatalogRepositories()
    {
        var env = CreateEnvironment();
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { ["catalog|SeasonPasses:name:x|y"] = new(true, 99m) },
            CustomProducts = [new CustomProductEntry("c1", "Custom", 5m, null, true)],
        };

        await env.Service.SaveDraftAsync(Date, draft);

        Assert.Empty(await env.DatedResults.GetByRequestedDateAsync(Date));
        Assert.Empty(await env.CatalogProducts.GetByCategoryAsync(SourceCategory.SeasonPasses));
    }

    // ===================== Hours (6-12) =====================

    [Fact]
    public async Task ComposeParkHours_VerifiedOfficialHours_UsedByDefault_WithDerivedGateTimes()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(EffectiveHoursStatus.Open, workspace.ParkHours.Effective.Status);
        Assert.Equal(new TimeOnly(10, 0), workspace.ParkHours.Effective.Opening);
        Assert.Equal(new TimeOnly(9, 30), workspace.ParkHours.Effective.NorthGateOpening);
        Assert.Equal(new TimeOnly(9, 15), workspace.ParkHours.Effective.AvenueOpening);
        Assert.False(workspace.ParkHours.Effective.IsManualOverrideApplied);
        Assert.Equal(AssociateVerificationState.Verified, workspace.ParkHours.Verification.State);
    }

    [Fact]
    public async Task ComposeParkHours_ManualOverrideEnabled_ChangesEffectiveOpeningAndGates()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(11, 0), new TimeOnly(20, 0)),
        };
        await env.Service.SaveDraftAsync(Date, draft);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(new TimeOnly(11, 0), workspace.ParkHours.Effective.Opening);
        Assert.Equal(new TimeOnly(10, 30), workspace.ParkHours.Effective.NorthGateOpening);
        Assert.Equal(new TimeOnly(10, 15), workspace.ParkHours.Effective.AvenueOpening);
        Assert.True(workspace.ParkHours.Effective.IsManualOverrideApplied);
    }

    [Fact]
    public async Task ComposeParkHours_PrivateBuyoutEnabled_SuppliesCustomHours()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(18, 0), new TimeOnly(23, 0), "Corporate event"),
        };
        await env.Service.SaveDraftAsync(Date, draft);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(EffectiveHoursStatus.Open, workspace.ParkHours.Effective.Status);
        Assert.Equal(new TimeOnly(18, 0), workspace.ParkHours.Effective.Opening);
        Assert.True(workspace.ParkHours.Effective.IsPrivateBuyoutApplied);
    }

    [Fact]
    public async Task ComposeParkHours_OfficialClosed_OnlyPrivateBuyoutReopensIt_ManualOverrideAloneDoesNot()
    {
        var env = CreateEnvironment();
        await SeedParkHoursClosedAsync(env.DatedResults, Date, FixedNow);
        var manualOnlyDraft = AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(11, 0), new TimeOnly(19, 0)),
        };
        await env.Service.SaveDraftAsync(Date, manualOnlyDraft);

        var stillClosed = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(EffectiveHoursStatus.Closed, stillClosed.ParkHours.Effective.Status);

        var buyoutDraft = manualOnlyDraft with
        {
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(18, 0), new TimeOnly(22, 0), "Buyout"),
        };
        await env.Service.SaveDraftAsync(Date, buyoutDraft);

        var reopened = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(EffectiveHoursStatus.Open, reopened.ParkHours.Effective.Status);
        Assert.Equal(new TimeOnly(18, 0), reopened.ParkHours.Effective.Opening);
    }

    [Fact]
    public async Task ComposeParkHours_StaleAfterFailedRecheck_StillReflectsLastKnownGoodOfficialClosedState()
    {
        var env = CreateEnvironment();
        await SeedParkHoursClosedAsync(env.DatedResults, Date, FixedNow.AddHours(-2));
        await SeedParkHoursFailedAsync(env.DatedResults, Date, FixedNow);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(AssociateVerificationState.Stale, workspace.ParkHours.Verification.State);
        Assert.Equal(EffectiveHoursStatus.Closed, workspace.ParkHours.Effective.Status);
    }

    [Fact]
    public async Task ComposeParkHours_NoDataAndNoOverride_ReportsNotCheckedYet()
    {
        var env = CreateEnvironment();

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(EffectiveHoursStatus.NotCheckedYet, workspace.ParkHours.Effective.Status);
        Assert.Equal(AssociateVerificationState.NotCheckedYet, workspace.ParkHours.Verification.State);
    }

    // ===================== Local date loading (13-16) =====================

    [Fact]
    public async Task LoadAssociateDateAsync_NeverInvokesRefreshOrchestrator()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow);

        await env.Service.LoadAssociateDateAsync(Date);
        await env.Service.LoadAssociateDateAsync(OtherDate);

        Assert.Empty(env.RefreshOrchestrator.ManualDateRefreshCalls);
        Assert.Equal(0, env.RefreshOrchestrator.ScheduledRefreshCallCount);
    }

    [Fact]
    public async Task LoadAssociateDateAsync_ComposesCachedDataScopedToRequestedDateOnly()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));
        await SeedAdmissionAsync(env.DatedResults, OtherDate, FixedNow,
            new DatedAdmissionProduct("PKG-B", "Other Date Ticket", 65m, null, null, OtherDate));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.Equal("Date Ticket", product.DisplayName);
        Assert.Equal(Date, product.ApplicableDate);
    }

    // ===================== Explicit online check (17-22) =====================

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_InvokesManualDateRefresh_ForOnlyTheRequestedDate()
    {
        var env = CreateEnvironment();

        await env.Service.CheckAssociateDateOnlineAsync(Date);

        var call = Assert.Single(env.RefreshOrchestrator.ManualDateRefreshCalls);
        Assert.Equal(Date, call);
        Assert.Equal(0, env.RefreshOrchestrator.ScheduledRefreshCallCount);
    }

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_AfterSuccessfulRefresh_RecomposesFromRefreshedOfficialData()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow);
        env.RefreshOrchestrator.OnManualDateRefresh = async (date, ct) =>
            await SeedParkHoursAsync(env.DatedResults, date, new TimeOnly(9, 0), new TimeOnly(22, 0), env.Clock.UtcNow);

        var workspace = await env.Service.CheckAssociateDateOnlineAsync(Date);

        Assert.Equal(new TimeOnly(9, 0), workspace.ParkHours.Effective.Opening);
    }

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_FailedRefresh_PreservesUsableCachedData()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));
        env.RefreshOrchestrator.OnManualDateRefresh = (_, _) => Task.CompletedTask; // simulates a refresh whose sources all errored, writing nothing new

        var workspace = await env.Service.CheckAssociateDateOnlineAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.Equal(60m, product.OfficialPriceAmount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckAssociateDateOnlineAsync_ManualDraftFieldsAndProductOverrides_SurviveRefreshRegardlessOfOutcome(bool refreshSucceeds)
    {
        var env = CreateEnvironment();
        var identity = AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "PKG-A");
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(11, 0), new TimeOnly(20, 0)),
            ProjectedAttendance = 999,
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [identity] = new(false, 55m) },
        };
        await env.Service.SaveDraftAsync(Date, draft);

        env.RefreshOrchestrator.OnManualDateRefresh = refreshSucceeds
            ? (date, ct) => SeedAdmissionAsync(env.DatedResults, date, env.Clock.UtcNow, new DatedAdmissionProduct("PKG-A", "Date Ticket", 65m, null, null, date))
            : (_, _) => Task.CompletedTask;

        var workspace = await env.Service.CheckAssociateDateOnlineAsync(Date);

        AssertDraftsEqual(draft, workspace.Draft);
        var product = Assert.Single(workspace.Products);
        Assert.False(product.IncludeOnPdf);
        Assert.Equal(55m, product.DisplayPriceOverride);
        Assert.Equal(55m, product.EffectiveDisplayPrice);
    }

    // ===================== Product draft state (23-30) =====================

    [Fact]
    public async Task ProductOverrides_IncludeExcludeAndDisplayPriceOverride_SurviveRefresh()
    {
        var env = CreateEnvironment();
        var identity = AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "PKG-A");
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [identity] = new(false, 45m) },
        });

        env.RefreshOrchestrator.OnManualDateRefresh = (date, ct) =>
            SeedAdmissionAsync(env.DatedResults, date, env.Clock.UtcNow, new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, date));
        var workspace = await env.Service.CheckAssociateDateOnlineAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.False(product.IncludeOnPdf);
        Assert.Equal(45m, product.DisplayPriceOverride);
    }

    [Fact]
    public async Task ProductOverrides_RestoringDefault_ExposesOfficialPriceAgain()
    {
        var env = CreateEnvironment();
        var identity = AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "PKG-A");
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [identity] = new(true, 45m) },
        });

        var overridden = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(45m, Assert.Single(overridden.Products).EffectiveDisplayPrice);

        // Restoring the official value means going back to ProductDraftOverride.Default (no override entry).
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());
        var restored = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(restored.Products);
        Assert.Null(product.DisplayPriceOverride);
        Assert.Equal(60m, product.EffectiveDisplayPrice);
    }

    [Fact]
    public async Task ProductOverrides_StayAssociatedWithCorrectIdentity_AcrossDatedAndCatalogPrefixes()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("shared-key", "Dated Product", 10m, null, null, Date));
        var catalogIdentity = SeedCatalogProduct(env.CatalogProducts, SourceCategory.SeasonPasses, "shared-key", 200m, FixedNow);
        var datedIdentity = AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "shared-key");

        Assert.NotEqual(datedIdentity, catalogIdentity); // same raw suffix, different family prefixes never collide.

        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride>
            {
                [datedIdentity] = new(false, 11m),
                [catalogIdentity] = new(true, 222m),
            },
        });

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var datedProduct = Assert.Single(workspace.Products, p => p.ProductIdentity == datedIdentity);
        var catalogProduct = Assert.Single(workspace.Products, p => p.ProductIdentity == catalogIdentity);
        Assert.False(datedProduct.IncludeOnPdf);
        Assert.Equal(11m, datedProduct.DisplayPriceOverride);
        Assert.True(catalogProduct.IncludeOnPdf);
        Assert.Equal(222m, catalogProduct.DisplayPriceOverride);
    }

    [Fact]
    public async Task NewOfficialProducts_AppearDynamically_WithoutAnyHardCodedProductName()
    {
        var env = CreateEnvironment();
        await SeedHauntedAsync(env.DatedResults, Date, FixedNow,
            new DatedHauntedProduct("hap-1", "Totally Unexpected Brand New Attraction Pass", 30m, null, null, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.Equal("Totally Unexpected Brand New Attraction Pass", product.DisplayName);
        Assert.Equal(SourceCategory.HauntedAttractions, product.Category);
    }

    [Fact]
    public async Task RemovedDatedProduct_SimplyAbsentFromLatestList_NotFabricated()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Ticket A", 60m, null, null, Date),
            new DatedAdmissionProduct("PKG-B", "Ticket B", 70m, null, null, Date));
        var firstLoad = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(2, firstLoad.Products.Count);

        // A later successful retrieval that no longer contains PKG-B (removed by the official source).
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow.AddHours(1),
            new DatedAdmissionProduct("PKG-A", "Ticket A", 60m, null, null, Date));

        var secondLoad = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(secondLoad.Products);
        Assert.Equal("Ticket A", product.DisplayName);
    }

    [Fact]
    public async Task ArchivedCatalogProducts_AreExcludedFromDefaultProjection()
    {
        var env = CreateEnvironment();
        SeedCatalogProduct(env.CatalogProducts, SourceCategory.SeasonPasses, "Gone Pass", 100m, FixedNow, lifecycle: ProductLifecycleState.Archived);
        SeedCatalogProduct(env.CatalogProducts, SourceCategory.SeasonPasses, "Active Pass", 100m, FixedNow, lifecycle: ProductLifecycleState.Active);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.Equal("Active Pass", product.DisplayName);
    }

    [Fact]
    public async Task CustomProducts_AreDraftLocal_NeverWrittenToOfficialCache_AndSurviveSaveLoadRoundTrip()
    {
        var env = CreateEnvironment();
        var custom = new CustomProductEntry("group-rate", "Group Rate Special", 15m, "Ask a manager", true);
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with { CustomProducts = [custom] });

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(workspace.Products, p => p.IsCustom);
        Assert.Equal("Group Rate Special", product.DisplayName);
        Assert.Equal(15m, product.EffectiveDisplayPrice);
        Assert.Null(product.Category);
        Assert.Equal(custom, Assert.Single(workspace.Draft.CustomProducts));
        Assert.Empty(await env.CatalogProducts.GetByCategoryAsync(SourceCategory.SeasonPasses));
        Assert.Empty(await env.CatalogProducts.GetByCategoryAsync(SourceCategory.AddOns));
    }

    // ===================== Product projection (31-37) =====================

    [Fact]
    public async Task ProductProjection_OfficialCurrentPrice_ProjectedForBothDatedAndCatalogProducts()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Dated Ticket", 60m, null, null, Date));
        SeedCatalogProduct(env.CatalogProducts, SourceCategory.SeasonPasses, "Gold Pass", 189.99m, FixedNow);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(60m, workspace.Products.Single(p => p.DisplayName == "Dated Ticket").OfficialPriceAmount);
        Assert.Equal(189.99m, workspace.Products.Single(p => p.DisplayName == "Gold Pass").OfficialPriceAmount);
    }

    [Fact]
    public async Task ProductProjection_ComparisonPriceAndQualifier_OnlyAppearWhenPresentInSourceData()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "No Extras", 60m, null, null, Date),
            new DatedAdmissionProduct("PKG-B", "With Extras", 70m, 90m, "Online Price", Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var plain = workspace.Products.Single(p => p.DisplayName == "No Extras");
        Assert.Null(plain.ComparisonPriceAmount);
        Assert.Null(plain.PriceQualifier);

        var withExtras = workspace.Products.Single(p => p.DisplayName == "With Extras");
        Assert.Equal(90m, withExtras.ComparisonPriceAmount);
        Assert.Equal("Online Price", withExtras.PriceQualifier);
    }

    [Fact]
    public async Task ProductProjection_StaleState_ProjectedWhenLatestRecheckFailedAfterAPriorSuccess()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow.AddHours(-5),
            new DatedAdmissionProduct("PKG-A", "Ticket", 60m, null, null, Date));
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date),
            Evidence(RetrievalStatus.Error, FixedNow, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.Equal(AssociateVerificationState.Stale, product.Verification.State);
        Assert.Equal(TimeSpan.FromHours(5), product.Verification.StaleAge);
    }

    [Fact]
    public async Task ProductProjection_LastVerifiedTimestamp_MatchesSuccessfulEvidenceTimeChecked()
    {
        var env = CreateEnvironment();
        var checkedAt = FixedNow.AddHours(-1);
        await SeedAdmissionAsync(env.DatedResults, Date, checkedAt,
            new DatedAdmissionProduct("PKG-A", "Ticket", 60m, null, null, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(checkedAt, Assert.Single(workspace.Products).Verification.LastVerifiedAt);
    }

    [Theory]
    [InlineData(RetrievalStatus.NotPublished)]
    [InlineData(RetrievalStatus.Unavailable)]
    public async Task ProductProjection_NotPublishedOrUnavailable_YieldsNoProductsWithoutFabricatingData(RetrievalStatus status)
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date),
            Evidence(status, FixedNow, Date),
            payloadJson: null);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Empty(workspace.Products);
    }

    [Fact]
    public async Task ProductProjection_ErrorStatus_NeverTreatedAsProductAbsence_PriorSuccessStillProjected()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow.AddHours(-1),
            new DatedAdmissionProduct("PKG-A", "Ticket", 60m, null, null, Date));
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date),
            Evidence(RetrievalStatus.Error, FixedNow, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Single(workspace.Products); // the Error attempt did not erase the prior verified product.
    }

    [Fact]
    public async Task SeasonalCatalogSections_OnlyAppearWhenSourceDataExists_NotHardCodedByCategory()
    {
        var env = CreateEnvironment();

        var beforeAnySeasonalData = await env.Service.LoadAssociateDateAsync(Date);
        Assert.DoesNotContain(beforeAnySeasonalData.Products, p => p.Category == SourceCategory.SeasonPasses);
        Assert.DoesNotContain(beforeAnySeasonalData.Products, p => p.Category == SourceCategory.HauntedAttractions);

        SeedCatalogProduct(env.CatalogProducts, SourceCategory.SeasonPasses, "Gold Pass", 189.99m, FixedNow);
        await SeedHauntedAsync(env.DatedResults, Date, FixedNow, new DatedHauntedProduct("hap-1", "Haunted Pass", 30m, null, null, Date));

        var afterSeasonalDataArrives = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Single(afterSeasonalDataArrives.Products, p => p.Category == SourceCategory.SeasonPasses);
        Assert.Single(afterSeasonalDataArrives.Products, p => p.Category == SourceCategory.HauntedAttractions);
    }

    // ===================== Save status (38-40) =====================

    [Fact]
    public async Task SaveStatus_TransitionsFromNeverSavedToSaved_WithLastSavedAtFromInjectedClock()
    {
        var clock = new FakeClock(FixedNow);
        var env = CreateEnvironment(clock);

        var beforeSave = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(AssociateSaveState.NeverSaved, beforeSave.SaveStatus.State);
        Assert.Null(beforeSave.SaveStatus.LastSavedAt);

        var saveResult = await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());
        Assert.Equal(AssociateSaveState.Saved, saveResult.State);
        Assert.Equal(FixedNow, saveResult.LastSavedAt);
    }

    [Fact]
    public async Task SaveStatus_LoadingExistingDraft_ReportsThePersistedTimestamp_NotTheCurrentClockTime()
    {
        var clock = new FakeClock(FixedNow);
        var env = CreateEnvironment(clock);
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());

        clock.UtcNow = FixedNow.AddDays(1); // time has moved on since the save.
        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(AssociateSaveState.Saved, workspace.SaveStatus.State);
        Assert.Equal(FixedNow, workspace.SaveStatus.LastSavedAt); // reflects the persisted draft's own UpdatedAt, not "now".
    }

    // ===================== Part C: JSON round-trip hardening =====================

    [Fact]
    public async Task JsonRoundTrip_RealisticDraftWithCollectionsAndDictionaries_SerializesPersistsDeserializesAndReconstructsWorkspace()
    {
        var env = CreateEnvironment();
        var draft = new AssociateDraftPayload(
            ManualHoursOverride: new ManualHoursOverride(true, new TimeOnly(9, 30), new TimeOnly(22, 0)),
            PrivateBuyout: new PrivateBuyoutOverride(false, null, null, null),
            ProjectedAttendance: 18000,
            PreSolds: 4200,
            EarlyRideTime: new EarlyRideTimeDetails("7:30-8:30 AM, wristband required", ["Fury 325", "Copperhead Strike", "Intimidator"]),
            OperationalNotes: ["Rain expected after 3 PM", "VIP tour group at 10 AM", "Short-staffed at North Gate"],
            DailyFunType: DailyFunType.QuestionOfTheDay,
            DailyFunText: "What year did Carowinds open?",
            ProductOverrides: new Dictionary<string, ProductDraftOverride>
            {
                [AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "PKG-A")] = new(true, null),
                [AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedFastLane, "FL-1")] = new(false, 89.99m),
                [AssociateProductIdentity.ForCatalogProduct("SeasonPasses:name:fake source|gold pass")] = new(true, 199.99m),
            },
            CustomProducts:
            [
                new CustomProductEntry("group-1", "School Group Rate", 25m, "Contact groups desk", true),
                new CustomProductEntry("promo-1", "Employee Family Day", null, null, false),
            ]);

        await env.Service.SaveDraftAsync(Date, draft);
        var persisted = await env.Drafts.GetAsync(Date);
        Assert.NotNull(persisted);

        // Round trip exactly as the service does: JSON text -> deserialized payload -> composed workspace.
        var roundTripped = JsonSerializer.Deserialize<AssociateDraftPayload>(persisted!.PayloadJson);
        Assert.NotNull(roundTripped);
        AssertDraftsEqual(draft, roundTripped);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);
        AssertDraftsEqual(draft, workspace.Draft);
        Assert.Equal(2, workspace.Products.Count(p => p.IsCustom));
        Assert.Equal(3, workspace.Draft.ProductOverrides.Count);
        Assert.Equal(3, workspace.Draft.EarlyRideTime.ParticipatingAttractions.Count);
        Assert.Equal(3, workspace.Draft.OperationalNotes.Count);
    }
}
