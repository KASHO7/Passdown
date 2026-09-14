using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
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

        // Phase 11C Part AF/AH: renamed from "Operational Notes" to "Passdown Notes" specifically
        // because this phase introduces a second, distinct "Active operational notes" panel next to
        // it (persistent, supervisor-managed notes) — the two must never share a name or heading.
        Assert.Contains("Passdown Notes", names);
        Assert.Contains("Active operational notes", names);
        Assert.Contains("Open operational notes", names);
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
        var checkOnlineBusyBar = view.GetLogicalDescendants().OfType<ProgressBar>().Single(p => p.Name == "CheckOnlineBusyBar");
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
                EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(7, 30), ["Fury 325"], null),
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

    // ==================================================================================
    // Phase 11C: Active Operational Notes — the Dashboard's read-only persistent-notes summary.
    // ==================================================================================

    private static DashboardOperationalNoteItem NoteItem(string title, OperationalNoteCategory category = OperationalNoteCategory.General, OperationalNotePriority priority = OperationalNotePriority.Normal) =>
        new(Guid.NewGuid(), title, category, priority, FakeOperationsDashboardService.DefaultDate, null);

    [Fact]
    public async Task DashboardView_NoActiveOperationalNotes_ConstructsWithEmptyState()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.True(viewModel.Presentation!.ActiveOperationalNotes.ShowEmptyState);
    }

    [Fact]
    public async Task DashboardView_OneImportantActiveOperationalNote_ConstructsWithoutError()
    {
        var item = NoteItem("South Gate scanner #3 out of service", OperationalNoteCategory.Equipment, OperationalNotePriority.Important);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                ActiveOperationalNotes = new DashboardOperationalNotesSummary(1, 1, [item], false),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Single(viewModel.Presentation!.ActiveOperationalNotes.Items);
        Assert.True(viewModel.Presentation!.ActiveOperationalNotes.Items[0].IsImportant);
    }

    [Fact]
    public async Task DashboardView_ThreeActiveOperationalNotes_NoOverflowIndicator()
    {
        var items = new[] { NoteItem("Note 1"), NoteItem("Note 2"), NoteItem("Note 3") };
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                ActiveOperationalNotes = new DashboardOperationalNotesSummary(3, 0, items, false),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.False(viewModel.Presentation!.ActiveOperationalNotes.HasOverflow);
    }

    // Ten-plus relevant notes: the preview stays bounded and the overflow indicator never hides that
    // more notes exist (Phase 11C Part H).
    [Fact]
    public async Task DashboardView_TenPlusActiveOperationalNotes_ShowsBoundedPreviewWithOverflow()
    {
        var previewItems = new[] { NoteItem("Note 1"), NoteItem("Note 2"), NoteItem("Note 3") };
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                ActiveOperationalNotes = new DashboardOperationalNotesSummary(12, 2, previewItems, false),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Equal(3, viewModel.Presentation!.ActiveOperationalNotes.Items.Count);
        Assert.True(viewModel.Presentation!.ActiveOperationalNotes.HasOverflow);
        Assert.Contains("9", viewModel.Presentation!.ActiveOperationalNotes.OverflowText);
    }

    // A very long title must still construct cleanly — presentation-only bounding via TextTrimming in
    // the view, never a persisted truncation (Phase 11C Part AG).
    [Fact]
    public async Task DashboardView_LongActiveOperationalNoteTitle_ConstructsWithoutError()
    {
        var longTitle = new string('a', 500);
        var item = NoteItem(longTitle);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                ActiveOperationalNotes = new DashboardOperationalNotesSummary(1, 0, [item], false),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Equal(longTitle, viewModel.Presentation!.ActiveOperationalNotes.Items[0].Title);
    }

    // Both note concepts populated at once (Passdown Notes + Active Operational Notes) construct
    // cleanly side by side — proving the two never collide or get merged into one list.
    [Fact]
    public async Task DashboardView_PassdownNotesAndActiveOperationalNotesBothPresent_ConstructsWithoutError()
    {
        var item = NoteItem("Group check-in support at North Gate", OperationalNoteCategory.Staffing);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                Notes = new DashboardNotesSummary(true, ["Gate 3 closed.", "VIP tour at 11."], 2),
                ActiveOperationalNotes = new DashboardOperationalNotesSummary(1, 0, [item], false),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.True(viewModel.Presentation!.NotesPreviewLines.Count > 0);
        Assert.Single(viewModel.Presentation!.ActiveOperationalNotes.Items);
    }

    // Phase 11C Part AI: a local operational-notes load failure must not crash the screen or blank the
    // rest of the Dashboard.
    [Fact]
    public async Task DashboardView_ActiveOperationalNotesLoadFailed_ConstructsWithErrorMessage()
    {
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with
            {
                ActiveOperationalNotes = new DashboardOperationalNotesSummary(0, 0, [], true),
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.True(viewModel.Presentation!.ActiveOperationalNotes.LoadFailed);
        Assert.False(viewModel.Presentation!.ActiveOperationalNotes.ShowEmptyState);
    }

    // A Data Status warning alongside active operational notes must still construct cleanly — the two
    // surfaces are independent (Phase 11C Part AB).
    [Fact]
    public async Task DashboardView_DataStatusWarningPlusActiveOperationalNotes_ConstructsWithoutError()
    {
        var item = NoteItem("Note during a data issue", OperationalNoteCategory.Operations, OperationalNotePriority.Important);
        var service = new FakeOperationsDashboardService
        {
            SnapshotFactory = date =>
            {
                var snapshot = FakeOperationsDashboardService.DefaultSnapshot(date);
                return snapshot with
                {
                    DataHealth = snapshot.DataHealth with { OverallStatus = DashboardOverallDataStatus.VerificationIssue },
                    ActiveOperationalNotes = new DashboardOperationalNotesSummary(1, 1, [item], false),
                };
            },
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var view = new DashboardView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.Equal(DashboardOverallDataStatus.VerificationIssue, viewModel.Snapshot!.DataHealth.OverallStatus);
        Assert.Single(viewModel.Presentation!.ActiveOperationalNotes.Items);
    }

    // Phase 11C Part O: "Open Operational Notes" raises its own navigation event.
    [Fact]
    public async Task OpenOperationalNotesCommand_RaisesOpenOperationalNotesRequested()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        await viewModel.PendingLoad;

        var raised = false;
        viewModel.OpenOperationalNotesRequested += (_, _) => raised = true;
        viewModel.OpenOperationalNotesCommand.Execute(null);

        Assert.True(raised);
    }
}
