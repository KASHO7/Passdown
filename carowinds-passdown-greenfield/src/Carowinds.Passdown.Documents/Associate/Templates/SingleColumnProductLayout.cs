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
    /// <summary>Composes every category full width, in display order. Phase 17F: no running section number is passed to the caller any more — Modern's "01/02/..." numbering was removed.</summary>
    public static void Compose(
        ColumnDescriptor column,
        IReadOnlyList<PdfProductCategory> categories,
        float rowSpacing,
        Action<IContainer, PdfProductCategory> composeCategory)
    {
        if (categories.Count == 0)
        {
            return;
        }

        var ordered = InDisplayOrder(categories);

        column.Item().Column(section =>
        {
            section.Spacing(rowSpacing);

            foreach (var category in ordered)
            {
                section.Item().Element(el => composeCategory(el, category));
            }
        });
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
