using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Domain.Tests.Hours;

/// <summary>
/// Phase 18D: independent per-date North Gate/Avenue overrides. Effective = override ?? default,
/// where the default stays the existing calculation (opening −30/−45 minutes); neither override
/// changes park hours or the other entrance, and no ordering rule is imposed.
/// </summary>
public class GateOpeningOverrideTests
{
    private static readonly TimeOnly Eleven = new(11, 0);
    private static readonly TimeOnly Eight = new(20, 0);

    private static EffectiveHours Official(TimeOnly opening, TimeOnly? northGate = null, TimeOnly? avenue = null) =>
        ParkHoursCalculator.Calculate(
            officialIsClosed: false, opening, Eight,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null,
            northGate, avenue);

    [Fact]
    public void NoOverride_UsesTheCalculatedDefaults()
    {
        var hours = Official(Eleven);

        Assert.Equal(new TimeOnly(10, 30), hours.NorthGateOpening);
        Assert.Equal(new TimeOnly(10, 15), hours.AvenueOpening);
        Assert.Equal(hours.DefaultNorthGateOpening, hours.NorthGateOpening);
        Assert.Equal(hours.DefaultAvenueOpening, hours.AvenueOpening);
        Assert.False(hours.IsNorthGateOverridden);
        Assert.False(hours.IsAvenueOverridden);
    }

    [Fact]
    public void OnlyAvenueOverridden_LeavesParkHoursAndNorthGateUnchanged()
    {
        var hours = Official(Eleven, avenue: new TimeOnly(10, 0));

        Assert.Equal(Eleven, hours.Opening);
        Assert.Equal(Eight, hours.Closing);
        Assert.Equal(new TimeOnly(10, 30), hours.NorthGateOpening);
        Assert.Equal(new TimeOnly(10, 0), hours.AvenueOpening);
        Assert.Equal(new TimeOnly(10, 15), hours.DefaultAvenueOpening);
        Assert.True(hours.IsAvenueOverridden);
        Assert.False(hours.IsNorthGateOverridden);
    }

    [Fact]
    public void OnlyNorthGateOverridden_LeavesParkHoursAndAvenueUnchanged()
    {
        var hours = Official(Eleven, northGate: new TimeOnly(10, 0));

        Assert.Equal(Eleven, hours.Opening);
        Assert.Equal(new TimeOnly(10, 0), hours.NorthGateOpening);
        Assert.Equal(new TimeOnly(10, 15), hours.AvenueOpening);
        Assert.True(hours.IsNorthGateOverridden);
        Assert.False(hours.IsAvenueOverridden);
    }

    [Fact]
    public void BothOverridden_EachIndependent()
    {
        var hours = Official(Eleven, new TimeOnly(9, 45), new TimeOnly(9, 30));

        Assert.Equal(new TimeOnly(9, 45), hours.NorthGateOpening);
        Assert.Equal(new TimeOnly(9, 30), hours.AvenueOpening);
        Assert.Equal(Eleven, hours.Opening);
    }

    [Fact]
    public void NoOrderingRuleIsImposed_UnusualValuesAreKeptAsEntered()
    {
        // Entrance after park opening, and Avenue after North Gate — operational reality may need it.
        var hours = Official(Eleven, new TimeOnly(11, 15), new TimeOnly(11, 30));

        Assert.Equal(new TimeOnly(11, 15), hours.NorthGateOpening);
        Assert.Equal(new TimeOnly(11, 30), hours.AvenueOpening);
    }

    [Fact]
    public void DefaultChangesWhileOverrideStored_OverrideStaysAuthoritative_AndResetUsesTheNewDefault()
    {
        var avenueOverride = new TimeOnly(10, 0);

        var before = Official(Eleven, avenue: avenueOverride);
        var afterOfficialChange = Official(new TimeOnly(12, 0), avenue: avenueOverride);
        var afterReset = Official(new TimeOnly(12, 0), avenue: null);

        Assert.Equal(avenueOverride, before.AvenueOpening);
        Assert.Equal(avenueOverride, afterOfficialChange.AvenueOpening);
        Assert.Equal(new TimeOnly(11, 15), afterOfficialChange.DefaultAvenueOpening);
        Assert.Equal(new TimeOnly(11, 30), afterOfficialChange.NorthGateOpening); // un-overridden entrance follows the default
        Assert.Equal(new TimeOnly(11, 15), afterReset.AvenueOpening);
    }

    [Fact]
    public void OverridesApplyOnTopOfAManualHoursOverrideAndAPrivateBuyout()
    {
        var manual = ParkHoursCalculator.Calculate(
            officialIsClosed: false, Eleven, Eight,
            manualOverrideEnabled: true, new TimeOnly(12, 0), Eight,
            privateBuyoutEnabled: false, null, null,
            northGateOverride: null, avenueOverride: new TimeOnly(11, 0));
        var buyout = ParkHoursCalculator.Calculate(
            officialIsClosed: true, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: true, new TimeOnly(18, 0), new TimeOnly(23, 0),
            northGateOverride: new TimeOnly(17, 0), avenueOverride: null);

        Assert.Equal(new TimeOnly(11, 30), manual.NorthGateOpening);
        Assert.Equal(new TimeOnly(11, 0), manual.AvenueOpening);
        Assert.Equal(new TimeOnly(17, 0), buyout.NorthGateOpening);
        Assert.Equal(new TimeOnly(17, 15), buyout.AvenueOpening);
    }

    [Fact]
    public void ClosedDate_NeverShowsAnEntranceOpening_EvenWithAStoredOverride()
    {
        var hours = ParkHoursCalculator.Calculate(
            officialIsClosed: true, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null,
            new TimeOnly(10, 0), new TimeOnly(9, 45));

        Assert.Equal(EffectiveHoursStatus.Closed, hours.Status);
        Assert.Null(hours.NorthGateOpening);
        Assert.Null(hours.AvenueOpening);
        Assert.False(hours.IsNorthGateOverridden);
        Assert.False(hours.IsAvenueOverridden);
    }

    [Fact]
    public void HoursNotCheckedYet_AnEnteredOverrideStillApplies()
    {
        var hours = ParkHoursCalculator.Calculate(
            officialIsClosed: false, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null,
            new TimeOnly(10, 0), null);

        Assert.Equal(EffectiveHoursStatus.NotCheckedYet, hours.Status);
        Assert.Equal(new TimeOnly(10, 0), hours.NorthGateOpening);
        Assert.Null(hours.AvenueOpening);
    }
}
