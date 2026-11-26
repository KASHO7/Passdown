using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;
using static Carowinds.Passdown.Application.Tests.PersonalSync.Phase26OfficialPriceObservationTests;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// Phase 31B: cross-computer price history periods — no repeated first-seen rows from interleaved
/// evidence, no merging across a verified change, stable under repeated and out-of-order synchronization.
/// </summary>
public class Phase31BPriceHistoryMergeTests
{
    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-3131-0000-0000-000000000001");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-3131-0000-0000-000000000002");
    private static readonly Guid DeviceC = Guid.Parse("cccccccc-3131-0000-0000-000000000003");

    private static DateTimeOffset Day(int month, int day) => new(2026, month, day, 14, 0, 0, TimeSpan.Zero);

    private static OfficialPriceObservation Remote(Guid device, int sequence, decimal amount, DateTimeOffset first, DateTimeOffset last,
        PriceKind kind = PriceKind.StandardCatalog, string name = "Gold Season Pass — 2027") =>
        OfficialPriceObservation.FromLocalInterval(Interval(GoldPass, sequence, amount, first, last, kind, name), device)!;

    private static IReadOnlyList<(decimal Amount, PriceKind Kind, DateTimeOffset First, DateTimeOffset Last, PriceHistoryProvenance Provenance)> Shape(IReadOnlyList<PriceHistoryEntry> entries) =>
        entries.Select(e => (e.Interval.Amount, e.Interval.PriceKind, e.Interval.FirstObservedAt, e.Interval.LastConfirmedAt, e.Provenance)).ToList();

    [Fact]
    public void AnotherComputersBriefConcurrentDisagreement_DoesNotSplitOnePeriodIntoAlternatingRows()
    {
        // This computer saw the sale price all month; computer B briefly recorded the same amount as a catalog price.
        var local = new[] { Interval(GoldPass, 1, 99m, Day(11, 1), Day(11, 27), PriceKind.Sale) };
        var fromB = new[]
        {
            Remote(DeviceB, 1, 99m, Day(11, 3), Day(11, 10), PriceKind.StandardCatalog),
            Remote(DeviceB, 2, 99m, Day(11, 11), Day(11, 27), PriceKind.Sale),
        };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, fromB);

        Assert.Equal(2, merged.Count); // previously three rows: Sale (Nov 1), Catalog (Nov 3), Sale (Nov 11)
        Assert.Equal((99m, PriceKind.Sale, Day(11, 1), Day(11, 27)), (merged[0].Interval.Amount, merged[0].Interval.PriceKind, merged[0].Interval.FirstObservedAt, merged[0].Interval.LastConfirmedAt));
        Assert.Equal(PriceHistoryProvenance.CollectedHere | PriceHistoryProvenance.ImportedThroughSync, merged[0].Provenance);
        Assert.Equal(PriceKind.StandardCatalog, merged[1].Interval.PriceKind);
        Assert.True(merged[1].OverlapsEarlierPeriod);
        Assert.False(merged[0].OverlapsEarlierPeriod);
    }

    [Fact]
    public void ARealTemporarySale_IsNeverMergedAcrossTheInterveningVerifiedChange()
    {
        var local = new[]
        {
            Interval(GoldPass, 1, 105m, Day(10, 1), Day(10, 31)),
            Interval(GoldPass, 2, 95m, Day(11, 1), Day(11, 20), PriceKind.Sale),
            Interval(GoldPass, 3, 105m, Day(11, 21), Day(11, 30)),
        };
        var fromB = new[]
        {
            Remote(DeviceB, 1, 105m, Day(10, 5), Day(11, 3)), // B confirmed $105 a little after the sale began
            Remote(DeviceB, 2, 95m, Day(11, 4), Day(11, 19), PriceKind.Sale),
            Remote(DeviceB, 3, 105m, Day(11, 22), Day(11, 28)),
        };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, fromB);

        Assert.Equal([105m, 95m, 105m], merged.Select(e => e.Interval.Amount));
        Assert.Equal((Day(10, 1), Day(11, 3)), (merged[0].Interval.FirstObservedAt, merged[0].Interval.LastConfirmedAt));
        Assert.Equal((Day(11, 1), Day(11, 20)), (merged[1].Interval.FirstObservedAt, merged[1].Interval.LastConfirmedAt));
        Assert.Equal((Day(11, 21), Day(11, 30)), (merged[2].Interval.FirstObservedAt, merged[2].Interval.LastConfirmedAt));
        Assert.Equal(-10m, merged[1].ChangeFromPrevious!.AmountDelta);
        Assert.Equal(10m, merged[2].ChangeFromPrevious!.AmountDelta);
    }

    [Fact]
    public void DifferentPriceTypesWithEqualAmounts_StayDistinct_AcrossComputers()
    {
        var local = new[]
        {
            Interval(GoldPass, 1, 99m, Day(10, 1), Day(10, 10)),
            Interval(GoldPass, 2, 99m, Day(10, 11), Day(10, 20), PriceKind.StartingFrom),
        };
        var fromB = new[]
        {
            Remote(DeviceB, 1, 99m, Day(10, 2), Day(10, 9)),
            Remote(DeviceB, 2, 99m, Day(10, 12), Day(10, 21), PriceKind.StartingFrom),
        };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, fromB);

        Assert.Equal([PriceKind.StandardCatalog, PriceKind.StartingFrom], merged.Select(e => e.Interval.PriceKind));
        Assert.True(merged[1].ChangeFromPrevious!.KindChanged);
        Assert.Equal(Day(10, 21), merged[1].Interval.LastConfirmedAt);
    }

    [Fact]
    public void ThisComputersOwnObservationRelayedBack_FoldsIntoItsLocalRow_KeepingAllProvenance()
    {
        var local = new[] { Interval(GoldPass, 1, 99m, Day(9, 1), Day(10, 1)) };
        // Same interval exported earlier under an older display name, relayed back by another computer.
        var relayed = new[] { Remote(DeviceA, 1, 99m, Day(9, 1), Day(10, 5), name: "Gold Pass 2027") };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, relayed, localDeviceId: DeviceA);

        var row = Assert.Single(merged);
        Assert.Equal("Gold Season Pass — 2027", row.Interval.DisplayName); // the local record represents the interval
        Assert.Equal((Day(9, 1), Day(10, 5)), (row.Interval.FirstObservedAt, row.Interval.LastConfirmedAt));
        Assert.Equal(PriceHistoryProvenance.CollectedHere | PriceHistoryProvenance.ImportedThroughSync, row.Provenance);
    }

    [Fact]
    public void ALocalReappearance_IsPreserved_EvenWhenAnotherComputersEvidenceJoinedTheEarlierRow()
    {
        var local = new[]
        {
            Interval(GoldPass, 1, 99m, Day(4, 1), Day(5, 1)),
            Interval(GoldPass, 2, 99m, Day(8, 1), Day(9, 1)), // retired, then published again
        };
        var fromB = new[] { Remote(DeviceB, 1, 99m, Day(4, 2), Day(4, 3)) };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, fromB);

        Assert.Equal(2, merged.Count);
        Assert.True(merged[1].IsReappearanceAtSamePrice);
    }

    [Fact]
    public void OutOfOrderImports_ProduceExactlyTheSameHistory()
    {
        var local = new[] { Interval(GoldPass, 1, 99m, Day(9, 1), Day(10, 1)), Interval(GoldPass, 2, 89m, Day(10, 2), Day(10, 20), PriceKind.Sale) };
        var remote = new List<OfficialPriceObservation>
        {
            Remote(DeviceB, 1, 99m, Day(8, 15), Day(9, 30)),
            Remote(DeviceB, 2, 89m, Day(10, 3), Day(10, 22), PriceKind.Sale),
            Remote(DeviceC, 1, 89m, Day(10, 5), Day(10, 6), PriceKind.Sale),
            Remote(DeviceC, 2, 99m, Day(10, 23), Day(11, 1)),
        };

        var expected = Shape(PriceHistoryTimelineMerge.Merge(GoldPass, local, remote));
        var random = new Random(31);
        for (var trial = 0; trial < 20; trial++)
        {
            var shuffled = remote.OrderBy(_ => random.Next()).ToList();
            var localShuffled = local.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, Shape(PriceHistoryTimelineMerge.Merge(GoldPass, localShuffled, shuffled)));
        }

        Assert.Equal([99m, 89m, 99m], expected.Select(e => e.Amount));
        Assert.Equal(Day(8, 15), expected[0].First); // First Seen = earliest known observation
        Assert.Equal(Day(10, 22), expected[1].Last); // Last Confirmed = latest known confirmation
    }

    [Fact]
    public void RepeatedTwoAndThreeComputerSynchronization_IsStable()
    {
        var localA = new[] { Interval(GoldPass, 1, 99m, Day(9, 1), Day(10, 1)) };
        var b1 = Remote(DeviceB, 1, 99m, Day(9, 2), Day(9, 20));
        var c1 = Remote(DeviceC, 1, 99m, Day(9, 3), Day(9, 25));

        var once = Shape(PriceHistoryTimelineMerge.Merge(GoldPass, localA, [b1]));
        // The same observations arriving again (and through a third computer's file) change nothing.
        var repeated = Shape(PriceHistoryTimelineMerge.Merge(GoldPass, localA, [b1, b1, b1 with { }]));
        Assert.Equal(once, repeated);

        var withC = PriceHistoryTimelineMerge.Merge(GoldPass, localA, [b1, c1, c1, b1]);
        var row = Assert.Single(withC);
        Assert.Equal((Day(9, 1), Day(10, 1)), (row.Interval.FirstObservedAt, row.Interval.LastConfirmedAt));

        // B confirmed again later: the same observation id with a later confirmation extends Last Confirmed only.
        var extended = PriceHistoryTimelineMerge.Merge(GoldPass, localA, [b1, b1 with { LastConfirmedUtc = Day(10, 9) }, c1]);
        Assert.Equal(Day(10, 9), Assert.Single(extended).Interval.LastConfirmedAt);
    }

    [Fact]
    public void DifferentProductsAndYears_AreNeverConflated()
    {
        const string GoldPass2026 = "SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2026";
        var local = new[] { Interval(GoldPass, 1, 99m, Day(9, 1), Day(10, 1)) };
        var otherYear = new[] { OfficialPriceObservation.FromLocalInterval(Interval(GoldPass2026, 1, 99m, Day(9, 1), Day(10, 1)), DeviceB)! };

        Assert.Single(PriceHistoryTimelineMerge.Merge(GoldPass, local, otherYear));
        Assert.Equal(PriceHistoryProvenance.CollectedHere, PriceHistoryTimelineMerge.Merge(GoldPass, local, otherYear)[0].Provenance);
    }

    [Fact]
    public async Task ReadingMergedHistory_NeverRewritesTheStoredRecords()
    {
        var history = new Carowinds.Passdown.Application.Tests.TestSupport.InMemorySeasonPriceHistoryRepository();
        var interval = Interval(GoldPass, 1, 99m, Day(9, 1), Day(10, 1));
        await history.InsertAsync(interval);
        var before = await history.GetHistoryAsync(GoldPass);

        PriceHistoryTimelineMerge.Merge(GoldPass, before, [Remote(DeviceB, 1, 99m, Day(8, 1), Day(10, 9))]);

        Assert.Equal(before, await history.GetHistoryAsync(GoldPass));
        Assert.Equal(interval, Assert.Single(await history.GetHistoryAsync(GoldPass)));
    }
}
