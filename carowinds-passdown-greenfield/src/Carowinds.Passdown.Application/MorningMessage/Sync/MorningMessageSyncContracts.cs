using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.MorningMessage.Sync;

/// <summary>Phase 30: what one Morning Message sync pass did. Counts only — never message text.</summary>
/// <param name="Ran">False when Morning Message Sync is off on this computer; nothing was read or written.</param>
/// <param name="DatesUpdated">Dates whose text on this computer changed because of another computer.</param>
/// <param name="PreferencesUpdated">The composer preferences on this computer changed because of another computer.</param>
/// <param name="ConflictCount">Conflicts waiting for review after this pass (dates plus preferences).</param>
/// <param name="DeferredCount">Changes held back because the composer is open on what they would change.</param>
/// <param name="UnreadableFiles">Shared files skipped whole (malformed, truncated, oversized, a newer format).</param>
/// <param name="RejectedValues">Individual values skipped because they failed validation.</param>
/// <param name="IoFailures">Files that could not be read or written right now — retried on a later pass.</param>
/// <param name="Exported">This computer's shared file was (re)written.</param>
public sealed record MorningMessageSyncResult(
    bool Ran,
    int DatesUpdated,
    bool PreferencesUpdated,
    int ConflictCount,
    int DeferredCount,
    int UnreadableFiles,
    int RejectedValues,
    int IoFailures,
    bool Exported)
{
    public static MorningMessageSyncResult NotRun { get; } = new(false, 0, false, 0, 0, 0, 0, 0, false);

    public static MorningMessageSyncResult Failed() => new(true, 0, false, 0, 0, 0, 0, 1, false);
}

/// <summary>Phase 30: one Morning Message exchange over the configured Personal Sync folder (run by the Personal Sync pass).</summary>
public interface IMorningMessageSync
{
    Task<MorningMessageSyncResult> RunAsync(string selectedRootPath, Guid deviceId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Phase 30: the Morning Messages line in Settings → Personal Sync.</summary>
public enum MorningMessageSyncStatus
{
    /// <summary>The option is off on this computer.</summary>
    NotSyncing,

    /// <summary>The option is on, but Personal Sync has no usable folder (off, or no folder chosen).</summary>
    NeedsSyncFolder,

    /// <summary>The shared folder could not be reached on the last attempt.</summary>
    FolderUnavailable,

    /// <summary>At least one conflict is waiting for Keep This Device / Use Other Device.</summary>
    ConflictNeedsReview,

    /// <summary>Local edits (or changes held while the composer was open) have not been exchanged yet.</summary>
    ChangesWaiting,

    /// <summary>Everything on this computer matches its last completed exchange.</summary>
    Synced,
}

public sealed record MorningMessageSyncOverview(bool Enabled, MorningMessageSyncStatus Status, int ConflictCount, DateTimeOffset? LastSyncedUtc);

/// <summary>One field of a conflict: both raw synchronized values (see <see cref="MorningMessageSyncFields"/> for their text forms); the review screen formats them.</summary>
public sealed record MorningMessageConflictField(string Field, string Label, string ThisDevice, string OtherDevice);

/// <summary>
/// Phase 30: one conflicted record — a date's message, or the composer preferences. Keep This Device / Use
/// Other Device applies to every conflicting field of the record; fields that merged cleanly are not listed.
/// </summary>
/// <param name="OperatingDate">The date, or null for the composer preferences.</param>
public sealed record MorningMessageConflict(DateOnly? OperatingDate, IReadOnlyList<MorningMessageConflictField> Fields, DateTimeOffset DetectedUtc)
{
    public string Title => OperatingDate is { } date
        ? "Morning message for " + date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-US"))
        : "Morning message preferences";
}

public enum MorningMessageConflictResolution
{
    KeepThisDevice,
    UseOtherDevice,
}

public enum MorningMessageResolveOutcome
{
    Resolved,
    NoConflictFound,

    /// <summary>The composer is open on this record; close it first so nothing typed is overwritten.</summary>
    ComposerOpen,

    /// <summary>The local files could not be written; nothing changed.</summary>
    Failed,
}

/// <summary>Phase 30: the Settings → Personal Sync → Sync Morning Messages surface.</summary>
public interface IMorningMessageSyncService
{
    /// <param name="personalSyncStatus">The Personal Sync folder status the settings screen already computed.</param>
    Task<MorningMessageSyncOverview> GetOverviewAsync(PersonalSyncStatus personalSyncStatus, CancellationToken cancellationToken = default);

    /// <summary>Saves this computer's choice only. Turning it off deletes nothing — local messages, shared files and sync history all stay.</summary>
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MorningMessageConflict>> GetConflictsAsync(CancellationToken cancellationToken = default);

    Task<MorningMessageResolveOutcome> ResolveAsync(DateOnly? operatingDate, MorningMessageConflictResolution resolution, CancellationToken cancellationToken = default);
}

/// <summary>Phase 30: what a sync pass (or a conflict resolution) changed or held back — published to the composer.</summary>
public sealed record MorningMessageSyncChange(
    IReadOnlyList<DateOnly> UpdatedDates,
    bool PreferencesUpdated,
    IReadOnlyList<DateOnly> DeferredDates,
    bool PreferencesDeferred)
{
    public bool IsEmpty => UpdatedDates.Count == 0 && !PreferencesUpdated && DeferredDates.Count == 0 && !PreferencesDeferred;
}

/// <summary>
/// Phase 30: the one coordination point between the Morning Message composer and Morning Message sync.
/// While the composer is open on a date, sync never writes that date's text or the preferences — the
/// composer's own debounced saves stay the only writer, so nothing being typed is overwritten. Held changes
/// are applied by the next pass after the composer closes; because they were not applied, an edit made
/// meanwhile is still recognized as independent and becomes a conflict instead of silently winning or losing.
/// <see cref="LockAsync"/> serializes "open the composer and load" against "apply imported changes".
/// </summary>
public sealed class MorningMessageEditorSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateOnly? _openDate;

    /// <summary>The date the composer is showing, or null when it is closed. Read and change only while holding <see cref="LockAsync"/>.</summary>
    public DateOnly? OpenDate => _openDate;

    public bool IsComposerOpen => _openDate is not null;

    /// <summary>Kept current by the Morning Message sync settings so the composer requests a sync only when the option is on.</summary>
    public bool SyncEnabled { get; set; }

    /// <summary>Raised (on a background thread) when sync changed local message text or preferences, or held a change back.</summary>
    public event EventHandler<MorningMessageSyncChange>? Changed;

    /// <summary>Raised when the composer saved an edit or closed, so a sync pass can carry the change (the app wires this to Personal Sync).</summary>
    public event EventHandler? SyncRequested;

    public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_gate);
    }

    /// <summary>Call while holding <see cref="LockAsync"/>.</summary>
    public void MarkOpen(DateOnly operatingDate) => _openDate = operatingDate;

    /// <summary>Call while holding <see cref="LockAsync"/>.</summary>
    public void MarkClosed() => _openDate = null;

    public void PublishChange(MorningMessageSyncChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!change.IsEmpty)
        {
            Changed?.Invoke(this, change);
        }
    }

    public void RequestSync()
    {
        if (SyncEnabled)
        {
            SyncRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
