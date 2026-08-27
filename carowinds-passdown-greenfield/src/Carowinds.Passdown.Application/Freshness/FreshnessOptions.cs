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
    /// Time of day (Eastern) at which every commercial category becomes due — park hours, dated
    /// admission, dated Fast Lane, dated haunted/SCarowinds products, dated Bring-A-Friend Duo,
    /// promotions/bundles, Military/First Responder, season passes, and add-ons. Phase 13C Part
    /// AL measured every commercial catalog page/endpoint at well under 1.2s each; bounded
    /// concurrency keeps the whole daily check's wall time low, so the season pass/add-on rolling
    /// interval this option previously configured was replaced with the same daily cadence as every
    /// other category (Part R) — see <c>docs/phase13-hardening.md</c>'s Phase 13C section.
    /// </summary>
    public TimeOnly DailyRefreshTimeEastern { get; set; } = new(6, 0);
}
