using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

/// <summary>Phase 31B: "retired" is only asserted on reliable evidence; dedicated-channel products are unconfirmed instead.</summary>
public class Phase31BProductAvailabilityTests
{
    private static readonly CatalogProductKey MilitaryGold = CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027");

    private static SourceEvidence Verified(DateTimeOffset at) =>
        new("Carowinds Military & First Responder Tickets", new Uri("https://www.sixflags.com/carowinds/military-discounts"), at,
            RetrievalStatus.Verified, "checked", RetrievalStrategy.Html, requestedDate: null);

    private static SourceEvidence Failed(DateTimeOffset at) =>
        new("Carowinds Military & First Responder Tickets", new Uri("https://www.sixflags.com/carowinds/military-discounts"), at,
            RetrievalStatus.Error, "failed", RetrievalStrategy.Html, requestedDate: null);

    private static readonly DateTimeOffset Day1 = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void MilitaryPassArchivedOnlyByAbsence_IsAvailabilityUnconfirmed_NotRetired()
    {
        var lifecycle = CatalogProductLifecycle.FirstObserved(MilitaryGold, Verified(Day1))
            .ConfirmAbsent(Verified(Day1.AddDays(1)))
            .ConfirmAbsent(Verified(Day1.AddDays(2)));
        Assert.Equal(ProductLifecycleState.Archived, lifecycle.State);

        var assessment = ProductAvailabilityClassifier.Classify(lifecycle.State, SourceCategory.MilitaryFirstResponder, lifecycle.ArchivedReason);

        Assert.Equal(ProductAvailability.Unconfirmed, assessment.Availability);
        Assert.Equal("Availability unconfirmed", assessment.Label);
        Assert.Contains("ID.me", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATemporaryOmission_OneMissingCheck_IsUnconfirmed_AndTheProductReactivatesWithItsIdentity()
    {
        var pending = CatalogProductLifecycle.FirstObserved(MilitaryGold, Verified(Day1)).ConfirmAbsent(Verified(Day1.AddDays(1)));
        Assert.Equal(ProductAvailability.Unconfirmed, ProductAvailabilityClassifier.Classify(pending.State, SourceCategory.MilitaryFirstResponder, pending.ArchivedReason).Availability);

        var back = pending.ConfirmPresent(Verified(Day1.AddDays(2)));
        Assert.Equal(ProductAvailability.Available, ProductAvailabilityClassifier.Classify(back.State, SourceCategory.MilitaryFirstResponder, back.ArchivedReason).Availability);
        Assert.Equal(MilitaryGold, back.Key);
        Assert.Equal(Day1, back.FirstSeenAt);
    }

    [Fact]
    public void FailedChecks_NeverAdvanceTowardRetirement()
    {
        var lifecycle = CatalogProductLifecycle.FirstObserved(MilitaryGold, Verified(Day1));
        Assert.Throws<ArgumentException>(() => lifecycle.ConfirmAbsent(Failed(Day1.AddDays(1))));
        Assert.Equal(ProductAvailability.Available, ProductAvailabilityClassifier.Classify(lifecycle.State, SourceCategory.MilitaryFirstResponder, null).Availability);
    }

    [Fact]
    public void AnExplicitlyUnavailableMilitaryProduct_IsRetired()
    {
        var lifecycle = CatalogProductLifecycle.FirstObserved(MilitaryGold, Verified(Day1))
            .MarkExplicitlyUnavailable(Verified(Day1.AddDays(1)), "Sold out on the official page.");

        var assessment = ProductAvailabilityClassifier.Classify(lifecycle.State, SourceCategory.MilitaryFirstResponder, lifecycle.ArchivedReason);

        Assert.Equal(ProductAvailability.Retired, assessment.Availability);
        Assert.Equal("Sold out on the official page.", assessment.Reason);
    }

    [Fact]
    public void AGeneralCatalogProductArchivedByRepeatedAbsence_IsRetired()
    {
        var key = CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "gold-2026");
        var lifecycle = CatalogProductLifecycle.FirstObserved(key, Verified(Day1)).ConfirmAbsent(Verified(Day1.AddDays(1))).ConfirmAbsent(Verified(Day1.AddDays(2)));

        Assert.Equal(ProductAvailability.Retired, ProductAvailabilityClassifier.Classify(lifecycle.State, SourceCategory.SeasonPasses, lifecycle.ArchivedReason).Availability);
    }

    [Fact]
    public void AnExpiredProduct_IsExpired_EvenOnADedicatedChannel()
    {
        var lifecycle = CatalogProductLifecycle.FirstObserved(MilitaryGold, Verified(Day1)).MarkExpired(Verified(Day1.AddDays(10)), new DateOnly(2026, 11, 5));

        Assert.Equal(ProductAvailability.Expired, ProductAvailabilityClassifier.Classify(lifecycle.State, SourceCategory.MilitaryFirstResponder, lifecycle.ArchivedReason).Availability);
    }

    [Fact]
    public void DedicatedChannels_AreOnlyTheMilitaryAndFirstResponderCategories()
    {
        Assert.True(ProductAvailabilityClassifier.IsDedicatedVerifiedChannel(SourceCategory.MilitaryFirstResponder));
        Assert.True(ProductAvailabilityClassifier.IsDedicatedVerifiedChannel(SourceCategory.DatedMilitaryFirstResponder));
        Assert.False(ProductAvailabilityClassifier.IsDedicatedVerifiedChannel(SourceCategory.SeasonPasses));
        Assert.False(ProductAvailabilityClassifier.IsDedicatedVerifiedChannel(SourceCategory.Promotions));
    }
}
