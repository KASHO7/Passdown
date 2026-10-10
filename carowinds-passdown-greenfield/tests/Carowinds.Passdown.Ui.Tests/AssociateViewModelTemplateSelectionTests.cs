using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 17C §29/§37: the Associate Passdown "PDF Style" template selection — persistence, defaults,
/// and that switching it never touches draft data or the current workspace (spec §17).
/// </summary>
public class AssociateViewModelTemplateSelectionTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static AssociateViewModel CreateViewModel(
        FakeAssociatePassdownService? service = null,
        FakeAssociatePassdownPdfExportService? exportService = null,
        FakeAssociatePassdownTemplatePreferenceStore? templateStore = null) =>
        new(
            service ?? new FakeAssociatePassdownService(),
            new FakeClock(FixedNow),
            pdfExportService: exportService ?? new FakeAssociatePassdownPdfExportService(),
            pdfSavePicker: new FakePdfSaveFilePicker(),
            templatePreferenceStore: templateStore ?? new FakeAssociatePassdownTemplatePreferenceStore());

    [Fact]
    public void TemplateOptions_ExposesExactlyClassicCompactModern()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(
            [AssociatePassdownTemplate.Classic, AssociatePassdownTemplate.Compact, AssociatePassdownTemplate.Modern],
            viewModel.TemplateOptions.Select(o => o.Value));
    }

    [Fact]
    public async Task Constructor_NoPriorPreference_DefaultsToClassic()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingTemplatePreferenceLoad;

        Assert.Equal(AssociatePassdownTemplate.Classic, viewModel.SelectedTemplateOption.Value);
    }

    [Fact]
    public async Task Constructor_LoadsThePreviouslySavedPreference()
    {
        var store = new FakeAssociatePassdownTemplatePreferenceStore { Current = new AssociatePassdownTemplatePreference(AssociatePassdownTemplate.Compact) };
        var viewModel = CreateViewModel(templateStore: store);
        await viewModel.PendingTemplatePreferenceLoad;

        Assert.Equal(AssociatePassdownTemplate.Compact, viewModel.SelectedTemplateOption.Value);
    }

    [Fact]
    public async Task Constructor_LoadingThePreference_DoesNotReSaveIt()
    {
        var store = new FakeAssociatePassdownTemplatePreferenceStore { Current = new AssociatePassdownTemplatePreference(AssociatePassdownTemplate.Modern) };
        var viewModel = CreateViewModel(templateStore: store);
        await viewModel.PendingTemplatePreferenceLoad;

        Assert.Empty(store.SaveCalls);
    }

    [Fact]
    public async Task ChangingSelectedTemplateOption_PersistsImmediately()
    {
        var store = new FakeAssociatePassdownTemplatePreferenceStore();
        var viewModel = CreateViewModel(templateStore: store);
        await viewModel.PendingTemplatePreferenceLoad;

        viewModel.SelectedTemplateOption = AssociatePassdownTemplateOption.For(AssociatePassdownTemplate.Modern);
        await viewModel.PendingTemplatePreferenceSave;

        Assert.Equal([new AssociatePassdownTemplatePreference(AssociatePassdownTemplate.Modern)], store.SaveCalls);
    }

    [Fact]
    public async Task ChangingSelectedTemplateOption_NeverMarksTheDraftUnsavedOrTouchesWorkspace()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;
        var workspaceBeforeChange = viewModel.Workspace;
        var saveStateBeforeChange = viewModel.AutosaveState;

        viewModel.SelectedTemplateOption = AssociatePassdownTemplateOption.For(AssociatePassdownTemplate.Compact);
        await viewModel.PendingTemplatePreferenceSave;

        Assert.Same(workspaceBeforeChange, viewModel.Workspace);
        Assert.Equal(saveStateBeforeChange, viewModel.AutosaveState);
    }

    [Fact]
    public async Task CreatePdfCommand_UsesTheCurrentlySelectedTemplate()
    {
        var exportService = new FakeAssociatePassdownPdfExportService();
        var store = new FakeAssociatePassdownTemplatePreferenceStore();
        var viewModel = CreateViewModel(exportService: exportService, templateStore: store);
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;

        viewModel.SelectedTemplateOption = AssociatePassdownTemplateOption.For(AssociatePassdownTemplate.Compact);
        await viewModel.PendingTemplatePreferenceSave;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Equal([AssociatePassdownTemplate.Compact], exportService.TemplateCalls);
    }

    [Fact]
    public async Task CreatePdfCommand_ChangingTemplateBetweenTwoPreviews_AffectsTheNextPreviewImmediately()
    {
        var exportService = new FakeAssociatePassdownPdfExportService();
        var viewModel = CreateViewModel(exportService: exportService);
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);
        viewModel.SelectedTemplateOption = AssociatePassdownTemplateOption.For(AssociatePassdownTemplate.Modern);
        await viewModel.PendingTemplatePreferenceSave;
        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Equal([AssociatePassdownTemplate.Classic, AssociatePassdownTemplate.Modern], exportService.TemplateCalls);
    }
}
