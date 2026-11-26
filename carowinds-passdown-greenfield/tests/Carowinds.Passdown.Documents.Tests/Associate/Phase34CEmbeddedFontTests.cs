using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using UglyToad.PdfPig;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 34C: Carowinds.Passdown.Documents.dll embeds only six of QuestPDF's eighteen Lato faces (Regular,
/// Bold, SemiBold, Italic, BoldItalic, SemiBoldItalic). The shipped release has no loose Lato files, so a
/// template asking for any other weight (Light, Medium, ExtraBold, Black, Thin…) would silently fall back to
/// a different face on Windows. These tests fail first instead: the embedded set is pinned, the document
/// sources may only use the embedded weights, and real PDFs of every template only contain embedded faces.
/// </summary>
public sealed class Phase34CEmbeddedFontTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);

    private static readonly string[] EmbeddedFaces = ["Bold", "BoldItalic", "Italic", "Regular", "SemiBold", "SemiBoldItalic"];

    public static TheoryData<AssociatePassdownTemplate> AllTemplates() => new(Enum.GetValues<AssociatePassdownTemplate>());

    [Fact]
    public void DocumentsAssembly_EmbedsExactlyTheSixLatoFacesInUse()
    {
        var names = typeof(AssociatePassdownPdfGenerator).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("LatoFont.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(EmbeddedFaces.Select(face => $"LatoFont.Lato-{face}.ttf").Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public void DocumentSources_OnlyAskForEmbeddedWeights()
    {
        var root = RepoRoot();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src", "Carowinds.Passdown.Documents"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(sources);

        var notEmbedded = new Regex(@"\.(Thin|ExtraLight|Light|Medium|ExtraBold|Black|ExtraBlack)\(\s*\)|\.FontWeight\(|FontWeight\.", RegexOptions.CultureInvariant);
        foreach (var path in sources)
        {
            var match = notEmbedded.Match(File.ReadAllText(path));
            Assert.False(match.Success, $"{Path.GetFileName(path)} uses '{match.Value}', a Lato weight that is not embedded (Carowinds.Passdown.Documents.csproj EmbeddedLatoFace).");
        }
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void RenderedPdfs_ContainOnlyEmbeddedLatoFacesAndTheMathFont(AssociatePassdownTemplate template)
    {
        var dense = AssociateWorkspaceFixtures.DenseDay();
        var workspaces = new[]
        {
            AssociateWorkspaceFixtures.ExcessiveDay(),
            AssociateWorkspaceFixtures.PrivateBuyoutDay(),
            AssociateWorkspaceFixtures.LongDailyFeatureDay(),
            dense with
            {
                Draft = dense.Draft with
                {
                    DailyFeatureType = DailyFeatureType.UselessFactOfTheDay,
                    DailyFeatureText = @"Café ½-price “deal” ≥ 3 × €5 since \(\frac{a}{b} + \sqrt{x^2}\).",
                    OperationalNotes = [.. Enumerable.Range(1, 40).Select(i => $"Note {i}: Guest Services — jalapeño, Über, ™, … extended hours near Gate {i % 7}.")],
                },
            },
        };

        var allowed = EmbeddedFaces.Select(face => $"Lato-{face}").Append("STIXTwoMath-Regular").ToHashSet(StringComparer.Ordinal);
        var generator = new AssociatePassdownPdfGenerator();
        foreach (var workspace in workspaces)
        {
            var result = generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);
            Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);

            using var document = PdfDocument.Open(result.PdfBytes!);
            var fonts = document.GetPages().SelectMany(page => page.Letters).Select(letter => letter.FontName ?? string.Empty).Distinct().ToList();
            var embeddedFonts = fonts.Where(name => name.Contains('+', StringComparison.Ordinal)).Select(name => name[(name.IndexOf('+') + 1)..]).ToList();

            Assert.Contains("Lato-Regular", embeddedFonts);
            Assert.Contains("Lato-Bold", embeddedFonts);
            Assert.All(embeddedFonts, name => Assert.Contains(name, allowed));
        }
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (CarowindsPassdown.slnx) not found.");
    }
}
