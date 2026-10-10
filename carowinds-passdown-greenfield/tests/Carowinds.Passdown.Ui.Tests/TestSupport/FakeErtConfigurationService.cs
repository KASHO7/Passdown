using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Deterministic, fully in-memory <see cref="IErtConfigurationService"/> for
/// <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel"/> tests (Phase 17B) — mirrors
/// <see cref="FakeAssociatePassdownService"/>'s style. <see cref="Resolve"/> lets a test's
/// <c>WorkspaceFactory</c> compose <see cref="AssociateWorkspace.EarlyRideTime"/> the same way
/// production's <c>EffectiveErtResolver</c> would, so a Save-then-reload round trip through this fake
/// and <see cref="FakeAssociatePassdownService"/> together behaves like the real two-service flow.
/// </summary>
internal sealed class FakeErtConfigurationService : IErtConfigurationService
{
    private readonly Dictionary<string, MonthlyErtConfiguration> _monthly = [];
    private readonly Dictionary<DateOnly, ErtDateOverride> _overrides = [];

    public List<MonthlyErtDraft> SaveMonthlyCalls { get; } = [];

    public List<ErtDateOverrideDraft> SaveOverrideCalls { get; } = [];

    public List<DateOnly> RemoveOverrideCalls { get; } = [];

    public Task<MonthlyErtConfiguration?> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default) =>
        Task.FromResult(_monthly.GetValueOrDefault(MonthlyErtConfiguration.FormatKey(year, month)));

    public Task<MonthlyErtConfiguration> SaveMonthlyAsync(MonthlyErtDraft draft, CancellationToken cancellationToken = default)
    {
        SaveMonthlyCalls.Add(draft);
        var configuration = new MonthlyErtConfiguration(draft.Year, draft.Month, draft.State, draft.StartTime, draft.Rides, draft.Note, DateTimeOffset.UtcNow);
        _monthly[configuration.YearMonthKey] = configuration;
        return Task.FromResult(configuration);
    }

    public Task<ErtDateOverride?> GetOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_overrides.GetValueOrDefault(operatingDate));

    public Task<ErtDateOverride> SaveOverrideAsync(ErtDateOverrideDraft draft, CancellationToken cancellationToken = default)
    {
        SaveOverrideCalls.Add(draft);
        var dateOverride = new ErtDateOverride(draft.OperatingDate, draft.State, draft.StartTime, draft.Rides, draft.Reason, DateTimeOffset.UtcNow);
        _overrides[draft.OperatingDate] = dateOverride;
        return Task.FromResult(dateOverride);
    }

    public Task RemoveOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        RemoveOverrideCalls.Add(operatingDate);
        _overrides.Remove(operatingDate);
        return Task.CompletedTask;
    }

    /// <summary>Directly seeds a monthly configuration without going through <see cref="SaveMonthlyAsync"/> (so it doesn't count as a "save call") — for arranging a test's starting state.</summary>
    public void SeedMonthly(MonthlyErtConfiguration configuration) => _monthly[configuration.YearMonthKey] = configuration;

    /// <summary>Directly seeds a date override — see <see cref="SeedMonthly"/>.</summary>
    public void SeedOverride(ErtDateOverride dateOverride) => _overrides[dateOverride.OperatingDate] = dateOverride;

    /// <summary>Mirrors production <c>EffectiveErtResolver.ResolveAsync</c> — the one place a test's fake workspace factory computes the effective value for a date from this fake's current stored state.</summary>
    public EffectiveErt Resolve(DateOnly operatingDate)
    {
        var monthly = _monthly.GetValueOrDefault(MonthlyErtConfiguration.FormatKey(operatingDate.Year, operatingDate.Month));
        var dateOverride = _overrides.GetValueOrDefault(operatingDate);
        return ErtPrecedenceResolver.Resolve(monthly?.ToSnapshot(), dateOverride?.ToSnapshot());
    }
}
