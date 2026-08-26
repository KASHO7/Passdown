using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 10A Part M/X: proves <see cref="DashboardView"/> resolves through Avalonia's real XAML
/// loader — including every binding/converter path — for a populated snapshot and for the initial
/// (pre-load) state, and that its key actions carry an accessible automation name.
/// </summary>
public class DashboardViewTests
{
    [Fact]
    public void DashboardView_ConstructsAndAcceptsDataContext()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService());

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    [Fact]
    public async Task DashboardView_WithSnapshot_ExposesCriticalAutomationNames()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        var names = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Open Passdown", names);
        Assert.Contains("Open History", names);
        Assert.Contains("Open Notes", names);
        Assert.Contains("Refresh today's operations summary", names);
    }

    // A load failure must render without throwing — no fabricated snapshot content (spec Part W).
    [Fact]
    public async Task DashboardView_LoadFailure_ConstructsWithoutError()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService { Exception = new InvalidOperationException("boom") });
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Dense content (a full 3-line notes preview plus every stat populated) still constructs cleanly.
    [Fact]
    public async Task DashboardView_DenseSnapshot_ConstructsWithoutError()
    {
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                ProjectedAttendance = 18000,
                PreSolds = 4200,
                EarlyRideTimeEnabled = true,
                EarlyRideTimeDetails = "7:30 AM entry via North Gate.",
                DailyFeatureType = DailyFeatureType.QuoteOfTheDay,
                DailyFeatureText = "\"The best view comes after the hardest climb.\"",
                Notes = new DashboardNotesSummary(true, ["Gate 3 closed.", "VIP tour at 11.", "Extra staffing at Fury."], 5),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Phase 10B Part K/Z: a very long Daily Feature and a very long note line must still construct
    // cleanly and render as bounded (truncated) presentation text, not raw multi-paragraph content.
    [Fact]
    public async Task DashboardView_LongDailyFeatureAndNotes_ConstructsWithBoundedText()
    {
        var longFeature = new string('a', 500);
        var longNoteLine = new string('b', 400);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                DailyFeatureType = DailyFeatureType.UselessFactOfTheDay,
                DailyFeatureText = longFeature,
                Notes = new DashboardNotesSummary(true, [longNoteLine], 1),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.True(viewModel.Presentation!.DailyFeatureText.Length < longFeature.Length);
        Assert.True(viewModel.Presentation!.NotesPreviewLines[0].Length < longNoteLine.Length);
    }

    // Phase 10B Part O/Z: NotPublished/Unavailable must construct and never be confused with Open —
    // Closed/CustomHours/PrivateBuyout regressions are covered at the Application layer
    // (OperationsDashboardServiceTests) and reused here, not recalculated.
    [Theory]
    [InlineData(DashboardParkStatus.Closed)]
    [InlineData(DashboardParkStatus.NotPublished)]
    [InlineData(DashboardParkStatus.Unavailable)]
    [InlineData(DashboardParkStatus.CustomHours)]
    [InlineData(DashboardParkStatus.PrivateBuyout)]
    public async Task DashboardView_EachParkStatus_ConstructsWithoutError(DashboardParkStatus status)
    {
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date =>
            {
                var snapshot = FakeOperationsDashboardService.DefaultSnapshot(date);
                return snapshot with { ParkStatus = snapshot.ParkStatus with { Status = status } };
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.NotEqual("Open", viewModel.Presentation!.ParkStatusBadgeText);
    }
}
