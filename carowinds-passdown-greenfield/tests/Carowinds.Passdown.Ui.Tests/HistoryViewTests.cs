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
}
