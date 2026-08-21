using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 4B's Associate Passdown screen state. Depends only on <see cref="IAssociatePassdownService"/>
/// (application-layer) and <see cref="IClock"/> (application-layer) — it never queries a
/// repository, calls HTTP/Playwright, or recomputes effective-hours arithmetic; every value it
/// exposes is either passed straight through from <see cref="AssociateWorkspace"/> or formatted by
/// the pure <c>Presentation</c> builders (see <see cref="ParkHoursPresentation"/> and friends).
/// </summary>
public partial class AssociateViewModel : ViewModelBase
{
    private readonly IAssociatePassdownService _service;
    private readonly ILogger<AssociateViewModel>? _logger;
    private int _loadVersion;
    private Task _pendingLoad = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(CategoryStatuses))]
    [NotifyPropertyChangedFor(nameof(ProductSummaries))]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    [NotifyPropertyChangedFor(nameof(HasCategoryStatuses))]
    [NotifyPropertyChangedFor(nameof(HasProductSummaries))]
    public partial AssociateWorkspace? Workspace { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDateOffset))]
    public partial DateOnly SelectedDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    public partial bool IsLoadingDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    public partial bool IsCheckingOnline { get; set; }

    /// <summary>Concise in-progress copy such as "Checked Fast Lane — 3 of 4 complete" (spec Part F). Null while not refreshing.</summary>
    [ObservableProperty]
    public partial string? RefreshStatusMessage { get; set; }

    /// <summary>The unobtrusive post-refresh summary, e.g. "Updated successfully." (spec Part F). Cleared at the start of the next check.</summary>
    [ObservableProperty]
    public partial string? RefreshResultMessage { get; set; }

    [ObservableProperty]
    public partial bool RefreshHadFailures { get; set; }

    /// <summary>Set when a local date load itself fails unexpectedly (e.g. a corrupt local database) — distinct from a normal, expected "Check Online" failure, which is reported via <see cref="RefreshResultMessage"/> instead.</summary>
    [ObservableProperty]
    public partial string? LoadErrorMessage { get; set; }

    public AssociateViewModel(IAssociatePassdownService service, IClock clock, ILogger<AssociateViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _logger = logger;

        // "Today" in the park's own wall-clock time (spec §6.5/§6.6 use the same Eastern
        // convention for "today") rather than the host machine's arbitrary local time zone.
        SelectedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);
    }

    public bool IsBusy => IsLoadingDate || IsCheckingOnline;

    /// <summary>
    /// A <see cref="DateTimeOffset"/>-shaped view of <see cref="SelectedDate"/> for Avalonia's
    /// <c>DatePicker</c> (spec §10.2's "professional Windows control"), which has no
    /// <see cref="DateOnly"/>-typed control. Setting it goes through the same
    /// <see cref="SelectedDate"/> setter as every other date change, so it is still exactly one
    /// local-only load path (spec §6.3) — this is purely a binding-shape adapter, not a second way
    /// to change the date.
    /// </summary>
    public DateTimeOffset? SelectedDateOffset
    {
        get => new(SelectedDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        set
        {
            if (value is { } picked)
            {
                SelectedDate = DateOnly.FromDateTime(picked.Date);
            }
        }
    }

    public bool HasWorkspace => Workspace is not null;

    public ParkHoursPresentation? ParkHours => Workspace is { } workspace ? ParkHoursPresentation.Build(workspace.ParkHours) : null;

    public IReadOnlyList<CategoryStatusPresentation> CategoryStatuses
    {
        get
        {
            if (Workspace is not { } workspace)
            {
                return [];
            }

            return workspace.CategoryStatuses
                .Select(status => CategoryStatusPresentation.Build(status, workspace.Products.Count(p => p.Category == status.Category)))
                .Where(presentation => presentation is not null)
                .Select(presentation => presentation!)
                .ToList();
        }
    }

    public IReadOnlyList<ProductCategorySummary> ProductSummaries =>
        Workspace is { } workspace ? ProductCategorySummary.Build(workspace.Products) : [];

    public bool HasCategoryStatuses => CategoryStatuses.Count > 0;

    public bool HasProductSummaries => ProductSummaries.Count > 0;

    public SaveStatusPresentation? SaveStatus => Workspace is { } workspace ? SaveStatusPresentation.Build(workspace.SaveStatus) : null;

    /// <summary>
    /// The in-flight (or most recently finished) local load, so a test can await it deterministically
    /// instead of racing the fire-and-forget task <see cref="OnSelectedDateChanged"/> starts. The
    /// constructor's own initial <see cref="SelectedDate"/> assignment goes through this exact same
    /// path — there is no separate "initial load" method — so production startup issues exactly one
    /// local load, not two.
    /// </summary>
    public Task PendingLoad => _pendingLoad;

    /// <summary>
    /// Spec §6.3 "fast date changes": every date change — including the very first one, via the
    /// constructor's initial assignment — runs through this single local-only path. It never calls
    /// <see cref="IAssociatePassdownService.CheckAssociateDateOnlineAsync"/> or anything else that
    /// could reach the network.
    /// </summary>
    partial void OnSelectedDateChanged(DateOnly value) => _pendingLoad = LoadDateAsync(value);

    private async Task LoadDateAsync(DateOnly date)
    {
        var version = ++_loadVersion;
        IsLoadingDate = true;
        LoadErrorMessage = null;
        try
        {
            var workspace = await _service.LoadAssociateDateAsync(date);
            if (version != _loadVersion)
            {
                return; // a newer date was selected before this one finished composing.
            }

            Workspace = workspace;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load the local Associate draft/cache for {OperatingDate}.", date);
            if (version == _loadVersion)
            {
                LoadErrorMessage = "This date could not be loaded from your saved information. Try selecting it again.";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoadingDate = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckOnline))]
    private async Task CheckOnlineAsync()
    {
        var date = SelectedDate;
        IsCheckingOnline = true;
        RefreshResultMessage = null;
        RefreshHadFailures = false;
        RefreshStatusMessage = RefreshMessages.InProgressDefault;

        var progress = new Progress<RefreshProgress>(p => RefreshStatusMessage = RefreshMessages.Progress(p));

        try
        {
            var result = await _service.CheckAssociateDateOnlineAsync(date, progress);
            Workspace = result.Workspace;
            RefreshResultMessage = RefreshMessages.Summary(result.Summary);
            RefreshHadFailures = result.Summary.Failed > 0 || result.Summary.Cancelled > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Each source already fails independently inside the refresh orchestrator (spec §13) —
            // reaching here means the check itself could not run at all (e.g. no local database
            // access), so the screen must still stay usable with whatever was already loaded.
            _logger?.LogError(ex, "Check Online failed unexpectedly for {OperatingDate}.", date);
            RefreshResultMessage = "Could not check online right now. Your saved draft and previously verified information are still available.";
            RefreshHadFailures = true;
        }
        finally
        {
            IsCheckingOnline = false;
            RefreshStatusMessage = null;
        }
    }

    private bool CanCheckOnline() => !IsBusy;

    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

    [RelayCommand]
    private void NextDay() => SelectedDate = SelectedDate.AddDays(1);
}
