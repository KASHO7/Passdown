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

    /// <summary><see cref="Format(decimal, PriceKind)"/>, or <see langword="null"/> when there is no amount.</summary>
    public static string? FormatOrNull(decimal? amount, PriceKind kind) => amount is { } value ? Format(value, kind) : null;
}
