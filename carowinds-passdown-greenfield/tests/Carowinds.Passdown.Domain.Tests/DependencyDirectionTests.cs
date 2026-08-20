using System.Reflection;

namespace Carowinds.Passdown.Domain.Tests;

public class DependencyDirectionTests
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
    public void DomainAssembly_DoesNotReferenceOutOfLayerAssemblies()
    {
        var domainAssembly = Assembly.Load("Carowinds.Passdown.Domain");

        var forbiddenReferences = domainAssembly.GetReferencedAssemblies()
            .Where(referenced => ForbiddenReferenceNamePrefixes.Any(prefix =>
                referenced.Name is not null &&
                referenced.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(referenced => referenced.Name)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"""
            Domain must stay free of Avalonia, SQLite, HTTP, Playwright, QuestPDF, and Windows
            dependencies (CLAUDE.md "Architecture"). Found: {string.Join(", ", forbiddenReferences)}
            """);
    }
}
