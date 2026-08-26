using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Deterministic, fully scriptable <see cref="IAssociatePassdownService"/> for
/// <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel"/> tests. Never touches
/// SQLite, HTTP, or Playwright — records every call so a test can assert whether the ViewModel
/// ever invoked the explicit online check (spec §6.3's "date change never triggers retrieval").
///
/// Phase 4C adds a small in-memory per-date draft store: unless a test overrides
/// <see cref="WorkspaceFactory"/>, <see cref="LoadAssociateDateAsync"/>/<see cref="CheckAssociateDateOnlineAsync"/>
/// compose a workspace from whatever <see cref="SaveDraftAsync"/> has actually saved for that date
/// so far (or the default draft if nothing has). This lets a test exercise real save-then-reload
/// round trips (Phase 4C Part H/I) instead of only ever asserting against
/// <see cref="SaveCalls"/> in isolation.
/// </summary>
internal sealed class FakeAssociatePassdownService : IAssociatePassdownService
{
    private readonly Dictionary<DateOnly, AssociateDraftPayload> _savedDrafts = [];
    private readonly Dictionary<DateOnly, DateTimeOffset> _savedAt = [];

    public List<DateOnly> LoadCalls { get; } = [];

    public List<DateOnly> CheckOnlineCalls { get; } = [];

    /// <summary>Every <see cref="SaveDraftAsync"/> call, in order — the primary assertion point for autosave/debounce/overlap tests.</summary>
    public List<(DateOnly OperatingDate, AssociateDraftPayload Draft)> SaveCalls { get; } = [];

    /// <summary>When set, overrides the default per-date-store composition entirely (existing Phase 4B presentation tests use this to script an exact workspace).</summary>
    public Func<DateOnly, AssociateWorkspace>? WorkspaceFactory { get; set; }

    public Func<DateOnly, AssociateOnlineCheckResult>? CheckOnlineResultFactory { get; set; }

    /// <summary>
    /// When set (and <see cref="CheckOnlineResultFactory"/> is not), simulates "the online check
    /// found different official data" by substituting just the composed workspace's
    /// <see cref="AssociateWorkspace.ParkHours"/> — the draft/products still come from the normal
    /// per-date store, so a test can assert official data changed while manual fields survived, in
    /// one call.
    /// </summary>
    public Func<DateOnly, AssociateParkHoursView>? OfficialParkHoursAfterCheckFactory { get; set; }

    public IReadOnlyList<RefreshProgress> ProgressToReport { get; set; } = [];

    /// <summary>When set, <see cref="LoadAssociateDateAsync"/> awaits this before returning — lets a test hold a load "in flight" to assert busy-state behavior.</summary>
    public TaskCompletionSource<bool>? LoadGate { get; set; }

    /// <summary>When set, <see cref="CheckAssociateDateOnlineAsync"/> awaits this before returning.</summary>
    public TaskCompletionSource<bool>? CheckOnlineGate { get; set; }

    /// <summary>
    /// One gate per <see cref="SaveDraftAsync"/> call, consumed in order: the Nth save call awaits
    /// the Nth queued gate (if any) before completing. Lets a test hold one save in flight while it
    /// makes further edits, to prove overlapping saves are serialized rather than racing.
    /// </summary>
    public Queue<TaskCompletionSource<bool>> SaveGates { get; } = new();

    public async Task<AssociateWorkspace> LoadAssociateDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        LoadCalls.Add(operatingDate);
        if (LoadGate is not null)
        {
            await LoadGate.Task;
        }

