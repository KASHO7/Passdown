using System.Text.RegularExpressions;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.RepositoryGuards;

/// <summary>
/// Repository guard: relative Markdown links and back-ticked repository paths in the current
/// documentation and Claude instructions resolve to files or directories that exist. Historical phase
/// and release records are deliberately excluded — they describe the repository as it was and may name
/// files that have since been removed. A passing run proves only that references resolve, not that the
/// prose is accurate (.claude/rules/documentation.md).
/// </summary>
public partial class CurrentDocumentationReferenceTests
{
    private static readonly string[] FixedDocuments =
    [
        "README.md",
        "CLAUDE.md",
        "AGENT_BUILD_SPEC.md",
        "docs/README.md",
        "docs/architecture.md",
        "docs/archive/README.md",
    ];

    public static IEnumerable<string> CurrentDocuments()
    {
        var documents = new List<string>(FixedDocuments);
        documents.AddRange(Files("docs/architecture", "*.md").Where(path => !path.EndsWith("/architecture-history.md", StringComparison.Ordinal)));
        documents.AddRange(Files(".claude/rules", "*.md"));
        documents.AddRange(Files(".claude/skills", "SKILL.md", SearchOption.AllDirectories));
        return documents;
    }

    [Fact]
    public void CurrentDocumentSet_IncludesEveryArchitectureGuideRuleAndSkill()
    {
        var documents = CurrentDocuments().ToArray();

        Assert.All(FixedDocuments, path => Assert.True(File.Exists(RepositoryFiles.PathOf(path)), $"Missing current document {path}"));
        Assert.True(documents.Count(path => path.StartsWith("docs/architecture/", StringComparison.Ordinal)) >= 9, "Expected the nine subsystem guides.");
        Assert.True(documents.Count(path => path.StartsWith(".claude/skills/", StringComparison.Ordinal)) >= 5, "Expected the five project skills.");
    }

    [Fact]
    public void RelativeMarkdownLinks_Resolve()
    {
        var broken = new List<string>();
        foreach (var document in CurrentDocuments())
        {
            var directory = Path.GetDirectoryName(RepositoryFiles.PathOf(document))!;
            foreach (Match match in MarkdownLinkPattern().Matches(RepositoryFiles.ReadText(document)))
            {
                var target = match.Groups[1].Value;
                if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Exists(Path.Combine(directory, target)))
                {
                    broken.Add($"{document}: ({target})");
                }
            }
        }

        Assert.True(broken.Count == 0, "Broken relative links:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void BacktickedRepositoryPaths_Exist()
    {
        var missing = new List<string>();
        foreach (var document in CurrentDocuments())
        {
            var directory = Path.GetDirectoryName(RepositoryFiles.PathOf(document))!;
            foreach (Match match in RepositoryPathPattern().Matches(RepositoryFiles.ReadText(document)))
            {
                var path = match.Groups[1].Value.TrimEnd('.', ',', ';', ':', ')');
                if (path.Contains('…') || path.Contains('<'))
                {
                    continue;
                }

                if (!Exists(RepositoryFiles.PathOf(path)) && !Exists(Path.Combine(directory, path)))
                {
                    missing.Add($"{document}: `{path}`");
                }
            }
        }

        Assert.True(missing.Count == 0, "Referenced paths that do not exist:\n" + string.Join("\n", missing));
    }

    private static IEnumerable<string> Files(string relativeDirectory, string pattern, SearchOption option = SearchOption.TopDirectoryOnly) =>
        Directory.GetFiles(RepositoryFiles.PathOf(relativeDirectory), pattern, option)
            .Select(RepositoryFiles.RelativeOf)
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// The path exists with exactly this letter case. macOS and Windows file systems are usually
    /// case-insensitive while Linux is not, so every segment below the application root is compared
    /// ordinally against the real directory entries to give the same answer everywhere.
    /// Segments above the root (a link that leaves the application directory) are only checked for existence.
    /// </summary>
    private static bool Exists(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            return false;
        }

        var root = Path.GetFullPath(RepositoryFiles.ApplicationRoot);
        var relative = Path.GetRelativePath(root, full);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return true;
        }

        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.EnumerateFileSystemEntries(current).Any(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal)))
            {
                return false;
            }

            current = Path.Combine(current, segment);
        }

        return true;
    }

    [GeneratedRegex(@"\]\(([^)#\s]+)(?:#[^)]*)?\)")]
    private static partial Regex MarkdownLinkPattern();

    // Glob patterns (containing '*') and placeholders ('<…>', '{…}') are not concrete paths and are skipped.
    [GeneratedRegex(@"`((?:\.\./|\.claude/|docs/|src/|tests/|tools/|fixtures/|packaging/)[^`*<>{} ]+)`")]
    private static partial Regex RepositoryPathPattern();
}
