using System.Text;
using System.Text.Json;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>Phase 26: what one official price-history pass did. Counts only — never prices or names.</summary>
/// <param name="ObservationsMerged">Observations this computer did not have before (not "price changes").</param>
/// <param name="ObservationsExtended">Known observations whose contributor has since confirmed the same price again.</param>
/// <param name="ObservationsRejected">Incoming records that failed validation (including identity/content mismatches).</param>
/// <param name="IntegrityConflicts">Same identity, different stored content — the local record was kept.</param>
/// <param name="UnreadableFiles">Contributor files skipped whole: malformed, truncated, oversized, too many bad records, or a newer format.</param>
/// <param name="IoFailures">Files that could not be read or written right now (locked, offline) — retried on a later pass.</param>
/// <param name="Exported">This computer's contributor file was (re)written this pass.</param>
public sealed record OfficialPriceHistorySyncResult(
    int ObservationsMerged,
    int ObservationsExtended,
    int ObservationsRejected,
    int IntegrityConflicts,
    int UnreadableFiles,
    int IoFailures,
    bool Exported)
{
    public static OfficialPriceHistorySyncResult Failed() => new(0, 0, 0, 0, 0, 1, false);
}

/// <summary>Phase 26: one official price-history exchange over the configured Personal Sync folder.</summary>
public interface IOfficialPriceHistorySync
{
    Task<OfficialPriceHistorySyncResult> RunAsync(string selectedRootPath, Guid deviceId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Phase 26: merge-only synchronization of verified official price history through the existing
/// Personal Sync folder (<c>official-price-history/</c>, see <see cref="OfficialPriceHistoryFile"/>).
/// <list type="number">
/// <item><b>Import</b>: reads every contributor file (bounded count and size), validates each record
/// (<see cref="OfficialPriceObservationValidator"/>), drops this computer's own observations, folds
/// duplicates across files by identity (latest confirmation wins — the one mutable field), and merges
/// the whole batch into <c>synced_price_observations</c> in one transaction. A malformed, truncated,
/// newer-format or locked file is skipped and left exactly where it is; nothing local is ever removed.</item>
/// <item><b>Export</b>: writes this computer's file — its own intervals plus everything it has merged,
/// so the combined history survives any one computer's file disappearing — atomically
/// (<see cref="AtomicFileWriter"/>), and only when the content actually changed.</item>
/// </list>
/// Never retrieves anything online, never touches <c>season_price_history</c>, the catalog, drafts or
/// notes, and never decides anything from file timestamps or which computer synced last.
/// </summary>
public sealed class OfficialPriceHistorySync(
    ISeasonPriceHistoryRepository localHistoryRepository,
    ISyncedPriceObservationRepository syncedRepository,
    IDataChangeNotifier? dataChangeNotifier = null,
    ILogger<OfficialPriceHistorySync>? logger = null) : IOfficialPriceHistorySync
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public async Task<OfficialPriceHistorySyncResult> RunAsync(string selectedRootPath, Guid deviceId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRootPath);

        var counters = new Counters();
        var local = await LoadLocalObservationsAsync(deviceId, cancellationToken).ConfigureAwait(false);
        var localIds = local.Select(o => o.ObservationId).ToHashSet(StringComparer.Ordinal);
        var directory = SyncPathService.GetOfficialPriceHistoryDirectoryPath(selectedRootPath);

        var incoming = await ReadIncomingAsync(directory, nowUtc, localIds, counters, cancellationToken).ConfigureAwait(false);

        var merge = SyncedPriceObservationMergeResult.Empty;
        if (incoming.Count > 0)
        {
            merge = await syncedRepository.MergeAsync(incoming, nowUtc, cancellationToken).ConfigureAwait(false);
            if (merge.Conflicts > 0)
            {
                _logger.LogWarning(
                    "Official price history: {ConflictCount} incoming observation(s) claimed an identity already stored with different content; the stored records were kept.",
                    merge.Conflicts);
            }

            if (merge.ChangedHistory)
            {
                _logger.LogInformation(
                    "Official price history: {Added} new observation(s) merged, {Extended} confirmation(s) extended.", merge.Added, merge.Extended);
                dataChangeNotifier?.Publish(new DataChange(DataChangeKind.OfficialPriceHistory));
            }
        }

        var exported = await ExportAsync(selectedRootPath, deviceId, local, counters, cancellationToken).ConfigureAwait(false);

        return new OfficialPriceHistorySyncResult(
            merge.Added, merge.Extended, counters.Rejected, merge.Conflicts, counters.UnreadableFiles, counters.IoFailures, exported);
    }

