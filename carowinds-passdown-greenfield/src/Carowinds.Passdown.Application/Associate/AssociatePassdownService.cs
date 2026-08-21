using System.Text.Json;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Freshness;
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
/// </summary>
public sealed class AssociatePassdownService(
    IAssociateDraftRepository draftRepository,
    IDatedResultRepository datedResultRepository,
    ICatalogProductRepository catalogProductRepository,
    IRefreshOrchestrator refreshOrchestrator,
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

        return new AssociateWorkspace(operatingDate, draft, parkHours, products, categoryStatuses, saveStatus);
    }

    private static AssociateDraftPayload DeserializeDraft(string payloadJson) =>
        JsonSerializer.Deserialize<AssociateDraftPayload>(payloadJson) ?? AssociateDraftPayload.CreateDefault();

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

        return new AssociateParkHoursView(effective, verification);
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
