namespace Carowinds.Passdown.Application.Time;

/// <summary>
/// Abstracts the current instant so scheduling/freshness logic never calls
/// <c>DateTimeOffset.Now</c>/<c>UtcNow</c> directly, keeping it deterministically
/// testable.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
