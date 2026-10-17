using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Domain.Shows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 21C: one selectable show in the Associate Shows section. Wraps the Application-reconciled
/// <see cref="AssociateShowRow"/>; the only thing the supervisor changes here is <see cref="IsSelected"/>
/// (all of a selected show's verified times print). Rebuilt only when the parent rebuilds from a newly
/// composed workspace; a click mutates this instance and schedules the parent's autosave.
/// </summary>
public partial class AssociateShowRowViewModel : ObservableObject
{
    private readonly Action _notifyChanged;
    private bool _suppressNotify;

    public AssociateShowRowViewModel(AssociateShowRow row, Action notifyChanged)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _notifyChanged = notifyChanged ?? throw new ArgumentNullException(nameof(notifyChanged));
        _suppressNotify = true;
        IsSelected = row.IsSelected;
        _suppressNotify = false;
    }

    public AssociateShowRow Row { get; }

    public string Name => Row.Name;

    public string TimesText => ShowTimes.FormatList(Row.Times);

    public string? Venue => Row.Venue;

    public bool HasVenue => Row.Venue is not null;

    /// <summary>Why a selected row is flagged — never shown for an ordinary current row.</summary>
    public string? StateNote => Row.State switch
    {
        AssociateShowRowState.TimesChanged => "Official times changed since this show was selected.",
        AssociateShowRowState.NoLongerListed => "No longer on the official schedule for this date — won't print.",
        AssociateShowRowState.NotOnCurrentSchedule => "Not on the current official schedule — won't print.",
        AssociateShowRowState.SavedCopy => "Official schedule unavailable — using the times saved when selected.",
        _ => null,
    };

    public bool HasStateNote => StateNote is not null;

    public string AccessibleName => $"Include {Name} on the passdown, {TimesText}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_suppressNotify)
        {
            _notifyChanged();
        }
    }

    /// <summary>Sets the selection as one edit (Select all / Clear).</summary>
    internal void SetSelected(bool value) => IsSelected = value;
}
