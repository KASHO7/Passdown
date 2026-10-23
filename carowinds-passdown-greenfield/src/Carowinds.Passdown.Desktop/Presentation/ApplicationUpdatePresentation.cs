using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 23D: every sentence Settings → Application Updates shows, in one place. Plain language, no
/// jargon, and never a claim the app cannot back: "available" only after a signed release verified,
/// "updated" only after the new version confirmed it started.
/// </summary>
public static class ApplicationUpdatePresentation
{
    public const string NotConfiguredTitle = "Manual update required";

    public const string NotConfiguredDetail =
        "Automatic updates are not set up in this build. To update, extract the new release ZIP as before.";

    public const string UpToDateTitle = "Carowinds Operations is up to date.";

    public const string NotCheckedTitle = "Updates have not been checked yet.";

    public const string CheckingTitle = "Checking for updates…";

    public const string ReadyDetail =
        "Carowinds Operations will close, update, and open again. Your drafts, notes, prices, History and settings are kept.";

    public const string SavePostponedText =
        "The update was postponed because your Passdown changes could not be saved. Your edits are still on screen. Use Retry Save on the Passdown screen, then choose Restart and Install again.";

    public const string RemindLaterText = "You won't see the update reminder again for this version for 7 days.";

    public static string LastCheckedText(DateTimeOffset? lastSuccessfulCheckUtc) =>
        lastSuccessfulCheckUtc is { } value ? $"Last checked: {EasternTimestampFormat.DateAndTime(value)}" : "Last checked: never";

    public static string CheckFailedText(UpdateSourceFailureKind? kind, DateTimeOffset? retryAfterUtc) => kind switch
    {
        UpdateSourceFailureKind.Offline => "Couldn't check for updates. This computer appears to be offline. The app will try again later.",
        UpdateSourceFailureKind.Timeout => "Couldn't check for updates. GitHub did not respond in time. The app will try again later.",
        UpdateSourceFailureKind.RateLimited => retryAfterUtc is { } retry
            ? $"GitHub asked the app to wait before checking again. Next check after {EasternTimestampFormat.Time(retry)}."
            : "GitHub asked the app to wait before checking again. The app will try again later.",
        _ => "Couldn't check for updates right now. The app will try again later.",
    };

    public static string AvailableTitle(string version) => $"Version {version} is available.";

    public static string? ReleasedText(DateTimeOffset? publishedUtc) =>
        publishedUtc is { } value ? $"Released {EasternTimeZone.ConvertFromUtc(value).ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}" : null;

