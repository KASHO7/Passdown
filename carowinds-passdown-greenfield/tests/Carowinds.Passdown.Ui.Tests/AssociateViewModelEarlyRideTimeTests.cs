using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 13F: <see cref="AssociateViewModel"/>-level coverage for the derived Early Ride Time
/// start (<see cref="AssociateViewModel.EarlyRideTimeStartText"/>/<see cref="AssociateViewModel.EarlyRideTimeCaptionText"/>)
/// that replaced manual ERT time entry — normal official hours, live Manual Hours/Private Buyout
/// interaction, Closed/NotPublished/Unavailable non-fabrication, and waterpark independence.
/// Complements <c>ParkHoursCalculatorTests</c> (pure Domain arithmetic, no ViewModel).
/// </summary>
public class AssociateViewModelEarlyRideTimeTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static AssociateViewModel CreateViewModel(FakeAssociatePassdownService? service = null) =>
        new(service ?? new FakeAssociatePassdownService(), new FakeClock(FixedNow));

    private static FakeAssociatePassdownService WithParkHours(
        EffectiveHours effective,
        AssociateVerificationInfo verification,
        bool officialIsClosed = false,
        TimeOnly? officialOpening = null,
        TimeOnly? officialClosing = null,
        TimeOnly? waterparkOpening = null,
        TimeOnly? waterparkClosing = null) => new()
    {
        WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
        {
            ParkHours = new AssociateParkHoursView(effective, verification, officialIsClosed, officialOpening, officialClosing, waterparkOpening, waterparkClosing),
        },
    };

    private static AssociateVerificationInfo Verified() => new(AssociateVerificationState.Verified, FixedNow, null, "Verified.");

    // Part E: normal open day — 10:00 AM opening derives 9:30 AM, identical to North Gate.
    [Fact]
    public async Task NormalOpenDay_DerivesStartThirtyMinutesBeforeOpening()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var service = WithParkHours(effective, Verified(), officialIsClosed: false, officialOpening: new TimeOnly(10, 0), officialClosing: new TimeOnly(22, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Equal("9:30 AM", viewModel.EarlyRideTimeStartText);
        Assert.Equal(viewModel.ParkHours!.NorthGateText, viewModel.EarlyRideTimeStartText);
        Assert.Contains("10:00 AM", viewModel.EarlyRideTimeCaptionText);
    }

    // Part F/Y: typing a Manual Hours override immediately changes the derived start — no save round-trip.
    [Fact]
    public async Task ManualHoursOverride_TypedLive_ImmediatelyChangesDerivedStart()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var service = WithParkHours(effective, Verified(), officialIsClosed: false, officialOpening: new TimeOnly(10, 0), officialClosing: new TimeOnly(22, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;
        Assert.Equal("9:30 AM", viewModel.EarlyRideTimeStartText);

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        viewModel.ManualClosingTimeText = "11:00 PM";

        Assert.Equal("8:30 AM", viewModel.EarlyRideTimeStartText);
    }

    // Part Z: clearing the Manual Hours override reverts the derived start to the official-derived value.
    [Fact]
    public async Task ManualHoursOverride_Disabled_RevertsToOfficialDerivedStart()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var service = WithParkHours(effective, Verified(), officialIsClosed: false, officialOpening: new TimeOnly(10, 0), officialClosing: new TimeOnly(22, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        Assert.Equal("8:30 AM", viewModel.EarlyRideTimeStartText);

        viewModel.ManualHoursOverrideEnabled = false;

        Assert.Equal("9:30 AM", viewModel.EarlyRideTimeStartText);
    }

    // Part G: Private Buyout represents the Associate operating day's effective opening, and North
    // Gate already derives from it unconditionally (ParkHoursCalculator has no buyout carve-out) —
    // Part L forbids ERT from computing a second, independent value, so buyout drives ERT too.
    [Fact]
    public async Task PrivateBuyout_Enabled_DerivesStartFromBuyoutOpening()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";

        Assert.Equal("5:30 PM", viewModel.EarlyRideTimeStartText);
        Assert.Equal(viewModel.ParkHours!.NorthGateText, viewModel.EarlyRideTimeStartText);
    }

    // Part H: officially closed with no buyout override never fabricates a start time.
    [Fact]
    public async Task Closed_NoBuyout_NeverFabricatesStartTime()
    {
        var effective = ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null);
        var service = WithParkHours(effective, new AssociateVerificationInfo(AssociateVerificationState.Closed, FixedNow, null, "Closed."), officialIsClosed: true);
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.EarlyRideTimeStartText);
        Assert.Contains("closed", viewModel.EarlyRideTimeCaptionText, StringComparison.OrdinalIgnoreCase);
    }

    // Part I: hours not yet published never fabricates a start time.
    [Fact]
    public async Task NotPublished_NeverFabricatesStartTime_AndSaysSo()
    {
        var effective = ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);
        var verification = new AssociateVerificationInfo(AssociateVerificationState.NotPublished, null, null, "Not yet published.");
        var service = WithParkHours(effective, verification, officialIsClosed: false);
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.EarlyRideTimeStartText);
        Assert.Contains("published", viewModel.EarlyRideTimeCaptionText, StringComparison.OrdinalIgnoreCase);
    }

    // Part J: source data unavailable never fabricates a start time.
    [Fact]
    public async Task Unavailable_NeverFabricatesStartTime_AndSaysSo()
    {
        var effective = ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);
        var verification = new AssociateVerificationInfo(AssociateVerificationState.Unavailable, null, null, "Source unavailable.");
        var service = WithParkHours(effective, verification, officialIsClosed: false);
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.EarlyRideTimeStartText);
        Assert.Contains("could not be checked", viewModel.EarlyRideTimeCaptionText, StringComparison.OrdinalIgnoreCase);
    }

    // Never-checked-yet (default fake workspace) never fabricates a start time.
    [Fact]
    public async Task NotCheckedYet_NeverFabricatesStartTime()
    {
        var viewModel = CreateViewModel(); // default fake workspace uses NotCheckedYet hours.
        await viewModel.PendingLoad;

        Assert.Null(viewModel.EarlyRideTimeStartText);
        Assert.Contains("have not been checked", viewModel.EarlyRideTimeCaptionText, StringComparison.OrdinalIgnoreCase);
    }

    // Part J/AD: stale last-known-good hours are still an effective opening — ERT derives from them
    // exactly like the Park Hours panel does, with no separate ERT-specific staleness model.
    [Fact]
    public async Task StaleLastKnownGoodHours_StillDerivesStartTime()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var verification = new AssociateVerificationInfo(AssociateVerificationState.Stale, FixedNow.AddDays(-1), TimeSpan.FromDays(1), "Could not be rechecked.");
        var service = WithParkHours(effective, verification, officialIsClosed: false, officialOpening: new TimeOnly(10, 0), officialClosing: new TimeOnly(22, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Equal("9:30 AM", viewModel.EarlyRideTimeStartText);
    }

    // Part K: Carolina Harbor / waterpark opening never drives ERT — only the main gate does.
    [Fact]
    public async Task WaterparkHoursPublished_MainGateNotChecked_NeverDerivesStartTime()
    {
        var effective = ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);
        var service = WithParkHours(
            effective, AssociateVerificationInfo.NotCheckedYet, officialIsClosed: false,
            waterparkOpening: new TimeOnly(11, 0), waterparkClosing: new TimeOnly(19, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.EarlyRideTimeStartText);
    }

    // Part AO: reading/editing ERT-related properties is local composition only — never a network call.
    [Fact]
    public async Task DerivedStart_NeverTriggersOnlineRetrieval()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;
        service.CheckOnlineCalls.Clear();

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        _ = viewModel.EarlyRideTimeStartText;
        _ = viewModel.EarlyRideTimeCaptionText;

        Assert.Empty(service.CheckOnlineCalls);
    }
}
