namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>
/// Phase 22E: the one rule for printing an official product price with its evidence — "$59.00" for an
/// exact (or plain catalog) price, "From $40.00" for an advertised starting price
/// (<see cref="PriceKind.StartingFrom"/>). Shared by the PDFs/Teams Copy (through the document model)
/// and Associate → Products, so a starting price can never read as exact on one surface and as a floor
/// on another. The prefix comes only from <see cref="PriceKind"/> — never from whether an amount
/// exists or from any displayed string.
/// </summary>
public static class AdvertisedPriceText
{
    /// <summary>"From $40.00" for <see cref="PriceKind.StartingFrom"/>, otherwise "$59.00".</summary>
    public static string Format(decimal amount, PriceKind kind)
    {
        var money = PresentationCulture.FormatUsd(amount);
        return kind == PriceKind.StartingFrom ? $"From {money}" : money;
    }

    /// <summary>
    /// Phase 25: <see cref="Format(decimal, PriceKind)"/> with the pass price unit — "$7.00/month" for a
    /// Membership's monthly rate, so a recurring amount can never read as a one-time price.
    /// </summary>
    public static string Format(decimal amount, PriceKind kind, PassPriceUnit unit) =>
        unit == PassPriceUnit.PerMonth ? $"{Format(amount, kind)}/month" : Format(amount, kind);

    /// <summary><see cref="Format(decimal, PriceKind, PassPriceUnit)"/>, or <see langword="null"/> when there is no amount.</summary>
    public static string? FormatOrNull(decimal? amount, PriceKind kind, PassPriceUnit unit) =>
        amount is { } value ? Format(value, kind, unit) : null;

    /// <summary><see cref="Format(decimal, PriceKind)"/>, or <see langword="null"/> when there is no amount.</summary>
    public static string? FormatOrNull(decimal? amount, PriceKind kind) => amount is { } value ? Format(value, kind) : null;
}
