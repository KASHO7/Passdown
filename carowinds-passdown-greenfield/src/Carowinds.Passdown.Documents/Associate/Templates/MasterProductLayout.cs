using Carowinds.Passdown.Application.Associate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// The one shared product-section layout algorithm every template renderer uses (Phase 17D §11/§12/
/// §13/§18 -- "all three templates use the SAME structural layout, only visual treatment changes").
/// Identifies categories by <see cref="PdfProductCategory.Section"/> -- never by
/// <see cref="PdfProductCategory.CategoryName"/> string matching -- so pairing can never silently break
/// if a display name changes. Daily Admission/Fast Lane share one row; Promotions/Season Passes share
/// a second row; every other category (SCarowinds, Add-Ons, Custom/Internal, and anything not covered
/// by an explicit pairing) renders full width, in the order it already arrived in
/// <paramref name="categories"/>. A pairing with only one side present renders that side alone at full
/// row width rather than leaving a blank half (spec §12's "the other section may expand") -- QuestPDF's
/// own single <c>RelativeItem</c> already fills the row, so no extra logic is needed for that case. A
/// pairing with both sides absent contributes nothing at all (spec §20's "zero reserved space").
///
/// Categories are deliberately NOT wrapped in QuestPDF's <c>ShowEntire()</c> here: unlike Operational
/// Notes (bounded, short lines), a category's row count is effectively unbounded -- manager-added
/// custom/internal products in particular have no catalog-size ceiling -- and <c>ShowEntire()</c>
/// throws <c>DocumentLayoutException</c> outright for any block taller than one full page, which would
/// turn "a lot of custom products" into a hard generation failure instead of the intended graceful
/// multi-page overflow (spec §17's "never truncate content"). A category is allowed to split across a
/// page boundary in the genuinely-excessive case; the §25 "isolated tiny page 2" failure mode this
/// phase actually needed to fix was specifically Operational Notes, handled at each renderer's own
/// <c>ComposeNotes</c>.
/// </summary>
internal static class MasterProductLayout
{
    public static void Compose(
        ColumnDescriptor column,
        IReadOnlyList<PdfProductCategory> categories,
        float pairColumnGap,
        float rowSpacing,
        Action<IContainer, PdfProductCategory> composeCategory)
    {
        if (categories.Count == 0)
        {
            return;
        }

        PdfProductCategory? Find(ProductDisplaySection section) =>
            categories.FirstOrDefault(category => category.Section == section);

        var dailyAdmission = Find(ProductDisplaySection.DailyAdmission);
        var fastLane = Find(ProductDisplaySection.FastLane);
        var promotions = Find(ProductDisplaySection.Promotions);
        var seasonPasses = Find(ProductDisplaySection.SeasonPasses);

        var fullWidth = categories.Where(category =>
            category.Section != ProductDisplaySection.DailyAdmission
            && category.Section != ProductDisplaySection.FastLane
            && category.Section != ProductDisplaySection.Promotions
            && category.Section != ProductDisplaySection.SeasonPasses).ToList();

        column.Item().Column(section =>
        {
            section.Spacing(rowSpacing);

            ComposePairRow(section, dailyAdmission, fastLane, pairColumnGap, composeCategory);
            ComposePairRow(section, promotions, seasonPasses, pairColumnGap, composeCategory);

            foreach (var category in fullWidth)
            {
                section.Item().Element(el => composeCategory(el, category));
            }
        });
    }

    private static void ComposePairRow(
        ColumnDescriptor column,
        PdfProductCategory? left,
        PdfProductCategory? right,
        float gap,
        Action<IContainer, PdfProductCategory> composeCategory)
    {
        if (left is null && right is null)
        {
            return;
        }

        if (left is not null && right is not null)
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(el => composeCategory(el, left));
                row.ConstantItem(gap);
                row.RelativeItem().Element(el => composeCategory(el, right));
            });
            return;
        }

        column.Item().Element(el => composeCategory(el, (left ?? right)!));
    }
}
