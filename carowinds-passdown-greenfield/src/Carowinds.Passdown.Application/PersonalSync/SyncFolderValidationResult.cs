namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>Outcome of validating a user-chosen sync root (Phase 16B §6).</summary>
public enum SyncFolderValidationOutcome
{
    /// <summary>The folder (or its app-owned subfolder) exists or was created, and is writable.</summary>
    Valid,

    /// <summary>Structurally unsafe — e.g. resolves inside the app's own local data directory. Will never become valid without choosing a different folder.</summary>
    Invalid,

    /// <summary>Not currently usable for a reason that may be transient — missing, unwritable, or otherwise inaccessible right now. May become valid again without changing the configured path.</summary>
    Unavailable,
}

public sealed record SyncFolderValidationResult(SyncFolderValidationOutcome Outcome, string? Message)
{
    public static SyncFolderValidationResult Valid() => new(SyncFolderValidationOutcome.Valid, null);

    public static SyncFolderValidationResult Invalid(string message) => new(SyncFolderValidationOutcome.Invalid, message);

    public static SyncFolderValidationResult Unavailable(string message) => new(SyncFolderValidationOutcome.Unavailable, message);
}

/// <summary>
/// Validates a user-chosen sync root before Personal Sync is allowed to report itself Ready (Phase
/// 16B §6). This validates a usable filesystem location — it never authenticates or otherwise talks
/// to the OneDrive service, and it never claims to prove the folder really is OneDrive-synced.
/// </summary>
public interface ISyncFolderValidator
{
    Task<SyncFolderValidationResult> ValidateAsync(string selectedRootPath, CancellationToken cancellationToken = default);
}
