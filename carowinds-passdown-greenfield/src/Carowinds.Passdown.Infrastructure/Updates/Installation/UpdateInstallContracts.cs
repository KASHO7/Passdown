using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates.Installation;

/// <summary>
/// Phase 23D: what the app asks the external updater to do — written to <c>requests\install-&lt;id&gt;.json</c>
/// after the package was verified and staged. The updater accepts nothing else: its command line carries
/// only this file's path, and it re-validates every field (paths, versions, the staged files' hashes) before
/// touching the installation. No command, executable name or URL is ever taken from remote metadata.
/// </summary>
public sealed record UpdateInstallRequest(
    int FormatVersion,
    Guid RequestId,
    string ProductId,
    string FromVersion,
    string ToVersion,
    string InstallDirectory,
    string StagedDirectory,
    int AppProcessId,
    DateTimeOffset? AppProcessStartUtc,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<UpdatePackageFile> Files)
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>
/// Phase 23D: the updater's own account of one attempt, written to <c>records\install-&lt;id&gt;.json</c> after
/// every step, so the app — and a person following the recovery guide — can always tell what happened.
/// Contains versions, status, a failure code and folder locations only; no user data.
/// </summary>
public sealed record UpdateInstallRecord(
    int FormatVersion,
    Guid RequestId,
    string FromVersion,
    string ToVersion,
    string InstallDirectory,
    string StagedDirectory,
    UpdateInstallStatus Status,
    string? FailureCode,
    DateTimeOffset UpdatedUtc,
    string? BackupDirectory,
    IReadOnlyList<UpdateSwapEntry> Journal)
{
    public UpdateInstallOutcome ToOutcome() => new(RequestId, FromVersion, ToVersion, Status, FailureCode, UpdatedUtc, BackupDirectory);
}

/// <summary>One file's progress through the swap: whether an old copy was moved to the backup and whether the new copy was placed.</summary>
public sealed record UpdateSwapEntry(string Path, bool BackedUp, bool Placed);

/// <summary>Phase 23D: stable, non-sensitive failure codes recorded by the updater and explained by the app.</summary>
public static class UpdateFailureCodes
{
    public const string InvalidRequest = "invalid-request";
    public const string AnotherUpdateRunning = "another-update-running";
    public const string AppDidNotClose = "app-did-not-close";
    public const string StagedFilesInvalid = "staged-files-invalid";
    public const string InstallationInUse = "installation-in-use";
    public const string NotWritable = "not-writable";
    public const string InsufficientDiskSpace = "insufficient-disk-space";
    public const string CopyFailed = "copy-failed";
    public const string ReplaceFailed = "replace-failed";
    public const string LaunchFailed = "launch-failed";
    public const string NewVersionDidNotStart = "new-version-did-not-start";
    public const string RollbackFailed = "rollback-failed";
}

internal static class UpdateJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>Request and record files are small; anything bigger is not ours.</summary>
    public const long MaximumFileBytes = 4 * 1024 * 1024;

    public static T? TryRead<T>(string path)
        where T : class
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumFileBytes || UpdatePaths.IsLink(info))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Temp-then-rename, so a reader never sees half a file.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
