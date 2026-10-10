using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>One row for the conflict list (Phase 16D §4) — enough to distinguish and understand a conflict without exposing raw GUIDs as the primary label.</summary>
public sealed record SyncConflictSummary(
    SyncEntityType EntityType,
    string EntityId,
    string DisplayLabel,
    bool IsRemoteDeletion,
    bool IsLocalDeletion,
    Guid RemoteDeviceId,
    int RemoteRevision,
    DateTimeOffset RemoteModifiedUtc,
    DateTimeOffset DetectedAtUtc,
    bool HasNewerRemoteActivity);

/// <summary>One labeled field for a conflict's local-vs-remote comparison (Phase 16D §5/§6) — a summarized comparison, not a raw JSON dump.</summary>
public sealed record ConflictFieldComparison(string Label, string LocalValue, string RemoteValue)
{
    public bool Differs => !string.Equals(LocalValue, RemoteValue, StringComparison.Ordinal);
}

/// <summary>
/// A focused Associate draft conflict comparison (Phase 16D §5) — <see cref="Local"/> is null only for
/// the "local considered this entity deleted, remote sent live content" mirror case (§17);
/// <see cref="Remote"/> is null only when <see cref="IsRemoteDeletion"/> is true.
/// </summary>
public sealed record DraftConflictDetail(
    DateOnly OperatingDate,
    AssociateDraftPayload? Local,
    AssociateDraftPayload? Remote,
    bool IsRemoteDeletion,
    bool IsLocalDeletion,
    Guid RemoteDeviceId,
    DateTimeOffset RemoteModifiedUtc,
    bool HasNewerRemoteActivity)
{
    public IReadOnlyList<ConflictFieldComparison> BuildFieldComparisons() => DraftConflictComparer.Compare(Local, Remote);
}

/// <summary>A focused Operational Note conflict comparison (Phase 16D §6). Same null-only-for-deletion/local-absence convention as <see cref="DraftConflictDetail"/>.</summary>
public sealed record NoteConflictDetail(
    Guid NoteId,
    OperationalNote? Local,
    OperationalNoteSyncPayload? Remote,
    bool IsRemoteDeletion,
    bool IsLocalDeletion,
    Guid RemoteDeviceId,
    DateTimeOffset RemoteModifiedUtc,
    bool HasNewerRemoteActivity)
{
    public IReadOnlyList<ConflictFieldComparison> BuildFieldComparisons() => NoteConflictComparer.Compare(Local, Remote);
}

/// <summary>
/// Builds the summarized (not raw-JSON) side-by-side field comparison for a draft conflict (Phase 16D
/// §5/§24) — every persisted, user-authored field that can differ between two devices' copies of the
/// same operating date. "(deleted on this device)"/"(deleted on other device)" stands in for a null side.
/// </summary>
internal static class DraftConflictComparer
{
    public static IReadOnlyList<ConflictFieldComparison> Compare(AssociateDraftPayload? local, AssociateDraftPayload? remote)
    {
        const string LocalDeletedLabel = "(deleted on this device)";
        const string RemoteDeletedLabel = "(deleted on other device)";

        string L(Func<AssociateDraftPayload, string> selector) => local is null ? LocalDeletedLabel : selector(local);
        string R(Func<AssociateDraftPayload, string> selector) => remote is null ? RemoteDeletedLabel : selector(remote);

        return
        [
            new ConflictFieldComparison("Manual Hours", L(FormatManualHours), R(FormatManualHours)),
            new ConflictFieldComparison("Private Buyout", L(FormatPrivateBuyout), R(FormatPrivateBuyout)),
            new ConflictFieldComparison("Projected Attendance", L(p => p.ProjectedAttendance?.ToString() ?? "—"), R(p => p.ProjectedAttendance?.ToString() ?? "—")),
            new ConflictFieldComparison("Pre-Solds", L(p => p.PreSolds?.ToString() ?? "—"), R(p => p.PreSolds?.ToString() ?? "—")),
            new ConflictFieldComparison("Daily Feature", L(FormatDailyFeature), R(FormatDailyFeature)),
            new ConflictFieldComparison("Product Overrides", L(p => p.ProductOverrides.Count.ToString()), R(p => p.ProductOverrides.Count.ToString())),
            new ConflictFieldComparison("Custom Products", L(FormatCustomProducts), R(FormatCustomProducts)),
            new ConflictFieldComparison("Selected Operational Notes", L(FormatSelectedNotes), R(FormatSelectedNotes)),
        ];
    }

    private static string FormatManualHours(AssociateDraftPayload p) => p.ManualHoursOverride.Enabled
        ? $"{p.ManualHoursOverride.OpeningTime:h\\:mm tt} – {p.ManualHoursOverride.ClosingTime:h\\:mm tt}"
        : "Off";

    private static string FormatPrivateBuyout(AssociateDraftPayload p) => p.PrivateBuyout.Enabled
        ? $"{p.PrivateBuyout.OpeningTime:h\\:mm tt} – {p.PrivateBuyout.ClosingTime:h\\:mm tt}{(string.IsNullOrWhiteSpace(p.PrivateBuyout.Details) ? "" : $" ({p.PrivateBuyout.Details})")}"
        : "Off";

    private static string FormatDailyFeature(AssociateDraftPayload p) =>
        p.DailyFeatureType is null ? "—" : $"{p.DailyFeatureType}: {p.DailyFeatureText}";

    private static string FormatCustomProducts(AssociateDraftPayload p) =>
        p.CustomProducts.Count == 0 ? "None" : string.Join("; ", p.CustomProducts.Select(c => $"{c.DisplayName}{(c.Price is { } price ? $" (${price})" : "")}"));

