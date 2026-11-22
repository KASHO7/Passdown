using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.PassPerks;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.PassPerks;

/// <summary>
/// Phase 32: the cached Pass Perks document (history, changed terms, verified removal versus incomplete page),
/// the refresh registration's last-known-good behavior, and the month/Calendar views built from the cache.
/// Perks mirror the official November 2026 Carowinds list.
/// </summary>
public class Phase32PassPerksTests
{
    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Nov21 = new(2026, 11, 21, 7, 0, 0, Eastern);
    private static readonly Uri Source = new("https://www.sixflags.com/carowinds/perks-and-play");
    private static readonly PassPerkCampaign November = new(2026, 11, "November Perks", YearInferred: true);
    private static readonly PassPerkCampaign December = new(2026, 12, "December Perks", YearInferred: true);

    private static OfficialPassPerk Perk(PassPerkCampaign campaign, string title, string? dateLine, string body = "Passholders and members can redeem one (1).", bool monthly = true) =>
        PassPerkText.Interpret(title, dateLine, [body], campaign, Source, Nov21, monthly);

    private static OfficialPassPerk Pretzel => Perk(November, "BOGO 50% Off Soft Pretzel", "November 1 - November 30");

    private static OfficialPassPerk Mug => Perk(November, "Buy Two, Get One FREE Hot Chocolate Mug", "November 27 - January 3");

    private static OfficialPassPerk Bring => Perk(November, "Earn 2 FREE Bring-A-Friend Tickets", null,
        "Visit twice between November 1 and November 22 to receive 2 FREE Bring-A-Friend Tickets, valid December 5 through January 3.", monthly: false);

    private static PassPerksObservation Observe(PassPerkCampaign campaign, params OfficialPassPerk[] perks) => new(campaign, perks);

    private static SourceEvidence Verified(DateTimeOffset at) =>
        new("Carowinds Perks & Play", Source, at, RetrievalStatus.Verified, "November Perks: verified.", RetrievalStrategy.Html);

    private static SourceEvidence Failed(DateTimeOffset at) =>
        new("Carowinds Perks & Play", Source, at, RetrievalStatus.Error, "We couldn't check the official Pass Perks right now.", RetrievalStrategy.Html, technicalDetail: "HTTP 503");

