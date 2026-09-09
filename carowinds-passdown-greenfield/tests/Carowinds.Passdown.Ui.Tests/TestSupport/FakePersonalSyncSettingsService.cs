using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPersonalSyncSettingsService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PersonalSyncSettingsViewModel"/> tests — never touches the filesystem.</summary>
internal sealed class FakePersonalSyncSettingsService : IPersonalSyncSettingsService
{
    public bool PersonalSyncEnabled { get; set; }

    public string? SyncRootPath { get; set; }

    public PersonalSyncStatus Status { get; set; } = PersonalSyncStatus.Off;

    public string? StatusDetail { get; set; }

    public IReadOnlyList<string> SuggestedRoots { get; set; } = [];

    public Guid DeviceId { get; set; } = Guid.NewGuid();

    public int LoadCallCount { get; private set; }

    public int SetEnabledCallCount { get; private set; }

    public int SetSyncRootCallCount { get; private set; }

    public Func<PersonalSyncSettingsSnapshot>? SnapshotFactory { get; set; }

    public Task<PersonalSyncSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        LoadCallCount++;
        return Task.FromResult(BuildSnapshot());
    }

    public Task<PersonalSyncSettingsSnapshot> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        SetEnabledCallCount++;
        PersonalSyncEnabled = enabled;
        return Task.FromResult(BuildSnapshot());
    }

    public Task<PersonalSyncSettingsSnapshot> SetSyncRootAsync(string syncRootPath, CancellationToken cancellationToken = default)
    {
        SetSyncRootCallCount++;
        SyncRootPath = syncRootPath;
        return Task.FromResult(BuildSnapshot());
    }

    private PersonalSyncSettingsSnapshot BuildSnapshot() =>
        SnapshotFactory?.Invoke() ?? new PersonalSyncSettingsSnapshot(PersonalSyncEnabled, SyncRootPath, Status, StatusDetail, SuggestedRoots, DeviceId);
}
