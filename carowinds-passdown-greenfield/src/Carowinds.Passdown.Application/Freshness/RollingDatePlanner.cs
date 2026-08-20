using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// <see cref="IRollingDatePlanner"/> anchored on "today" in Eastern wall-clock
/// time (spec §6.5/§6.6) — derived from <see cref="IClock"/> rather than
/// <c>DateTimeOffset.Now</c>, so a test can pin an instant near local midnight or
/// a DST transition and assert which calendar date "today" resolves to.
/// </summary>
public sealed class RollingDatePlanner(IClock clock, RollingWindowOptions options) : IRollingDatePlanner
{
    public const int DefaultWindowLengthDays = 7;

    public IReadOnlyList<DateOnly> GetRefreshWindow() => GetRefreshWindow(options.WindowLengthDays);

    public IReadOnlyList<DateOnly> GetRefreshWindow(int windowLengthDays)
    {
        if (windowLengthDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(windowLengthDays), windowLengthDays, "Window length must be at least 1 day.");
        }

        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);

        var window = new DateOnly[windowLengthDays];
        for (var offset = 0; offset < windowLengthDays; offset++)
        {
            window[offset] = today.AddDays(offset);
        }

        return window;
    }
}
