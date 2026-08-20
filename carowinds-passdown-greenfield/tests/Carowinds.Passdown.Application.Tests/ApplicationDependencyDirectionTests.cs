using System.Reflection;

namespace Carowinds.Passdown.Application.Tests;

/// <summary>
/// Mirrors <c>Carowinds.Passdown.Domain.Tests.DependencyDirectionTests</c>: Application
/// holds use cases and abstractions/ports over Domain (CLAUDE.md "Architecture") — it
/// must never itself pull in a concrete Infrastructure/Desktop/Documents dependency,
/// even indirectly. Concrete adapters (SQLite, HTTP, Playwright, QuestPDF, Avalonia)
/// belong in the layers that implement these ports, not the layer that declares them.
/// </summary>
public class ApplicationDependencyDirectionTests
{
    private static readonly string[] ForbiddenReferenceNamePrefixes =
    [
        "Avalonia",
        "Microsoft.Data.Sqlite",
        "SQLitePCL",
        "Microsoft.Playwright",
        "QuestPDF",
        "System.Net.Http",
        "Microsoft.Win32",
    ];

    [Fact]
    public void ApplicationAssembly_DoesNotReferenceOutOfLayerAssemblies()
    {
        var applicationAssembly = Assembly.Load("Carowinds.Passdown.Application");

        var forbiddenReferences = applicationAssembly.GetReferencedAssemblies()
            .Where(referenced => ForbiddenReferenceNamePrefixes.Any(prefix =>
                referenced.Name is not null &&
                referenced.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(referenced => referenced.Name)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"""
            Application must stay free of Avalonia, SQLite, HTTP, Playwright, QuestPDF, and
            Windows dependencies (CLAUDE.md "Architecture"). Found: {string.Join(", ", forbiddenReferences)}
            """);
    }
}
