namespace Carowinds.PricingRetentionHarness;

/// <summary>
/// The disposable Windows accounts the pricing-retention harness may write to when it uses the signed-in user's own
/// Carowinds Passdown database (no <c>--database</c>). Exact account names only (Windows names are case-insensitive):
/// no prefix, suffix or partial match, no trimming, and every other account — including the owner's normal one — is
/// refused. Linked into Carowinds.Passdown.Infrastructure.Tests (Tools/PricingRetentionHarnessTestAccountPolicyTests).
/// </summary>
public static class TestAccountPolicy
{
    /// <summary>The approved disposable test accounts.</summary>
    public static IReadOnlyList<string> ApprovedAccounts { get; } = ["CarowindsTest", "CarowindsTestB"];

    public static bool IsApproved(string? userName) =>
        userName is not null && ApprovedAccounts.Any(account => string.Equals(account, userName, StringComparison.OrdinalIgnoreCase));

    public static string RefusalMessage(string? userName) =>
        $"This writes to the signed-in user's Carowinds Passdown data. Sign in to Windows as one of the disposable test accounts ({string.Join(" or ", ApprovedAccounts.Select(a => $"'{a}'"))}) (current user: '{userName}').";
}
