using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;
using static Carowinds.Passdown.Application.Tests.PersonalSync.Phase26OfficialPriceObservationTests;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>Phase 26: the read-time merge of local intervals and synced observations into one history.</summary>
public class Phase26PriceHistoryTimelineMergeTests
{
    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid DeviceC = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static DateTimeOffset Day(int month, int day) => new(2026, month, day, 14, 0, 0, TimeSpan.Zero);

    private static OfficialPriceObservation Remote(Guid device, int sequence, decimal amount, DateTimeOffset first, DateTimeOffset last,
        string key = GoldPass, PriceKind kind = PriceKind.StandardCatalog, string name = "Gold Season Pass — 2027") =>
        OfficialPriceObservation.FromLocalInterval(Interval(key, sequence, amount, first, last, kind, name), device)!;

    private static IReadOnlyList<(decimal Amount, PriceKind Kind, DateTimeOffset First, DateTimeOffset Last)> Shape(IReadOnlyList<PriceHistoryEntry> entries) =>
        entries.Select(e => (e.Interval.Amount, e.Interval.PriceKind, e.Interval.FirstObservedAt, e.Interval.LastConfirmedAt)).ToList();

    [Fact]
    public void NoImports_ReproducesTheLocalHistoryExactly()
    {
        var local = new[]
        {
            Interval(GoldPass, 1, 99m, Day(4, 1), Day(5, 1)),
            Interval(GoldPass, 2, 104m, Day(5, 2), Day(6, 1)),
            Interval(GoldPass, 3, 104m, Day(8, 1), Day(9, 1)), // reappearance after retirement
        };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, []);

