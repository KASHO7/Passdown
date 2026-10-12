using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
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
        IOperationalNotesService OperationalNotes,
        FakeClock Clock);

    private static TestEnvironment CreateEnvironment(
        FakeClock? clock = null,
        IOperationalNotesService? operationalNotes = null,
        Func<IScheduledRefreshLock>? refreshLockFactory = null,
        ISyncChangeTracker? syncChangeTracker = null)
    {
        clock ??= new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        operationalNotes ??= new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var service = new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, operationalNotes, clock, refreshLockFactory, syncChangeTracker: syncChangeTracker);
        return new TestEnvironment(service, drafts, datedResults, catalogProducts, refreshOrchestrator, operationalNotes, clock);
    }

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset timeChecked, DateOnly? requestedDate = null) =>
        new("Fake Source", SourceUrl, timeChecked, status, $"{status}.", RetrievalStrategy.DirectApi,
            requestedDate, confirmedDate: status == RetrievalStatus.Verified ? requestedDate : null);

    private static Task SeedParkHoursAsync(
        InMemoryDatedResultRepository repo, DateOnly date, TimeOnly opening, TimeOnly closing, DateTimeOffset timeChecked,
        TimeOnly? waterparkOpening = null, TimeOnly? waterparkClosing = null) =>
        repo.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, date),
            Evidence(RetrievalStatus.Verified, timeChecked, date),
            JsonSerializer.Serialize(new ParkHoursPayload(opening, closing, waterparkOpening, waterparkClosing)));

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
        Assert.Equal(expected.OperationalNotes, actual.OperationalNotes);
        Assert.Equal(expected.DailyFeatureType, actual.DailyFeatureType);
        Assert.Equal(expected.DailyFeatureText, actual.DailyFeatureText);
        Assert.Equal(expected.ProductOverrides, actual.ProductOverrides);
        Assert.Equal(expected.CustomProducts, actual.CustomProducts);
        Assert.Equal(expected.SectionSelection, actual.SectionSelection);
        Assert.Equal(expected.SelectedOperationalNotes, actual.SelectedOperationalNotes);
    }

    // ===================== Phase 8A: Daily Feature / section selection =====================

    // Spec Phase 8A: Daily Feature supports exactly the three intended types.
    [Fact]
    public void DailyFeatureType_HasExactlyTheThreeIntendedValues()
    {
        var values = Enum.GetValues<DailyFeatureType>();

        Assert.Equal(3, values.Length);
        Assert.Contains(DailyFeatureType.QuestionOfTheDay, values);
        Assert.Contains(DailyFeatureType.UselessFactOfTheDay, values);
        Assert.Contains(DailyFeatureType.QuoteOfTheDay, values);
    }

    // Schema-v1 drafts (pre-Phase 8A) persisted "DailyFunType"/"DailyFunText" under their old names
    // and had no section-selection concept at all; loading one must not lose that data or silently
    // hide every optional section.
    [Fact]
    public async Task LoadAssociateDateAsync_LegacySchemaV1Payload_MigratesDailyFunAndDefaultsSectionsToIncluded()
    {
        var env = CreateEnvironment();
        var legacyJson = """
            {
                "ManualHoursOverride": { "Enabled": false, "OpeningTime": null, "ClosingTime": null },
                "PrivateBuyout": { "Enabled": false, "OpeningTime": null, "ClosingTime": null, "Details": null },
                "ProjectedAttendance": 9000,
                "PreSolds": null,
                "EarlyRideTime": { "Details": null, "ParticipatingAttractions": [] },
                "OperationalNotes": ["Legacy note"],
                "DailyFunType": 1,
                "DailyFunText": "Carowinds opened in 1973.",
                "ProductOverrides": {},
                "CustomProducts": []
            }
            """;
        await env.Drafts.SaveAsync(new AssociateDraft(Date, SchemaVersion: 1, PayloadJson: legacyJson, UpdatedAt: FixedNow));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(DailyFeatureType.UselessFactOfTheDay, workspace.Draft.DailyFeatureType); // legacy int 1 == FactOfTheDay's old position == UselessFactOfTheDay's new position.
        Assert.Equal("Carowinds opened in 1973.", workspace.Draft.DailyFeatureText);
        Assert.Equal(PassdownSectionSelection.AllIncluded, workspace.Draft.SectionSelection);
        Assert.Equal(9000, workspace.Draft.ProjectedAttendance);
        Assert.Equal(["Legacy note"], workspace.Draft.OperationalNotes);
    }

    // Phase 8B: a Phase 8A draft already has a "SectionSelection" object, but that object predates
    // "IncludeHauntedAttractions" — loading it must default SCarowinds to included, not silently
    // hide it via the JSON deserializer's own missing-bool-defaults-to-false behavior.
    [Fact]
    public async Task LoadAssociateDateAsync_Phase8ADraftWithoutHauntedSelection_DefaultsSCarowindsToIncluded()
    {
        var env = CreateEnvironment();
        var phase8AJson = """
            {
                "ManualHoursOverride": { "Enabled": false, "OpeningTime": null, "ClosingTime": null },
                "PrivateBuyout": { "Enabled": false, "OpeningTime": null, "ClosingTime": null, "Details": null },
                "ProjectedAttendance": null,
                "PreSolds": null,
                "EarlyRideTime": { "Details": null, "ParticipatingAttractions": [] },
                "OperationalNotes": [],
                "DailyFeatureType": null,
                "DailyFeatureText": null,
                "ProductOverrides": {},
                "CustomProducts": [],
                "SectionSelection": {
                    "IncludeDailyAdmission": true,
                    "IncludeFastLane": false,
                    "IncludePromotions": true,
                    "IncludeSeasonPasses": true,
                    "IncludeAddOns": true,
                    "IncludeOperationalNotes": true
                }
            }
            """;
        await env.Drafts.SaveAsync(new AssociateDraft(Date, SchemaVersion: 2, PayloadJson: phase8AJson, UpdatedAt: FixedNow));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.True(workspace.Draft.SectionSelection.IncludeHauntedAttractions);
        Assert.False(workspace.Draft.SectionSelection.IncludeFastLane); // the draft's real, explicit choices are preserved.
        Assert.True(workspace.Draft.SectionSelection.IncludeDailyAdmission);
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
            OperationalNotes: ["Note one", "Note two"],
            DailyFeatureType: DailyFeatureType.UselessFactOfTheDay,
            DailyFeatureText: "Carowinds opened in 1973.",
            ProductOverrides: new Dictionary<string, ProductDraftOverride>
            {
                ["dated|DatedAdmissionTickets|PKG1"] = new ProductDraftOverride(false, 42.50m),
            },
            CustomProducts: [new CustomProductEntry("custom-1", "Group Rate", 10m, "Ask manager", true)],
            SectionSelection: new PassdownSectionSelection(true, false, true, false, true, false, true),
            SelectedOperationalNotes: []);

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
    public async Task SaveDraftAsync_MarksTheDraftDirtyForSync_UsingTheDraftDateAsTheEntityId()
    {
        var syncChangeTracker = new RecordingSyncChangeTracker();
        var env = CreateEnvironment(syncChangeTracker: syncChangeTracker);

        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());

        var call = Assert.Single(syncChangeTracker.Calls);
        Assert.Equal(SyncEntityType.AssociateDraft, call.EntityType);
        Assert.Equal(SyncEntityIdentity.ForDraft(Date), call.EntityId);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNoSyncChangeTrackerConfigured_StillSavesSuccessfully()
    {
        // Phase 16C §1: sync tracking is additive — a caller/test that never wires a tracker (the
        // default for every pre-16C call site) must behave exactly as before.
        var env = CreateEnvironment(syncChangeTracker: null);

        var status = await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());

        Assert.Equal(AssociateSaveState.Saved, status.State);
    }

    private sealed class RecordingSyncChangeTracker : ISyncChangeTracker
    {
        public List<(SyncEntityType EntityType, string EntityId)> Calls { get; } = [];

        public Task MarkLocalChangeAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
        {
            Calls.Add((entityType, entityId));
            return Task.CompletedTask;
        }
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

    // Phase 13D Part D/E: waterpark hours flow through to the composed workspace when the source
    // published them, and are simply absent (never fabricated) when it didn't.
    [Fact]
    public async Task ComposeParkHours_WithWaterparkHoursPublished_ExposesThem()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow, new TimeOnly(11, 0), new TimeOnly(18, 0));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.True(workspace.ParkHours.HasWaterparkHours);
        Assert.Equal(new TimeOnly(11, 0), workspace.ParkHours.WaterparkOpening);
        Assert.Equal(new TimeOnly(18, 0), workspace.ParkHours.WaterparkClosing);
    }

    [Fact]
    public async Task ComposeParkHours_WithoutWaterparkHoursPublished_LeavesThemAbsent()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(10, 0), new TimeOnly(21, 0), FixedNow);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.False(workspace.ParkHours.HasWaterparkHours);
        Assert.Null(workspace.ParkHours.WaterparkOpening);
        Assert.Null(workspace.ParkHours.WaterparkClosing);
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

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        Assert.Equal(new TimeOnly(9, 0), result.Workspace.ParkHours.Effective.Opening);
    }

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_FailedRefresh_PreservesUsableCachedData()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));
        env.RefreshOrchestrator.OnManualDateRefresh = (_, _) => Task.CompletedTask; // simulates a refresh whose sources all errored, writing nothing new

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        var product = Assert.Single(result.Workspace.Products);
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

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        AssertDraftsEqual(draft, result.Workspace.Draft);
        var product = Assert.Single(result.Workspace.Products);
        Assert.False(product.IncludeOnPdf);
        Assert.Equal(55m, product.DisplayPriceOverride);
        Assert.Equal(55m, product.EffectiveDisplayPrice);
    }

    // ===================== Phase 13B: overlap-lock guard =====================

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_NoLockFactory_RunsRefreshWithoutAnyGuard()
    {
        // Every pre-13B caller/test — the default, null factory means no overlap protection at all.
        var env = CreateEnvironment(refreshLockFactory: null);

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        Assert.Single(env.RefreshOrchestrator.ManualDateRefreshCalls);
        Assert.False(result.Summary.WasSkippedDueToOverlap);
    }

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_LockAlreadyHeld_ReturnsAlreadyInProgress_AndNeverCallsOrchestrator()
    {
        var fakeLock = new FakeScheduledRefreshLock { AcquireSucceeds = false };
        var env = CreateEnvironment(refreshLockFactory: () => fakeLock);

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        Assert.Empty(env.RefreshOrchestrator.ManualDateRefreshCalls); // never a second concurrent orchestrator run
        Assert.True(result.Summary.WasSkippedDueToOverlap);
        Assert.Equal(0, result.Summary.OperationsAttempted);
        Assert.Equal(1, fakeLock.TryAcquireCallCount);
        Assert.Equal(0, fakeLock.DisposeCallCount); // never acquired, so nothing to release
    }

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_LockAlreadyHeld_StillReloadsCurrentLocalState()
    {
        var fakeLock = new FakeScheduledRefreshLock { AcquireSucceeds = false };
        var env = CreateEnvironment(refreshLockFactory: () => fakeLock);
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow,
            new DatedAdmissionProduct("PKG-A", "Date Ticket", 60m, null, null, Date));

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        // Part L "reload local state when appropriate" — whatever a concurrently-running refresh
        // may already have written locally is still reflected, even though this call didn't run one.
        var product = Assert.Single(result.Workspace.Products);
        Assert.Equal(60m, product.OfficialPriceAmount);
    }

    [Fact]
    public async Task CheckAssociateDateOnlineAsync_LockAcquired_RunsRefresh_AndReleasesLockAfterward()
    {
        var fakeLock = new FakeScheduledRefreshLock { AcquireSucceeds = true };
        var env = CreateEnvironment(refreshLockFactory: () => fakeLock);

        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        Assert.Single(env.RefreshOrchestrator.ManualDateRefreshCalls);
        Assert.False(result.Summary.WasSkippedDueToOverlap);
        Assert.Equal(1, fakeLock.TryAcquireCallCount);
        Assert.Equal(1, fakeLock.DisposeCallCount); // released after the refresh completes
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
        var result = await env.Service.CheckAssociateDateOnlineAsync(Date);

        var product = Assert.Single(result.Workspace.Products);
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

    // Phase 17A §1/§11: an expired product is hidden from normal operational display exactly like an
    // archived one, but remains a distinct lifecycle reason — never deleted, never confused with a
    // confirmed-absence archive.
    [Fact]
    public async Task ExpiredCatalogProducts_AreExcludedFromDefaultProjection()
    {
        var env = CreateEnvironment();
        SeedCatalogProduct(env.CatalogProducts, SourceCategory.Promotions, "Ended Promotion", 20m, FixedNow, lifecycle: ProductLifecycleState.Expired);
        SeedCatalogProduct(env.CatalogProducts, SourceCategory.Promotions, "Active Promotion", 20m, FixedNow, lifecycle: ProductLifecycleState.Active);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var product = Assert.Single(workspace.Products);
        Assert.Equal("Active Promotion", product.DisplayName);
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

    // ===================== Category-level status (Phase 4B Part E) =====================

    [Fact]
    public async Task CategoryStatuses_CoverEveryNonParkHoursDatedCategory()
    {
        var env = CreateEnvironment();

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        // Phase 13C added SourceCategory.BringAFriendDuo as a fourth status-tracked dated category;
        // Phase 15A added SourceCategory.DatedBringAFriend as a fifth (Dashboard-visible only — it
        // never gets its own Associate Products/PDF section, see ProductDisplaySectionResolver).
        Assert.Equal(
            new[]
            {
                SourceCategory.DatedAdmissionTickets, SourceCategory.DatedFastLane, SourceCategory.HauntedAttractions,
                SourceCategory.BringAFriendDuo, SourceCategory.DatedBringAFriend,
            },
            workspace.CategoryStatuses.Select(s => s.Category));
        Assert.DoesNotContain(workspace.CategoryStatuses, s => s.Category == SourceCategory.ParkHours);
    }

    [Fact]
    public async Task CategoryStatuses_NoAttemptEver_ReportsNotCheckedYet()
    {
        var env = CreateEnvironment();

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var status = workspace.CategoryStatuses.Single(s => s.Category == SourceCategory.DatedFastLane);
        Assert.Equal(AssociateVerificationState.NotCheckedYet, status.State);
        Assert.Null(status.LastCheckedAt);
    }

    [Theory]
    [InlineData(RetrievalStatus.NotPublished)]
    [InlineData(RetrievalStatus.Unavailable)]
    public async Task CategoryStatuses_SuccessfulButEmptyOutcome_ReportsStatusEvenWithZeroProductRows(RetrievalStatus status)
    {
        var env = CreateEnvironment();
        var checkedAt = FixedNow.AddHours(-2);
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.HauntedAttractions, Date),
            Evidence(status, checkedAt, Date),
            payloadJson: null);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.DoesNotContain(workspace.Products, p => p.Category == SourceCategory.HauntedAttractions);
        var categoryStatus = workspace.CategoryStatuses.Single(s => s.Category == SourceCategory.HauntedAttractions);
        Assert.Equal(status switch { RetrievalStatus.NotPublished => AssociateVerificationState.NotPublished, _ => AssociateVerificationState.Unavailable }, categoryStatus.State);
        Assert.Equal(checkedAt, categoryStatus.LastCheckedAt);
    }

    [Fact]
    public async Task CategoryStatuses_ErrorWithNoPriorSuccess_ReportsErrorAndLastCheckedTime_EvenThoughNoProductRowsExist()
    {
        var env = CreateEnvironment();
        var checkedAt = FixedNow.AddMinutes(-30);
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date),
            Evidence(RetrievalStatus.Error, checkedAt, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.DoesNotContain(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets);
        var categoryStatus = workspace.CategoryStatuses.Single(s => s.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(AssociateVerificationState.Error, categoryStatus.State);
        Assert.Equal(checkedAt, categoryStatus.LastCheckedAt);
        Assert.NotNull(categoryStatus.Detail);
    }

    [Fact]
    public async Task CategoryStatuses_PriorSuccessThenLaterError_ReportsStaleWithProductRowsStillPresent()
    {
        var env = CreateEnvironment();
        await SeedAdmissionAsync(env.DatedResults, Date, FixedNow.AddHours(-3),
            new DatedAdmissionProduct("PKG-A", "Ticket", 60m, null, null, Date));
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date),
            Evidence(RetrievalStatus.Error, FixedNow, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Single(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets); // stale rows are still shown, not hidden.
        var categoryStatus = workspace.CategoryStatuses.Single(s => s.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(AssociateVerificationState.Stale, categoryStatus.State);
        Assert.Equal(FixedNow, categoryStatus.LastCheckedAt); // reflects the latest attempt, not the older success.
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
            OperationalNotes: ["Rain expected after 3 PM", "VIP tour group at 10 AM", "Short-staffed at North Gate"],
            DailyFeatureType: DailyFeatureType.QuestionOfTheDay,
            DailyFeatureText: "What year did Carowinds open?",
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
            ],
            SectionSelection: PassdownSectionSelection.AllIncluded with { IncludeSeasonPasses = false },
            SelectedOperationalNotes: []);

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
        Assert.Equal(3, workspace.Draft.OperationalNotes.Count);
    }

    // ===================== Phase 11D: persistent-note selection =====================

    private static Task<OperationalNote> CreateScannerNoteAsync(TestEnvironment env, DateOnly effectiveFrom, DateOnly? effectiveThrough = null) =>
        env.OperationalNotes.CreateAsync(new OperationalNoteDraft(
            "South Gate scanner #3 out of service", "Direct guests to gate 2.",
            OperationalNoteCategory.Equipment, OperationalNotePriority.Normal, effectiveFrom, effectiveThrough));

    [Fact]
    public async Task SelectOperationalNoteAsync_RelevantSource_AddsFrozenSelection()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);

        var workspace = await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        var selection = Assert.Single(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal(note.Id, selection.SourceOperationalNoteId);
        Assert.Equal(note.Title, selection.Title);
        Assert.Equal(note.Body, selection.Body);
        Assert.Equal(note.Category, selection.Category);
        Assert.Equal(note.Priority, selection.Priority);
        Assert.Equal(note.EffectiveFrom, selection.EffectiveFrom);
        Assert.Equal(note.EffectiveThrough, selection.EffectiveThrough);
    }

    [Fact]
    public async Task SelectOperationalNoteAsync_NoteNotRelevantForDate_Throws()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, effectiveFrom: OtherDate, effectiveThrough: OtherDate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.SelectOperationalNoteAsync(Date, note.Id));
    }

    [Fact]
    public async Task SelectOperationalNoteAsync_UnknownId_Throws()
    {
        var env = CreateEnvironment();

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.SelectOperationalNoteAsync(Date, Guid.NewGuid()));
    }

    [Fact]
    public async Task SelectOperationalNoteAsync_CalledTwice_IsIdempotent()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);

        await env.Service.SelectOperationalNoteAsync(Date, note.Id);
        var workspace = await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        Assert.Single(workspace.Draft.SelectedOperationalNotes);
    }

    [Fact]
    public async Task DeselectOperationalNoteAsync_RemovesSelection()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        var workspace = await env.Service.DeselectOperationalNoteAsync(Date, note.Id);

        Assert.Empty(workspace.Draft.SelectedOperationalNotes);
    }

    [Fact]
    public async Task DeselectOperationalNoteAsync_NeverSelected_IsSafeNoOp()
    {
        var env = CreateEnvironment();

        var workspace = await env.Service.DeselectOperationalNoteAsync(Date, Guid.NewGuid());

        Assert.Empty(workspace.Draft.SelectedOperationalNotes);
    }

    // Part I: a second, later selection after a deselect is a new explicit decision — it copies
    // whatever the source note currently says, not the earlier frozen content.
    [Fact]
    public async Task Reselect_AfterDeselect_CopiesCurrentSourceContent_NotTheOldFrozenCopy()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);
        await env.Service.DeselectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            "Scanner repaired; use normally", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Normal, Date, null));

        var workspace = await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        var selection = Assert.Single(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal("Scanner repaired; use normally", selection.Title);
        Assert.Null(selection.Body);
    }

    // Central Phase 11D guarantee (spec Part B/C/AD): editing/resolving/re-dating the source note
    // after selection must never silently rewrite or remove the already-selected frozen copy.
    [Fact]
    public async Task SourceEditAfterSelection_DoesNotMutateSelectedCopy()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            "Scanner repaired; use normally", "All clear.", OperationalNoteCategory.Equipment, OperationalNotePriority.Important, Date, null));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var selection = Assert.Single(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal("South Gate scanner #3 out of service", selection.Title);
        Assert.Equal("Direct guests to gate 2.", selection.Body);
        Assert.Equal(OperationalNotePriority.Normal, selection.Priority);
    }

    [Fact]
    public async Task SourceResolveAfterSelection_DoesNotRemoveSelectedCopy()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.ResolveAsync(note.Id);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Single(workspace.Draft.SelectedOperationalNotes);
    }

    [Fact]
    public async Task SourceDateChangeAfterSelection_DoesNotRemoveSelectedCopy()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        // The source note's own range moves entirely off Date; the selected copy stays.
        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            note.Title, note.Body, note.Category, note.Priority, OtherDate, OtherDate));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);
        var selection = Assert.Single(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal(Date, selection.EffectiveFrom);
    }

    [Fact]
    public async Task Selections_AreDateSpecific_NeverLeakAcrossDates()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date, effectiveThrough: OtherDate);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        var otherDateWorkspace = await env.Service.LoadAssociateDateAsync(OtherDate);
        Assert.Empty(otherDateWorkspace.Draft.SelectedOperationalNotes);

        var dateWorkspace = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Single(dateWorkspace.Draft.SelectedOperationalNotes);
    }

    [Fact]
    public async Task SelectedOperationalNotes_PreserveSelectionOrder()
    {
        var env = CreateEnvironment();
        var noteA = await CreateScannerNoteAsync(env, Date);
        var noteB = await env.OperationalNotes.CreateAsync(new OperationalNoteDraft(
            "Group check-in support at North Gate", null, OperationalNoteCategory.GuestServices, OperationalNotePriority.Normal, Date, null));

        await env.Service.SelectOperationalNoteAsync(Date, noteB.Id);
        var workspace = await env.Service.SelectOperationalNoteAsync(Date, noteA.Id);

        Assert.Equal([noteB.Id, noteA.Id], workspace.Draft.SelectedOperationalNotes.Select(s => s.SourceOperationalNoteId));
    }

    [Fact]
    public async Task LegacyDraft_WithoutSelectedOperationalNotesProperty_DefaultsToEmpty()
    {
        var env = CreateEnvironment();
        var legacyJson = """
            {
                "ManualHoursOverride": { "Enabled": false, "OpeningTime": null, "ClosingTime": null },
                "PrivateBuyout": { "Enabled": false, "OpeningTime": null, "ClosingTime": null, "Details": null },
                "ProjectedAttendance": null,
                "PreSolds": null,
                "EarlyRideTime": { "Details": null, "ParticipatingAttractions": [] },
                "OperationalNotes": ["Pre-11D note"],
                "DailyFeatureType": null,
                "DailyFeatureText": null,
                "ProductOverrides": {},
                "CustomProducts": [],
                "SectionSelection": {
                    "IncludeDailyAdmission": true, "IncludeFastLane": true, "IncludePromotions": true,
                    "IncludeSeasonPasses": true, "IncludeAddOns": true, "IncludeOperationalNotes": true,
                    "IncludeHauntedAttractions": true
                }
            }
            """;
        await env.Drafts.SaveAsync(new AssociateDraft(Date, SchemaVersion: 2, PayloadJson: legacyJson, UpdatedAt: FixedNow));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Empty(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal(["Pre-11D note"], workspace.Draft.OperationalNotes);
    }

    [Fact]
    public async Task SelectOperationalNoteAsync_NeverMutatesSourceNote()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);

        await env.Service.SelectOperationalNoteAsync(Date, note.Id);
        await env.Service.DeselectOperationalNoteAsync(Date, note.Id);

        var reloaded = await env.OperationalNotes.ListAsync();
        var stored = Assert.Single(reloaded);
        Assert.Equal(note.UpdatedAtUtc, stored.UpdatedAtUtc);
        Assert.Equal(OperationalNoteStatus.Active, stored.Status);
        Assert.Null(stored.ResolvedAtUtc);
    }

    [Fact]
    public async Task SelectDeselect_NeverInvokeRefreshOrchestrator()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);

        await env.Service.SelectOperationalNoteAsync(Date, note.Id);
        await env.Service.DeselectOperationalNoteAsync(Date, note.Id);

        Assert.Empty(env.RefreshOrchestrator.ManualDateRefreshCalls);
        Assert.Equal(0, env.RefreshOrchestrator.ScheduledRefreshCallCount);
    }

    [Fact]
    public async Task SelectOperationalNoteAsync_DoesNotAffectManualOperationalNotesText()
    {
        var env = CreateEnvironment();
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with { OperationalNotes = ["A manual note"] });
        var note = await CreateScannerNoteAsync(env, Date);

        var workspace = await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        Assert.Equal(["A manual note"], workspace.Draft.OperationalNotes);
    }

    [Fact]
    public async Task GetRelevantOperationalNoteSuggestionsAsync_UsesAssociateSelectedDate_NotEasternToday()
    {
        var env = CreateEnvironment();
        var farFutureDate = Date.AddDays(30);
        await CreateScannerNoteAsync(env, farFutureDate);

        var suggestionsForToday = await env.Service.GetRelevantOperationalNoteSuggestionsAsync(Date);
        var suggestionsForFarFuture = await env.Service.GetRelevantOperationalNoteSuggestionsAsync(farFutureDate);

        Assert.Empty(suggestionsForToday);
        Assert.Single(suggestionsForFarFuture);
    }

    // ===================== Phase 11E: source-change awareness + Replace with Current Version =====================

    [Fact]
    public async Task ComposedWorkspace_UnchangedSource_ReportsUnchangedState()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(OperationalNoteSourceComparisonState.Unchanged, workspace.SelectedOperationalNoteSourceStates![note.Id]);
    }

    [Fact]
    public async Task ComposedWorkspace_SourceEdited_ReportsContentChanged()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            "Scanner repaired; use normally", "All clear.", OperationalNoteCategory.Equipment, OperationalNotePriority.Important, Date, null));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(OperationalNoteSourceComparisonState.ContentChanged, workspace.SelectedOperationalNoteSourceStates![note.Id]);
    }

    [Fact]
    public async Task ComposedWorkspace_SourceResolved_ReportsResolvedState()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.ResolveAsync(note.Id);

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(OperationalNoteSourceComparisonState.Resolved, workspace.SelectedOperationalNoteSourceStates![note.Id]);
    }

    [Fact]
    public async Task ComposedWorkspace_SourceDateRangeNoLongerCoversSelectionDate_ReportsNoLongerApplicable()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            note.Title, note.Body, note.Category, note.Priority, OtherDate, OtherDate));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(OperationalNoteSourceComparisonState.NoLongerApplicable, workspace.SelectedOperationalNoteSourceStates![note.Id]);
    }

    [Fact]
    public async Task ComposedWorkspace_NoSelections_SourceStatesIsEmpty()
    {
        var env = CreateEnvironment();

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Empty(workspace.SelectedOperationalNoteSourceStates!);
    }

    [Fact]
    public async Task ReplaceOperationalNoteWithCurrentAsync_ContentChanged_UpdatesFrozenCopyToCurrentSourceContent()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            "Scanner repaired; use normally", "All clear.", OperationalNoteCategory.Equipment, OperationalNotePriority.Important, Date, null));

        var workspace = await env.Service.ReplaceOperationalNoteWithCurrentAsync(Date, note.Id);

        var selection = Assert.Single(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal("Scanner repaired; use normally", selection.Title);
        Assert.Equal("All clear.", selection.Body);
        Assert.Equal(OperationalNotePriority.Important, selection.Priority);
        Assert.Equal(OperationalNoteSourceComparisonState.Unchanged, workspace.SelectedOperationalNoteSourceStates![note.Id]);
    }

    // Part R: replacing preserves the selection's list position rather than moving it to the end.
    [Fact]
    public async Task ReplaceOperationalNoteWithCurrentAsync_PreservesSelectionOrder()
    {
        var env = CreateEnvironment();
        var noteA = await CreateScannerNoteAsync(env, Date);
        var noteB = await env.OperationalNotes.CreateAsync(new OperationalNoteDraft(
            "Group check-in support at North Gate", null, OperationalNoteCategory.GuestServices, OperationalNotePriority.Normal, Date, null));
        await env.Service.SelectOperationalNoteAsync(Date, noteA.Id);
        await env.Service.SelectOperationalNoteAsync(Date, noteB.Id);

        await env.OperationalNotes.UpdateAsync(noteA.Id, new OperationalNoteDraft(
            "Scanner repaired", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Normal, Date, null));

        var workspace = await env.Service.ReplaceOperationalNoteWithCurrentAsync(Date, noteA.Id);

        Assert.Equal([noteA.Id, noteB.Id], workspace.Draft.SelectedOperationalNotes.Select(s => s.SourceOperationalNoteId));
        Assert.Equal("Scanner repaired", workspace.Draft.SelectedOperationalNotes[0].Title);
    }

    [Fact]
    public async Task ReplaceOperationalNoteWithCurrentAsync_NotCurrentlySelected_IsSafeNoOp()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);

        var workspace = await env.Service.ReplaceOperationalNoteWithCurrentAsync(Date, note.Id);

        Assert.Empty(workspace.Draft.SelectedOperationalNotes);
    }

    [Fact]
    public async Task ReplaceOperationalNoteWithCurrentAsync_AutosavesTheNewFrozenCopy()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);
        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            "Scanner repaired", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Normal, Date, null));

        await env.Service.ReplaceOperationalNoteWithCurrentAsync(Date, note.Id);

        var reloaded = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal("Scanner repaired", Assert.Single(reloaded.Draft.SelectedOperationalNotes).Title);
    }

    [Fact]
    public async Task ReplaceOperationalNoteWithCurrentAsync_NeverMutatesSourceNote()
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.Service.ReplaceOperationalNoteWithCurrentAsync(Date, note.Id);

        var reloaded = await env.OperationalNotes.ListAsync();
        var stored = Assert.Single(reloaded);
        Assert.Equal(note.UpdatedAtUtc, stored.UpdatedAtUtc);
    }

    // Part P: no code path other than an explicit Replace ever changes a selection's frozen content.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoAutomaticSync_ReloadOrCheckOnline_NeverUpdatesFrozenSelection(bool checkOnline)
    {
        var env = CreateEnvironment();
        var note = await CreateScannerNoteAsync(env, Date);
        await env.Service.SelectOperationalNoteAsync(Date, note.Id);

        await env.OperationalNotes.UpdateAsync(note.Id, new OperationalNoteDraft(
            "Scanner repaired; use normally", "All clear.", OperationalNoteCategory.Equipment, OperationalNotePriority.Important, Date, null));

        var workspace = checkOnline
            ? (await env.Service.CheckAssociateDateOnlineAsync(Date)).Workspace
            : await env.Service.LoadAssociateDateAsync(Date);

        var selection = Assert.Single(workspace.Draft.SelectedOperationalNotes);
        Assert.Equal("South Gate scanner #3 out of service", selection.Title);
        Assert.Equal(OperationalNoteSourceComparisonState.ContentChanged, workspace.SelectedOperationalNoteSourceStates![note.Id]);
    }

    // ===================== Phase 18D: The Conjuring: Beyond Fear =====================

    // The product arrives in the HauntedAttractions dated payload (official keyword THECONJURING,
    // docs/retrieval/dated-haunted-source.md Phase 18D addendum) and must surface beside the
    // Haunted Attractions passes in the SCarowinds section — never Add-Ons, never suppressed.
    [Fact]
    public async Task Phase18D_ConjuringInHauntedPayload_AppearsInSCarowindsSectionWithItsExactNightPrice()
    {
        var env = CreateEnvironment();
        await SeedHauntedAsync(env.DatedResults, Date, FixedNow,
            new DatedHauntedProduct("10098524", "Haunted Attractions Pass", 25m, null, null, Date),
            new DatedHauntedProduct("10098530", "Haunted Attractions Express Pass", 75m, null, null, Date),
            new DatedHauntedProduct("10098308", "The Conjuring: Beyond Fear", 30m, null, null, Date));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        var conjuring = Assert.Single(workspace.Products, p => p.DisplayName == "The Conjuring: Beyond Fear");
        Assert.Equal(SourceCategory.HauntedAttractions, conjuring.Category);
        Assert.Equal(30m, conjuring.OfficialPriceAmount);
        Assert.True(conjuring.DateSpecific);
        Assert.Equal(Date, conjuring.ApplicableDate);
        Assert.True(conjuring.IncludeOnPdf);
        Assert.Equal(ProductDisplaySection.SCarowinds, ProductDisplaySectionResolver.Resolve(conjuring));

        var resolved = ProductDisplaySectionResolver.ResolveAll(workspace.Products);
        Assert.Equal(3, resolved.Count(entry => entry.Section == ProductDisplaySection.SCarowinds));
        Assert.DoesNotContain(resolved, entry => entry.Section == ProductDisplaySection.AddOns);
    }

    // ===================== Phase 18D: North Gate / Avenue overrides =====================

    [Fact]
    public async Task Phase18D_GateOverrides_PersistWithTheDraft_AndSurviveAServiceRestart()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(11, 0), new TimeOnly(20, 0), FixedNow);
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with { AvenueOpeningOverride = new TimeOnly(10, 0) });

        // "Restart": a brand-new service over the same stores.
        var restarted = new AssociatePassdownService(env.Drafts, env.DatedResults, env.CatalogProducts, env.RefreshOrchestrator, env.OperationalNotes, env.Clock);
        var workspace = await restarted.LoadAssociateDateAsync(Date);

        Assert.Equal(new TimeOnly(10, 0), workspace.Draft.AvenueOpeningOverride);
        Assert.Null(workspace.Draft.NorthGateOpeningOverride);
        Assert.Equal(new TimeOnly(11, 0), workspace.ParkHours.Effective.Opening);
        Assert.Equal(new TimeOnly(10, 30), workspace.ParkHours.Effective.NorthGateOpening);
        Assert.Equal(new TimeOnly(10, 0), workspace.ParkHours.Effective.AvenueOpening);

        var otherDate = await restarted.LoadAssociateDateAsync(OtherDate);
        Assert.Null(otherDate.Draft.AvenueOpeningOverride); // per operating date, never global.
    }

    [Fact]
    public async Task Phase18D_OfficialDefaultChangesAfterAnOverride_OverrideStays_ResetUsesTheCurrentDefault()
    {
        var env = CreateEnvironment();
        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(11, 0), new TimeOnly(20, 0), FixedNow);
        await env.Service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with { AvenueOpeningOverride = new TimeOnly(10, 0) });

        await SeedParkHoursAsync(env.DatedResults, Date, new TimeOnly(12, 0), new TimeOnly(20, 0), FixedNow.AddHours(1));
        var changed = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(new TimeOnly(10, 0), changed.ParkHours.Effective.AvenueOpening);
        Assert.Equal(new TimeOnly(11, 30), changed.ParkHours.Effective.NorthGateOpening);

        await env.Service.SaveDraftAsync(Date, changed.Draft with { AvenueOpeningOverride = null });
        var reset = await env.Service.LoadAssociateDateAsync(Date);
        Assert.Equal(new TimeOnly(11, 15), reset.ParkHours.Effective.AvenueOpening);
        Assert.False(reset.ParkHours.Effective.IsAvenueOverridden);
    }

    [Fact]
    public async Task Phase18D_PreGateOverrideDraftJson_LoadsWithNoOverrides()
    {
        var env = CreateEnvironment();
        // A schema-4 draft exactly as Phase 18C wrote it: no gate-override properties at all.
        var legacyJson = JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 4200 });
        Assert.DoesNotContain("OpeningOverride", legacyJson);
        await env.Drafts.SaveAsync(new AssociateDraft(Date, 4, legacyJson, FixedNow));

        var workspace = await env.Service.LoadAssociateDateAsync(Date);

        Assert.Equal(4200, workspace.Draft.ProjectedAttendance);
        Assert.Null(workspace.Draft.NorthGateOpeningOverride);
        Assert.Null(workspace.Draft.AvenueOpeningOverride);
    }

    [Fact]
    public void Phase18D_DraftWithoutOverrides_SerializesAndHashesExactlyAsBefore_SoUpgradingCausesNoSyncChurn()
    {
        var draft = AssociateDraftPayload.CreateDefault() with { PreSolds = 12 };
        var canonical = JsonSerializer.Serialize(draft, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

        Assert.DoesNotContain("NorthGateOpeningOverride", JsonSerializer.Serialize(draft));
        Assert.DoesNotContain("AvenueOpeningOverride", canonical);

        var withOverride = draft with { NorthGateOpeningOverride = new TimeOnly(9, 45) };
        Assert.Contains("\"NorthGateOpeningOverride\":\"09:45:00\"", JsonSerializer.Serialize(withOverride));
        Assert.NotEqual(SyncContentHash.ComputeDraftHash(draft), SyncContentHash.ComputeDraftHash(withOverride));
        Assert.Equal(
            new TimeOnly(9, 45),
            AssociateDraftPayloadSerializer.Deserialize(JsonSerializer.Serialize(withOverride)).NorthGateOpeningOverride);
    }

    [Fact]
    public void Phase18D_ConflictComparison_ShowsBothGateOverrides()
    {
        var local = AssociateDraftPayload.CreateDefault() with { NorthGateOpeningOverride = new TimeOnly(9, 45) };
        var remote = AssociateDraftPayload.CreateDefault() with { AvenueOpeningOverride = new TimeOnly(10, 0) };

        var rows = new DraftConflictDetail(Date, local, remote, false, false, Guid.NewGuid(), FixedNow, false)
            .BuildFieldComparisons().ToDictionary(r => r.Label);

        Assert.Equal(("9:45 AM", "Default"), (rows["North Gate Override"].LocalValue, rows["North Gate Override"].RemoteValue));
        Assert.Equal(("Default", "10:00 AM"), (rows["Avenue Override"].LocalValue, rows["Avenue Override"].RemoteValue));
    }
}
