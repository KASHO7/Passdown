using System.Xml.Linq;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.RepositoryGuards;

/// <summary>
/// Repository guard: the production project graph matches the documented dependency direction
/// (CLAUDE.md "Non-negotiable contracts", .claude/rules/architecture.md, docs/architecture.md
/// "Dependency direction"). Complements the assembly-reference checks in
/// Domain.Tests/DependencyDirectionTests and Application.Tests/ApplicationDependencyDirectionTests by
/// reading every <c>src/*/*.csproj</c>, so a new project or reference cannot slip in unreviewed.
/// Changing the graph is an architectural decision: update this table and docs/architecture.md together.
/// </summary>
public class ProjectDependencyGraphTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ApprovedGraph = new Dictionary<string, string[]>
    {
        ["Carowinds.Passdown.Domain"] = [],
        ["Carowinds.Passdown.Application"] = ["Carowinds.Passdown.Domain"],
        ["Carowinds.Passdown.Infrastructure"] = ["Carowinds.Passdown.Application", "Carowinds.Passdown.Domain"],
        ["Carowinds.Passdown.Documents"] = ["Carowinds.Passdown.Application", "Carowinds.Passdown.Domain"],
        ["Carowinds.Passdown.Desktop"] = ["Carowinds.Passdown.Application", "Carowinds.Passdown.Documents", "Carowinds.Passdown.Infrastructure"],
        ["Carowinds.Passdown.Updater"] = ["Carowinds.Passdown.Infrastructure"],
    };

    [Fact]
    public void ProductionProjects_AreExactlyTheDocumentedSix()
    {
        var actual = ReadProductionGraph().Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(ApprovedGraph.Keys.Order(StringComparer.Ordinal).ToArray(), actual);
    }

    [Fact]
    public void ProductionProjectReferences_MatchTheApprovedDependencyDirection()
    {
        var graph = ReadProductionGraph();

        foreach (var (project, approved) in ApprovedGraph)
        {
            var actual = graph[project].Order(StringComparer.Ordinal).ToArray();
            Assert.True(
                approved.Order(StringComparer.Ordinal).SequenceEqual(actual),
                $"{project} references [{string.Join(", ", actual)}]; approved: [{string.Join(", ", approved)}]. " +
                "A dependency change needs architecture review and a docs/architecture.md update.");
        }
    }

    [Fact]
    public void ProductionProjectGraph_HasNoCycles()
    {
        var graph = ReadProductionGraph();
        var state = new Dictionary<string, int>();

        foreach (var project in graph.Keys)
        {
            Visit(project, []);
        }

        void Visit(string project, List<string> path)
        {
            if (state.GetValueOrDefault(project) == 2)
            {
                return;
            }

            Assert.False(state.GetValueOrDefault(project) == 1, $"Project reference cycle: {string.Join(" -> ", path.Append(project))}");
            state[project] = 1;
            foreach (var reference in graph.GetValueOrDefault(project, []))
            {
                Visit(reference, [.. path, project]);
            }

            state[project] = 2;
        }
    }

    [Fact]
    public void DomainProject_HasNoPackageReferences()
    {
        var domain = XDocument.Load(RepositoryFiles.PathOf("src/Carowinds.Passdown.Domain/Carowinds.Passdown.Domain.csproj"));

        var packages = domain.Descendants().Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .ToArray();

        Assert.True(packages.Length == 0, $"Domain must stay free of framework and I/O packages; found: {string.Join(", ", packages)}");
    }

    private static Dictionary<string, string[]> ReadProductionGraph()
    {
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var projectFile in Directory.GetFiles(RepositoryFiles.PathOf("src"), "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var references = XDocument.Load(projectFile).Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
                .ToArray();
            graph[Path.GetFileNameWithoutExtension(projectFile)] = references;
        }

        return graph;
    }
}
