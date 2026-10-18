using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: per-computer, per-user update state (one small JSON file next to <c>appearance.json</c>).
/// Never part of Personal Sync: what was checked, snoozed or acknowledged on one PC says nothing about another.
/// </summary>
public sealed record UpdateLocalState(
    DateTimeOffset? LastSuccessfulCheckUtc = null,
    DateTimeOffset? NextCheckNotBeforeUtc = null,
    string? SnoozedVersion = null,
    DateTimeOffset? SnoozedUntilUtc = null,
    string? LastAcknowledgedReleaseNotesVersion = null)
{
    public static UpdateLocalState Empty { get; } = new();
}

public interface IUpdateStateStore
{
    /// <summary>Never throws: a missing or unreadable file is <see cref="UpdateLocalState.Empty"/>.</summary>
    UpdateLocalState Load();

    /// <summary>Returns false (and logs) when the file could not be written; callers carry on with the in-memory value.</summary>
    Task<bool> SaveAsync(UpdateLocalState state, CancellationToken cancellationToken = default);
}

/// <summary>Phase 23D: a fully downloaded, verified and extracted release, ready to hand to the external updater.</summary>
public sealed record StagedUpdate(UpdateOffer Offer, string StagedDirectory);

public sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
}

public enum StagingFailureKind
{
    Download,
    SizeMismatch,
    HashMismatch,
    InvalidArchive,
    InsufficientDiskSpace,
    StorageError,
}

public sealed class UpdateStagingException(StagingFailureKind kind, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public StagingFailureKind Kind { get; } = kind;
}

/// <summary>
/// Phase 23D: downloads the package into a dedicated staging folder (never the installation folder and never
/// the SQLite data folder), checks its exact size and SHA-256 against the signed manifest, and extracts it
/// with path and inventory validation. Throws <see cref="UpdateStagingException"/>; a failure leaves no
/// partially extracted folder behind.
/// </summary>
public interface IUpdatePackageStager
{
    Task<StagedUpdate> StageAsync(UpdateOffer offer, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>Removes staging and download leftovers that no pending installation refers to. Best-effort; never throws.</summary>
    void CleanObsolete();
}

public enum InstallationReadinessKind
{
    Ready,

    /// <summary>Not running on Windows — the updater only replaces a Windows portable installation.</summary>
    UnsupportedPlatform,

    /// <summary>Not a portable release folder (e.g. a development build, or the experimental single-file EXE).</summary>
    NotPortableInstallation,

    /// <summary>The current Windows user cannot write to the installation folder.</summary>
    NotWritable,
}

public sealed record InstallationReadiness(InstallationReadinessKind Kind, string InstallDirectory)
{
    public bool IsReady => Kind == InstallationReadinessKind.Ready;
}

/// <summary>Phase 23D: decides — with real checks, not folder-name guesses — whether in-place updating can work here.</summary>
public interface IInstallationInspector
{
    InstallationReadiness Inspect();
}

public enum UpdateLaunchFailureKind
{
    None,
    StagedUpdaterMissing,
    RequestNotWritten,
    UpdaterDidNotStart,
}

public sealed record UpdateLaunchResult(UpdateLaunchFailureKind Failure, Guid RequestId)
{
    public bool Started => Failure == UpdateLaunchFailureKind.None;
}

/// <summary>
/// Phase 23D: writes the validated install request and starts the staged copy of the external updater.
/// The caller must close the application right after a successful launch; the updater waits for this
/// process to exit before touching any installed file.
/// </summary>
public interface IUpdateInstallLauncher
{
    UpdateLaunchResult Launch(StagedUpdate staged, SemanticVersion installedVersion);
}

public enum UpdateInstallStatus
{
    /// <summary>The updater accepted the request and is waiting for the app to close or replacing files.</summary>
    InProgress,

    /// <summary>Files replaced; the new version was started and has not confirmed it is running yet.</summary>
    AwaitingLaunchConfirmation,

    /// <summary>The new version confirmed it started. The only status that counts as a successful update.</summary>
    Succeeded,

    /// <summary>Nothing was changed (app did not close, folder in use, verification failed, no space, no permission).</summary>
    NotInstalled,

    /// <summary>Files were replaced but the new version did not start; the previous version was restored.</summary>
    RolledBack,

    /// <summary>Replacement or rollback failed part-way; the backup folder must be restored by hand.</summary>
    RecoveryRequired,
}

/// <summary>Phase 23D: what the external updater recorded about one installation attempt.</summary>
public sealed record UpdateInstallOutcome(
    Guid RequestId,
    string FromVersion,
    string ToVersion,
    UpdateInstallStatus Status,
    string? FailureCode,
    DateTimeOffset UpdatedUtc,
    string? BackupDirectory);

/// <summary>Phase 23D: the app's side of the install records the external updater writes.</summary>
public interface IUpdateOutcomeStore
{
    /// <summary>The most recent attempt, or null when this profile has never run an in-app update.</summary>
    UpdateInstallOutcome? LoadLatest();

    /// <summary>
    /// Called by the new version once its window is up: tells the waiting updater that the launch worked.
    /// Only confirms an attempt that is awaiting confirmation for exactly <paramref name="runningVersion"/>.
    /// </summary>
    bool ConfirmLaunch(Guid requestId, SemanticVersion runningVersion);
}
