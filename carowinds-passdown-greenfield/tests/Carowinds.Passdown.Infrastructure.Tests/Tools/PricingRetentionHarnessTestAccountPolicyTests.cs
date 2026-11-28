using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Carowinds.PricingRetentionHarness;

namespace Carowinds.Passdown.Infrastructure.Tests.Tools;

/// <summary>
/// Pre-RC.15 pricing-retention harness (docs/pre-rc15-post-closing-pricing-retention.md §11): only the approved
/// disposable Windows test accounts may let it write to the signed-in user's own database. Exact names only.
/// </summary>
public class PricingRetentionHarnessTestAccountPolicyTests
{
    [Theory]
    [InlineData("CarowindsTest")]
    [InlineData("CarowindsTestB")]
    [InlineData("carowindstest")]
    [InlineData("CAROWINDSTESTB")]
    public void ApprovedTestAccounts_AreAllowed(string userName) =>
        Assert.True(TestAccountPolicy.IsApproved(userName));

    [Theory]
    [InlineData("Sam")]
    [InlineData("jane.doe")]
    [InlineData("Administrator")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("CarowindsTestC")]
    [InlineData("CarowindsTestBB")]
    [InlineData("CarowindsTestB2")]
    [InlineData("XCarowindsTest")]
    [InlineData("Carowinds Test")]
    [InlineData("Carowinds")]
    [InlineData(" CarowindsTestB")]
    [InlineData("CarowindsTestB ")]
    [InlineData("DOMAIN\\CarowindsTest")]
    public void OrdinaryAndLookAlikeAccounts_AreRefused(string userName) =>
        Assert.False(TestAccountPolicy.IsApproved(userName));

    [Fact]
    public void MissingUserName_IsRefused() =>
        Assert.False(TestAccountPolicy.IsApproved(null));

    [Fact]
    public void ApprovedAccounts_AreExactlyTheTwoDisposableAccounts() =>
        Assert.Equal(["CarowindsTest", "CarowindsTestB"], TestAccountPolicy.ApprovedAccounts);

    [Fact]
    public void RefusalMessage_NamesBothApprovedAccounts_AndTheCurrentUser()
    {
        var message = TestAccountPolicy.RefusalMessage("Sam");

        Assert.Contains("'CarowindsTest'", message);
        Assert.Contains("'CarowindsTestB'", message);
        Assert.Contains("current user: 'Sam'", message);
    }

    [Fact]
    public void Harness_GuardsTheSignedInUsersDatabase_WithThePolicy()
    {
        var program = RepositoryFiles.ReadText("tools/pricing-retention-harness/Program.cs");

        // The default database (no --database) is guarded by the policy and nothing else decides the account.
        Assert.Contains("string.Equals(databasePath, defaultPath, StringComparison.OrdinalIgnoreCase)\n    && !TestAccountPolicy.IsApproved(Environment.UserName))", program.Replace("\r\n", "\n"));
        Assert.Contains("return Refuse(TestAccountPolicy.RefusalMessage(Environment.UserName));", program);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"TestAccountPolicy\.IsApproved\("));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(program, @"Environment\.UserName").Count); // the check and its message only
        // Offline: the harness has no HTTP client of its own.
        Assert.DoesNotContain("new HttpClient", program);
        Assert.DoesNotContain("RetrievalHttpClient(", program);
    }
}
