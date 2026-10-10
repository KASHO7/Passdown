using Avalonia;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 13E Part AL: construction/layout evidence for the redesigned Manual Hours/Private Buyout
/// time-entry fields at the two supported desktop targets, across the representative states Part AL
/// calls out. No pixel/bitmap renderer exists in this environment (same documented limitation as
/// every prior phase's layout coverage, e.g. <c>OperationsCalendarViewTests</c>) — a real
/// Measure/Arrange pass proving layout does not throw is construction/layout-level evidence, not a
/// pixel-level PASS.
/// </summary>
public class AssociateViewOperationalTimeLayoutTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static async Task<AssociateViewModel> CreateLoadedViewModelAsync()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow));
        await viewModel.PendingLoad;
        return viewModel;
    }

    private static void AssertLayoutDoesNotThrow(AssociateViewModel viewModel, double width, double height)
    {
        var view = new AssociateView { DataContext = viewModel, Width = width, Height = height };

        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));

        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task ManualHoursDisabled_LayoutDoesNotThrow(double width, double height)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.ManualHoursOverrideEnabled = false;
        viewModel.PrivateBuyoutEnabled = false;

        AssertLayoutDoesNotThrow(viewModel, width, height);
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task ManualHoursEnabledValid_LayoutDoesNotThrow(double width, double height)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";

        AssertLayoutDoesNotThrow(viewModel, width, height);
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task ManualHoursEnabledInvalid_LongValidationMessage_LayoutDoesNotThrow(double width, double height)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "this is not a recognizable operating time at all";

        Assert.NotNull(viewModel.ManualOpeningTimeError);
        AssertLayoutDoesNotThrow(viewModel, width, height);
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task PrivateBuyoutEnabledValid_LayoutDoesNotThrow(double width, double height)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";
        viewModel.PrivateBuyoutDetails = "Acme Corp holiday party";

        AssertLayoutDoesNotThrow(viewModel, width, height);
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task ManualHoursAndPrivateBuyout_BothVisibleWithRangeWarning_LayoutDoesNotThrow(double width, double height)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "10:00 PM";
        viewModel.ManualClosingTimeText = "9:00 AM"; // close before open — the non-blocking range warning.
        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";

        Assert.NotNull(viewModel.ManualHoursWarningText);
        AssertLayoutDoesNotThrow(viewModel, width, height);
    }
}
