using System.Globalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// Phase 20A: turns one successful official observation of a tracked season product into a
/// price-history write — called only by <see cref="ProductReconciliationService"/>, inside the same
/// unit of work as that product's catalog upsert, so the current price and its history can never
/// diverge. Never called for a failed retrieval, a missing product, an explicitly unavailable/expired
/// product, a manual draft edit, PDF/clipboard output or a cached reread.
///
/// Logs only meaningful events (baseline, price change, semantics change, reappearance) with the
/// product key and old/new amounts and kinds; a reconfirmation of the same state is silent.
/// </summary>
public sealed class SeasonPriceHistoryRecorder(
    ISeasonPriceHistoryRepository repository,
    ILogger<SeasonPriceHistoryRecorder>? logger = null)
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>
    /// Records <paramref name="observed"/> (already known to be tracked by
    /// <see cref="PriceHistoryTrackingPolicy"/>). Returns <see langword="null"/> without writing when the
    /// observation carries no numeric price — a missing amount is never a price state.
    /// </summary>
    /// <param name="resumedAfterRetirement">The product was retired (archived/expired) before this observation.</param>
    public async Task<SeasonPriceObservationOutcome?> RecordAsync(
        ObservedProduct observed,
        string sourceName,
        DateTimeOffset observedAt,
        bool resumedAfterRetirement,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (observed.CurrentPriceAmount is not { } amount)
        {
            return null;
        }

        var keyText = observed.Key.ToString();
        var state = new SeasonPriceState(amount, PriceSemanticsClassifier.ClassifyCatalog(observed.PriceQualifier, observed.ComparisonPriceAmount));
        var latest = await repository.GetLatestAsync(keyText, unitOfWork, cancellationToken).ConfigureAwait(false);
        var outcome = SeasonPriceTimeline.Classify(latest?.State, state, resumedAfterRetirement);

        if (!SeasonPriceTimeline.StartsNewInterval(outcome))
        {
            if (observedAt > latest!.LastConfirmedAt)
            {
                await repository.ConfirmAsync(keyText, latest.Sequence, observedAt, unitOfWork, cancellationToken).ConfigureAwait(false);
            }

            return outcome;
        }

        var interval = new SeasonPriceHistoryInterval(
            ProductKey: keyText,
            Sequence: (latest?.Sequence ?? 0) + 1,
            Amount: state.Amount,
            PriceKind: state.Kind,
            DisplayPrice: observed.CurrentDisplayPrice,
            PriceQualifier: observed.PriceQualifier,
            DisplayName: observed.DisplayName,
            SourceName: sourceName,
            FirstObservedAt: observedAt,
            LastConfirmedAt: observedAt,
            Origin: SeasonPriceHistoryOrigin.Observed);
        await repository.InsertAsync(interval, unitOfWork, cancellationToken).ConfigureAwait(false);

        Log(outcome, keyText, latest?.State, state);
        return outcome;
    }

    private void Log(SeasonPriceObservationOutcome outcome, string productKey, SeasonPriceState? previous, SeasonPriceState current)
    {
        switch (outcome)
        {
            case SeasonPriceObservationOutcome.Baseline:
                _logger.LogInformation(
                    "Price history baseline created: {ProductKey} {Price} ({PriceKind}).",
                    productKey, Format(current.Amount), current.Kind);
                break;
            case SeasonPriceObservationOutcome.PriceChanged:
                _logger.LogInformation(
                    "Price changed: {ProductKey} {OldPrice} -> {NewPrice} ({PriceKind}).",
                    productKey, Format(previous!.Amount), Format(current.Amount), current.Kind);
                break;
            case SeasonPriceObservationOutcome.SemanticsChanged:
            case SeasonPriceObservationOutcome.PriceAndSemanticsChanged:
                _logger.LogInformation(
                    "Price semantics changed: {ProductKey} {OldPrice} ({OldPriceKind}) -> {NewPrice} ({NewPriceKind}).",
                    productKey, Format(previous!.Amount), previous.Kind, Format(current.Amount), current.Kind);
                break;
            case SeasonPriceObservationOutcome.Reappeared:
                _logger.LogInformation(
                    "Price history resumed after the product was published again: {ProductKey} {Price} ({PriceKind}).",
                    productKey, Format(current.Amount), current.Kind);
                break;
        }
    }

    private static string Format(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
