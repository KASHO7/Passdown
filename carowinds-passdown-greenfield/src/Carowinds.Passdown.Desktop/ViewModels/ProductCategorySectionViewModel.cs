using System.ComponentModel;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One product category's editable section (spec Phase 4D Part B) — a grouping shell only, rebuilt
/// fresh each time <see cref="AssociateViewModel"/> applies a (re)composed workspace, wrapping
/// whichever <see cref="ProductRowViewModel"/> instances exist for the category plus an optional
/// status banner (Phase 4B Part E) for a category currently showing zero rows. Never fabricates a
/// row for an empty category — an empty, status-less category simply is not built.
///
/// Phase 35C: the section heading is a disclosure ("Daily Admission · 3 items · 3 included") that can
/// collapse the rows (view state only — the parent remembers it per category for the session; it never
/// changes what prints), and the Products search filters rows without touching draft data.
/// </summary>
public sealed partial class ProductCategorySectionViewModel : ObservableObject
{
    private readonly Action<ProductCategorySectionViewModel>? _expandedChanged;

    public ProductCategorySectionViewModel(
        string CategoryDisplayName,
        CategoryStatusPresentation? Status,
        IReadOnlyList<ProductRowViewModel> Products,
        string? SourceNoteText = null,
        OfficialSourceLinkViewModel? SourceLink = null,
        bool isExpanded = true,
        Action<ProductCategorySectionViewModel>? expandedChanged = null)
    {
        this.CategoryDisplayName = CategoryDisplayName;
        this.Status = Status;
        this.Products = Products;
        this.SourceNoteText = SourceNoteText;
        this.SourceLink = SourceLink;
        IsExpanded = isExpanded;
        _expandedChanged = expandedChanged;
        foreach (var row in Products)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    public string CategoryDisplayName { get; }

    public CategoryStatusPresentation? Status { get; }

    public IReadOnlyList<ProductRowViewModel> Products { get; }

    public string? SourceNoteText { get; }

    public OfficialSourceLinkViewModel? SourceLink { get; }

    /// <summary>Phase 25: official-source attribution for the Season Passes / Memberships groups.</summary>
    public bool HasSourceNote => !string.IsNullOrWhiteSpace(SourceNoteText);

    /// <summary>Phase 26: a clickable official page for the same groups, only when the verified source URL is allowlisted.</summary>
    public bool HasSourceLink => SourceLink is not null;

    public bool HasStatus => Status is not null;

    public bool HasProducts => Products.Count > 0;

    /// <summary>Phase 35C: whether the rows are shown (a search shows matching rows of a collapsed section too).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodyVisible))]
    [NotifyPropertyChangedFor(nameof(HeaderAccessibleName))]
    public partial bool IsExpanded { get; set; }

    /// <summary>Phase 35C: a Products search is active (the summary counts matching rows).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodyVisible))]
    [NotifyPropertyChangedFor(nameof(IsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyPropertyChangedFor(nameof(HeaderAccessibleName))]
    public partial bool IsSearchActive { get; private set; }

    public bool IsBodyVisible => IsExpanded || IsSearchActive;

    /// <summary>Hidden only while a search matches none of its rows.</summary>
    public bool IsSectionVisible => !IsSearchActive || VisibleCount > 0;

    public int VisibleCount => IsSearchActive ? Products.Count(row => row.IsSearchMatch) : Products.Count;

    public int IncludedCount => Products.Count(row => row.IsSearchMatch && row.CanIncludeOnPdf && row.IncludeOnPdf);

    /// <summary>"3 items · 2 included" — the included count follows the checkboxes live.</summary>
    public string SummaryText => !HasProducts
        ? "No items"
        : $"{VisibleCount} {(VisibleCount == 1 ? "item" : "items")} · {IncludedCount} included";

    public string HeaderAccessibleName => $"{CategoryDisplayName}, {SummaryText}, {(IsBodyVisible ? "expanded" : "collapsed")}";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value) => _expandedChanged?.Invoke(this);

    /// <summary>Phase 35C: filters rows by <paramref name="query"/> (blank = all rows). View state only.</summary>
    internal void ApplySearch(string? query)
    {
        var active = !string.IsNullOrWhiteSpace(query);
        foreach (var row in Products)
        {
            row.IsSearchMatch = !active || row.MatchesSearch(query!, CategoryDisplayName);
        }

        IsSearchActive = active;
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsSectionVisible));
        OnPropertyChanged(nameof(HeaderAccessibleName));
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductRowViewModel.IncludeOnPdf))
        {
            OnPropertyChanged(nameof(IncludedCount));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(HeaderAccessibleName));
        }
    }
}
