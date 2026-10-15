using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 18B REL-004 (docs/phase18-comprehensive-audit.md), product rule: "auto-reload clean editors;
/// preserve dirty editors and require explicit conflict resolution; never silently overwrite a synced
/// change from an already-open editor."
///
/// End to end on the real stack: two devices, each with its own real SQLite database and real
/// services, sharing one real sync folder through real <see cref="PersonalSyncService"/> passes, with
/// the real <see cref="AssociateViewModel"/>/<see cref="OperationalNotesViewModel"/> as the open editor.
/// Pre-18B, every "dirty editor" scenario here silently replaced the other device's change with the
/// editor's stale snapshot (no conflict, the other device then accepted it as a safe update).
/// </summary>
public sealed class OpenEditorSyncProtectionTests : IDisposable
{
    private static readonly DateOnly Date = new(2026, 9, 9);
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 0, 0, TimeSpan.Zero); // 10:00 AM Eastern on Date.

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carowinds-rel004-{Guid.NewGuid():N}");
    private string SharedFolder => Path.Combine(_root, "shared");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ------------------------------------------------------------------ harness

    private sealed class Device
    {
        public required FakeClock Clock { get; init; }
        public required SqliteUnitOfWorkFactory UnitOfWork { get; init; }
        public required IAssociateDraftRepository Drafts { get; init; }
        public required IOperationalNoteRepository NoteRepository { get; init; }
        public required ISyncEntityStateRepository States { get; init; }
        public required ISyncConflictRepository Conflicts { get; init; }
        public required AssociatePassdownService Associate { get; init; }
        public required ErtConfigurationService Ert { get; init; }
        public required OperationalNotesService Notes { get; init; }
        public required PersonalSyncService Sync { get; init; }
        public required PersonalSyncConflictResolutionService Resolution { get; init; }
        public required List<SyncEntityKey> Published { get; init; }
    }

    private Device CreateDevice(string name, Guid deviceId)
    {
        var clock = new FakeClock(Now);
        var db = new SqliteConnectionFactory(Path.Combine(_root, $"{name}.db"));
        new MigrationRunner(db,
        [
            new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(), new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(), new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(), new Migration0009AddFinalizedPassdownPdfColumns(),
            new Migration0010CreateOperationalNotesTable(), new Migration0011CreateDeepRefreshStatusTable(), new Migration0012CreateSyncEntityStateTable(),
            new Migration0013CreateSyncConflictsTable(), new Migration0014CreateMonthlyErtConfigurationsTable(), new Migration0015CreateErtDateOverridesTable(),
        ], NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new SqliteAssociateDraftRepository(db);
        var noteRepository = new SqliteOperationalNoteRepository(db);
        var monthly = new SqliteMonthlyErtRepository(db);
        var overrides = new SqliteErtDateOverrideRepository(db);
        var states = new SqliteSyncEntityStateRepository(db);
        var conflicts = new SqliteSyncConflictRepository(db);
        var unitOfWork = new SqliteUnitOfWorkFactory(db);
        var tracker = new SyncChangeTracker(states, clock);
        var recorder = new OpenEditorConflictRecorder(states, conflicts, clock);
        var notifier = new SyncLocalChangeNotifier();
        var published = new List<SyncEntityKey>();
        notifier.LocalDataChanged += (_, changed) => published.AddRange(changed);

        var notes = new OperationalNotesService(noteRepository, clock, tracker, unitOfWork, recorder);
        var ert = new ErtConfigurationService(monthly, overrides, clock, tracker, unitOfWork, recorder);
        var associate = new AssociatePassdownService(
            drafts, new SqliteDatedResultRepository(db), new SqliteCatalogProductRepository(db), new UnreachableRefreshOrchestrator(),
            notes, clock, syncChangeTracker: tracker, effectiveErtResolver: new EffectiveErtResolver(monthly, overrides),
            unitOfWorkFactory: unitOfWork, openEditorConflictRecorder: recorder);

        var deviceIds = new FixedDeviceId(deviceId);
        var config = new InMemorySyncConfiguration(new SyncConfiguration(true, SharedFolder));
        var settings = new PersonalSyncSettingsService(config, new SyncFolderValidator(), new NoSuggestions(), deviceIds, clock);
        var sync = new PersonalSyncService(settings, config, deviceIds, drafts, noteRepository, new SqliteFinalizedPassdownRepository(db),
            states, conflicts, clock, monthly, overrides, unitOfWork, notifier);
        var resolution = new PersonalSyncConflictResolutionService(settings, deviceIds, drafts, noteRepository, states, conflicts, unitOfWork, clock,
            monthly, overrides, notifier);

        return new Device
        {
            Clock = clock, UnitOfWork = unitOfWork, Drafts = drafts, NoteRepository = noteRepository, States = states, Conflicts = conflicts,
            Associate = associate, Ert = ert, Notes = notes, Sync = sync, Resolution = resolution, Published = published,
        };
    }

    /// <summary>The open editor. Autosave never fires on its own — a test flushes explicitly, exactly like a date switch/close would.</summary>
    private static async Task<AssociateViewModel> OpenAssociateEditorAsync(Device device)
    {
        var viewModel = new AssociateViewModel(
            device.Associate, device.Clock,
            autosaveDelay: (_, token) => Task.Delay(Timeout.Infinite, token),
            ertConfigurationService: device.Ert,
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore());
        await viewModel.PendingLoad;
        Assert.Equal(Date, viewModel.SelectedDate);
        return viewModel;
    }

    private static async Task<AssociateDraftPayload?> StoredDraftAsync(Device device) =>
        await device.Drafts.GetAsync(Date) is { } record ? AssociateDraftPayloadSerializer.Deserialize(record.PayloadJson) : null;

    private static AssociateDraftPayload Payload(int? attendance = null, int? preSolds = null) =>
        AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = attendance, PreSolds = preSolds };

    /// <summary>"Another device" edits the draft through its own service and publishes it to the shared folder.</summary>
    private static async Task RemoteDeviceEditsDraftAsync(Device remote, AssociateDraftPayload payload)
    {
        Assert.Equal(PersonalSyncOutcome.Completed, (await remote.Sync.RunAsync()).Outcome); // pick up anything already shared first
        var baseHash = (await remote.Associate.LoadAssociateDateAsync(Date)).DraftContentHash;
        var result = await remote.Associate.SaveEditorDraftAsync(Date, payload, baseHash);
        Assert.False(result.ConflictDetected);
        await remote.Sync.RunAsync();
    }

    private static string DraftEntityId => SyncEntityIdentity.ForDraft(Date);

    // ------------------------------------------------------------------ Associate draft

    // Phase 18D: the North Gate/Avenue overrides are ordinary draft fields, so they ride the same
    // draft sync entity and the same REL-004 protection — no separate sync entity.

    [Fact]
    public async Task Phase18D_GateOverrides_SyncToTheOtherDevice_AndACleanEditorReloadsThem()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var editorA = await OpenAssociateEditorAsync(a);
        editorA.AvenueOverrideText = "10:00 AM";
        await editorA.FlushPendingAutosaveAsync();
        await a.Sync.RunAsync();

        await b.Sync.RunAsync();
        var onB = await StoredDraftAsync(b);
        Assert.Equal(new TimeOnly(10, 0), onB!.AvenueOpeningOverride);
        Assert.Null(onB.NorthGateOpeningOverride);

        // B overrides North Gate; A's clean editor reloads both values.
        await RemoteDeviceEditsDraftAsync(b, onB with { NorthGateOpeningOverride = new TimeOnly(9, 45) });
        await a.Sync.RunAsync();
        await editorA.HandleSyncLocalChangesAsync(a.Published.ToList());

        Assert.Equal("9:45 AM", editorA.NorthGateOverrideText);
        Assert.Equal("10:00 AM", editorA.AvenueOverrideText);
        Assert.Null(editorA.SyncConflictMessage);
    }

    [Fact]
    public async Task Phase18D_DirtyGateOverrideEdit_PreservesTheLocalValue_AndKeepsTheRemoteOneAsAConflict()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var editor = await OpenAssociateEditorAsync(a);
        editor.AvenueOverrideText = "10:00 AM";
        await editor.FlushPendingAutosaveAsync();
        await a.Sync.RunAsync();

        await RemoteDeviceEditsDraftAsync(b, Payload() with { AvenueOpeningOverride = new TimeOnly(9, 30) });

        editor.AvenueOverrideText = "9:50 AM"; // unsaved local edit
        await a.Sync.RunAsync();
        await editor.HandleSyncLocalChangesAsync(a.Published.ToList());
        Assert.Equal("9:50 AM", editor.AvenueOverrideText); // dirty editor untouched

        await editor.FlushPendingAutosaveAsync();

        Assert.Equal(new TimeOnly(9, 50), (await StoredDraftAsync(a))!.AvenueOpeningOverride);
        var conflict = await a.Conflicts.GetAsync(SyncEntityType.AssociateDraft, DraftEntityId);
        Assert.NotNull(conflict);
        Assert.Equal(new TimeOnly(9, 30), AssociateDraftPayloadSerializer.Deserialize(conflict.RemotePayloadJson!).AvenueOpeningOverride);
        Assert.Equal(AssociateViewModel.DraftSyncConflictMessage, editor.SyncConflictMessage);
        await b.Sync.RunAsync();
        Assert.Equal(new TimeOnly(9, 30), (await StoredDraftAsync(b))!.AvenueOpeningOverride); // never silently overwritten
    }


    [Fact]
    public async Task DirtyDraftEditor_SyncImportUnderneath_SaveKeepsBothVersionsAsAnExplicitConflict()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        // Shared starting point: A saves attendance 100 and syncs; B picks it up.
        var editor = await OpenAssociateEditorAsync(a);
        editor.ProjectedAttendanceText = "100";
        await editor.FlushPendingAutosaveAsync();
        await a.Sync.RunAsync();

        // B changes the same date (pre-solds) and syncs.
        await RemoteDeviceEditsDraftAsync(b, Payload(attendance: 100, preSolds: 555));

        // A's open editor gets an unsaved edit (dirty), then A's sync pass imports B's version underneath it.
        editor.ProjectedAttendanceText = "200";
        await a.Sync.RunAsync();
        Assert.Equal(555, (await StoredDraftAsync(a))!.PreSolds); // the import really happened

        // The notification must not touch a dirty editor.
        await editor.HandleSyncLocalChangesAsync(a.Published.ToList());
        Assert.Equal("200", editor.ProjectedAttendanceText);

        await editor.FlushPendingAutosaveAsync();

        // This device's edits are saved, never discarded...
        Assert.Equal(200, (await StoredDraftAsync(a))!.ProjectedAttendance);
        // ...the other device's change is preserved, not silently overwritten...
        var conflict = await a.Conflicts.GetAsync(SyncEntityType.AssociateDraft, DraftEntityId);
        Assert.NotNull(conflict);
        Assert.Equal(555, AssociateDraftPayloadSerializer.Deserialize(conflict.RemotePayloadJson!).PreSolds);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), conflict.RemoteDeviceId);
        Assert.Equal(SyncConflictState.Conflicting, (await a.States.GetAsync(SyncEntityType.AssociateDraft, DraftEntityId))!.ConflictState);
        // ...the associate is told...
        Assert.Equal(AssociateViewModel.DraftSyncConflictMessage, editor.SyncConflictMessage);
        // ...and the shared folder still holds B's version: export is paused until an explicit choice.
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        Assert.Equal(555, (await StoredDraftAsync(b))!.PreSolds);
        Assert.Equal(100, (await StoredDraftAsync(b))!.ProjectedAttendance);
    }

    [Fact]
    public async Task DirtyDraftConflict_UseOtherDevice_RestoresTheSyncedVersion()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var editor = await OpenAssociateEditorAsync(a);
        editor.ProjectedAttendanceText = "100";
        await editor.FlushPendingAutosaveAsync();
        await a.Sync.RunAsync();
        await RemoteDeviceEditsDraftAsync(b, Payload(attendance: 100, preSolds: 555));
        editor.ProjectedAttendanceText = "200";
        await a.Sync.RunAsync();
        await editor.FlushPendingAutosaveAsync();

        Assert.Equal(ConflictResolutionOutcome.Resolved, (await a.Resolution.ResolveUseRemoteAsync(SyncEntityType.AssociateDraft, DraftEntityId)).Outcome);

        var stored = await StoredDraftAsync(a);
        Assert.Equal(100, stored!.ProjectedAttendance);
        Assert.Equal(555, stored.PreSolds);
        Assert.Contains(new SyncEntityKey(SyncEntityType.AssociateDraft, DraftEntityId), a.Published);

        // The (now clean) editor reloads the restored version when told.
        await editor.HandleSyncLocalChangesAsync([new SyncEntityKey(SyncEntityType.AssociateDraft, DraftEntityId)]);
        Assert.Equal("100", editor.ProjectedAttendanceText);
        Assert.Equal("555", editor.PreSoldsText);
    }

    [Fact]
    public async Task DirtyDraftConflict_KeepThisDevice_PublishesTheEditorsVersion()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var editor = await OpenAssociateEditorAsync(a);
        editor.ProjectedAttendanceText = "100";
        await editor.FlushPendingAutosaveAsync();
        await a.Sync.RunAsync();
        await RemoteDeviceEditsDraftAsync(b, Payload(attendance: 100, preSolds: 555));
        editor.ProjectedAttendanceText = "200";
        await a.Sync.RunAsync();
        await editor.FlushPendingAutosaveAsync();

        Assert.Equal(ConflictResolutionOutcome.Resolved, (await a.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, DraftEntityId)).Outcome);
        await b.Sync.RunAsync();

        Assert.Equal(200, (await StoredDraftAsync(b))!.ProjectedAttendance);
        Assert.Null((await StoredDraftAsync(b))!.PreSolds);
    }

    [Fact]
    public async Task CleanDraftEditor_IsReloadedAfterSyncImport_AndItsNextEditIsNotAConflict()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var editor = await OpenAssociateEditorAsync(a);
        editor.ProjectedAttendanceText = "100";
        await editor.FlushPendingAutosaveAsync();
        await a.Sync.RunAsync();

        await RemoteDeviceEditsDraftAsync(b, Payload(attendance: 100, preSolds: 555));
        await a.Sync.RunAsync();

        Assert.Equal([new SyncEntityKey(SyncEntityType.AssociateDraft, DraftEntityId)], a.Published);
        await editor.HandleSyncLocalChangesAsync(a.Published.ToList());
        Assert.Equal("555", editor.PreSoldsText); // auto-reloaded

        editor.ProjectedAttendanceText = "300";
        await editor.FlushPendingAutosaveAsync();

        Assert.Null(editor.SyncConflictMessage);
        Assert.Null(await a.Conflicts.GetAsync(SyncEntityType.AssociateDraft, DraftEntityId));
        var stored = await StoredDraftAsync(a);
        Assert.Equal(300, stored!.ProjectedAttendance);
        Assert.Equal(555, stored.PreSolds); // B's change survived A's next edit

        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        Assert.Equal(300, (await StoredDraftAsync(b))!.ProjectedAttendance);
        Assert.Equal(555, (await StoredDraftAsync(b))!.PreSolds);
    }

    [Fact]
    public async Task DirtyDraftEditor_DraftCreatedByAnotherDevice_WhileThisOneHadNone_IsAConflictNotAnOverwrite()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var editor = await OpenAssociateEditorAsync(a); // no draft exists yet anywhere

        await RemoteDeviceEditsDraftAsync(b, Payload(preSolds: 777));
        editor.ProjectedAttendanceText = "50";
        await a.Sync.RunAsync();
        await editor.FlushPendingAutosaveAsync();

        var conflict = await a.Conflicts.GetAsync(SyncEntityType.AssociateDraft, DraftEntityId);
        Assert.NotNull(conflict);
        Assert.Equal(777, AssociateDraftPayloadSerializer.Deserialize(conflict.RemotePayloadJson!).PreSolds);
        Assert.Equal(50, (await StoredDraftAsync(a))!.ProjectedAttendance);
    }

    /// <summary>
    /// The other direction of the race: an editor commits a save while a sync import of the same
    /// draft is between its read and its write. Pre-18B the import read the old local version, then
    /// its write (waiting on the lock) replaced the editor's just-committed save — a silent loss. Now
    /// the import's read-decide-write holds the write lock, so it reads the editor's save and records
    /// a conflict instead.
    /// </summary>
    [Fact]
    public async Task SyncImport_CannotOverwriteAnEditorSaveCommittedDuringIt()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var baseHash = (await a.Associate.SaveEditorDraftAsync(Date, Payload(attendance: 100), null)).ContentHash;
        await a.Sync.RunAsync();
        await RemoteDeviceEditsDraftAsync(b, Payload(attendance: 100, preSolds: 555));

        // A local editor transaction is open and writes first...
        var editorTransaction = await a.UnitOfWork.BeginAsync();
        await a.Drafts.SaveAsync(new AssociateDraft(Date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(Payload(attendance: 999)), Now), editorTransaction);

        // ...while A's sync pass starts and reaches this draft.
        var syncPass = Task.Run(() => a.Sync.RunAsync());
        await Task.Delay(1000);
        await editorTransaction.CommitAsync();
        await editorTransaction.DisposeAsync();
        await syncPass;

        Assert.Equal(999, (await StoredDraftAsync(a))!.ProjectedAttendance);
        var conflict = await a.Conflicts.GetAsync(SyncEntityType.AssociateDraft, DraftEntityId);
        Assert.NotNull(conflict);
        Assert.Equal(555, AssociateDraftPayloadSerializer.Deserialize(conflict.RemotePayloadJson!).PreSolds);
        Assert.NotNull(baseHash);
    }

    // ------------------------------------------------------------------ Early Ride Time panel

    [Fact]
    public async Task DirtyErtPanel_MonthChangedByAnotherDevice_SaveKeepsBothVersionsAsAnExplicitConflict()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        await a.Ert.SaveMonthlyFromEditorAsync(new MonthlyErtDraft(2026, 9, ErtConfigurationState.Configured, null, ["Fury 325"], null), null);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var editor = await OpenAssociateEditorAsync(a);
        await editor.OpenErtEditorCommand.ExecuteAsync(null);
        editor.MonthlyErtRidesText = "Fury 325\nCopperhead Strike"; // unsaved panel edit

        var bBase = IErtConfigurationService.ContentHashOf(await b.Ert.GetMonthlyAsync(2026, 9));
        await b.Ert.SaveMonthlyFromEditorAsync(new MonthlyErtDraft(2026, 9, ErtConfigurationState.NoErt, null, [], "Closed for event"), bBase);
        await b.Sync.RunAsync();
        await a.Sync.RunAsync();

        await editor.HandleSyncLocalChangesAsync(a.Published.ToList());
        Assert.Equal("Fury 325\nCopperhead Strike", editor.MonthlyErtRidesText); // dirty panel untouched
        Assert.True(editor.IsErtEditorOpen);

        await editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        Assert.Equal(["Fury 325", "Copperhead Strike"], (await a.Ert.GetMonthlyAsync(2026, 9))!.Rides);
        var conflict = await a.Conflicts.GetAsync(SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 9));
        Assert.NotNull(conflict);
        Assert.Equal(ErtConfigurationState.NoErt, MonthlyErtSyncPayloadSerializer.Deserialize(conflict.RemotePayloadJson!).State);
        Assert.Equal(AssociateViewModel.ErtSyncConflictMessage, editor.SyncConflictMessage);
    }

    [Fact]
    public async Task CleanErtPanel_IsRefilledAfterSyncImport()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        await a.Ert.SaveMonthlyFromEditorAsync(new MonthlyErtDraft(2026, 9, ErtConfigurationState.Configured, null, ["Fury 325"], null), null);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var editor = await OpenAssociateEditorAsync(a);
        await editor.OpenErtEditorCommand.ExecuteAsync(null);

        var bBase = IErtConfigurationService.ContentHashOf(await b.Ert.GetMonthlyAsync(2026, 9));
        await b.Ert.SaveMonthlyFromEditorAsync(new MonthlyErtDraft(2026, 9, ErtConfigurationState.Configured, null, ["Intimidator"], null), bBase);
        await b.Sync.RunAsync();
        await a.Sync.RunAsync();
        await editor.HandleSyncLocalChangesAsync(a.Published.ToList());

        Assert.True(editor.IsErtEditorOpen);
        Assert.Equal("Intimidator", editor.MonthlyErtRidesText);

        await editor.SaveMonthlyErtCommand.ExecuteAsync(null);
        Assert.Null(await a.Conflicts.GetAsync(SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 9)));
    }

    // ------------------------------------------------------------------ Operational Notes editor

    [Fact]
    public async Task DirtyNoteEditor_NoteChangedByAnotherDevice_SaveKeepsBothVersionsAsAnExplicitConflict()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var b = CreateDevice("b", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var note = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, Date, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var editor = new OperationalNotesViewModel(a.Notes, a.Clock);
        await editor.PendingLoad;
        editor.SelectedNote = editor.Notes.Single(row => row.Id == note.Id);
        editor.EditCommand.Execute(null);
        editor.EditorTitle = "Edited on A";

        var bNote = (await b.NoteRepository.GetByIdAsync(note.Id))!;
        await b.Notes.UpdateFromEditorAsync(bNote, new OperationalNoteDraft("Edited on B", "Body", OperationalNoteCategory.Staffing, OperationalNotePriority.Important, Date, null));
        await b.Sync.RunAsync();
        await a.Sync.RunAsync();

        await editor.HandleSyncLocalChangesAsync(a.Published.ToList());
        Assert.Equal("Edited on A", editor.EditorTitle); // dirty editor untouched
        Assert.True(editor.IsEditing);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Edited on A", (await a.NoteRepository.GetByIdAsync(note.Id))!.Title);
        var conflict = await a.Conflicts.GetAsync(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(note.Id));
        Assert.NotNull(conflict);
        Assert.Equal("Edited on B", OperationalNoteSyncPayloadSerializer.Deserialize(conflict.RemotePayloadJson!).Title);
        Assert.Equal(OperationalNotesViewModel.NoteSyncConflictMessage, editor.ActionMessage);
    }

    [Fact]
    public async Task NoteEditor_NoteDeletedUnderneath_SaveRecreatesItAndRecordsTheDeletionAsAConflict()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var note = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Date, null));

        var editor = new OperationalNotesViewModel(a.Notes, a.Clock);
        await editor.PendingLoad;
        editor.SelectedNote = editor.Notes.Single(row => row.Id == note.Id);
        editor.EditCommand.Execute(null);
        editor.EditorTitle = "Still needed";

        await a.NoteRepository.DeleteAsync(note.Id); // as an applied remote tombstone would

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Still needed", (await a.NoteRepository.GetByIdAsync(note.Id))!.Title);
        var conflict = await a.Conflicts.GetAsync(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(note.Id));
        Assert.NotNull(conflict);
        Assert.True(conflict.RemoteIsDeleted);
    }

    [Fact]
    public async Task NotesList_NotEditing_ReloadsAfterSyncImport_ButNeverWhileEditing()
    {
        var a = CreateDevice("a", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var note = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Date, null));
        var editor = new OperationalNotesViewModel(a.Notes, a.Clock);
        await editor.PendingLoad;
        var key = new SyncEntityKey(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(note.Id));

        await a.NoteRepository.UpdateAsync(note with { Title = "Imported title" });
        await editor.HandleSyncLocalChangesAsync([key]);
        Assert.Equal("Imported title", editor.Notes.Single().Title);

        editor.NewNoteCommand.Execute(null);
        editor.EditorTitle = "Half-typed";
        await a.NoteRepository.UpdateAsync(note with { Title = "Imported again" });
        await editor.HandleSyncLocalChangesAsync([key]);
        Assert.Equal("Half-typed", editor.EditorTitle);
        Assert.Equal("Imported title", editor.Notes.Single().Title); // not reloaded under an open editor

        editor.CancelCommand.Execute(null);
        await WaitUntilAsync(() => editor.Notes.SingleOrDefault()?.Title == "Imported again"); // deferred reload ran
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    // ------------------------------------------------------------------ fakes

    private sealed class FixedDeviceId(Guid id) : IDeviceIdProvider
    {
        public Guid GetOrCreateDeviceId() => id;
    }

    private sealed class InMemorySyncConfiguration(SyncConfiguration initial) : ISyncConfigurationStore
    {
        private SyncConfiguration _current = initial;

        public Task<SyncConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_current);

        public Task SaveAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _current = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class NoSuggestions : IOneDriveLocationSuggestionProvider
    {
        public IReadOnlyList<string> GetSuggestedRoots() => [];
    }
}
