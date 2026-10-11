using System.Reflection;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 11B Part AJ: <see cref="OperationalNotesViewModel"/> driven against the real
/// <see cref="OperationalNotesService"/> plus an in-memory <see cref="IOperationalNoteRepository"/>
/// (never SQLite, but real Application-layer logic — no reimplementation of validation/lifecycle in
/// the view model or in a test double) — proves the two layers actually compose correctly end to end.
/// </summary>
public class OperationalNotesServiceIntegrationTests
{
    private static readonly DateTimeOffset TodayInstant = new(2026, 8, 26, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static (OperationalNotesViewModel ViewModel, OperationalNotesService Service, InMemoryOperationalNoteRepository Repository) Create()
    {
        var repository = new InMemoryOperationalNoteRepository();
        var clock = new FakeClock(TodayInstant);
        var service = new OperationalNotesService(repository, clock);
        var viewModel = new OperationalNotesViewModel(service, clock);
        return (viewModel, service, repository);
    }

    // 1: create via the ViewModel persists through the real service into the repository.
    [Fact]
    public async Task Create_PersistsThroughRealServiceAndRepository()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;

        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "Real create";
        await vm.SaveCommand.ExecuteAsync(null);

        var stored = await service.ListAsync();
        Assert.Single(stored);
        Assert.Equal("Real create", stored[0].Title);
    }

    // 2: edit persists.
    [Fact]
    public async Task Edit_PersistsThroughRealServiceAndRepository()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;
        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "Original";
        await vm.SaveCommand.ExecuteAsync(null);

        vm.EditCommand.Execute(null);
        vm.EditorTitle = "Edited";
        await vm.SaveCommand.ExecuteAsync(null);

        var stored = await service.ListAsync();
        Assert.Single(stored);
        Assert.Equal("Edited", stored[0].Title);
    }

    // 3: resolve persists.
    [Fact]
    public async Task Resolve_PersistsThroughRealServiceAndRepository()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;
        vm.NewNoteCommand.Execute(null);
        vm.EditorTitle = "Resolvable";
        await vm.SaveCommand.ExecuteAsync(null);

        vm.RequestResolveCommand.Execute(null);
        await vm.ConfirmResolveCommand.ExecuteAsync(null);

        var stored = await service.ListAsync();
        Assert.Equal(OperationalNoteStatus.Resolved, stored[0].Status);
    }

    // 4: a Reload sees a note saved through the same service instance (proves the ViewModel doesn't cache stale data across a real service round trip).
    [Fact]
    public async Task Reload_SeesNoteSavedElsewhereThroughTheSameService()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;

        await service.CreateAsync(new OperationalNoteDraft("Created directly", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Today, null));
        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.Contains(vm.Notes, n => n.Title == "Created directly");
    }

    // 5: lifecycle state is derived from the real OperationalNoteLifecycle rules against the reference date.
    [Fact]
    public async Task LifecycleState_ChangesBasedOnOperatingDate()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;
        await service.CreateAsync(new OperationalNoteDraft("Future", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Today.AddDays(3), null));
        await vm.ReloadCommand.ExecuteAsync(null);

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Upcoming;
        Assert.Single(vm.Notes);

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Active;
        Assert.Empty(vm.Notes);
    }

    // 6: an expired note is never silently flipped to persisted Resolved.
    [Fact]
    public async Task ExpiredNote_RemainsPersistedActive()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;
        await service.CreateAsync(new OperationalNoteDraft("Expired", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Today.AddDays(-10), Today.AddDays(-1)));
        await vm.ReloadCommand.ExecuteAsync(null);

        var stored = (await service.ListAsync()).Single();
        Assert.Equal(OperationalNoteStatus.Active, stored.Status);
        Assert.Equal(OperationalNoteLifecycleState.Expired, OperationalNoteLifecycle.Derive(stored, Today));
    }

    // 7: switching filters never mutates a record.
    [Fact]
    public async Task Filtering_NeverMutatesRecords()
    {
        var (vm, service, _) = Create();
        await vm.PendingLoad;
        var created = await service.CreateAsync(new OperationalNoteDraft("A", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Today, null));
        await vm.ReloadCommand.ExecuteAsync(null);

        vm.LifecycleFilter = OperationalNoteLifecycleFilter.All;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Upcoming;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Expired;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;
        vm.LifecycleFilter = OperationalNoteLifecycleFilter.Active;

        var stored = (await service.ListAsync()).Single();
        Assert.Equal(created, stored);
    }

    // 8: no network dependency was introduced anywhere in this chain — same constructor-reflection guarantee OperationalNotesServiceTests already proves at the Application layer, reasserted here at the composition the ViewModel actually uses. Phase 16C adds one sanctioned dependency, ISyncChangeTracker (optional, defaults to null) — still no HTTP/retrieval dependency.
    [Fact]
    public void RealServiceConstructor_HasNoNetworkOrRetrievalDependency()
    {
        var parameters = typeof(OperationalNotesService).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single().GetParameters();

        // Phase 18B REL-004: plus the persistence-only unit-of-work factory and open-editor conflict recorder.
        Assert.Equal(5, parameters.Length);
        Assert.Contains(parameters, p => p.ParameterType == typeof(IOperationalNoteRepository));
        Assert.Contains(parameters, p => p.ParameterType == typeof(Carowinds.Passdown.Application.Time.IClock));
        Assert.Contains(parameters, p => p.ParameterType == typeof(Carowinds.Passdown.Application.PersonalSync.ISyncChangeTracker));
        Assert.Contains(parameters, p => p.ParameterType == typeof(Carowinds.Passdown.Application.Persistence.IUnitOfWorkFactory));
        Assert.Contains(parameters, p => p.ParameterType == typeof(Carowinds.Passdown.Application.PersonalSync.OpenEditorConflictRecorder));
    }
}