    private static string FormatSelectedNotes(AssociateDraftPayload p) =>
        p.SelectedOperationalNotes.Count == 0 ? "None" : string.Join("; ", p.SelectedOperationalNotes.Select(n => n.Title));
}

/// <summary>Builds the side-by-side field comparison for a note conflict (Phase 16D §6/§25).</summary>
internal static class NoteConflictComparer
{
    public static IReadOnlyList<ConflictFieldComparison> Compare(OperationalNote? local, OperationalNoteSyncPayload? remote)
    {
        const string LocalDeletedLabel = "(deleted on this device)";
        const string RemoteDeletedLabel = "(deleted on other device)";

        string title = local?.Title ?? LocalDeletedLabel;
        string remoteTitle = remote?.Title ?? RemoteDeletedLabel;
        string body = local is null ? LocalDeletedLabel : (local.Body ?? "—");
        string remoteBody = remote is null ? RemoteDeletedLabel : (remote.Body ?? "—");
        string category = local is null ? LocalDeletedLabel : local.Category.ToString();
        string remoteCategory = remote is null ? RemoteDeletedLabel : remote.Category.ToString();
        string priority = local is null ? LocalDeletedLabel : local.Priority.ToString();
        string remotePriority = remote is null ? RemoteDeletedLabel : remote.Priority.ToString();
        string dates = local is null ? LocalDeletedLabel : FormatDateRange(local.EffectiveFrom, local.EffectiveThrough);
        string remoteDates = remote is null ? RemoteDeletedLabel : FormatDateRange(remote.EffectiveFrom, remote.EffectiveThrough);
        string status = local is null ? LocalDeletedLabel : local.Status.ToString();
        string remoteStatus = remote is null ? RemoteDeletedLabel : remote.Status.ToString();

        return
        [
            new ConflictFieldComparison("Title", title, remoteTitle),
            new ConflictFieldComparison("Body", body, remoteBody),
            new ConflictFieldComparison("Category", category, remoteCategory),
            new ConflictFieldComparison("Priority", priority, remotePriority),
            new ConflictFieldComparison("Effective Dates", dates, remoteDates),
            new ConflictFieldComparison("Status", status, remoteStatus),
        ];
    }

    private static string FormatDateRange(DateOnly from, DateOnly? through) =>
        through is null ? $"{from:yyyy-MM-dd} onward" : $"{from:yyyy-MM-dd} – {through:yyyy-MM-dd}";
}

/// <summary>How one conflict-resolution command ended (Phase 16D §7/§8/§21/§32) — never a thrown exception for an expected, retryable condition.</summary>
public enum ConflictResolutionOutcome
{
    /// <summary>The chosen side was applied/exported and the conflict is now cleared.</summary>
    Resolved,

    /// <summary>No unresolved conflict exists for this entity — either already resolved by a prior call/retry, or it converged on its own. Treated as success (idempotence, §21), not an error.</summary>
    NoConflictFound,

    /// <summary>The configured sync folder is unavailable right now — the conflict is left exactly as it was, safely retryable (§7/§32).</summary>
    ExportUnavailable,

    /// <summary>The requested action does not match the conflict's actual shape (e.g. "Use Other Device" on a remote deletion, which must go through Accept Deletion instead — §15) — nothing was changed.</summary>
    WrongActionForConflict,
}

public sealed record ConflictResolutionResult(ConflictResolutionOutcome Outcome, string? Message)
{
    public static ConflictResolutionResult Resolved() => new(ConflictResolutionOutcome.Resolved, null);

    public static ConflictResolutionResult NoConflictFound() => new(ConflictResolutionOutcome.NoConflictFound, "No unresolved conflict was found for this item — it may already be resolved.");

    public static ConflictResolutionResult ExportUnavailable() => new(ConflictResolutionOutcome.ExportUnavailable, "The sync folder is unavailable right now. Nothing was lost — try again once it's reachable.");

    public static ConflictResolutionResult WrongActionForConflict(string message) => new(ConflictResolutionOutcome.WrongActionForConflict, message);
}

/// <summary>
/// Application service owning every step of conflict review/resolution (Phase 16D §19) — validation,
/// persistence, revision/rebase changes, export, and conflict clearing. ViewModels only ever coordinate
/// UX through this seam; none of this logic lives in a ViewModel.
/// </summary>
public interface IPersonalSyncConflictResolutionService
{
    Task<IReadOnlyList<SyncConflictSummary>> GetConflictsAsync(CancellationToken cancellationToken = default);

    Task<DraftConflictDetail?> GetDraftConflictDetailAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    Task<NoteConflictDetail?> GetNoteConflictDetailAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>"Keep This Device" (§7): the current local content (or current local deletion) becomes the new authoritative revision, exported and rebased so future syncs see it as clean.</summary>
    Task<ConflictResolutionResult> ResolveKeepLocalAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>"Use Other Device" (§8): the persisted conflicting remote payload replaces local content. Refused (<see cref="ConflictResolutionOutcome.WrongActionForConflict"/>) when the conflict is actually a remote deletion — use <see cref="ResolveAcceptDeletionAsync"/> instead (§15).</summary>
    Task<ConflictResolutionResult> ResolveUseRemoteAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>"Accept Deletion" (§15): the remote tombstone wins — the local record is deleted and the tombstone accepted. Refused when the conflict is not actually a remote deletion.</summary>
    Task<ConflictResolutionResult> ResolveAcceptDeletionAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default);
}
