using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
