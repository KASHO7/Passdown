using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// A read-only "Daily Admission — 4 products" row (spec Phase 4B: "do not build the full editable
/// Associate form yet" — this gives visible confirmation that dynamic product data is flowing
/// through without any per-product editing UI). Never lists a category that has zero rows — an
/// empty category is either invisible (spec §7.1: no hard-coded seasonal sections) or already
/// covered by <see cref="CategoryStatusPresentation"/>.
/// </summary>
public sealed record ProductCategorySummary(string CategoryDisplayName, int Count)
{
    public static IReadOnlyList<ProductCategorySummary> Build(IEnumerable<AssociateProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        return products
            .Where(p => p.Category is not null)
            .GroupBy(p => p.Category!.Value)
            .OrderBy(g => (int)g.Key)
            .Select(g => new ProductCategorySummary(SourceCategoryDisplay.FriendlyName(g.Key), g.Count()))
            .ToList();
    }
}
