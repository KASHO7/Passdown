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
///
/// Phase 20A: when a <see cref="SeasonPriceHistoryRecorder"/> is supplied, every observed product that
/// <see cref="PriceHistoryTrackingPolicy"/> tracks also gets its price-history observation written in
/// the <b>same</b> unit of work as its catalog upsert — both commit or neither does. Missing,
/// explicitly unavailable and expired products never touch price history (lifecycle is not a price).
/// </summary>
public sealed class ProductReconciliationService(
    ICatalogProductRepository catalogRepository,
    IProductHistoryRepository historyRepository,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<ProductReconciliationService>? logger = null,
    SeasonPriceHistoryRecorder? priceHistoryRecorder = null) : IProductReconciliationService
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public async Task<ReconciliationResult> ReconcileAsync(
        SourceCategory category,
        SourceEvidence evidence,
        IReadOnlyList<ObservedProduct> observedProducts,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<CatalogProductKey>? unconfirmedProductKeys = null)
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
        var legacyAliases = LegacyPassIdentityAliases(
            category,
            observedProducts.Select(p => p.Key).Concat(unconfirmedProductKeys ?? []),
            existingByKey);
        observedProducts = ApplyLegacyPassIdentities(observedProducts, legacyAliases);
        observedProducts = SeparatePassYears(category, observedProducts, existingByKey);
        var observedKeys = observedProducts.Select(p => p.Key.ToString()).ToHashSet();
        var unconfirmedKeys = (unconfirmedProductKeys ?? [])
            .Select(key => legacyAliases.GetValueOrDefault(key.ToString(), key.ToString()))
            .ToHashSet();

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
                !unconfirmedKeys.Contains(record.ProductKey) &&
                record.LastEvidence.SourceName == evidence.SourceName &&
                record.LifecycleState is ProductLifecycleState.Active or ProductLifecycleState.PendingConfirmation);

        foreach (var record in missing)
        {
            results.Add(await ReconcileMissingAsync(record, evidence, cancellationToken).ConfigureAwait(false));
        }

        LogTransitions(category, evidence, observedProducts.Count, results);
        return new ReconciliationResult(category, results);
    }

    /// <summary>
    /// Phase 25 legacy compatibility: before the official redesign, a Season Pass was stored under the
    /// old page's per-card CMS id ("2027 Gold Pass" → <c>SeasonPasses:id:550e3db6-…</c>). The redesigned
    /// page no longer publishes that id, so the same real product now arrives under its deterministic
    /// pass-offer key (<see cref="PassOffer"/>). Only when the observation is a Season Pass
    /// <b>new-purchase</b> price with a stated season year, no record exists under its own new key yet,
    /// and exactly one stored legacy (non pass-offer) Season Passes record names the same tier and the
    /// same year (<see cref="PassOffer.FromLegacyName"/>, active records preferred), is the observation
    /// filed under that legacy key instead — so its catalog row, draft overrides and price history
    /// continue as one product rather than forking into a duplicate. Memberships, renewal offers and
    /// initiation fees never adopt a legacy identity (the old page never sold them), nothing stored is
    /// rewritten, and an ambiguous match adopts nothing. Returns observed-key → legacy-key text.
    /// </summary>
    private IReadOnlyDictionary<string, string> LegacyPassIdentityAliases(
        SourceCategory category,
        IEnumerable<CatalogProductKey> keys,
        IReadOnlyDictionary<string, CatalogProductRecord> existingByKey)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (category != SourceCategory.SeasonPasses)
        {
            return aliases;
        }

        var legacyByOffer = existingByKey.Values
            .Where(record => PassOffer.FromKey(record.ProductKey) is null)
            .Select(record => (Record: record, Offer: PassOffer.FromLegacyName(record.DisplayName)))
            .Where(entry => entry.Offer is { SeasonYear: not null })
            .GroupBy(entry => entry.Offer!, entry => entry.Record)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var key in keys)
        {
            var keyText = key.ToString();
            if (aliases.ContainsKey(keyText) ||
                existingByKey.ContainsKey(keyText) ||
                PassOffer.FromKey(keyText) is not { Product: PassProductType.SeasonPass, PriceType: PassPriceType.NewPurchase, SeasonYear: not null } offer ||
                !legacyByOffer.TryGetValue(offer, out var candidates))
            {
                continue;
            }

            var active = candidates
                .Where(record => record.LifecycleState is ProductLifecycleState.Active or ProductLifecycleState.PendingConfirmation)
                .ToList();
            var chosen = active.Count == 1 ? active[0] : active.Count == 0 && candidates.Count == 1 ? candidates[0] : null;
            if (chosen is null)
            {
                _logger.LogWarning(
                    "Several stored Season Pass records match {PassOffer}; none was adopted as its legacy identity ({Candidates}).",
                    offer.IdentityText, string.Join(", ", candidates.Select(record => record.ProductKey)));
                continue;
            }

            if (aliases.ContainsValue(chosen.ProductKey))
            {
                continue;
            }

            _logger.LogInformation(
                "Season Pass {PassOffer} continues under its existing identity {ProductKey} ('{StoredName}').",
                offer.IdentityText, chosen.ProductKey, chosen.DisplayName);
            aliases[keyText] = chosen.ProductKey;
        }

        return aliases;
    }

    private static IReadOnlyList<ObservedProduct> ApplyLegacyPassIdentities(
        IReadOnlyList<ObservedProduct> observedProducts, IReadOnlyDictionary<string, string> aliases) =>
        aliases.Count == 0
            ? observedProducts
            : observedProducts
                .Select(product => aliases.TryGetValue(product.Key.ToString(), out var legacyKey)
                    ? product with { Key = CatalogProductKey.Parse(legacyKey) }
                    : product)
                .ToList();

    /// <summary>
    /// Phase 21A pass-year identity: a pass's validity year is part of what the product is — a 2026
    /// Gold Pass and a 2027 Gold Pass are different products with separate price timelines even if
    /// the official site were to reuse one CMS id (or name/url) for both. Identity precedence (official
    /// id &gt; url &gt; source+name) is unchanged; only when the stored product under the observed key
    /// states a different pass year than the observation (<see cref="PassValidityYear.StatesDifferentYear"/>)
    /// is the observation filed under <see cref="CatalogProductKey.WithPassYear"/> instead. The stored
    /// product keeps its key, history and price timeline and retires through the normal two-absence
    /// rule; nothing is rewritten. A name without a year never splits anything, and the year always
    /// comes from the official name, never the observation date. Limited to the pass-family categories
    /// Season Price History tracks.
    /// </summary>
    private IReadOnlyList<ObservedProduct> SeparatePassYears(
        SourceCategory category,
        IReadOnlyList<ObservedProduct> observedProducts,
        IReadOnlyDictionary<string, CatalogProductRecord> existingByKey)
    {
        if (!PriceHistoryTrackingPolicy.CandidateCategories.Contains(category))
        {
            return observedProducts;
        }

        List<ObservedProduct>? remapped = null;
        for (var i = 0; i < observedProducts.Count; i++)
        {
            var observed = observedProducts[i];
            if (!existingByKey.TryGetValue(observed.Key.ToString(), out var existing))
            {
                continue;
            }

            if (PassValidityYear.StatesDifferentYear(existing.DisplayName, observed.DisplayName))
            {
                var yearKey = observed.Key.WithPassYear(PassValidityYear.FromDisplayName(observed.DisplayName)!.Value);
                _logger.LogInformation(
                    "Pass validity year differs under one official identity: {ProductKey} is '{StoredName}', observed '{ObservedName}'; tracking the observed pass separately as {PassYearKey}.",
                    existing.ProductKey, existing.DisplayName, observed.DisplayName, yearKey.ToString());
                remapped ??= [.. observedProducts];
                remapped[i] = observed with { Key = yearKey };
            }
            else if (PassValidityYear.FromDisplayName(existing.DisplayName) is not null &&
                     PassValidityYear.FromDisplayName(observed.DisplayName) is null)
            {
                _logger.LogInformation(
                    "Pass year not stated by the official name for {ProductKey} ('{ObservedName}', previously '{StoredName}'); keeping the existing identity.",
                    existing.ProductKey, observed.DisplayName, existing.DisplayName);
            }
        }

        return remapped ?? observedProducts;
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
            var newPriceObservation = outcome == ReconciliationOutcome.Added
                ? PriceObservationFor(observed, evidence, resumedAfterRetirement: false)
                : null;
            var newPriceOutcome = await PersistAsync(newUpsert, reason, newPriceObservation, cancellationToken).ConfigureAwait(false);
            return new ReconciledProduct(
                keyText, outcome, HistoryAppended: true, observed.DisplayName, SeasonTagsOf(observed.Key.Category, observed.DisplayName))
            {
                PriceHistoryOutcome = newPriceOutcome,
            };
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
            await PersistAsync(expiredUpsert, alreadyExpiredForTheSameReason ? null : expiredLifecycle.ArchivedReason, priceObservation: null, cancellationToken)
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
            await PersistAsync(archivedUpsert, alreadyArchivedForTheSameReason ? null : archivedLifecycle.ArchivedReason, priceObservation: null, cancellationToken)
                .ConfigureAwait(false);
            return new ReconciledProduct(
                keyText, ReconciliationOutcome.Archived, HistoryAppended: !alreadyArchivedForTheSameReason, observed.DisplayName, seasonTags, previousSeasonTags);
        }

        var confirmedLifecycle = existingLifecycle.ConfirmPresent(evidence);
        var changeReason = DescribeChange(existing, observed);
        var confirmedUpsert = BuildUpsert(confirmedLifecycle, observed);
        // Phase 20A: only a retired product (archived/expired) resuming opens a new price interval on an
        // unchanged price; a single missed check (PendingConfirmation) never interrupts the interval.
        var wasRetired = existing.LifecycleState is ProductLifecycleState.Archived or ProductLifecycleState.Expired;
        var priceOutcome = await PersistAsync(
                confirmedUpsert, changeReason, PriceObservationFor(observed, evidence, wasRetired), cancellationToken)
            .ConfigureAwait(false);

        var confirmedOutcome = existing.LifecycleState != ProductLifecycleState.Active
            ? ReconciliationOutcome.Reactivated
            : ReconciliationOutcome.Confirmed;
        return new ReconciledProduct(
            keyText, confirmedOutcome, HistoryAppended: changeReason is not null, observed.DisplayName, seasonTags, previousSeasonTags)
        {
            PriceHistoryOutcome = priceOutcome,
        };
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

        await PersistAsync(upsert, reason, priceObservation: null, cancellationToken).ConfigureAwait(false);
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
    ///
    /// Phase 20A: a non-null <paramref name="priceObservation"/> (a tracked product that was actually
    /// observed) always uses a unit of work, so the price-history read-decide-write happens under the
    /// same write transaction as the catalog upsert. A failure anywhere rolls back all three writes,
    /// leaving last-known-good state untouched.
    /// </summary>
    private async Task<SeasonPriceObservationOutcome?> PersistAsync(
        CatalogProductUpsert upsert, string? changeReason, PriceObservation? priceObservation, CancellationToken cancellationToken)
    {
        if (changeReason is null && priceObservation is null)
        {
            await catalogRepository.UpsertAsync(upsert, cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);

        await catalogRepository.UpsertAsync(upsert, unitOfWork, cancellationToken).ConfigureAwait(false);
        if (changeReason is not null)
        {
            await historyRepository.AppendAsync(BuildHistoryEntry(upsert, changeReason), unitOfWork, cancellationToken).ConfigureAwait(false);
        }

        SeasonPriceObservationOutcome? priceOutcome = null;
        if (priceObservation is not null)
        {
            priceOutcome = await priceHistoryRecorder!.RecordAsync(
                    priceObservation.Observed,
                    priceObservation.SourceName,
                    priceObservation.ObservedAt,
                    priceObservation.ResumedAfterRetirement,
                    unitOfWork,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return priceOutcome;
    }

    /// <summary>
    /// The price-history observation for an actually observed product, or <see langword="null"/> when
    /// there is no recorder or <see cref="PriceHistoryTrackingPolicy"/> does not track it.
    /// </summary>
    private PriceObservation? PriceObservationFor(ObservedProduct observed, SourceEvidence evidence, bool resumedAfterRetirement)
    {
        if (priceHistoryRecorder is null ||
            !PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(observed.Key.Category, evidence.SourceName, observed.DisplayName, observed.DateSpecific))
        {
            return null;
        }

        return new PriceObservation(observed, evidence.SourceName, evidence.TimeChecked, resumedAfterRetirement);
    }

    private sealed record PriceObservation(ObservedProduct Observed, string SourceName, DateTimeOffset ObservedAt, bool ResumedAfterRetirement);

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