        return BuildWorkspace(operatingDate);
    }

    public async Task<AssociateOnlineCheckResult> CheckAssociateDateOnlineAsync(DateOnly operatingDate, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CheckOnlineCalls.Add(operatingDate);

        foreach (var update in ProgressToReport)
        {
            progress?.Report(update);
        }

        if (CheckOnlineGate is not null)
        {
            await CheckOnlineGate.Task;
        }

        if (CheckOnlineResultFactory is { } resultFactory)
        {
            return resultFactory(operatingDate);
        }

        var workspace = BuildWorkspace(operatingDate);
        if (OfficialParkHoursAfterCheckFactory is { } parkHoursFactory)
        {
            workspace = workspace with { ParkHours = parkHoursFactory(operatingDate) };
        }

        return new AssociateOnlineCheckResult(workspace, SuccessSummary(operatingDate));
    }

    public async Task<AssociateSaveStatus> SaveDraftAsync(DateOnly operatingDate, AssociateDraftPayload draft, CancellationToken cancellationToken = default)
    {
        SaveCalls.Add((operatingDate, draft));

        if (SaveGates.Count > 0)
        {
            await SaveGates.Dequeue().Task;
        }

        _savedDrafts[operatingDate] = draft;
        var savedAt = DateTimeOffset.UtcNow;
        _savedAt[operatingDate] = savedAt;
        return new AssociateSaveStatus(AssociateSaveState.Saved, savedAt);
    }

    /// <summary>Scripted persistent-note suggestions per date (Phase 11D) — a test seeds this the way it seeds <see cref="WorkspaceFactory"/>.</summary>
    public Dictionary<DateOnly, List<OperationalNote>> RelevantOperationalNotesByDate { get; } = [];

    public List<(DateOnly OperatingDate, Guid OperationalNoteId)> SelectOperationalNoteCalls { get; } = [];

    public List<(DateOnly OperatingDate, Guid OperationalNoteId)> DeselectOperationalNoteCalls { get; } = [];

    public Task<IReadOnlyList<OperationalNote>> GetRelevantOperationalNoteSuggestionsAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OperationalNote>>(RelevantOperationalNotesByDate.GetValueOrDefault(operatingDate, []));

    /// <summary>Mirrors the real service's read-modify-write against the per-date draft store, including throwing for an id absent from <see cref="RelevantOperationalNotesByDate"/> (Part O).</summary>
    public Task<AssociateWorkspace> SelectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken cancellationToken = default)
    {
        SelectOperationalNoteCalls.Add((operatingDate, operationalNoteId));

        var draft = _savedDrafts.GetValueOrDefault(operatingDate, AssociateDraftPayload.CreateDefault());
        if (!draft.SelectedOperationalNotes.Any(selection => selection.SourceOperationalNoteId == operationalNoteId))
        {
            var source = RelevantOperationalNotesByDate.GetValueOrDefault(operatingDate, []).FirstOrDefault(note => note.Id == operationalNoteId)
                ?? throw new InvalidOperationException($"Operational note {operationalNoteId} is not available to select for {operatingDate:yyyy-MM-dd}.");

            var selection = new AssociateOperationalNoteSelection(
                source.Id, source.Title, source.Body, source.Category, source.Priority,
                source.EffectiveFrom, source.EffectiveThrough, DateTimeOffset.UtcNow);

            draft = draft with { SelectedOperationalNotes = [.. draft.SelectedOperationalNotes, selection] };
            _savedDrafts[operatingDate] = draft;
            _savedAt[operatingDate] = DateTimeOffset.UtcNow;
        }

        return Task.FromResult(BuildWorkspace(operatingDate));
    }

    public Task<AssociateWorkspace> DeselectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken cancellationToken = default)
    {
        DeselectOperationalNoteCalls.Add((operatingDate, operationalNoteId));

        var draft = _savedDrafts.GetValueOrDefault(operatingDate, AssociateDraftPayload.CreateDefault());
        draft = draft with
        {
            SelectedOperationalNotes = draft.SelectedOperationalNotes.Where(selection => selection.SourceOperationalNoteId != operationalNoteId).ToList(),
        };
        _savedDrafts[operatingDate] = draft;
        _savedAt[operatingDate] = DateTimeOffset.UtcNow;

        return Task.FromResult(BuildWorkspace(operatingDate));
    }

    private AssociateWorkspace BuildWorkspace(DateOnly date)
    {
        if (WorkspaceFactory is { } factory)
        {
            return factory(date);
        }

        var draft = _savedDrafts.GetValueOrDefault(date, AssociateDraftPayload.CreateDefault());
        var saveStatus = _savedAt.TryGetValue(date, out var savedAt)
            ? new AssociateSaveStatus(AssociateSaveState.Saved, savedAt)
            : AssociateSaveStatus.NeverSaved;

        return new AssociateWorkspace(
            date,
            draft,
            ComposeParkHoursFromDraft(draft),
            Products: [],
            CategoryStatuses: [],
            saveStatus);
    }

    /// <summary>Mirrors production <c>AssociatePassdownService.ComposeParkHours</c>'s override precedence (no official hours ever set here — this fake has no dated-source data) so a test's saved-then-reloaded draft round-trips its manual/buyout override the same way the real service does, instead of always coming back "not checked yet".</summary>
    private static AssociateParkHoursView ComposeParkHoursFromDraft(AssociateDraftPayload draft)
    {
        var effective = ParkHoursCalculator.Calculate(
            officialIsClosed: false, officialOpening: null, officialClosing: null,
            draft.ManualHoursOverride.Enabled, draft.ManualHoursOverride.OpeningTime, draft.ManualHoursOverride.ClosingTime,
            draft.PrivateBuyout.Enabled, draft.PrivateBuyout.OpeningTime, draft.PrivateBuyout.ClosingTime);

        return new AssociateParkHoursView(effective, AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null);
    }

    public static AssociateWorkspace DefaultWorkspace(DateOnly date) => new(
        date,
        AssociateDraftPayload.CreateDefault(),
        new AssociateParkHoursView(NotCheckedHours, AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null),
        Products: [],
        CategoryStatuses: [],
        AssociateSaveStatus.NeverSaved);

    public static EffectiveHours NotCheckedHours => ParkHoursCalculator.Calculate(
        officialIsClosed: false, null, null,
        manualOverrideEnabled: false, null, null,
        privateBuyoutEnabled: false, null, null);

    public static RefreshSummary SuccessSummary(DateOnly date) => new(
        RefreshJobType.ManualSingleDate, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        OperationsAttempted: 1, Succeeded: 1, Failed: 0, Cancelled: 0,
        Results: [SourceOperationResult.Success("fake-source", SourceCategory.ParkHours, date, RetrievalStatus.Verified, "Verified.")]);

    public static RefreshSummary PartialFailureSummary(DateOnly date) => new(
        RefreshJobType.ManualSingleDate, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        OperationsAttempted: 2, Succeeded: 1, Failed: 1, Cancelled: 0,
        Results:
        [
            SourceOperationResult.Success("fake-park-hours", SourceCategory.ParkHours, date, RetrievalStatus.Verified, "Verified."),
            SourceOperationResult.Failure("fake-fast-lane", SourceCategory.DatedFastLane, date, RetrievalStatus.Error, "Could not be checked."),
        ]);
}
