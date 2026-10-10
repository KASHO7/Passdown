using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Display text for <see cref="PersonalSyncStatus"/> (Phase 16B §14) — the exact five labels the spec calls for, nothing more.</summary>
public sealed record PersonalSyncStatusPresentation(string Text)
{
    public static PersonalSyncStatusPresentation Build(PersonalSyncStatus status) => status switch
    {
        PersonalSyncStatus.Off => new PersonalSyncStatusPresentation("Off"),
        PersonalSyncStatus.NotConfigured => new PersonalSyncStatusPresentation("Not configured"),
        PersonalSyncStatus.Ready => new PersonalSyncStatusPresentation("Ready"),
        PersonalSyncStatus.FolderUnavailable => new PersonalSyncStatusPresentation("Folder unavailable"),
        PersonalSyncStatus.ConfigurationError => new PersonalSyncStatusPresentation("Configuration error"),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
