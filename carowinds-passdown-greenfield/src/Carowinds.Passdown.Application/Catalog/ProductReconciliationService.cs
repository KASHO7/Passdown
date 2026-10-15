using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// <see cref="IProductReconciliationService"/> built on <see cref="ICatalogProductRepository"/>
/// and <see cref="IProductHistoryRepository"/>. All add/update/absence/archive
/// decisions are made here by driving <see cref="CatalogProductLifecycle"/>'s
/// existing transition rules (spec §7.3) — this class never re-implements them;
/// repositories only persist whatever state results.
///
/// Phase 19B: logs each lifecycle transition (discovered, candidate absence, retired, reactivated,
/// expired, season tags changed) and a successful check that returned zero products — never an
/// unchanged product, a raw payload, or anything user-authored.
/// </summary>
public sealed class ProductReconciliationService(
    ICatalogProductRepository catalogRepository,
    IProductHistoryRepository historyRepository,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<ProductReconciliationService>? logger = null) : IProductReconciliationService
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public async Task<ReconciliationResult> ReconcileAsync(
        SourceCategory category,
        SourceEvidence evidence,
        IReadOnlyList<ObservedProduct> observedProducts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(observedProducts);
        if (evidence.Status != RetrievalStatus.Verified)
        {
            throw new ArgumentException(
                "Reconciliation requires a verified retrieval — a failed or stale " +
                "result must retain last-known-good state instead (spec §6.7/§7.1).",
                nameof(evidence));
        }

        var existingByKey = (await catalogRepository.GetByCategoryAsync(category, lifecycleState: null, cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(record => record.ProductKey);
        var observedKeys = observedProducts.Select(p => p.Key.ToString()).ToHashSet();

        var results = new List<ReconciledProduct>();

        foreach (var observed in observedProducts)
        {
            existingByKey.TryGetValue(observed.Key.ToString(), out var existing);
            results.Add(await ReconcileObservedAsync(observed, existing, evidence, cancellationToken).ConfigureAwait(false));
        }

        // Scoped to this call's own source (via evidence.SourceName), not every source sharing
        // this category: SourceCategory.AddOns is deliberately shared by two independent sources
        // (season-pass add-ons and general add-ons, spec §6.1's shared cadence), so without this
        // scope every reconciliation of one would wrongly push the other's disjoint products
        // toward absence/archival.
        var missing = existingByKey.Values
            .Where(record =>
                !observedKeys.Contains(record.ProductKey) &&
                record.LastEvidence.SourceName == evidence.SourceName &&
                record.LifecycleState is ProductLifecycleState.Active or ProductLifecycleState.PendingConfirmation);

        foreach (var record in missing)
        {
            results.Add(await ReconcileMissingAsync(record, evidence, cancellationToken).ConfigureAwait(false));
        }

        LogTransitions(category, evidence, observedProducts.Count, results);
        return new ReconciliationResult(category, results);
    }

    private void LogTransitions(SourceCategory category, SourceEvidence evidence, int observedCount, IReadOnlyList<ReconciledProduct> results)
    {
        if (observedCount == 0)
        {
            _logger.LogInformation(
                "Catalog source {SourceName} ({Category}) checked successfully with zero products.", evidence.SourceName, category);
        }

        foreach (var result in results)
        {
            switch (result.Outcome)
            {
                case ReconciliationOutcome.Added:
                    _logger.LogInformation(
                        "Catalog product discovered: {ProductKey} '{DisplayName}' (season tags: {SeasonTags}).",
                        result.ProductKey, result.DisplayName, result.SeasonTags);
                    break;
                case ReconciliationOutcome.PendingRemoval:
                    _logger.LogInformation(
                        "Catalog product missing from a successful {SourceName} check (candidate absence, {Threshold} needed to retire): {ProductKey} '{DisplayName}'.",
                        evidence.SourceName, CatalogProductLifecycle.ConfirmedAbsenceThreshold, result.ProductKey, result.DisplayName);
                    break;
                case ReconciliationOutcome.Archived when result.HistoryAppended: // already archived for the same reason → silent
                    _logger.LogInformation(
                        "Catalog product now inactive (retired from the current catalog): {ProductKey} '{DisplayName}'.",
                        result.ProductKey, result.DisplayName);
                    break;
                case ReconciliationOutcome.Expired when result.HistoryAppended: // already expired for the same reason → silent
                    _logger.LogInformation(
                        "Catalog product expired (valid-through date passed): {ProductKey} '{DisplayName}'.",
                        result.ProductKey, result.DisplayName);
                    break;
                case ReconciliationOutcome.Reactivated:
                    _logger.LogInformation(
                        "Catalog product reactivated (published again): {ProductKey} '{DisplayName}'.",
                        result.ProductKey, result.DisplayName);
                    break;
            }

            if (result.SeasonTagsChanged)
            {
                _logger.LogInformation(
                    "Catalog product season tags changed from {PreviousSeasonTags} to {SeasonTags}: {ProductKey}.",
                    result.PreviousSeasonTags, result.SeasonTags, result.ProductKey);
            }
        }
    }

    private static ProductSeasonTags SeasonTagsOf(SourceCategory category, string displayName) =>
        ProductSeasonClassifier.Classify(category, displayName);

    /// <summary>Handles one retrieved product, whether it's brand new or already known.</summary>
    private async Task<ReconciledProduct> ReconcileObservedAsync(
        ObservedProduct observed, CatalogProductRecord? existing, SourceEvidence evidence, CancellationToken cancellationToken)
    {
        var keyText = observed.Key.ToString();
        var expiredValidThrough = ExpiredValidThroughDate(observed, evidence);

        if (existing is null)
        {
            var lifecycle = CatalogProductLifecycle.FirstObserved(observed.Key, evidence);
            var outcome = ReconciliationOutcome.Added;
            var reason = "New product observed.";

            if (expiredValidThrough is { } newValidThrough)
            {
                lifecycle = lifecycle.MarkExpired(evidence, newValidThrough);
                outcome = ReconciliationOutcome.Expired;
                reason = lifecycle.ArchivedReason!;
            }
            else if (observed.IsExplicitlyUnavailable)
            {
                lifecycle = lifecycle.MarkExplicitlyUnavailable(evidence, observed.UnavailableReason ?? DefaultUnavailableReason);
                outcome = ReconciliationOutcome.Archived;
                reason = lifecycle.ArchivedReason!;
            }

            var newUpsert = BuildUpsert(lifecycle, observed);
            await PersistAsync(newUpsert, reason, cancellationToken).ConfigureAwait(false);
            return new ReconciledProduct(
                keyText, outcome, HistoryAppended: true, observed.DisplayName, SeasonTagsOf(observed.Key.Category, observed.DisplayName));
        }

        var existingLifecycle = Rehydrate(existing);
        var seasonTags = SeasonTagsOf(observed.Key.Category, observed.DisplayName);
        var previousSeasonTags = SeasonTagsOf(existing.Category, existing.DisplayName);

        if (expiredValidThrough is { } validThrough)
        {
            var expiredLifecycle = existingLifecycle.MarkExpired(evidence, validThrough);
            var alreadyExpiredForTheSameReason =
                existing.LifecycleState == ProductLifecycleState.Expired &&
                existing.ArchivedReason == expiredLifecycle.ArchivedReason;

            var expiredUpsert = BuildUpsert(expiredLifecycle, observed);
            await PersistAsync(expiredUpsert, alreadyExpiredForTheSameReason ? null : expiredLifecycle.ArchivedReason, cancellationToken)
                .ConfigureAwait(false);
            return new ReconciledProduct(
                keyText, ReconciliationOutcome.Expired, HistoryAppended: !alreadyExpiredForTheSameReason, observed.DisplayName, seasonTags, previousSeasonTags);
        }

        if (observed.IsExplicitlyUnavailable)
        {
            var archivedLifecycle = existingLifecycle.MarkExplicitlyUnavailable(
                evidence, observed.UnavailableReason ?? DefaultUnavailableReason);
            var alreadyArchivedForTheSameReason =
                existing.LifecycleState == ProductLifecycleState.Archived &&
                existing.ArchivedReason == archivedLifecycle.ArchivedReason;

            var archivedUpsert = BuildUpsert(archivedLifecycle, observed);
            await PersistAsync(archivedUpsert, alreadyArchivedForTheSameReason ? null : archivedLifecycle.ArchivedReason, cancellationToken)
                .ConfigureAwait(false);
            return new ReconciledProduct(
                keyText, ReconciliationOutcome.Archived, HistoryAppended: !alreadyArchivedForTheSameReason, observed.DisplayName, seasonTags, previousSeasonTags);
        }

        var confirmedLifecycle = existingLifecycle.ConfirmPresent(evidence);
        var changeReason = DescribeChange(existing, observed);
        var confirmedUpsert = BuildUpsert(confirmedLifecycle, observed);
        await PersistAsync(confirmedUpsert, changeReason, cancellationToken).ConfigureAwait(false);

        var confirmedOutcome = existing.LifecycleState != ProductLifecycleState.Active
            ? ReconciliationOutcome.Reactivated
            : ReconciliationOutcome.Confirmed;
        return new ReconciledProduct(
            keyText, confirmedOutcome, HistoryAppended: changeReason is not null, observed.DisplayName, seasonTags, previousSeasonTags);
    }

    /// <summary>Handles one previously known product that this retrieval did not return at all.</summary>
    private async Task<ReconciledProduct> ReconcileMissingAsync(
        CatalogProductRecord existing, SourceEvidence evidence, CancellationToken cancellationToken)
    {
        var key = CatalogProductKey.Parse(existing.ProductKey);
        var lifecycle = Rehydrate(existing).ConfirmAbsent(evidence);

        var upsert = new CatalogProductUpsert(
            Key: key,
            DisplayName: existing.DisplayName,
            LifecycleState: lifecycle.State,
            FirstSeenAt: lifecycle.FirstSeenAt,
            LastSeenAt: lifecycle.LastSeenAt,
            LastVerifiedAt: lifecycle.LastVerifiedAt,
            ConsecutiveConfirmedAbsenceCount: lifecycle.ConsecutiveConfirmedAbsenceCount,
            ArchivedAt: lifecycle.ArchivedAt,
            ArchivedReason: lifecycle.ArchivedReason,
            CurrentPriceAmount: existing.CurrentPriceAmount,
            CurrentDisplayPrice: existing.CurrentDisplayPrice,
            ComparisonPriceAmount: existing.ComparisonPriceAmount,
            PriceQualifier: existing.PriceQualifier,
            DateSpecific: existing.DateSpecific,
            ApplicableDate: existing.ApplicableDate,
            LastEvidence: evidence);

        var reason = lifecycle.State == ProductLifecycleState.Archived
            ? lifecycle.ArchivedReason!
            : "Missing from a successful retrieval; pending confirmation.";
        var outcome = lifecycle.State == ProductLifecycleState.Archived
            ? ReconciliationOutcome.Archived
            : ReconciliationOutcome.PendingRemoval;

        await PersistAsync(upsert, reason, cancellationToken).ConfigureAwait(false);
        var tags = SeasonTagsOf(existing.Category, existing.DisplayName);
        return new ReconciledProduct(existing.ProductKey, outcome, HistoryAppended: true, existing.DisplayName, tags, tags);
    }

    /// <summary>
    /// Upserts the catalog row and, when <paramref name="changeReason"/> is not
    /// null, appends a matching history snapshot in the same transaction (spec
    /// §7.3) — the two must reflect the same moment, never a partially-applied
    /// pair. A null reason means nothing meaningful changed, so only the catalog
    /// row's freshness (last-seen/last-verified/evidence) is updated and no
    /// history row is written (spec §7.3 "avoid duplicate history rows").
    /// </summary>
    private async Task PersistAsync(CatalogProductUpsert upsert, string? changeReason, CancellationToken cancellationToken)
    {
        if (changeReason is null)
        {
            await catalogRepository.UpsertAsync(upsert, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);

        await catalogRepository.UpsertAsync(upsert, unitOfWork, cancellationToken).ConfigureAwait(false);
        await historyRepository.AppendAsync(BuildHistoryEntry(upsert, changeReason), unitOfWork, cancellationToken).ConfigureAwait(false);

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec Phase 17A §1/§2: an authoritative valid-through date is a lifecycle fact, never evidence
    /// of a retrieval problem — it is checked independently of, and prior to,
    /// <see cref="ObservedProduct.IsExplicitlyUnavailable"/> so a future adapter that populates both
    /// signals for the same product still resolves to one clear reason. Returns the passed
    /// valid-through date only once the current Eastern date (from <paramref name="evidence"/>'s own
    /// check time — never a second clock read) is strictly after it; <see langword="null"/> when no
    /// date was supplied or it has not passed yet, in which case the caller's normal
    /// present/explicitly-unavailable handling proceeds unchanged.
    /// </summary>
    private static DateOnly? ExpiredValidThroughDate(ObservedProduct observed, SourceEvidence evidence)
    {
        if (observed.ValidThroughDate is not { } validThrough)
        {
            return null;
        }

        var currentEasternDate = DateOnly.FromDateTime(evidence.TimeChecked.DateTime);
        return currentEasternDate > validThrough ? validThrough : null;
    }

    private static CatalogProductLifecycle Rehydrate(CatalogProductRecord record) =>
        CatalogProductLifecycle.Rehydrate(
            CatalogProductKey.Parse(record.ProductKey),
            record.LifecycleState,
            record.FirstSeenAt,
            record.LastSeenAt,
            record.LastVerifiedAt,
            record.ConsecutiveConfirmedAbsenceCount,
            record.ArchivedAt,
            record.ArchivedReason,
            record.LastEvidence);

    private static CatalogProductUpsert BuildUpsert(CatalogProductLifecycle lifecycle, ObservedProduct observed) => new(
        Key: lifecycle.Key,
        DisplayName: observed.DisplayName,
        LifecycleState: lifecycle.State,
        FirstSeenAt: lifecycle.FirstSeenAt,
        LastSeenAt: lifecycle.LastSeenAt,
        LastVerifiedAt: lifecycle.LastVerifiedAt,
        ConsecutiveConfirmedAbsenceCount: lifecycle.ConsecutiveConfirmedAbsenceCount,
        ArchivedAt: lifecycle.ArchivedAt,
        ArchivedReason: lifecycle.ArchivedReason,
        CurrentPriceAmount: observed.CurrentPriceAmount,
        CurrentDisplayPrice: observed.CurrentDisplayPrice,
        ComparisonPriceAmount: observed.ComparisonPriceAmount,
        PriceQualifier: observed.PriceQualifier,
        DateSpecific: observed.DateSpecific,
        ApplicableDate: observed.ApplicableDate,
        LastEvidence: lifecycle.LastEvidence);

    private static ProductHistoryEntry BuildHistoryEntry(CatalogProductUpsert upsert, string changeReason) => new(
        ProductKey: upsert.Key.ToString(),
        RecordedAt: upsert.LastEvidence.TimeChecked,
        DisplayName: upsert.DisplayName,
        LifecycleState: upsert.LifecycleState,
        CurrentPriceAmount: upsert.CurrentPriceAmount,
        CurrentDisplayPrice: upsert.CurrentDisplayPrice,
        ComparisonPriceAmount: upsert.ComparisonPriceAmount,
        PriceQualifier: upsert.PriceQualifier,
        ChangeReason: changeReason);

    /// <summary>Null when nothing about the product meaningfully changed, so callers can skip an unnecessary history row.</summary>
    private static string? DescribeChange(CatalogProductRecord existing, ObservedProduct observed)
    {
        var reasons = new List<string>();

        if (existing.LifecycleState != ProductLifecycleState.Active)
        {
            reasons.Add("Reactivated after returning to the official catalog.");
        }

        if (!string.Equals(existing.DisplayName, observed.DisplayName, StringComparison.Ordinal))
        {
            reasons.Add($"Display name changed from '{existing.DisplayName}' to '{observed.DisplayName}'.");
        }

        if (existing.CurrentPriceAmount != observed.CurrentPriceAmount ||
            !string.Equals(existing.CurrentDisplayPrice, observed.CurrentDisplayPrice, StringComparison.Ordinal))
        {
            reasons.Add($"Price changed from '{existing.CurrentDisplayPrice ?? "(none)"}' to '{observed.CurrentDisplayPrice ?? "(none)"}'.");
        }

        if (existing.ComparisonPriceAmount != observed.ComparisonPriceAmount)
        {
            reasons.Add("Comparison price changed.");
        }

        if (!string.Equals(existing.PriceQualifier, observed.PriceQualifier, StringComparison.Ordinal))
        {
            reasons.Add("Price qualifier changed.");
        }

        return reasons.Count == 0 ? null : string.Join(" ", reasons);
    }

    private const string DefaultUnavailableReason = "Source explicitly marked this product expired/unavailable.";
}
