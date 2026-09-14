using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Domain.Tests.Ert;

/// <summary>Covers <see cref="ErtPrecedenceResolver.Resolve"/> — pure precedence logic, no I/O (Phase 17B spec Part 4/37/38).</summary>
public class ErtPrecedenceResolverTests
{
    private static readonly MonthlyErtSnapshot ConfiguredMonthly = new(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], "Monthly note.");
    private static readonly MonthlyErtSnapshot NoErtMonthly = new(2026, 8, ErtConfigurationState.NoErt, null, [], null);
    private static readonly ErtOverrideSnapshot ConfiguredOverride = new(new DateOnly(2026, 8, 19), ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance");
    private static readonly ErtOverrideSnapshot NoErtOverride = new(new DateOnly(2026, 8, 19), ErtConfigurationState.NoErt, null, [], "Rain closure");

    // 37A: no monthly config → NotConfigured.
    [Fact]
    public void NoMonthlyAndNoOverride_ReturnsNotConfigured()
    {
        var result = ErtPrecedenceResolver.Resolve(monthly: null, dateOverride: null);

        Assert.Equal(EffectiveErtKind.NotConfigured, result.Kind);
        Assert.Null(result.Source);
        Assert.Null(result.StartTime);
        Assert.Empty(result.Rides);
        Assert.Null(result.Note);
    }

    // 37B: monthly config only → effective.
    [Fact]
    public void MonthlyOnly_ReturnsConfiguredFromMonthly()
    {
        var result = ErtPrecedenceResolver.Resolve(ConfiguredMonthly, dateOverride: null);

        Assert.Equal(EffectiveErtKind.Configured, result.Kind);
        Assert.Equal(EffectiveErtSource.Monthly, result.Source);
        Assert.Equal(new TimeOnly(9, 30), result.StartTime);
        Assert.Equal(["Fury 325", "Copperhead Strike"], result.Rides);
        Assert.Equal("Monthly note.", result.Note);
    }

    // 38B/38.I: an override wins, even without a monthly configuration.
    [Fact]
    public void OverrideWithoutMonthly_ReturnsConfiguredFromOverride()
    {
        var result = ErtPrecedenceResolver.Resolve(monthly: null, ConfiguredOverride);

        Assert.Equal(EffectiveErtKind.Configured, result.Kind);
        Assert.Equal(EffectiveErtSource.Override, result.Source);
        Assert.Equal(new TimeOnly(10, 0), result.StartTime);
        Assert.Equal(["Copperhead Strike"], result.Rides);
        Assert.Equal("Fury maintenance", result.Note);
    }

    // 38B: override wins over an existing monthly default.
    [Fact]
    public void OverrideAndMonthly_OverrideWins()
    {
        var result = ErtPrecedenceResolver.Resolve(ConfiguredMonthly, ConfiguredOverride);

        Assert.Equal(EffectiveErtSource.Override, result.Source);
        Assert.Equal(new TimeOnly(10, 0), result.StartTime);
        Assert.Equal(["Copperhead Strike"], result.Rides);
    }

    // 37D: NoErt at the monthly level.
    [Fact]
    public void MonthlyNoErt_ReturnsNoErt()
    {
        var result = ErtPrecedenceResolver.Resolve(NoErtMonthly, dateOverride: null);

        Assert.Equal(EffectiveErtKind.NoErt, result.Kind);
        Assert.Equal(EffectiveErtSource.Monthly, result.Source);
        Assert.Null(result.StartTime);
        Assert.Empty(result.Rides);
    }

    // 38H: NoErt at the override level — wins over a configured monthly default.
    [Fact]
    public void OverrideNoErt_WinsOverConfiguredMonthly()
    {
        var result = ErtPrecedenceResolver.Resolve(ConfiguredMonthly, NoErtOverride);

        Assert.Equal(EffectiveErtKind.NoErt, result.Kind);
        Assert.Equal(EffectiveErtSource.Override, result.Source);
        Assert.Equal("Rain closure", result.Note);
    }

    // 32/38C: the date after an override reverts to the monthly default (modeled by simply passing no override).
    [Fact]
    public void NoOverrideForThisDate_FallsBackToMonthly()
    {
        var result = ErtPrecedenceResolver.Resolve(ConfiguredMonthly, dateOverride: null);

        Assert.Equal(EffectiveErtSource.Monthly, result.Source);
        Assert.Equal(new TimeOnly(9, 30), result.StartTime);
    }
}
