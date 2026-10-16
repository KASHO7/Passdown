using System.Text.Json;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// <see cref="IAssociatePassdownService"/> built on Phase 2's draft/dated/catalog repositories and
/// Phase 3's <see cref="IRefreshOrchestrator"/>. The one rule every method here is written around:
/// a date load composes from whatever is already cached, and only an explicit online check ever
/// calls the orchestrator (spec §6.3) — see <see cref="LoadAssociateDateAsync"/> vs.
/// <see cref="CheckAssociateDateOnlineAsync"/>.
///
/// Phase 11D adds <see cref="IOperationalNotesService"/> as this service's one bridge to the
/// persistent Operational Notes table — the only place in the codebase allowed to copy a
/// persistent note's content into an Associate draft, and only in response to an explicit
/// <see cref="SelectOperationalNoteAsync"/> call (never automatically). The Desktop
/// <c>AssociateViewModel</c> deliberately still has no <see cref="IOperationalNotesService"/>
/// dependency of its own — every persistent-note operation it needs goes through this service.
/// </summary>
public sealed class AssociatePassdownService(
    IAssociateDraftRepository draftRepository,
    IDatedResultRepository datedResultRepository,
    ICatalogProductRepository catalogProductRepository,
    IRefreshOrchestrator refreshOrchestrator,
    IOperationalNotesService operationalNotesService,
    IClock clock,
    Func<IScheduledRefreshLock>? refreshLockFactory = null,
    IDeepRefreshStatusRepository? deepRefreshStatusRepository = null,
    ISyncChangeTracker? syncChangeTracker = null,
    IEffectiveErtResolver? effectiveErtResolver = null,
    IUnitOfWorkFactory? unitOfWorkFactory = null,
    OpenEditorConflictRecorder? openEditorConflictRecorder = null,
    IOfficialRefreshGate? refreshGate = null,
    IDataChangeNotifier? dataChangeNotifier = null,
    ISeasonPriceHistoryRepository? seasonPriceHistoryRepository = null) : IAssociatePassdownService
{
    private static readonly SourceCategory[] CatalogCategories =
    [
        SourceCategory.SeasonPasses,
        SourceCategory.AddOns,
        SourceCategory.Promotions,
        SourceCategory.MilitaryFirstResponder,
    ];

    /// <summary>Dated categories a Phase 4B screen needs a category-level status for (spec Part E) — every dated category except <see cref="SourceCategory.ParkHours"/>, which already has its own dedicated <see cref="AssociateParkHoursView"/>.</summary>
    private static readonly SourceCategory[] StatusTrackedDatedCategories =
    [
        SourceCategory.DatedAdmissionTickets,
        SourceCategory.DatedFastLane,
        SourceCategory.HauntedAttractions,
        SourceCategory.BringAFriendDuo,
        SourceCategory.DatedBringAFriend,
    ];

    /// <summary>
    /// The official display name shared by both ordinary Bring-A-Friend surfaces — the Promotions
    /// catalog card's own title and the <c>BAFWizardDailyTickets</c> dated package's own name
    /// (Phase 15A) — used to identify the one catalog row <see cref="MergeExactDatedBringAFriend"/>
    /// refines, never a fabricated or guessed string.
    /// </summary>
    private const string OrdinaryBringAFriendDisplayName = "Bring-A-Friend Ticket";

    public Task<AssociateWorkspace> LoadAssociateDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        ComposeAsync(operatingDate, cancellationToken);

    public async Task<AssociateOnlineCheckResult> CheckAssociateDateOnlineAsync(DateOnly operatingDate, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Phase 13B Part K/L: the sole "explicit user action" entry point onto RefreshMode.UserForced
        // (Dashboard's own Check Online reaches this exact method too, via
        // IOperationsDashboardService.CheckOnlineAsync — one guard covers both screens, never two).
        // A null factory (every pre-13B test/caller) means no overlap protection at all, matching
        // ScheduledRefreshHost's own optional-lock-factory default — this never changes behavior for
        // a caller that doesn't supply one.
        if (refreshGate is not null)
        {
            return await RunGatedForcedRefreshAsync(refreshGate, operatingDate, progress, cancellationToken).ConfigureAwait(false);
        }

        if (refreshLockFactory is null)
        {
            return await RunForcedRefreshAsync(operatingDate, progress, cancellationToken).ConfigureAwait(false);
        }

        var refreshLock = refreshLockFactory();
        if (!refreshLock.TryAcquire())
        {
            // Never a second concurrent orchestrator run, never a raw lock exception surfaced to the
            // caller (Part L) — just report "already in progress" and hand back whatever is current
            // locally, since a scheduled/startup-catch-up refresh already holding the lock may itself
            // be writing fresher data right now.
            var currentWorkspace = await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
            return new AssociateOnlineCheckResult(currentWorkspace, RefreshSummary.AlreadyInProgress(clock.UtcNow));
        }

        try
        {
            return await RunForcedRefreshAsync(operatingDate, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshLock.Dispose();
        }
    }

    /// <summary>
    /// Phase 19A: Check Online through the app-wide <see cref="IOfficialRefreshGate"/> (which also holds
    /// the cross-process lock). An automatic check already running inside the app is waited for rather
    /// than reported as "already in progress" — the explicit request then still runs in full, bypassing
    /// every automatic freshness/cooldown rule. Only a different process (the scheduled task) holding the
    /// lock still yields <see cref="RefreshSummary.AlreadyInProgress"/>.
    /// </summary>
    private async Task<AssociateOnlineCheckResult> RunGatedForcedRefreshAsync(IOfficialRefreshGate gate, DateOnly operatingDate, IProgress<RefreshProgress>? progress, CancellationToken cancellationToken)
    {
        var kind = deepRefreshStatusRepository is not null && await NeedsDeepRefreshAsync(cancellationToken).ConfigureAwait(false)
            ? OfficialRefreshKind.DeepRefresh
            : OfficialRefreshKind.DateCheck;

        using var entry = await gate.TryEnterAsync(new OfficialRefreshActivity(kind, operatingDate), waitForInProcessWork: true, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            var currentWorkspace = await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
            return new AssociateOnlineCheckResult(currentWorkspace, RefreshSummary.AlreadyInProgress(clock.UtcNow));
        }

        return await RunForcedRefreshAsync(operatingDate, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssociateOnlineCheckResult> RunForcedRefreshAsync(DateOnly operatingDate, IProgress<RefreshProgress>? progress, CancellationToken cancellationToken)
    {
        // Phase 13D Part M/N: when future Park Hours coverage is insufficient (never run before, or
        // fallen behind the target horizon — computed purely from local persisted state, never "has
        // this user ever clicked Check Online"), this same click also bootstraps that coverage and
        // force-refreshes every supported commercial source, rather than leaving the associate to
        // discover thin Calendar coverage some other time. Once coverage is healthy, Check Online
        // stays the normal, fast, single-date UserForced refresh (Part N) — never an unnecessary
        // whole-month repopulation. A null repository (every pre-13D caller/test) preserves the exact
        // prior behavior unconditionally.
        var summary = deepRefreshStatusRepository is not null && await NeedsDeepRefreshAsync(cancellationToken).ConfigureAwait(false)
            ? await refreshOrchestrator.RunDeepRefreshAsync(bypassFreshness: true, operatingDate, progress, cancellationToken).ConfigureAwait(false)
            : await refreshOrchestrator.RunUserForcedRefreshAsync(operatingDate, progress, cancellationToken).ConfigureAwait(false);

        // Recomposed unconditionally, whether the refresh above succeeded or failed: on failure,
        // the orchestrator's own registrations already left every prior last-known-good value
        // untouched (spec §6.7), so this simply re-reads whatever is now the current cached state
        // either way — no separate success/failure branch is needed here.
        var workspace = await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        return new AssociateOnlineCheckResult(workspace, summary);
    }

    private async Task<bool> NeedsDeepRefreshAsync(CancellationToken cancellationToken)
    {
        var status = await deepRefreshStatusRepository!.GetAsync(cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);
        return DeepRefreshCoverage.NeedsDeepRefresh(status, today);
    }

    public async Task<AssociateSaveStatus> SaveDraftAsync(DateOnly operatingDate, AssociateDraftPayload draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var now = clock.UtcNow;
        var record = new AssociateDraft(operatingDate, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(draft), now);
        await draftRepository.SaveAsync(record, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Phase 16C §7: SQLite save first, sync metadata second, never in the same transaction — a
        // failure here must never roll back (or be rolled back by) the save that already succeeded.
        if (syncChangeTracker is not null)
        {
            await syncChangeTracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(operatingDate), cancellationToken).ConfigureAwait(false);
        }

        dataChangeNotifier?.Publish(new DataChange(DataChangeKind.AssociateDraft, operatingDate));
        return new AssociateSaveStatus(AssociateSaveState.Saved, now);
    }

    public async Task<AssociateDraftSaveResult> SaveEditorDraftAsync(DateOnly operatingDate, AssociateDraftPayload draft, string? expectedContentHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var payloadJson = JsonSerializer.Serialize(draft);
        var newHash = ComputeStoredDraftHash(payloadJson);

        if (unitOfWorkFactory is null || openEditorConflictRecorder is null)
        {
            // Composition without persistence-transaction support (unit tests of unrelated behavior).
            var legacyStatus = await SaveDraftAsync(operatingDate, draft, cancellationToken).ConfigureAwait(false);
            return new AssociateDraftSaveResult(legacyStatus, newHash, ConflictDetected: false);
        }

        var now = clock.UtcNow;
        var record = new AssociateDraft(operatingDate, AssociateDraftPayload.CurrentSchemaVersion, payloadJson, now);
        var conflictDetected = false;

        // BEGIN IMMEDIATE: holding the write lock across read-compare-write is what makes this atomic
        // with respect to a concurrent Personal Sync import (which takes the same lock for its own
        // read-decide-write) — neither can slip a write between the other's read and write.
        await using (var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false))
        {
            var stored = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
            var storedHash = stored is null ? null : ComputeStoredDraftHash(stored.PayloadJson);

            await draftRepository.SaveAsync(record, unitOfWork, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(storedHash, expectedContentHash, StringComparison.Ordinal)
                && !string.Equals(storedHash, newHash, StringComparison.Ordinal))
            {
                var storedPayloadJson = stored is null
                    ? null
                    : JsonSerializer.Serialize(AssociateDraftPayloadSerializer.Deserialize(stored.PayloadJson));
                await openEditorConflictRecorder.RecordAsync(
                    SyncEntityType.AssociateDraft,
                    SyncEntityIdentity.ForDraft(operatingDate),
                    new DivergedStoredVersion(storedPayloadJson, AssociateDraftPayload.CurrentSchemaVersion, storedHash, stored?.UpdatedAt ?? now),
                    unitOfWork,
                    cancellationToken).ConfigureAwait(false);
                conflictDetected = true;
            }

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Phase 16C §7: SQLite save first, sync metadata second — on the conflict path too.
        if (syncChangeTracker is not null)
        {
            await syncChangeTracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(operatingDate), cancellationToken).ConfigureAwait(false);
        }

        // Phase 19A: committed — let local projections (the Dashboard) re-read it. Never a network trigger.
        dataChangeNotifier?.Publish(new DataChange(DataChangeKind.AssociateDraft, operatingDate));
        return new AssociateDraftSaveResult(new AssociateSaveStatus(AssociateSaveState.Saved, now), newHash, conflictDetected);
    }

    /// <summary>Phase 18B REL-004: the same canonical hash Personal Sync uses, over the payload as this service reads it back (legacy fields migrated), so two spellings of the same draft never look different.</summary>
    private static string ComputeStoredDraftHash(string payloadJson) =>
        SyncContentHash.ComputeDraftHash(AssociateDraftPayloadSerializer.Deserialize(payloadJson));

    /// <summary>
    /// Phase 11D Part E/F: persistent notes relevant to <paramref name="operatingDate"/> — the
    /// Associate-selected date, deliberately never Dashboard's Eastern "today" — in the same
    /// relevance/ordering already proven by <see cref="IOperationalNotesService.ListRelevantForDateAsync"/>.
    /// These are suggestions only; nothing here selects one automatically (Part S carried forward
    /// from Phase 11A/11C).
    /// </summary>
    public Task<IReadOnlyList<OperationalNote>> GetRelevantOperationalNoteSuggestionsAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        operationalNotesService.ListRelevantForDateAsync(operatingDate, cancellationToken);

    /// <summary>
    /// Phase 11D Part B/O: freezes a copy of <paramref name="operationalNoteId"/>'s current display
    /// content into <paramref name="operatingDate"/>'s draft. Re-reads the saved draft directly
    /// (not any in-memory caller state) so this is safe to call after the caller has already flushed
    /// its own pending edits. Idempotent while already selected (Part H); throws when the note is
    /// unknown or not currently relevant for this date (Part O) — the UI only ever offers ids drawn
    /// from <see cref="GetRelevantOperationalNoteSuggestionsAsync"/>, so this is a defensive check
    /// against a stale/invalid id, not a normal user-facing validation path.
    /// </summary>
    public async Task<AssociateWorkspace> SelectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken cancellationToken = default)
    {
        var (draft, baseHash) = await LoadDraftPayloadAsync(operatingDate, cancellationToken).ConfigureAwait(false);

        if (draft.SelectedOperationalNotes.Any(selection => selection.SourceOperationalNoteId == operationalNoteId))
        {
            return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        }

        var relevant = await operationalNotesService.ListRelevantForDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var source = relevant.FirstOrDefault(note => note.Id == operationalNoteId)
            ?? throw new InvalidOperationException($"Operational note {operationalNoteId} is not available to select for {operatingDate:yyyy-MM-dd}.");

        var selection = new AssociateOperationalNoteSelection(
            source.Id, source.Title, source.Body, source.Category, source.Priority,
            source.EffectiveFrom, source.EffectiveThrough, clock.UtcNow);

        var updated = draft with { SelectedOperationalNotes = [.. draft.SelectedOperationalNotes, selection] };
        await SaveEditorDraftAsync(operatingDate, updated, baseHash, cancellationToken).ConfigureAwait(false);

        return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phase 11D Part H/AH: removes a selected note's frozen copy from <paramref name="operatingDate"/>'s
    /// draft. Safe/idempotent when the note was never selected (Part AV #8) — no tombstone, matching
    /// how the rest of the draft is a plain overwrite of current state.
    /// </summary>
    public async Task<AssociateWorkspace> DeselectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken cancellationToken = default)
    {
        var (draft, baseHash) = await LoadDraftPayloadAsync(operatingDate, cancellationToken).ConfigureAwait(false);

        var updated = draft with
        {
            SelectedOperationalNotes = draft.SelectedOperationalNotes
                .Where(selection => selection.SourceOperationalNoteId != operationalNoteId)
                .ToList(),
        };

        await SaveEditorDraftAsync(operatingDate, updated, baseHash, cancellationToken).ConfigureAwait(false);
        return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phase 11E Part I/O: the one deliberate, explicit way an already-selected note's frozen copy can
    /// ever change — never a silent resync (Part P). Re-reads the source note's *current* content
    /// (via <see cref="IOperationalNotesService.ListAsync"/>, not <see cref="GetRelevantOperationalNoteSuggestionsAsync"/>,
    /// since a supervisor may deliberately want to pull in a Resolved or no-longer-date-applicable
    /// source's current text too — Parts G/I) and overwrites the selection **in place** at its existing
    /// list index, so selection order is preserved rather than moving the note to the end (Part R). A
    /// no-op, returning the unchanged recomposed workspace, when the note is not currently selected.
    /// Throws <see cref="InvalidOperationException"/> only when the source note cannot be found at all
    /// (Part L) — nothing here mutates the source note itself.
    /// </summary>
    public async Task<AssociateWorkspace> ReplaceOperationalNoteWithCurrentAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken cancellationToken = default)
    {
        var (draft, baseHash) = await LoadDraftPayloadAsync(operatingDate, cancellationToken).ConfigureAwait(false);

        var selections = draft.SelectedOperationalNotes.ToList();
        var index = selections.FindIndex(selection => selection.SourceOperationalNoteId == operationalNoteId);
        if (index < 0)
        {
            return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        }

        var allNotes = await operationalNotesService.ListAsync(cancellationToken).ConfigureAwait(false);
        var source = allNotes.FirstOrDefault(note => note.Id == operationalNoteId)
            ?? throw new InvalidOperationException($"Operational note {operationalNoteId} is no longer available to replace.");

        selections[index] = new AssociateOperationalNoteSelection(
            source.Id, source.Title, source.Body, source.Category, source.Priority,
            source.EffectiveFrom, source.EffectiveThrough, clock.UtcNow);

        var updated = draft with { SelectedOperationalNotes = selections };
        await SaveEditorDraftAsync(operatingDate, updated, baseHash, cancellationToken).ConfigureAwait(false);

        return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Phase 18B REL-004: also returns the stored version's hash so the read-modify-write callers save conditionally on exactly what they read.</summary>
    private async Task<(AssociateDraftPayload Draft, string? ContentHash)> LoadDraftPayloadAsync(DateOnly operatingDate, CancellationToken cancellationToken)
    {
        var draftRecord = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        return draftRecord is null
            ? (AssociateDraftPayload.CreateDefault(), null)
            : (AssociateDraftPayloadSerializer.Deserialize(draftRecord.PayloadJson), ComputeStoredDraftHash(draftRecord.PayloadJson));
    }

    private async Task<AssociateWorkspace> ComposeAsync(DateOnly operatingDate, CancellationToken cancellationToken)
    {
        var draftRecord = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var draft = draftRecord is null ? AssociateDraftPayload.CreateDefault() : AssociateDraftPayloadSerializer.Deserialize(draftRecord.PayloadJson);
        var saveStatus = draftRecord is null ? AssociateSaveStatus.NeverSaved : new AssociateSaveStatus(AssociateSaveState.Saved, draftRecord.UpdatedAt);

        var asOf = clock.UtcNow;

        var datedResults = await datedResultRepository.GetByRequestedDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var datedByCategory = datedResults.ToDictionary(r => r.Key.Category);

        var parkHours = AssociateParkHoursComposer.Compose(datedByCategory.GetValueOrDefault(SourceCategory.ParkHours), draft, asOf);

        var products = new List<AssociateProduct>();
        products.AddRange(ComposeDatedProducts(
            SourceCategory.DatedAdmissionTickets,
            datedByCategory.GetValueOrDefault(SourceCategory.DatedAdmissionTickets),
            draft, asOf,
            (DatedAdmissionPayload payload) => payload.Products.Select(p =>
                new DatedProductFields(p.PackageId, p.DisplayName, p.ExactDatePrice, p.ComparisonPrice, p.PriceQualifier, p.ApplicableDate)).ToList()));
        products.AddRange(ComposeDatedProducts(
            SourceCategory.DatedFastLane,
            datedByCategory.GetValueOrDefault(SourceCategory.DatedFastLane),
            draft, asOf,
            (DatedFastLanePayload payload) => payload.Products.Select(p =>
                new DatedProductFields(p.PackageId, p.DisplayName, p.ExactDatePrice, p.ComparisonPrice, p.PriceQualifier, p.ApplicableDate)).ToList()));
        products.AddRange(ComposeDatedProducts(
            SourceCategory.HauntedAttractions,
            datedByCategory.GetValueOrDefault(SourceCategory.HauntedAttractions),
            draft, asOf,
            (DatedHauntedPayload payload) => payload.Products.Select(p =>
                new DatedProductFields(p.PackageId, p.DisplayName, p.ExactDatePrice, p.ComparisonPrice, p.PriceQualifier, p.ApplicableDate)).ToList()));
        products.AddRange(ComposeDatedProducts(
            SourceCategory.BringAFriendDuo,
            datedByCategory.GetValueOrDefault(SourceCategory.BringAFriendDuo),
            draft, asOf,
            (DatedBringAFriendDuoPayload payload) => payload.Products.Select(p =>
                new DatedProductFields(p.PackageId, p.DisplayName, p.PerPersonPrice, p.ComparisonPrice, p.PriceQualifier, p.ApplicableDate)).ToList()));

        var priceHistoryKeys = await LoadPriceHistoryKeysAsync(cancellationToken).ConfigureAwait(false);
        foreach (var category in CatalogCategories)
        {
            var records = await catalogProductRepository.GetByCategoryAsync(category, lifecycleState: null, cancellationToken).ConfigureAwait(false);
            products.AddRange(ComposeCatalogProducts(category, records, draft, priceHistoryKeys));
        }

        MergeExactDatedBringAFriend(products, datedByCategory.GetValueOrDefault(SourceCategory.DatedBringAFriend), operatingDate, asOf);
        SuppressGenericFallbacksSupersededByExactDated(products, operatingDate);

        products.AddRange(ComposeCustomProducts(draft));

        var categoryStatuses = StatusTrackedDatedCategories
            .Select(category => ComposeCategoryStatus(category, datedByCategory.GetValueOrDefault(category), asOf))
            .ToList();

        var selectedNoteSourceStates = await ComposeSelectedOperationalNoteSourceStatesAsync(operatingDate, draft.SelectedOperationalNotes, cancellationToken).ConfigureAwait(false);

        // Phase 17B: the one place a date override and a monthly configuration are ever loaded
        // together (IEffectiveErtResolver.ResolveAsync) — every consumer of this workspace
        // (Associate screen, Dashboard, PDF, finalized snapshot) reads the result from here rather
        // than re-resolving. A null resolver (every pre-17B test/caller that doesn't care about ERT)
        // never fabricates a configured value — it composes as NotConfigured, matching spec §6/34/35.
        var earlyRideTime = effectiveErtResolver is not null
            ? await effectiveErtResolver.ResolveAsync(operatingDate, cancellationToken).ConfigureAwait(false)
            : EffectiveErt.NotConfigured;

        return new AssociateWorkspace(operatingDate, draft, parkHours, products, categoryStatuses, saveStatus, selectedNoteSourceStates, earlyRideTime)
        {
            DraftContentHash = draftRecord is null ? null : SyncContentHash.ComputeDraftHash(draft),
        };
    }

    /// <summary>
    /// Phase 11E Part G: one <see cref="IOperationalNotesService.ListAsync"/> call per compose (never
    /// per row, and never at all when there is nothing selected) feeds <see cref="OperationalNoteSourceComparison.ComputeAll"/>
    /// — informational only, and never mutates <paramref name="selections"/> or any source note.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, OperationalNoteSourceComparisonState>> ComposeSelectedOperationalNoteSourceStatesAsync(
        DateOnly operatingDate, IReadOnlyList<AssociateOperationalNoteSelection> selections, CancellationToken cancellationToken)
    {
        if (selections.Count == 0)
        {
            return new Dictionary<Guid, OperationalNoteSourceComparisonState>();
        }

        var allNotes = await operationalNotesService.ListAsync(cancellationToken).ConfigureAwait(false);
        return OperationalNoteSourceComparison.ComputeAll(selections, allNotes, operatingDate);
    }

    /// <summary>
    /// Category-level status (spec Part E), computed the same way as an individual product's
    /// <see cref="AssociateVerificationInfo"/> but independent of whether any product row exists —
    /// this is what lets a category with zero rows (no prior successful payload) still say why,
    /// e.g. "Not available for this date" instead of rendering nothing at all.
    /// </summary>
    private static AssociateProductCategoryStatus ComposeCategoryStatus(SourceCategory category, DatedResultRecord? record, DateTimeOffset asOf)
    {
        var lastKnownGood = record?.ToLastKnownGood() ?? LastKnownGood<string>.Empty;
        var verification = AssociateVerificationInfo.From(lastKnownGood, asOf);

        // Unlike AssociateVerificationInfo.LastVerifiedAt (success-only), a category with no
        // successful payload yet should still be able to say when it was last attempted.
        var lastCheckedAt = lastKnownGood.LastAttemptEvidence?.TimeChecked ?? lastKnownGood.SuccessEvidence?.TimeChecked;
        var detail = lastKnownGood.LastAttemptEvidence?.Detail ?? lastKnownGood.SuccessEvidence?.Detail;

        return new AssociateProductCategoryStatus(category, verification.State, lastCheckedAt, detail);
    }

    private sealed record DatedProductFields(string PackageId, string DisplayName, decimal Price, decimal? ComparisonPrice, string? PriceQualifier, DateOnly ApplicableDate);

    private static IEnumerable<AssociateProduct> ComposeDatedProducts<TPayload>(
        SourceCategory category,
        DatedResultRecord? record,
        AssociateDraftPayload draft,
        DateTimeOffset asOf,
        Func<TPayload, IReadOnlyList<DatedProductFields>> selectFields)
    {
        var lastKnownGood = record?.ToLastKnownGood() ?? LastKnownGood<string>.Empty;
        if (lastKnownGood.SuccessValue is not { } json)
        {
            yield break;
        }

        var payload = JsonSerializer.Deserialize<TPayload>(json);
        if (payload is null)
        {
            yield break;
        }

        var verification = AssociateVerificationInfo.From(lastKnownGood, asOf);

        foreach (var field in selectFields(payload))
        {
            var identity = AssociateProductIdentity.ForDatedProduct(category, field.PackageId);
            var draftOverride = draft.ProductOverrides.GetValueOrDefault(identity, ProductDraftOverride.Default);

            yield return new AssociateProduct(
                identity,
                field.DisplayName,
                category,
                IsCustom: false,
                field.Price,
                OfficialDisplayPrice: null, // no source-provided display string for dated products — a future UI formats the amount itself.
                field.ComparisonPrice,
                field.PriceQualifier,
                DateSpecific: true,
                field.ApplicableDate,
                verification,
                draftOverride.IncludeOnPdf,
                draftOverride.DisplayPriceOverride,
                // Phase 13C: every dated product is an exact price for its confirmed applicable
                // date by construction — DateSpecific is already the authoritative "is this exact"
                // signal (docs/retrieval/pricing-semantics.md), so PriceKind mirrors that here
                // rather than re-deriving it from qualifier text (dated sources never populate one).
                PriceKind.ExactDated);
        }
    }

    /// <summary>
    /// Phase 20A: product keys with recorded season price history, used only to decide whether a row
    /// offers "Price history". Price history is advisory — if it cannot be read, composition carries on
    /// with the tracking policy alone instead of failing the Associate screen.
    /// </summary>
    private async Task<IReadOnlySet<string>> LoadPriceHistoryKeysAsync(CancellationToken cancellationToken)
    {
        if (seasonPriceHistoryRepository is null)
        {
            return EmptyKeySet;
        }

        try
        {
            return await seasonPriceHistoryRepository.GetTrackedProductKeysAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EmptyKeySet;
        }
    }

    private static readonly IReadOnlySet<string> EmptyKeySet = new HashSet<string>();

    private static IEnumerable<AssociateProduct> ComposeCatalogProducts(
        SourceCategory category, IReadOnlyList<CatalogProductRecord> records, AssociateDraftPayload draft, IReadOnlySet<string> priceHistoryKeys)
    {
        foreach (var record in records)
        {
            if (record.LifecycleState is ProductLifecycleState.Archived or ProductLifecycleState.Expired)
            {
                continue; // spec §7.3/Phase 17A §1: archived/expired products don't print/show by default (recoverable only for diagnostics/history).
            }

            var identity = AssociateProductIdentity.ForCatalogProduct(record.ProductKey);
            var draftOverride = draft.ProductOverrides.GetValueOrDefault(identity, ProductDraftOverride.Default);

            var priceKind = PriceSemanticsClassifier.ClassifyCatalog(record.PriceQualifier, record.ComparisonPriceAmount);
            var bundle = priceKind == PriceKind.Bundle
                ? PriceSemanticsClassifier.TryParseBundle(record.PriceQualifier, record.CurrentPriceAmount)
                : null;

            yield return new AssociateProduct(
                identity,
                record.DisplayName,
                category,
                IsCustom: false,
                record.CurrentPriceAmount,
                record.CurrentDisplayPrice,
                record.ComparisonPriceAmount,
                record.PriceQualifier,
                record.DateSpecific,
                record.ApplicableDate,
                VerificationFromCatalog(record),
                draftOverride.IncludeOnPdf,
                draftOverride.DisplayPriceOverride,
                priceKind,
                BundleTotalPriceAmount: bundle is not null ? record.CurrentPriceAmount : null,
                BundleQuantity: bundle?.Quantity,
                PerPersonPriceAmount: bundle?.PerPersonPriceAmount)
            {
                PriceHistoryProductKey = PriceHistoryTrackingPolicy.ClassifyStoredProduct(
                        category, record.LastEvidence.SourceName, record.DisplayName, record.DateSpecific, priceHistoryKeys.Contains(record.ProductKey)) is not null
                    ? record.ProductKey
                    : null,
            };
        }
    }

    /// <summary>
    /// Phase 15A: folds ordinary Bring-A-Friend's exact per-date price (<see cref="SourceCategory.DatedBringAFriend"/>,
    /// keyword <c>BAFWizardDailyTickets</c>) into the existing Promotions-category "Bring-A-Friend
    /// Ticket" catalog row, in place, rather than adding it as its own product/section — the same
    /// real-world offer must appear exactly once (docs/phase13-hardening.md Phase 15A section).
    /// Precedence: when a verified exact value exists for <paramref name="operatingDate"/>, it wins —
    /// the row's price/qualifier/<see cref="PriceKind"/> switch to <see cref="PriceKind.ExactDated"/>
    /// and "Starting From" is dropped, while <see cref="AssociateProduct.ComparisonPriceAmount"/> (the
    /// official "$80 Gate Price", already sourced from the static catalog card) is preserved
    /// unchanged — never fabricated here, never dropped. <see cref="AssociateProduct.ProductIdentity"/>
    /// stays the catalog row's own identity so a manager's existing per-product draft override
    /// survives regardless of whether today's date has an exact price. When no exact value exists (no
    /// data, a different date, or a failed check), the catalog row is left completely untouched — the
    /// existing generic "Starting From" value remains, exactly as Phase 13C behaved (spec Part 10).
    /// </summary>
    private static void MergeExactDatedBringAFriend(
        List<AssociateProduct> products,
        DatedResultRecord? record,
        DateOnly operatingDate,
        DateTimeOffset asOf)
    {
        var lastKnownGood = record?.ToLastKnownGood() ?? LastKnownGood<string>.Empty;
        if (lastKnownGood.SuccessValue is not { } json)
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<DatedBringAFriendPayload>(json);
        var exactProduct = payload?.Products.FirstOrDefault(p => p.ApplicableDate == operatingDate);
        if (exactProduct is null)
        {
            return;
        }

        var catalogIndex = products.FindIndex(p =>
            !p.IsCustom && p.Category == SourceCategory.Promotions &&
            string.Equals(p.DisplayName.Trim(), OrdinaryBringAFriendDisplayName, StringComparison.Ordinal));
        if (catalogIndex < 0)
        {
            return;
        }

        var verification = AssociateVerificationInfo.From(lastKnownGood, asOf);
        products[catalogIndex] = products[catalogIndex] with
        {
            OfficialPriceAmount = exactProduct.Price,
            OfficialDisplayPrice = null,
            PriceQualifier = null,
            DateSpecific = true,
            ApplicableDate = operatingDate,
            Verification = verification,
            PriceKind = PriceKind.ExactDated,
        };
    }

    /// <summary>
    /// Spec Phase 17A §7/§8/§9: when an exact dated price already answers a generic "Starting From"
    /// catalog card for <paramref name="operatingDate"/> — e.g. Daily Admission's dated "Park Ticket"
    /// (<see cref="SourceCategory.DatedAdmissionTickets"/>) superseding the identically-named
    /// Promotions catalog card scraped from the same official page (see
    /// docs/retrieval/dated-admission-source.md/docs/retrieval/catalog-source-inventory.md) — the
    /// generic fallback is redundant and is dropped from display entirely, never shown twice for the
    /// same real-world product. Matched by <see cref="ProductDisplayIdentity"/> (normalized display
    /// name), the same equivalence signal <see cref="MergeExactDatedBringAFriend"/> already relies on,
    /// generalized here to a removal rather than an in-place merge because an exact dated product in
    /// this position already gets its own composed row (unlike Bring-A-Friend's dated category, which
    /// never resolves to a display section of its own). Scoped to <see cref="PriceKind.StartingFrom"/>
    /// generics only, so a distinct bundle with its own name (e.g. "Ticket + Meal, Snack &amp; Drink
    /// Bundle") is never touched (spec §8) — it simply never matches any exact row's name.
    /// </summary>
    private static void SuppressGenericFallbacksSupersededByExactDated(List<AssociateProduct> products, DateOnly operatingDate)
    {
        var exactIdentitiesForDate = products
            .Where(p => p.PriceKind == PriceKind.ExactDated && p.ApplicableDate == operatingDate)
            .Select(p => ProductDisplayIdentity.FromDisplayName(p.DisplayName))
            .ToHashSet();

        if (exactIdentitiesForDate.Count == 0)
        {
            return;
        }

        products.RemoveAll(p =>
            !p.IsCustom &&
            p.PriceKind == PriceKind.StartingFrom &&
            exactIdentitiesForDate.Contains(ProductDisplayIdentity.FromDisplayName(p.DisplayName)));
    }

    /// <summary>
    /// Always <see cref="AssociateVerificationState.Verified"/>: <see cref="IProductReconciliationService.ReconcileAsync"/>
    /// requires <see cref="RetrievalStatus.Verified"/> evidence to run at all (throws otherwise), so
    /// every persisted <see cref="CatalogProductRecord.LastEvidence"/> is always from a successful
    /// catalog retrieval. Unlike dated results, catalog storage does not retain a separate
    /// last-failed-attempt snapshot per product, so there is no per-product "Stale" to derive here.
    /// </summary>
    private static AssociateVerificationInfo VerificationFromCatalog(CatalogProductRecord record) =>
        new(AssociateVerificationState.Verified, record.LastVerifiedAt, StaleAge: null, record.LastEvidence.Detail);

    private static IEnumerable<AssociateProduct> ComposeCustomProducts(AssociateDraftPayload draft) =>
        draft.CustomProducts.Select(custom => new AssociateProduct(
            $"custom|{custom.Id}",
            custom.DisplayName,
            Category: null,
            IsCustom: true,
            OfficialPriceAmount: null,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null,
            DateSpecific: false,
            ApplicableDate: null,
            Verification: AssociateVerificationInfo.NotCheckedYet, // no official source backs a custom entry.
            custom.IncludeOnPdf,
            custom.Price));
}
