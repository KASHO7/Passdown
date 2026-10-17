using System.Globalization;

namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>
/// Phase 21A: the validity year an official pass name states ("2027 Gold Pass" → 2027). This is the
/// pass's own year, not when it was observed — a 2027 pass sold in October 2026 is a 2027 pass. Only
/// an explicit, standalone four-digit year in the official name counts; a name with no year, or with
/// two different years, has an unknown pass year, and the current calendar year is never assumed.
/// </summary>
public static class PassValidityYear
{
    private const int EarliestPlausibleYear = 2000;

    private const int LatestPlausibleYear = 2099;

    public static int? FromDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        int? found = null;
        foreach (var token in displayName.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length != 4 || !token.All(char.IsAsciiDigit) ||
                !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
                year is < EarliestPlausibleYear or > LatestPlausibleYear)
            {
                continue;
            }

            if (found is { } existing && existing != year)
            {
                return null; // two different years: ambiguous, never guessed.
            }

            found = year;
        }

        return found;
    }

    /// <summary>
    /// True only when both names state a pass year and the years differ — the one case where the
    /// same official id must not merge two passes' price timelines. Unknown on either side is not a
    /// difference.
    /// </summary>
    public static bool StatesDifferentYear(string? storedDisplayName, string? observedDisplayName) =>
        FromDisplayName(storedDisplayName) is { } stored &&
        FromDisplayName(observedDisplayName) is { } observed &&
        stored != observed;

    private static readonly char[] Separators = [' ', '-', '–', '—', '/', '(', ')', ',', ':', '|', ' '];
}
