using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Carowinds.Passdown.Infrastructure.Time;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 21C opt-in live check of the official show schedule (docs/phase21c-official-show-schedules.md).
/// Excluded from normal runs (<c>Category=LiveSmoke</c>). One plain public GET; no sign-in.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class ShowScheduleSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_ForToday_ConfirmsTheExactDate_AndNeverReturnsTimelessShows()
    {
        using var httpClient = new HttpClient();
        var source = new ShowScheduleSource(new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(15) }), new SystemClock());
        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(DateTimeOffset.UtcNow).DateTime);

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, SourceCategory.ShowSchedule, today), CancellationToken.None);

        Assert.NotEqual(RetrievalStatus.Error, result.Evidence.Status);
        if (result.Evidence.Status == RetrievalStatus.Verified)
        {
            Assert.Equal(today, result.Evidence.ConfirmedDate);
            Assert.All(result.Payload!.Shows, show => Assert.NotEmpty(show.Times));
        }
    }
}
