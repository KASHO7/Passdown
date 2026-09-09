using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display text for the Phase 16D conflict list/detail UI — turns <see cref="SyncConflictSummary"/>/
/// <see cref="DraftConflictDetail"/>/<see cref="NoteConflictDetail"/> into the small set of strings the
/// Settings screen's conflict panel renders. No raw GUID or internal technical metadata is exposed here
/// (spec §1) beyond what <see cref="SyncConflictSummary.DisplayLabel"/> already resolved.
/// </summary>
public static class PersonalSyncConflictStatusPresentation
{
    /// <summary>The Settings screen's one-line status callout (spec §27's "Status: Conflict attention needed / 2 conflicts").</summary>
    public static string Format(int conflictCount) =>
        conflictCount switch
        {
            0 => "No conflicts.",
            1 => "1 conflict needs your attention.",
            _ => $"{conflictCount} conflicts need your attention.",
        };
}

/// <summary>
/// The local-vs-remote comparison and available actions for one open conflict (Phase 16D §5/§6/§7/§8/§15)
/// — built from whichever of <see cref="DraftConflictDetail"/>/<see cref="NoteConflictDetail"/> the
/// ViewModel fetched, so the View only ever binds to this one shape regardless of entity type.
/// </summary>
public sealed record ConflictDetailPresentation(
    SyncEntityType EntityType,
    string EntityId,
    string Title,
    bool IsRemoteDeletion,
    bool IsLocalDeletion,
    string RemoteActivityLabel,
    string? NewerActivityNote,
    IReadOnlyList<ConflictFieldComparison> Fields)
{
    /// <summary>"This item was deleted on another device, but changed on this device." (spec §15) for a delete-vs-edit conflict; a neutral description otherwise.</summary>
    public string Explanation => (IsRemoteDeletion, IsLocalDeletion) switch
    {
        (true, _) => "This item was deleted on another device, but changed on this device.",
        (_, true) => "This item was deleted on this device, but changed on another device.",
        _ => "This item was changed on another device at the same time it was changed here.",
    };

    public static ConflictDetailPresentation ForDraft(SyncConflictSummary summary, DraftConflictDetail detail) => new(
        SyncEntityType.AssociateDraft,
        summary.EntityId,
        summary.DisplayLabel,
        detail.IsRemoteDeletion,
        detail.IsLocalDeletion,
        BuildRemoteActivityLabel(detail.RemoteModifiedUtc),
        BuildNewerActivityNote(detail.HasNewerRemoteActivity),
        detail.BuildFieldComparisons());

    public static ConflictDetailPresentation ForNote(SyncConflictSummary summary, NoteConflictDetail detail) => new(
        SyncEntityType.OperationalNote,
        summary.EntityId,
        summary.DisplayLabel,
        detail.IsRemoteDeletion,
        detail.IsLocalDeletion,
        BuildRemoteActivityLabel(detail.RemoteModifiedUtc),
        BuildNewerActivityNote(detail.HasNewerRemoteActivity),
        detail.BuildFieldComparisons());

    private static string BuildRemoteActivityLabel(DateTimeOffset remoteModifiedUtc) =>
        $"Changed on another device on {remoteModifiedUtc.LocalDateTime:MMM d 'at' h:mm tt}.";

    private static string? BuildNewerActivityNote(bool hasNewerRemoteActivity) =>
        hasNewerRemoteActivity ? "The other device has changed this again since this conflict was detected. Resolve this conflict first; the newer change will be picked up on a later sync." : null;
}
