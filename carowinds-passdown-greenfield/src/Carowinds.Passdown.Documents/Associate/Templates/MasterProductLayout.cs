using Carowinds.Passdown.Application.Associate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Compact's paired product-section layout (Phase 17D origin; Compact-only since Phase 17E §16.2 —
/// Classic and Modern use <see cref="SingleColumnProductLayout"/>).
/// Identifies categories by <see cref="PdfProductCategory.Section"/> -- never by
/// <see cref="PdfProductCategory.CategoryName"/> string matching -- so pairing can never silently break
/// if a display name changes. Daily Admission/Fast Lane share one row; Promotions/Season Passes share
/// a second row; every other category (SCarowinds, Add-Ons, Custom/Internal, and anything not covered
/// by an explicit pairing) renders full width, in the order it already arrived in
/// <paramref name="categories"/>. A pairing with only one side present renders that side alone in the
/// left column at column width (Phase 17F — previously it stretched to full row width, which pushed its
/// prices out of line with the paired sections). A pairing with both sides absent contributes nothing at all (spec §20's "zero reserved space").
///
/// Phase 17F: each pair row / full-width category is wrapped in
/// <see cref="PdfPrimitives.KeepSection"/> (keep small sections whole, guarantee a heading lands with
/// its first rows otherwise). Categories are still deliberately NOT wrapped in QuestPDF's
/// <c>ShowEntire()</c> here: unlike Operational
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
        float headingWithFirstRowsHeight,
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

            var firstLone = (dailyAdmission is null) != (fastLane is null) ? dailyAdmission ?? fastLane : null;
            var secondLone = (promotions is null) != (seasonPasses is null) ? promotions ?? seasonPasses : null;

            if (firstLone is not null && secondLone is not null)
            {
                // Phase 17F: each pairing has exactly one side present (e.g. Daily Admission without
                // Fast Lane, Season Passes without Promotions) — the two lone sections share one row,
                // in the same reading order, instead of stacking down the left column beside a blank
                // right half. Keeps Compact two-column on partial days.
                ComposePairRow(section, firstLone, secondLone, pairColumnGap, headingWithFirstRowsHeight, composeCategory);
            }
            else
            {
                ComposePairRow(section, dailyAdmission, fastLane, pairColumnGap, headingWithFirstRowsHeight, composeCategory);
                ComposePairRow(section, promotions, seasonPasses, pairColumnGap, headingWithFirstRowsHeight, composeCategory);
            }

            foreach (var category in fullWidth)
            {
                section.Item().KeepSection(category.Products.Count, headingWithFirstRowsHeight).Element(el => composeCategory(el, category));
            }
        });
    }

    private static void ComposePairRow(
        ColumnDescriptor column,
        PdfProductCategory? left,
        PdfProductCategory? right,
        float gap,
        float headingWithFirstRowsHeight,
        Action<IContainer, PdfProductCategory> composeCategory)
    {
        if (left is null && right is null)
        {
            return;
        }

        if (left is not null && right is not null)
        {
            // Phase 17F orphan control: the pair is kept/split as one unit, sized by its taller side.
            var tallerSide = Math.Max(left.Products.Count, right.Products.Count);
            column.Item().KeepSection(tallerSide, headingWithFirstRowsHeight).Row(row =>
            {
                row.RelativeItem().Element(el => composeCategory(el, left));
                row.ConstantItem(gap);
                row.RelativeItem().Element(el => composeCategory(el, right));
            });
            return;
        }

        // Phase 17F: a lone pair member stays at column width in the left column (it no longer
        // stretches across the full row), so its price column lines up with every other paired
        // section's instead of jumping to the far page edge. Purely horizontal — no vertical space is
        // reserved for the absent partner.
        var single = (left ?? right)!;
        column.Item().KeepSection(single.Products.Count, headingWithFirstRowsHeight).Row(row =>
        {
            row.RelativeItem().Element(el => composeCategory(el, single));
            row.ConstantItem(gap);
            row.RelativeItem();
        });
    }
}
