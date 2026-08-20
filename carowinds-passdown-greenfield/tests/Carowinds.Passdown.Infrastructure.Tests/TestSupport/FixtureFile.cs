namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

/// <summary>
/// Locates and reads committed retrieval fixtures under the repo's <c>fixtures/</c> directory
/// (spec §14.2), so contract tests can exercise parser logic against saved HTML/JSON without
/// needing internet access. Resolves the directory by walking up from the test assembly's own
/// output directory rather than a hard-coded relative path, since that differs between
/// <c>dotnet test</c> and an IDE test runner.
/// </summary>
public static class FixtureFile
{
    public static string ReadText(string relativePath)
    {
        var fixturesRoot = ResolveFixturesRoot();
        var path = Path.Combine(fixturesRoot, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No fixture at '{relativePath}' under the resolved fixtures root '{fixturesRoot}'. " +
                "Fixtures must be committed under carowinds-passdown-greenfield/fixtures (spec §14.2).",
                path);
        }

        return File.ReadAllText(path);
    }

    private static string ResolveFixturesRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
            {
                return Path.Combine(directory.FullName, "fixtures");
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate carowinds-passdown-greenfield (via CarowindsPassdown.slnx) by walking up from '{AppContext.BaseDirectory}'.");
    }
}
