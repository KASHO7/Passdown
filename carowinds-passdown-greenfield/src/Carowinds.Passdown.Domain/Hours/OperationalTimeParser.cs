using System.Globalization;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Domain.Hours;

/// <summary>
/// Parses the practical, keyboard-typed time formats a supervisor uses for Manual Hours / Private
/// Buyout entry (Phase 13E) into a <see cref="TimeOnly"/> — deterministic and culture-invariant (spec
/// Part H: never let machine locale interpret "9.30" or "09/10"), with zero UI dependency so it is
/// usable and testable without Avalonia.
///
/// Supported grammar (case-insensitive, surrounding whitespace trimmed, one optional space before
/// AM/PM):
///   - <c>H</c> or <c>HH</c>                — bare hour, 1-12, implies :00
///   - <c>H:MM</c> or <c>HH:MM</c>          — hour 1-12, minute 00-59
///   - <c>HMM</c> or <c>HHMM</c>            — a 3-4 digit run with NO colon; only accepted together
///                                             with a trailing AM/PM (e.g. "930am") — a bare digit run
///                                             with no meridiem (e.g. "930") is rejected as ambiguous
///                                             (spec Part H), never guessed
///   - any of the above with a trailing <c>am</c>/<c>AM</c>/<c>pm</c>/<c>PM</c> (with or without a
///     preceding space)
///
/// When no AM/PM is given, the hour must still be 1-12 (no bare 24-hour input, e.g. "13:00", is
/// accepted — not in the supported set); it defaults to AM, except a bare "12" (or "12:MM"), which
/// defaults to noon (PM) — the common everyday reading of "12 o'clock" alone. This default is a
/// deliberate, documented choice (see docs/phase13-hardening.md Phase 13E), not an inferred guess.
///
/// Decimal points, slashes, 24-hour hours above 12, and single-letter "a"/"p" meridiems are
/// deliberately NOT supported — ambiguous formats are rejected outright rather than guessed at (spec
/// Part H/Part AG #17).
/// </summary>
public static class OperationalTimeParser
{
    /// <summary>Shown for any syntactically or semantically invalid, non-blank input (spec Part L) — never a raw parser/exception detail.</summary>
    public const string InvalidTimeMessage = "Enter a time such as 9:00 AM.";

    private static readonly Regex ColonOrBareHourPattern = new(
        @"^(?<hour>\d{1,2})(:(?<minute>\d{2}))?\s*(?<meridiem>[AaPp][Mm])?$",
        RegexOptions.Compiled);

    private static readonly Regex DigitRunWithMeridiemPattern = new(
        @"^(?<digits>\d{3,4})\s*(?<meridiem>[AaPp][Mm])$",
        RegexOptions.Compiled);

    /// <summary>
    /// Attempts to parse <paramref name="text"/>. A null/blank/whitespace-only input is a successful
    /// parse of "no value" (<paramref name="result"/> null, no error) — whether a blank value is
    /// actually acceptable in context (e.g. a required, enabled Manual Hours field) is a caller-level
    /// concern, not this parser's (spec Part G: no UI dependency, so no notion of "required" here).
    /// </summary>
    public static bool TryParse(string? text, out TimeOnly? result, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = null;
            error = null;
            return true;
        }

        var trimmed = text.Trim();

        int hour;
        int minute;
        string? meridiem;

        var match = ColonOrBareHourPattern.Match(trimmed);
        if (match.Success)
        {
            hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
            minute = match.Groups["minute"].Success
                ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture)
                : 0;
            meridiem = match.Groups["meridiem"].Success ? match.Groups["meridiem"].Value : null;
        }
        else
        {
            match = DigitRunWithMeridiemPattern.Match(trimmed);
            if (!match.Success)
            {
                result = null;
                error = InvalidTimeMessage;
                return false;
            }

            var digits = match.Groups["digits"].Value;
            hour = int.Parse(digits[..^2], CultureInfo.InvariantCulture);
            minute = int.Parse(digits[^2..], CultureInfo.InvariantCulture);
            meridiem = match.Groups["meridiem"].Value;
        }

        if (hour is < 1 or > 12 || minute is < 0 or > 59)
        {
            result = null;
            error = InvalidTimeMessage;
            return false;
        }

        var hour24 = meridiem?.ToUpperInvariant() switch
        {
            "PM" => hour == 12 ? 12 : hour + 12,
            "AM" => hour == 12 ? 0 : hour,
            _ => hour == 12 ? 12 : hour, // no meridiem given: default AM, except bare 12 => noon.
        };

        result = new TimeOnly(hour24, minute);
        error = null;
        return true;
    }
}
