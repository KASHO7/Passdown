using System.Collections.ObjectModel;
using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 4B–4D's Associate Passdown screen state. Depends only on <see cref="IAssociatePassdownService"/>
/// (application-layer) and <see cref="IClock"/> (application-layer) — it never queries a
/// repository, calls HTTP/Playwright, or recomputes effective-hours arithmetic (every effective-hours
/// value comes from re-running the canonical <see cref="ParkHoursCalculator.Calculate"/> against the
/// composed workspace's official inputs, never a reimplementation of the -30/-45 minute rule); every
/// other value it exposes is either passed straight through from <see cref="AssociateWorkspace"/> or
/// formatted by the pure <c>Presentation</c> builders (see <see cref="ParkHoursPresentation"/> and
/// friends).
///
/// Phase 4C adds editable draft fields plus debounced autosave (spec §8.3): every bindable field
/// below mirrors one part of <see cref="AssociateDraftPayload"/>, populated from a loaded
/// <see cref="AssociateWorkspace"/> by <see cref="ApplyWorkspaceToFields"/> and converted back by
/// <see cref="BuildDraft"/> — the Application layer's <see cref="IAssociatePassdownService.SaveDraftAsync"/>
/// remains the only place a draft is actually persisted.
///
/// Phase 4D extends the same load/edit/autosave pipeline to products: <see cref="ProductSections"/>
/// and <see cref="CustomProducts"/> are populated by <see cref="BuildProductSections"/> (part of
/// <see cref="ApplyWorkspaceToFields"/>) and read back by <see cref="BuildDraft"/> exactly like every
/// scalar field — there is no second save queue or product-specific persistence path.
/// </summary>
public partial class AssociateViewModel : ViewModelBase
{
    private static readonly TimeSpan DefaultAutosaveDebounce = TimeSpan.FromMilliseconds(800);

    private readonly IAssociatePassdownService _service;
    private readonly ILogger<AssociateViewModel>? _logger;
    private readonly TimeSpan _autosaveDebounce;
    private readonly Func<TimeSpan, CancellationToken, Task> _autosaveDelay;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private int _loadVersion;
    private Task _pendingLoad = Task.CompletedTask;
    private Task _pendingAutosave = Task.CompletedTask;
    private CancellationTokenSource? _debounceCts;

    /// <summary>The operating date the current editable fields belong to — distinct from <see cref="SelectedDate"/>, which may already hold a newer date while a flush-then-load is still switching over (Part H).</summary>
    private DateOnly? _draftDateInEdit;

    private bool _isApplyingWorkspace;
    private bool _isDirty;
    private bool _saveInFlight;
    private int? _projectedAttendance;
    private int? _preSolds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    [NotifyPropertyChangedFor(nameof(SaveStatusText))]
    public partial AssociateWorkspace? Workspace { get; set; }

    /// <summary>
    /// Editable, data-driven per-category product sections (spec Phase 4D Part B). Rebuilt only by
    /// <see cref="BuildProductSections"/> — a load, date switch, or Check Online — never derived as a
    /// live getter off <see cref="Workspace"/>, so a mid-edit reassignment of <see cref="Workspace"/>
    /// (e.g. <see cref="SaveDraftNowAsync"/> after an autosave) never resets an in-progress edit or
    /// steals focus from a control the associate is actively using.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProductSections))]
    public partial IReadOnlyList<ProductCategorySectionViewModel> ProductSections { get; set; } = [];

    /// <summary>Manager-added custom/internal products (spec Phase 4D Part F), independent of <see cref="ProductSections"/> — a custom entry has no official source category.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomProducts))]
    public partial ObservableCollection<CustomProductRowViewModel> CustomProducts { get; set; } = [];

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

