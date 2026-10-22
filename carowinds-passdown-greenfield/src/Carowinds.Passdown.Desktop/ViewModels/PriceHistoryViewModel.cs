using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>One product in the Price History picker.</summary>
public sealed record PriceHistoryProductOption(PriceHistoryProduct Product)
{
    public string ProductKey => Product.ProductKey;

    public string DisplayName => Product.DisplayName;

    public string FamilyText => PriceHistoryPresentation.FamilyText(Product.Family);

    public bool IsInactive => !Product.IsActive;

    /// <summary>"Season Pass" or "Season Pass Add-On · Retired" — never color-only.</summary>
    public string DetailText => IsInactive ? $"{FamilyText} · Retired" : FamilyText;

    public PriceHistoryFamily Family => Product.Family;

    public string AutomationName => $"{DisplayName}, {DetailText}";
}

/// <summary>
/// Phase 20A: the focused, read-only Season Price History screen, opened from Associate → Products
/// (a tracked row's "Price history" action, or the section's "Season pass price history" entry).
/// Lists only tracked Season Passes / Season Pass Add-Ons; retired ones stay reachable through
/// "Include retired products" without ever returning to current pricing. Local-only — it never
/// triggers a retrieval, and a failure to load is shown inline without affecting anything else.
/// </summary>
public partial class PriceHistoryViewModel : ObservableObject
{
    private readonly ISeasonPriceHistoryService _service;
    private readonly ILogger _logger;
    private IReadOnlyList<PriceHistoryProductOption> _allProducts = [];
    private int _historyLoadVersion;

    public PriceHistoryViewModel(ISeasonPriceHistoryService service, ILogger<PriceHistoryViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    /// <summary>Phase 25: the "Price type" filter choices (all, Season Passes, renewals, Memberships, initiation fees, add-ons).</summary>
    public IReadOnlyList<PriceHistoryFamilyFilter> FamilyFilters => PriceHistoryPresentation.FamilyFilters;

    [ObservableProperty]
    public partial PriceHistoryFamilyFilter SelectedFamilyFilter { get; set; } = PriceHistoryPresentation.FamilyFilters[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProducts))]
    [NotifyPropertyChangedFor(nameof(ShowNoProducts))]
    public partial IReadOnlyList<PriceHistoryProductOption> Products { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial PriceHistoryProductOption? SelectedProduct { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<PriceHistoryRowPresentation> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial string? AvailabilityText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoProducts))]
    public partial bool IsLoaded { get; private set; }

    public bool HasProducts => Products.Count > 0;

    public bool ShowNoProducts => IsLoaded && _allProducts.Count == 0;

    public bool HasSelection => SelectedProduct is not null;

    public bool HasRows => Rows.Count > 0;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public string TimingNoteText => PriceHistoryPresentation.TimingNoteText;

    public string NoProductsText => PriceHistoryPresentation.NoProductsText;

    /// <summary>The most recent history load, for tests to await deterministically.</summary>
    public Task PendingHistoryLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Opens the screen, optionally preselecting <paramref name="productKey"/> (a retired product turns on "Include retired products").</summary>
    public async Task OpenAsync(string? productKey)
    {
        IsOpen = true;
        IsLoaded = false;
        StatusText = null;

        try
        {
            var products = await _service.ListTrackedProductsAsync();
            _allProducts = products.Select(p => new PriceHistoryProductOption(p)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Season price history product list could not be loaded.");
            _allProducts = [];
            Products = [];
            SelectedProduct = null;
            Rows = [];
            StatusText = PriceHistoryPresentation.LoadErrorText;
            IsLoaded = true;
            return;
        }

        var requested = productKey is null ? null : _allProducts.FirstOrDefault(p => p.ProductKey == productKey);
        if (requested is { IsInactive: true } && !IncludeInactive)
        {
            IncludeInactive = true; // refilters through OnIncludeInactiveChanged
        }

        IsLoaded = true;
        ApplyFilter(preferredKey: requested?.ProductKey ?? SelectedProduct?.ProductKey);
        await PendingHistoryLoad;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnIncludeInactiveChanged(bool value) => ApplyFilter(SelectedProduct?.ProductKey);

    partial void OnSearchTextChanged(string? value) => ApplyFilter(SelectedProduct?.ProductKey);

    partial void OnSelectedFamilyFilterChanged(PriceHistoryFamilyFilter value) => ApplyFilter(SelectedProduct?.ProductKey);

    partial void OnSelectedProductChanged(PriceHistoryProductOption? value) => PendingHistoryLoad = LoadHistoryAsync(value);

    private void ApplyFilter(string? preferredKey)
    {
        var search = SearchText?.Trim();
        var filtered = _allProducts
            .Where(p => IncludeInactive || !p.IsInactive)
            .Where(p => SelectedFamilyFilter?.Family is not { } family || p.Family == family)
            .Where(p => string.IsNullOrEmpty(search) || p.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Products = filtered;

        var selection = filtered.FirstOrDefault(p => p.ProductKey == preferredKey) ?? filtered.FirstOrDefault();
        if (!Equals(selection, SelectedProduct))
        {
            SelectedProduct = selection;
        }
        else if (selection is null)
        {
            PendingHistoryLoad = LoadHistoryAsync(null);
        }
    }

    private async Task LoadHistoryAsync(PriceHistoryProductOption? product)
    {
        var version = ++_historyLoadVersion;
        if (product is null)
        {
            Rows = [];
            AvailabilityText = null;
            StatusText = IsLoaded && _allProducts.Count > 0 ? "No products match." : null;
            return;
        }

        try
        {
            var entries = await _service.GetHistoryAsync(product.ProductKey);
            if (version != _historyLoadVersion)
            {
                return; // a newer selection superseded this load
            }

            Rows = PriceHistoryPresentation.BuildRows(entries, product.Product.Unit);
            AvailabilityText = entries.Count == 0 ? null : PriceHistoryPresentation.AvailabilityText(entries);
            StatusText = entries.Count switch
            {
                0 => PriceHistoryPresentation.NoStatesText,
                1 => PriceHistoryPresentation.NoChangesYetText,
                _ => null,
            };
        }
        catch (Exception ex)
        {
            if (version != _historyLoadVersion)
            {
                return;
            }

            _logger.LogWarning(ex, "Season price history could not be loaded for {ProductKey}.", product.ProductKey);
            Rows = [];
            AvailabilityText = null;
            StatusText = PriceHistoryPresentation.LoadErrorText;
        }
    }
}
