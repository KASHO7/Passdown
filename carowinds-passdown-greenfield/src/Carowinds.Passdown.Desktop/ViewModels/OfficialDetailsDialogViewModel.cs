using Carowinds.Passdown.Application.Links;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 31C: the in-app View details dialog for one product or official offer. Shows only what the
/// app already stored (no browser, no retrieval); official pages are offered as allowlisted links
/// inside the dialog. Opening or closing it changes no draft, price, availability or sync state.
/// </summary>
public sealed partial class OfficialDetailsDialogViewModel : ObservableObject
{
    private readonly IExternalLinkLauncher? _launcher;

    public OfficialDetailsDialogViewModel(IExternalLinkLauncher? launcher)
    {
        _launcher = launcher;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial OfficialDetailsPresentation? Details { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinks))]
    public partial IReadOnlyList<OfficialSourceLinkViewModel> Links { get; private set; } = [];

    /// <summary>Plain addresses, shown only when no link launcher is available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnlinkedSourceText))]
    public partial string? UnlinkedSourceText { get; private set; }

    public bool HasLinks => Links.Count > 0;

    public bool HasUnlinkedSourceText => UnlinkedSourceText is not null;

    public void Open(OfficialDetailsPresentation details)
    {
        ArgumentNullException.ThrowIfNull(details);

        // Only allowlisted official addresses are ever shown or opened (same policy as Phase 26/31).
        var official = details.SourceUrls
            .Where(url => OfficialSourceLinkPolicy.TryValidate(url, out _))
            .Distinct()
            .ToList();
        Links = official
            .Select(url => OfficialSourceLinkViewModel.TryCreate(url, _launcher, "Official page: " + OfficialSourceLinkPolicy.DisplayAddress(url)))
            .OfType<OfficialSourceLinkViewModel>()
            .ToList();
        UnlinkedSourceText = Links.Count == 0 && official.Count > 0
            ? "Official page: " + string.Join(", ", official.Select(OfficialSourceLinkPolicy.DisplayAddress))
            : null;
        Details = details;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;
}
