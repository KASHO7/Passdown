using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 9D Part Z/W: proves <see cref="HistoryView"/> resolves through Avalonia's real XAML loader —
/// including every DataTemplate/StaticResource/converter path — for a populated list, a selected
/// detail, and the empty state, and that its key actions carry an accessible label (spec Part W).
/// </summary>
public class HistoryViewTests
{
    private static readonly DateOnly Date = new(2026, 8, 25);

    [Fact]
    public void HistoryView_EmptyHistory_ConstructsAndAcceptsDataContext()
    {
        var viewModel = new PassdownHistoryViewModel(new FakePassdownHistoryService(), new FakeFinalizedPassdownPdfRetrievalService());

        var view = new HistoryView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    [Fact]
    public async Task HistoryView_WithSelectedDetail_ConstructsAndExposesCriticalAutomationNames()
    {
        var snapshot = new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion, Date, "Tuesday, August 25, 2026",
            new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "9:00 AM - 9:00 PM", "Open", null, null, null, null),
            new FinalizedAttendanceSnapshot(12000, 300),
            new FinalizedEarlyRideTimeSnapshot(false, null, []),
            new FinalizedDailyFeatureSnapshot(null, null),
            ["A note."],
            [new FinalizedProductCategorySnapshot("Dated Admission", FinalizedProductSectionTier.KeySelling, [new FinalizedProductRowSnapshot("1-Day Ticket", "$54.99", null, null, null)])],
            false,
            "8/25/2026 5:58 PM");

        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Date, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[Date] = new Application.History.PassdownHistoryDetail(service.Summaries[0], snapshot, null);

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        var view = new HistoryView { DataContext = viewModel };

        var names = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Finalized passdown history", names);
        Assert.Contains("Open Final PDF", names);
        Assert.Contains("Save PDF Copy", names);
    }

    // Phase 9E Part N: a Recovery-origin record still constructs and renders normally — the origin
    // chip is metadata-only text, not a distinct visual state the view branches on.
    [Fact]
    public async Task HistoryView_RecoveryOriginRecord_ConstructsWithoutError()
    {
        var snapshot = MinimalSnapshot();
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Date, DateTimeOffset.UtcNow, FinalizationOrigin.Recovery, true, 100)],
        };
        service.Details[Date] = new Application.History.PassdownHistoryDetail(service.Summaries[0], snapshot, null);

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        var view = new HistoryView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Phase 9E Part N/R: a legacy pre-9C record (no frozen PDF) constructs without error — the PDF
    // action buttons' IsVisible="{Binding SelectedDetail.Summary.HasFrozenPdf}" (HistoryView.axaml)
    // is what hides them from a real user; that binding path/behavior is already covered at the
    // ViewModel level (PassdownHistoryViewModelTests' HasFrozenPdf/CanExecute coverage), so this test
    // only needs to prove the XAML loader itself handles a false HasFrozenPdf detail cleanly.
    [Fact]
    public async Task HistoryView_NoPdfLegacyRecord_ConstructsWithoutError()
    {
        var snapshot = MinimalSnapshot();
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Date, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, false, null)],
        };
        service.Details[Date] = new Application.History.PassdownHistoryDetail(service.Summaries[0], snapshot, null);

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        var view = new HistoryView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.False(viewModel.SelectedDetail!.Summary.HasFrozenPdf);
    }

    // Phase 9E Part N/H: a malformed/unsupported-schema detail (Snapshot null, UnavailableReason set)
    // still constructs — the detail sections simply don't render, and the error text is shown instead.
    [Fact]
    public async Task HistoryView_MalformedSnapshotDetail_ConstructsWithoutError_ShowsUnavailableReason()
    {
        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Date, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[Date] = new Application.History.PassdownHistoryDetail(
            service.Summaries[0], Snapshot: null, UnavailableReason: "This finalized passdown's stored data could not be read.");

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        var view = new HistoryView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.False(viewModel.HasSnapshot);
        Assert.Equal("This finalized passdown's stored data could not be read.", viewModel.DetailErrorMessage);
    }

    // Phase 9E Part N/Q: long notes/many product rows still construct without error or truncation
    // in the bound collections.
    [Fact]
    public async Task HistoryView_DenseContent_ConstructsWithoutError()
    {
        var longNote = string.Join(" ", Enumerable.Repeat("A fairly long operational note sentence.", 10));
        var manyProducts = Enumerable.Range(1, 25)
            .Select(i => new FinalizedProductRowSnapshot($"Product {i}", $"${i}.99", $"${i + 10}.99", "Online Price", $"Note for product {i}."))
            .ToList();
        var snapshot = new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion, Date, "Tuesday, August 25, 2026",
            new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "9:00 AM - 9:00 PM", "Open", null, null, null, null),
            new FinalizedAttendanceSnapshot(20000, 5000),
            new FinalizedEarlyRideTimeSnapshot(true, "Early Ride Time from 8:00 AM.", ["Fury 325", "Intimidator", "Copperhead Strike"]),
            new FinalizedDailyFeatureSnapshot("Quote of the Day", longNote),
            Enumerable.Range(1, 6).Select(i => $"Operational note {i}: {longNote}").ToList(),
            [new FinalizedProductCategorySnapshot("Dated Admission", FinalizedProductSectionTier.KeySelling, manyProducts)],
            true,
            "8/25/2026 5:58 PM");

        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Date, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[Date] = new Application.History.PassdownHistoryDetail(service.Summaries[0], snapshot, null);

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        var view = new HistoryView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Equal(25, viewModel.SelectedDetail!.Snapshot!.ProductCategories[0].Products.Count);
    }

    private static FinalizedPassdownSnapshot MinimalSnapshot() => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion, Date, "Tuesday, August 25, 2026",
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "9:00 AM - 9:00 PM", "Open", null, null, null, null),
        new FinalizedAttendanceSnapshot(12000, 300),
        new FinalizedEarlyRideTimeSnapshot(false, null, []),
        new FinalizedDailyFeatureSnapshot(null, null),
        ["A note."],
        [new FinalizedProductCategorySnapshot("Dated Admission", FinalizedProductSectionTier.KeySelling, [new FinalizedProductRowSnapshot("1-Day Ticket", "$54.99", null, null, null)])],
        false,
        "8/25/2026 5:58 PM");
}
