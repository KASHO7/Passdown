using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPersonalSyncService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PersonalSyncSettingsViewModel"/> tests — never touches the filesystem or a real database.</summary>
internal sealed class FakePersonalSyncService : IPersonalSyncService
{
    public PersonalSyncResult NextResult { get; set; } = PersonalSyncResult.Disabled();

    public int RunCallCount { get; private set; }

    public Task<PersonalSyncResult> RunAsync(CancellationToken cancellationToken = default)
    {
        RunCallCount++;
        return Task.FromResult(NextResult);
    }
}
