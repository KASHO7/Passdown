namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>One locally-persisted entity, identified exactly as sync identifies it.</summary>
public sealed record SyncEntityKey(SyncEntityType EntityType, string EntityId);

/// <summary>
/// Phase 18B REL-004: tells already-open editors that Personal Sync (an import, or an explicit "Use
/// Other Device"/"Accept Deletion" conflict resolution) has just changed local data underneath them,
/// so a <em>clean</em> editor can reload. Published only after the change is committed. This is a
/// convenience signal, not the safety mechanism: a dirty editor never relies on it — every editor save
/// is independently conditional on the version the editor loaded (see
/// <see cref="OpenEditorConflictRecorder"/>), so a missed or late notification can never cause a silent
/// overwrite. Handlers run on the publishing (background) thread and must marshal to the UI themselves.
/// </summary>
public interface ISyncLocalChangeNotifier
{
    event EventHandler<IReadOnlyCollection<SyncEntityKey>>? LocalDataChanged;

    void Publish(IReadOnlyCollection<SyncEntityKey> changedEntities);
}

public sealed class SyncLocalChangeNotifier : ISyncLocalChangeNotifier
{
    public event EventHandler<IReadOnlyCollection<SyncEntityKey>>? LocalDataChanged;

    public void Publish(IReadOnlyCollection<SyncEntityKey> changedEntities)
    {
        ArgumentNullException.ThrowIfNull(changedEntities);
        if (changedEntities.Count > 0)
        {
            LocalDataChanged?.Invoke(this, changedEntities);
        }
    }
}
