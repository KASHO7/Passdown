using Carowinds.Passdown.Application.Associate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17E's single-column product layout — every category rendered full width, one after another,
/// never paired side by side. Used by Classic (spec Part 2: "no two-column product matrix") and Modern
/// (spec Part 4: "mostly single-column product sections") — Compact alone keeps the paired layout via
/// <see cref="MasterProductLayout"/>, so the two composition strategies genuinely differ rather than
/// sharing one algorithm restyled per template. Categories still render in the same fixed order as
/// <see cref="MasterProductLayout"/> (Daily Admission, Fast Lane, Promotions, Season Passes, then
/// everything else in arrival order) so the two layouts never disagree on section ordering — only on
/// whether two sections share a row.
/// </summary>
internal static class SingleColumnProductLayout
{
    /// <summary>Composes every category full width, in display order, and returns how many were composed — the caller can use that count to continue a running section number (Modern's numbered sections) into whatever follows (Operational Notes).</summary>
    public static int Compose(
        ColumnDescriptor column,
        IReadOnlyList<PdfProductCategory> categories,
        float rowSpacing,
        Action<IContainer, PdfProductCategory, int> composeCategory)
    {
        if (categories.Count == 0)
        {
            return 0;
        }

        var ordered = InDisplayOrder(categories);

        column.Item().Column(section =>
        {
            section.Spacing(rowSpacing);

            for (var index = 0; index < ordered.Count; index++)
            {
                var category = ordered[index];
                var number = index + 1;
                section.Item().Element(el => composeCategory(el, category, number));
            }
        });

        return ordered.Count;
    }

    private static List<PdfProductCategory> InDisplayOrder(IReadOnlyList<PdfProductCategory> categories)
    {
        var used = new HashSet<PdfProductCategory>(ReferenceEqualityComparer.Instance);
        var ordered = new List<PdfProductCategory>(categories.Count);

        void AddIfPresent(ProductDisplaySection section)
        {
            var match = categories.FirstOrDefault(category => category.Section == section);
            if (match is not null && used.Add(match))
            {
                ordered.Add(match);
            }
        }

        AddIfPresent(ProductDisplaySection.DailyAdmission);
        AddIfPresent(ProductDisplaySection.FastLane);
        AddIfPresent(ProductDisplaySection.Promotions);
        AddIfPresent(ProductDisplaySection.SeasonPasses);

        foreach (var category in categories)
        {
            if (used.Add(category))
            {
                ordered.Add(category);
            }
        }

        return ordered;
    }
}
