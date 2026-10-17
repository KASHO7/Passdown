using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 19B §45: opt-in live check that the Bring-A-Friend Duo keyword is always interpreted as a
/// successful answer — published or not — never an Error just because the limited-time offer ended
/// (it had ended as of 2026-10-15). Excluded from a normal <c>dotnet test</c> run (spec §14.3); plain
/// anonymous calendar POSTs only — never signs in, adds to a cart or submits payment.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class DatedBringAFriendDuoSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_ForToday_IsASuccessfulAnswerWhetherOrNotTheOfferIsRunning()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new DatedBringAFriendDuoSource(retrievalHttpClient, new SystemClock());

        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(DateTimeOffset.UtcNow).DateTime);
        var result = await source.RetrieveAsync(new RetrievalRequest("dated-bring-a-friend-duo.official", SourceCategory.BringAFriendDuo, today), CancellationToken.None);

        Assert.True(result.Evidence.Status.IsSuccessfulCompletion(), $"Expected a successful answer, got {result.Evidence.Status}: {result.Evidence.TechnicalDetail}");
        if (result.Evidence.Status != RetrievalStatus.Verified)
        {
            Assert.Null(result.Payload); // absent/not published — never a fabricated price
        }
    }
}
