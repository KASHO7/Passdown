using System.Globalization;

namespace Carowinds.Passdown.Domain;

/// <summary>
/// Phase 18B (QUAL-001) product decision: every user-facing date, time, weekday/month name, and price
/// is shown in US English conventions — "Saturday, September 19, 2026", "Sep 19, 2026", "10:30 AM",
/// "$55.00", "From $37" — regardless of the Windows region/display language. A French, German, or UK
/// PC must not change month names, date order, AM/PM, or the currency symbol of a USD price.
/// <para>
/// This is the one place that culture is named. Presentation code formats with
/// <see cref="UnitedStates"/> (never <c>new CultureInfo(...)</c>, never the machine's current culture);
/// <see cref="ApplyAsProcessDefault"/> additionally pins the process default at startup so framework
/// controls (date pickers, XAML string formats) follow the same rule.
/// </para>
/// <para>
/// Deliberately out of scope: storage, sync wire formats, hashing, file names, and parsing stay
/// explicitly invariant/ISO (<c>yyyy-MM-dd</c>, round-trip "O"), and nothing here changes time zones —
/// Carowinds operational dates/times remain America/New_York. This is not a localization framework:
/// there is no culture switching and no translated text.
/// </para>
/// </summary>
public static class PresentationCulture
{
    /// <summary>
    /// en-US from the runtime's built-in data. <see cref="CultureInfo.GetCultureInfo(string)"/> returns
    /// a cached, read-only instance that never picks up the Windows user's regional overrides (unlike
    /// <c>new CultureInfo("en-US")</c>, which would honor a customized short-time pattern).
    /// </summary>
    public static CultureInfo UnitedStates { get; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A USD amount as <c>$55.00</c> — always the dollar sign and two decimals, never a grouping separator, never converted.</summary>
    public static string FormatUsd(decimal amount) => "$" + amount.ToString("0.00", UnitedStates);

    /// <summary>
    /// Makes en-US the default culture for every thread of this process, including the current one.
    /// Called once at the very start of <c>Main</c>. Safe for persistence because every persisted,
    /// synced, or parsed value already uses an explicit invariant culture.
    /// </summary>
    public static void ApplyAsProcessDefault()
    {
        CultureInfo.DefaultThreadCurrentCulture = UnitedStates;
        CultureInfo.DefaultThreadCurrentUICulture = UnitedStates;
        CultureInfo.CurrentCulture = UnitedStates;
        CultureInfo.CurrentUICulture = UnitedStates;
    }
}
