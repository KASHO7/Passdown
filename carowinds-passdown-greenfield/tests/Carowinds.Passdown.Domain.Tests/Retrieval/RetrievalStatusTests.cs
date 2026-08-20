using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

public class RetrievalStatusTests
{
    [Theory]
    [InlineData(RetrievalStatus.Verified, true)]
    [InlineData(RetrievalStatus.Closed, true)]
    [InlineData(RetrievalStatus.NotPublished, true)]
    [InlineData(RetrievalStatus.Unavailable, true)]
    [InlineData(RetrievalStatus.Error, false)]
    [InlineData(RetrievalStatus.Stale, false)]
    public void IsSuccessfulCompletion_MatchesSpecDefinitionOfASourceBeingReached(RetrievalStatus status, bool expected)
    {
        Assert.Equal(expected, status.IsSuccessfulCompletion());
    }
}
