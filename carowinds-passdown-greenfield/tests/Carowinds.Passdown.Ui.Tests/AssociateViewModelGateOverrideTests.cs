using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 18D: independent per-date North Gate/Avenue opening-time overrides on the Associate
/// screen — effective display, the explicit override/default distinction (the default is never
/// copied into the override box), Reset, validation, autosave/date-switch/restart persistence, and
/// the PDF export receiving the effective values. Personal Sync / REL-004 coverage for these fields
/// runs on the real two-device stack in <see cref="OpenEditorSyncProtectionTests"/>.
/// Official park hours here are 11:00 AM – 8:00 PM, so the calculated defaults are North Gate
/// 10:30 AM and Avenue 10:15 AM.
/// </summary>
public class AssociateViewModelGateOverrideTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler) CreateViewModel(
        FakeAssociatePassdownPdfExportService? exportService = null)
    {
        var service = new FakeAssociatePassdownService { OfficialHours = (new TimeOnly(11, 0), new TimeOnly(20, 0)) };
        var scheduler = new ManualDebounceScheduler();
        var viewModel = exportService is null
            ? new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay)
            : new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay,
                pdfExportService: exportService, pdfSavePicker: new FakePdfSaveFilePicker());
        return (viewModel, service, scheduler);
    }

    private static async Task ElapseAndAwaitSaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler scheduler)
    {
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    [Fact]
    public async Task NoOverride_ShowsCalculatedDefaults_AndLeavesOverrideBoxesBlank()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;

        Assert.Equal("10:30 AM", viewModel.ParkHours!.NorthGateText);
        Assert.Equal("10:15 AM", viewModel.ParkHours.AvenueText);
        Assert.Null(viewModel.NorthGateOverrideText); // the default is never copied into the override field.
        Assert.Null(viewModel.AvenueOverrideText);
        Assert.False(viewModel.NorthGateIsOverridden);
        Assert.False(viewModel.AvenueIsOverridden);
        Assert.False(viewModel.CanResetNorthGateOverride);
        Assert.False(viewModel.CanResetAvenueOverride);
    }

    [Fact]
    public async Task OnlyAvenueOverridden_ChangesAvenueOnly_AndNormalizesTheTypedText()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.AvenueOverrideText = "10am";

        Assert.Equal("10:00 AM", viewModel.AvenueOverrideText);
        Assert.Equal("10:00 AM", viewModel.ParkHours!.AvenueText);
        Assert.Equal("10:30 AM", viewModel.ParkHours.NorthGateText);
        Assert.Equal("11:00 AM – 8:00 PM", viewModel.ParkHours.HeadlineText);
        Assert.True(viewModel.AvenueIsOverridden);
        Assert.False(viewModel.NorthGateIsOverridden);

        await ElapseAndAwaitSaveAsync(viewModel, scheduler);
        var saved = service.SaveCalls.Last().Draft;
        Assert.Equal(new TimeOnly(10, 0), saved.AvenueOpeningOverride);
        Assert.Null(saved.NorthGateOpeningOverride);
        Assert.False(saved.ManualHoursOverride.Enabled);
    }

    [Fact]
    public async Task OnlyNorthGateOverridden_ChangesNorthGateOnly()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.NorthGateOverrideText = "10:00 AM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal("10:00 AM", viewModel.ParkHours!.NorthGateText);
        Assert.Equal("10:15 AM", viewModel.ParkHours.AvenueText);
        Assert.Equal(new TimeOnly(10, 0), service.SaveCalls.Last().Draft.NorthGateOpeningOverride);
        Assert.Null(service.SaveCalls.Last().Draft.AvenueOpeningOverride);
    }

    [Fact]
    public async Task BothOverridden_ThenResetEach_ReturnsEachToItsDefaultIndependently()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.NorthGateOverrideText = "9:45 AM";
        viewModel.AvenueOverrideText = "9:30 AM";
        Assert.Equal("9:45 AM", viewModel.ParkHours!.NorthGateText);
        Assert.Equal("9:30 AM", viewModel.ParkHours.AvenueText);

        viewModel.ResetNorthGateOverrideCommand.Execute(null);

        Assert.Null(viewModel.NorthGateOverrideText);
        Assert.Equal("10:30 AM", viewModel.ParkHours!.NorthGateText);
        Assert.Equal("9:30 AM", viewModel.ParkHours.AvenueText);
        Assert.False(viewModel.NorthGateIsOverridden);
        Assert.True(viewModel.AvenueIsOverridden);

        viewModel.ResetAvenueOverrideCommand.Execute(null);
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal("10:15 AM", viewModel.ParkHours!.AvenueText);
        Assert.Null(service.SaveCalls.Last().Draft.NorthGateOpeningOverride);
        Assert.Null(service.SaveCalls.Last().Draft.AvenueOpeningOverride);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("25:99")]
    [InlineData("10:")]
    public async Task InvalidInput_ShowsError_AndNeverReplacesTheLastValidOverride(string typed)
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;
        viewModel.AvenueOverrideText = "10:00 AM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);
        var saveCount = service.SaveCalls.Count;

        viewModel.AvenueOverrideText = typed;

        Assert.Equal(OperationalTimeParser.InvalidTimeMessage, viewModel.AvenueOverrideError);
        Assert.Equal(typed, viewModel.AvenueOverrideText);
        Assert.Equal("10:00 AM", viewModel.ParkHours!.AvenueText);
        Assert.Null(viewModel.NorthGateOverrideError);
        Assert.Equal(saveCount, service.SaveCalls.Count); // malformed text schedules no save.
        Assert.Equal(new TimeOnly(10, 0), service.StoredDraft(Today)!.AvenueOpeningOverride);
    }

    [Fact]
    public async Task BlankInput_MeansNoOverride_WithoutAnError()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;
        viewModel.NorthGateOverrideText = "10:00 AM";

        viewModel.NorthGateOverrideText = "   ";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Null(viewModel.NorthGateOverrideError);
        Assert.Equal("10:30 AM", viewModel.ParkHours!.NorthGateText);
        Assert.Null(service.SaveCalls.Last().Draft.NorthGateOpeningOverride);
    }

    [Fact]
    public async Task Overrides_SurviveDateNavigation_AndDoNotLeakToOtherDates()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;
        viewModel.AvenueOverrideText = "10:00 AM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        Assert.Null(viewModel.AvenueOverrideText);
        Assert.Equal("10:15 AM", viewModel.ParkHours!.AvenueText);

        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;
        Assert.Equal("10:00 AM", viewModel.AvenueOverrideText);
        Assert.Equal("10:00 AM", viewModel.ParkHours!.AvenueText);
        Assert.Equal("10:30 AM", viewModel.ParkHours.NorthGateText);
        Assert.True(viewModel.AvenueIsOverridden);
    }

    [Fact]
    public async Task Overrides_SurviveARestart_WithANewViewModelOverTheSameStore()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;
        viewModel.NorthGateOverrideText = "9:50 AM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var restarted = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: new ManualDebounceScheduler().Delay);
        await restarted.PendingLoad;

        Assert.Equal("9:50 AM", restarted.NorthGateOverrideText);
        Assert.Equal("9:50 AM", restarted.ParkHours!.NorthGateText);
        Assert.Equal("10:15 AM", restarted.ParkHours.AvenueText);
    }

    [Fact]
    public async Task CreatePdf_ReceivesTheEffectiveGateTimes()
    {
        var exportService = new FakeAssociatePassdownPdfExportService();
        var (viewModel, _, _) = CreateViewModel(exportService);
        await viewModel.PendingLoad;
        viewModel.AvenueOverrideText = "10:00 AM";

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        var effective = Assert.Single(exportService.Calls).ParkHours.Effective;
        Assert.Equal(new TimeOnly(10, 0), effective.AvenueOpening);
        Assert.Equal(new TimeOnly(10, 30), effective.NorthGateOpening);
        Assert.Equal(new TimeOnly(11, 0), effective.Opening);
    }
}
