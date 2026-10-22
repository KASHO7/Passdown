using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Phase 26: one verified official price observation as it travels between computers — a single
/// <c>season_price_history</c> interval recorded by one computer (its <em>contributor</em>), with
/// every original field preserved. It is evidence that the contributor's official checks showed this
/// exact price from <see cref="FirstObservedUtc"/> through <see cref="LastConfirmedUtc"/>; it never
/// claims that the receiving computer checked the website at those times.
/// <para>
/// Every field except <see cref="LastConfirmedUtc"/> is immutable and covered by
/// <see cref="ObservationId"/> (<see cref="OfficialPriceObservationIdentity"/>).
/// <see cref="LastConfirmedUtc"/> only ever moves forward (the contributor confirmed the same price
/// again), and merges by taking the maximum — so merging is commutative, associative and idempotent.
/// <see cref="Currency"/> and <see cref="BillingUnit"/> are derived values carried for validation
/// only; they are not part of the identity.
/// </para>
/// </summary>
public sealed record OfficialPriceObservation(
    string ObservationId,
    Guid ContributorDeviceId,
    int ContributorSequence,
    string ProductKey,
    decimal Amount,
    PriceKind PriceKind,
    string? DisplayPrice,
    string? PriceQualifier,
    string DisplayName,
    string SourceName,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastConfirmedUtc,
    SeasonPriceHistoryOrigin Origin,
    string Currency,
    PassPriceUnit BillingUnit)
{
    public const string UsDollars = "USD";

    /// <summary>True when every immutable (identity-covered) field matches — <see cref="LastConfirmedUtc"/> is ignored.</summary>
    public bool HasSameImmutableContent(OfficialPriceObservation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ObservationId == other.ObservationId
            && ContributorDeviceId == other.ContributorDeviceId
            && ContributorSequence == other.ContributorSequence
            && ProductKey == other.ProductKey
            && Amount == other.Amount
            && PriceKind == other.PriceKind
            && DisplayPrice == other.DisplayPrice
            && PriceQualifier == other.PriceQualifier
            && DisplayName == other.DisplayName
            && SourceName == other.SourceName
            && FirstObservedUtc.UtcTicks == other.FirstObservedUtc.UtcTicks
            && Origin == other.Origin;
    }

    /// <summary>
    /// The observation for one interval this computer recorded itself. <paramref name="family"/> is the
    /// product's tracked family (it decides the billing unit). Returns <see langword="null"/> when the
    /// interval is not eligible to leave this computer (<see cref="OfficialPriceObservationValidator.IsEligibleLocalInterval"/>).
    /// </summary>
    public static OfficialPriceObservation? FromLocalInterval(SeasonPriceHistoryInterval interval, Guid contributorDeviceId)
    {
        ArgumentNullException.ThrowIfNull(interval);

        if (!OfficialPriceObservationValidator.IsEligibleLocalInterval(interval, out var family))
        {
            return null;
        }

        var amount = decimal.Round(interval.Amount, 2);
        var id = OfficialPriceObservationIdentity.Compute(
            contributorDeviceId, interval.Sequence, interval.ProductKey, amount, interval.PriceKind, interval.DisplayPrice,
            interval.PriceQualifier, interval.DisplayName, interval.SourceName, interval.FirstObservedAt, interval.Origin);

        return new OfficialPriceObservation(
            id, contributorDeviceId, interval.Sequence, interval.ProductKey, amount, interval.PriceKind, interval.DisplayPrice,
            interval.PriceQualifier, interval.DisplayName, interval.SourceName, interval.FirstObservedAt.ToUniversalTime(),
            interval.LastConfirmedAt.ToUniversalTime(), interval.Origin, UsDollars, PriceHistoryTrackingPolicy.UnitOf(family));
    }
}

/// <summary>
/// Phase 26: the deterministic, collision-resistant identity of an <see cref="OfficialPriceObservation"/>
/// — lowercase hex SHA-256 of a canonical JSON array of every immutable field:
/// <code>
/// ["carowinds-official-price-observation/v1", contributorDeviceId("D"), contributorSequence,
///  productKey, amount("0.00", invariant), priceKind, displayPrice|null, priceQualifier|null,
///  displayName, sourceName, firstObservedUtcTicks, origin]
/// </code>
/// It is never the price alone, the display name alone or the calendar date alone. The contributor
/// and its local interval sequence keep two computers' independent observations of the same price
/// separate pieces of evidence (they still display as one row when they overlap — see
/// <see cref="PriceHistoryTimelineMerge"/>); the content fields make a recomputation detect any
/// record whose content does not match the identity it claims. <see cref="OfficialPriceObservation.LastConfirmedUtc"/>
/// is deliberately excluded: it is the one field allowed to move forward.
/// </summary>
public static class OfficialPriceObservationIdentity
{
    private const string Scheme = "carowinds-official-price-observation/v1";

