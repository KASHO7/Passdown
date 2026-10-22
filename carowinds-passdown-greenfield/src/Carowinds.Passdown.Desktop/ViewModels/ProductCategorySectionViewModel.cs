using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One product category's editable section (spec Phase 4D Part B) — a grouping shell only, rebuilt
/// fresh each time <see cref="AssociateViewModel"/> applies a (re)composed workspace, wrapping
/// whichever <see cref="ProductRowViewModel"/> instances exist for the category plus an optional
/// status banner (Phase 4B Part E) for a category currently showing zero rows. Never fabricates a
/// row for an empty category — an empty, status-less category simply is not built.
/// </summary>
public sealed record ProductCategorySectionViewModel(
    string CategoryDisplayName,
    CategoryStatusPresentation? Status,
    IReadOnlyList<ProductRowViewModel> Products,
    string? SourceNoteText = null)
{
    /// <summary>Phase 25: official-source attribution for the Season Passes / Memberships groups.</summary>
    public bool HasSourceNote => !string.IsNullOrWhiteSpace(SourceNoteText);

    public bool HasStatus => Status is not null;

    public bool HasProducts => Products.Count > 0;
}