    private async Task<List<OfficialPriceObservation>> LoadLocalObservationsAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var result = new List<OfficialPriceObservation>();
        var keys = await localHistoryRepository.GetTrackedProductKeysAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            var intervals = await localHistoryRepository.GetHistoryAsync(key, cancellationToken).ConfigureAwait(false);
            foreach (var interval in intervals)
            {
                if (OfficialPriceObservation.FromLocalInterval(interval, deviceId) is { } observation)
                {
                    result.Add(observation);
                }
            }
        }

        return result;
    }

    private async Task<List<OfficialPriceObservation>> ReadIncomingAsync(
        string directory, DateTimeOffset nowUtc, IReadOnlySet<string> localIds, Counters counters, CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, OfficialPriceObservation>(StringComparer.Ordinal);
        if (!Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null)
        {
            return [];
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            counters.IoFailures++;
            return [];
        }

        var candidates = files
            .Where(path => SyncPathService.TryParseOfficialPriceHistoryFileName(Path.GetFileName(path), out _, out _))
            .Where(path => SyncTempFileCleaner.IsContainedDirectlyIn(directory, path))
            .Order(StringComparer.Ordinal)
            .Take(OfficialPriceHistoryFileSerializer.MaxFilesPerPass)
            .ToList();

        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = await ReadFileAsync(path, counters, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                continue;
            }

            var accepted = new List<OfficialPriceObservation>();
            var invalid = 0;
            var firstRejection = OfficialPriceObservationRejection.None;
            foreach (var observation in file.Observations!)
            {
                var rejection = OfficialPriceObservationValidator.Validate(observation, nowUtc);
                if (rejection != OfficialPriceObservationRejection.None)
                {
                    firstRejection = firstRejection == OfficialPriceObservationRejection.None ? rejection : firstRejection;
                    if (++invalid > OfficialPriceHistoryFileSerializer.MaxInvalidObservationsPerFile)
                    {
                        break;
                    }

                    continue;
                }

                accepted.Add(observation);
            }

            if (invalid > 0 && invalid <= OfficialPriceHistoryFileSerializer.MaxInvalidObservationsPerFile)
            {
                // Integrity diagnostic: the file itself is left untouched in the sync folder for investigation.
                _logger.LogWarning(
                    "Official price history: skipped {InvalidCount} observation(s) in {FileName} that failed verification (first reason: {Reason}).",
                    invalid, Path.GetFileName(path), firstRejection);
            }

            if (invalid > OfficialPriceHistoryFileSerializer.MaxInvalidObservationsPerFile)
            {
                // A file this damaged is not trusted at all; it stays where it is for investigation.
                _logger.LogWarning("Official price history: {FileName} has too many invalid observations and was skipped.", Path.GetFileName(path));
                counters.UnreadableFiles++;
                continue;
            }

            counters.Rejected += invalid;
            foreach (var observation in accepted)
            {
                if (localIds.Contains(observation.ObservationId))
                {
                    continue; // this computer's own observation, relayed back — the local row is authoritative
                }

                if (!byId.TryGetValue(observation.ObservationId, out var existing))
                {
                    byId[observation.ObservationId] = observation;
                }
                else if (observation.LastConfirmedUtc > existing.LastConfirmedUtc)
                {
                    byId[observation.ObservationId] = existing with { LastConfirmedUtc = observation.LastConfirmedUtc };
                }
            }
        }

        return byId.Values.OrderBy(o => o.ObservationId, StringComparer.Ordinal).ToList();
    }

    private async Task<OfficialPriceHistoryFile?> ReadFileAsync(string path, Counters counters, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        string json;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null; // vanished between listing and reading — never inferred as anything
            }

            if (info.LinkTarget is not null || info.Length > OfficialPriceHistoryFileSerializer.MaxFileBytes)
            {
                _logger.LogWarning("Official price history: {FileName} is not a regular file within the size limit and was skipped.", name);
                counters.UnreadableFiles++;
                return null;
            }

            json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            counters.IoFailures++;
            return null;
        }

        OfficialPriceHistoryFile? file;
        try
        {
            file = JsonSerializer.Deserialize<OfficialPriceHistoryFile>(json, OfficialPriceHistoryFileSerializer.Options);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Official price history: {FileName} could not be read as price history (malformed or partially written) and was skipped.", name);
            counters.UnreadableFiles++;
            return null;
        }

        if (file is null || !string.Equals(file.Format, OfficialPriceHistoryFileSerializer.FormatName, StringComparison.Ordinal) || file.Observations is null)
        {
            _logger.LogWarning("Official price history: {FileName} is not an official price-history file and was skipped.", name);
            counters.UnreadableFiles++;
            return null;
        }

        if (!OfficialPriceHistoryFileSerializer.IsSupported(file))
        {
            _logger.LogWarning(
                "Official price history: {FileName} uses format version {Version}, newer than this app understands; it was left untouched.", name, file.FormatVersion);
            counters.UnreadableFiles++;
            return null;
        }

        if (file.Observations.Count > OfficialPriceHistoryFileSerializer.MaxObservationsPerFile)
        {
            _logger.LogWarning("Official price history: {FileName} has more observations than the supported limit and was skipped.", name);
            counters.UnreadableFiles++;
            return null;
        }

        return file;
    }

    private async Task<bool> ExportAsync(
        string selectedRootPath, Guid deviceId, IReadOnlyList<OfficialPriceObservation> local, Counters counters, CancellationToken cancellationToken)
    {
        var imported = await syncedRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var all = local
            .Concat(imported.Select(i => i.Observation))
            .GroupBy(o => o.ObservationId, StringComparer.Ordinal)
            .Select(group => group.MaxBy(o => o.LastConfirmedUtc.UtcTicks)!)
            .ToList();

        if (all.Count == 0)
        {
            return false; // nothing to share yet — no empty file is created
        }

        var path = SyncPathService.GetOfficialPriceHistoryFilePath(selectedRootPath, deviceId);
        var content = OfficialPriceHistoryFileSerializer.Serialize(deviceId, all);

        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == Encoding.UTF8.GetByteCount(content)
                && string.Equals(await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false), content, StringComparison.Ordinal))
            {
                return false; // already current — no rewrite, no sync-provider churn
            }

            await AtomicFileWriter.WriteTextAsync(path, content, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Official price history: this computer's file could not be written right now; it will be retried on a later sync.");
            counters.IoFailures++;
            return false;
        }
    }

    private sealed class Counters
    {
        public int Rejected;
        public int UnreadableFiles;
        public int IoFailures;
    }

    /// <summary>True for the exceptions a price-history pass treats as "try again later" rather than a bug.</summary>
    internal static bool IsRecoverable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SqliteException or JsonException;
}
