using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 35C: a row whose display price is edited through the anchored price popover — an official
/// product (<see cref="ProductRowViewModel"/>, a draft-local override of the official price) or a custom
/// item (<see cref="CustomProductRowViewModel"/>, its own optional price). The row keeps its existing
/// parse/validate/apply path; the popover only stages text and hands it over on Save.
/// </summary>
public interface IPriceEditTarget
{
    /// <summary>The product name the editor is labelled with.</summary>
    string PriceEditorName { get; }

    /// <summary>The value the editor opens with — the current effective display value as an editable amount.</summary>
    string PriceEditorInitialText { get; }

    /// <summary>"Official price: $50.00", or the custom-item wording; never claims a manual value is official.</summary>
    string PriceEditorOfficialLine { get; }

    /// <summary>A note on how the saved amount prints (starting-price rows print "From $x"); null otherwise.</summary>
    string? PriceEditorPrintNote { get; }

    /// <summary>Whether a manual override exists that "Use official price" can clear.</summary>
    bool PriceEditorCanUseOfficial { get; }

    /// <summary>Whether a blank value is a valid save (a custom item's price is optional; an official row's override is not).</summary>
    bool PriceEditorAllowsBlank { get; }

    /// <summary>
    /// Validates <paramref name="text"/> with the row's existing rules and, when valid and different, applies it
    /// through the row's existing override path (autosave + preview refresh). Invalid text changes nothing.
    /// </summary>
    bool TryApplyPriceEdit(string? text, out string? error);

    /// <summary>Clears the manual override through the row's existing reset command.</summary>
    void UsePriceEditorOfficial();
}

/// <summary>
/// Phase 35C: the one anchored "Edit display price" popover on Products (docs/phase35c-products-pricing-redesign.md).
/// Exactly one editor exists, so only one product can be edited at a time; opening another product replaces the
/// pending edit without saving it. Cancel, Escape, a click outside, a date switch, a section switch, a list
/// rebuild and scrolling all close it without changing anything. Save validates through the row's existing
/// rules and closes only after the value is applied; invalid input stays in the editor with its message.
/// </summary>
public partial class PriceEditorViewModel : ObservableObject
{
    /// <summary>The message shown when a blank value is saved on an official row.</summary>
    public const string BlankValueMessage = "Enter a display price, or choose Use official price.";

    /// <summary>The message shown when the edited product or date is no longer the one on screen.</summary>
    public const string StaleTargetMessage = "The date or product list changed, so this edit was not saved.";

    private readonly Func<IPriceEditTarget, DateOnly, bool> _isTargetCurrent;
    private DateOnly _operatingDate;
    private bool _suppressCloseHandling;

    public PriceEditorViewModel(Func<IPriceEditTarget, DateOnly, bool> isTargetCurrent)
    {
        ArgumentNullException.ThrowIfNull(isTargetCurrent);
        _isTargetCurrent = isTargetCurrent;
    }

    /// <summary>Raised after the editor closes; the argument says whether a value was saved or cleared.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>Two-way bound to the popup. A light dismiss (click outside) sets it false, which cancels.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetName))]
    [NotifyPropertyChangedFor(nameof(OfficialLine))]
    [NotifyPropertyChangedFor(nameof(PrintNote))]
    [NotifyPropertyChangedFor(nameof(HasPrintNote))]
    [NotifyPropertyChangedFor(nameof(CanUseOfficial))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyPropertyChangedFor(nameof(InputAccessibleName))]
    public partial IPriceEditTarget? Target { get; private set; }

    /// <summary>The staged text. Nothing is applied until Save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? EditText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; private set; }

    /// <summary>A short message left on the Products page after an edit was refused because its date/product changed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; private set; }

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>The operating date the open editor belongs to (for tests and diagnostics).</summary>
    public DateOnly? OperatingDate => Target is null ? null : _operatingDate;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public string TargetName => Target?.PriceEditorName ?? string.Empty;

    public string OfficialLine => Target?.PriceEditorOfficialLine ?? string.Empty;

    public string? PrintNote => Target?.PriceEditorPrintNote;

    public bool HasPrintNote => !string.IsNullOrEmpty(PrintNote);

    public bool CanUseOfficial => Target?.PriceEditorCanUseOfficial == true;

    public string AccessibleName => $"Edit display price for {TargetName}";

    public string InputAccessibleName => $"Display price for {TargetName}";

    /// <summary>Opens the editor on <paramref name="target"/> for <paramref name="operatingDate"/>, discarding any other pending edit.</summary>
    public void Open(IPriceEditTarget target, DateOnly operatingDate)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsOpen)
        {
            Close(committed: false); // one editor at a time — the previous pending edit is discarded, never saved.
        }

        _operatingDate = operatingDate;
        Notice = null;
        Target = target;
        EditText = target.PriceEditorInitialText;
        ErrorText = null;
        IsOpen = true;
    }

    /// <summary>Closes without changing anything when the editor is open on anything (date/section switch, rebuild, scroll).</summary>
    public void CancelIfOpen()
    {
        if (IsOpen)
        {
            Close(committed: false);
        }
    }

    /// <summary>Closes without changing anything when the editor is open on <paramref name="target"/> (e.g. that custom item was removed).</summary>
    public void CancelIfTarget(IPriceEditTarget target)
    {
        if (IsOpen && ReferenceEquals(Target, target))
        {
            Close(committed: false);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!IsOpen || Target is not { } target)
        {
            return;
        }

        if (!_isTargetCurrent(target, _operatingDate))
        {
            Notice = StaleTargetMessage; // never applied to whatever row or date is on screen now.
            Close(committed: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(EditText) && !target.PriceEditorAllowsBlank)
        {
            ErrorText = BlankValueMessage;
            return;
        }

        if (!target.TryApplyPriceEdit(EditText, out var error))
        {
            ErrorText = error; // the editor stays open with the typed text and the message.
            return;
        }

        Close(committed: true);
    }

    [RelayCommand]
    private void Cancel() => CancelIfOpen();

    [RelayCommand]
    private void UseOfficial()
    {
        if (!IsOpen || Target is not { } target || !target.PriceEditorCanUseOfficial)
        {
            return;
        }

        if (!_isTargetCurrent(target, _operatingDate))
        {
            Notice = StaleTargetMessage;
            Close(committed: false);
            return;
        }

        target.UsePriceEditorOfficial();
        Close(committed: true);
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (!value && !_suppressCloseHandling && Target is not null)
        {
            Close(committed: false); // light dismiss from the popup: discard the staged text.
        }
    }

    private void Close(bool committed)
    {
        _suppressCloseHandling = true;
        try
        {
            IsOpen = false;
        }
        finally
        {
            _suppressCloseHandling = false;
        }

        Target = null;
        EditText = null;
        ErrorText = null;

        Closed?.Invoke(this, committed);
    }
}
