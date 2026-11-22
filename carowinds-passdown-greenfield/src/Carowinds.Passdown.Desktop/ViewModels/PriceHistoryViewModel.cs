using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Domain.Catalog;
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

    /// <summary>"Season Pass" or "Season Pass Add-On · Retired" — never color-only. Phase 26: a product known only through Personal Sync reads "… · Imported history".</summary>
    /// <remarks>Phase 31B: "… · Availability unconfirmed" for a dedicated-channel product the app cannot confirm either way; "… · Expired" for an ended one.</remarks>
    public string DetailText => Product.IsImportedOnly
        ? $"{FamilyText} · {PriceHistoryPresentation.ImportedOnlyDetailText}"
        : Product.Availability.Availability == ProductAvailability.Available ? FamilyText : $"{FamilyText} · {Product.Availability.Label}";

    /// <summary>Phase 31B: why the product reads as it does (tooltip), e.g. the ID.me-gated channel explanation.</summary>
    public string AvailabilityReason => Product.Availability.Reason;

    /// <summary>Phase 31B: the reason line is shown under the selected product only when it is not simply available.</summary>
    public bool IsAvailabilityNoteVisible => !Product.IsImportedOnly && Product.Availability.Availability != ProductAvailability.Available;

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
    private readonly IExternalLinkLauncher? _linkLauncher;
    private IReadOnlyList<PriceHistoryProductOption> _allProducts = [];
    private int _historyLoadVersion;
    private int _productsLoadVersion;

    /// <summary>
    /// Phase 34B-2: the synced observations read once per open (or refresh), so switching products does not re-read
    /// the whole synced table. Replaced on every open and every refresh — which happens whenever Personal Sync merges
    /// price observations or an official refresh changes the catalog — and dropped when the screen closes.
    /// </summary>
    private ISeasonPriceHistoryReader? _reader;

    /// <param name="dataChangeNotifier">Phase 26: when Personal Sync merges official price observations, an open screen refreshes in place.</param>
    /// <param name="linkLauncher">Phase 26: opens the selected product's verified official page (no link is offered without one).</param>
    public PriceHistoryViewModel(
        ISeasonPriceHistoryService service,
        ILogger<PriceHistoryViewModel>? logger = null,
        IDataChangeNotifier? dataChangeNotifier = null,
        IExternalLinkLauncher? linkLauncher = null,
        IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _linkLauncher = linkLauncher;

        if (dataChangeNotifier is not null)
        {
            var dispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();
            dataChangeNotifier.Changed += (_, change) =>
            {
                // Phase 34B-2: OfficialData too — an official refresh can change the Season Pass catalog the legacy
                // identity fold and the product list are built from, so the open screen re-reads (keeping selection).
                if (change.Kind is DataChangeKind.OfficialPriceHistory or DataChangeKind.OfficialData)
                {
                    dispatcher.Post(() => PendingRefresh = RefreshAsync());
                }
            };
        }
    }

    /// <summary>The most recent in-place refresh after a Personal Sync merge, for tests to await.</summary>
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>Phase 26: the selected product's verified official page, when its recorded source is an allowed official address.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourceLink))]
    public partial OfficialSourceLinkViewModel? SourceLink { get; private set; }

    public bool HasSourceLink => SourceLink is not null;

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
        await LoadProductsAsync(productKey, revealRetired: true);
    }

    /// <summary>
    /// Phase 26: re-reads the list and the selected product's history in place — the selected product,
    /// search, price-type filter and "Include retired products" all stay as the user left them. Does
    /// nothing while the screen is closed (the next open reads fresh data anyway). Local-only.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (!IsOpen)
        {
            return;
        }

        await LoadProductsAsync(SelectedProduct?.ProductKey, revealRetired: false);
    }

    private async Task LoadProductsAsync(string? productKey, bool revealRetired)
    {
        var version = ++_productsLoadVersion;
        _reader = null; // never answer from observations older than this load
        try
        {
            var reader = await _service.OpenReaderAsync();
            var products = await reader.ListTrackedProductsAsync();
            if (version != _productsLoadVersion)
            {
                return; // a newer open or refresh superseded this one
            }

            _reader = IsOpen ? reader : null;
            _allProducts = products.Select(p => new PriceHistoryProductOption(p)).ToList();
        }
        catch (Exception ex)
        {
            if (version != _productsLoadVersion)
            {
                return;
            }

            _logger.LogWarning(ex, "Season price history product list could not be loaded.");
            _reader = null;
            _allProducts = [];
            Products = [];
            SelectedProduct = null;
            Rows = [];
            StatusText = PriceHistoryPresentation.LoadErrorText;
            IsLoaded = true;
            return;
        }

        var requested = productKey is null ? null : _allProducts.FirstOrDefault(p => p.ProductKey == productKey);
        if (revealRetired && requested is { IsInactive: true } && !IncludeInactive)
        {
            IncludeInactive = true; // refilters through OnIncludeInactiveChanged
        }

        IsLoaded = true;
        ApplyFilter(preferredKey: requested?.ProductKey ?? SelectedProduct?.ProductKey, reloadHistory: !revealRetired);
        await PendingHistoryLoad;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            _reader = null; // release the cached observations; the next open reads fresh data
        }
    }

    partial void OnIncludeInactiveChanged(bool value) => ApplyFilter(SelectedProduct?.ProductKey);

    partial void OnSearchTextChanged(string? value) => ApplyFilter(SelectedProduct?.ProductKey);

    partial void OnSelectedFamilyFilterChanged(PriceHistoryFamilyFilter value) => ApplyFilter(SelectedProduct?.ProductKey);

    partial void OnSelectedProductChanged(PriceHistoryProductOption? value) => PendingHistoryLoad = LoadHistoryAsync(value);

    private void ApplyFilter(string? preferredKey, bool reloadHistory = false)
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
        else if (selection is null || reloadHistory)
        {
            // Phase 26: on a refresh the same product's history may have changed underneath.
            PendingHistoryLoad = LoadHistoryAsync(selection);
        }
    }

    private async Task LoadHistoryAsync(PriceHistoryProductOption? product)
    {
        var version = ++_historyLoadVersion;
        SourceLink = OfficialSourceLinkViewModel.TryCreate(product?.Product.OfficialSourceUrl, _linkLauncher);
        if (product is null)
        {
            Rows = [];
            AvailabilityText = null;
            StatusText = IsLoaded && _allProducts.Count > 0 ? "No products match." : null;
            return;
        }

        try
        {
            var reader = _reader;
            var entries = reader is null
                ? await _service.GetHistoryAsync(product.ProductKey)
                : await reader.GetHistoryAsync(product.ProductKey);
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
