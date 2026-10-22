using System.Text;
using System.Text.Json;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.Preferences;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// Phase 30: optional Morning Message sync over the existing Personal Sync folder
/// (<c>morning-message/&lt;device-id&gt;.json</c>, see <see cref="MorningMessageSyncFile"/>), and the Settings
/// surface for it (<see cref="IMorningMessageSyncService"/>).
/// <list type="number">
/// <item><b>Off by default, per computer.</b> While <see cref="SyncConfiguration.MorningMessageSyncEnabled"/> is
/// off on this computer nothing in the shared folder is read or written and no local file changes.</item>
/// <item><b>Import</b>: every other computer's file is read (bounded count and size) and validated; each
/// synchronized field (<see cref="MorningMessageSyncFields"/>) is reconciled on its own by
/// <see cref="MorningMessageFieldReconciliation"/> — a newer descendant replaces the local value, independent
/// edits to different fields both survive, and independent different edits to the same field become a
/// conflict that keeps the local value and preserves the other one. Nothing is decided by clocks or file times.</item>
/// <item><b>Composer safety</b>: while the composer is open (<see cref="MorningMessageEditorSession"/>), the
/// open date and the preferences are left alone and reported as waiting.</item>
/// <item><b>Write order</b>: local message files first (each one all-or-nothing), then the local sync state,
/// then this computer's shared file — so a version is never published that this computer has not recorded,
/// and an interruption at any point is re-detected safely on the next pass.</item>
/// </list>
/// Absence is never deletion: a missing file, date or field removes nothing anywhere. Logs carry counts and
/// file names only, never message text.
/// </summary>
public sealed class MorningMessageSync(
    ISyncConfigurationStore configurationStore,
    IMorningMessageContentStore contentStore,
    IMorningMessagePreferenceStore preferenceStore,
    MorningMessageSyncStateStore stateStore,
    MorningMessageEditorSession session,
    IDeviceIdProvider deviceIdProvider,
    ILogger<MorningMessageSync>? logger = null) : IMorningMessageSync, IMorningMessageSyncService
{
    /// <summary>Dates each computer keeps (<see cref="FileMorningMessageContentStore.RetainedDates"/>); dates older than that are never imported.</summary>
    public const int RetainedDates = FileMorningMessageContentStore.RetainedDates;

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    // ----- The pass -----

    public async Task<MorningMessageSyncResult> RunAsync(string selectedRootPath, Guid deviceId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRootPath);

        var configuration = await configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        session.SyncEnabled = configuration.MorningMessageSyncEnabled;
        if (!configuration.MorningMessageSyncEnabled)
        {
            return MorningMessageSyncResult.NotRun;
        }

        var counters = new Counters();
        var directory = SyncPathService.GetMorningMessageDirectoryPath(selectedRootPath);
        var shared = await ReadSharedAsync(directory, deviceId, counters, cancellationToken).ConfigureAwait(false);

        MorningMessageSyncChange change;
        using (await session.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (shared is null)
            {
                await SaveStatusAsync(state, failed: true, deferred: state.DeferredCount, syncedAt: null, cancellationToken).ConfigureAwait(false);
                return MorningMessageSyncResult.Failed() with { UnreadableFiles = counters.UnreadableFiles, ConflictCount = CountConflictRecords(state) };
            }

            state.Counter = Math.Max(state.Counter, shared.HighestOwnCounter);

            var content = new Dictionary<DateOnly, MorningMessageContent>(await contentStore.LoadAllAsync(cancellationToken).ConfigureAwait(false));
            var preferences = preferenceStore.Load();
            var plan = Reconcile(state, content, preferences, shared.Files, deviceId, nowUtc, session.OpenDate);

            try
            {
                if (plan.ContentWrites.Count > 0)
                {
                    await contentStore.SaveManyAsync(plan.ContentWrites, cancellationToken).ConfigureAwait(false);
                }

                if (plan.PreferencesWrite is { } newPreferences)
                {
                    await preferenceStore.SaveAsync(newPreferences, cancellationToken).ConfigureAwait(false);
                }

                // Recorded before anything is published (see the type summary).
                state.DeferredCount = plan.Deferred;
                await stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Morning Message sync: the messages on this computer could not be updated right now ({ExceptionType}); nothing was published.", ex.GetType().Name);
                return MorningMessageSyncResult.Failed() with { ConflictCount = CountConflictRecords(state) };
            }

            // What this computer publishes: only values whose recorded version describes them exactly, for
            // the dates it actually keeps after the write (retention included).
            var finalContent = plan.ContentWrites.Count > 0
                ? await contentStore.LoadAllAsync(cancellationToken).ConfigureAwait(false)
                : content;

            var exported = await ExportAsync(selectedRootPath, deviceId, state, finalContent, plan.PreferencesWrite ?? preferences, counters, cancellationToken).ConfigureAwait(false);
            var failed = counters.IoFailures > 0;
            await SaveStatusAsync(state, failed, plan.Deferred, failed ? null : nowUtc, cancellationToken).ConfigureAwait(false);

            change = new MorningMessageSyncChange(plan.UpdatedDates, plan.PreferencesWrite is not null, plan.DeferredDates, plan.PreferencesDeferred);

            if (plan.UpdatedDates.Count > 0 || plan.PreferencesWrite is not null || plan.ConflictsAdded > 0)
            {
                _logger.LogInformation(
                    "Morning Message sync: {Dates} date(s) and {Preferences} preference set updated from another computer; {Conflicts} conflict record(s) waiting.",
                    plan.UpdatedDates.Count, plan.PreferencesWrite is null ? 0 : 1, CountConflictRecords(state));
            }

            var result = new MorningMessageSyncResult(
                true, plan.UpdatedDates.Count, plan.PreferencesWrite is not null, CountConflictRecords(state), plan.Deferred,
                counters.UnreadableFiles, shared.Files.Sum(file => file.RejectedValues), counters.IoFailures, exported);

            session.PublishChange(change);
            return result;
        }
    }

    private async Task SaveStatusAsync(MorningMessageSyncState state, bool failed, int deferred, DateTimeOffset? syncedAt, CancellationToken cancellationToken)
    {
        state.LastPassFailed = failed;
        state.DeferredCount = deferred;
        if (syncedAt is { } at)
        {
            state.LastSyncedUtc = at;
        }

        try
        {
            await stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Morning Message sync: the sync status could not be saved on this computer ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private sealed record SharedRead(IReadOnlyList<MorningMessageSyncFileContent> Files, long HighestOwnCounter);

    /// <summary>Null when the folder itself could not be listed (offline, locked) — nothing is then changed locally.</summary>
    private async Task<SharedRead?> ReadSharedAsync(string directory, Guid deviceId, Counters counters, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return new SharedRead([], 0); // nobody has published yet
        }

        string[] paths;
        try
        {
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                _logger.LogWarning("Morning Message sync: the morning-message folder is a link and was not read.");
                counters.UnreadableFiles++;
                return new SharedRead([], 0);
            }

            paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            counters.IoFailures++;
            return null;
        }

        var files = new List<MorningMessageSyncFileContent>();
        long highestOwnCounter = 0;
        var candidates = paths
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Where(entry => SyncPathService.TryParseMorningMessageFileName(entry.Name, out _, out _))
            .Where(entry => SyncTempFileCleaner.IsContainedDirectlyIn(directory, entry.Path))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Take(MorningMessageSyncFileSerializer.MaxFilesPerPass);

        foreach (var (path, name) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncPathService.TryParseMorningMessageFileName(name, out var fileDevice, out _);

            var content = await ReadFileAsync(path, name, fileDevice, counters, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                continue;
            }

            if (fileDevice == deviceId)
            {
                // This computer's own earlier file is never imported (the local files are authoritative); it
                // only guarantees new edit counters stay above any it already published, even if the local
                // sync state was lost.
                highestOwnCounter = Math.Max(highestOwnCounter, HighestCounterFor(content, deviceId));
                continue;
            }

            highestOwnCounter = Math.Max(highestOwnCounter, HighestCounterFor(content, deviceId));
            files.Add(content);
        }

        return new SharedRead(files, highestOwnCounter);
    }

    private static long HighestCounterFor(MorningMessageSyncFileContent content, Guid deviceId) =>
        content.Preferences.Values.Concat(content.Dates.Values.SelectMany(fields => fields.Values))
            .Select(field => field.Version.CounterFor(deviceId))
            .DefaultIfEmpty(0)
            .Max();

    private async Task<MorningMessageSyncFileContent?> ReadFileAsync(string path, string name, Guid fileDevice, Counters counters, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            if (info.LinkTarget is not null || info.Length > MorningMessageSyncFileSerializer.MaxFileBytes)
            {
                _logger.LogWarning("Morning Message sync: {FileName} is not a regular file within the size limit and was skipped.", name);
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

        MorningMessageSyncFile? file;
        try
        {
            file = JsonSerializer.Deserialize<MorningMessageSyncFile>(json, MorningMessageSyncFileSerializer.Options);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Morning Message sync: {FileName} could not be read (malformed or partially written) and was skipped.", name);
            counters.UnreadableFiles++;
            return null;
        }

        var (content, problem) = MorningMessageSyncFileSerializer.Validate(file, fileDevice);
        if (content is null)
        {
            _logger.LogWarning("Morning Message sync: {FileName} was skipped and left untouched ({Problem}).", name, problem);
            counters.UnreadableFiles++;
            return null;
        }

        if (content.RejectedValues > 0)
        {
            _logger.LogWarning("Morning Message sync: {Count} value(s) in {FileName} failed validation and were skipped.", content.RejectedValues, name);
        }

        return content;
    }

    // ----- Reconciliation (pure apart from the state object it updates) -----

    private sealed class Plan
    {
        public Dictionary<DateOnly, MorningMessageContent> ContentWrites { get; } = [];

        public MorningMessagePreferences? PreferencesWrite { get; set; }

        public List<DateOnly> UpdatedDates { get; } = [];

        public List<DateOnly> DeferredDates { get; } = [];

        public bool PreferencesDeferred { get; set; }

        public int Deferred { get; set; }

        public int ConflictsAdded { get; set; }
    }

    private static Plan Reconcile(
        MorningMessageSyncState state,
        IReadOnlyDictionary<DateOnly, MorningMessageContent> content,
        MorningMessagePreferences preferences,
        IReadOnlyList<MorningMessageSyncFileContent> files,
        Guid self,
        DateTimeOffset nowUtc,
        DateOnly? heldDate)
    {
        var plan = new Plan();
        var composerOpen = heldDate is not null;

        // Preferences.
        var preferenceValues = MorningMessageSyncFields.FromPreferences(preferences);
        var preferenceChanges = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in MorningMessageSyncFields.PreferenceFields)
        {
            var remotes = files.Select(file => file.Preferences.GetValueOrDefault(field)).OfType<MorningMessageFieldVersion>().ToList();
            var fieldState = state.Get(null, field);
            if (composerOpen)
            {
                if (HasUnseen(remotes, fieldState))
                {
                    plan.PreferencesDeferred = true;
                    plan.Deferred++;
                }

                continue;
            }

            var local = preferenceValues[field];
            var offered = fieldState is null && MorningMessageSyncFields.IsDefaultPreferenceValue(field, local) ? null : local;
            Apply(state, plan, null, field, offered, local, remotes, self, nowUtc, preferenceChanges);
        }

        if (preferenceChanges.Count > 0)
        {
            plan.PreferencesWrite = MorningMessageSyncFields.ApplyToPreferences(preferences, preferenceChanges);
        }

        // Daily content. Dates already on this computer, plus other computers' dates that would fall inside
        // the newest RetainedDates of the combined set (older ones would be dropped again at once).
        var remoteDates = files.SelectMany(file => file.Dates.Keys).Distinct().ToList();
        var remoteOnly = remoteDates.Where(date => !content.ContainsKey(date) && !state.Dates.ContainsKey(date)).ToList();
        var admitted = content.Keys.Concat(remoteOnly)
            .OrderByDescending(date => date)
            .Take(RetainedDates)
            .ToHashSet();

        // A date this computer has synchronized before but no longer keeps (aged out of its 60) is not in
        // either list: no opinion, never a deletion, and never re-imported.
        var emptyValues = MorningMessageSyncFields.FromContent(MorningMessageContent.Empty);
        foreach (var date in content.Keys.Concat(remoteOnly).Distinct().Order())
        {
            var present = content.TryGetValue(date, out var current);
            if (!present && !admitted.Contains(date))
            {
                continue;
            }

            var localValues = present ? MorningMessageSyncFields.FromContent(current!) : null;
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in MorningMessageSyncFields.ContentFields)
            {
                var remotes = files
                    .Select(file => file.Dates.TryGetValue(date, out var fields) ? fields.GetValueOrDefault(field) : null)
                    .OfType<MorningMessageFieldVersion>()
                    .ToList();
                var fieldState = state.Get(date, field);

                if (date == heldDate)
                {
                    if (HasUnseen(remotes, fieldState))
                    {
                        if (!plan.DeferredDates.Contains(date))
                        {
                            plan.DeferredDates.Add(date);
                        }

                        plan.Deferred++;
                    }

                    continue;
                }

                var local = localValues?[field];
                var offered = local is null || (fieldState is null && MorningMessageSyncFields.IsUnsetContentValue(field, local)) ? null : local;
                Apply(state, plan, date, field, offered, local ?? emptyValues[field], remotes, self, nowUtc, changes);
            }

            if (changes.Count > 0)
            {
                plan.ContentWrites[date] = MorningMessageSyncFields.ApplyToContent(current ?? MorningMessageContent.Empty, changes);
                plan.UpdatedDates.Add(date);
            }
        }

        // Sync bookkeeping for dates this computer no longer keeps (aged out) is dropped; the other
        // computers still hold their copies, and nothing is deleted anywhere.
        foreach (var date in state.Dates.Keys.Where(date => !content.ContainsKey(date) && !plan.ContentWrites.ContainsKey(date)).ToList())
        {
            state.Dates.Remove(date);
            state.Conflicts.RemoveAll(conflict => conflict.Date == date);
        }

        return plan;
    }

    private static bool HasUnseen(IReadOnlyList<MorningMessageFieldVersion> remotes, MorningMessageFieldState? fieldState) =>
        remotes.Any(remote => !(fieldState?.Version ?? MorningMessageVersion.Empty).Covers(remote.Version));

    private static void Apply(
        MorningMessageSyncState state,
        Plan plan,
        DateOnly? date,
        string field,
        string? offered,
        string current,
        IReadOnlyList<MorningMessageFieldVersion> remotes,
        Guid self,
        DateTimeOffset nowUtc,
        Dictionary<string, string> changes)
    {
        var decision = MorningMessageFieldReconciliation.Decide(offered, state.Get(date, field), remotes, self, state.NextCounter);

        if (decision.State is not null)
        {
            state.Set(date, field, decision.State);
        }

        if (decision.Value is { } value && !string.Equals(value, current, StringComparison.Ordinal))
        {
            changes[field] = value;
        }

        var hadConflict = state.GetConflict(date, field) is not null;
        state.SetConflict(date, field, decision.Outcome == MorningMessageFieldOutcome.Conflict ? decision.Other : null, nowUtc);
        if (decision.Outcome == MorningMessageFieldOutcome.Conflict && !hadConflict)
        {
            plan.ConflictsAdded++;
        }
    }

    // ----- Export -----

    private async Task<bool> ExportAsync(
        string root,
        Guid deviceId,
        MorningMessageSyncState state,
        IReadOnlyDictionary<DateOnly, MorningMessageContent> content,
        MorningMessagePreferences preferences,
        Counters counters,
        CancellationToken cancellationToken)
    {
        var preferenceValues = MorningMessageSyncFields.FromPreferences(preferences);
        var exportedPreferences = Describe(state.Preferences, preferenceValues);

        var exportedDates = new Dictionary<DateOnly, IReadOnlyDictionary<string, MorningMessageFieldVersion>>();
        foreach (var (date, fields) in state.Dates)
        {
            if (content.TryGetValue(date, out var dateContent))
            {
                exportedDates[date] = Describe(fields, MorningMessageSyncFields.FromContent(dateContent));
            }
        }

        if (exportedPreferences.Count == 0 && exportedDates.Values.All(fields => fields.Count == 0))
        {
            return false; // nothing to share yet — no empty file is created
        }

        var path = SyncPathService.GetMorningMessageFilePath(root, deviceId);
        var text = MorningMessageSyncFileSerializer.Serialize(deviceId, exportedPreferences, exportedDates);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length == Encoding.UTF8.GetByteCount(text)
                && string.Equals(await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false), text, StringComparison.Ordinal))
            {
                return false; // already current — no rewrite, no sync-provider churn
            }

            await AtomicFileWriter.WriteTextAsync(path, text, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Morning Message sync: this computer's shared file could not be written right now; it will be retried on a later sync.");
            counters.IoFailures++;
            return false;
        }
    }

    /// <summary>A field is published only when its current local value is exactly the one its recorded version describes (an unrecorded edit waits for the next pass).</summary>
    private static Dictionary<string, MorningMessageFieldVersion> Describe(
        IReadOnlyDictionary<string, MorningMessageFieldState> states, IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, MorningMessageFieldVersion>(StringComparer.Ordinal);
        foreach (var (field, fieldState) in states)
        {
            if (values.TryGetValue(field, out var value) && MorningMessageSyncFields.Hash(value) == fieldState.Hash)
            {
                result[field] = new MorningMessageFieldVersion(value, fieldState.Version);
            }
        }

        return result;
    }

    private sealed class Counters
    {
        public int UnreadableFiles;
        public int IoFailures;
    }

    /// <summary>Conflicted records (a date, or the preferences) — what the status line and review list count.</summary>
    private static int CountConflictRecords(MorningMessageSyncState state) => state.Conflicts.Select(conflict => conflict.Date).Distinct().Count();

    /// <summary>True for the exceptions a Morning Message pass treats as "try again later" rather than a bug.</summary>
    internal static bool IsRecoverable(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException;

    // ----- Settings surface -----

    public async Task<MorningMessageSyncOverview> GetOverviewAsync(PersonalSyncStatus personalSyncStatus, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        session.SyncEnabled = configuration.MorningMessageSyncEnabled;
        var state = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var conflicts = CountConflictRecords(state);

        if (!configuration.MorningMessageSyncEnabled)
        {
            return new MorningMessageSyncOverview(false, MorningMessageSyncStatus.NotSyncing, conflicts, state.LastSyncedUtc);
        }

        var status = personalSyncStatus switch
        {
            PersonalSyncStatus.Off or PersonalSyncStatus.NotConfigured or PersonalSyncStatus.ConfigurationError => MorningMessageSyncStatus.NeedsSyncFolder,
            PersonalSyncStatus.FolderUnavailable => MorningMessageSyncStatus.FolderUnavailable,
            _ when conflicts > 0 => MorningMessageSyncStatus.ConflictNeedsReview,
            _ when state.LastPassFailed => MorningMessageSyncStatus.FolderUnavailable,
            _ when state.LastSyncedUtc is null || state.DeferredCount > 0 || await HasUnpublishedEditsAsync(state, cancellationToken).ConfigureAwait(false)
                => MorningMessageSyncStatus.ChangesWaiting,
            _ => MorningMessageSyncStatus.Synced,
        };

        return new MorningMessageSyncOverview(true, status, conflicts, state.LastSyncedUtc);
    }

    private async Task<bool> HasUnpublishedEditsAsync(MorningMessageSyncState state, CancellationToken cancellationToken)
    {
        foreach (var (field, value) in MorningMessageSyncFields.FromPreferences(preferenceStore.Load()))
        {
            if (state.Get(null, field) is { } known ? known.Hash != MorningMessageSyncFields.Hash(value) : !MorningMessageSyncFields.IsDefaultPreferenceValue(field, value))
            {
                return true;
            }
        }

        foreach (var (date, dateContent) in await contentStore.LoadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var (field, value) in MorningMessageSyncFields.FromContent(dateContent))
            {
                if (state.Get(date, field) is { } known ? known.Hash != MorningMessageSyncFields.Hash(value) : !MorningMessageSyncFields.IsUnsetContentValue(field, value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = await configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await configurationStore.SaveAsync(current with { MorningMessageSyncEnabled = enabled }, cancellationToken).ConfigureAwait(false);
        session.SyncEnabled = enabled;
    }

    public async Task<IReadOnlyList<MorningMessageConflict>> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state.Conflicts.Count == 0)
        {
            return [];
        }

        var content = await contentStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var preferenceValues = MorningMessageSyncFields.FromPreferences(preferenceStore.Load());

        return state.Conflicts
            .GroupBy(conflict => conflict.Date)
            .OrderBy(group => group.Key is null ? 0 : 1)
            .ThenBy(group => group.Key)
            .Select(group =>
            {
                var local = group.Key is { } date
                    ? MorningMessageSyncFields.FromContent(content.GetValueOrDefault(date) ?? MorningMessageContent.Empty)
                    : preferenceValues;
                var fields = group
                    .OrderBy(conflict => FieldOrder(conflict.Field))
                    .Select(conflict => new MorningMessageConflictField(
                        conflict.Field,
                        MorningMessageSyncFields.Label(conflict.Field),
                        local.GetValueOrDefault(conflict.Field) ?? string.Empty,
                        conflict.Other.Value))
                    .ToList();
                return new MorningMessageConflict(group.Key, fields, group.Min(conflict => conflict.DetectedUtc));
            })
            .ToList();
    }

    private static int FieldOrder(string field)
    {
        var index = MorningMessageSyncFields.PreferenceFields.Concat(MorningMessageSyncFields.ContentFields).ToList().IndexOf(field);
        return index < 0 ? int.MaxValue : index;
    }

    public async Task<MorningMessageResolveOutcome> ResolveAsync(DateOnly? operatingDate, MorningMessageConflictResolution resolution, CancellationToken cancellationToken = default)
    {
        var self = deviceIdProvider.GetOrCreateDeviceId();
        MorningMessageSyncChange change;
        using (await session.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (session.IsComposerOpen && (operatingDate is null || session.OpenDate == operatingDate))
            {
                return MorningMessageResolveOutcome.ComposerOpen;
            }

            var state = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var conflicts = state.Conflicts.Where(conflict => conflict.Date == operatingDate).ToList();
            if (conflicts.Count == 0)
            {
                return MorningMessageResolveOutcome.NoConflictFound;
            }

            var content = await contentStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var preferences = preferenceStore.Load();
            var current = operatingDate is { } date
                ? content.GetValueOrDefault(date) ?? MorningMessageContent.Empty
                : null;
            var localValues = current is not null ? MorningMessageSyncFields.FromContent(current) : MorningMessageSyncFields.FromPreferences(preferences);

            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var conflict in conflicts)
            {
                var fieldState = state.Get(operatingDate, conflict.Field);
                var counter = state.NextCounter();
                if (resolution == MorningMessageConflictResolution.KeepThisDevice)
                {
                    var local = localValues.GetValueOrDefault(conflict.Field) ?? string.Empty;
                    state.Set(operatingDate, conflict.Field, MorningMessageFieldReconciliation.KeepLocal(local, fieldState, conflict.Other, self, counter));
                }
                else
                {
                    state.Set(operatingDate, conflict.Field, MorningMessageFieldReconciliation.UseOther(fieldState, conflict.Other, self, counter));
                    changes[conflict.Field] = conflict.Other.Value;
                }

                state.SetConflict(operatingDate, conflict.Field, null, default);
            }

            try
            {
                if (changes.Count > 0 && operatingDate is { } changedDate)
                {
                    await contentStore.SaveManyAsync(
                        new Dictionary<DateOnly, MorningMessageContent> { [changedDate] = MorningMessageSyncFields.ApplyToContent(current!, changes) },
                        cancellationToken).ConfigureAwait(false);
                }
                else if (changes.Count > 0)
                {
                    await preferenceStore.SaveAsync(MorningMessageSyncFields.ApplyToPreferences(preferences, changes), cancellationToken).ConfigureAwait(false);
                }

                await stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Morning Message sync: a conflict choice could not be saved on this computer ({ExceptionType}); nothing changed.", ex.GetType().Name);
                return MorningMessageResolveOutcome.Failed;
            }

            change = new MorningMessageSyncChange(
                changes.Count > 0 && operatingDate is { } updated ? [updated] : [],
                changes.Count > 0 && operatingDate is null,
                [],
                false);
        }

        session.PublishChange(change);
        session.RequestSync();
        return MorningMessageResolveOutcome.Resolved;
    }
}
