namespace Carowinds.Passdown.Application.Refresh;

/// <summary>Tunables for <see cref="RefreshOrchestrator"/> (Part D/K — bindable from configuration, not hard-coded).</summary>
public sealed class RefreshOrchestratorOptions
{
    /// <summary>Conservative bound on concurrently in-flight source/date operations (spec §6.4).</summary>
    public int MaxConcurrency { get; set; } = 3;
}
