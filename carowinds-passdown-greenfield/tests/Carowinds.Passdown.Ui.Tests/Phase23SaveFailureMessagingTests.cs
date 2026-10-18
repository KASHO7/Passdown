using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 23: a failed local save says "Save failed — changes not saved" (with its reason available)
/// instead of the ordinary "Unsaved changes", while dirty state, retry-on-next-change and the stored
/// draft behave exactly as before. ViewModel and markup assertions only; runs in the mixed Ui partition.
/// </summary>
public sealed class Phase23SaveFailureMessagingTests
{
    private const string FailedText = "Save failed — changes not saved";

    private static async Task<(AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler)> LoadedAsync()
    {
        var service = new FakeAssociatePassdownService();
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero)), autosaveDelay: scheduler.Delay);
        await viewModel.PendingLoad;
        return (viewModel, service, scheduler);
    }

    private static async Task EditAndSaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler scheduler, string attendance)
    {
        viewModel.ProjectedAttendanceText = attendance;
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    [Fact]
    public async Task APendingEdit_StillReadsUnsavedChanges_AndHasNoFailureDetail()
    {
        var (viewModel, _, _) = await LoadedAsync();

        viewModel.ProjectedAttendanceText = "38,500";

        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState);
        Assert.Equal("Unsaved changes", viewModel.SaveStatusText);
        Assert.Null(viewModel.SaveStatusDetailText);
        Assert.Null(viewModel.SaveFailureReason);
    }

    [Fact]
    public async Task AFailedSave_ShowsSaveFailed_AndKeepsTheReasonAvailable()
    {
        var (viewModel, service, scheduler) = await LoadedAsync();
        service.SaveException = new IOException("The disk is full.");

        await EditAndSaveAsync(viewModel, scheduler, "38,500");

        Assert.Equal(AssociateAutosaveState.SaveFailed, viewModel.AutosaveState);
        Assert.Equal(FailedText, viewModel.SaveStatusText);
        Assert.Equal("The disk is full.", viewModel.SaveFailureReason);
        Assert.Contains("The disk is full.", viewModel.SaveStatusDetailText, StringComparison.Ordinal);
        Assert.Single(service.SaveCalls); // attempted exactly once; nothing stored
        Assert.Null(service.StoredDraft(viewModel.SelectedDate));
    }

    [Fact]
    public async Task AnotherEditAfterAFailure_KeepsTheFailureVisible_UntilASaveSucceeds()
    {
        var (viewModel, service, scheduler) = await LoadedAsync();
        service.SaveException = new IOException("database is locked");
        await EditAndSaveAsync(viewModel, scheduler, "38,500");

        viewModel.ProjectedAttendanceText = "38,600"; // debounce not yet elapsed

        Assert.Equal(FailedText, viewModel.SaveStatusText);
        Assert.Equal("database is locked", viewModel.SaveFailureReason);
    }

    [Fact]
    public async Task TheRetryOnTheNextChange_SavesTheEdit_AndClearsTheFailure()
    {
        var (viewModel, service, scheduler) = await LoadedAsync();
        service.SaveException = new IOException("database is locked");
        await EditAndSaveAsync(viewModel, scheduler, "38,500");

        service.SaveException = null;
        await EditAndSaveAsync(viewModel, scheduler, "38,600");

        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
        Assert.NotEqual(FailedText, viewModel.SaveStatusText);
        Assert.NotEqual("Unsaved changes", viewModel.SaveStatusText);
        Assert.Null(viewModel.SaveFailureReason);
        Assert.Null(viewModel.SaveStatusDetailText);
        Assert.Equal(2, service.SaveCalls.Count);
        Assert.NotNull(service.StoredDraft(viewModel.SelectedDate));
    }

    [Fact]
    public async Task ASuccessfulSave_NeverShowsTheFailureText()
    {
        var (viewModel, service, scheduler) = await LoadedAsync();

        await EditAndSaveAsync(viewModel, scheduler, "38,500");

        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
        Assert.NotEqual(FailedText, viewModel.SaveStatusText);
        Assert.Null(viewModel.SaveStatusDetailText);
        Assert.NotNull(service.StoredDraft(viewModel.SelectedDate));
    }

    [Fact]
    public void AssociateMarkup_ExposesTheFailureReason_AsTooltipAndNarratorHelpText()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        Assert.Contains("AutomationProperties.HelpText=\"{Binding SaveStatusDetailText}\"", markup);
        Assert.Contains("ToolTip.Tip=\"{Binding SaveStatusDetailText}\"", markup);
        Assert.Contains("Text=\"{Binding SaveStatusText, FallbackValue='Not saved yet'}\"", markup);
    }
}
