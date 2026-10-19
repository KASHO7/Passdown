using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates.Installation;

/// <summary>Phase 23D: <see cref="IUpdateOutcomeStore"/> over the updater's record files.</summary>
public sealed class UpdateInstallRecordStore(UpdatePaths paths, string? currentInstallDirectory = null) : IUpdateOutcomeStore
{
    public const int RecordsKept = 10;

    private readonly string _installDirectory = currentInstallDirectory ?? AppContext.BaseDirectory;

    public UpdateInstallRecord? LoadLatestRecord()
    {
        try
        {
            if (!Directory.Exists(paths.Records))
            {
                return null;
            }

            return Directory.EnumerateFiles(paths.Records, "install-*.json")
                .Select(UpdateJson.TryRead<UpdateInstallRecord>)
                .Where(record => record is { FormatVersion: 1 })
                .OrderByDescending(record => record!.UpdatedUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public UpdateInstallOutcome? LoadLatest() => LoadLatestRecord()?.ToOutcome();

    public bool ConfirmLaunch(Guid requestId, SemanticVersion runningVersion)
    {
        var record = UpdateJson.TryRead<UpdateInstallRecord>(paths.RecordFile(requestId));
        if (record is not { Status: UpdateInstallStatus.AwaitingLaunchConfirmation }
            || record.ToVersion != runningVersion.Normalized
            || !UpdatePaths.IsWithin(_installDirectory, record.InstallDirectory)
            || !UpdatePaths.IsWithin(record.InstallDirectory, _installDirectory))
        {
            return false;
        }

        try
        {
            UpdateJson.WriteAtomic(paths.ConfirmationFile(requestId), new LaunchConfirmation(requestId, runningVersion.Normalized, Environment.ProcessId));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Staged folders an unfinished attempt still needs (the updater runs from one); cleanup leaves them.</summary>
    public IReadOnlyList<string> ActiveStagedDirectories()
    {
        try
        {
            if (!Directory.Exists(paths.Records))
            {
                return [];
            }

            return Directory.EnumerateFiles(paths.Records, "install-*.json")
                .Select(UpdateJson.TryRead<UpdateInstallRecord>)
                .Where(record => record is { Status: UpdateInstallStatus.InProgress or UpdateInstallStatus.AwaitingLaunchConfirmation })
                .Select(record => record!.StagedDirectory)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Keeps the newest <see cref="RecordsKept"/> records and removes requests/confirmations of finished attempts. Best-effort.</summary>
    public void Prune()
    {
        try
        {
            if (!Directory.Exists(paths.Records))
            {
                return;
            }

            var records = Directory.EnumerateFiles(paths.Records, "install-*.json")
                .Select(path => (path, record: UpdateJson.TryRead<UpdateInstallRecord>(path)))
                .OrderByDescending(item => item.record?.UpdatedUtc ?? DateTimeOffset.MinValue)
                .ToList();

            foreach (var (path, record) in records.Skip(RecordsKept))
            {
                if (record is { Status: UpdateInstallStatus.InProgress or UpdateInstallStatus.AwaitingLaunchConfirmation })
                {
                    continue;
                }

                File.Delete(path);
                if (record is not null)
                {
                    File.Delete(paths.ConfirmationFile(record.RequestId));
                    File.Delete(paths.RequestFile(record.RequestId));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

/// <summary>Phase 23D: the new version's "I am running" signal to the waiting updater.</summary>
public sealed record LaunchConfirmation(Guid RequestId, string Version, int ProcessId);
