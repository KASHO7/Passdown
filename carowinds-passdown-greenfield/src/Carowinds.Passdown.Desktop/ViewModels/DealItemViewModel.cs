using Carowinds.Passdown.Application.Links;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Phase 31: one official deal row on the Products screen, with a clickable link per official page it was verified on.</summary>
public sealed class DealItemViewModel
{
    public DealItemViewModel(DealItemPresentation presentation, IExternalLinkLauncher? launcher)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        Presentation = presentation;
        Links = presentation.SourceUrls
            .Select(url => OfficialSourceLinkViewModel.TryCreate(url, launcher, "Official page: " + OfficialSourceLinkPolicy.DisplayAddress(url)))
            .OfType<OfficialSourceLinkViewModel>()
            .ToList();
        SourceText = string.Join(", ", presentation.SourceUrls.Select(OfficialSourceLinkPolicy.DisplayAddress));
    }

    public DealItemPresentation Presentation { get; }

    public string Title => Presentation.Title;

    public string? Badge => Presentation.Badge;

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public string CategoryText => Presentation.CategoryText;

    public string? PriceText => Presentation.PriceText;

    public bool HasPriceText => PriceText is not null;

    public string? RegularPriceText => Presentation.RegularPriceText;

    public bool HasRegularPriceText => RegularPriceText is not null;

    public string? SavingsText => Presentation.SavingsText;

    public bool HasSavingsText => SavingsText is not null;

    public IReadOnlyList<string> OptionLines => Presentation.OptionLines;

    public IReadOnlyList<string> DateLines => Presentation.DateLines;

    public IReadOnlyList<string> TermLines => Presentation.TermLines;

    public bool HasTermLines => TermLines.Count > 0;

    public IReadOnlyList<string> WarningLines => Presentation.WarningLines;

    public string StatusText => Presentation.StatusText;

    public string VerifiedText => Presentation.VerifiedText;

    /// <summary>Plain-text source addresses, shown when no link launcher is available (e.g. in tests).</summary>
    public string SourceText { get; }

    public IReadOnlyList<OfficialSourceLinkViewModel> Links { get; }

    public string AccessibleSummary => string.Join(". ", new[] { Title, PriceText, StatusText }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Phase 31: a current-catalog product shown under past-season offers (not current, not exported).</summary>
public sealed record PastSeasonProductViewModel(string DisplayName, string OfficialPriceText, string Note);