    public static string Compute(
        Guid contributorDeviceId,
        int contributorSequence,
        string productKey,
        decimal amount,
        PriceKind priceKind,
        string? displayPrice,
        string? priceQualifier,
        string displayName,
        string sourceName,
        DateTimeOffset firstObservedUtc,
        SeasonPriceHistoryOrigin origin)
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            Scheme,
            contributorDeviceId.ToString("D", CultureInfo.InvariantCulture),
            contributorSequence,
            productKey,
            FormatAmount(amount),
            priceKind.ToString(),
            displayPrice,
            priceQualifier,
            displayName,
            sourceName,
            firstObservedUtc.UtcTicks,
            origin.ToString(),
        });

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string Compute(OfficialPriceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Compute(
            observation.ContributorDeviceId, observation.ContributorSequence, observation.ProductKey, observation.Amount,
            observation.PriceKind, observation.DisplayPrice, observation.PriceQualifier, observation.DisplayName,
            observation.SourceName, observation.FirstObservedUtc, observation.Origin);
    }

    /// <summary>The canonical amount text (two decimals, invariant culture) used by the identity and by storage.</summary>
    public static string FormatAmount(decimal amount) => decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Why an incoming <see cref="OfficialPriceObservation"/> was not accepted (diagnostics only).</summary>
public enum OfficialPriceObservationRejection
{
    None,
    MissingField,
    InvalidIdentityFormat,
    IdentityMismatch,
    InvalidContributor,
    InvalidProduct,
    UntrackedProduct,
    InvalidAmount,
    InvalidCurrency,
    InvalidPriceKind,
    InvalidBillingUnit,
    InvalidText,
    InvalidTimestamp,
    InvalidOrigin,
}

/// <summary>
/// Phase 26: the eligibility and validation rules for official price observations. Only trustworthy,
/// persisted, source-backed observations of a tracked season product are ever synchronized: a real
/// positive USD amount, a tracked product family, a source, and sane original timestamps. Manual
/// draft prices, custom products, unavailable options (which never have a price state), dated
/// admission/ticket prices (never tracked as history) and zero-dollar placeholders are never eligible.
/// Incoming text is treated purely as data — it is only ever displayed, never interpreted.
/// </summary>
public static class OfficialPriceObservationValidator
{
    public const int MaxProductKeyLength = 512;
    public const int MaxDisplayNameLength = 300;
    public const int MaxSourceNameLength = 200;
    public const int MaxPriceTextLength = 100;

    /// <summary>Generous ceiling for any Carowinds season product price; anything larger is not a real observation.</summary>
    public const decimal MaxAmount = 10_000m;

    /// <summary>Earliest plausible observation time (this app did not exist before then).</summary>
    public static readonly DateTimeOffset EarliestObservation = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Clock-skew allowance between computers before a confirmation time counts as "in the future".</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(2);

    /// <summary>True when a locally recorded interval may be exported; <paramref name="family"/> is its tracked family.</summary>
    public static bool IsEligibleLocalInterval(SeasonPriceHistoryInterval interval, out PriceHistoryFamily family)
    {
        ArgumentNullException.ThrowIfNull(interval);
        family = default;

        if (interval.Sequence < 1 || !IsValidAmount(interval.Amount) || !IsSyncablePriceKind(interval.PriceKind)
            || interval.LastConfirmedAt < interval.FirstObservedAt
            || !IsValidText(interval.DisplayName, MaxDisplayNameLength, required: true)
            || !IsValidText(interval.SourceName, MaxSourceNameLength, required: true)
            || !IsValidText(interval.DisplayPrice, MaxPriceTextLength, required: false)
            || !IsValidText(interval.PriceQualifier, MaxPriceTextLength, required: false))
        {
            return false;
        }

        return TryClassify(interval.ProductKey, interval.SourceName, interval.DisplayName, out family);
    }

