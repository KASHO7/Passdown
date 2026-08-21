using System.Diagnostics;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Browser;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Refresh;

/// <summary>
/// Opt-in live smoke test (spec §14.3, Phase 3G Part L): runs a real "Check this date online"
/// manual refresh (Part C) through <see cref="RefreshOrchestrator"/> against every currently
/// implemented dated source — park hours, dated admission, dated Fast Lane, dated
/// haunted/SCarowinds — for tomorrow's date, over the real network. Reports elapsed time and each
/// source's outcome via test output. Excluded from a normal <c>dotnet test</c> run — see the
/// <c>build-and-test</c> skill for the opt-in <c>Category=LiveSmoke</c> filter. A failure here must
/// never fail the normal suite; it only reports what the live sources actually returned today.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class RefreshOrchestratorLiveSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunManualDateRefreshAsync_AcrossAllDatedSources_CompletesAndReportsTiming()
    {
        var clock = new SystemClock();
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });

        var datedResultRepository = new RecordingDatedResultRepository();
        var registrations = new List<SourceRegistration>
        {
            new DatedSourceRegistration<ParkHoursPayload>(new ParkHoursSource(retrievalHttpClient, clock), datedResultRepository),
            new DatedSourceRegistration<DatedAdmissionPayload>(new DatedAdmissionSource(retrievalHttpClient, clock), datedResultRepository),
            new DatedSourceRegistration<DatedFastLanePayload>(new DatedFastLaneSource(retrievalHttpClient, clock), datedResultRepository),
            new DatedSourceRegistration<DatedHauntedPayload>(new DatedHauntedSource(retrievalHttpClient, clock), datedResultRepository),
        };

        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var sourceStateRepository = new NullSourceStateRepository();
        var freshnessPlanner = new FreshnessPlanner(sourceStateRepository, clock, policies);
        var rollingDatePlanner = new RollingDatePlanner(clock, new RollingWindowOptions());
        var browserSessionFactory = new PlaywrightJobBrowserSessionFactory(new PlaywrightBrowserSessionFactory(new PlaywrightBrowserSessionOptions()));

        var orchestrator = new RefreshOrchestrator(
            registrations, freshnessPlanner, rollingDatePlanner, sourceStateRepository, policies,
            browserSessionFactory, clock, new RefreshOrchestratorOptions { MaxConcurrency = 4 });

        var requestedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).AddDays(1).DateTime);

        var stopwatch = Stopwatch.StartNew();
        var summary = await orchestrator.RunManualDateRefreshAsync(requestedDate);
        stopwatch.Stop();

        output.WriteLine($"Manual refresh for {requestedDate:yyyy-MM-dd} completed in {stopwatch.ElapsedMilliseconds} ms.");
        foreach (var result in summary.Results)
        {
            output.WriteLine($"  {result.Category}: {result.Outcome} ({result.Status}) — {result.Detail}");
        }

        Assert.Equal(4, summary.OperationsAttempted);
        Assert.Equal(0, summary.Cancelled);
    }

    private sealed class RecordingDatedResultRepository : IDatedResultRepository
    {
        private readonly Dictionary<DatedResultKey, DatedResultRecord> _records = new();

        public Task<DatedResultRecord?> GetAsync(DatedResultKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.GetValueOrDefault(key));

        public Task<IReadOnlyList<DatedResultRecord>> GetByRequestedDateAsync(DateOnly requestedDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DatedResultRecord>>(_records.Values.Where(r => r.Key.Date == requestedDate).ToList());

        public Task RecordSuccessAsync(DatedResultKey key, SourceEvidence evidence, string? payloadJson, CancellationToken cancellationToken = default)
        {
            _records[key] = new DatedResultRecord(key, evidence, payloadJson, evidence);
            return Task.CompletedTask;
        }

        public Task RecordFailedAttemptAsync(DatedResultKey key, SourceEvidence evidence, CancellationToken cancellationToken = default)
        {
            var existing = _records.GetValueOrDefault(key);
            _records[key] = new DatedResultRecord(key, existing?.SuccessEvidence, existing?.SuccessPayloadJson, evidence);
            return Task.CompletedTask;
        }
    }

    /// <summary>Freshness/source-state is irrelevant to a manual single-date refresh (Part C never touches it) — this only satisfies the constructor.</summary>
    private sealed class NullSourceStateRepository : ISourceStateRepository
    {
        public Task<SourceStateRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SourceStateRecord?>(null);

        public Task SaveAsync(SourceStateRecord state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
