namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 13G: guards the visible product rename ("Carowinds Passdown"/"Carowinds Admissions
/// Operations" → "Carowinds Operations") directly against the shipped markup/project source,
/// mirroring <see cref="TesterGuidePackagingTests"/>'s approach of reading real source text rather
/// than constructing a full headless <c>MainWindow</c> (which would require a fully DI-resolved
/// <c>MainViewModel</c> just to read a static <c>Title</c>/<c>Icon</c> attribute). Also guards that
/// the one hard compatibility requirement of this phase — legacy local-data identifiers are never
/// cosmetically renamed — holds for the two literal names most likely to be touched by a careless
/// find-and-replace across this same set of files.
/// </summary>
public class ProductBrandingTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static string MainWindowAxamlText => File.ReadAllText(Path.Combine(RepoRoot, "src", "Carowinds.Passdown.Desktop", "Views", "MainWindow.axaml"));
    private static string DesktopCsprojText => File.ReadAllText(Path.Combine(RepoRoot, "src", "Carowinds.Passdown.Desktop", "Carowinds.Passdown.Desktop.csproj"));
    private static string DirectoryBuildPropsText => File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
    // Phase 17C: the brand header text moved from AssociatePassdownPdfGenerator (now a thin
    // orchestrator) into the Classic template renderer — Classic being the one that preserves the
    // pre-17C recognizable style this test guards.
    private static string PdfGeneratorText => File.ReadAllText(Path.Combine(RepoRoot, "src", "Carowinds.Passdown.Documents", "Associate", "Templates", "ClassicAssociatePassdownTemplateRenderer.cs"));
    private static string AppPathsText => File.ReadAllText(Path.Combine(RepoRoot, "src", "Carowinds.Passdown.Infrastructure", "AppPaths.cs"));
    private static string ScheduledTaskPlanText => File.ReadAllText(Path.Combine(RepoRoot, "src", "Carowinds.Passdown.Desktop", "ScheduledRefresh", "ScheduledTaskPlan.cs"));

    [Fact]
    public void MainWindow_TitleIsTheRenamedProductName()
    {
        Assert.Contains("Title=\"Carowinds Operations\"", MainWindowAxamlText);
    }

    [Fact]
    public void MainWindow_DoesNotContainTheOldAdmissionsOperationsBranding()
    {
        Assert.DoesNotContain("Carowinds Admissions Operations", MainWindowAxamlText);
        Assert.DoesNotContain("ADMISSIONS OPERATIONS", MainWindowAxamlText);
    }

    [Fact]
    public void MainWindow_DoesNotContainStaleCarowindsPassdownAppBranding()
    {
        Assert.DoesNotContain("Carowinds Passdown", MainWindowAxamlText);
    }

    [Fact]
    public void MainWindow_ShellRetainsTheLegitimateAssociatePassdownWorkflowLabel()
    {
        // "Passdown" describing the document/workflow (not the app) must survive the rename.
        Assert.Contains("Content=\"Associate Passdown\"", MainWindowAxamlText);
    }

    [Fact]
    public void MainWindow_IconPointsAtTheApprovedPhase13GLogoAsset()
    {
        Assert.Contains("Icon=\"/Assets/logo.ico\"", MainWindowAxamlText);
        Assert.DoesNotContain("avalonia-logo.ico", MainWindowAxamlText);

        var iconPath = Path.Combine(RepoRoot, "src", "Carowinds.Passdown.Desktop", "Assets", "logo.ico");
        Assert.True(File.Exists(iconPath), $"Expected window icon asset at '{iconPath}'.");
    }

    [Fact]
    public void DesktopCsproj_ApplicationIconPointsAtTheApprovedPhase13GLogoAsset()
    {
        Assert.Contains("<ApplicationIcon>Assets\\logo.ico</ApplicationIcon>", DesktopCsprojText);
    }

    [Fact]
    public void DirectoryBuildProps_ProductIsTheRenamedProductName()
    {
        Assert.Contains("<Product>Carowinds Operations</Product>", DirectoryBuildPropsText);
        Assert.DoesNotContain("<Product>Carowinds Passdown</Product>", DirectoryBuildPropsText);
    }

    [Fact]
    public void AssociatePassdownPdf_HeaderUsesTheRenamedProductName()
    {
        Assert.Contains("\"CAROWINDS OPERATIONS\"", PdfGeneratorText);
        Assert.DoesNotContain("CAROWINDS ADMISSIONS OPERATIONS", PdfGeneratorText);
    }

    [Fact]
    public void AssociatePassdownPdf_HeaderStillPrintsTheLegitimateAssociatePassdownDocumentTitle()
    {
        Assert.Contains("\"Associate Passdown\"", PdfGeneratorText);
    }

    // ------------------------------------------------------------------
    // Compatibility guards: the rename must never touch these legacy identifiers (CLAUDE.md
    // "Critical compatibility rule" / this phase's own explicit hard requirement).
    // ------------------------------------------------------------------

    [Fact]
    public void AppPaths_LocalDataFolderNameIsUnchangedByTheRename()
    {
        Assert.Contains("\"Carowinds Passdown\"", AppPathsText);
    }

    [Fact]
    public void ScheduledTaskPlan_TaskNameIsUnchangedByTheRename()
    {
        Assert.Contains("\"Carowinds Passdown Daily Refresh\"", ScheduledTaskPlanText);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
