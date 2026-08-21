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
public partial class CustomProductRowViewModel : ObservableObject
{
    private readonly Action _notifyChanged;
    private readonly Action<CustomProductRowViewModel> _onRemove;
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

    public CustomProductRowViewModel(CustomProductEntry entry, Action notifyChanged, Action<CustomProductRowViewModel> onRemove)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(notifyChanged);
        ArgumentNullException.ThrowIfNull(onRemove);
        _notifyChanged = notifyChanged;
        _onRemove = onRemove;

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

    partial void OnDisplayNameTextChanged(string value) => _notifyChanged();

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
        _notifyChanged();
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
