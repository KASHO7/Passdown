using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 26: observation identity, eligibility and incoming-record validation.</summary>
public class Phase26OfficialPriceObservationTests
{
    internal const string GoldPass = "SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2027";
    internal const string GoldRenewal = "SeasonPasses:pass-offer:carowinds|gold|season-pass|renewal|2027";
    internal const string GoldMembership = "SeasonPasses:pass-offer:carowinds|gold|membership|monthly";
    internal const string GoldInitiationFee = "SeasonPasses:pass-offer:carowinds|gold|membership|initiation-fee";
    internal const string SourceName = "Carowinds Memberships & Season Passes";

    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset April = new(2026, 4, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    internal static SeasonPriceHistoryInterval Interval(
        string key, int sequence, decimal amount, DateTimeOffset first, DateTimeOffset last,
        PriceKind kind = PriceKind.StandardCatalog, string name = "Gold Season Pass — 2027") =>
        new(key, sequence, amount, kind, kind == PriceKind.StartingFrom ? $"From ${amount}" : $"${amount}", kind == PriceKind.StartingFrom ? "Starting From" : null,
            name, SourceName, first, last, SeasonPriceHistoryOrigin.Observed);

    private static OfficialPriceObservation Observation(
        string key = GoldPass, decimal amount = 99m, Guid? device = null, int sequence = 1, PriceKind kind = PriceKind.StandardCatalog, string name = "Gold Season Pass — 2027")
        => OfficialPriceObservation.FromLocalInterval(Interval(key, sequence, amount, April, April.AddDays(30), kind, name), device ?? DeviceA)!;

    [Fact]
    public void Identity_IsDeterministic_AndHexSha256()
    {
        var first = Observation();
        var second = Observation();

        Assert.Equal(first.ObservationId, second.ObservationId);
        Assert.Matches("^[0-9a-f]{64}$", first.ObservationId);
        Assert.Equal(OfficialPriceObservationIdentity.Compute(first), first.ObservationId);
    }

    [Fact]
    public void Identity_ExcludesLastConfirmed_SoAReconfirmationKeepsTheSameIdentity()
    {
        var early = OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, 99m, April, April.AddDays(1)), DeviceA)!;
        var later = OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, 99m, April, April.AddDays(90)), DeviceA)!;

        Assert.Equal(early.ObservationId, later.ObservationId);
        Assert.True(early.HasSameImmutableContent(later));
    }

    [Fact]
    public void Identity_DiffersForEveryImmutableField_NeverJustThePrice()
    {
        var baseline = Observation();
        var variants = new[]
        {
            Observation(amount: 104m),
            Observation(device: DeviceB),
            Observation(sequence: 2),
            Observation(key: GoldRenewal, name: "Gold Season Pass Renewal — 2027"),
            Observation(kind: PriceKind.StartingFrom),
            Observation(name: "Gold Pass 2027"),
            OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, 99m, April.AddTicks(1), April.AddDays(30)), DeviceA)!,
            OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, 99m, April, April.AddDays(30)) with { Origin = SeasonPriceHistoryOrigin.Backfilled }, DeviceA)!,
            OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, 99m, April, April.AddDays(30)) with { SourceName = "Carowinds Season Passes" }, DeviceA)!,
        };

        Assert.All(variants, variant => Assert.NotEqual(baseline.ObservationId, variant.ObservationId));
        Assert.Equal(variants.Length, variants.Select(v => v.ObservationId).Distinct().Count());
    }

    [Fact]
    public void SamePriceObservedIndependentlyByTwoComputers_RemainsSeparateEvidence()
    {
        var onA = Observation(device: DeviceA);
        var onB = Observation(device: DeviceB);

        Assert.Equal(onA.Amount, onB.Amount);
        Assert.NotEqual(onA.ObservationId, onB.ObservationId);
    }

    [Theory]
    [InlineData(GoldPass, "Gold Season Pass — 2027", PassPriceUnit.OneTime)]
    [InlineData(GoldRenewal, "Gold Season Pass Renewal — 2027", PassPriceUnit.OneTime)]
    [InlineData(GoldMembership, "Gold Membership", PassPriceUnit.PerMonth)]
    [InlineData(GoldInitiationFee, "Gold Membership Initiation Fee", PassPriceUnit.OneTime)]
    public void LocalInterval_CarriesTheBillingUnitOfItsOwnFamily(string key, string name, PassPriceUnit unit)
    {
        var observation = Observation(key: key, name: name, amount: 7m);

        Assert.Equal(unit, observation.BillingUnit);
        Assert.Equal(OfficialPriceObservation.UsDollars, observation.Currency);
        Assert.Equal(OfficialPriceObservationRejection.None, OfficialPriceObservationValidator.Validate(observation, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(99.999)]
    [InlineData(20000)]
    public void LocalInterval_WithPlaceholderOrImplausibleAmount_IsNeverExported(double amount)
    {
        Assert.Null(OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, (decimal)amount, April, April), DeviceA));
    }

    [Fact]
    public void LocalInterval_DatedOrUntrackedProduct_IsNeverExported()
    {
        Assert.Null(OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, 1, 99m, April, April, PriceKind.ExactDated), DeviceA));
        Assert.Null(OfficialPriceObservation.FromLocalInterval(Interval("DatedAdmissionTickets:name:carowinds tickets|general admission", 1, 59m, April, April, name: "General Admission"), DeviceA));
        Assert.Null(OfficialPriceObservation.FromLocalInterval(Interval("not-a-key", 1, 59m, April, April), DeviceA));
    }

    [Fact]
    public void LegacyUnclassifiedKindAndBackfilledOrigin_ArePreservedNotReclassified()
    {
        var legacy = OfficialPriceObservation.FromLocalInterval(
            Interval("SeasonPasses:name:carowinds season passes|gold pass", 1, 89m, April, April, PriceKind.Unknown, "Gold Pass") with { Origin = SeasonPriceHistoryOrigin.Backfilled },
            DeviceA)!;

        Assert.Equal(PriceKind.Unknown, legacy.PriceKind);
        Assert.Equal(SeasonPriceHistoryOrigin.Backfilled, legacy.Origin);
        Assert.Equal(PassPriceUnit.OneTime, legacy.BillingUnit);
        Assert.Equal(OfficialPriceObservationRejection.None, OfficialPriceObservationValidator.Validate(legacy, Now));
    }

    public static TheoryData<string> RejectionCases => new()
    {
        "id-format", "id-mismatch", "amount-zero", "amount-precision", "currency", "kind-dated", "unit-mismatch",
        "future", "last-before-first", "too-early", "untracked", "bad-key", "control-chars", "empty-contributor", "sequence", "missing-name",
    };

    [Theory]
    [MemberData(nameof(RejectionCases))]
    public void Validate_RejectsUntrustworthyRecords(string scenario)
    {
        var good = Observation();
        var bad = scenario switch
        {
            "id-format" => good with { ObservationId = "../../etc/passwd" },
            "id-mismatch" => good with { Amount = 79m }, // claimed identity no longer matches the content
            "amount-zero" => Rehash(good with { Amount = 0m }),
            "amount-precision" => Rehash(good with { Amount = 99.001m }),
            "currency" => good with { Currency = "EUR" },
            "kind-dated" => Rehash(good with { PriceKind = PriceKind.ExactDated }),
            "unit-mismatch" => good with { BillingUnit = PassPriceUnit.PerMonth },
            "future" => good with { LastConfirmedUtc = Now.AddDays(10) },
            "last-before-first" => good with { LastConfirmedUtc = good.FirstObservedUtc.AddDays(-1) },
            "too-early" => Rehash(good with { FirstObservedUtc = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero) }),
            "untracked" => Rehash(good with { ProductKey = "DatedAdmissionTickets:name:carowinds tickets|general admission" }),
            "bad-key" => Rehash(good with { ProductKey = "file:///C:/Windows/System32" }),
            "control-chars" => Rehash(good with { DisplayName = "Gold\u0000Pass" }),
            "empty-contributor" => Rehash(good with { ContributorDeviceId = Guid.Empty }),
            "sequence" => Rehash(good with { ContributorSequence = 0 }),
            "missing-name" => good with { DisplayName = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        Assert.NotEqual(OfficialPriceObservationRejection.None, OfficialPriceObservationValidator.Validate(bad, Now));
        Assert.Equal(OfficialPriceObservationRejection.None, OfficialPriceObservationValidator.Validate(good, Now));
    }

    [Fact]
    public void Validate_SameIdentityWithConflictingContent_IsAnIdentityMismatch()
    {
        var good = Observation();
        var tampered = good with { Amount = 89m };

        Assert.Equal(OfficialPriceObservationRejection.IdentityMismatch, OfficialPriceObservationValidator.Validate(tampered, Now));
    }

    [Fact]
    public void FileSerializer_IsDeterministicRegardlessOfInputOrder()
    {
        var observations = new[] { Observation(amount: 99m), Observation(amount: 104m, sequence: 2), Observation(key: GoldMembership, name: "Gold Membership", amount: 7m) };

        var forward = OfficialPriceHistoryFileSerializer.Serialize(DeviceA, observations);
        var reversed = OfficialPriceHistoryFileSerializer.Serialize(DeviceA, observations.Reverse());

        Assert.Equal(forward, reversed);
        Assert.Contains("\"PriceKind\": \"StandardCatalog\"", forward);
        Assert.Contains("\"BillingUnit\": \"PerMonth\"", forward);
        Assert.Contains("\"FormatVersion\": 1", forward);
    }

    [Fact]
    public void SummaryText_SpeaksOfObservationsNeverPriceChanges()
    {
        Assert.Equal("Official price history: 28 new observations merged.", OfficialPriceHistorySummary.MergedText(28));
        Assert.Equal("Official price history: 1 new observation merged.", OfficialPriceHistorySummary.MergedText(1));
        Assert.DoesNotContain("change", OfficialPriceHistorySummary.MergedText(28), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Official price history: 2 observations could not be verified and were skipped.", OfficialPriceHistorySummary.SkippedText(2));
    }

    private static OfficialPriceObservation Rehash(OfficialPriceObservation observation) =>
        observation with { ObservationId = OfficialPriceObservationIdentity.Compute(observation) };
}
