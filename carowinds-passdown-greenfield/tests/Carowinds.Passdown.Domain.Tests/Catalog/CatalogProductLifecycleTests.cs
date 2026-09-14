using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

public class CatalogProductLifecycleTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/passes");
    private static readonly CatalogProductKey Key =
        CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass");

    private static readonly DateTimeOffset Day1 = new(2026, 8, 18, 6, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Day2 = new(2026, 8, 19, 6, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Day3 = new(2026, 8, 20, 6, 0, 0, TimeSpan.FromHours(-4));

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset timeChecked) => new(
        sourceName: "Season passes",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: status,
        detail: status == RetrievalStatus.Error ? "Timed out." : "Checked successfully.",
        strategy: RetrievalStrategy.Html);

    [Fact]
    public void FirstObserved_RequiresAVerifiedRetrieval()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Error, Day1)));

        Assert.Contains("verified retrieval", exception.Message);
    }

    [Fact]
    public void FirstObserved_StartsActiveWithZeroAbsences()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));

        Assert.Equal(ProductLifecycleState.Active, product.State);
        Assert.Equal(0, product.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(Day1, product.FirstSeenAt);
        Assert.Equal(Day1, product.LastVerifiedAt);
    }

    [Fact]
    public void ConfirmAbsent_ErrorEvidenceIsRejected_NeverCountsAsAbsence()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));

        var exception = Assert.Throws<ArgumentException>(
            () => product.ConfirmAbsent(Evidence(RetrievalStatus.Error, Day2)));

        Assert.Contains("must never count as confirmed product absence", exception.Message);
        // The rejected attempt must not have mutated any prior state (immutability).
        Assert.Equal(ProductLifecycleState.Active, product.State);
        Assert.Equal(0, product.ConsecutiveConfirmedAbsenceCount);
    }

    [Fact]
    public void ConfirmAbsent_FirstSuccessfulAbsenceMovesToPendingConfirmation_NotYetArchived()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));

        var afterFirstMiss = product.ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day2));

        Assert.Equal(ProductLifecycleState.PendingConfirmation, afterFirstMiss.State);
        Assert.Equal(1, afterFirstMiss.ConsecutiveConfirmedAbsenceCount);
        Assert.Null(afterFirstMiss.ArchivedAt);
    }

    [Fact]
    public void ConfirmAbsent_SecondConsecutiveSuccessfulAbsenceArchives()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1))
            .ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day2));

        var afterSecondMiss = product.ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day3));

        Assert.Equal(ProductLifecycleState.Archived, afterSecondMiss.State);
        Assert.Equal(2, afterSecondMiss.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(Day3, afterSecondMiss.ArchivedAt);
        Assert.NotNull(afterSecondMiss.ArchivedReason);
    }

    [Fact]
    public void ConfirmPresent_BetweenTwoMissesResetsTheAbsenceCount()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1))
            .ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day2));

        var recovered = product.ConfirmPresent(Evidence(RetrievalStatus.Verified, Day3));

        Assert.Equal(ProductLifecycleState.Active, recovered.State);
        Assert.Equal(0, recovered.ConsecutiveConfirmedAbsenceCount);
    }

    [Fact]
    public void ConfirmPresent_ReactivatesAnArchivedProduct()
    {
        var archived = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1))
            .ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day2))
            .ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day3));
        Assert.Equal(ProductLifecycleState.Archived, archived.State);

        var reactivated = archived.ConfirmPresent(Evidence(RetrievalStatus.Verified, Day3.AddDays(30)));

        Assert.Equal(ProductLifecycleState.Active, reactivated.State);
        Assert.Null(reactivated.ArchivedAt);
        Assert.Null(reactivated.ArchivedReason);
        Assert.Equal(0, reactivated.ConsecutiveConfirmedAbsenceCount);
    }

    [Fact]
    public void MarkExplicitlyUnavailable_ArchivesImmediatelyWithoutAConfirmationRetry()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));

        var archived = product.MarkExplicitlyUnavailable(
            Evidence(RetrievalStatus.Unavailable, Day2), "Product page marked expired.");

        Assert.Equal(ProductLifecycleState.Archived, archived.State);
        Assert.Equal(Day2, archived.ArchivedAt);
        Assert.Equal("Product page marked expired.", archived.ArchivedReason);
    }

    [Fact]
    public void MarkExplicitlyUnavailable_ErrorEvidenceIsRejected()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));

        Assert.Throws<ArgumentException>(
            () => product.MarkExplicitlyUnavailable(Evidence(RetrievalStatus.Error, Day2), "reason"));
    }

    // Phase 17A §2/§22: end-date semantics are inclusive — the product is still valid ON its own
    // valid-through date, expiring only the day after.
    [Fact]
    public void MarkExpired_OnTheValidThroughDateItself_IsRejected_StillValidThatDay()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));
        var validThrough = DateOnly.FromDateTime(Day2.DateTime);

        var exception = Assert.Throws<ArgumentException>(
            () => product.MarkExpired(Evidence(RetrievalStatus.Verified, Day2), validThrough));

        Assert.Contains("has not yet passed", exception.Message);
    }

    [Fact]
    public void MarkExpired_TheDayAfterTheValidThroughDate_ArchivesImmediately()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));
        var validThrough = DateOnly.FromDateTime(Day2.DateTime);

        var expired = product.MarkExpired(Evidence(RetrievalStatus.Verified, Day3), validThrough);

        Assert.Equal(ProductLifecycleState.Expired, expired.State);
        Assert.Equal(Day3, expired.ArchivedAt);
        Assert.Equal(0, expired.ConsecutiveConfirmedAbsenceCount); // no confirmation retries needed, mirrors MarkExplicitlyUnavailable
        Assert.Contains(validThrough.ToString("yyyy-MM-dd"), expired.ArchivedReason);
    }

    [Fact]
    public void MarkExpired_ErrorEvidenceIsRejected_NeverEvidenceOfExpiration()
    {
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));
        var validThrough = DateOnly.FromDateTime(Day1.DateTime);

        Assert.Throws<ArgumentException>(
            () => product.MarkExpired(Evidence(RetrievalStatus.Error, Day3), validThrough));
    }

    [Fact]
    public void MarkExpired_IsDistinctFromConfirmedAbsence_NoAbsenceRetryNeeded()
    {
        // A product that is still observed on the page (never missing at all) can still expire purely
        // from its own valid-through date passing — this never routes through ConfirmAbsent's
        // two-check delay.
        var product = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1));
        var validThrough = DateOnly.FromDateTime(Day1.DateTime);

        var expired = product.MarkExpired(Evidence(RetrievalStatus.Verified, Day2), validThrough);

        Assert.NotEqual(ProductLifecycleState.PendingConfirmation, expired.State);
        Assert.Equal(ProductLifecycleState.Expired, expired.State);
    }

    [Fact]
    public void Rehydrate_ReproducesEveryPersistedField()
    {
        var evidence = Evidence(RetrievalStatus.Verified, Day2);

        var rehydrated = CatalogProductLifecycle.Rehydrate(
            Key,
            ProductLifecycleState.PendingConfirmation,
            firstSeenAt: Day1,
            lastSeenAt: Day1,
            lastVerifiedAt: Day1,
            consecutiveConfirmedAbsenceCount: 1,
            archivedAt: null,
            archivedReason: null,
            lastEvidence: evidence);

        Assert.Equal(Key, rehydrated.Key);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, rehydrated.State);
        Assert.Equal(Day1, rehydrated.FirstSeenAt);
        Assert.Equal(Day1, rehydrated.LastSeenAt);
        Assert.Equal(Day1, rehydrated.LastVerifiedAt);
        Assert.Equal(1, rehydrated.ConsecutiveConfirmedAbsenceCount);
        Assert.Null(rehydrated.ArchivedAt);
        Assert.Null(rehydrated.ArchivedReason);
        Assert.Equal(evidence, rehydrated.LastEvidence);
    }

    [Fact]
    public void Rehydrate_ThenConfirmAbsentASecondTime_ArchivesJustLikeAFreshlyBuiltInstance()
    {
        // A rehydrated PendingConfirmation instance (absence count 1) must behave
        // identically to one built by walking the real transitions — proving
        // Rehydrate is a faithful reconstruction, not a shortcut that skips rules.
        var walked = CatalogProductLifecycle.FirstObserved(Key, Evidence(RetrievalStatus.Verified, Day1))
            .ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day2));

        var rehydrated = CatalogProductLifecycle.Rehydrate(
            Key,
            walked.State,
            walked.FirstSeenAt,
            walked.LastSeenAt,
            walked.LastVerifiedAt,
            walked.ConsecutiveConfirmedAbsenceCount,
            walked.ArchivedAt,
            walked.ArchivedReason,
            walked.LastEvidence);

        var secondMissFromWalked = walked.ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day3));
        var secondMissFromRehydrated = rehydrated.ConfirmAbsent(Evidence(RetrievalStatus.Verified, Day3));

        Assert.Equal(ProductLifecycleState.Archived, secondMissFromWalked.State);
        Assert.Equal(secondMissFromWalked.State, secondMissFromRehydrated.State);
        Assert.Equal(secondMissFromWalked.ArchivedReason, secondMissFromRehydrated.ArchivedReason);
    }

    [Fact]
    public void Rehydrate_RejectsNullKey()
    {
        Assert.Throws<ArgumentNullException>(() => CatalogProductLifecycle.Rehydrate(
            null!, ProductLifecycleState.Active, Day1, Day1, Day1, 0, null, null, Evidence(RetrievalStatus.Verified, Day1)));
    }
}
