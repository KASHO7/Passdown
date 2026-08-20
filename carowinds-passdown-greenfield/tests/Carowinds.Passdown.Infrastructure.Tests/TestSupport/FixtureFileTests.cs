namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

public class FixtureFileTests
{
    [Fact]
    public void ReadText_ReadsACommittedFixture()
    {
        var content = FixtureFile.ReadText("_infrastructure/sample-fixture.txt");

        Assert.Contains("FixtureFile", content);
    }

    [Fact]
    public void ReadText_WhenFixtureIsMissing_ThrowsWithAHelpfulMessage()
    {
        var exception = Assert.Throws<FileNotFoundException>(() => FixtureFile.ReadText("_infrastructure/does-not-exist.txt"));

        Assert.Contains("_infrastructure/does-not-exist.txt", exception.Message);
    }
}