    public static string DownloadSizeText(long bytes) => $"Download size: {FormatBytes(bytes)}";

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} bytes",
    };

    public static string DownloadingText(UpdateDownloadProgress progress) =>
        progress.BytesReceived >= progress.TotalBytes && progress.TotalBytes > 0
            ? "Verifying the download…"
            : $"Downloading update… {progress.Fraction * 100:0}% ({FormatBytes(progress.BytesReceived)} of {FormatBytes(progress.TotalBytes)})";

    public static string ReadyTitle(string version) => $"Version {version} is ready to install.";

    /// <summary>Null when installation can proceed.</summary>
    public static string? ReadinessText(InstallationReadiness readiness) => readiness.Kind switch
    {
        InstallationReadinessKind.Ready => null,
        InstallationReadinessKind.UnsupportedPlatform => "Updates can be installed only on Windows. Extract the new release ZIP to update.",
        InstallationReadinessKind.NotPortableInstallation =>
            "This copy can't update itself because it isn't running from a standard release folder. Extract the new release ZIP to update.",
        InstallationReadinessKind.NotWritable =>
            "Windows doesn't let this account change the folder the app runs from, so the update can't be installed automatically. No administrator access is requested. Extract the new release ZIP into a folder you can change (for example, in Documents) and run it from there.",
        _ => "This update can't be installed automatically. Extract the new release ZIP to update.",
    };

    public static string StagingFailedText(StagingFailureKind kind) => kind switch
    {
        StagingFailureKind.Download => "The download didn't finish. Check the connection and try again. Nothing was changed.",
        StagingFailureKind.SizeMismatch or StagingFailureKind.HashMismatch or StagingFailureKind.InvalidArchive =>
            "The downloaded update didn't pass verification and was deleted. Nothing was changed.",
        StagingFailureKind.InsufficientDiskSpace => "There isn't enough free disk space to prepare the update. Free some space and try again. Nothing was changed.",
        _ => "The update couldn't be saved on this computer. Try again later. Nothing was changed.",
    };

    public static string LaunchFailedText(UpdateLaunchFailureKind kind) => kind switch
    {
        UpdateLaunchFailureKind.StagedUpdaterMissing => "The prepared update is incomplete and can't be installed. Choose Install Update to download it again. Nothing was changed.",
        _ => "The updater couldn't be started. Nothing was changed. Try again, or extract the new release ZIP to update.",
    };

    /// <summary>A sentence about the previous attempt, shown only when it did not succeed. Null for success or no attempt.</summary>
    public static string? PreviousAttemptText(UpdateInstallOutcome? outcome, string runningVersion)
    {
        if (outcome is null || outcome.Status is UpdateInstallStatus.Succeeded or UpdateInstallStatus.InProgress)
        {
            return null;
        }

        // An attempt that started from some other version is history, not news about this one.
        if (outcome.FromVersion != runningVersion && outcome.Status != UpdateInstallStatus.RecoveryRequired)
        {
            return null;
        }

        if (outcome.Status == UpdateInstallStatus.AwaitingLaunchConfirmation)
        {
            return outcome.ToVersion == runningVersion ? null : $"The update to {outcome.ToVersion} did not finish. Version {runningVersion} is running.";
        }

        return outcome.Status switch
        {
            UpdateInstallStatus.RolledBack =>
                $"The update to {outcome.ToVersion} didn't start correctly, so version {outcome.FromVersion} was restored. No data was changed.",
            UpdateInstallStatus.RecoveryRequired when outcome.FailureCode == "database-upgraded-not-confirmed" =>
                $"The update to {outcome.ToVersion} didn't confirm that it started, and it may already have updated your database, so it wasn't reversed. Nothing was deleted, and a backup of your database from before the update was kept. See UPDATE-RECOVERY.txt in the app folder.",
            UpdateInstallStatus.RecoveryRequired =>
                $"The update to {outcome.ToVersion} couldn't finish, and the previous version couldn't be restored automatically. See UPDATE-RECOVERY.txt in the app folder. Your data is safe.",
            _ => outcome.FailureCode switch
            {
                "installation-in-use" => $"The update to {outcome.ToVersion} wasn't installed because another copy of Carowinds Operations, or the 6 AM scheduled refresh, was using the app folder. Close other copies and try again.",
                "not-writable" => $"The update to {outcome.ToVersion} wasn't installed because Windows didn't allow changes to the app folder. Nothing was changed.",
                "insufficient-disk-space" => $"The update to {outcome.ToVersion} wasn't installed because there wasn't enough free disk space. Nothing was changed.",
                "database-backup-failed" => $"The update to {outcome.ToVersion} wasn't installed because your database couldn't be backed up first. Nothing was changed.",
                "database-in-use" => $"The update to {outcome.ToVersion} wasn't installed because another program was using your database, so it couldn't be backed up. Nothing was changed. Try again later.",
                "database-damaged" => $"The update to {outcome.ToVersion} wasn't installed because your database failed its safety check. Nothing was changed. Export diagnostics and ask for help before updating.",
                "app-did-not-close" => $"The update to {outcome.ToVersion} wasn't installed because the app didn't close. Nothing was changed.",
                _ => $"The update to {outcome.ToVersion} wasn't installed. Nothing was changed.",
            },
        };
    }
}