    /// <summary>Validates one incoming observation in full, including that its content matches the identity it claims.</summary>
    public static OfficialPriceObservationRejection Validate(OfficialPriceObservation? observation, DateTimeOffset nowUtc)
    {
        if (observation is null || observation.ObservationId is null || observation.ProductKey is null
            || observation.DisplayName is null || observation.SourceName is null || observation.Currency is null)
        {
            return OfficialPriceObservationRejection.MissingField;
        }

        if (!IsLowerHexSha256(observation.ObservationId))
        {
            return OfficialPriceObservationRejection.InvalidIdentityFormat;
        }

        if (observation.ContributorDeviceId == Guid.Empty || observation.ContributorSequence < 1)
        {
            return OfficialPriceObservationRejection.InvalidContributor;
        }

        if (observation.ProductKey.Length > MaxProductKeyLength || HasControlCharacters(observation.ProductKey))
        {
            return OfficialPriceObservationRejection.InvalidProduct;
        }

        if (!IsValidText(observation.DisplayName, MaxDisplayNameLength, required: true)
            || !IsValidText(observation.SourceName, MaxSourceNameLength, required: true)
            || !IsValidText(observation.DisplayPrice, MaxPriceTextLength, required: false)
            || !IsValidText(observation.PriceQualifier, MaxPriceTextLength, required: false))
        {
            return OfficialPriceObservationRejection.InvalidText;
        }

        if (!TryClassify(observation.ProductKey, observation.SourceName, observation.DisplayName, out var family))
        {
            return IsParsableProductKey(observation.ProductKey)
                ? OfficialPriceObservationRejection.UntrackedProduct
                : OfficialPriceObservationRejection.InvalidProduct;
        }

        if (!IsValidAmount(observation.Amount))
        {
            return OfficialPriceObservationRejection.InvalidAmount;
        }

        if (!string.Equals(observation.Currency, OfficialPriceObservation.UsDollars, StringComparison.Ordinal))
        {
            return OfficialPriceObservationRejection.InvalidCurrency;
        }

        if (!Enum.IsDefined(observation.PriceKind) || !IsSyncablePriceKind(observation.PriceKind))
        {
            return OfficialPriceObservationRejection.InvalidPriceKind;
        }

        if (!Enum.IsDefined(observation.BillingUnit) || observation.BillingUnit != PriceHistoryTrackingPolicy.UnitOf(family))
        {
            return OfficialPriceObservationRejection.InvalidBillingUnit;
        }

        if (!Enum.IsDefined(observation.Origin))
        {
            return OfficialPriceObservationRejection.InvalidOrigin;
        }

        if (observation.FirstObservedUtc < EarliestObservation
            || observation.LastConfirmedUtc < observation.FirstObservedUtc
            || observation.LastConfirmedUtc > nowUtc + FutureTolerance)
        {
            return OfficialPriceObservationRejection.InvalidTimestamp;
        }

        return string.Equals(OfficialPriceObservationIdentity.Compute(observation), observation.ObservationId, StringComparison.Ordinal)
            ? OfficialPriceObservationRejection.None
            : OfficialPriceObservationRejection.IdentityMismatch;
    }

    /// <summary>The tracked family of a stored/synced product key, or false when the key is not a tracked season product.</summary>
    public static bool TryClassify(string productKey, string sourceName, string displayName, out PriceHistoryFamily family)
    {
        family = default;
        if (!TryParseCategory(productKey, out var key))
        {
            return false;
        }

        if (!PriceHistoryTrackingPolicy.CandidateCategories.Contains(key.Category))
        {
            return false;
        }

        // A recorded history row was already judged tracked when it was written; ClassifyStoredProduct
        // keeps exposing it the same way the Price History screen does (Phase 20A).
        var classified = PriceHistoryTrackingPolicy.ClassifyStoredProduct(
            key.Category, sourceName, displayName, dateSpecific: false, hasRecordedHistory: true, productKey);
        if (classified is not { } tracked)
        {
            return false;
        }

        family = tracked;
        return true;
    }

    private static bool TryParseCategory(string productKey, out CatalogProductKey key)
    {
        key = null!;
        if (string.IsNullOrWhiteSpace(productKey))
        {
            return false;
        }

        try
        {
            key = CatalogProductKey.Parse(productKey);
            return key.Value.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsParsableProductKey(string productKey) => TryParseCategory(productKey, out _);

    private static bool IsValidAmount(decimal amount) =>
        amount > 0m && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    /// <summary>Dated prices are never season history, and a "was" price is never a primary price state.</summary>
    private static bool IsSyncablePriceKind(PriceKind kind) => kind is not (PriceKind.ExactDated or PriceKind.ComparisonWas);

    private static bool IsValidText(string? value, int maxLength, bool required)
    {
        if (value is null)
        {
            return !required;
        }

        return (!required || !string.IsNullOrWhiteSpace(value)) && value.Length <= maxLength && !HasControlCharacters(value);
    }

    private static bool HasControlCharacters(string value) => value.Any(char.IsControl);

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
