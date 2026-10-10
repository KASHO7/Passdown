using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 17B: <see cref="AssociateViewModel"/>-level coverage for the persistent monthly Early Ride
/// Time configuration + optional per-date override, resolved via <see cref="EffectiveErt"/> — replaces
/// the Phase 13F derived-from-park-hours suite this file used to carry (the coupling to
/// <c>EffectiveHours.NorthGateOpening</c>/"North Gate" no longer exists at all). Revised Phase 17D §1/
/// §2/§3: no routine ERT start time is collected/displayed anywhere any more, and the Configure
/// Monthly/Override editors are one single compact panel (<see cref="AssociateViewModel.IsErtEditorOpen"/>)
/// opened by one [Edit] action, rather than two independently-opened panels. Complements
/// <c>ErtPrecedenceResolverTests</c> (pure Domain precedence, no ViewModel) and
/// <c>EffectiveErtResolverTests</c> (Application-layer resolution against real repositories).
/// </summary>
public class AssociateViewModelEarlyRideTimeTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Builds the two fakes without constructing the ViewModel yet — the constructor's own initial
    /// <see cref="AssociateViewModel.SelectedDate"/> assignment triggers a synchronous-completing
    /// fake load (no real I/O to await), so any <c>ertService.Seed*</c> call made only after
    /// construction would arrive too late for that first load to see. Every test that wants seeded
    /// data reflected in the ViewModel's very first load must seed before calling <see cref="CreateViewModel"/>.
    /// </summary>
    private static (FakeAssociatePassdownService Service, FakeErtConfigurationService ErtService) CreateFakes()
    {
        var ertService = new FakeErtConfigurationService();
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with { EarlyRideTime = ertService.Resolve(date) },
        };
        return (service, ertService);
    }

    private static AssociateViewModel CreateViewModel(FakeAssociatePassdownService service, FakeErtConfigurationService ertService) =>
        new(service, new FakeClock(FixedNow), ertConfigurationService: ertService);

    // Spec Part 6/34/35: no monthly configuration and no override — never fabricate a value.
    [Fact]
    public async Task NoConfiguration_ShowsNotConfiguredForTheMonth_AndNeverFabricatesATime()
    {
        var (service, ertService) = CreateFakes();
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsNotConfigured);
        Assert.False(viewModel.EarlyRideTimeIsConfigured);
        Assert.False(viewModel.EarlyRideTimeIsNoErt);
        Assert.Equal("Not configured for August 2026", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 4/12, revised Phase 17D §2: a monthly default in effect for the selected date shows
    // rides only, never a start time.
    [Fact]
    public async Task MonthlyConfigured_ShowsRidesOnly_NeverAStartTime()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], null, FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsConfigured);
        Assert.False(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Fury 325, Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 11: a month explicitly confirmed to have no ERT is distinct from NotConfigured.
    [Fact]
    public async Task MonthlyNoErt_ShowsNone()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.NoErt, null, [], null, FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsNoErt);
        Assert.False(viewModel.EarlyRideTimeIsNotConfigured);
        Assert.Equal("None", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 4/12: a date override wins over the monthly default for that date only.
    [Fact]
    public async Task DateOverride_WinsOverMonthlyDefault()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], null, FixedNow));
        ertService.SeedOverride(new ErtDateOverride(Today, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 38.I: an override may exist even when no monthly configuration exists for its month.
    [Fact]
    public async Task DateOverride_AppliesEvenWithoutAMonthlyConfiguration()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedOverride(new ErtDateOverride(Today, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 32 (maintenance-exception scenario): the day after an override reverts to the
    // monthly default, and the monthly configuration itself is unchanged by the override existing.
    [Fact]
    public async Task DayAfterOverride_ReturnsToMonthlyDefault()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], null, FixedNow));
        ertService.SeedOverride(new ErtDateOverride(Today, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;

        Assert.False(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Fury 325, Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 29: switching to a new month never inherits the prior month's configuration.
    [Fact]
    public async Task MonthChange_DoesNotInheritThePreviousMonthsConfiguration()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;
        Assert.True(viewModel.EarlyRideTimeIsConfigured);

        viewModel.SelectedDate = new DateOnly(2026, 9, 1);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsNotConfigured);
        Assert.Equal("Not configured for September 2026", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 30: the same month number in a different year is an independent configuration.
    [Fact]
    public async Task SameMonthNextYear_IsIndependent()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        viewModel.SelectedDate = new DateOnly(2027, 8, 21);
        await viewModel.PendingLoad;

        Assert.True(viewModel.EarlyRideTimeIsNotConfigured);
    }

    // Spec Part 7/9, revised Phase 17D §1: Save Monthly persists rides only (no start time collected
    // or sent) through IErtConfigurationService and refreshes the resolved display — all without a
    // debounce round trip. Phase 17D §3: the single compact editor panel stays open after Save so the
    // associate can also use the Date Override section in the same panel.
    [Fact]
    public async Task SaveMonthlyErtCommand_ValidInput_SavesAndRefreshesDisplay()
    {
        var (service, ertService) = CreateFakes();
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        await viewModel.OpenErtEditorCommand.ExecuteAsync(null);
        viewModel.MonthlyErtRidesText = "Fury 325\nCopperhead Strike";
        await viewModel.SaveMonthlyErtCommand.ExecuteAsync(null);

        var call = Assert.Single(ertService.SaveMonthlyCalls);
        Assert.Equal(2026, call.Year);
        Assert.Equal(8, call.Month);
        Assert.Equal(ErtConfigurationState.Configured, call.State);
        Assert.Null(call.StartTime);
        Assert.Equal(["Fury 325", "Copperhead Strike"], call.Rides);

        Assert.True(viewModel.IsErtEditorOpen); // the panel stays open — it is one shared panel with independent Save actions.
        Assert.Equal("Fury 325, Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 11: "No ERT this month" saves without requiring (or sending) a start time.
    [Fact]
    public async Task SaveMonthlyErtCommand_NoErtChecked_SavesWithoutAStartTime()
    {
        var (service, ertService) = CreateFakes();
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        await viewModel.OpenErtEditorCommand.ExecuteAsync(null);
        viewModel.MonthlyErtNoErt = true;
        await viewModel.SaveMonthlyErtCommand.ExecuteAsync(null);

        var call = Assert.Single(ertService.SaveMonthlyCalls);
        Assert.Equal(ErtConfigurationState.NoErt, call.State);
        Assert.Null(call.StartTime);
        Assert.True(viewModel.EarlyRideTimeIsNoErt);
    }

    // Spec Part 8, revised Phase 17D §1/§3: Save Override persists a date-scoped record (rides only,
    // no start time) independent of the monthly one, from the same compact editor panel.
    [Fact]
    public async Task SaveOverrideCommand_ValidInput_SavesAndShowsOverride()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        await viewModel.OpenErtEditorCommand.ExecuteAsync(null);
        viewModel.ErtUseOverrideForDate = true;
        viewModel.OverrideRidesText = "Copperhead Strike";
        viewModel.OverrideReasonText = "Fury maintenance";
        await viewModel.SaveOverrideCommand.ExecuteAsync(null);

        var call = Assert.Single(ertService.SaveOverrideCalls);
        Assert.Equal(Today, call.OperatingDate);
        Assert.Null(call.StartTime);
        Assert.Equal(["Copperhead Strike"], call.Rides);
        Assert.Equal("Fury maintenance", call.Reason);

        Assert.True(viewModel.IsErtEditorOpen); // the panel stays open, same as Save Monthly.
        Assert.True(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 8/28: removing an override immediately falls back to the monthly default.
    [Fact]
    public async Task RemoveOverrideCommand_FallsBackToMonthlyDefault()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, FixedNow));
        ertService.SeedOverride(new ErtDateOverride(Today, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;
        Assert.True(viewModel.EarlyRideTimeIsOverride);

        await viewModel.RemoveOverrideCommand.ExecuteAsync(null);

        Assert.Equal(Today, Assert.Single(ertService.RemoveOverrideCalls));
        Assert.False(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Fury 325", viewModel.EarlyRideTimeStatusText);
    }

    // Spec Part 7, revised Phase 17D §3: opening the single compact editor pre-fills the Monthly
    // Default section from the month's own saved record — not from whatever is currently in effect
    // (which may be coming from a date override) — and pre-fills/pre-checks the Date Override section
    // from any existing override, in the same open operation.
    [Fact]
    public async Task OpenErtEditor_PreFillsMonthlyFromItsOwnRecord_AndOverrideFromTheExistingOverride()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, FixedNow));
        ertService.SeedOverride(new ErtDateOverride(Today, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        await viewModel.OpenErtEditorCommand.ExecuteAsync(null);

        Assert.Contains("Fury 325", viewModel.MonthlyErtRidesText);
        Assert.True(viewModel.ErtUseOverrideForDate);
        Assert.Contains("Copperhead Strike", viewModel.OverrideRidesText);
        Assert.Equal("Fury maintenance", viewModel.OverrideReasonText);
    }

    // Phase 17D §3: with no existing override, opening the editor leaves the "Use override" checkbox
    // unchecked rather than pre-selecting it.
    [Fact]
    public async Task OpenErtEditor_NoExistingOverride_LeavesUseOverrideUnchecked()
    {
        var (service, ertService) = CreateFakes();
        ertService.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, FixedNow));
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;

        await viewModel.OpenErtEditorCommand.ExecuteAsync(null);

        Assert.False(viewModel.ErtUseOverrideForDate);
    }

    // Reading/editing ERT-related properties is local composition only — never a network call.
    [Fact]
    public async Task EarlyRideTimeDisplay_NeverTriggersOnlineRetrieval()
    {
        var (service, ertService) = CreateFakes();
        var viewModel = CreateViewModel(service, ertService);
        await viewModel.PendingLoad;
        service.CheckOnlineCalls.Clear();

        _ = viewModel.EarlyRideTimeStatusText;
        _ = viewModel.EarlyRideTimeIsConfigured;
        _ = viewModel.EarlyRideTimeIsOverride;

        Assert.Empty(service.CheckOnlineCalls);
    }
}
