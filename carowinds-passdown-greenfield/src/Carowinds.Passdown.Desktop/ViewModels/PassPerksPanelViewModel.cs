using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Phase 32: one choice in the Pass Perks month picker.</summary>
public sealed record PassPerkMonthOption(DateOnly Month, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Phase 32: one compact Pass Perk row with its View details action.</summary>
public sealed partial class PassPerkItemViewModel
{
    private readonly Action<OfficialDetailsPresentation>? _openDetails;

    public PassPerkItemViewModel(
        PassPerkItemPresentation presentation,
        Action<OfficialDetailsPresentation>? openDetails,
        string? contextText = null,
        Domain.PassPerks.PassPerkMarkerKind? markerKind = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        Presentation = presentation;
        _openDetails = openDetails;
        ContextText = contextText;
        MarkerKind = markerKind;
    }

    /// <summary>Phase 32A: Calendar only — the marker type this row belongs to (null in Products).</summary>
    public Domain.PassPerks.PassPerkMarkerKind? MarkerKind { get; }

    public bool IsQualifyingVisit => MarkerKind == Domain.PassPerks.PassPerkMarkerKind.QualifyingVisit;

    public bool IsRedemption => MarkerKind == Domain.PassPerks.PassPerkMarkerKind.Redemption;

    public bool IsDeadline => MarkerKind == Domain.PassPerks.PassPerkMarkerKind.Deadline;

    /// <summary>Phase 32A: "Qualifying visit", "Redemption / use" or "Booking deadline" (Calendar rows only).</summary>
    public string? MarkerKindText => MarkerKind switch
    {
        Domain.PassPerks.PassPerkMarkerKind.QualifyingVisit => "Qualifying visit",
        Domain.PassPerks.PassPerkMarkerKind.Redemption => "Redemption / use",
        Domain.PassPerks.PassPerkMarkerKind.Deadline => "Booking deadline",
        _ => null,
    };

    /// <summary>"Qualifying visit · Last day for qualifying visits" — type first, then the exact meaning.</summary>
    public string? HeadlineText => MarkerKindText is { } kind
        ? (ContextText is null ? kind : $"{kind} · {ContextText}")
        : ContextText;

    public bool HasHeadlineText => HeadlineText is not null;

    public PassPerkItemPresentation Presentation { get; }

    public string Title => Presentation.Title;

    public string TypeLabel => Presentation.TypeLabel;

    public string MetaText => Presentation.MetaText;

    /// <summary>Calendar only: why the perk is on the selected date ("Last day to redeem").</summary>
    public string? ContextText { get; }

    public bool HasContextText => ContextText is not null;

    public string? NoticeText => Presentation.NoticeText;

    public bool HasNoticeText => NoticeText is not null;

    public bool IsNotCurrent => Presentation.RowStatusLabel is not null;

    public string AccessibleSummary => HeadlineText is null ? Presentation.AccessibleSummary : $"{HeadlineText}: {Presentation.AccessibleSummary}";

    public bool CanViewDetails => _openDetails is not null;

    public string ViewDetailsAutomationName => $"View details for {Title}";

    [RelayCommand]
    private void ViewDetails() => _openDetails?.Invoke(Presentation.Details);
}

/// <summary>
/// Phase 32: the Products screen's Pass Perks panel. Reads only the cached official document
/// (<see cref="IPassPerksService"/>); choosing another month re-formats the cached data and never
/// retrieves online. Opening details uses the shared in-app View details dialog.
/// </summary>
public sealed partial class PassPerksPanelViewModel : ObservableObject
{
    private readonly IPassPerksService _service;
    private readonly Action<OfficialDetailsPresentation>? _openDetails;
    private readonly ILogger? _logger;
    private PassPerksOverview? _overview;
    private bool _rebuilding;

    public PassPerksPanelViewModel(IPassPerksService service, Action<OfficialDetailsPresentation>? openDetails, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _openDetails = openDetails;
        _logger = logger;
    }

    public string IndividualPassNote => PassPerksPresentation.IndividualPassNote;

    [ObservableProperty]
    public partial IReadOnlyList<PassPerkMonthOption> Months { get; private set; } = [];

    [ObservableProperty]
    public partial PassPerkMonthOption? SelectedMonth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    public partial IReadOnlyList<PassPerkItemViewModel> Items { get; private set; } = [];

    public bool HasItems => Items.Count > 0;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = PassPerksPresentation.NotCheckedText;

    [ObservableProperty]
    public partial bool IsStatusWarning { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyStateText))]
    public partial string? EmptyStateText { get; private set; }

    public bool HasEmptyStateText => EmptyStateText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryNoticeText))]
    public partial string? HistoryNoticeText { get; private set; }

    public bool HasHistoryNoticeText => HistoryNoticeText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgramTerms))]
    [NotifyCanExecuteChangedFor(nameof(ViewProgramTermsCommand))]
    public partial OfficialDetailsPresentation? ProgramTermsDetails { get; private set; }

    public bool HasProgramTerms => ProgramTermsDetails is not null && _openDetails is not null;

    /// <summary>The most recent load, so tests can await it.</summary>
    public Task PendingLoad { get; private set; } = Task.CompletedTask;

    public void StartLoad() => PendingLoad = LoadAsync();

    public async Task LoadAsync()
    {
        PassPerksOverview overview;
        try
        {
            overview = await _service.GetOverviewAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to read cached Pass Perks; showing them as not checked.");
            overview = PassPerksOverview.NotChecked(_overview?.OperatingDate ?? DateOnly.FromDateTime(DateTime.Today), DateTimeOffset.Now);
        }

        Apply(overview);
    }

    /// <summary>Applies a freshly read overview, keeping the chosen month when it is still offered.</summary>
    internal void Apply(PassPerksOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        _overview = overview;
        var current = new DateOnly(overview.OperatingDate.Year, overview.OperatingDate.Month, 1);
        var keep = SelectedMonth?.Month;

        _rebuilding = true;
        Months = overview.AvailableMonths.Select(m => new PassPerkMonthOption(m, MonthLabel(m, current))).ToList();
        SelectedMonth = Months.FirstOrDefault(m => m.Month == keep) ?? Months.FirstOrDefault(m => m.Month == current) ?? Months.FirstOrDefault();
        _rebuilding = false;
        Rebuild();
    }

    partial void OnSelectedMonthChanged(PassPerkMonthOption? value)
    {
        if (!_rebuilding)
        {
            Rebuild(); // formatting of cached data only — never a retrieval
        }
    }

    private void Rebuild()
    {
        if (_overview is null || SelectedMonth is null)
        {
            return;
        }

        var panel = PassPerksPresentation.BuildPanel(_overview, SelectedMonth.Month);
        StatusText = panel.StatusText;
        IsStatusWarning = panel.IsStatusWarning;
        EmptyStateText = panel.EmptyStateText;
        HistoryNoticeText = panel.HistoryNoticeText;
        Items = panel.Items.Select(i => new PassPerkItemViewModel(i, _openDetails)).ToList();
        ProgramTermsDetails = panel.ProgramTermsDetails;
    }

    private bool CanViewProgramTerms() => HasProgramTerms;

    [RelayCommand(CanExecute = nameof(CanViewProgramTerms))]
    private void ViewProgramTerms()
    {
        if (ProgramTermsDetails is { } details)
        {
            _openDetails?.Invoke(details);
        }
    }

    private static string MonthLabel(DateOnly month, DateOnly current) =>
        month == current ? $"{PassPerksPresentation.MonthText(month)} (this month)" : PassPerksPresentation.MonthText(month);
}
