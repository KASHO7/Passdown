using System.Reflection;

namespace Carowinds.Passdown.Infrastructure.Tests;

/// <summary>
/// Phase 7B removed Microsoft.Playwright from the shipped Infrastructure dependency graph because
/// zero production source adapters used it (docs/release-strategy.md "Playwright dependency
/// decision") — its Node driver silently added ~88 MB to every Windows publish for a feature
/// nothing exercised. This guards against it reappearing unnoticed: reintroducing browser
/// automation should be a deliberate, reviewed decision (with its own IJobBrowserSessionFactory
/// implementation, per UnavailableJobBrowserSession.cs), not an accidental transitive add from
/// some unrelated package bump.
/// </summary>
public class PlaywrightDependencyTests
{
    [Fact]
    public void InfrastructureAssembly_DoesNotReferenceMicrosoftPlaywright()
    {
        var infrastructureAssembly = Assembly.Load("Carowinds.Passdown.Infrastructure");

        var hasPlaywrightReference = infrastructureAssembly.GetReferencedAssemblies()
            .Any(referenced => referenced.Name is not null &&
                referenced.Name.StartsWith("Microsoft.Playwright", StringComparison.OrdinalIgnoreCase));

        Assert.False(
            hasPlaywrightReference,
            "Microsoft.Playwright should not be referenced by Infrastructure (Phase 7B decision — see docs/release-strategy.md \"Playwright dependency decision\"). If a future source genuinely needs browser automation, reintroduce the package deliberately alongside a real IJobBrowserSessionFactory implementation.");
    }
}
