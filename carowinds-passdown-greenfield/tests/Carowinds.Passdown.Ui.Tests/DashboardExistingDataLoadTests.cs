using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Dashboard load against real, existing local SQLite data (machine-specific Dashboard failure fix).
/// The defect: a Phase 17D+ rides-only Early Ride Time configuration persists <c>StartTime = null</c>
/// for a <see cref="EffectiveErtKind.Configured"/> state, and the Dashboard presentation dereferenced
/// <c>StartTime!.Value</c> — so only PCs whose local data held a rides-only ERT for today's month (or
/// a rides-only override for today) showed "Unable to load today's operations summary.", and
/// Refresh re-ran the same deterministic failure. Every case here goes through the real migration
/// list, real SQLite repositories, the real <see cref="AssociatePassdownService"/>/
/// <see cref="OperationsDashboardService"/>, and the real <see cref="OperationsDashboardViewModel"/>
/// — never a network call (<see cref="UnreachableRefreshOrchestrator"/> throws if one is attempted).
/// </summary>
public sealed class DashboardExistingDataLoadTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero); // 10:00 AM Eastern
    private static readonly DateOnly Today = new(2026, 8, 21);

    private readonly SqliteConnectionFactory _connectionFactory =
        new(Path.Combine(Path.GetTempPath(), $"carowinds-passdown-dashboard-tests-{Guid.NewGuid():N}.db"));

    private readonly FakeClock _clock = new(FixedNow);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _connectionFactory.DatabasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static IReadOnlyList<IMigration> AllMigrations() =>
        App.BuildServiceProvider().GetRequiredService<IReadOnlyList<IMigration>>();

    private void Migrate(int? throughVersion = null)
    {
        var migrations = AllMigrations().Where(m => throughVersion is null || m.Version <= throughVersion).ToList();
        new MigrationRunner(_connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();
    }

    private OperationsDashboardService CreateDashboardService()
    {
        var notes = new OperationalNotesService(new SqliteOperationalNoteRepository(_connectionFactory), _clock);
        var ertResolver = new EffectiveErtResolver(new SqliteMonthlyErtRepository(_connectionFactory), new SqliteErtDateOverrideRepository(_connectionFactory));
        var associate = new AssociatePassdownService(
            new SqliteAssociateDraftRepository(_connectionFactory),
            new SqliteDatedResultRepository(_connectionFactory),
            new SqliteCatalogProductRepository(_connectionFactory),
            new UnreachableRefreshOrchestrator(),
            notes,
            _clock,
            effectiveErtResolver: ertResolver);

        return new OperationsDashboardService(associate, new SqliteFinalizedPassdownRepository(_connectionFactory), notes, _clock);
    }

    private ErtConfigurationService CreateErtService() =>
        new(new SqliteMonthlyErtRepository(_connectionFactory), new SqliteErtDateOverrideRepository(_connectionFactory), _clock);

    private async Task<OperationsDashboardViewModel> LoadDashboardAsync()
    {
        var viewModel = new OperationsDashboardViewModel(CreateDashboardService());
        await viewModel.PendingLoad;
        return viewModel;
    }

    private static void AssertLoaded(OperationsDashboardViewModel viewModel)
    {
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.HasSnapshot);
        Assert.NotNull(viewModel.Presentation);
        Assert.Equal(Today, viewModel.Snapshot!.OperatingDate);
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    // A. Fresh current-schema DB; also C (no draft), E (no finalized records), G (no source-state /
    // dated / catalog rows at all), I (ERT NotConfigured).
    [Fact]
    public async Task FreshDatabase_NoLocalData_LoadsWithEmptyStates()
    {
        Migrate();

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal(DashboardPassdownStatus.NoDraft, viewModel.Snapshot!.PassdownStatus.Status);
        Assert.Null(viewModel.Snapshot.LatestFinalized);
        Assert.Equal(EffectiveErtKind.NotConfigured, viewModel.Snapshot.EarlyRideTime.Kind);
        Assert.Equal("Not configured", viewModel.Presentation!.EarlyRideTimeText);
        Assert.Equal("No published data yet", viewModel.Presentation.DataHealth.OverallHeadline);
    }

    // The exact reported defect: a monthly ERT saved through the Phase 17D+ editor (rides only, no
    // start time) for the current month. Before the fix this produced ErrorMessage, every time.
    [Fact]
    public async Task RidesOnlyMonthlyErt_SavedByCurrentEditor_DashboardLoads()
    {
        Migrate();
        await CreateErtService().SaveMonthlyAsync(new MonthlyErtDraft(Today.Year, Today.Month, ErtConfigurationState.Configured, StartTime: null, ["Fury 325", "Copperhead Strike"], Note: null));

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal(EffectiveErtKind.Configured, viewModel.Snapshot!.EarlyRideTime.Kind);
        Assert.Null(viewModel.Snapshot.EarlyRideTime.StartTime);
        Assert.Equal("Fury 325, Copperhead Strike", viewModel.Presentation!.EarlyRideTimeText);
    }

    // Same defect via a rides-only date override for today (no monthly configuration at all).
    [Fact]
    public async Task RidesOnlyDateOverrideForToday_DashboardLoads()
    {
        Migrate();
        await CreateErtService().SaveOverrideAsync(new ErtDateOverrideDraft(Today, ErtConfigurationState.Configured, StartTime: null, ["Fury 325"], Reason: null));

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal("Fury 325", viewModel.Presentation!.EarlyRideTimeText);
    }

    // J. ERT explicitly NoERT for the month.
    [Fact]
    public async Task NoErtMonthly_DashboardLoads()
    {
        Migrate();
        await CreateErtService().SaveMonthlyAsync(new MonthlyErtDraft(Today.Year, Today.Month, ErtConfigurationState.NoErt, StartTime: null, [], Note: null));

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal("None", viewModel.Presentation!.EarlyRideTimeText);
    }

    // K. A pre-17D record that still carries a legacy StartTime keeps its existing Dashboard text.
    [Fact]
    public async Task LegacyErtWithStartTime_DashboardLoads_UnchangedText()
    {
        Migrate();
        await CreateErtService().SaveMonthlyAsync(new MonthlyErtDraft(Today.Year, Today.Month, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], Note: null));

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal("Starts 9:30 AM", viewModel.Presentation!.EarlyRideTimeText);
    }

    // The real schema-v1 (pre-Phase 8A) draft shape — the same fixture AssociatePassdownServiceTests
    // uses: old "DailyFun*" names, no SectionSelection, no SelectedOperationalNotes.
    private const string LegacySchemaV1DraftJson = """
        {
            "ManualHoursOverride": { "Enabled": false, "OpeningTime": null, "ClosingTime": null },
            "PrivateBuyout": { "Enabled": false, "OpeningTime": null, "ClosingTime": null, "Details": null },
            "ProjectedAttendance": 12000,
            "PreSolds": null,
            "EarlyRideTime": { "Details": null, "ParticipatingAttractions": [] },
            "OperationalNotes": ["Gate 3 closed"],
            "DailyFunType": 0,
            "DailyFunText": "Legacy question",
            "ProductOverrides": {},
            "CustomProducts": []
        }
        """;

    // B/D/F. A database created at an old schema (v8: drafts + finalized table, pre-PDF columns, pre
    // operational notes/sync/ERT), holding a schema-v1 legacy draft for today and a legacy (pre-9C,
    // no frozen PDF) finalized row, then upgraded incrementally to current — plus a rides-only ERT
    // saved after upgrade — must load exactly like a fresh DB with the same data.
    [Fact]
    public async Task DatabaseMigratedFromOldSchema_WithLegacyDraftAndLegacyFinalizedRow_DashboardLoads()
    {
        Migrate(throughVersion: 8);

        Execute(
            "INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) VALUES ($date, 1, $payload, $updatedAt);",
            ("$date", Today.ToString("yyyy-MM-dd")),
            ("$payload", LegacySchemaV1DraftJson),
            ("$updatedAt", FixedNow.AddHours(-2).ToString("O")));
        Execute(
            "INSERT INTO finalized_passdowns (id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin) VALUES ($id, $date, $finalizedAt, 1, '{}', 'legacy-hash', 'Scheduled');",
            ("$id", Guid.NewGuid().ToString()),
            ("$date", Today.AddDays(-1).ToString("yyyy-MM-dd")),
            ("$finalizedAt", FixedNow.AddHours(-20).ToString("O")));

        Migrate();
        Assert.Equal(AllMigrations().Max(m => m.Version), new MigrationRunner(_connectionFactory, AllMigrations(), NullLogger<MigrationRunner>.Instance).Run());

        await CreateErtService().SaveMonthlyAsync(new MonthlyErtDraft(Today.Year, Today.Month, ErtConfigurationState.Configured, StartTime: null, ["Fury 325"], Note: null));

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal(DashboardPassdownStatus.DraftInProgress, viewModel.Snapshot!.PassdownStatus.Status);
        Assert.Equal(12000, viewModel.Snapshot.ProjectedAttendance);
        Assert.Equal("Legacy question", viewModel.Snapshot.DailyFeatureText);
        Assert.NotNull(viewModel.Snapshot.LatestFinalized);
        Assert.False(viewModel.Snapshot.LatestFinalized!.HasFrozenPdf);
        Assert.Equal("Fury 325", viewModel.Presentation!.EarlyRideTimeText);
    }

    // C-inverse: today's draft exists but no source data was ever retrieved on this machine (a PC
    // that has never run Check Online). Partially-populated per-category source state is covered at
    // the service level by OperationsDashboardServiceTests (LoadAsync_OneCategory*/LoadAsync_Catalog*).
    [Fact]
    public async Task DraftExists_NoSourceDataEverRetrieved_DashboardLoads()
    {
        Migrate();
        await new SqliteAssociateDraftRepository(_connectionFactory).SaveAsync(
            new AssociateDraft(Today, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault()), FixedNow));

        var viewModel = await LoadDashboardAsync();

        AssertLoaded(viewModel);
        Assert.Equal(DashboardPassdownStatus.DraftInProgress, viewModel.Snapshot!.PassdownStatus.Status);
    }

    // N. Genuinely corrupt local data still surfaces the controlled, concise Dashboard error — it is
    // never silently "recovered" and the stored row is never modified.
    [Fact]
    public async Task CorruptDraftPayload_SurfacesControlledError_AndPreservesRow()
    {
        Migrate();
        Execute(
            "INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) VALUES ($date, 4, 'not json', $updatedAt);",
            ("$date", Today.ToString("yyyy-MM-dd")),
            ("$updatedAt", FixedNow.ToString("O")));

        var viewModel = await LoadDashboardAsync();

        Assert.False(viewModel.HasSnapshot);
        Assert.Equal("Unable to load today's operations summary.", viewModel.ErrorMessage);
        var stored = await new SqliteAssociateDraftRepository(_connectionFactory).GetAsync(Today);
        Assert.Equal("not json", stored!.PayloadJson);
    }

    // L (real data). A Dashboard that failed on existing data recovers through Refresh alone once the
    // underlying condition is gone — no restart, no new view model.
    [Fact]
    public async Task FailedLoad_ThenDataCorrected_RefreshRecoversWithoutRestart()
    {
        Migrate();
        Execute(
            "INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) VALUES ($date, 4, 'not json', $updatedAt);",
            ("$date", Today.ToString("yyyy-MM-dd")),
            ("$updatedAt", FixedNow.ToString("O")));

        var viewModel = await LoadDashboardAsync();
        Assert.NotNull(viewModel.ErrorMessage);

        Execute("DELETE FROM associate_drafts WHERE operating_date = $date;", ("$date", Today.ToString("yyyy-MM-dd")));
        await viewModel.ReloadCommand.ExecuteAsync(null);

        AssertLoaded(viewModel);
    }
}
