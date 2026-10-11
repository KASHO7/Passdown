using System.Globalization;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Plain "$0.00" money formatting/parsing shared by the Phase 4D product-editing row view models.
/// Deliberately not culture-"C" formatting — Carowinds prices are always USD regardless of the host
/// machine's locale, and <see cref="CultureInfo.InvariantCulture"/>'s currency symbol is not "$".
/// </summary>
internal static class PriceFormatting
{
    public static string Format(decimal amount) => PresentationCulture.FormatUsd(amount);

    public static string? FormatOrNull(decimal? amount) => amount is { } value ? Format(value) : null;

    /// <summary>
    /// Blank parses as a valid "no override" (<paramref name="value"/> null). An unparsable or
    /// negative value fails without touching <paramref name="value"/> — the caller is expected to
    /// keep its own last-known-good amount untouched on failure (spec Phase 4D Part E).
    /// </summary>
    public static bool TryParse(string? text, out decimal? value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = null;
            return true;
        }

        var trimmed = text.Trim().TrimStart('$').Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            value = parsed;
            error = null;
            return true;
        }

        value = null;
        error = "Enter a valid price, e.g. 24.99.";
        return false;
    }
}
