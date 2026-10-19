using System.Globalization;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 22F: the Associate PDF footer's "Oct 17 • 6:05 AM" stamp is the Eastern wall clock (it was the host
/// PC's zone), with the same layout and wording — no zone label is added to the PDF. Frozen History PDFs are
/// stored bytes and are never regenerated, so they are unaffected.
/// </summary>
public sealed class Phase22FGeneratedTimestampTests
{
    [Theory]
    [InlineData("2026-10-17T10:05:00Z", "Oct 17 • 6:05 AM")]  // EDT
    [InlineData("2026-01-15T11:05:00Z", "Jan 15 • 6:05 AM")]  // EST
    [InlineData("2026-03-08T07:30:00Z", "Mar 8 • 3:30 AM")]   // just after spring forward
    [InlineData("2026-11-01T06:30:00Z", "Nov 1 • 1:30 AM")]   // the repeated hour, now EST
    [InlineData("2026-10-18T02:15:00Z", "Oct 17 • 10:15 PM")] // after midnight UTC, still the 17th in Eastern
    public void GeneratedStamp_IsTheEasternWallClock(string utc, string expected)
    {
        var generatedAt = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);

        var model = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), generatedAt);

        Assert.Equal(expected, model.GeneratedAtText);
    }

    [Fact]
    public void GeneratedStamp_IgnoresTheInstantsOwnOffset()
    {
        var utc = new DateTimeOffset(2026, 10, 17, 10, 5, 0, TimeSpan.Zero);

        var fromTokyo = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), utc.ToOffset(TimeSpan.FromHours(9)));
        var fromPacific = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), utc.ToOffset(TimeSpan.FromHours(-7)));

        Assert.Equal("Oct 17 • 6:05 AM", fromTokyo.GeneratedAtText);
        Assert.Equal(fromTokyo.GeneratedAtText, fromPacific.GeneratedAtText);
    }
}
