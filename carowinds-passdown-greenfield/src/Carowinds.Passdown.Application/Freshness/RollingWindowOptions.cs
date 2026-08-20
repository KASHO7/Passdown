namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// The rolling cache window length (spec §6.5): "The default rolling window is
/// seven days. The architecture should support a configurable longer window
/// without redesign."
/// </summary>
public sealed class RollingWindowOptions
{
    public int WindowLengthDays { get; set; } = RollingDatePlanner.DefaultWindowLengthDays;
}