    [Fact]
    public void FirstObservation_StoresEveryPerkAndTheCampaign()
    {
        var document = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug, Bring), Nov21);

        Assert.Equal(3, document.Perks.Count);
        Assert.All(document.Perks, p => Assert.Equal((Nov21, Nov21), (p.FirstSeenAt, p.LastVerifiedAt)));
        Assert.Equal("2026-11", document.LatestCampaignKey);
        Assert.Single(document.Campaigns);
    }

    [Fact]
    public void ChangedTerms_KeepThePreviousWordingWithProvenance()
    {
        var first = PassPerksDocument.Empty.Merge(Observe(November, Pretzel), Nov21);
        var changed = Perk(November, "BOGO 50% Off Soft Pretzel", "November 1 - November 15");

        var second = first.Merge(Observe(November, changed), Nov21.AddDays(1));

        var perk = Assert.Single(second.Perks);
        Assert.Equal("November 1 - November 15", perk.DateText);
        Assert.Equal(Nov21, perk.FirstSeenAt);
        var previous = Assert.Single(perk.PreviousVersions);
        Assert.Equal("November 1 - November 30", previous.DateText);
        Assert.Equal(Nov21, previous.LastVerifiedAt);

        // The same wording again adds no version.
        var third = second.Merge(Observe(November, changed), Nov21.AddDays(2));
        Assert.Single(third.Perks[0].PreviousVersions);
    }

    [Fact]
    public void CompleteCheckWithoutAPerk_IsAVerifiedRemoval_KeptForHistory()
    {
        var first = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug), Nov21);

        var second = first.Merge(Observe(November, Pretzel), Nov21.AddDays(1));

        var mug = second.Perks.Single(p => p.Title == Mug.Title);
        Assert.Equal(Nov21.AddDays(1), mug.NoLongerListedSince);
        Assert.Equal(Nov21, mug.LastVerifiedAt); // provenance of the previous listing is kept

        // Listed again later: current again, removal cleared.
        var third = second.Merge(Observe(November, Pretzel, Mug), Nov21.AddDays(2));
        Assert.Null(third.Perks.Single(p => p.Title == Mug.Title).NoLongerListedSince);
    }

    [Fact]
    public void IncompleteCheck_NeverRemovesAPerk()
    {
        var first = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug), Nov21);

        var partial = Observe(November, Pretzel) with { UnreadableCount = 1 };
        var second = first.Merge(partial, Nov21.AddDays(1));

        var mug = second.Perks.Single(p => p.Title == Mug.Title);
        Assert.Null(mug.NoLongerListedSince);
        Assert.True(mug.LastCheckIncomplete);
        Assert.Equal(PassPerkStatus.NotReconfirmed, PassPerkStatusEvaluator.Evaluate(mug, new DateOnly(2026, 11, 22), Nov21.AddDays(1)).Status);
    }

    [Fact]
    public void NewMonth_KeepsTheOldMonthAsHistory_NotAsCurrent()
    {
        var november = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug), Nov21);
        var decemberCheck = new DateTimeOffset(2026, 12, 1, 7, 0, 0, Eastern);
        var churro = Perk(December, "BOGO 50% Off Churro", "December 1 - December 31");

        var document = november.Merge(Observe(December, churro), decemberCheck);

        Assert.Equal("2026-12", document.LatestCampaignKey);
        Assert.Equal(["2026-11", "2026-12"], document.Campaigns.Select(c => c.Campaign.Key));
        var overview = new PassPerksOverview(document, OfficialDocumentStatus.NotChecked, new DateOnly(2026, 12, 1), decemberCheck);
        Assert.All(overview.ForMonth(2026, 11).Entries, e => Assert.Equal(PassPerkStatus.NoLongerListed, e.Assessment.Status));
        Assert.Equal(PassPerkStatus.Current, Assert.Single(overview.ForMonth(2026, 12).Entries).Assessment.Status);
        Assert.True(overview.ForMonth(2026, 12).IsLatestCampaign);
        Assert.False(overview.ForMonth(2026, 11).IsLatestCampaign);
    }

    [Fact]
    public void HistoryRetention_DropsVeryOldRemovedPerks()
    {
        var first = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug), Nov21);

        var muchLater = first.Merge(Observe(December, Perk(December, "BOGO 50% Off Churro", "December 1 - December 31")), Nov21.AddDays(401));

        Assert.Equal(["BOGO 50% Off Churro"], muchLater.Perks.Select(p => p.Title));
    }

    [Fact]
    public void ProgramTerms_MissingPagesKeepTheirLastVerifiedSentences()
    {
        var full = new PassPerksProgramTerms { EligibilityStatement = "VALID FOR REGULAR, GOLD, AND PRESTIGE.", FaqEligibilityAnswer = "All Season Passholders.", LoadingNote = "Loaded on the first day of the month." };
        var first = PassPerksDocument.Empty.Merge(Observe(November, Pretzel) with { ProgramTerms = full }, Nov21);

        var termsFailed = new PassPerksProgramTerms { LoadingNote = "Loaded on the first day of the month." };
        var second = first.Merge(Observe(November, Pretzel) with { ProgramTerms = termsFailed }, Nov21.AddDays(1));

        Assert.Equal(full.EligibilityStatement, second.ProgramTerms!.EligibilityStatement);
        Assert.Equal(full.FaqEligibilityAnswer, second.ProgramTerms.FaqEligibilityAnswer);
    }

    [Fact]
    public async Task Registration_VerifiedMerges_FailureRecordsOnlyTheAttempt()
    {
        var documents = new InMemoryOfficialReferenceDocumentRepository();

        async Task<SourceOperationResult> RunAsync(SourceResult<PassPerksObservation> result) =>
            await new PassPerksSourceRegistration(
                    new FakeOfficialDataSource<PassPerksObservation>("pass-perks.official", SourceCategory.PassPerks, (_, _) => Task.FromResult(result)),
                    documents)
                .ExecuteAsync(null, CancellationToken.None);

        var ok = await RunAsync(SourceResult<PassPerksObservation>.Success("pass-perks.official", Verified(Nov21), Observe(November, Pretzel, Mug, Bring)));
        var failed = await RunAsync(SourceResult<PassPerksObservation>.WithoutPayload("pass-perks.official", Failed(Nov21.AddDays(1))));

        Assert.Equal(SourceOperationOutcomeKind.Success, ok.Outcome);
        Assert.Equal(SourceOperationOutcomeKind.Failure, failed.Outcome);
        var record = await documents.GetAsync(PassPerksDocumentIds.PassPerks);
        Assert.True(record!.LastAttemptFailed);
        var stored = OfficialSeasonJson.TryDeserialize<PassPerksDocument>(record.SuccessPayloadJson);
        Assert.Equal(3, stored!.Perks.Count); // last-known-good untouched

        var overview = await new PassPerksService(documents, new FakeClock(Nov21.AddDays(1).ToUniversalTime())).GetOverviewAsync();
        Assert.Equal(OfficialDocumentFreshness.LastCheckFailed, overview.Status.Freshness);
        Assert.Equal(3, overview.ForMonth(2026, 11).Entries.Count);
    }

    [Fact]
    public async Task Registration_VerifiedButEmpty_IsNeverStoredAsAnEmptyMonth()
    {
        var documents = new InMemoryOfficialReferenceDocumentRepository();
        var registration = new PassPerksSourceRegistration(
            new FakeOfficialDataSource<PassPerksObservation>("pass-perks.official", SourceCategory.PassPerks, (_, _) =>
                Task.FromResult(SourceResult<PassPerksObservation>.Success("pass-perks.official", Verified(Nov21), Observe(November)))),
            documents);

        var result = await registration.ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(SourceOperationOutcomeKind.Failure, result.Outcome);
        Assert.Null((await documents.GetAsync(PassPerksDocumentIds.PassPerks))!.SuccessPayloadJson);
    }

    [Fact]
    public async Task Registration_RejectsARequestedDate() =>
        await Assert.ThrowsAsync<ArgumentException>(() => new PassPerksSourceRegistration(
                new FakeOfficialDataSource<PassPerksObservation>("pass-perks.official", SourceCategory.PassPerks, (_, _) => throw new InvalidOperationException("no request expected")),
                new InMemoryOfficialReferenceDocumentRepository())
            .ExecuteAsync(new DateOnly(2026, 11, 21), CancellationToken.None));

    [Fact]
    public async Task Service_NeverChecked_IsNotVerified_NotEmpty()
    {
        var overview = await new PassPerksService(new InMemoryOfficialReferenceDocumentRepository(), new FakeClock(Nov21.ToUniversalTime())).GetOverviewAsync();

        Assert.False(overview.HasVerifiedData);
        Assert.Equal(OfficialDocumentFreshness.NotChecked, overview.Status.Freshness);
        var month = overview.ForMonth(2026, 11);
        Assert.False(month.WasObserved);
        Assert.Empty(month.Entries);
        Assert.Equal([new DateOnly(2026, 11, 1)], overview.AvailableMonths);
    }

    [Fact]
    public void Overview_UnpublishedMonth_IsNotObserved_AndHistoricalMonthsAreSelectable()
    {
        var document = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug, Bring), Nov21);

        var overview = new PassPerksOverview(document, OfficialDocumentStatus.NotChecked, new DateOnly(2026, 12, 2), new DateTimeOffset(2026, 12, 2, 9, 0, 0, Eastern));

        Assert.False(overview.ForMonth(2026, 12).WasObserved); // December not published yet: "not verified", never "none"
        Assert.Equal([new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 1)], overview.AvailableMonths);
        Assert.True(overview.ForMonth(2026, 11).WasObserved);
    }

    [Fact]
    public void Overview_CalendarEntriesAndMonthLongPerks()
    {
        var document = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug, Bring), Nov21);
        var overview = new PassPerksOverview(document, OfficialDocumentStatus.NotChecked, new DateOnly(2026, 11, 21), Nov21.AddHours(1));

        var entries = overview.CalendarEntries(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(
            [(new DateOnly(2026, 11, 22), "Earn 2 FREE Bring-A-Friend Tickets"), (new DateOnly(2026, 11, 27), "Buy Two, Get One FREE Hot Chocolate Mug"), (new DateOnly(2026, 12, 5), "Earn 2 FREE Bring-A-Friend Tickets")],
            entries.Select(e => (e.Entry.Date, e.Entry.Perk.Title)));
        Assert.Equal(["BOGO 50% Off Soft Pretzel"], overview.MonthLongEntries(2026, 11).Select(e => e.Perk.Title));
    }

    [Fact]
    public void Document_RoundTripsThroughJson()
    {
        var document = PassPerksDocument.Empty.Merge(Observe(November, Pretzel, Mug, Bring) with
        {
            ProgramTerms = new PassPerksProgramTerms { TermsUpdatedText = "Updated July 30, 2026", DefinedRewardTypes = ["Visit-Based Rewards"] },
        }, Nov21);

        var json = OfficialSeasonJson.Serialize(document);
        var back = OfficialSeasonJson.TryDeserialize<PassPerksDocument>(json)!;

        Assert.Equal(json, OfficialSeasonJson.Serialize(back));
        var bring = back.Perks.Single(p => p.RewardType == PassPerkRewardType.VisitBased);
        Assert.Equal(new DateOnly(2027, 1, 3), bring.Redemption!.End);
        Assert.Equal(2, bring.RequiredVisits);
        Assert.Null(OfficialSeasonJson.TryDeserialize<PassPerksDocument>("{broken"));
    }
}
