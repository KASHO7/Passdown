using System.Text.RegularExpressions;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.RepositoryGuards;

/// <summary>
/// Repository guard (Phase 36B, audit finding TD-01): real Avalonia <c>Window</c>s may only be created by the six
/// Ui.Tests classes that the deterministic procedure runs alone. Ui.Tests has no running dispatcher loop
/// (<c>HeadlessTestSetup</c>), so a real window in the mixed partition breaks thread affinity — Phase 34A recorded
/// 148 failures from one mis-partitioned run. New real-window tests join a run-alone class as a <c>partial</c>
/// (for example <c>Phase35BWorkspaceWindowTests.cs</c> is part of <c>Phase33PreviewWindowTests</c>) or extend the
/// list here, in the <c>build-and-test</c> skill and in the testing guide together.
/// </summary>
/// <remarks>
/// Mechanical check only: it looks for <c>new Window</c>, <c>new MainWindow</c> and <c>.Show()</c>/<c>.ShowDialog()</c>
/// outside comments. A window created some other way (a target-typed <c>new()</c>, a factory in a shared helper) is
/// not detected; keep window creation explicit in the run-alone classes.
/// </remarks>
public partial class RealWindowPartitionGuardTests
{
    internal static readonly string[] RunAloneClasses =
    [
        "OperationsCalendarViewTests",
        "Phase15ERealWindowLayoutTests",
        "RealWindowStartupTimingTests",
        "SpellCheckNavigationLifecycleTests",
        "Phase21DMathEditorWindowTests",
        "Phase33PreviewWindowTests",
    ];

    private const string UiTestsDirectory = "tests/Carowinds.Passdown.Ui.Tests";

    [Fact]
    public void RealWindows_AreCreatedOnlyInRunAloneClasses()
    {
        var violations = UiTestSourceFiles()
            .Where(path => !IsPermittedRealWindowSource(File.ReadAllText(path)))
            .Select(RepositoryFiles.RelativeOf)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Real Window creation outside the six run-alone Ui.Tests classes (make the test a partial of one of " +
            string.Join(", ", RunAloneClasses) + ", or extend the run-alone list in this guard, the build-and-test skill " +
            "and docs/architecture/testing-and-performance.md together): " + string.Join(", ", violations));
    }

    [Fact]
    public void EveryRunAloneClass_StillExistsInUiTests()
    {
        var declared = UiTestSourceFiles().SelectMany(path => DeclaredClassNames(File.ReadAllText(path))).ToHashSet(StringComparer.Ordinal);

        Assert.All(RunAloneClasses, name => Assert.Contains(name, declared));
    }

    [Fact]
    public void BuildAndTestSkillAndTestingGuide_ListTheSameRunAloneClasses()
    {
        var skill = RepositoryFiles.ReadText(".claude/skills/build-and-test/SKILL.md");
        var guide = RepositoryFiles.ReadText("docs/architecture/testing-and-performance.md");

        Assert.All(RunAloneClasses, name =>
        {
            // Excluded from the mixed partition, run alone in the loop, and named in the current testing guide.
            Assert.Contains($"FullyQualifiedName!~.{name}.", skill, StringComparison.Ordinal);
            Assert.Contains($" {name}", skill, StringComparison.Ordinal);
            Assert.Contains($"`{name}`", guide, StringComparison.Ordinal);
        });

        Assert.Equal(RunAloneClasses.Length, MixedPartitionExclusion().Matches(skill).Select(m => m.Groups[1].Value).Distinct().Count());
    }

    [Theory]
    [InlineData("public class FooTests { void M() { var w = new Window(); w.Show(); } }", false)]
    [InlineData("public class FooTests { void M() { var w = new MainWindow { Width = 9 }; } }", false)]
    [InlineData("public class FooTests { void M() { var w = new Avalonia.Controls.Window(); } }", false)]
    [InlineData("public class FooTests { void M() { dialog.ShowDialog(owner); } }", false)]
    [InlineData("public partial class Phase33PreviewWindowTests { void M() { var w = new Window(); w.Show(); } }", true)]
    [InlineData("public class FooTests\n{\n    // a real new Window() would need .Show() here\n    /// <summary>Not a new Window.</summary>\n}", true)]
    [InlineData("public class FooTests { void M() { var view = new AssociateView(); vm.ShowShowsSectionCommand.Execute(null); } }", true)]
    public void Detector_FlagsWindowCreationOnlyOutsideRunAloneClasses(string source, bool permitted)
    {
        Assert.Equal(permitted, IsPermittedRealWindowSource(source));
    }

    internal static bool IsPermittedRealWindowSource(string source)
    {
        var code = LineComment().Replace(source, string.Empty);
        if (!RealWindowCreation().IsMatch(code))
        {
            return true;
        }

        return DeclaredClassNames(code).Any(name => RunAloneClasses.Contains(name, StringComparer.Ordinal));
    }

    private static IEnumerable<string> DeclaredClassNames(string source) =>
        ClassDeclaration().Matches(source).Select(m => m.Groups[1].Value);

    private static IEnumerable<string> UiTestSourceFiles()
    {
        var root = RepositoryFiles.PathOf(UiTestsDirectory);
        var separator = Path.DirectorySeparatorChar;
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"//.*$", RegexOptions.Multiline)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\bnew\s+(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*(?:Main)?Window\s*[({]|\.Show(?:Dialog)?\s*\(")]
    private static partial Regex RealWindowCreation();

    [GeneratedRegex(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"FullyQualifiedName!~\.([A-Za-z0-9_]+)\.")]
    private static partial Regex MixedPartitionExclusion();
}
