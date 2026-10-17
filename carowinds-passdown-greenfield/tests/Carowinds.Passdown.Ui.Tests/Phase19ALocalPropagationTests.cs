using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 19A local data propagation, end to end on the real stack: a real SQLite database, the real
/// Associate/ERT/Notes/Dashboard services, the real <see cref="AssociateViewModel"/> (autosave driven by
/// a manual debounce) and the real <see cref="OperationsDashboardViewModel"/>, joined only by the app's
/// <see cref="IDataChangeNotifier"/>. Nothing presses a Dashboard refresh: each edit must reach the
/// Dashboard on its own. Every case also proves no online work happened — the orchestrator throws if
/// reached, and the refresh gate is never entered.
/// </summary>
public sealed class Phase19ALocalPropagationTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero); // 11:00 AM Eastern
    private static readonly DateOnly Today = new(2026, 8, 21);

    private readonly SqliteConnectionFactory _db = new(Path.Combine(Path.GetTempPath(), $"carowinds-19a-{Guid.NewGuid():N}.db"));
    private readonly FakeClock _clock = new(FixedNow);
    private readonly SyncLocalChangeNotifier _syncNotifier = new();
    private readonly DataChangeNotifier _notifier;
    private readonly OfficialRefreshGate _gate = new();
    private int _gateEntries;

    public Phase19ALocalPropagationTests()
    {
        _notifier = new DataChangeNotifier(_syncNotifier);
        _gate.Started += (_, _) => Interlocked.Increment(ref _gateEntries);
        var migrations = App.BuildServiceProvider().GetRequiredService<IReadOnlyList<IMigration>>();
        new MigrationRunner(_db, migrations, NullLogger<MigrationRunner>.Instance).Run();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(_db.DatabasePath + suffix))
            {
                File.Delete(_db.DatabasePath + suffix);
            }
        }
    }

    private sealed record Stack(AssociateViewModel Associate, OperationsDashboardViewModel Dashboard, ErtConfigurationService Ert, ManualDebounceScheduler Debounce, OperationalNotesService Notes);

    private async Task<Stack> CreateStackAsync()
    {
        // Official park hours 11:00 AM – 8:00 PM, so the calculated gate defaults are 10:30 / 10:15.
        var evidence = new SourceEvidence("Park hours", new Uri("https://example.test/hours"), FixedNow, RetrievalStatus.Verified,
            "Checked.", RetrievalStrategy.DirectApi, Today, Today);
        await new SqliteDatedResultRepository(_db).RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, Today), evidence,
            JsonSerializer.Serialize(new ParkHoursPayload(new TimeOnly(11, 0), new TimeOnly(20, 0))));

        var notes = new OperationalNotesService(new SqliteOperationalNoteRepository(_db), _clock, dataChangeNotifier: _notifier);
        var monthly = new SqliteMonthlyErtRepository(_db);
        var overrides = new SqliteErtDateOverrideRepository(_db);
        var ert = new ErtConfigurationService(monthly, overrides, _clock, dataChangeNotifier: _notifier);
        var associateService = new AssociatePassdownService(
            new SqliteAssociateDraftRepository(_db), new SqliteDatedResultRepository(_db), new SqliteCatalogProductRepository(_db),
            new UnreachableRefreshOrchestrator(), notes, _clock,
            effectiveErtResolver: new EffectiveErtResolver(monthly, overrides),
            refreshGate: _gate, dataChangeNotifier: _notifier);
        var dashboardService = new OperationsDashboardService(associateService, new SqliteFinalizedPassdownRepository(_db), notes, _clock);

        var debounce = new ManualDebounceScheduler();
        var associate = new AssociateViewModel(associateService, _clock, autosaveDelay: debounce.Delay, ertConfigurationService: ert,
            dataChangeNotifier: _notifier, uiDispatcher: new ImmediateUiDispatcher());
        var dashboard = new OperationsDashboardViewModel(dashboardService, dataChangeNotifier: _notifier, uiDispatcher: new ImmediateUiDispatcher());

        await associate.PendingLoad;
        await dashboard.PendingLoad;
        return new Stack(associate, dashboard, ert, debounce, notes);
    }

    private static async Task AutosaveAsync(Stack stack)
    {
        stack.Debounce.ElapseLatest();
        await stack.Associate.PendingAutosave;
        await stack.Dashboard.PendingLoad;
    }

    private void AssertNoOnlineWork() => Assert.Equal(0, _gateEntries); // UnreachableRefreshOrchestrator would also have thrown.

    [Fact]
    public async Task ProjectedAttendance_ReachesDashboardWithoutRefresh()
    {
        var stack = await CreateStackAsync();
        Assert.Null(stack.Dashboard.Snapshot!.ProjectedAttendance);

        stack.Associate.ProjectedAttendanceText = "14250";
        await AutosaveAsync(stack);

        Assert.Equal(14250, stack.Dashboard.Snapshot!.ProjectedAttendance);
        Assert.Contains("14250", stack.Dashboard.Presentation!.AttendanceText);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task PreSolds_ReachDashboardWithoutRefresh()
    {
        var stack = await CreateStackAsync();

        stack.Associate.PreSoldsText = "820";
        await AutosaveAsync(stack);

        Assert.Equal(820, stack.Dashboard.Snapshot!.PreSolds);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task EarlyRideTimeConfiguration_ReachesDashboardWithoutRefresh()
    {
        var stack = await CreateStackAsync();
        Assert.Equal(EffectiveErtKind.NotConfigured, stack.Dashboard.Snapshot!.EarlyRideTime.Kind);

        await stack.Ert.SaveMonthlyAsync(new MonthlyErtDraft(2026, 8, ErtConfigurationState.Configured, null, ["Fury 325", "Copperhead Strike"], null));
        await stack.Dashboard.PendingLoad;

        Assert.Equal(EffectiveErtKind.Configured, stack.Dashboard.Snapshot!.EarlyRideTime.Kind);
        Assert.Equal(["Fury 325", "Copperhead Strike"], stack.Dashboard.Snapshot.EarlyRideTime.Rides);

        await stack.Ert.SaveOverrideAsync(new ErtDateOverrideDraft(Today, ErtConfigurationState.NoErt, null, [], "Weather"));
        await stack.Dashboard.PendingLoad;
        Assert.Equal(EffectiveErtKind.NoErt, stack.Dashboard.Snapshot!.EarlyRideTime.Kind);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task AvenueOverride_SetAndReset_ReachDashboardWithoutRefresh()
    {
        var stack = await CreateStackAsync();
        Assert.Equal(new TimeOnly(10, 15), stack.Dashboard.Snapshot!.ParkStatus.Hours.Effective.AvenueOpening);

        stack.Associate.AvenueOverrideText = "10am";
        await AutosaveAsync(stack);
        Assert.Equal(new TimeOnly(10, 0), stack.Dashboard.Snapshot!.ParkStatus.Hours.Effective.AvenueOpening);

        stack.Associate.ResetAvenueOverrideCommand.Execute(null);
        await AutosaveAsync(stack);
        Assert.Equal(new TimeOnly(10, 15), stack.Dashboard.Snapshot!.ParkStatus.Hours.Effective.AvenueOpening);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task NorthGateOverride_SetAndReset_ReachDashboardWithoutRefresh()
    {
        var stack = await CreateStackAsync();
        Assert.Equal(new TimeOnly(10, 30), stack.Dashboard.Snapshot!.ParkStatus.Hours.Effective.NorthGateOpening);

        stack.Associate.NorthGateOverrideText = "10:20 am";
        await AutosaveAsync(stack);
        Assert.Equal(new TimeOnly(10, 20), stack.Dashboard.Snapshot!.ParkStatus.Hours.Effective.NorthGateOpening);

        stack.Associate.ResetNorthGateOverrideCommand.Execute(null);
        await AutosaveAsync(stack);
        Assert.Equal(new TimeOnly(10, 30), stack.Dashboard.Snapshot!.ParkStatus.Hours.Effective.NorthGateOpening);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task OperationalNote_ReachesDashboardWithoutRefresh()
    {
        var stack = await CreateStackAsync();

        await stack.Notes.CreateAsync(new OperationalNoteDraft("Gate 3 closed", null, OperationalNoteCategory.General, OperationalNotePriority.Important, Today, null));
        await stack.Dashboard.PendingLoad;

        Assert.Equal(1, stack.Dashboard.Snapshot!.ActiveOperationalNotes.Count);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task PersonalSyncImport_ReachesDashboardThroughTheSameMechanism()
    {
        var stack = await CreateStackAsync();

        // Another device's draft lands in the local database (as a sync import would write it)…
        var imported = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 9999 };
        await new SqliteAssociateDraftRepository(_db).SaveAsync(new AssociateDraft(Today, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(imported), FixedNow));
        // …and Personal Sync announces it, exactly as PersonalSyncService does after committing.
        _syncNotifier.Publish([new SyncEntityKey(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Today))]);
        await stack.Dashboard.PendingLoad;

        Assert.Equal(9999, stack.Dashboard.Snapshot!.ProjectedAttendance);
        AssertNoOnlineWork();
    }

    [Fact]
    public async Task EditToAnotherDate_DoesNotReloadTodaysDashboard()
    {
        var stack = await CreateStackAsync();
        stack.Associate.SelectedDate = Today.AddDays(3);
        await stack.Associate.PendingLoad;
        var before = stack.Dashboard.Snapshot;

        stack.Associate.ProjectedAttendanceText = "500";
        await AutosaveAsync(stack);

        Assert.Same(before, stack.Dashboard.Snapshot);
        Assert.Null(stack.Dashboard.Snapshot!.ProjectedAttendance);
    }
}