    /// <summary>Presentation-only save lifecycle (spec §8.3's "unsaved -> saving -> saved"); see <see cref="AssociateAutosaveState"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusText))]
    public partial AssociateAutosaveState AutosaveState { get; set; } = AssociateAutosaveState.Saved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(ManualHoursWarningText))]
    public partial bool ManualHoursOverrideEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(ManualHoursWarningText))]
    public partial TimeSpan? ManualOpeningTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(ManualHoursWarningText))]
    public partial TimeSpan? ManualClosingTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(PrivateBuyoutHoursWarningText))]
    public partial bool PrivateBuyoutEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(PrivateBuyoutHoursWarningText))]
    public partial TimeSpan? PrivateBuyoutOpeningTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(PrivateBuyoutHoursWarningText))]
    public partial TimeSpan? PrivateBuyoutClosingTime { get; set; }

    [ObservableProperty]
    public partial string? PrivateBuyoutDetails { get; set; }

    [ObservableProperty]
    public partial string? ProjectedAttendanceText { get; set; }

    [ObservableProperty]
    public partial string? ProjectedAttendanceError { get; set; }

    [ObservableProperty]
    public partial string? PreSoldsText { get; set; }

    [ObservableProperty]
    public partial string? PreSoldsError { get; set; }

    [ObservableProperty]
    public partial bool IsEarlyRideTimeEnabled { get; set; }

    [ObservableProperty]
    public partial string? EarlyRideTimeDetailsText { get; set; }

    [ObservableProperty]
    public partial string? ParticipatingAttractionsText { get; set; }

    [ObservableProperty]
    public partial string? OperationalNotesText { get; set; }

    [ObservableProperty]
    public partial DailyFunTypeOption SelectedDailyFunTypeOption { get; set; } = DailyFunTypeOption.All[0];

    [ObservableProperty]
    public partial string? DailyFunText { get; set; }

    public AssociateViewModel(
        IAssociatePassdownService service,
        IClock clock,
        ILogger<AssociateViewModel>? logger = null,
        TimeSpan? autosaveDebounce = null,
        Func<TimeSpan, CancellationToken, Task>? autosaveDelay = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _logger = logger;
        _autosaveDebounce = autosaveDebounce ?? DefaultAutosaveDebounce;
        _autosaveDelay = autosaveDelay ?? Task.Delay;

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

    /// <summary>
    /// Re-runs <see cref="ParkHoursCalculator.Calculate"/> against the workspace's official inputs
    /// plus whatever override fields are currently in the editable form (spec §8.2's "recalculate
    /// immediately when the manager changes the override") — never a save round-trip, and never a
    /// second, ViewModel-owned copy of the -30/-45 minute rule.
    /// </summary>
    public ParkHoursPresentation? ParkHours
    {
        get
        {
            if (Workspace is not { } workspace)
            {
                return null;
            }

            var official = workspace.ParkHours;
            var live = ParkHoursCalculator.Calculate(
                official.OfficialIsClosed, official.OfficialOpening, official.OfficialClosing,
                ManualHoursOverrideEnabled, ToTimeOnly(ManualOpeningTime), ToTimeOnly(ManualClosingTime),
                PrivateBuyoutEnabled, ToTimeOnly(PrivateBuyoutOpeningTime), ToTimeOnly(PrivateBuyoutClosingTime));

            return ParkHoursPresentation.Build(official with { Effective = live });
        }
    }

    public string? ManualHoursWarningText =>
        ManualHoursOverrideEnabled && ManualOpeningTime is { } manualOpen && ManualClosingTime is { } manualClose && manualClose <= manualOpen
            ? "Closing time should be after opening time."
            : null;

    public string? PrivateBuyoutHoursWarningText =>
        PrivateBuyoutEnabled && PrivateBuyoutOpeningTime is { } buyoutOpen && PrivateBuyoutClosingTime is { } buyoutClose && buyoutClose <= buyoutOpen
            ? "Closing time should be after opening time."
            : null;

    public IReadOnlyList<DailyFunTypeOption> DailyFunTypeOptions => DailyFunTypeOption.All;

    public bool HasProductSections => ProductSections.Count > 0;

    public bool HasCustomProducts => CustomProducts.Count > 0;

    public SaveStatusPresentation? SaveStatus => Workspace is { } workspace ? SaveStatusPresentation.Build(workspace.SaveStatus) : null;

    /// <summary>The footer's actual text: the in-progress autosave state when there is one, otherwise the last persisted <see cref="SaveStatus"/> (spec §8.3/§10.3's "Saved locally at 2:14 PM").</summary>
    public string SaveStatusText => AutosaveState switch
    {
        AssociateAutosaveState.Saving => "Saving…",
        AssociateAutosaveState.Unsaved => "Unsaved changes",
        _ => SaveStatus?.Text ?? "Not saved yet",
    };

    /// <summary>
    /// The in-flight (or most recently finished) local load, so a test can await it deterministically
    /// instead of racing the fire-and-forget task <see cref="OnSelectedDateChanged"/> starts. The
    /// constructor's own initial <see cref="SelectedDate"/> assignment goes through this exact same
    /// path — there is no separate "initial load" method — so production startup issues exactly one
    /// local load, not two.
    /// </summary>
    public Task PendingLoad => _pendingLoad;

    /// <summary>The in-flight (or most recently finished) debounced autosave, so a test can await a scheduled save deterministically instead of racing it.</summary>
    public Task PendingAutosave => _pendingAutosave;

    /// <summary>
    /// Spec §6.3 "fast date changes": every date change — including the very first one, via the
    /// constructor's initial assignment — runs through this single local-only path. It never calls
    /// <see cref="IAssociatePassdownService.CheckAssociateDateOnlineAsync"/> or anything else that
    /// could reach the network. Spec Part H: the previous date's pending edits are flushed/saved
    /// before the new date's workspace is composed and applied.
    /// </summary>
    partial void OnSelectedDateChanged(DateOnly value) => _pendingLoad = SwitchDateAsync(value);

    private async Task SwitchDateAsync(DateOnly newDate)
    {
        await FlushPendingAutosaveAsync().ConfigureAwait(true);
        await LoadDateAsync(newDate).ConfigureAwait(true);
    }

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

            ApplyWorkspaceToFields(workspace);
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
            // Part I: persist whatever is currently only sitting in the editable fields first, so
            // the recompose below (which re-reads the saved draft record) reflects it instead of
            // discarding it in favor of stale on-disk state.
            await FlushPendingAutosaveAsync().ConfigureAwait(true);

            var result = await _service.CheckAssociateDateOnlineAsync(date, progress);
            ApplyWorkspaceToFields(result.Workspace);
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

    /// <summary>Adds a blank custom/internal product row (spec Phase 4D Part F) — always draft-local, never given an official-looking id.</summary>
    [RelayCommand]
    private void AddCustomProduct()
    {
        var entry = new CustomProductEntry(Guid.NewGuid().ToString("N"), "New custom item", Price: null, Notes: null, IncludeOnPdf: true);
        CustomProducts.Add(new CustomProductRowViewModel(entry, ScheduleAutosave, RemoveCustomProduct));
        OnPropertyChanged(nameof(HasCustomProducts));
        ScheduleAutosave();
    }

    private void RemoveCustomProduct(CustomProductRowViewModel row)
    {
        CustomProducts.Remove(row);
        OnPropertyChanged(nameof(HasCustomProducts));
        ScheduleAutosave();
    }

    partial void OnManualHoursOverrideEnabledChanged(bool value) => ScheduleAutosave();

    partial void OnManualOpeningTimeChanged(TimeSpan? value) => ScheduleAutosave();

    partial void OnManualClosingTimeChanged(TimeSpan? value) => ScheduleAutosave();

    partial void OnPrivateBuyoutEnabledChanged(bool value) => ScheduleAutosave();

    partial void OnPrivateBuyoutOpeningTimeChanged(TimeSpan? value) => ScheduleAutosave();

    partial void OnPrivateBuyoutClosingTimeChanged(TimeSpan? value) => ScheduleAutosave();

    partial void OnPrivateBuyoutDetailsChanged(string? value) => ScheduleAutosave();

    partial void OnProjectedAttendanceTextChanged(string? value)
    {
        if (TryParseNonNegativeInt(value, out var parsed, out var error))
        {
            ProjectedAttendanceError = null;
            _projectedAttendance = parsed;
            ScheduleAutosave();
        }
        else
        {
            ProjectedAttendanceError = error;
        }
    }

    partial void OnPreSoldsTextChanged(string? value)
    {
        if (TryParseNonNegativeInt(value, out var parsed, out var error))
        {
            PreSoldsError = null;
            _preSolds = parsed;
            ScheduleAutosave();
        }
        else
        {
            PreSoldsError = error;
        }
    }

    /// <summary>Turning Early Ride Time off means "not applicable" (spec Part D) — its detail fields are cleared rather than left populated but hidden.</summary>
    partial void OnIsEarlyRideTimeEnabledChanged(bool value)
    {
        if (!value && !_isApplyingWorkspace)
        {
            EarlyRideTimeDetailsText = null;
            ParticipatingAttractionsText = null;
        }

        ScheduleAutosave();
    }

    partial void OnEarlyRideTimeDetailsTextChanged(string? value) => ScheduleAutosave();

    partial void OnParticipatingAttractionsTextChanged(string? value) => ScheduleAutosave();

    partial void OnOperationalNotesTextChanged(string? value) => ScheduleAutosave();

    partial void OnSelectedDailyFunTypeOptionChanged(DailyFunTypeOption value) => ScheduleAutosave();

    partial void OnDailyFunTextChanged(string? value) => ScheduleAutosave();

    /// <summary>Pushes a freshly loaded/refreshed workspace's draft into the editable fields without treating that as a user edit (see <see cref="_isApplyingWorkspace"/>), then publishes the workspace itself.</summary>
    private void ApplyWorkspaceToFields(AssociateWorkspace workspace)
    {
        _isApplyingWorkspace = true;
        try
        {
            var draft = workspace.Draft;

            ManualHoursOverrideEnabled = draft.ManualHoursOverride.Enabled;
            ManualOpeningTime = ToTimeSpan(draft.ManualHoursOverride.OpeningTime);
            ManualClosingTime = ToTimeSpan(draft.ManualHoursOverride.ClosingTime);

            PrivateBuyoutEnabled = draft.PrivateBuyout.Enabled;
            PrivateBuyoutOpeningTime = ToTimeSpan(draft.PrivateBuyout.OpeningTime);
            PrivateBuyoutClosingTime = ToTimeSpan(draft.PrivateBuyout.ClosingTime);
            PrivateBuyoutDetails = draft.PrivateBuyout.Details;

            _projectedAttendance = draft.ProjectedAttendance;
            ProjectedAttendanceText = draft.ProjectedAttendance?.ToString(CultureInfo.InvariantCulture);
            ProjectedAttendanceError = null;

            _preSolds = draft.PreSolds;
            PreSoldsText = draft.PreSolds?.ToString(CultureInfo.InvariantCulture);
            PreSoldsError = null;

            IsEarlyRideTimeEnabled = !string.IsNullOrWhiteSpace(draft.EarlyRideTime.Details) || draft.EarlyRideTime.ParticipatingAttractions.Count > 0;
            EarlyRideTimeDetailsText = draft.EarlyRideTime.Details;
            ParticipatingAttractionsText = string.Join(Environment.NewLine, draft.EarlyRideTime.ParticipatingAttractions);

            OperationalNotesText = string.Join(Environment.NewLine, draft.OperationalNotes);

            SelectedDailyFunTypeOption = DailyFunTypeOption.For(draft.DailyFunType);
            DailyFunText = draft.DailyFunText;

            BuildProductSections(workspace);

            _draftDateInEdit = workspace.OperatingDate;
            _isDirty = false;
            AutosaveState = AssociateAutosaveState.Saved;
        }
        finally
        {
            _isApplyingWorkspace = false;
        }

        Workspace = workspace;
    }

    /// <summary>
    /// Groups the composed workspace's official products by category (spec Phase 4D Part B), pairing
    /// each category with its status banner (Phase 4B Part E) when it currently has zero rows, and
    /// rebuilds the custom-product rows from the draft's own <see cref="AssociateDraftPayload.CustomProducts"/>
    /// (never from <see cref="AssociateProduct"/> projections — that is how <see cref="CustomProductEntry.Notes"/>
    /// stays editable, spec Part G, without forcing a meaningless Notes field onto official products).
    /// A category with neither product rows nor a status entry is not built at all — no fabricated
    /// empty section (spec Part B/H).
    /// </summary>
    private void BuildProductSections(AssociateWorkspace workspace)
    {
        var statusByCategory = workspace.CategoryStatuses.ToDictionary(status => status.Category);
        var officialByCategory = workspace.Products
            .Where(product => !product.IsCustom && product.Category is not null)
            .GroupBy(product => product.Category!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<AssociateProduct>)group.ToList());

        var categories = officialByCategory.Keys.Concat(statusByCategory.Keys).Distinct().OrderBy(category => (int)category);

        var sections = new List<ProductCategorySectionViewModel>();
        foreach (var category in categories)
        {
            var products = officialByCategory.GetValueOrDefault(category, []);
            var status = statusByCategory.TryGetValue(category, out var categoryStatus)
                ? CategoryStatusPresentation.Build(categoryStatus, products.Count)
                : null;

            if (products.Count == 0 && status is null)
            {
                continue;
            }

            var rows = products.Select(product => new ProductRowViewModel(product, ScheduleAutosave)).ToList();
            sections.Add(new ProductCategorySectionViewModel(SourceCategoryDisplay.FriendlyName(category), status, rows));
        }

        ProductSections = sections;

        CustomProducts = new ObservableCollection<CustomProductRowViewModel>(
            workspace.Draft.CustomProducts.Select(custom => new CustomProductRowViewModel(custom, ScheduleAutosave, RemoveCustomProduct)));
    }

    /// <summary>Converts the current editable fields into a full <see cref="AssociateDraftPayload"/>, including product overrides/custom products read from the current <see cref="ProductSections"/>/<see cref="CustomProducts"/> row view models (spec Phase 4D Parts D/E/F).</summary>
    private AssociateDraftPayload BuildDraft()
    {
        var existing = Workspace?.Draft ?? AssociateDraftPayload.CreateDefault();

        var productOverrides = ProductSections
            .SelectMany(section => section.Products)
            .ToDictionary(row => row.ProductIdentity, row => new ProductDraftOverride(row.IncludeOnPdf, row.DisplayPriceOverride));

        return existing with
        {
            ManualHoursOverride = new ManualHoursOverride(ManualHoursOverrideEnabled, ToTimeOnly(ManualOpeningTime), ToTimeOnly(ManualClosingTime)),
            PrivateBuyout = new PrivateBuyoutOverride(PrivateBuyoutEnabled, ToTimeOnly(PrivateBuyoutOpeningTime), ToTimeOnly(PrivateBuyoutClosingTime), NullIfBlank(PrivateBuyoutDetails)),
            ProjectedAttendance = _projectedAttendance,
            PreSolds = _preSolds,
            EarlyRideTime = new EarlyRideTimeDetails(NullIfBlank(EarlyRideTimeDetailsText), ParseLines(ParticipatingAttractionsText)),
            OperationalNotes = ParseLines(OperationalNotesText),
            DailyFunType = SelectedDailyFunTypeOption.Value,
            DailyFunText = NullIfBlank(DailyFunText),
            ProductOverrides = productOverrides,
            CustomProducts = CustomProducts.Select(row => row.ToEntry()).ToList(),
        };
    }

    /// <summary>
    /// Any edit marks the draft unsaved and (re)starts the debounce timer (spec §8.3's "short
    /// debounce"); a later edit before the timer elapses cancels and replaces it rather than
    /// queuing a second save, so rapid typing never produces one save per keystroke. No-ops while
    /// <see cref="ApplyWorkspaceToFields"/> is itself assigning the fields — that is a data load,
    /// not a user edit.
    /// </summary>
    private void ScheduleAutosave()
    {
        if (_isApplyingWorkspace)
        {
            return;
        }

        _isDirty = true;
        AutosaveState = AssociateAutosaveState.Unsaved;

        if (_draftDateInEdit is not { } date)
        {
            return; // nothing loaded yet to attach this edit to.
        }

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _pendingAutosave = RunAutosaveAsync(date, cts.Token);
    }

    private async Task RunAutosaveAsync(DateOnly date, CancellationToken debounceToken)
    {
        try
        {
            await _autosaveDelay(_autosaveDebounce, debounceToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer edit or a flush-then-switch.
        }

        if (debounceToken.IsCancellationRequested)
        {
            return;
        }

        await SaveDraftNowAsync(date).ConfigureAwait(true);
    }

    /// <summary>
    /// The one place <see cref="IAssociatePassdownService.SaveDraftAsync"/> is actually called.
    /// Serialized by <see cref="_saveSemaphore"/> so overlapping calls (a debounce firing while a
    /// flush is already saving, or vice versa) can never race — a second caller simply waits, then
    /// saves whatever the editable fields hold at that later point, never a stale snapshot.
    /// </summary>
    private async Task SaveDraftNowAsync(DateOnly date)
    {
        await _saveSemaphore.WaitAsync().ConfigureAwait(true);
        _saveInFlight = true;
        try
        {
            _isDirty = false;
            AutosaveState = AssociateAutosaveState.Saving;
            var draft = BuildDraft();

            var status = await _service.SaveDraftAsync(date, draft).ConfigureAwait(true);

            if (Workspace is { } current && current.OperatingDate == date)
            {
                Workspace = current with { Draft = draft, SaveStatus = status };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to autosave the Associate draft for {OperatingDate}.", date);
            _isDirty = true; // the edit is still only local; the next scheduled/flushed save retries it.
        }
        finally
        {
            _saveInFlight = false;
            AutosaveState = _isDirty ? AssociateAutosaveState.Unsaved : AssociateAutosaveState.Saved;
            _saveSemaphore.Release();
        }
    }

    /// <summary>
    /// Synchronously drains any pending debounce/save for the date currently being edited (spec
    /// Part H/I) before the caller replaces it — a date switch or a Check Online recompose. Cancels
    /// the debounce timer itself (no need to wait 800ms for something about to be flushed anyway)
    /// and saves directly instead.
    /// </summary>
    private async Task FlushPendingAutosaveAsync()
    {
        _debounceCts?.Cancel();

        if (_draftDateInEdit is not { } date)
        {
            return;
        }

        while (_isDirty || _saveInFlight)
        {
            await SaveDraftNowAsync(date).ConfigureAwait(true);
        }
    }

    private static bool TryParseNonNegativeInt(string? text, out int? value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = null;
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = null;
            error = "Enter a whole number.";
            return false;
        }

        if (parsed < 0)
        {
            value = null;
            error = "Enter a number 0 or greater.";
            return false;
        }

        value = parsed;
        error = null;
        return true;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ParseLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static TimeOnly? ToTimeOnly(TimeSpan? value) => value is { } span ? TimeOnly.FromTimeSpan(span) : null;

    private static TimeSpan? ToTimeSpan(TimeOnly? value) => value?.ToTimeSpan();
}
