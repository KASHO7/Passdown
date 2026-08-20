using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Retrieval;

public class RetrievalDiagnosticsTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/park-hours");
    private static readonly DateTimeOffset SampleTime = new(2026, 8, 20, 6, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Error_UsesTheSuppliedUserFacingDetailVerbatim()
    {
        var exception = new InvalidOperationException("Connection reset by peer at 10.0.0.1:443");

        var evidence = RetrievalDiagnostics.Error(
            "Park hours",
            SampleUrl,
            SampleTime,
            RetrievalStrategy.Html,
            "We couldn't check park hours right now.",
            exception);

        Assert.Equal(RetrievalStatus.Error, evidence.Status);
        Assert.Equal("We couldn't check park hours right now.", evidence.Detail);
        Assert.DoesNotContain("10.0.0.1", evidence.Detail);
    }

    [Fact]
    public void Error_RetainsFullExceptionDetailForLogsOnly()
    {
        var exception = new InvalidOperationException("Connection reset by peer at 10.0.0.1:443");

        var evidence = RetrievalDiagnostics.Error(
            "Park hours",
            SampleUrl,
            SampleTime,
            RetrievalStrategy.Html,
            "We couldn't check park hours right now.",
            exception);

        Assert.NotNull(evidence.TechnicalDetail);
        Assert.Contains("10.0.0.1", evidence.TechnicalDetail);
        Assert.Contains(nameof(InvalidOperationException), evidence.TechnicalDetail);
    }

    [Fact]
    public void Error_CarriesTheRequestedDateThrough()
    {
        var date = new DateOnly(2026, 10, 31);

        var evidence = RetrievalDiagnostics.Error(
            "Dated tickets",
            SampleUrl,
            SampleTime,
            RetrievalStrategy.Browser,
            "We couldn't check ticket prices for this date.",
            new TimeoutException(),
            requestedDate: date);

        Assert.Equal(date, evidence.RequestedDate);
        Assert.Null(evidence.ConfirmedDate);
    }
}
