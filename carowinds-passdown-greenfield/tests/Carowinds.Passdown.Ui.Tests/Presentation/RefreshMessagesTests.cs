using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Ui.Tests.Presentation;

/// <summary>Phase 13B Part L/R: <see cref="RefreshMessages.Summary"/> must read distinctly for a <see cref="RefreshMode.UserForced"/> attempt that never ran because a refresh was already in progress.</summary>
public class RefreshMessagesTests
{
    [Fact]
    public void Summary_AlreadyInProgress_ReturnsDistinctMessage_NeverTheZeroOperationsMessage()
    {
        var summary = RefreshSummary.AlreadyInProgress(DateTimeOffset.UnixEpoch);

        var message = RefreshMessages.Summary(summary);

        Assert.Contains("already in progress", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nothing to check", message);
    }

    [Fact]
    public void Summary_AlreadyInProgress_NeverReadsAsAFailure()
    {
        var summary = RefreshSummary.AlreadyInProgress(DateTimeOffset.UnixEpoch);

        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Cancelled);
        Assert.True(summary.WasSkippedDueToOverlap);
    }

    [Fact]
    public void Summary_ZeroOperationsWithoutOverlap_StillReturnsNothingToCheckMessage()
    {
        var summary = new RefreshSummary(RefreshJobType.UserForced, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, 0, 0, []);

        var message = RefreshMessages.Summary(summary);

        Assert.Equal("Nothing to check for this date.", message);
    }
}
