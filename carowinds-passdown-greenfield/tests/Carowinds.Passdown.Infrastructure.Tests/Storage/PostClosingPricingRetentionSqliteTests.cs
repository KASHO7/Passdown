using System.Net;
using System.Text.Json;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Pre-RC.15 post-closing pricing retention (docs/pre-rc15-post-closing-pricing-retention.md), end to
/// end against real SQLite (schema unchanged): official fixture → real accesso adapter → registration →
/// <c>dated_results</c>. Reproduces the Windows report of 2026-11-27: once the park closes the official
/// calendar stops listing today (live-observed 11:12 PM ET), the source answers a successful
/// <see cref="RetrievalStatus.Unavailable"/>, and before the fix that answer replaced the prices
/// verified earlier that day with an empty payload. "Today" is 2026-08-20, matching the fixtures.
/// </summary>
public class PostClosingPricingRetentionSqliteTests : IDisposable
{
    /// <summary>2026-08-20 3:10 PM Eastern (EDT) — operating hours.</summary>
    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 20, 19, 10, 0, TimeSpan.Zero);

    /// <summary>2026-08-20 10:15 PM Eastern — after closing, still the same Eastern date.</summary>
    private static readonly DateTimeOffset AfterClosing = new(2026, 8, 21, 2, 15, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 8, 20);
    private static readonly DateOnly Tomorrow = new(2026, 8, 21);

    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>Answers each accesso POST from a responder, so a test can switch the official answer mid-test like the real calendar does at closing.</summary>
    private sealed class SwitchableAccessoClient : IRetrievalHttpClient
    {
        public Func<string, RetrievalHttpResponse>? Responder { get; set; }

        public Exception? Failure { get; set; }

        public int Posts { get; private set; }

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            Posts++;
            return Failure is not null ? Task.FromException<RetrievalHttpResponse>(Failure) : Task.FromResult(Responder!(jsonBody));
        }
    }

    private static RetrievalHttpResponse Fixture(string path) => new(HttpStatusCode.OK, "application/json", FixtureFile.ReadText(path));

    private static bool IsSingleDay(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("start_date").GetString() == document.RootElement.GetProperty("end_date").GetString();
    }

    /// <summary>Fast Lane asks for the single day first, then a horizon window (docs/retrieval/dated-fast-lane-source.md).</summary>
    private static Func<string, RetrievalHttpResponse> FastLaneOperating() => body => IsSingleDay(body)
        ? Fixture("dated-fast-lane/published-today.json")
        : Fixture("dated-fast-lane/horizon-window.json");

    private static Func<string, RetrievalHttpResponse> FastLaneAfterClosing() => body => IsSingleDay(body)
        ? Fixture("dated-fast-lane/no-dates-found.json")
        : Fixture("dated-fast-lane/after-closing-horizon-window.json");

    private sealed record Env(
        DatedSourceRegistration<DatedAdmissionPayload> Admission,
        DatedSourceRegistration<DatedFastLanePayload> FastLane,
        SwitchableAccessoClient AdmissionHttp,
        SwitchableAccessoClient FastLaneHttp,
        FakeClock Clock,
        SqliteDatedResultRepository Repository);

    private Env Build()
    {
        var clock = new FakeClock(VerifiedAt);
        var repository = new SqliteDatedResultRepository(_fixture.ConnectionFactory);
        var admissionHttp = new SwitchableAccessoClient { Responder = _ => Fixture("dated-admission/published-window.json") };
        var fastLaneHttp = new SwitchableAccessoClient { Responder = FastLaneOperating() };
        return new Env(
            new DatedSourceRegistration<DatedAdmissionPayload>(new DatedAdmissionSource(admissionHttp, clock), repository),
            new DatedSourceRegistration<DatedFastLanePayload>(new DatedFastLaneSource(fastLaneHttp, clock), repository),
            admissionHttp,
            fastLaneHttp,
            clock,
            repository);
    }

    private static DatedAdmissionPayload AdmissionPayload(DatedResultRecord? record) =>
        JsonSerializer.Deserialize<DatedAdmissionPayload>(Assert.IsType<string>(record?.SuccessPayloadJson))!;

    private static DatedFastLanePayload FastLanePayload(DatedResultRecord? record) =>
        JsonSerializer.Deserialize<DatedFastLanePayload>(Assert.IsType<string>(record?.SuccessPayloadJson))!;

    private async Task VerifyDuringOperatingHoursAsync(Env env)
    {
        Assert.Equal(RetrievalStatus.Verified, (await env.Admission.ExecuteAsync(Today, CancellationToken.None)).Status);
        Assert.Equal(RetrievalStatus.Verified, (await env.FastLane.ExecuteAsync(Today, CancellationToken.None)).Status);
    }

    private static void SwitchToAfterClosing(Env env)
    {
        env.Clock.UtcNow = AfterClosing;
        env.AdmissionHttp.Responder = _ => Fixture("dated-admission/after-closing-window.json");
        env.FastLaneHttp.Responder = FastLaneAfterClosing();
    }

    [Fact]
    public async Task AfterClosing_UnavailableAnswer_KeepsTheDailyAdmissionPricesVerifiedEarlier()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        SwitchToAfterClosing(env);

        var result = await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        var record = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today));

        // The official answer is still reported honestly as a successful "not offered" check…
        Assert.Equal(SourceOperationOutcomeKind.Success, result.Outcome);
        Assert.Equal(RetrievalStatus.Unavailable, result.Status);
        Assert.Equal(RetrievalStatus.Unavailable, record!.LastAttemptEvidence.Status);
        Assert.Equal(AfterClosing, record.LastAttemptEvidence.TimeChecked);
        // …but the prices verified at 3:10 PM, with their original evidence, are not erased.
        Assert.Equal(RetrievalStatus.Verified, record.SuccessEvidence!.Status);
        Assert.Equal(VerifiedAt, record.SuccessEvidence.TimeChecked);
        Assert.Equal(Today, record.SuccessEvidence.ConfirmedDate);
        var product = Assert.Single(AdmissionPayload(record).Products);
        Assert.Equal("Park Ticket", product.DisplayName);
        Assert.Equal(49.00m, product.ExactDatePrice);
        Assert.Equal(Today, product.ApplicableDate);
    }

    [Fact]
    public async Task AfterClosing_UnavailableAnswer_KeepsTheFastLanePricesVerifiedEarlier()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        SwitchToAfterClosing(env);

        var result = await env.FastLane.ExecuteAsync(Today, CancellationToken.None);
        var record = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedFastLane, Today));

        Assert.Equal(RetrievalStatus.Unavailable, result.Status);
        Assert.Equal(RetrievalStatus.Unavailable, record!.LastAttemptEvidence.Status);
        Assert.Equal(VerifiedAt, record.SuccessEvidence!.TimeChecked);
        var prices = FastLanePayload(record).Products.Select(p => (p.DisplayName, p.ExactDatePrice)).OrderBy(p => p.DisplayName).ToList();
        Assert.Equal([("FAST LANE RESERVE", 49.00m), ("FAST LANE ULTIMATE", 85.00m)], prices);
    }

    [Fact]
    public async Task AfterClosing_RepeatedAutomaticRefreshes_KeepTheOriginalVerification()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        SwitchToAfterClosing(env);

        await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        env.Clock.UtcNow = AfterClosing.AddMinutes(90); // 11:45 PM — still the same Eastern date
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);

        var record = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today));
        Assert.Equal(VerifiedAt, record!.SuccessEvidence!.TimeChecked);
        Assert.Equal(AfterClosing.AddMinutes(90), record.LastAttemptEvidence.TimeChecked);
        Assert.Single(AdmissionPayload(record).Products);
    }

    [Fact]
    public async Task AfterClosing_RetainedPrices_SurviveAnAppRestart()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        SwitchToAfterClosing(env);
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        await env.FastLane.ExecuteAsync(Today, CancellationToken.None);

        var reopened = new SqliteDatedResultRepository(_fixture.ReopenConnectionFactory());
        var admission = await reopened.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today));
        var fastLane = await reopened.GetAsync(new DatedResultKey(SourceCategory.DatedFastLane, Today));

        Assert.Equal(49.00m, Assert.Single(AdmissionPayload(admission).Products).ExactDatePrice);
        Assert.Equal(2, FastLanePayload(fastLane).Products.Count);
        Assert.Equal(RetrievalStatus.Unavailable, admission!.LastAttemptEvidence.Status);
    }

    [Fact]
    public async Task AfterClosing_FailedRefresh_StillKeepsVerifiedPrices_AndRecordsTheFailure()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        env.Clock.UtcNow = AfterClosing;
        env.AdmissionHttp.Failure = new HttpRequestException("Network unreachable.");

        var result = await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        var record = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today));

        Assert.Equal(SourceOperationOutcomeKind.Failure, result.Outcome);
        Assert.Equal(RetrievalStatus.Error, record!.LastAttemptEvidence.Status);
        Assert.Equal(VerifiedAt, record.SuccessEvidence!.TimeChecked);
        Assert.Single(AdmissionPayload(record).Products);
    }

    [Fact]
    public async Task AfterClosing_TheNextOperatingDateIsStillRetrievedNormally()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        SwitchToAfterClosing(env);

        var result = await env.Admission.ExecuteAsync(Tomorrow, CancellationToken.None);
        var tomorrow = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Tomorrow));

        Assert.Equal(RetrievalStatus.Verified, result.Status);
        Assert.Equal(AfterClosing, tomorrow!.SuccessEvidence!.TimeChecked);
        Assert.Equal(3, AdmissionPayload(tomorrow).Products.Count); // three names, each with a standard and comparison price
        Assert.All(AdmissionPayload(tomorrow).Products, p => Assert.Equal(Tomorrow, p.ApplicableDate));
    }

    [Fact]
    public async Task CheckOnlineForAPastDate_DoesNotEraseThatDatesRecordedPrices()
    {
        var env = Build();
        await VerifyDuringOperatingHoursAsync(env);
        env.Clock.UtcNow = new DateTimeOffset(2026, 8, 21, 14, 0, 0, TimeSpan.Zero); // the next morning

        var result = await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        var record = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today));

        Assert.Equal(RetrievalStatus.Unavailable, result.Status);
        Assert.Equal(1, env.AdmissionHttp.Posts); // only the 3:10 PM call; a past date never goes online
        Assert.Equal(VerifiedAt, record!.SuccessEvidence!.TimeChecked);
        Assert.Single(AdmissionPayload(record).Products);
    }

    [Fact]
    public async Task NeverVerifiedDate_UnavailableAnswer_StillRecordsNoPrices()
    {
        var env = Build();
        env.Clock.UtcNow = AfterClosing;
        env.AdmissionHttp.Responder = _ => Fixture("dated-admission/after-closing-window.json");

        await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        var record = await env.Repository.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today));

        Assert.Equal(RetrievalStatus.Unavailable, record!.SuccessEvidence!.Status);
        Assert.Null(record.SuccessPayloadJson);
    }

    [Theory]
    [InlineData(RetrievalStatus.Verified)]
    [InlineData(RetrievalStatus.Closed)]
    [InlineData(RetrievalStatus.Error)]
    public async Task RecordNoOfferAttemptRetainingVerified_RejectsAnyOtherStatus(RetrievalStatus status)
    {
        var repository = new SqliteDatedResultRepository(_fixture.ConnectionFactory);
        var evidence = new SourceEvidence("Official", new Uri("https://example.test/official"), AfterClosing, status, "Detail.", RetrievalStrategy.DirectApi, Today, confirmedDate: status == RetrievalStatus.Verified ? Today : null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.RecordNoOfferAttemptRetainingVerifiedAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today), evidence));
    }
}
