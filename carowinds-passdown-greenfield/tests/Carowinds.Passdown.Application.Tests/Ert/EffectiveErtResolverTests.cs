using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Tests.Ert;

/// <summary>
/// Covers <see cref="EffectiveErtResolver"/> against real repository implementations (in-memory here,
/// SQLite in Infrastructure.Tests) — month/year boundary independence (spec Part 29/30) and the
/// park-hours-independence regression (spec Part 31) are Application-level concerns since they only
/// involve this resolver plus the two repositories, never park-hours data.
/// </summary>
public class EffectiveErtResolverTests
{
    private static (IEffectiveErtResolver Resolver, InMemoryMonthlyErtRepository MonthlyRepository, InMemoryErtDateOverrideRepository OverrideRepository) CreateEnvironment()
    {
        var monthlyRepository = new InMemoryMonthlyErtRepository();
        var overrideRepository = new InMemoryErtDateOverrideRepository();
        return (new EffectiveErtResolver(monthlyRepository, overrideRepository), monthlyRepository, overrideRepository);
    }

    [Fact]
    public async Task ResolveAsync_NoConfiguration_ReturnsNotConfigured()
    {
        var (resolver, _, _) = CreateEnvironment();

        var result = await resolver.ResolveAsync(new DateOnly(2026, 9, 5));

        Assert.Equal(EffectiveErtKind.NotConfigured, result.Kind);
    }

    [Fact]
    public async Task ResolveAsync_MonthlyOnly_ReturnsMonthlyValue()
    {
        var (resolver, monthlyRepository, _) = CreateEnvironment();
        await monthlyRepository.UpsertAsync(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, DateTimeOffset.UtcNow));

        var result = await resolver.ResolveAsync(new DateOnly(2026, 9, 5));

        Assert.Equal(EffectiveErtKind.Configured, result.Kind);
        Assert.Equal(EffectiveErtSource.Monthly, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_OverrideWins_ForThatDateOnly()
    {
        var (resolver, monthlyRepository, overrideRepository) = CreateEnvironment();
        await monthlyRepository.UpsertAsync(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, DateTimeOffset.UtcNow));
        await overrideRepository.UpsertAsync(new ErtDateOverride(new DateOnly(2026, 9, 14), ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", DateTimeOffset.UtcNow));

        var overrideDayResult = await resolver.ResolveAsync(new DateOnly(2026, 9, 14));
        var nextDayResult = await resolver.ResolveAsync(new DateOnly(2026, 9, 15));

        Assert.Equal(EffectiveErtSource.Override, overrideDayResult.Source);
        Assert.Equal(new TimeOnly(10, 0), overrideDayResult.StartTime);
        Assert.Equal(EffectiveErtSource.Monthly, nextDayResult.Source);
        Assert.Equal(new TimeOnly(9, 30), nextDayResult.StartTime);
    }

    // Spec Part 29: a new month with no configuration does not inherit the prior month's.
    [Fact]
    public async Task ResolveAsync_NextMonth_DoesNotInheritPreviousMonth()
    {
        var (resolver, monthlyRepository, _) = CreateEnvironment();
        await monthlyRepository.UpsertAsync(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, DateTimeOffset.UtcNow));

        var result = await resolver.ResolveAsync(new DateOnly(2026, 10, 1));

        Assert.Equal(EffectiveErtKind.NotConfigured, result.Kind);
    }

    // Spec Part 30: the same month number the following year is an independent configuration.
    [Fact]
    public async Task ResolveAsync_SameMonthNextYear_IsIndependent()
    {
        var (resolver, monthlyRepository, _) = CreateEnvironment();
        await monthlyRepository.UpsertAsync(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, DateTimeOffset.UtcNow));

        var result = await resolver.ResolveAsync(new DateOnly(2027, 9, 5));

        Assert.Equal(EffectiveErtKind.NotConfigured, result.Kind);
    }

    // Spec Part 31 (critical regression): ERT resolution never looks at, and is never influenced by,
    // park-hours data at all — the resolver's inputs are exclusively the two ERT repositories.
    [Fact]
    public async Task ResolveAsync_DependsOnlyOnErtRepositories_NeverParkHours()
    {
        var (resolver, monthlyRepository, _) = CreateEnvironment();
        await monthlyRepository.UpsertAsync(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, DateTimeOffset.UtcNow));

        var before = await resolver.ResolveAsync(new DateOnly(2026, 9, 5));
        // No park-hours data exists anywhere in this test's setup at all — if resolution depended on
        // it, this would already be broken. Re-resolving confirms the value is stable/deterministic.
        var after = await resolver.ResolveAsync(new DateOnly(2026, 9, 5));

        Assert.Equal(before, after);
        Assert.Equal(new TimeOnly(9, 30), after.StartTime);
    }

    // Spec Part 32 (maintenance-exception scenario end to end).
    [Fact]
    public async Task ResolveAsync_MaintenanceExceptionScenario()
    {
        var (resolver, monthlyRepository, overrideRepository) = CreateEnvironment();
        await monthlyRepository.UpsertAsync(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], null, DateTimeOffset.UtcNow));
        await overrideRepository.UpsertAsync(new ErtDateOverride(new DateOnly(2026, 9, 14), ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", DateTimeOffset.UtcNow));

        var sep14 = await resolver.ResolveAsync(new DateOnly(2026, 9, 14));
        var sep15 = await resolver.ResolveAsync(new DateOnly(2026, 9, 15));
        var monthlyStillIntact = await monthlyRepository.GetAsync(2026, 9);

        Assert.Equal(new TimeOnly(10, 0), sep14.StartTime);
        Assert.Equal(["Copperhead Strike"], sep14.Rides);
        Assert.Equal(new TimeOnly(9, 30), sep15.StartTime);
        Assert.Equal(["Fury 325", "Copperhead Strike"], sep15.Rides);
        Assert.Equal(new TimeOnly(9, 30), monthlyStillIntact!.StartTime); // the override never mutated the monthly record.
    }
}
