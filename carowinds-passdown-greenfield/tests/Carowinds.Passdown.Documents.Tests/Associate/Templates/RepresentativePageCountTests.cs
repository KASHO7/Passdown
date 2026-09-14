using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain.Ert;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 17C §28/§29: representative day-shape fixtures (light/normal/busy-product/many-notes/
/// ERT-override/NoERT), rendered under all three templates, recording page count and proving no
/// content is clipped (spec: "never silently omit selected data" — Compact should generally equal or
/// outperform Classic on page count, but a genuinely busy passdown may still legitimately need two
/// pages for any template). Logs each scenario's per-template page count via <see cref="ITestOutputHelper"/>
/// so a `dotnet test` run's output doubles as the spec §28 metrics record, without needing a separate
/// throwaway diagnostic harness.
/// </summary>
public sealed class RepresentativePageCountTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    public static IEnumerable<object[]> Scenarios()
    {
        yield return ["A. Light day", (Func<AssociateWorkspace>)AssociateWorkspaceFixtures.SparseDay];
        yield return ["B. Normal day", (Func<AssociateWorkspace>)AssociateWorkspaceFixtures.NormalDay];
        yield return ["C. Busy product day", (Func<AssociateWorkspace>)AssociateWorkspaceFixtures.DenseDay];
        yield return ["D. Many notes", (Func<AssociateWorkspace>)ManyNotesDay];
        yield return ["E. ERT override day", (Func<AssociateWorkspace>)ErtOverrideDay];
        yield return ["F. NoERT day", (Func<AssociateWorkspace>)NoErtDay];
    }

    private static AssociateWorkspace ManyNotesDay() => AssociateWorkspaceFixtures.ExcessiveDay();

    private static AssociateWorkspace ErtOverrideDay() => AssociateWorkspaceFixtures.NormalDay() with
    {
        EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Override, new TimeOnly(10, 0), ["Copperhead Strike"], "Delayed opening"),
    };

    private static AssociateWorkspace NoErtDay() => AssociateWorkspaceFixtures.NormalDay() with
    {
        EarlyRideTime = new EffectiveErt(EffectiveErtKind.NoErt, EffectiveErtSource.Monthly, null, [], null),
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EveryScenario_GeneratesSuccessfullyUnderEveryTemplate_AndNeverDropsContent(string scenarioName, Func<AssociateWorkspace> fixture)
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(fixture(), GeneratedAt);

        var pageCounts = new Dictionary<AssociatePassdownTemplate, int>();

        foreach (var template in new[] { AssociatePassdownTemplate.Classic, AssociatePassdownTemplate.Compact, AssociatePassdownTemplate.Modern })
        {
            var result = Generator.Generate(model, template);
            Assert.True(result.Status == AssociatePassdownPdfStatus.Generated, $"{scenarioName}/{template} failed to generate.");
            Assert.NotNull(result.PdfBytes);

            var text = PdfInspector.ExtractAllText(result.PdfBytes!);
            foreach (var category in model.ProductCategories)
            {
                foreach (var product in category.Products)
                {
                    Assert.Contains(product.DisplayName, text);
                }
            }

            pageCounts[template] = result.PageCount;
        }

        output.WriteLine($"{scenarioName}: Classic={pageCounts[AssociatePassdownTemplate.Classic]} " +
            $"Compact={pageCounts[AssociatePassdownTemplate.Compact]} Modern={pageCounts[AssociatePassdownTemplate.Modern]}");

        // Spec §28: Compact should generally equal or outperform Classic — never required to strictly
        // beat it (a very light day fits one page under every template equally).
        Assert.True(pageCounts[AssociatePassdownTemplate.Compact] <= pageCounts[AssociatePassdownTemplate.Classic],
            $"{scenarioName}: Compact ({pageCounts[AssociatePassdownTemplate.Compact]} pages) used more pages than Classic ({pageCounts[AssociatePassdownTemplate.Classic]} pages).");
    }
}
