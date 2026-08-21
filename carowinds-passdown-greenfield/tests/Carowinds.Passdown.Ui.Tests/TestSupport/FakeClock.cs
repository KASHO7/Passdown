using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
