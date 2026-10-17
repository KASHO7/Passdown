using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// Phase 19B: seasonal catalog and product lifecycle, end to end through the real
/// <see cref="ProductReconciliationService"/>, <see cref="CatalogSourceRegistration"/>,
/// <see cref="AssociatePassdownService"/> composition and (for freshness) the real
/// <see cref="RefreshOrchestrator"/> — no hard-coded dates or product names decide availability; only
/// what each simulated successful official check returned.
/// </summary>
public class SeasonalCatalogLifecycleTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly DateTimeOffset Run1 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private const string DuoName = "Celebrate America! Bring-A-Friend Duo";

    private static DateTimeOffset Run(int n) => Run1.AddHours(4 * (n - 1));

    private static ObservedProduct Promo(string name, decimal price = 50m) => new(
        CatalogProductKey.FromSourceAndName(SourceCategory.Promotions, "promotions", name),
        name, price, $"${price}", null, null, DateSpecific: false, ApplicableDate: null);

    /// <summary>One Promotions catalog source whose published list and failure mode each test controls, wired the way the app wires it.</summary>
    private sealed class Harness
    {
        public FakeClock Clock { get; } = new(Run1);
        public InMemoryCatalogProductRepository Catalog { get; } = new();
        public InMemoryProductHistoryRepository History { get; } = new();
        public InMemoryDatedResultRepository DatedResults { get; } = new();
        public InMemoryAssociateDraftRepository Drafts { get; } = new();
        public List<ObservedProduct> Published { get; set; } = [];
        public bool FailNextCheck { get; set; }
        public CatalogSourceRegistration Promotions { get; }
        public AssociatePassdownService Associate { get; }

        public Harness()
        {
            var reconciliation = new ProductReconciliationService(Catalog, History, new FakeUnitOfWorkFactory());
            Promotions = new CatalogSourceRegistration(
                new FakeOfficialDataSource<CatalogPayload>("promotions", SourceCategory.Promotions, (_, _) => Task.FromResult(FailNextCheck
                    ? SourceResult<CatalogPayload>.WithoutPayload("promotions", FakeEvidence.Error(Clock.UtcNow))
                    : SourceResult<CatalogPayload>.Success("promotions", FakeEvidence.Verified(Clock.UtcNow), new CatalogPayload(Published.ToList())))),
                reconciliation);
            Associate = new AssociatePassdownService(
                Drafts, DatedResults, Catalog, new FakeRefreshOrchestrator(),
                new OperationalNotesService(new InMemoryOperationalNoteRepository(), Clock), Clock);
        }

        public async Task<SourceOperationResult> CheckAsync(int run, params ObservedProduct[] published)
        {
            Clock.UtcNow = Run(run);
            Published = published.ToList();
            return await Promotions.ExecuteAsync(requestedDate: null, CancellationToken.None);
        }

        public async Task<IReadOnlyList<string>> CurrentProductNamesAsync(DateOnly? date = null) =>
            (await Associate.LoadAssociateDateAsync(date ?? Today)).Products.Select(p => p.DisplayName).ToList();

        public async Task<CatalogProductRecord?> RecordAsync(ObservedProduct product) => await Catalog.GetAsync(product.Key);
    }

    // §35 — a limited promotion ends: successful absences retire it, never an Error.
    [Fact]
    public async Task LimitedPromotionEnds_SuccessfulAbsencesRetireIt_WithoutAnyError()
    {
        var h = new Harness();
        var regular = Promo("Bring-A-Friend Ticket");
        var promo = Promo("Limited Weekend Offer");

        var first = await h.CheckAsync(1, regular, promo);
        var missingOnce = await h.CheckAsync(2, regular);

        Assert.Equal(SourceOperationOutcomeKind.Success, first.Outcome);
        Assert.Equal(SourceOperationOutcomeKind.Success, missingOnce.Outcome);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, (await h.RecordAsync(promo))!.LifecycleState);
        Assert.Contains("Limited Weekend Offer", await h.CurrentProductNamesAsync()); // one miss never retires

        var missingTwice = await h.CheckAsync(3, regular);

        Assert.Equal(SourceOperationOutcomeKind.Success, missingTwice.Outcome);
        Assert.Equal(RetrievalStatus.Verified, missingTwice.Status);
        var record = await h.RecordAsync(promo);
        Assert.NotNull(record); // identity retained — never deleted
        Assert.Equal(ProductLifecycleState.Archived, record!.LifecycleState);
        Assert.Equal(CatalogProductLifecycle.ConfirmedAbsenceThreshold, record.ConsecutiveConfirmedAbsenceCount);
        Assert.DoesNotContain("Limited Weekend Offer", await h.CurrentProductNamesAsync());
        Assert.Contains("Bring-A-Friend Ticket", await h.CurrentProductNamesAsync());

        var history = await h.History.GetHistoryAsync(promo.Key.ToString());
        Assert.Equal(3, history.Count); // added, pending, retired — all retained
    }

    // §36 — a retrieval error never counts as absence.
    [Fact]
    public async Task RetrievalError_NeverAdvancesAbsence_AndKeepsTheProductCurrent()
    {
        var h = new Harness();
        var promo = Promo("Limited Weekend Offer");
        await h.CheckAsync(1, promo);

        h.FailNextCheck = true;
        var failed1 = await h.CheckAsync(2);
        var failed2 = await h.CheckAsync(3);

        Assert.Equal(SourceOperationOutcomeKind.Failure, failed1.Outcome);
        Assert.Equal(RetrievalStatus.Error, failed2.Status);
        var record = await h.RecordAsync(promo);
        Assert.Equal(ProductLifecycleState.Active, record!.LifecycleState);
        Assert.Equal(0, record.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(Run(1), record.LastVerifiedAt); // last-known-good untouched
        Assert.Contains("Limited Weekend Offer", await h.CurrentProductNamesAsync());
    }

    // §37 — reappearance reactivates the same identity, no duplicate row.
    [Fact]
    public async Task RetiredProductReappears_ReactivatesTheSameIdentity_WithoutADuplicateRow()
    {
        var h = new Harness();
        var promo = Promo("Limited Weekend Offer");
        await h.CheckAsync(1, promo);
        await h.CheckAsync(2);
        await h.CheckAsync(3);
        Assert.Equal(ProductLifecycleState.Archived, (await h.RecordAsync(promo))!.LifecycleState);

        await h.CheckAsync(4, Promo("Limited Weekend Offer", price: 45m));

        var rows = await h.Catalog.GetByCategoryAsync(SourceCategory.Promotions);
        var row = Assert.Single(rows);
        Assert.Equal(promo.Key.ToString(), row.ProductKey);
        Assert.Equal(ProductLifecycleState.Active, row.LifecycleState);
        Assert.Equal(Run(1), row.FirstSeenAt); // same product, not a new one
        Assert.Contains("Limited Weekend Offer", await h.CurrentProductNamesAsync());
    }

    // §14 — Bring-A-Friend Duo goes through the same generic lifecycle as any promotion.
    [Fact]
    public async Task BringAFriendDuo_EndsAndReturns_ThroughTheGenericLifecycle_AndKeepsItsHistoricalDatedValue()
    {
        var h = new Harness();
        var duo = Promo(DuoName, 25m);
        var pastDate = new DateOnly(2026, 8, 29);

        // A dated Duo value captured while the offer ran (historical dated result, retained as-is).
        var duoDated = new DatedSourceRegistration<DatedBringAFriendDuoPayload>(
            new FakeOfficialDataSource<DatedBringAFriendDuoPayload>("dated-bring-a-friend-duo", SourceCategory.BringAFriendDuo, (request, _) =>
                Task.FromResult(SourceResult<DatedBringAFriendDuoPayload>.Success(
                    "dated-bring-a-friend-duo",
                    FakeEvidence.Verified(h.Clock.UtcNow, request.RequestedDate),
                    new DatedBringAFriendDuoPayload([new DatedBringAFriendDuoProduct("10097207", DuoName + " Offer", 25m, null, null, request.RequestedDate!.Value)])))),
            h.DatedResults);
        await duoDated.ExecuteAsync(pastDate, CancellationToken.None);

        var published = await h.CheckAsync(1, Promo("Bring-A-Friend Ticket"), duo);
        Assert.Equal(SourceOperationOutcomeKind.Success, published.Outcome);
        Assert.Contains(DuoName, await h.CurrentProductNamesAsync());

        var gone1 = await h.CheckAsync(2, Promo("Bring-A-Friend Ticket"));
        var gone2 = await h.CheckAsync(3, Promo("Bring-A-Friend Ticket"));

        Assert.Equal(SourceOperationOutcomeKind.Success, gone1.Outcome);
        Assert.Equal(SourceOperationOutcomeKind.Success, gone2.Outcome);
        Assert.Equal(ProductLifecycleState.Archived, (await h.RecordAsync(duo))!.LifecycleState);
        Assert.DoesNotContain(DuoName, await h.CurrentProductNamesAsync());

        // The past date's dated Duo value is untouched by the catalog retirement.
        var pastWorkspace = await h.Associate.LoadAssociateDateAsync(pastDate);
        Assert.Contains(pastWorkspace.Products, p => p.Category == SourceCategory.BringAFriendDuo && p.OfficialPriceAmount == 25m);

        await h.CheckAsync(4, Promo("Bring-A-Friend Ticket"), duo);
        Assert.Equal(ProductLifecycleState.Active, (await h.RecordAsync(duo))!.LifecycleState);
        Assert.Contains(DuoName, await h.CurrentProductNamesAsync());
    }

    // §38 — season tags are non-exclusive and never hide a currently published product.
    [Fact]
    public async Task OverlappingSeasonProducts_AreAllCurrent_WhilePublished()
    {
        var h = new Harness();
        await h.CheckAsync(1,
            Promo("Bring-A-Friend Ticket"),
            Promo("Park Ticket + SCarowinds Bundle"),
            Promo("SCarowinds Bundles"),
            Promo("WinterFest Ticket"),
            Promo("SCarowinds + WinterFest Combo"));

        var products = (await h.Associate.LoadAssociateDateAsync(Today)).Products;

        Assert.Equal(5, products.Count);
        Assert.Equal(ProductSeasonTags.None, products.Single(p => p.DisplayName == "Bring-A-Friend Ticket").SeasonTags);
        Assert.Equal(ProductSeasonTags.SCarowinds, products.Single(p => p.DisplayName == "SCarowinds Bundles").SeasonTags);
        Assert.Equal(ProductSeasonTags.WinterFest, products.Single(p => p.DisplayName == "WinterFest Ticket").SeasonTags);
        Assert.Equal(ProductSeasonTags.SCarowinds | ProductSeasonTags.WinterFest, products.Single(p => p.DisplayName == "SCarowinds + WinterFest Combo").SeasonTags);
    }

    // §39 — SCarowinds → WinterFest transition with no refresh error.
    [Fact]
    public async Task ScarowindsToWinterFest_RetiresScarowindsProducts_KeepsRegular_AddsWinterFest_AllSuccessful()
    {
        var h = new Harness();
        var regular = Promo("Bring-A-Friend Ticket");
        var scarowinds = Promo("SCarowinds Bundles");
        var winterFest = Promo("WinterFest Ticket + Hot Cocoa");

        var outcomes = new List<SourceOperationResult>
        {
            await h.CheckAsync(1, regular, scarowinds),
            await h.CheckAsync(2, regular, winterFest),
            await h.CheckAsync(3, regular, winterFest),
        };

        Assert.All(outcomes, o => Assert.Equal(SourceOperationOutcomeKind.Success, o.Outcome));
        Assert.Equal(ProductLifecycleState.Archived, (await h.RecordAsync(scarowinds))!.LifecycleState);
        Assert.Equal(ProductLifecycleState.Active, (await h.RecordAsync(regular))!.LifecycleState);
        Assert.Equal(ProductLifecycleState.Active, (await h.RecordAsync(winterFest))!.LifecycleState);

        var products = (await h.Associate.LoadAssociateDateAsync(Today)).Products;
        Assert.DoesNotContain(products, p => p.SeasonTags.HasFlag(ProductSeasonTags.SCarowinds));
        Assert.Contains(products, p => p.SeasonTags == ProductSeasonTags.WinterFest);
        Assert.Contains(products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.DoesNotContain(
            ProductDisplaySectionResolver.ResolveAll(products), entry => entry.Section == ProductDisplaySection.SCarowinds);
    }

    // §40/§19 — a date-specific "not published" never retires a globally active catalog product.
    [Fact]
    public async Task DateSpecificNotPublished_DoesNotRetireTheGlobalCatalogProduct_OnlyCatalogAbsenceDoes()
    {
        var h = new Harness();
        var duo = Promo(DuoName, 25m);
        await h.CheckAsync(1, duo);

        var futureDate = Today.AddDays(30);
        var duoDated = new DatedSourceRegistration<DatedBringAFriendDuoPayload>(
            new FakeOfficialDataSource<DatedBringAFriendDuoPayload>("dated-bring-a-friend-duo", SourceCategory.BringAFriendDuo, (request, _) =>
                Task.FromResult(SourceResult<DatedBringAFriendDuoPayload>.WithoutPayload(
                    "dated-bring-a-friend-duo",
                    new SourceEvidence("Fake Source", new Uri("https://example.test/fake"), h.Clock.UtcNow, RetrievalStatus.NotPublished,
                        "Not published.", RetrievalStrategy.DirectApi, request.RequestedDate)))),
            h.DatedResults);

        var dated = await duoDated.ExecuteAsync(futureDate, CancellationToken.None);

        Assert.Equal(SourceOperationOutcomeKind.Success, dated.Outcome);
        var futureWorkspace = await h.Associate.LoadAssociateDateAsync(futureDate);
        Assert.Equal(AssociateVerificationState.NotPublished, futureWorkspace.CategoryStatuses.Single(s => s.Category == SourceCategory.BringAFriendDuo).State);
        Assert.Equal(ProductLifecycleState.Active, (await h.RecordAsync(duo))!.LifecycleState);
        Assert.Contains(DuoName, futureWorkspace.Products.Select(p => p.DisplayName));

        await h.CheckAsync(2);
        await h.CheckAsync(3);

        Assert.Equal(ProductLifecycleState.Archived, (await h.RecordAsync(duo))!.LifecycleState);
    }

    // §21 — a saved draft that selected a now-retired product loads safely and is not rewritten.
    [Fact]
    public async Task DraftReferencingARetiredProduct_LoadsSafely_AndTheStoredDraftIsNotRewritten()
    {
        var h = new Harness();
        var promo = Promo("Limited Weekend Offer");
        await h.CheckAsync(1, promo);
        var identity = AssociateProductIdentity.ForCatalogProduct(promo.Key.ToString());
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [identity] = new(IncludeOnPdf: true, DisplayPriceOverride: 40m) },
        };
        await h.Associate.SaveDraftAsync(Today, draft);
        var storedBefore = (await h.Drafts.GetAsync(Today))!.PayloadJson;

        await h.CheckAsync(2);
        await h.CheckAsync(3);
        var workspace = await h.Associate.LoadAssociateDateAsync(Today);

        Assert.DoesNotContain(workspace.Products, p => p.ProductIdentity == identity);
        Assert.Equal(storedBefore, (await h.Drafts.GetAsync(Today))!.PayloadJson);
        Assert.True(workspace.Draft.ProductOverrides.ContainsKey(identity)); // preserved until the user next edits
    }

    // §41 — finalized History is frozen; retiring a product later never rewrites it.
    [Fact]
    public async Task FinalizedPassdown_KeepsARetiredProduct_AndItsFrozenPdfBytes()
    {
        var h = new Harness();
        var promo = Promo("Limited Weekend Offer");
        await h.CheckAsync(1, promo);

        var finalizedRepository = new InMemoryFinalizedPassdownRepository();
        var finalization = new FinalizedPassdownFinalizationService(finalizedRepository, new ProductFreezingSnapshotBuilder());
        var workspaceThen = await h.Associate.LoadAssociateDateAsync(Today);
        await finalization.FinalizeAsync(Today, workspaceThen, FinalizationOrigin.Manual, Run(1));
        var frozenBefore = (await finalizedRepository.GetByDateAsync(Today))!;

        await h.CheckAsync(2);
        await h.CheckAsync(3);
        var again = await finalization.FinalizeAsync(Today, await h.Associate.LoadAssociateDateAsync(Today), FinalizationOrigin.Scheduled, Run(3));

        var frozenAfter = (await finalizedRepository.GetByDateAsync(Today))!;
        Assert.Equal(FinalizationOutcome.AlreadyFinalized, again.Outcome);
        Assert.Equal(frozenBefore.SnapshotJson, frozenAfter.SnapshotJson);
        Assert.Equal(frozenBefore.PdfBytes, frozenAfter.PdfBytes);
        var snapshot = FinalizedPassdownSnapshotSerializer.Deserialize(frozenAfter.SnapshotJson);
        Assert.Contains(snapshot.ProductCategories.SelectMany(c => c.Products), p => p.DisplayName == "Limited Weekend Offer");
        Assert.DoesNotContain("Limited Weekend Offer", await h.CurrentProductNamesAsync());
    }

    // §44/§11 — a normal disappearance keeps the comprehensive refresh fully successful; a real error does not.
    [Fact]
    public async Task ComprehensiveRefresh_ProductDisappearsNormally_StaysChecked_ButARealErrorIsPartial()
    {
        var h = new Harness();
        var sourceStates = new InMemorySourceStateRepository();
        var parkHours = new DatedSourceRegistration<FakePayload>(
            new FakeOfficialDataSource<FakePayload>("park-hours", SourceCategory.ParkHours, (request, _) =>
                Task.FromResult(SourceResult<FakePayload>.Success("park-hours", FakeEvidence.Verified(h.Clock.UtcNow, request.RequestedDate), new FakePayload("ok")))),
            h.DatedResults);
        var registrations = new List<SourceRegistration> { parkHours, h.Promotions };
        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var orchestrator = new RefreshOrchestrator(
            registrations, new FreshnessPlanner(sourceStates, h.Clock, policies), new RollingDatePlanner(h.Clock, new RollingWindowOptions()),
            sourceStates, policies, new FakeJobBrowserSessionFactory(), h.Clock, new RefreshOrchestratorOptions());

        h.Published = [Promo("Bring-A-Friend Ticket"), Promo(DuoName)];
        await orchestrator.RunComprehensiveRefreshAsync();
        h.Clock.UtcNow = Run(2);
        h.Published = [Promo("Bring-A-Friend Ticket")]; // Duo ended — successful check, product absent
        await orchestrator.RunComprehensiveRefreshAsync();

        var afterDisappearance = await OfficialDataStatus.ReadAsync(registrations, sourceStates);
        Assert.Equal(OfficialDataCheckState.Checked, afterDisappearance.State);
        Assert.Empty(afterDisappearance.FailedSources);

        h.Clock.UtcNow = Run(3);
        h.FailNextCheck = true;
        await orchestrator.RunComprehensiveRefreshAsync();

        var afterError = await OfficialDataStatus.ReadAsync(registrations, sourceStates);
        Assert.Equal(OfficialDataCheckState.PartiallyFailed, afterError.State);
        Assert.Equal("promotions", Assert.Single(afterError.FailedSources).SourceId);
    }

    // §26 — reconciliation reports what appeared, disappeared and changed season (no persistence needed).
    [Fact]
    public async Task Reconciliation_ReportsSeasonalTransitionMetadata()
    {
        var h = new Harness();
        var reconciliation = new ProductReconciliationService(h.Catalog, h.History, new FakeUnitOfWorkFactory());
        var bundleKey = CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "bundle-1");
        ObservedProduct Bundle(string name) => new(bundleKey, name, 60m, "$60", null, null, false, null);

        var first = await reconciliation.ReconcileAsync(SourceCategory.Promotions, FakeEvidence.Verified(Run(1)), [Bundle("SCarowinds Bundles"), Promo("Bring-A-Friend Ticket")]);
        var renamed = await reconciliation.ReconcileAsync(SourceCategory.Promotions, FakeEvidence.Verified(Run(2)), [Bundle("WinterFest Bundles")]);

        var added = first.Products.Single(p => p.ProductKey == bundleKey.ToString());
        Assert.Equal(ReconciliationOutcome.Added, added.Outcome);
        Assert.Equal(ProductSeasonTags.SCarowinds, added.SeasonTags);

        var changed = renamed.Products.Single(p => p.ProductKey == bundleKey.ToString());
        Assert.True(changed.SeasonTagsChanged);
        Assert.Equal(ProductSeasonTags.SCarowinds, changed.PreviousSeasonTags);
        Assert.Equal(ProductSeasonTags.WinterFest, changed.SeasonTags);

        var missing = renamed.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(ReconciliationOutcome.PendingRemoval, missing.Outcome);
        Assert.False(missing.SeasonTagsChanged);
    }

    /// <summary>Freezes product names into the snapshot (the shared fake builder freezes none) so History immutability is observable.</summary>
    private sealed class ProductFreezingSnapshotBuilder : IFinalizedPassdownSnapshotBuilder
    {
        public FinalizedPassdownSnapshot Build(AssociateWorkspace workspace, DateTimeOffset asOf) => BuildProjection(workspace, asOf).Snapshot;

        public FinalizedPassdownProjection BuildProjection(AssociateWorkspace workspace, DateTimeOffset asOf, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic)
        {
            var rows = workspace.Products.Select(p => new FinalizedProductRowSnapshot(p.DisplayName, p.OfficialDisplayPrice, null, null, null)).ToList();
            var snapshot = new FinalizedPassdownSnapshot(
                FinalizedPassdownSnapshot.CurrentSchemaVersion,
                workspace.OperatingDate,
                workspace.OperatingDate.ToString("yyyy-MM-dd"),
                new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.NotCheckedYet, "Hours not checked yet", null, null, null, null, null),
                new FinalizedAttendanceSnapshot(null, null),
                new FinalizedEarlyRideTimeSnapshot(false, null, []),
                new FinalizedDailyFeatureSnapshot(null, null),
                [],
                [new FinalizedProductCategorySnapshot("Promotions", FinalizedProductSectionTier.Promotional, rows)],
                HasUnverifiedProductPricing: false,
                GeneratedAtText: asOf.ToString("O"));
            return new FinalizedPassdownProjection(snapshot, System.Text.Encoding.UTF8.GetBytes($"PDF:{string.Join('|', rows.Select(r => r.DisplayName))}"));
        }
    }
}
