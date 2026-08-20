namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

/// <summary>
/// Trait values for <c>dotnet test --filter Category=...</c> (spec §14.3). A normal
/// <c>dotnet test</c> run must not depend on the internet or on live official sources
/// (CLAUDE.md "Testing"); tests tagged <see cref="LiveSmoke"/> are excluded by default — see the
/// <c>build-and-test</c> skill for the opt-in filter.
/// </summary>
public static class TestCategories
{
    public const string LiveSmoke = "LiveSmoke";
}
