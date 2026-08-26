using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
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
        Assert.Contains("Latest finalized passdown", names);
        Assert.Contains("Data status", names);
        Assert.Contains("Check Online", names);

        // Phase 10E Part W: the remaining panels gained accessible names to match the precedent set
        // by "Data status"/"Latest finalized passdown" (Phase 10C/10D), so a screen reader can
        // navigate the page's landmark structure, not just its individual actions.
        Assert.Contains("Today's Operation", names);
        Assert.Contains("Passdown Status", names);
        Assert.Contains("Today at a Glance", names);
        Assert.Contains("Operational Notes", names);
    }

    // Phase 10E Part R: a dedicated busy indicator for Check Online (distinct from the header's
    // local-reload ProgressBar, keyed off IsCheckingOnline rather than IsLoading) exists in the real
    // XAML tree and constructs cleanly through both the idle and busy states. This intentionally does
    // not assert live IsVisible transitions off the UI-bound control — under this suite's parallel
    // test execution, reading a binding-driven property immediately after starting an async command is
    // a dispatcher-timing race, not a meaningful assertion (mirrors why every other test in this file
    // asserts view-model state rather than bound-control state for busy/async transitions).
    [Fact]
    public async Task DashboardView_CheckOnlineBusy_HasDedicatedBusyIndicator()
    {
        var service = new FakeOperationsDashboardService { CheckOnlineGate = new TaskCompletionSource<bool>() };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };
        var checkOnlineBusyBar = view.GetLogicalDescendants().OfType<ProgressBar>().Single(p => p.Width == 140);
        Assert.NotSame(checkOnlineBusyBar, view.GetLogicalDescendants().OfType<ProgressBar>().Single(p => double.IsNaN(p.Width)));

        var runTask = viewModel.CheckOnlineCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsCheckingOnline);

        service.CheckOnlineGate!.SetResult(true);
        await runTask;

        Assert.False(viewModel.IsCheckingOnline);
    }

    // Phase 10E Part L/C: with every source bucket populated (all six count chips present, not just
    // the default one-or-two), the Data Status card must still construct cleanly — the count row uses
    // a WrapPanel specifically so this does not clip at the 960px fallback width.
    [Fact]
    public async Task DashboardView_AllDataStatusCountBucketsPopulated_ConstructsWithoutError()
    {
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date =>
            {
                var snapshot = FakeOperationsDashboardService.DefaultSnapshot(date);
                var sources = snapshot.DataHealth.Sources
                    .Select((entry, index) => entry with { Status = (DashboardSourceStatus)(index % 6) })
                    .ToList();
                return snapshot with
                {
                    DataHealth = snapshot.DataHealth with
                    {
                        CurrentCount = 2,
                        StaleCount = 1,
                        UnavailableCount = 1,
                        NotPublishedCount = 1,
                        VerificationIssueCount = 1,
                        NotCheckedCount = 1,
                        Sources = sources,
                    },
                };
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Equal(6, viewModel.Presentation!.DataHealth.Counts.Count);
    }

    // Phase 10D Part AD/AA: every overall Data Status classification constructs cleanly through the
    // real XAML loader.
    [Theory]
    [InlineData(DashboardOverallDataStatus.Current)]
    [InlineData(DashboardOverallDataStatus.SomeStale)]
    [InlineData(DashboardOverallDataStatus.SomeUnavailable)]
    [InlineData(DashboardOverallDataStatus.VerificationIssue)]
    [InlineData(DashboardOverallDataStatus.NoPublishedData)]
    public async Task DashboardView_EachDataStatus_ConstructsWithoutError(DashboardOverallDataStatus status)
    {
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date =>
            {
                var snapshot = FakeOperationsDashboardService.DefaultSnapshot(date);
                return snapshot with { DataHealth = snapshot.DataHealth with { OverallStatus = status } };
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.False(string.IsNullOrEmpty(viewModel.Presentation!.DataHealth.OverallHeadline));
    }

    // Phase 10D Part N: the Check Online busy state constructs cleanly and disables the action while running.
    [Fact]
    public async Task DashboardView_CheckOnlineBusy_ConstructsWithoutError()
    {
        var service = new FakeOperationsDashboardService { CheckOnlineGate = new TaskCompletionSource<bool>() };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };
        var runTask = viewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCheckingOnline);
        Assert.Same(viewModel, view.DataContext);

        service.CheckOnlineGate!.SetResult(true);
        await runTask;
    }

    // Phase 10C Part U/M: no finalized history yet — the empty state constructs and never fabricates a date.
    [Fact]
    public async Task DashboardView_NoFinalizedHistory_ConstructsWithEmptyState()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Null(viewModel.Presentation!.LatestFinalized);
    }

    // Phase 10C Part U: one finalized record constructs cleanly and is reflected in the presentation.
    [Fact]
    public async Task DashboardView_OneFinalizedRecord_ConstructsWithLatestFinalized()
    {
        var summary = new FinalizedPassdownSummary(new DateOnly(2026, 8, 24), DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 50_000);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with { LatestFinalized = summary },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.NotNull(viewModel.Presentation!.LatestFinalized);
    }

    // Phase 10C Part U/E: "Draft In Progress" (today has a saved-but-not-finalized draft) constructs
    // cleanly and shows the Open Passdown action — never implying today is finalized.
    [Fact]
    public async Task DashboardView_DraftInProgress_ConstructsWithoutError()
    {
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                PassdownStatus = new DashboardPassdownStatusSummary(DashboardPassdownStatus.DraftInProgress, DateTimeOffset.UtcNow, null),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Equal("Draft In Progress", viewModel.Presentation!.PassdownStatusText);
        Assert.True(viewModel.Presentation!.ShowOpenPassdownAction);
    }

    // Phase 10C Part U/N: a no-PDF (pre-9C) latest finalized record must still construct cleanly and
    // never be treated as invalid.
    [Fact]
    public async Task DashboardView_LatestFinalizedHasNoPdf_ConstructsWithoutError()
    {
        var summary = new FinalizedPassdownSummary(new DateOnly(2026, 8, 24), DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, false, null);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with { LatestFinalized = summary },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.False(viewModel.Presentation!.LatestFinalized!.HasFrozenPdf);
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
