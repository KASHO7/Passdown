using Carowinds.Passdown.Application.Associate;
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
/// Pre-RC.15 post-closing pricing retention (docs/pre-rc15-post-closing-pricing-retention.md) through
/// the real <see cref="DatedSourceRegistration{TPayload}"/> and <see cref="AssociatePassdownService"/>:
/// prices verified during operating hours stay in Products &amp; Pricing after the official calendar
/// stops listing the date, marked previously verified (never current), while genuine official changes
/// still replace them. "Today" is Friday 2026-11-27 (EST), the date of the Windows report.
/// </summary>
public class PostClosingPricingRetentionTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly DateOnly Tomorrow = new(2026, 11, 28);

    /// <summary>3:10 PM EST.</summary>
    private static readonly DateTimeOffset VerifiedAt = new(2026, 11, 27, 20, 10, 0, TimeSpan.Zero);

    /// <summary>10:30 PM EST — after closing, same Eastern date.</summary>
    private static readonly DateTimeOffset AfterClosing = new(2026, 11, 28, 3, 30, 0, TimeSpan.Zero);

    private static readonly Uri SourceUrl = new("https://example.test/official");

    private const string ParkTicketIdentity = "dated|DatedAdmissionTickets|park-1";

    private sealed class Script<TPayload>
    {
        public Func<RetrievalRequest, SourceResult<TPayload>>? Next { get; set; }
    }

    private sealed record Env(
        AssociatePassdownService Service,
        InMemoryDatedResultRepository Dated,
        InMemoryCatalogProductRepository Catalog,
        FakeClock Clock,
        DatedSourceRegistration<DatedAdmissionPayload> Admission,
        Script<DatedAdmissionPayload> AdmissionScript,
        DatedSourceRegistration<DatedFastLanePayload> FastLane,
        Script<DatedFastLanePayload> FastLaneScript,
        DatedSourceRegistration<ParkHoursPayload> ParkHours,
        Script<ParkHoursPayload> ParkHoursScript);

    private static Env Create()
    {
        var clock = new FakeClock(VerifiedAt);
        var dated = new InMemoryDatedResultRepository();
        var catalog = new InMemoryCatalogProductRepository();
        var drafts = new InMemoryAssociateDraftRepository();
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var service = new AssociatePassdownService(drafts, dated, catalog, new FakeRefreshOrchestrator(), notes, clock);

        var admissionScript = new Script<DatedAdmissionPayload>();
        var fastLaneScript = new Script<DatedFastLanePayload>();
        var hoursScript = new Script<ParkHoursPayload>();
        return new Env(
            service, dated, catalog, clock,
            new DatedSourceRegistration<DatedAdmissionPayload>(
                new FakeOfficialDataSource<DatedAdmissionPayload>("dated-admission.official", SourceCategory.DatedAdmissionTickets, (r, _) => Task.FromResult(admissionScript.Next!(r))), dated),
            admissionScript,
            new DatedSourceRegistration<DatedFastLanePayload>(
                new FakeOfficialDataSource<DatedFastLanePayload>("dated-fast-lane.official", SourceCategory.DatedFastLane, (r, _) => Task.FromResult(fastLaneScript.Next!(r))), dated),
            fastLaneScript,
            new DatedSourceRegistration<ParkHoursPayload>(
                new FakeOfficialDataSource<ParkHoursPayload>("park-hours.official", SourceCategory.ParkHours, (r, _) => Task.FromResult(hoursScript.Next!(r))), dated),
            hoursScript);
    }

    private static SourceEvidence Evidence(DateTimeOffset at, DateOnly date, RetrievalStatus status, string detail = "Detail.") =>
        new("Official", SourceUrl, at, status, detail, RetrievalStrategy.DirectApi, date, confirmedDate: status == RetrievalStatus.Verified ? date : null);

    private static Func<RetrievalRequest, SourceResult<DatedAdmissionPayload>> AdmissionVerified(Env env, decimal parkTicket) => r =>
        SourceResult<DatedAdmissionPayload>.Success("dated-admission.official", Evidence(env.Clock.UtcNow, r.RequestedDate!.Value, RetrievalStatus.Verified),
            new DatedAdmissionPayload([new DatedAdmissionProduct("park-1", "Park Ticket", parkTicket, null, null, r.RequestedDate!.Value)]));

    private static Func<RetrievalRequest, SourceResult<DatedFastLanePayload>> FastLaneVerified(Env env, params (string Id, string Name, decimal Price)[] products) => r =>
        SourceResult<DatedFastLanePayload>.Success("dated-fast-lane.official", Evidence(env.Clock.UtcNow, r.RequestedDate!.Value, RetrievalStatus.Verified),
            new DatedFastLanePayload(products.Select(p => new DatedFastLaneProduct(p.Id, p.Name, p.Price, null, null, r.RequestedDate!.Value)).ToList()));

    private static Func<RetrievalRequest, SourceResult<TPayload>> NoOffer<TPayload>(Env env, string sourceId, RetrievalStatus status = RetrievalStatus.Unavailable) => r =>
        SourceResult<TPayload>.WithoutPayload(sourceId, Evidence(env.Clock.UtcNow, r.RequestedDate!.Value, status, "Not available for this date."));

    private static async Task VerifyThenCloseAsync(Env env, RetrievalStatus closingAnswer = RetrievalStatus.Unavailable)
    {
        env.AdmissionScript.Next = AdmissionVerified(env, 64.99m);
        env.FastLaneScript.Next = FastLaneVerified(env, ("fl-reserve", "Fast Lane Reserve", 49m), ("fl-ultimate", "Fast Lane Ultimate", 85m));
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        await env.FastLane.ExecuteAsync(Today, CancellationToken.None);

        env.Clock.UtcNow = AfterClosing;
        env.AdmissionScript.Next = NoOffer<DatedAdmissionPayload>(env, "dated-admission.official", closingAnswer);
        env.FastLaneScript.Next = NoOffer<DatedFastLanePayload>(env, "dated-fast-lane.official", closingAnswer);
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        await env.FastLane.ExecuteAsync(Today, CancellationToken.None);
    }

    // ------------------------------------------------------------------ retention

    [Fact]
    public async Task AfterClosing_DailyAdmissionStaysListed_AsPreviouslyVerifiedWithItsOriginalTime()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var row = Assert.Single(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(ParkTicketIdentity, row.ProductIdentity);
        Assert.Equal(64.99m, row.OfficialPriceAmount);
        Assert.Equal(PriceKind.ExactDated, row.PriceKind);
        Assert.Equal(Today, row.ApplicableDate);
        Assert.Equal(AssociateVerificationState.PreviouslyVerified, row.Verification.State);
        Assert.NotEqual(AssociateVerificationState.Verified, row.Verification.State); // never presented as current
        Assert.Equal(VerifiedAt, row.Verification.LastVerifiedAt);
        Assert.Equal("Not available for this date.", row.Verification.Detail);
    }

    [Fact]
    public async Task AfterClosing_FastLaneStaysListed_AsPreviouslyVerified()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var rows = workspace.Products.Where(p => p.Category == SourceCategory.DatedFastLane).OrderBy(p => p.DisplayName).ToList();
        Assert.Equal(["Fast Lane Reserve", "Fast Lane Ultimate"], rows.Select(r => r.DisplayName));
        Assert.Equal([49m, 85m], rows.Select(r => r.OfficialPriceAmount!.Value));
        Assert.All(rows, r => Assert.Equal(AssociateVerificationState.PreviouslyVerified, r.Verification.State));
        Assert.All(rows, r => Assert.Equal(VerifiedAt, r.Verification.LastVerifiedAt));
    }

    [Fact]
    public async Task AfterClosing_NotPublishedAnswer_IsRetainedTheSameWay()
    {
        var env = Create();
        await VerifyThenCloseAsync(env, RetrievalStatus.NotPublished);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var row = Assert.Single(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(AssociateVerificationState.PreviouslyVerified, row.Verification.State);
    }

    [Fact]
    public async Task AfterClosing_SeasonPassesAreUnaffected()
    {
        var env = Create();
        await env.Catalog.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "gold-2027"), "2027 Gold Pass", ProductLifecycleState.Active, VerifiedAt, VerifiedAt, VerifiedAt,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: 99m, CurrentDisplayPrice: "$99", ComparisonPriceAmount: null, PriceQualifier: null,
            DateSpecific: false, ApplicableDate: null, LastEvidence: new SourceEvidence("Official", SourceUrl, VerifiedAt, RetrievalStatus.Verified, "Detail.", RetrievalStrategy.DirectApi)));
        await VerifyThenCloseAsync(env);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var pass = Assert.Single(workspace.Products, p => p.Category == SourceCategory.SeasonPasses);
        Assert.Equal(99m, pass.OfficialPriceAmount);
        Assert.Equal(AssociateVerificationState.Verified, pass.Verification.State);
    }

    [Fact]
    public async Task AfterClosing_ManualOverride_StaysTheEffectivePrice_AndOfficialPriceStaysSeparate()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [ParkTicketIdentity] = new(IncludeOnPdf: true, DisplayPriceOverride: 59m) },
        };
        await env.Service.SaveDraftAsync(Today, draft);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        var row = Assert.Single(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(59m, row.EffectiveDisplayPrice);
        Assert.Equal(64.99m, row.OfficialPriceAmount);
        Assert.Equal(AssociateVerificationState.PreviouslyVerified, row.Verification.State);
        Assert.Equal(64.99m, Assert.Single(System.Text.Json.JsonSerializer.Deserialize<DatedAdmissionPayload>(
            (await env.Dated.GetAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today)))!.SuccessPayloadJson!)!.Products).ExactDatePrice); // draft edits never touch the official cache
    }

    [Fact]
    public async Task AfterClosing_LoadingTheDate_DoesNotMutateTheDraft()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);
        var before = (await env.Service.LoadAssociateDateAsync(Today)).Draft;

        var after = (await env.Service.LoadAssociateDateAsync(Today)).Draft;

        Assert.Equal(before.ProductOverrides, after.ProductOverrides);
        Assert.Equal(before.SectionSelection, after.SectionSelection);
    }

    [Fact]
    public async Task NextMorning_HistoricalRule_StillShowsTheRetainedExactDatePrices()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);
        env.Clock.UtcNow = new DateTimeOffset(2026, 11, 28, 13, 0, 0, TimeSpan.Zero); // 8:00 AM EST, past 6:00 AM finalization

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        Assert.False(workspace.IsHistoricalPricingUnavailable);
        Assert.Contains(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets && p.OfficialPriceAmount == 64.99m);
        Assert.Equal(2, workspace.Products.Count(p => p.Category == SourceCategory.DatedFastLane));
    }

    [Fact]
    public async Task AfterClosing_TheNextOperatingDate_ShowsItsOwnCurrentPrices()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);
        env.AdmissionScript.Next = AdmissionVerified(env, 54.99m);
        await env.Admission.ExecuteAsync(Tomorrow, CancellationToken.None);

        var tomorrow = await env.Service.LoadAssociateDateAsync(Tomorrow);
        var today = await env.Service.LoadAssociateDateAsync(Today);

        var next = Assert.Single(tomorrow.Products, p => p.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(54.99m, next.OfficialPriceAmount);
        Assert.Equal(AssociateVerificationState.Verified, next.Verification.State);
        Assert.Equal(Tomorrow, next.ApplicableDate);
        Assert.Equal(64.99m, Assert.Single(today.Products, p => p.Category == SourceCategory.DatedAdmissionTickets).OfficialPriceAmount);
    }

    // ------------------------------------------------------------------ genuine official changes still apply

    [Theory]
    [InlineData(69.99)]
    [InlineData(59.99)]
    public async Task VerifiedPriceChange_ReplacesThePrice(double newPrice)
    {
        var env = Create();
        env.AdmissionScript.Next = AdmissionVerified(env, 64.99m);
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);
        env.Clock.UtcNow = VerifiedAt.AddHours(2);
        env.AdmissionScript.Next = AdmissionVerified(env, (decimal)newPrice);
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);

        var row = Assert.Single((await env.Service.LoadAssociateDateAsync(Today)).Products, p => p.Category == SourceCategory.DatedAdmissionTickets);

        Assert.Equal((decimal)newPrice, row.OfficialPriceAmount);
        Assert.Equal(AssociateVerificationState.Verified, row.Verification.State);
        Assert.Equal(VerifiedAt.AddHours(2), row.Verification.LastVerifiedAt);
    }

    [Fact]
    public async Task VerifiedProductWithdrawal_WhileTheDateIsStillOnSale_RemovesThatProduct()
    {
        var env = Create();
        env.FastLaneScript.Next = FastLaneVerified(env, ("fl-reserve", "Fast Lane Reserve", 49m), ("fl-ultimate", "Fast Lane Ultimate", 85m));
        await env.FastLane.ExecuteAsync(Today, CancellationToken.None);
        env.Clock.UtcNow = VerifiedAt.AddHours(1);
        env.FastLaneScript.Next = FastLaneVerified(env, ("fl-reserve", "Fast Lane Reserve", 49m));
        await env.FastLane.ExecuteAsync(Today, CancellationToken.None);

        var rows = (await env.Service.LoadAssociateDateAsync(Today)).Products.Where(p => p.Category == SourceCategory.DatedFastLane).ToList();

        Assert.Equal("Fast Lane Reserve", Assert.Single(rows).DisplayName);
        Assert.Equal(AssociateVerificationState.Verified, rows[0].Verification.State);
    }

    [Fact]
    public async Task SalesReopen_AfterRetention_NewVerifiedAnswerBecomesCurrentAgain()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);
        env.Clock.UtcNow = AfterClosing.AddMinutes(20);
        env.AdmissionScript.Next = AdmissionVerified(env, 62m);
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);

        var row = Assert.Single((await env.Service.LoadAssociateDateAsync(Today)).Products, p => p.Category == SourceCategory.DatedAdmissionTickets);

        Assert.Equal(62m, row.OfficialPriceAmount);
        Assert.Equal(AssociateVerificationState.Verified, row.Verification.State);
    }

    [Fact]
    public async Task FailedRefresh_AfterRetention_IsReportedStale_AndKeepsTheVerifiedPrice()
    {
        var env = Create();
        await VerifyThenCloseAsync(env);
        env.AdmissionScript.Next = r => SourceResult<DatedAdmissionPayload>.WithoutPayload(
            "dated-admission.official", Evidence(env.Clock.UtcNow, r.RequestedDate!.Value, RetrievalStatus.Error, "We couldn't check."));
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);

        var row = Assert.Single((await env.Service.LoadAssociateDateAsync(Today)).Products, p => p.Category == SourceCategory.DatedAdmissionTickets);

        Assert.Equal(AssociateVerificationState.Stale, row.Verification.State);
        Assert.Equal(64.99m, row.OfficialPriceAmount);
        Assert.Equal(VerifiedAt, row.Verification.LastVerifiedAt);
    }

    [Fact]
    public async Task NeverVerifiedDate_UnavailableAnswer_ShowsNoInventedPrice()
    {
        var env = Create();
        env.Clock.UtcNow = AfterClosing;
        env.AdmissionScript.Next = NoOffer<DatedAdmissionPayload>(env, "dated-admission.official");
        await env.Admission.ExecuteAsync(Today, CancellationToken.None);

        var workspace = await env.Service.LoadAssociateDateAsync(Today);

        Assert.DoesNotContain(workspace.Products, p => p.Category == SourceCategory.DatedAdmissionTickets);
        Assert.Equal(AssociateVerificationState.Unavailable, workspace.CategoryStatuses.Single(s => s.Category == SourceCategory.DatedAdmissionTickets).State);
    }

    // ------------------------------------------------------------------ scope bounds

    [Fact]
    public async Task ParkHours_AreOutsideTheRule_AndStillTakeTheLatestOfficialAnswer()
    {
        var env = Create();
        env.ParkHoursScript.Next = r => SourceResult<ParkHoursPayload>.Success("park-hours.official", Evidence(env.Clock.UtcNow, r.RequestedDate!.Value, RetrievalStatus.Verified),
            new ParkHoursPayload(new TimeOnly(17, 0), new TimeOnly(22, 0)));
        await env.ParkHours.ExecuteAsync(Today, CancellationToken.None);
        env.Clock.UtcNow = AfterClosing;
        env.ParkHoursScript.Next = NoOffer<ParkHoursPayload>(env, "park-hours.official");
        await env.ParkHours.ExecuteAsync(Today, CancellationToken.None);

        var record = await env.Dated.GetAsync(new DatedResultKey(SourceCategory.ParkHours, Today));

        Assert.Equal(RetrievalStatus.Unavailable, record!.SuccessEvidence!.Status);
        Assert.Null(record.SuccessPayloadJson);
    }

    [Fact]
    public void RetentionCategories_AreExactlyThePhase21AStoredObservationSources()
    {
        Assert.True(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.DatedAdmissionTickets));
        Assert.True(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.DatedFastLane));
        Assert.True(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.HauntedAttractions));
        Assert.True(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.DatedBringAFriend));
        Assert.True(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.BringAFriendDuo));
        Assert.True(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.DatedMilitaryFirstResponder));
        Assert.False(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.ParkHours));
        Assert.False(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.SeasonPasses));
        Assert.False(VerifiedDatedPriceRetention.AppliesTo(SourceCategory.Promotions));
    }

    [Theory]
    [InlineData(RetrievalStatus.Verified, true, false)]
    [InlineData(RetrievalStatus.Unavailable, false, true)]
    [InlineData(RetrievalStatus.NotPublished, false, true)]
    [InlineData(RetrievalStatus.Closed, false, false)]
    [InlineData(RetrievalStatus.Error, false, false)]
    public void ShouldRetain_OnlyForPayloadlessNoOfferAnswers(RetrievalStatus status, bool hasPayload, bool expected)
    {
        var key = new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today);
        var verified = Evidence(VerifiedAt, Today, RetrievalStatus.Verified);
        var existing = new DatedResultRecord(key, verified, "{}", verified);

        Assert.Equal(expected, VerifiedDatedPriceRetention.ShouldRetain(SourceCategory.DatedAdmissionTickets, existing, status, hasPayload));
    }

    [Fact]
    public void ShouldRetain_NeedsAStoredVerifiedPayload()
    {
        var key = new DatedResultKey(SourceCategory.DatedAdmissionTickets, Today);
        var unavailable = Evidence(VerifiedAt, Today, RetrievalStatus.Unavailable);

        Assert.False(VerifiedDatedPriceRetention.ShouldRetain(SourceCategory.DatedAdmissionTickets, null, RetrievalStatus.Unavailable, false));
        Assert.False(VerifiedDatedPriceRetention.ShouldRetain(SourceCategory.DatedAdmissionTickets, new DatedResultRecord(key, unavailable, null, unavailable), RetrievalStatus.Unavailable, false));
    }
}
