using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One editable manager-added custom/internal product (spec Phase 4D Parts F/G), wrapping a
/// draft-local <see cref="CustomProductEntry"/> — never backed by any official catalog identity.
/// <see cref="Id"/> is only ever a locally generated token, never a <c>CatalogProductKey</c>, and
/// this type is never constructed from or projected into <c>Application.Catalog</c>.
/// </summary>
public partial class CustomProductRowViewModel : ObservableObject, IPriceEditTarget
{
    private readonly Action _notifyChanged;
    private readonly Action<CustomProductRowViewModel> _onRemove;
    private readonly Action<IPriceEditTarget>? _openPriceEditor;
    private decimal? _price;

    public string Id { get; }

    [ObservableProperty]
    public partial string DisplayNameText { get; set; }

    [ObservableProperty]
    public partial string? PriceText { get; set; }

    [ObservableProperty]
    public partial string? PriceError { get; set; }

    [ObservableProperty]
    public partial string? NotesText { get; set; }

    [ObservableProperty]
    public partial bool IncludeOnPdf { get; set; }

    public CustomProductRowViewModel(CustomProductEntry entry, Action notifyChanged, Action<CustomProductRowViewModel> onRemove, Action<IPriceEditTarget>? openPriceEditor = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(notifyChanged);
        ArgumentNullException.ThrowIfNull(onRemove);
        _notifyChanged = notifyChanged;
        _onRemove = onRemove;
        _openPriceEditor = openPriceEditor;

        Id = entry.Id;
        _price = entry.Price;
        DisplayNameText = entry.DisplayName;
        PriceText = PriceFormatting.FormatOrNull(entry.Price);
        NotesText = entry.Notes;
        IncludeOnPdf = entry.IncludeOnPdf;
    }

    /// <summary>Converts the current fields back into the draft-persisted shape — a blank name falls back to a placeholder rather than persisting an unusable empty row (spec Part F).</summary>
    public CustomProductEntry ToEntry()
    {
        var name = string.IsNullOrWhiteSpace(DisplayNameText) ? "Custom item" : DisplayNameText.Trim();
        var notes = string.IsNullOrWhiteSpace(NotesText) ? null : NotesText.Trim();
        return new CustomProductEntry(Id, name, _price, notes, IncludeOnPdf);
    }

    partial void OnDisplayNameTextChanged(string value)
    {
        OnPropertyChanged(nameof(EditPriceAutomationName));
        _notifyChanged();
    }

    partial void OnNotesTextChanged(string? value) => _notifyChanged();

    partial void OnIncludeOnPdfChanged(bool value) => _notifyChanged();

    partial void OnPriceTextChanged(string? value)
    {
        if (!PriceFormatting.TryParse(value, out var parsed, out var error))
        {
            PriceError = error;
            return; // the last valid price is left untouched (spec Part E's rule applied to custom items too).
        }

        PriceError = null;
        _price = parsed;
        OnPropertyChanged(nameof(PriceDisplayText));
        OnPropertyChanged(nameof(HasPrice));
        OnPropertyChanged(nameof(EditPriceAutomationName));
        _notifyChanged();
    }

    // ------------------------------------------------------------------ Phase 35C: price as text + popover

    /// <summary>Phase 35C: shown in the display-price column when the custom item has no price (its price is optional).</summary>
    public const string NoPriceText = "No price";

    /// <summary>The last valid price.</summary>
    public decimal? Price => _price;

    public bool HasPrice => _price is not null;

    /// <summary>Phase 35C: the custom item's price as plain text, or <see cref="NoPriceText"/>.</summary>
    public string PriceDisplayText => PriceFormatting.FormatOrNull(_price) ?? NoPriceText;

    public string EditPriceAutomationName => $"Edit price for {ToEntry().DisplayName}, currently {PriceDisplayText}";

    /// <summary>Phase 35C: whether the row matches the Products search (view state only).</summary>
    [ObservableProperty]
    public partial bool IsSearchMatch { get; set; } = true;

    internal bool MatchesSearch(string query)
    {
        var needle = query.Trim();
        return new[] { DisplayNameText, NotesText, PriceDisplayText, "Custom & Internal Items" }
            .Any(text => text?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    }

    [RelayCommand]
    private void EditPrice() => _openPriceEditor?.Invoke(this);

    string IPriceEditTarget.PriceEditorName => ToEntry().DisplayName;

    string IPriceEditTarget.PriceEditorInitialText => PriceFormatting.FormatOrNull(_price) ?? string.Empty;

    string IPriceEditTarget.PriceEditorOfficialLine => "Custom item \u2014 entered here, not an official Carowinds price. Leave blank for no price.";

    string? IPriceEditTarget.PriceEditorPrintNote => null;

    bool IPriceEditTarget.PriceEditorCanUseOfficial => false;

    bool IPriceEditTarget.PriceEditorAllowsBlank => true;

    bool IPriceEditTarget.TryApplyPriceEdit(string? text, out string? error)
    {
        if (!PriceFormatting.TryParse(text, out var parsed, out error))
        {
            return false;
        }

        if (parsed == _price)
        {
            return true;
        }

        PriceText = PriceFormatting.FormatOrNull(parsed); // the existing custom price path (validate, keep, autosave).
        return true;
    }

    void IPriceEditTarget.UsePriceEditorOfficial()
    {
        // A custom item has no official price.
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
