using System.Text.Json;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Hours;
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
    IClock clock) : IAssociatePassdownService
{
    private static readonly SourceCategory[] CatalogCategories =
    [
        SourceCategory.SeasonPasses,
        SourceCategory.AddOns,
        SourceCategory.Promotions,
    ];

    /// <summary>Dated categories a Phase 4B screen needs a category-level status for (spec Part E) — every dated category except <see cref="SourceCategory.ParkHours"/>, which already has its own dedicated <see cref="AssociateParkHoursView"/>.</summary>
    private static readonly SourceCategory[] StatusTrackedDatedCategories =
    [
        SourceCategory.DatedAdmissionTickets,
        SourceCategory.DatedFastLane,
        SourceCategory.HauntedAttractions,
    ];

    public Task<AssociateWorkspace> LoadAssociateDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        ComposeAsync(operatingDate, cancellationToken);

    public async Task<AssociateOnlineCheckResult> CheckAssociateDateOnlineAsync(DateOnly operatingDate, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var summary = await refreshOrchestrator.RunManualDateRefreshAsync(operatingDate, progress, cancellationToken).ConfigureAwait(false);

        // Recomposed unconditionally, whether the refresh above succeeded or failed: on failure,
        // RunManualDateRefreshAsync's own dated-source registrations already left every prior
        // last-known-good value untouched (spec §6.7), so this simply re-reads whatever is now the
        // current cached state either way — no separate success/failure branch is needed here.
        var workspace = await ComposeAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        return new AssociateOnlineCheckResult(workspace, summary);
    }

    public async Task<AssociateSaveStatus> SaveDraftAsync(DateOnly operatingDate, AssociateDraftPayload draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var now = clock.UtcNow;
        var record = new AssociateDraft(operatingDate, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(draft), now);
        await draftRepository.SaveAsync(record, cancellationToken).ConfigureAwait(false);

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
        return draftRecord is null ? AssociateDraftPayload.CreateDefault() : DeserializeDraft(draftRecord.PayloadJson);
    }

    private async Task<AssociateWorkspace> ComposeAsync(DateOnly operatingDate, CancellationToken cancellationToken)
    {
        var draftRecord = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var draft = draftRecord is null ? AssociateDraftPayload.CreateDefault() : DeserializeDraft(draftRecord.PayloadJson);
        var saveStatus = draftRecord is null ? AssociateSaveStatus.NeverSaved : new AssociateSaveStatus(AssociateSaveState.Saved, draftRecord.UpdatedAt);

        var asOf = clock.UtcNow;

        var datedResults = await datedResultRepository.GetByRequestedDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var datedByCategory = datedResults.ToDictionary(r => r.Key.Category);

        var parkHours = ComposeParkHours(datedByCategory.GetValueOrDefault(SourceCategory.ParkHours), draft, asOf);

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

        foreach (var category in CatalogCategories)
        {
            var records = await catalogProductRepository.GetByCategoryAsync(category, lifecycleState: null, cancellationToken).ConfigureAwait(false);
            products.AddRange(ComposeCatalogProducts(category, records, draft));
        }

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
    /// Deserializes a persisted draft, migrating older payload shapes forward. Schema-v1 predates
    /// both the Daily Fun -> Daily Feature rename and <see cref="PassdownSectionSelection"/>, so its
    /// JSON has neither "DailyFeatureType"/"DailyFeatureText" (only the old "DailyFunType"/
    /// "DailyFunText" names) nor a "SectionSelection" object at all — both come back null from the
    /// plain deserialize below, which this fills in from the legacy property names (the enum's
    /// underlying integer values are unchanged by the rename, so no value translation is needed,
    /// just reading it under its old key) or the all-included default. Phase 8A's own persisted
    /// "SectionSelection" object predates <see cref="PassdownSectionSelection.IncludeHauntedAttractions"/>
    /// (added Phase 8B), so that one property comes back <see langword="false"/> — the JSON
    /// deserializer's own missing-bool default, not an associate's real choice to hide it — from an
    /// otherwise-present "SectionSelection" object; <see cref="HasIncludeHauntedAttractionsProperty"/>
    /// distinguishes that case from an explicit "false" so it can be corrected to included, matching
    /// how every other Phase 8A section already defaults to included when missing entirely. This is
    /// presence-based rather than gated on the record's own <c>SchemaVersion</c> field, matching how
    /// this method already ignored that field before Phase 8A — it only ever needs to backfill
    /// whatever the plain deserialize left null/default, regardless of what schema_version says.
    /// </summary>
    private static AssociateDraftPayload DeserializeDraft(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<AssociateDraftPayload>(payloadJson) ?? AssociateDraftPayload.CreateDefault();

        if (payload.DailyFeatureType is null && payload.DailyFeatureText is null)
        {
            var (legacyType, legacyText) = ReadLegacyDailyFun(payloadJson);
            payload = payload with { DailyFeatureType = legacyType, DailyFeatureText = legacyText };
        }

        var sectionSelection = payload.SectionSelection ?? PassdownSectionSelection.AllIncluded;
        if (!HasIncludeHauntedAttractionsProperty(payloadJson))
        {
            sectionSelection = sectionSelection with { IncludeHauntedAttractions = true };
        }

        // Phase 11D Part AO: a pre-11D payload has no "SelectedOperationalNotes" property at all, so
        // the plain deserialize above leaves it null — a legacy draft must load with zero selections,
        // never a fabricated one, matching every other optional-field migration in this method.
        var selectedOperationalNotes = payload.SelectedOperationalNotes ?? [];

        return payload with { SectionSelection = sectionSelection, SelectedOperationalNotes = selectedOperationalNotes };
    }

    private static bool HasIncludeHauntedAttractionsProperty(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.TryGetProperty("SectionSelection", out var section)
            && section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty("IncludeHauntedAttractions", out _);
    }

    private static (DailyFeatureType? Type, string? Text) ReadLegacyDailyFun(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        var type = root.TryGetProperty("DailyFunType", out var typeElement) && typeElement.ValueKind != JsonValueKind.Null
            ? typeElement.Deserialize<DailyFeatureType?>()
            : null;

        var text = root.TryGetProperty("DailyFunText", out var textElement) && textElement.ValueKind != JsonValueKind.Null
            ? textElement.GetString()
            : null;

        return (type, text);
    }

    private static AssociateParkHoursView ComposeParkHours(DatedResultRecord? record, AssociateDraftPayload draft, DateTimeOffset asOf)
    {
        var lastKnownGood = record?.ToLastKnownGood() ?? LastKnownGood<string>.Empty;
        var verification = AssociateVerificationInfo.From(lastKnownGood, asOf);

        // Uses the raw last-successful evidence's own status, not the (possibly Stale-relabeled)
        // `verification.State` — a "Closed" result that later became stale (a subsequent check
        // failed outright) must still compute as officially closed, just flagged Stale via
        // `verification` for presentation, per spec §6.7.
        var officialIsClosed = lastKnownGood.SuccessEvidence?.Status == RetrievalStatus.Closed;
        var officialPayload = lastKnownGood.SuccessValue is { } json ? JsonSerializer.Deserialize<ParkHoursPayload>(json) : null;

        var effective = ParkHoursCalculator.Calculate(
            officialIsClosed,
            officialPayload?.OpeningTime,
            officialPayload?.ClosingTime,
            draft.ManualHoursOverride.Enabled,
            draft.ManualHoursOverride.OpeningTime,
            draft.ManualHoursOverride.ClosingTime,
            draft.PrivateBuyout.Enabled,
            draft.PrivateBuyout.OpeningTime,
            draft.PrivateBuyout.ClosingTime);

        return new AssociateParkHoursView(effective, verification, officialIsClosed, officialPayload?.OpeningTime, officialPayload?.ClosingTime);
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
                draftOverride.DisplayPriceOverride);
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
                draftOverride.DisplayPriceOverride);
        }
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
