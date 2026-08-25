using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 9D Part Y: behavioral coverage for <see cref="PassdownHistoryViewModel"/> — the read-only
/// history screen's state and its Open Final PDF/Save PDF Copy commands. Never touches SQLite/Avalonia
/// storage/OS shell — every dependency is a scriptable fake.
/// </summary>
public class PassdownHistoryViewModelTests
{
    private static readonly DateOnly DateA = new(2026, 8, 25);
    private static readonly DateOnly DateB = new(2026, 8, 24);

    private static FinalizedPassdownSnapshot BuildSnapshot(DateOnly date, string? privateBuyoutDetails = null) => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        date,
        date.ToString("dddd, MMMM d, yyyy"),
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "9:00 AM - 9:00 PM", "Open", "9:00 AM", "9:15 AM", "Verified", privateBuyoutDetails),
        new FinalizedAttendanceSnapshot(12000, 300),
        new FinalizedEarlyRideTimeSnapshot(true, "7:30 AM entry", ["Fury 325"]),
        new FinalizedDailyFeatureSnapshot("Ride Spotlight", "Fury 325"),
        ["Gate 3 closed for maintenance."],
        [
            new FinalizedProductCategorySnapshot("Dated Admission", FinalizedProductSectionTier.KeySelling,
                [new FinalizedProductRowSnapshot("1-Day Ticket", "$54.99", "$64.99", null, null)]),
            new FinalizedProductCategorySnapshot("Custom / Internal", FinalizedProductSectionTier.Reference,
                [new FinalizedProductRowSnapshot("Staff Meal Voucher", "$8.00", null, null, "Cafeteria only.")]),
        ],
        false,
        "8/25/2026 5:58 PM");

    private static PassdownHistoryDetail BuildDetail(DateOnly date, bool hasFrozenPdf = true, string? privateBuyoutDetails = null) => new(
        new FinalizedPassdownSummary(date, new DateTimeOffset(2026, 8, 26, 10, 2, 0, TimeSpan.Zero), FinalizationOrigin.Scheduled, hasFrozenPdf, hasFrozenPdf ? 12345 : null),
        BuildSnapshot(date, privateBuyoutDetails),
        null);

    private static PassdownHistoryViewModel CreateViewModel(
        FakePassdownHistoryService? historyService = null,
        FakeFinalizedPassdownPdfRetrievalService? pdfRetrievalService = null,
        FakePdfSaveFilePicker? pdfSavePicker = null,
        FakeFinalizedPdfLauncher? pdfLauncher = null) =>
        new(
            historyService ?? new FakePassdownHistoryService(),
            pdfRetrievalService ?? new FakeFinalizedPassdownPdfRetrievalService(),
            pdfSavePicker: pdfSavePicker ?? new FakePdfSaveFilePicker(),
            pdfLauncher: pdfLauncher ?? new FakeFinalizedPdfLauncher());

    // 3: empty history state.
    [Fact]
    public async Task NoFinalizedPassdowns_ShowsEmptyState()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;

        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasHistory);
        Assert.Empty(viewModel.HistoryItems);
    }

    // 1: newest-first (the service already sorts; the view model must not reorder/filter what it returns).
    [Fact]
    public async Task HistoryItems_ReflectsServiceOrder_NewestFirst()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries =
            [
                new FinalizedPassdownSummary(DateB, DateTimeOffset.UtcNow, FinalizationOrigin.Recovery, true, 100),
                new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100),
            ],
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.False(viewModel.ShowEmptyState);
        Assert.Equal([DateA, DateB], viewModel.HistoryItems.Select(item => item.PassdownDate));
        Assert.Equal("Scheduled", viewModel.HistoryItems[0].OriginText);
        Assert.Equal("Recovery", viewModel.HistoryItems[1].OriginText);
    }

    // 4/7/8/9/10/11/12/13: selecting a row loads the frozen snapshot with every captured field intact.
    [Fact]
    public async Task SelectingItem_LoadsFrozenSnapshot_WithFullDetail()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[DateA] = BuildDetail(DateA, privateBuyoutDetails: "Private buyout 6-9 PM.");
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        Assert.True(viewModel.HasSnapshot);
        var snapshot = viewModel.SelectedDetail!.Snapshot!;
        Assert.Equal("Private buyout 6-9 PM.", snapshot.ParkHours.PrivateBuyoutDetails);
        Assert.Equal(12000, snapshot.Attendance.ProjectedAttendance);
        Assert.Equal(300, snapshot.Attendance.PreSolds);
        Assert.Equal("Ride Spotlight", snapshot.DailyFeature.TypeLabel);
        Assert.Equal(["Gate 3 closed for maintenance."], snapshot.OperationalNotes);
        Assert.Equal("1-Day Ticket", snapshot.ProductCategories[0].Products[0].DisplayName);
        Assert.Equal("$54.99", snapshot.ProductCategories[0].Products[0].EffectivePriceText);
        Assert.Equal("$64.99", snapshot.ProductCategories[0].Products[0].ComparisonPriceText);
        Assert.Equal("Staff Meal Voucher", snapshot.ProductCategories[1].Products[0].DisplayName);
    }

    // 25: repository/service failure produces a UI error state, both for the list and for a detail load.
    [Fact]
    public async Task ListAsync_Throws_SetsListErrorMessage_AndDoesNotCrash()
    {
        var service = new FakePassdownHistoryService { ListException = new InvalidOperationException("boom") };
        var viewModel = CreateViewModel(service);

        await viewModel.PendingLoad;

        Assert.False(string.IsNullOrEmpty(viewModel.ListErrorMessage));
        Assert.False(viewModel.ShowEmptyState);
    }

    [Fact]
    public async Task GetAsync_Throws_SetsDetailErrorMessage_AndDoesNotCrash()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
            GetException = new InvalidOperationException("boom"),
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        Assert.False(string.IsNullOrEmpty(viewModel.DetailErrorMessage));
        Assert.Null(viewModel.SelectedDetail);
    }

    // 21/22: unsupported schema / malformed snapshot degrade to an explained detail, never a crash —
    // and PDF actions must still work when the record has a frozen PDF.
    [Fact]
    public async Task UnavailableSnapshot_ShowsMetadataAndReason_ButKeepsPdfActionsAvailable()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[DateA] = new PassdownHistoryDetail(
            new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100),
            null,
            "This finalized passdown was created by a newer version of the app.");
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        Assert.False(viewModel.HasSnapshot);
        Assert.Equal("This finalized passdown was created by a newer version of the app.", viewModel.DetailErrorMessage);
        Assert.True(viewModel.OpenFinalPdfCommand.CanExecute(null));
        Assert.True(viewModel.SavePdfCopyCommand.CanExecute(null));
    }

    // 14/15: PDF actions are only enabled when the selected record actually has a frozen PDF.
    [Fact]
    public async Task HasFrozenPdfFalse_DisablesPdfActions()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, false, null)],
        };
        service.Details[DateA] = BuildDetail(DateA, hasFrozenPdf: false);
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        Assert.False(viewModel.OpenFinalPdfCommand.CanExecute(null));
        Assert.False(viewModel.SavePdfCopyCommand.CanExecute(null));
        Assert.Equal("Exact historical PDF unavailable", viewModel.HistoryItems[0].PdfAvailabilityText);
    }

    [Fact]
    public async Task HasFrozenPdfTrue_EnablesPdfActions()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[DateA] = BuildDetail(DateA);
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        Assert.True(viewModel.OpenFinalPdfCommand.CanExecute(null));
        Assert.True(viewModel.SavePdfCopyCommand.CanExecute(null));
    }

    // 16/17/20: Open Final PDF goes through the retrieval service only, never a generator — proven by
    // the fake launcher receiving exactly the bytes the retrieval service returned.
    [Fact]
    public async Task OpenFinalPdfCommand_UsesRetrievalServiceOnly_AndOpensExactBytes()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 4)],
        };
        service.Details[DateA] = BuildDetail(DateA);
        var pdfRetrieval = new FakeFinalizedPassdownPdfRetrievalService();
        var launcher = new FakeFinalizedPdfLauncher();
        var viewModel = CreateViewModel(service, pdfRetrieval, pdfLauncher: launcher);
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        await viewModel.OpenFinalPdfCommand.ExecuteAsync(null);

        Assert.Equal([DateA], pdfRetrieval.Calls);
        var call = Assert.Single(launcher.Calls);
        Assert.Equal(FakeFinalizedPassdownPdfRetrievalService.Default(DateA).PdfBytes, call.Bytes);
        // 19: suggested filename uses PassdownDate.
        Assert.Equal("Carowinds-Passdown-2026-08-25.pdf", call.SuggestedFileName);
        Assert.Null(viewModel.PdfActionMessage);
    }

    // 18/19: Save PDF Copy writes the exact stored bytes with a date-stamped filename, via the same
    // native save-file picker abstraction Create PDF already uses.
    [Fact]
    public async Task SavePdfCopyCommand_WritesExactStoredBytes_WithDateStampedFileName()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 4)],
        };
        service.Details[DateA] = BuildDetail(DateA);
        var picker = new FakePdfSaveFilePicker();
        var viewModel = CreateViewModel(service, pdfSavePicker: picker);
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        await viewModel.SavePdfCopyCommand.ExecuteAsync(null);

        var call = Assert.Single(picker.Calls);
        Assert.Equal(FakeFinalizedPassdownPdfRetrievalService.Default(DateA).PdfBytes, call.Bytes);
        Assert.Equal("Carowinds-Passdown-2026-08-25.pdf", call.SuggestedFileName);
        Assert.Equal("PDF saved.", viewModel.PdfActionMessage);
    }

    // 26: save cancellation is harmless.
    [Fact]
    public async Task SavePdfCopyCommand_Cancelled_ProducesNoErrorMessage()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 4)],
        };
        service.Details[DateA] = BuildDetail(DateA);
        var picker = new FakePdfSaveFilePicker { ResultFactory = PdfSaveResult.Cancelled };
        var viewModel = CreateViewModel(service, pdfSavePicker: picker);
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        await viewModel.SavePdfCopyCommand.ExecuteAsync(null);

        Assert.Null(viewModel.PdfActionMessage);
        Assert.False(viewModel.IsSavingPdf);
    }

    // Spec Part U: a stored-size/bytes mismatch is treated as a support-facing error, never silently opened/saved.
    [Fact]
    public async Task PdfSizeMismatch_ProducesSupportError_AndNeverReachesLauncherOrPicker()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(DateA, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 999)],
        };
        service.Details[DateA] = BuildDetail(DateA);
        var pdfRetrieval = new FakeFinalizedPassdownPdfRetrievalService
        {
            ResultFactory = date => new FinalizedPassdownPdfRetrievalResult([1, 2, 3], "Carowinds-Passdown-2026-08-25.pdf", "deadbeef", 999),
        };
        var launcher = new FakeFinalizedPdfLauncher();
        var picker = new FakePdfSaveFilePicker();
        var viewModel = CreateViewModel(service, pdfRetrieval, picker, launcher);
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        await viewModel.OpenFinalPdfCommand.ExecuteAsync(null);

        Assert.Empty(launcher.Calls);
        Assert.Equal("This PDF's stored data looks inconsistent. Contact support.", viewModel.PdfActionMessage);
    }

    // 27: no delete/update/finalize/edit command exists anywhere on the History view model.
    [Fact]
    public void PassdownHistoryViewModel_DefinesNoMutatingCommand()
    {
        var forbidden = new[] { "Delete", "Remove", "Update", "Edit", "Finalize", "Save" };
        var commandProperties = typeof(PassdownHistoryViewModel).GetProperties()
            .Where(p => p.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToArray();

        Assert.Contains("OpenFinalPdfCommand", commandProperties);
        Assert.Contains("SavePdfCopyCommand", commandProperties);
        Assert.DoesNotContain(commandProperties, name =>
            forbidden.Any(word => name.Contains(word, StringComparison.Ordinal)) && name != "SavePdfCopyCommand");
    }

    // 28: no network/refresh/associate-persistence dependency exists on the constructor at all.
    [Fact]
    public void PassdownHistoryViewModel_ConstructorHasNoRefreshOrAssociateDependency()
    {
        var constructor = typeof(PassdownHistoryViewModel).GetConstructors().Single();
        var parameterTypeNames = constructor.GetParameters().Select(p => p.ParameterType.Name).ToArray();

        Assert.DoesNotContain(parameterTypeNames, name => name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypeNames, name => name.Contains("AssociatePassdownService", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypeNames, name => name.Contains("Generator", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypeNames, name => name.Contains("CatalogProduct", StringComparison.OrdinalIgnoreCase));
    }
}
