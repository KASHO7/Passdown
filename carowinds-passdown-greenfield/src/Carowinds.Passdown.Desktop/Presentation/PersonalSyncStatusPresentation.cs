using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display text for <see cref="PersonalSyncStatus"/> (Phase 16B §14) — the exact five labels the spec calls for, nothing more.
/// Phase 22F: <see cref="IsProblem"/> (folder unreachable / invalid configuration) drives error styling only; the label already says it.
/// </summary>
public sealed record PersonalSyncStatusPresentation(string Text, bool IsProblem = false)
{
    public static PersonalSyncStatusPresentation Build(PersonalSyncStatus status) => status switch
    {
        PersonalSyncStatus.Off => new PersonalSyncStatusPresentation("Off"),
        PersonalSyncStatus.NotConfigured => new PersonalSyncStatusPresentation("Not configured"),
        PersonalSyncStatus.Ready => new PersonalSyncStatusPresentation("Ready"),
        PersonalSyncStatus.FolderUnavailable => new PersonalSyncStatusPresentation("Folder unavailable", IsProblem: true),
        PersonalSyncStatus.ConfigurationError => new PersonalSyncStatusPresentation("Configuration error", IsProblem: true),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
