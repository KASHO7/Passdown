using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 23A, Personal Sync: on the real SQLite stack (disposable temp database, never a real
/// profile), a draft write that fails inside the save transaction — injected deterministically by a
/// repository wrapper, so no file is made read-only or damaged — must leave no sync metadata behind and
/// request no sync pass, so an uncommitted draft can never be published. Once the injected failure is
/// lifted, Retry Save commits and marks the draft for sync exactly once.
/// </summary>
public sealed class Phase23ASaveFailureSyncTests : IDisposable
{
    private static readonly DateOnly Date = new(2026, 9, 9);
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carowinds-23a-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Deterministic failure injection: while <see cref="Failing"/> is set, writes throw the error a locked database raises.</summary>
    private sealed class FailingDraftRepository(IAssociateDraftRepository inner) : IAssociateDraftRepository
    {
        public bool Failing { get; set; }

        public int FailedWrites { get; private set; }

        public Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) => inner.GetAsync(operatingDate, cancellationToken);

        public Task SaveAsync(AssociateDraft draft, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
        {
            if (Failing)
            {
                FailedWrites++;
                throw new SqliteException("SQLite Error 5: 'database is locked'.", 5);
            }

            return inner.SaveAsync(draft, unitOfWork, cancellationToken);
        }

        public Task DeleteAsync(DateOnly operatingDate, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default) => inner.DeleteAsync(operatingDate, unitOfWork, cancellationToken);

        public Task<IReadOnlyList<DateOnly>> ListSavedDraftDatesAsync(CancellationToken cancellationToken = default) => inner.ListSavedDraftDatesAsync(cancellationToken);

        public Task<IReadOnlyList<AssociateDraft>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default) => inner.GetByDateRangeAsync(startDate, endDateInclusive, cancellationToken);
    }

    private sealed record Stack(
        SqliteConnectionFactory Db,
        FailingDraftRepository Drafts,
        SqliteSyncEntityStateRepository States,
        FakePersonalSyncCoordinator Coordinator,
        AssociatePassdownService Service,
        FakeClock Clock);

    private Stack CreateStack()
    {
        Directory.CreateDirectory(_root);
        var clock = new FakeClock(Now);
        var db = new SqliteConnectionFactory(Path.Combine(_root, "profile.db"));
        new MigrationRunner(db,
        [
            new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(), new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(), new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(), new Migration0009AddFinalizedPassdownPdfColumns(),
            new Migration0010CreateOperationalNotesTable(), new Migration0011CreateDeepRefreshStatusTable(), new Migration0012CreateSyncEntityStateTable(),
            new Migration0013CreateSyncConflictsTable(), new Migration0014CreateMonthlyErtConfigurationsTable(), new Migration0015CreateErtDateOverridesTable(),
        ], NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new FailingDraftRepository(new SqliteAssociateDraftRepository(db));
        var states = new SqliteSyncEntityStateRepository(db);
        var conflicts = new SqliteSyncConflictRepository(db);
        var unitOfWork = new SqliteUnitOfWorkFactory(db);
        var coordinator = new FakePersonalSyncCoordinator();
        var tracker = new AutomaticSyncChangeTracker(new SyncChangeTracker(states, clock), coordinator);
        var recorder = new OpenEditorConflictRecorder(states, conflicts, clock);
        var monthly = new SqliteMonthlyErtRepository(db);
        var overrides = new SqliteErtDateOverrideRepository(db);
        var notes = new OperationalNotesService(new SqliteOperationalNoteRepository(db), clock, tracker, unitOfWork, recorder);
        var service = new AssociatePassdownService(
            drafts, new SqliteDatedResultRepository(db), new SqliteCatalogProductRepository(db), new UnreachableRefreshOrchestrator(),
            notes, clock, syncChangeTracker: tracker, effectiveErtResolver: new EffectiveErtResolver(monthly, overrides),
            unitOfWorkFactory: unitOfWork, openEditorConflictRecorder: recorder);

        return new Stack(db, drafts, states, coordinator, service, clock);
    }

    private static async Task<AssociateViewModel> OpenEditorAsync(Stack stack)
    {
        var viewModel = new AssociateViewModel(stack.Service, stack.Clock, autosaveDelay: (_, token) => Task.Delay(Timeout.Infinite, token));
        await viewModel.PendingLoad;
        return viewModel;
    }

    private static void Execute(SqliteConnectionFactory db, string sql)
    {
        using var connection = db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task AFailedDraftSave_IsNeverMarkedForSync_AndRecoveryMarksItOnce()
    {
        var stack = CreateStack();
        var (drafts, states, coordinator) = (stack.Drafts, stack.States, stack.Coordinator);
        var viewModel = await OpenEditorAsync(stack);
        var requestsBefore = coordinator.RequestedReasons.Count;

        drafts.Failing = true;
        viewModel.ProjectedAttendanceText = "38,500";
        viewModel.NextDayCommand.Execute(null); // flushes: the write fails inside the transaction
        await viewModel.PendingLoad;

        Assert.Equal(1, drafts.FailedWrites);
        Assert.Equal(AssociateAutosaveState.SaveFailed, viewModel.AutosaveState);
        Assert.Contains("database is locked", viewModel.SaveFailureReason, StringComparison.Ordinal);
        Assert.Equal(Date, viewModel.SelectedDate);
        Assert.Null(await drafts.GetAsync(Date));
        Assert.Null(await states.GetAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Date)));
        Assert.Equal(requestsBefore, coordinator.RequestedReasons.Count); // no sync pass requested for an uncommitted draft

        drafts.Failing = false;
        await viewModel.RetrySaveCommand.ExecuteAsync(null);

        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
        var stored = await drafts.GetAsync(Date);
        Assert.NotNull(stored);
        Assert.Equal(38500, AssociateDraftPayloadSerializer.Deserialize(stored!.PayloadJson).ProjectedAttendance);
        Assert.NotNull(await states.GetAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Date)));
        Assert.Equal(requestsBefore + 1, coordinator.RequestedReasons.Count);
    }

    /// <summary>
    /// The root cause, on the real SQLite repository: a genuine SQLite error raised inside the real
    /// INSERT (an ABORT trigger in this disposable database — no file permissions changed) fails
    /// synchronously. Microsoft.Data.Sqlite's async methods complete synchronously, so the whole
    /// flush-fail path finishes before <c>Execute</c> even returns — which is why the pre-23A
    /// <c>while (dirty) save()</c> loop never yielded to the UI thread. Now it is one attempt, the date
    /// switch is refused, and nothing is marked for sync until a retry actually commits.
    /// </summary>
    [Fact]
    public async Task ARealSqliteWriteFailure_FailsSynchronously_AndIsAttemptedOnlyOnce()
    {
        var stack = CreateStack();
        var viewModel = await OpenEditorAsync(stack);
        Execute(stack.Db, """
            CREATE TRIGGER phase23a_fail_insert BEFORE INSERT ON associate_drafts BEGIN SELECT RAISE(ABORT, 'injected disk I/O error'); END;
            CREATE TRIGGER phase23a_fail_update BEFORE UPDATE ON associate_drafts BEGIN SELECT RAISE(ABORT, 'injected disk I/O error'); END;
            """);

        viewModel.ProjectedAttendanceText = "38,500";
        viewModel.NextDayCommand.Execute(null);

        Assert.True(viewModel.PendingLoad.IsCompleted); // the failing write never yielded
        await viewModel.PendingLoad;
        Assert.Equal(AssociateAutosaveState.SaveFailed, viewModel.AutosaveState);
        Assert.Contains("injected disk I/O error", viewModel.SaveFailureReason, StringComparison.Ordinal);
        Assert.Equal(Date, viewModel.SelectedDate);
        Assert.Equal("38,500", viewModel.ProjectedAttendanceText);
        Assert.Null(await stack.Drafts.GetAsync(Date));
        Assert.Null(await stack.States.GetAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Date)));

        Execute(stack.Db, "DROP TRIGGER phase23a_fail_insert; DROP TRIGGER phase23a_fail_update;");
        await viewModel.RetrySaveCommand.ExecuteAsync(null);

        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
        Assert.Equal(38500, AssociateDraftPayloadSerializer.Deserialize((await stack.Drafts.GetAsync(Date))!.PayloadJson).ProjectedAttendance);
        Assert.NotNull(await stack.States.GetAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(Date)));
    }
}
