using System.Globalization;

namespace Carowinds.Passdown.Domain.Hours;

/// <summary>
/// Canonical 12-hour display for a parsed operational time (Phase 13E Part D) — e.g. "9:00 AM",
/// "10:30 PM". The one place this formatting is produced, shared by every consumer of
/// <see cref="OperationalTimeParser"/> so Manual Hours and Private Buyout never drift into two
/// slightly different display conventions.
/// </summary>
public static class OperationalTimeFormatter
{
    public static string Format(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
