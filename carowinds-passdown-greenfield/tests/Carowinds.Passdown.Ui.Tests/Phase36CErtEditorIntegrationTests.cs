using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 36C (docs/phase36c-ert-editor-maintainability.md): the extracted Early Ride Time editor inside the real
/// <see cref="AssociateViewModel"/>. The workspace keeps one selected date, one draft and one autosave lifecycle; the editor
/// reads that date, saves only through the ERT service, and the workspace refresh it requests is the existing one.
/// </summary>
public class Phase36CErtEditorIntegrationTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, FakeErtConfigurationService Ert, ContentEchoPdfExportService Export) Create()
    {
        var ert = new FakeErtConfigurationService();
        ert.SeedMonthly(new MonthlyErtConfiguration(2026, 8, ErtConfigurationState.Configured, null, ["Fury 325"], null, FixedNow));
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with { EarlyRideTime = ert.Resolve(date) },
        };
        var export = new ContentEchoPdfExportService();
        // Autosave with no debounce: any edit that reached the draft would be saved at once, so "no save" is meaningful.
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), ertConfigurationService: ert, autosaveDelay: (_, _) => Task.CompletedTask, pdfExportService: export);
        return (viewModel, service, ert, export);
    }

    [Fact]
    public async Task TheWorkspaceExposesOneStableEditor_AcrossDateSwitches()
    {
        var (viewModel, _, _, _) = Create();
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today.AddMonths(1);
        await viewModel.PendingLoad;

        Assert.Same(editor, viewModel.EarlyRideTimeEditor);
    }

    [Fact]
    public async Task EveryErtAction_NeverSavesTheDraft_NorMarksItUnsaved()
    {
        var (viewModel, service, ert, _) = Create();
        await viewModel.PendingLoad;
        var stateBefore = viewModel.AutosaveState;
        var editor = viewModel.EarlyRideTimeEditor;

        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.MonthlyErtRidesText = "Fury 325\nIntimidator";
        await editor.SaveMonthlyErtCommand.ExecuteAsync(null);
        editor.ErtUseOverrideForDate = true;
        editor.OverrideRidesText = "Copperhead Strike";
        await editor.SaveOverrideCommand.ExecuteAsync(null);
        await editor.RemoveOverrideCommand.ExecuteAsync(null);
        editor.MonthlyErtNoteText = "typed, then closed";
        editor.CancelErtEditorCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Empty(service.SaveCalls);                     // the Passdown draft was never written
        Assert.Equal(stateBefore, viewModel.AutosaveState);   // and never shown as having unsaved edits
        Assert.Single(ert.SaveMonthlyCalls);
        Assert.Single(ert.SaveOverrideCalls);
        Assert.Single(ert.RemoveOverrideCalls);
    }

    [Fact]
    public async Task TheEditorUsesTheWorkspacesSelectedDate()
    {
        var (viewModel, _, ert, _) = Create();
        await viewModel.PendingLoad;
        viewModel.SelectedDate = new DateOnly(2026, 9, 3);
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;

        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.MonthlyErtRidesText = "Intimidator";
        await editor.SaveMonthlyErtCommand.ExecuteAsync(null);
        editor.ErtUseOverrideForDate = true;
        editor.OverrideRidesText = "Copperhead Strike";
        await editor.SaveOverrideCommand.ExecuteAsync(null);

        Assert.Equal((2026, 9), (ert.SaveMonthlyCalls[0].Year, ert.SaveMonthlyCalls[0].Month));
        Assert.Equal(new DateOnly(2026, 9, 3), ert.SaveOverrideCalls[0].OperatingDate);
        Assert.Equal(new DateOnly(2026, 9, 3), viewModel.SelectedDate); // saving never moves the one selected date
    }

    [Fact]
    public async Task SaveMonthly_ReloadsTheWorkspaceExactlyOnce_AndTheStatusShowsTheNewRides()
    {
        var (viewModel, service, ert, _) = Create();
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;
        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        var loadsBefore = service.LoadCalls.Count;

        editor.MonthlyErtRidesText = "Copperhead Strike\nIntimidator";
        await editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        Assert.Single(ert.SaveMonthlyCalls);
        Assert.Equal(loadsBefore + 1, service.LoadCalls.Count); // one workspace refresh, no duplicate service calls
        Assert.Equal(Today, service.LoadCalls[^1]);
        Assert.Equal("Copperhead Strike, Intimidator", viewModel.EarlyRideTimeStatusText);
        Assert.True(viewModel.EarlyRideTimeIsConfigured);
        Assert.True(editor.IsErtEditorOpen);
    }

    [Fact]
    public async Task SaveOverride_TheReloadedWorkspaceCarriesTheOverride()
    {
        var (viewModel, _, _, _) = Create();
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;
        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.ErtUseOverrideForDate = true;
        editor.OverrideRidesText = "Copperhead Strike";

        await editor.SaveOverrideCommand.ExecuteAsync(null);

        var workspace = Assert.IsType<AssociateWorkspace>(viewModel.Workspace);
        Assert.Equal(EffectiveErtSource.Override, workspace.EarlyRideTime.Source);
        Assert.Equal(["Copperhead Strike"], workspace.EarlyRideTime.Rides);
        Assert.True(viewModel.EarlyRideTimeIsOverride);
        Assert.Equal("Copperhead Strike", viewModel.EarlyRideTimeStatusText);
    }

    [Fact]
    public async Task CreatePdf_AfterAnErtSave_GeneratesFromTheReloadedWorkspace()
    {
        var (viewModel, _, _, export) = Create();
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;
        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.MonthlyErtRidesText = "Intimidator";
        await editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        var generated = export.Calls[^1].Workspace;
        Assert.Equal(Today, generated.OperatingDate);
        Assert.Equal(["Intimidator"], generated.EarlyRideTime.Rides);
    }

    [Fact]
    public async Task SwitchingDates_ClosesTheEditor_DiscardsItsUnsavedText_AndSavesNothing()
    {
        var (viewModel, service, ert, _) = Create();
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;
        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.MonthlyErtRidesText = "unsaved";

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;

        Assert.False(editor.IsErtEditorOpen);
        Assert.False(editor.IsDirty);
        Assert.Empty(ert.SaveMonthlyCalls);
        Assert.Empty(service.SaveCalls);

        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        Assert.Equal("Fury 325", editor.MonthlyErtRidesText); // reopened from the stored record, not the discarded text
    }

    [Fact]
    public async Task RapidDateSwitches_ThenOpen_ShowTheFinalDatesValues()
    {
        var (viewModel, _, ert, _) = Create();
        ert.SeedMonthly(new MonthlyErtConfiguration(2026, 10, ErtConfigurationState.NoErt, null, [], null, FixedNow));
        await viewModel.PendingLoad;

        foreach (var date in new[] { new DateOnly(2026, 8, 22), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2) })
        {
            viewModel.SelectedDate = date;
        }

        await viewModel.PendingLoad;
        await viewModel.EarlyRideTimeEditor.OpenErtEditorCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 10, 2), viewModel.SelectedDate);
        Assert.True(viewModel.EarlyRideTimeEditor.MonthlyErtNoErt); // October's record, not August's or September's
        Assert.True(string.IsNullOrEmpty(viewModel.EarlyRideTimeEditor.MonthlyErtRidesText));
    }

    [Fact]
    public async Task ADirtyEditor_DefersAnOfficialDataReload_AndACleanOneDoesNot()
    {
        var (viewModel, service, _, _) = Create();
        await viewModel.PendingLoad;
        var editor = viewModel.EarlyRideTimeEditor;
        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.MonthlyErtRidesText = "unsaved";
        var loadsBefore = service.LoadCalls.Count;

        await viewModel.HandleOfficialDataChangedAsync(new DataChange(DataChangeKind.OfficialData, Today));
        Assert.Equal(loadsBefore, service.LoadCalls.Count); // unsaved ERT edits keep the workspace from being replaced
        Assert.True(editor.IsErtEditorOpen);
        Assert.Equal("unsaved", editor.MonthlyErtRidesText);

        editor.CancelErtEditorCommand.Execute(null);
        await viewModel.HandleOfficialDataChangedAsync(new DataChange(DataChangeKind.OfficialData, Today));
        Assert.Equal(loadsBefore + 1, service.LoadCalls.Count);
    }
}
