namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// The concrete cadence values behind each category's <c>FreshnessPolicy</c>
/// (spec §6.1). Centralized here — bindable from configuration (e.g. an Options
/// pattern section) — rather than hard-coded inside scheduling logic, per spec
/// §6.1's "cadences should be configuration, not scattered hard-coded comparisons."
/// Defaults match the spec's stated cadences.
/// </summary>
public sealed class FreshnessOptions
{
    /// <summary>
    /// Time of day (Eastern) at which the daily-cadence categories become due:
    /// park hours, dated admission, dated Fast Lane, dated haunted/SCarowinds
    /// products, and short-term promotions/bundles.
    /// </summary>
    public TimeOnly DailyRefreshTimeEastern { get; set; } = new(6, 0);

    /// <summary>Season passes: catalog, refreshed every N days.</summary>
    public int SeasonPassRefreshIntervalDays { get; set; } = 7;

    /// <summary>General and season-pass add-ons: catalog, refreshed every N days.</summary>
    public int AddOnRefreshIntervalDays { get; set; } = 14;
}