        Assert.Equal(local.Select(i => i with { }).ToList(), merged.Select(e => e.Interval).ToList());
        Assert.All(merged, e => Assert.Equal(PriceHistoryProvenance.CollectedHere, e.Provenance));
        Assert.True(merged[2].IsReappearanceAtSamePrice);
    }

    [Fact]
    public void OlderHistoryFromAnotherComputer_ExtendsThisComputersHistoryBackward()
    {
        // Computer B began collecting in August; computer A had collected since April.
        var localB = new[] { Interval(GoldPass, 1, 104m, Day(8, 1), Day(10, 20)) };
        var fromA = new[] { Remote(DeviceA, 1, 99m, Day(4, 1), Day(6, 30)), Remote(DeviceA, 2, 104m, Day(7, 1), Day(10, 21)) };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, localB, fromA);

        Assert.Equal(2, merged.Count);
        Assert.Equal((99m, Day(4, 1), Day(6, 30)), (merged[0].Interval.Amount, merged[0].Interval.FirstObservedAt, merged[0].Interval.LastConfirmedAt));
        Assert.Equal(PriceHistoryProvenance.ImportedThroughSync, merged[0].Provenance);
        Assert.Equal((104m, Day(7, 1), Day(10, 21)), (merged[1].Interval.Amount, merged[1].Interval.FirstObservedAt, merged[1].Interval.LastConfirmedAt));
        Assert.Equal(PriceHistoryProvenance.CollectedHere | PriceHistoryProvenance.ImportedThroughSync, merged[1].Provenance);
        Assert.Equal(5m, merged[1].ChangeFromPrevious!.AmountDelta);
        Assert.Equal([1, 2], merged.Select(e => e.Interval.Sequence));
    }

    [Fact]
    public void SamePriceSeenIndependentlyByTwoComputers_IsOneRow_NotADuplicate()
    {
        var local = new[] { Interval(GoldPass, 1, 99m, Day(9, 1), Day(10, 1)) };
        var remote = new[] { Remote(DeviceB, 1, 99m, Day(9, 1).AddMinutes(3), Day(10, 2)) };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, remote);

        var row = Assert.Single(merged);
        Assert.Equal(Day(9, 1), row.Interval.FirstObservedAt);
        Assert.Equal(Day(10, 2), row.Interval.LastConfirmedAt);
    }

    [Fact]
    public void SamePriceAcrossAGapFromDifferentComputers_IsNotMistakenForAReappearance()
    {
        var local = new[] { Interval(GoldPass, 1, 99m, Day(4, 1), Day(5, 1)) };
        var remote = new[] { Remote(DeviceB, 1, 99m, Day(8, 1), Day(10, 1)) };

        var row = Assert.Single(PriceHistoryTimelineMerge.Merge(GoldPass, local, remote));
        Assert.Equal((Day(4, 1), Day(10, 1)), (row.Interval.FirstObservedAt, row.Interval.LastConfirmedAt));
    }

    [Fact]
    public void ARemoteComputersOwnReappearance_IsPreserved()
    {
        var remote = new[] { Remote(DeviceA, 1, 99m, Day(4, 1), Day(5, 1)), Remote(DeviceA, 2, 99m, Day(8, 1), Day(9, 1)) };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, [], remote);

        Assert.Equal(2, merged.Count);
        Assert.True(merged[1].IsReappearanceAtSamePrice);
    }

    [Fact]
    public void DifferentValuesAtDifferentDates_StayDistinctRowsInDateOrder()
    {
        var remote = new[]
        {
            Remote(DeviceA, 3, 104m, Day(7, 1), Day(7, 31)),
            Remote(DeviceA, 1, 99m, Day(4, 1), Day(5, 31)),
            Remote(DeviceA, 2, 89m, Day(6, 1), Day(6, 30)),
        };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, [], remote);

        Assert.Equal([99m, 89m, 104m], merged.Select(e => e.Interval.Amount));
        Assert.Equal(-10m, merged[1].ChangeFromPrevious!.AmountDelta);
    }

    [Fact]
    public void ExactAndFromPricing_AreDifferentStatesEvenAtTheSameAmount()
    {
        var remote = new[]
        {
            Remote(DeviceA, 1, 99m, Day(4, 1), Day(5, 1)),
            Remote(DeviceB, 1, 99m, Day(4, 20), Day(6, 1), kind: PriceKind.StartingFrom),
        };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, [], remote);

        Assert.Equal(2, merged.Count);
        Assert.True(merged[1].ChangeFromPrevious!.KindChanged);
        Assert.False(merged[1].ChangeFromPrevious!.AmountChanged);
    }

    [Fact]
    public void OtherProductsObservations_NeverEnterThisProductsHistory()
    {
        // Same tier, same season, same amount — a renewal, a Membership rate and an initiation fee are never a Season Pass price.
        var remote = new[]
        {
            Remote(DeviceA, 1, 99m, Day(4, 1), Day(5, 1)),
            Remote(DeviceA, 1, 99m, Day(4, 1), Day(5, 1), GoldRenewal, name: "Gold Season Pass Renewal — 2027"),
            Remote(DeviceA, 1, 7m, Day(4, 1), Day(5, 1), GoldMembership, name: "Gold Membership"),
            Remote(DeviceA, 1, 20m, Day(4, 1), Day(5, 1), GoldInitiationFee, name: "Gold Membership Initiation Fee"),
        };

        Assert.Equal([99m], PriceHistoryTimelineMerge.Merge(GoldPass, [], remote).Select(e => e.Interval.Amount));
        Assert.Equal([99m], PriceHistoryTimelineMerge.Merge(GoldRenewal, [], remote).Select(e => e.Interval.Amount));
        Assert.Equal([7m], PriceHistoryTimelineMerge.Merge(GoldMembership, [], remote).Select(e => e.Interval.Amount));
        Assert.Equal([20m], PriceHistoryTimelineMerge.Merge(GoldInitiationFee, [], remote).Select(e => e.Interval.Amount));
    }

    [Fact]
    public void Merge_IsIndependentOfInputOrder_AndIdempotentForRepeatedObservations()
    {
        var remote = new List<OfficialPriceObservation>
        {
            Remote(DeviceA, 1, 99m, Day(4, 1), Day(6, 30)),
            Remote(DeviceA, 2, 104m, Day(7, 1), Day(10, 1)),
            Remote(DeviceB, 1, 104m, Day(8, 1), Day(10, 2)),
            Remote(DeviceC, 1, 109m, Day(10, 10), Day(10, 20)),
            Remote(DeviceC, 1, 7m, Day(10, 10), Day(10, 20), GoldMembership, name: "Gold Membership"),
        };

        var expected = Shape(PriceHistoryTimelineMerge.Merge(GoldPass, [], remote));
        var random = new Random(26);
        for (var i = 0; i < 25; i++)
        {
            var shuffled = remote.OrderBy(_ => random.Next()).ToList();
            var withDuplicates = shuffled.Concat(shuffled.Take(3)).ToList();
            Assert.Equal(expected, Shape(PriceHistoryTimelineMerge.Merge(GoldPass, [], withDuplicates)));
        }
    }

    [Fact]
    public void ThreeComputers_ConvergeToTheSameLogicalHistory_FromEachOnesPointOfView()
    {
        // PC-A April–October, PC-B August–October, PC-C October only.
        var a = new[] { Interval(GoldPass, 1, 99m, Day(4, 1), Day(6, 30)), Interval(GoldPass, 2, 104m, Day(7, 1), Day(10, 20)) };
        var b = new[] { Interval(GoldPass, 1, 104m, Day(8, 1), Day(10, 18)) };
        var c = new[] { Interval(GoldPass, 1, 104m, Day(10, 5), Day(10, 21)) };

        static IReadOnlyList<OfficialPriceObservation> As(Guid device, IEnumerable<SeasonPriceHistoryInterval> intervals) =>
            intervals.Select(i => OfficialPriceObservation.FromLocalInterval(i, device)!).ToList();

        var viewA = PriceHistoryTimelineMerge.Merge(GoldPass, a, [.. As(DeviceB, b), .. As(DeviceC, c)]);
        var viewB = PriceHistoryTimelineMerge.Merge(GoldPass, b, [.. As(DeviceC, c), .. As(DeviceA, a)]);
        var viewC = PriceHistoryTimelineMerge.Merge(GoldPass, c, [.. As(DeviceA, a), .. As(DeviceB, b)]);

        Assert.Equal(Shape(viewA), Shape(viewB));
        Assert.Equal(Shape(viewA), Shape(viewC));
        Assert.Equal([99m, 104m], viewA.Select(e => e.Interval.Amount));
        Assert.Equal((Day(7, 1), Day(10, 21)), (viewB[1].Interval.FirstObservedAt, viewB[1].Interval.LastConfirmedAt));
        // Only provenance differs: each computer knows which evidence it collected itself.
        Assert.Equal(PriceHistoryProvenance.ImportedThroughSync, viewB[0].Provenance);
        Assert.Equal(PriceHistoryProvenance.CollectedHere, viewA[0].Provenance);
    }

    [Fact]
    public void ImportedOlderObservation_NeverBecomesTheNewestRow()
    {
        var local = new[] { Interval(GoldPass, 1, 109m, Day(10, 1), Day(10, 21)) };
        var remote = new[] { Remote(DeviceA, 1, 99m, Day(4, 1), Day(6, 30)) };

        var merged = PriceHistoryTimelineMerge.Merge(GoldPass, local, remote);

        Assert.Equal(109m, merged[^1].Interval.Amount);
        Assert.Equal(Day(10, 21), merged[^1].Interval.LastConfirmedAt);
    }

    [Fact]
    public void MonthlyMembershipRateChanges_MergeWithinTheirOwnSeries()
    {
        var local = new[] { Interval(GoldMembership, 1, 7m, Day(9, 1), Day(10, 1), name: "Gold Membership") };
        var remote = new[] { Remote(DeviceA, 1, 6m, Day(4, 1), Day(8, 15), GoldMembership, name: "Gold Membership") };

        var merged = PriceHistoryTimelineMerge.Merge(GoldMembership, local, remote);

        Assert.Equal([6m, 7m], merged.Select(e => e.Interval.Amount));
        Assert.Equal(1m, merged[1].ChangeFromPrevious!.AmountDelta);
    }
}
