using System.Globalization;

namespace Carowinds.Passdown.Domain;

/// <summary>
/// Phase 22D: the one display rule for whole-number operational counts — projected attendance and
/// pre-solds — on every surface (Dashboard, Associate, History detail, sync conflicts, Teams Copy, the
/// three PDF templates): en-US thousands grouping (<c>38500</c> → <c>38,500</c>) from
/// <see cref="PresentationCulture.UnitedStates"/>, never the machine's locale.
/// <para>
/// Display only. Stored, synced and finalized values stay plain integers; nothing here is used for
/// persistence, hashing or source parsing. Not for dates, years, times, IDs, prices or free text.
/// </para>
/// </summary>
public static class OperationalCount
{
    /// <summary><c>0</c> → "0", <c>1000</c> → "1,000", <c>38500</c> → "38,500", <c>1000000</c> → "1,000,000".</summary>
    public static string Format(int count) => count.ToString("N0", PresentationCulture.UnitedStates);

    /// <summary>The grouped count, or <paramref name="missingText"/> when no value was entered (missing is never shown as 0).</summary>
    public static string Format(int? count, string missingText) => count is { } value ? Format(value) : missingText;

    /// <summary>
    /// Parses what an associate typed into a count field: the same whole numbers accepted before Phase 22D
    /// (optional sign, surrounding spaces, invariant digits) plus en-US group separators, which are simply
    /// ignored so editing a displayed "38,500" in place ("38,5001") still yields the digits typed.
    /// Returns false for anything else (decimals, letters, a lone comma). Range checks (negative values)
    /// stay with the caller.
    /// </summary>
    public static bool TryParseEntry(string text, out int value)
    {
        var trimmed = text.Trim();
        var digits = trimmed.Contains(',', StringComparison.Ordinal)
            ? trimmed.Replace(",", string.Empty, StringComparison.Ordinal)
            : trimmed;

        if (digits.Length == 0 || digits.Any(char.IsWhiteSpace))
        {
            value = 0;
            return false;
        }

        return int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
