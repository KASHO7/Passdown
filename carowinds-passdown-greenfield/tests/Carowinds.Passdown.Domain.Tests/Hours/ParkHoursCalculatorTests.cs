using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Domain.Tests.Hours;

public class ParkHoursCalculatorTests
{
    private static readonly TimeOnly OfficialOpening = new(10, 0);
    private static readonly TimeOnly OfficialClosing = new(21, 0);

    [Fact]
    public void Calculate_VerifiedOfficialHours_NoOverrides_UsesOfficialTimesAndDerivesGates()
    {
        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: false, OfficialOpening, OfficialClosing,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null);

        Assert.Equal(EffectiveHoursStatus.Open, result.Status);
        Assert.Equal(OfficialOpening, result.Opening);
        Assert.Equal(OfficialClosing, result.Closing);
        Assert.Equal(new TimeOnly(9, 30), result.NorthGateOpening);
        Assert.Equal(new TimeOnly(9, 15), result.AvenueOpening);
        Assert.False(result.IsManualOverrideApplied);
        Assert.False(result.IsPrivateBuyoutApplied);
    }

    [Fact]
    public void Calculate_ManualOverrideEnabled_ChangesEffectiveGateTimes()
    {
        var manualOpening = new TimeOnly(11, 0);
        var manualClosing = new TimeOnly(20, 0);

        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: false, OfficialOpening, OfficialClosing,
            manualOverrideEnabled: true, manualOpening, manualClosing,
            privateBuyoutEnabled: false, null, null);

        Assert.Equal(EffectiveHoursStatus.Open, result.Status);
        Assert.Equal(manualOpening, result.Opening);
        Assert.Equal(new TimeOnly(10, 30), result.NorthGateOpening);
        Assert.Equal(new TimeOnly(10, 15), result.AvenueOpening);
        Assert.True(result.IsManualOverrideApplied);
    }

    [Fact]
    public void Calculate_OfficialClosed_NoBuyout_ShowsClosed()
    {
        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: true, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null);

        Assert.Equal(EffectiveHoursStatus.Closed, result.Status);
        Assert.Null(result.Opening);
        Assert.Null(result.NorthGateOpening);
    }

    [Fact]
    public void Calculate_OfficialClosed_ManualOverrideAloneDoesNotReopenIt()
    {
        var manualOpening = new TimeOnly(11, 0);

        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: true, null, null,
            manualOverrideEnabled: true, manualOpening, new TimeOnly(19, 0),
            privateBuyoutEnabled: false, null, null);

        // Spec §8.2 names the private-buyout override, not the manual-hours override, as the
        // mechanism that can show hours on an officially closed date.
        Assert.Equal(EffectiveHoursStatus.Closed, result.Status);
    }

    [Fact]
    public void Calculate_PrivateBuyoutEnabled_OverridesClosed()
    {
        var buyoutOpening = new TimeOnly(18, 0);
        var buyoutClosing = new TimeOnly(23, 0);

        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: true, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: true, buyoutOpening, buyoutClosing);

        Assert.Equal(EffectiveHoursStatus.Open, result.Status);
        Assert.Equal(buyoutOpening, result.Opening);
        Assert.Equal(buyoutClosing, result.Closing);
        Assert.True(result.IsPrivateBuyoutApplied);
        Assert.False(result.IsManualOverrideApplied);
    }

    [Fact]
    public void Calculate_PrivateBuyoutEnabled_TakesPrecedenceOverManualOverrideToo()
    {
        var buyoutOpening = new TimeOnly(18, 0);
        var buyoutClosing = new TimeOnly(23, 0);

        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: false, OfficialOpening, OfficialClosing,
            manualOverrideEnabled: true, new TimeOnly(11, 0), new TimeOnly(20, 0),
            privateBuyoutEnabled: true, buyoutOpening, buyoutClosing);

        Assert.Equal(buyoutOpening, result.Opening);
        Assert.True(result.IsPrivateBuyoutApplied);
    }

    [Fact]
    public void Calculate_NothingVerifiedAndNoOverride_ReportsNotCheckedYet()
    {
        var result = ParkHoursCalculator.Calculate(
            officialIsClosed: false, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null);

        Assert.Equal(EffectiveHoursStatus.NotCheckedYet, result.Status);
        Assert.Null(result.Opening);
    }
}
