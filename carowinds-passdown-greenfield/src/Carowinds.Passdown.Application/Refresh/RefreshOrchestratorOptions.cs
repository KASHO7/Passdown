namespace Carowinds.Passdown.Application.Refresh;

/// <summary>Tunables for <see cref="RefreshOrchestrator"/> (Part D/K — bindable from configuration, not hard-coded).</summary>
public sealed class RefreshOrchestratorOptions
{
    /// <summary>Conservative bound on concurrently in-flight source/date operations (spec §6.4).</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Phase 22E.1: how many months ahead Deep Refresh discovers the published park schedule (one
    /// official request per month; clamped to 1..<see cref="Freshness.ParkHoursCoveragePlanner.MaxDiscoveryMonths"/>).
    /// </summary>
    public int ParkHoursDiscoveryMonths { get; set; } = Freshness.ParkHoursCoveragePlanner.DefaultDiscoveryMonths;
}
