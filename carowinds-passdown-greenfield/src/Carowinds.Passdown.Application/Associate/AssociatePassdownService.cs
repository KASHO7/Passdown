using System.Text.Json;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
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
    ISyncChangeTracker? syncChangeTracker = null) : IAssociatePassdownService
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
        await draftRepository.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        // Phase 16C §7: SQLite save first, sync metadata second, never in the same transaction — a
        // failure here must never roll back (or be rolled back by) the save that already succeeded.
        if (syncChangeTracker is not null)
        {
            await syncChangeTracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(operatingDate), cancellationToken).ConfigureAwait(false);
        }

        return new AssociateSaveStatus(AssociateSaveState.Saved, now);
    }

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
        var draft = await LoadDraftPayloadAsync(operatingDate, cancellationToken).ConfigureAwait(false);

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
        await SaveDraftAsync(operatingDate, updated, cancellationToken).ConfigureAwait(false);

        return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phase 11D Part H/AH: removes a selected note's frozen copy from <paramref name="operatingDate"/>'s
    /// draft. Safe/idempotent when the note was never selected (Part AV #8) — no tombstone, matching
    /// how the rest of the draft is a plain overwrite of current state.
    /// </summary>
    public async Task<AssociateWorkspace> DeselectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken cancellationToken = default)
    {
        var draft = await LoadDraftPayloadAsync(operatingDate, cancellationToken).ConfigureAwait(false);

        var updated = draft with
        {
            SelectedOperationalNotes = draft.SelectedOperationalNotes
                .Where(selection => selection.SourceOperationalNoteId != operationalNoteId)
                .ToList(),
        };

        await SaveDraftAsync(operatingDate, updated, cancellationToken).ConfigureAwait(false);
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
        var draft = await LoadDraftPayloadAsync(operatingDate, cancellationToken).ConfigureAwait(false);

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
        await SaveDraftAsync(operatingDate, updated, cancellationToken).ConfigureAwait(false);

        return await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssociateDraftPayload> LoadDraftPayloadAsync(DateOnly operatingDate, CancellationToken cancellationToken)
    {
        var draftRecord = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        return draftRecord is null ? AssociateDraftPayload.CreateDefault() : AssociateDraftPayloadSerializer.Deserialize(draftRecord.PayloadJson);
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

        foreach (var category in CatalogCategories)
        {
            var records = await catalogProductRepository.GetByCategoryAsync(category, lifecycleState: null, cancellationToken).ConfigureAwait(false);
            products.AddRange(ComposeCatalogProducts(category, records, draft));
        }

        MergeExactDatedBringAFriend(products, datedByCategory.GetValueOrDefault(SourceCategory.DatedBringAFriend), operatingDate, asOf);

        products.AddRange(ComposeCustomProducts(draft));

        var categoryStatuses = StatusTrackedDatedCategories
            .Select(category => ComposeCategoryStatus(category, datedByCategory.GetValueOrDefault(category), asOf))
            .ToList();

        var selectedNoteSourceStates = await ComposeSelectedOperationalNoteSourceStatesAsync(operatingDate, draft.SelectedOperationalNotes, cancellationToken).ConfigureAwait(false);

        return new AssociateWorkspace(operatingDate, draft, parkHours, products, categoryStatuses, saveStatus, selectedNoteSourceStates);
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

    private static IEnumerable<AssociateProduct> ComposeCatalogProducts(SourceCategory category, IReadOnlyList<CatalogProductRecord> records, AssociateDraftPayload draft)
    {
        foreach (var record in records)
        {
            if (record.LifecycleState == ProductLifecycleState.Archived)
            {
                continue; // spec §7.3: archived products don't print/show by default (recoverable only for diagnostics/history).
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
                PerPersonPriceAmount: bundle?.PerPersonPriceAmount);
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
