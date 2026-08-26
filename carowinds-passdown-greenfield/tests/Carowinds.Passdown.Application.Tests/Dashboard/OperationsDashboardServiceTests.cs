using System.Reflection;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Dashboard;

/// <summary>
/// Covers <see cref="OperationsDashboardService"/> (Phase 10A Part E/J): every value must come from
/// the real <see cref="AssociatePassdownService"/>'s already-composed <see cref="AssociateWorkspace"/>
/// (never a second hours/draft-composition implementation) and <see cref="IFinalizedPassdownRepository"/>
/// metadata only — never a PDF blob load. Uses the real <see cref="AssociatePassdownService"/> over
/// in-memory repositories (not a fake) so a passing test actually proves reuse, not just a scripted
/// double agreeing with itself.
/// </summary>
public class OperationsDashboardServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero); // 10:00 AM Eastern (EDT, UTC-4)

    private sealed record TestEnvironment(
        OperationsDashboardService Service,
        InMemoryAssociateDraftRepository Drafts,
        InMemoryDatedResultRepository DatedResults,
        CountingFinalizedPassdownRepository FinalizedPassdowns,
        FakeClock Clock);

    private static TestEnvironment CreateEnvironment(FakeClock? clock = null)
    {
        clock ??= new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var associateService = new AssociatePassdownService(drafts, datedResults, catalogProducts, new FakeRefreshOrchestrator(), clock);
        var finalizedPassdowns = new CountingFinalizedPassdownRepository();
        var service = new OperationsDashboardService(associateService, finalizedPassdowns, clock);
        return new TestEnvironment(service, drafts, datedResults, finalizedPassdowns, clock);
    }

    private static Task SaveDraftAsync(TestEnvironment env, AssociateDraftPayload draft) =>
        env.Drafts.SaveAsync(new AssociateDraft(Date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(draft), env.Clock.UtcNow));

    // #4/#5: "today" resolves to the current Eastern calendar date, independent of any caller-selected date.
    [Fact]
    public async Task GetTodayAsync_ResolvesToCurrentEasternDate()
    {
        // 2026-08-21T14:00:00Z is 10:00 AM EDT (UTC-4) on 2026-08-21 — same calendar date either way,
        // so also assert the Eastern conversion is actually being used near a day boundary.
        var lateUtcClock = new FakeClock(new DateTimeOffset(2026, 8, 22, 2, 30, 0, TimeSpan.Zero)); // 10:30 PM EDT on 2026-08-21
        var env = CreateEnvironment(lateUtcClock);

        var snapshot = await env.Service.GetTodayAsync();

        Assert.Equal(new DateOnly(2026, 8, 21), snapshot.OperatingDate);
    }

    [Fact]
    public async Task LoadAsync_ArbitraryDate_UsesThatDateRegardlessOfClock()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 9, 1));

        Assert.Equal(new DateOnly(2026, 9, 1), snapshot.OperatingDate);
    }

    // #6/#23: no HTTP/Playwright/refresh dependency exists on this type by construction.
    [Fact]
    public void Constructor_HasNoRefreshOrchestratorOrRetrievalDependency()
    {
        var constructor = typeof(OperationsDashboardService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(parameterTypes, t => t == typeof(IRefreshOrchestrator));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Http", StringComparison.OrdinalIgnoreCase));
    }

    // #12: no draft ever saved -> NoDraft, never Finalized/DraftInProgress.
    [Fact]
    public async Task LoadAsync_NoSavedDraft_ReportsNoDraft()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.NoDraft, snapshot.PassdownStatus.Status);
        Assert.Null(snapshot.PassdownStatus.LastSavedAt);
    }

    // #11: a saved (but not finalized) draft -> DraftInProgress, carrying the real save timestamp.
    [Fact]
    public async Task LoadAsync_SavedDraft_ReportsDraftInProgress()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault());

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.DraftInProgress, snapshot.PassdownStatus.Status);
        Assert.Equal(env.Clock.UtcNow, snapshot.PassdownStatus.LastSavedAt);
    }

    [Fact]
    public async Task LoadAsync_FinalizedDate_ReportsFinalizedWithTimestamp_AndNeverLoadsFullRecord()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault());
        var finalizedAt = env.Clock.UtcNow.AddHours(20);
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date, finalizedAt, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1, 2, 3], PdfSha256: "pdfhash", PdfSizeBytes: 3));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.Finalized, snapshot.PassdownStatus.Status);
        Assert.Equal(finalizedAt, snapshot.PassdownStatus.FinalizedAt);

        // #18: the dashboard must never materialize a historical PDF blob.
        Assert.Equal(0, env.FinalizedPassdowns.GetByDateCallCount);
    }

    // #8: officially closed -> Closed.
    [Fact]
    public async Task LoadAsync_OfficialParkHoursClosed_ReportsClosed()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Closed),
            payloadJson: null);

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.Closed, snapshot.ParkStatus.Status);
    }

    // #9: an enabled manual hours override on an otherwise-open day -> CustomHours (reusing ParkHoursCalculator's own precedence, never a second rule).
    [Fact]
    public async Task LoadAsync_ManualHoursOverrideEnabled_ReportsCustomHours()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Verified),
            JsonSerializer.Serialize(new ParkHoursPayload(new TimeOnly(10, 0), new TimeOnly(20, 0))));
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(9, 0), new TimeOnly(23, 0)),
        });

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.CustomHours, snapshot.ParkStatus.Status);
    }

    // #10: an enabled private-buyout override -> PrivateBuyout, even on an officially closed day (Part F names this the one exception).
    [Fact]
    public async Task LoadAsync_PrivateBuyoutEnabled_ReportsPrivateBuyout()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Closed),
            payloadJson: null);
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault() with
        {
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(18, 0), new TimeOnly(22, 0), "Corporate event."),
        });

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.PrivateBuyout, snapshot.ParkStatus.Status);
    }

    // #13: notes summary derives only from the current date's draft — never a second persistence model.
    [Fact]
    public async Task LoadAsync_OperationalNotes_SummarizesFromDraftOnly()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault() with
        {
            OperationalNotes = ["Gate 3 closed.", "VIP tour at 11 AM.", "Extra staffing at Fury.", "Fourth line."],
        });

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.True(snapshot.Notes.HasNotes);
        Assert.Equal(4, snapshot.Notes.TotalLineCount);
        Assert.Equal(3, snapshot.Notes.PreviewLines.Count);
    }

    [Fact]
    public async Task LoadAsync_NoOperationalNotes_ReportsEmptySummary()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.False(snapshot.Notes.HasNotes);
        Assert.Empty(snapshot.Notes.PreviewLines);
    }

    // #14: source health aggregates the same per-category verification state the Associate screen already computes.
    [Fact]
    public async Task LoadAsync_OneCategoryUnavailable_ReportsSomeUnavailable()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedFastLane, Date),
            Evidence(RetrievalStatus.Unavailable));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardSourceHealth.SomeUnavailable, snapshot.SourceHealth.Health);
        Assert.True(snapshot.SourceHealth.UnavailableCount > 0);
    }

    [Fact]
    public async Task LoadAsync_NothingEverChecked_ReportsAllCurrent()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardSourceHealth.AllCurrent, snapshot.SourceHealth.Health);
    }

    private static SourceEvidence Evidence(RetrievalStatus status) =>
        new("Fake Source", new Uri("https://example.test/fake-source"), FixedNow, status, $"{status}.", RetrievalStrategy.DirectApi, Date, status == RetrievalStatus.Verified ? Date : null);

    /// <summary>Wraps the real in-memory repository, counting <see cref="GetByDateAsync"/> calls — the only method that can return a historical PDF blob — so a test can assert the dashboard never calls it (Phase 10A Part S).</summary>
    private sealed class CountingFinalizedPassdownRepository : IFinalizedPassdownRepository
    {
        private readonly InMemoryFinalizedPassdownRepository _inner = new();

        public int GetByDateCallCount { get; private set; }

        public Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
        {
            GetByDateCallCount++;
            return _inner.GetByDateAsync(passdownDate, cancellationToken);
        }

        public Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default) =>
            _inner.ExistsForDateAsync(passdownDate, cancellationToken);

        public Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(finalizedPassdown, cancellationToken);

        public Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            _inner.ListAsync(cancellationToken);
    }
}
