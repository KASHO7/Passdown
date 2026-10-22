using System.Collections.ObjectModel;
using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Infrastructure.Preferences;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Associate Passdown's internal section rail (spec Parts 2/3 — Overview / Products / Notes; Phase
/// 10C Part F). View-model-owned so a caller like <see cref="MainViewModel"/> can request a specific
/// section (e.g. a Dashboard "Open Notes" deep link) without manipulating <c>ToggleButton</c> state or
/// searching the visual tree.
/// </summary>
public enum AssociateSection
{
    Overview,
    Products,
    Notes,
}

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

    /// <summary>Fallback for <see cref="AssociateWorkspace.SelectedOperationalNoteSourceStates"/> when it is <see langword="null"/> (Phase 11E Part N: pre-11E workspace construction sites) — every selection reads as <see cref="OperationalNoteSourceComparisonState.Unchanged"/> rather than the ViewModel special-casing null.</summary>
    private static readonly IReadOnlyDictionary<Guid, OperationalNoteSourceComparisonState> EmptySourceStates = new Dictionary<Guid, OperationalNoteSourceComparisonState>();

    private readonly IAssociatePassdownService _service;

    private readonly IClock _clock;
    private readonly IErtConfigurationService _ertConfigurationService;
    private readonly IAssociatePassdownPdfExportService _pdfExportService;
    private readonly IPdfSaveFilePicker _pdfSavePicker;
    private readonly IAssociatePassdownTemplatePreferenceStore _templatePreferenceStore;
    private readonly IClipboardService _clipboardService;
    private readonly IOfficialDataRefreshCoordinator? _refreshCoordinator;
    private readonly IUiDispatcher _uiDispatcher;

    /// <summary>Phase 26: opens a validated official source page in the default browser (null in tests that do not supply one — no link is offered).</summary>
    private readonly IExternalLinkLauncher? _externalLinkLauncher;
    private readonly ILogger<AssociateViewModel>? _logger;
    private readonly TimeSpan _autosaveDebounce;
    private readonly Func<TimeSpan, CancellationToken, Task> _autosaveDelay;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private int _loadVersion;
    private Task _pendingLoad = Task.CompletedTask;
    private Task _pendingAutosave = Task.CompletedTask;
    private Task _pendingTemplatePreferenceLoad = Task.CompletedTask;
    private Task _pendingTemplatePreferenceSave = Task.CompletedTask;
    private bool _isLoadingTemplatePreference;
    private bool _isRegroupingCountText;
    private CancellationTokenSource? _debounceCts;

    // Phase 19A: the quiet automatic check of the selected date (cancelled/superseded whenever the date
    // changes or Check Online starts), and a newer-official-data reload deferred until the editor is clean.
    private CancellationTokenSource? _autoCheckCts;
    private Task _pendingAutoCheck = Task.CompletedTask;
    private Task _pendingOfficialDataReload = Task.CompletedTask;
    private bool _officialDataReloadDeferred;

    /// <summary>The operating date the current editable fields belong to — distinct from <see cref="SelectedDate"/>, which may already hold a newer date while a flush-then-load is still switching over (Part H).</summary>
    private DateOnly? _draftDateInEdit;

    private bool _isApplyingWorkspace;
    private bool _isDirty;
    private bool _saveInFlight;

    // Phase 23A: set while a blocked date switch puts SelectedDate back on the date still in edit, so
    // that restore is not itself treated as a new date change (no flush, no load).
    private bool _isRestoringSelectedDate;

    /// <summary>
    /// Phase 23A: the most save passes one flush makes. A pass only repeats when the associate kept
    /// typing while the previous pass was writing; a failed pass always ends the flush immediately, so
    /// a persistent storage failure costs exactly one attempt per flush — never a retry loop.
    /// </summary>
    internal const int MaxFlushSavePasses = 3;

    internal const string DateSwitchBlockedMessage =
        "Your changes could not be saved on this computer, so the date was not changed. Select Retry Save, then try again.";

    internal const string CheckOnlineBlockedMessage =
        "Not checked online: your changes could not be saved on this computer. Select Retry Save, then check again.";

    internal const string NoteChangeBlockedMessage =
        "The note was not changed because your other changes could not be saved on this computer. Select Retry Save, then try again.";

    internal const string PdfBlockedMessage =
        "The PDF was not created because your changes could not be saved on this computer. Select Retry Save, then try again.";

    internal const string CloseBlockedMessage =
        "Your latest changes could not be saved on this computer. Select Retry Save, or close the app again to exit without them.";

    // Phase 18B REL-004: the content hash of each persisted record this editor last loaded or saved —
    // the expected base of its next save, so a version Personal Sync changed underneath the editor is
    // never silently overwritten (see IAssociatePassdownService.SaveEditorDraftAsync).
    private string? _draftBaseHash;
    private string? _monthlyErtBaseHash;
    private string? _overrideBaseHash;
    private ErtEditorFields? _ertEditorLoadedFields;

    /// <summary>The Early Ride Time panel's editable values, captured when it was (re)loaded — a panel whose current values still equal these is "clean" and may be reloaded after a sync change.</summary>
    private sealed record ErtEditorFields(bool MonthlyNoErt, string? MonthlyRides, string? MonthlyNote, bool UseOverride, bool OverrideNoErt, string? OverrideRides, string? OverrideReason);

    internal const string DraftSyncConflictMessage =
        "This date was changed on another device while you were editing. Your edits are saved on this computer and the other version is kept too — choose which to keep under Personal Sync conflicts.";

    internal const string ErtSyncConflictMessage =
        "Early Ride Time was changed on another device while this editor was open. Your values are saved on this computer and the other version is kept too — choose which to keep under Personal Sync conflicts.";
    private int? _projectedAttendance;
    private int? _preSolds;

    /// <summary>
    /// The last successfully parsed (or loaded) Manual Hours / Private Buyout time, kept separate
    /// from the editable <c>*Text</c> properties so an invalid/unparseable keystroke can never
    /// overwrite it (Phase 13E Part N/AH #9-10) — mirrors <see cref="ProductRowViewModel"/>'s
    /// "last valid override" pattern. <c>*TimeParseError</c> holds the current parser message, if any;
    /// <see cref="ManualOpeningTimeError"/> and friends combine it with the "required while enabled"
    /// rule (Part R).
    /// </summary>
    private TimeOnly? _manualOpeningTime;
    private TimeOnly? _manualClosingTime;
    private TimeOnly? _privateBuyoutOpeningTime;
    private TimeOnly? _privateBuyoutClosingTime;
    private string? _manualOpeningTimeParseError;
    private string? _manualClosingTimeParseError;
    private string? _privateBuyoutOpeningTimeParseError;
    private string? _privateBuyoutClosingTimeParseError;

    /// <summary>
    /// Phase 18D: the last valid committed North Gate/Avenue override for the selected date (null =
    /// use the calculated default), kept apart from the editable <c>*Text</c> exactly like the
    /// Manual Hours fields above so a malformed entry can never become draft data.
    /// </summary>
    private TimeOnly? _northGateOverride;
    private TimeOnly? _avenueOverride;
    private string? _northGateOverrideParseError;
    private string? _avenueOverrideParseError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(EarlyRideTimeStatusText))]
    [NotifyPropertyChangedFor(nameof(EarlyRideTimeIsOverride))]
    [NotifyPropertyChangedFor(nameof(EarlyRideTimeIsConfigured))]
    [NotifyPropertyChangedFor(nameof(EarlyRideTimeIsNotConfigured))]
    [NotifyPropertyChangedFor(nameof(EarlyRideTimeIsNoErt))]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    [NotifyPropertyChangedFor(nameof(SaveStatusText))]
    [NotifyCanExecuteChangedFor(nameof(CreatePdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyForTeamsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMorningMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyOdNotesCommand))]
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
    [NotifyPropertyChangedFor(nameof(SelectedDateForPicker))]
    [NotifyPropertyChangedFor(nameof(DateNavigation))]
    [NotifyPropertyChangedFor(nameof(IsCalendarTodayHighlighted))]
    [NotifyPropertyChangedFor(nameof(AvailableOperationalNoteSuggestionsHeading))]
    [NotifyPropertyChangedFor(nameof(SelectedMonthLabel))]
    [NotifyPropertyChangedFor(nameof(UseOverrideForDateLabel))]
    [NotifyPropertyChangedFor(nameof(EarlyRideTimeStatusText))]
    public partial DateOnly SelectedDate { get; set; }

    /// <summary>
    /// Which of Overview/Products/Notes is currently shown (Phase 10C Part F) — deliberately
    /// independent of <see cref="SelectedDate"/>: switching this never loads, saves, or otherwise
    /// touches draft data, exactly like the view-layer <c>ToggleButton</c> state it replaces.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsProductsSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsNotesSectionSelected))]
    public partial AssociateSection SelectedSection { get; set; } = AssociateSection.Overview;

    public bool IsOverviewSectionSelected => SelectedSection == AssociateSection.Overview;

    public bool IsProductsSectionSelected => SelectedSection == AssociateSection.Products;

    public bool IsNotesSectionSelected => SelectedSection == AssociateSection.Notes;

    [RelayCommand]
    private void ShowOverviewSection() => SelectedSection = AssociateSection.Overview;

    [RelayCommand]
    private void ShowProductsSection() => SelectedSection = AssociateSection.Products;

    [RelayCommand]
    private void ShowNotesSection() => SelectedSection = AssociateSection.Notes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreatePdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyForTeamsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMorningMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyOdNotesCommand))]
    public partial bool IsLoadingDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreatePdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyForTeamsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMorningMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyOdNotesCommand))]
    public partial bool IsCheckingOnline { get; set; }

    /// <summary>
    /// Phase 19A: true while the quiet automatic check of the selected (today/future, missing/stale)
    /// date is actually online. Deliberately separate from <see cref="IsCheckingOnline"/>: it never
    /// disables Check Online, Create PDF, or Copy, and never blocks the screen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAutoCheckingDate { get; set; }

    /// <summary>Phase 19A: the shared app-wide official-data freshness line (null only in tests that do not supply one).</summary>
    public OfficialDataStatusViewModel? OfficialDataStatus { get; }

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

    /// <summary>
    /// Phase 18B REL-004: set when a save from this screen found that Personal Sync had changed the
    /// same record while it was open. Nothing was overwritten or lost: this device's edits were saved
    /// and the other device's version is preserved as a Personal Sync conflict awaiting an explicit
    /// choice. Cleared when another date is loaded.
    /// </summary>
    [ObservableProperty]
    public partial string? SyncConflictMessage { get; set; }

    /// <summary>Presentation-only save lifecycle (spec §8.3's "unsaved -> saving -> saved"); see <see cref="AssociateAutosaveState"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusText))]
    [NotifyPropertyChangedFor(nameof(SaveStatusDetailText))]
    [NotifyPropertyChangedFor(nameof(IsSaveFailed))]
    [NotifyCanExecuteChangedFor(nameof(RetrySaveCommand))]
    public partial AssociateAutosaveState AutosaveState { get; set; } = AssociateAutosaveState.Saved;

    /// <summary>Phase 23A: true while the most recent save attempt failed — shows the Retry Save action.</summary>
    public bool IsSaveFailed => AutosaveState == AssociateAutosaveState.SaveFailed;

    /// <summary>
    /// Phase 23A: what was held back because the unsaved edits could not be written (a date switch,
    /// Check Online, a note change, a PDF, or closing the app). Null when nothing is blocked; cleared by
    /// the next successful save or date load.
    /// </summary>
    [ObservableProperty]
    public partial string? SaveBlockedMessage { get; set; }

    /// <summary>
    /// Phase 23: the reason the most recent save attempt failed (the exception message), or null once a
    /// save succeeds or another date is loaded. Kept so the failure is explained, not only flagged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusDetailText))]
    public partial string? SaveFailureReason { get; set; }

    /// <summary>Tooltip / Narrator help text for the save chip: the failure reason after a failed save, otherwise null.</summary>
    public string? SaveStatusDetailText => AutosaveState == AssociateAutosaveState.SaveFailed
        ? $"The last save on this computer failed: {SaveFailureReason ?? "unknown error"} Your edits are still on screen; select Retry Save or make another change to try again."
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(ManualHoursWarningText))]
    [NotifyPropertyChangedFor(nameof(ManualOpeningTimeError))]
    [NotifyPropertyChangedFor(nameof(ManualClosingTimeError))]
    public partial bool ManualHoursOverrideEnabled { get; set; }

    /// <summary>
    /// Free-typed Manual Hours opening time (Phase 13E — replaces the stock <c>TimePicker</c>).
    /// Bound with <c>UpdateSourceTrigger=LostFocus</c> (see <c>AssociateView.axaml</c>) so parsing/
    /// normalization runs once per commit (Tab/Enter/click-away), never per keystroke — this is what
    /// lets <see cref="OnManualOpeningTimeTextChanged"/> safely rewrite the field to its canonical
    /// display (Part P) without fighting the associate mid-type. See <see cref="OperationalTimeParser"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? ManualOpeningTimeText { get; set; }

    [ObservableProperty]
    public partial string? ManualClosingTimeText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParkHours))]
    [NotifyPropertyChangedFor(nameof(PrivateBuyoutHoursWarningText))]
    [NotifyPropertyChangedFor(nameof(PrivateBuyoutOpeningTimeError))]
    [NotifyPropertyChangedFor(nameof(PrivateBuyoutClosingTimeError))]
    public partial bool PrivateBuyoutEnabled { get; set; }

    [ObservableProperty]
    public partial string? PrivateBuyoutOpeningTimeText { get; set; }

    [ObservableProperty]
    public partial string? PrivateBuyoutClosingTimeText { get; set; }

    [ObservableProperty]
    public partial string? PrivateBuyoutDetails { get; set; }

    /// <summary>
    /// Phase 18D: free-typed North Gate override (blank = use the calculated default — the default is
    /// never copied in). Same LostFocus commit/normalize/parse path as the Manual Hours fields.
    /// </summary>
    [ObservableProperty]
    public partial string? NorthGateOverrideText { get; set; }

    /// <summary>Phase 18D: free-typed Avenue override — same contract as <see cref="NorthGateOverrideText"/>, independent of it.</summary>
    [ObservableProperty]
    public partial string? AvenueOverrideText { get; set; }

    public string? NorthGateOverrideError => _northGateOverrideParseError;

    public string? AvenueOverrideError => _avenueOverrideParseError;

    /// <summary>True while a North Gate override is in effect for the selected date — drives the subtle "Override" caption and the Reset action.</summary>
    public bool NorthGateIsOverridden => LiveParkHours?.Effective.IsNorthGateOverridden ?? false;

    public bool AvenueIsOverridden => LiveParkHours?.Effective.IsAvenueOverridden ?? false;

    /// <summary>True whenever an override is stored for the selected date — even on a Closed date where it is not applied — so Reset is always reachable.</summary>
    public bool CanResetNorthGateOverride => _northGateOverride is not null;

    public bool CanResetAvenueOverride => _avenueOverride is not null;

    /// <summary>Phase 18D "Reset to default": removes the override only (the field goes blank); the effective time falls back to whatever the current calculated default is.</summary>
    [RelayCommand]
    private void ResetNorthGateOverride() => NorthGateOverrideText = null;

    [RelayCommand]
    private void ResetAvenueOverride() => AvenueOverrideText = null;

    [ObservableProperty]
    public partial string? ProjectedAttendanceText { get; set; }

    [ObservableProperty]
    public partial string? ProjectedAttendanceError { get; set; }

    [ObservableProperty]
    public partial string? PreSoldsText { get; set; }

    [ObservableProperty]
    public partial string? PreSoldsError { get; set; }

    /// <summary>
    /// Whether the single compact Early Ride Time editor panel is open (Phase 17D §3 — replaces the
    /// Phase 17B pair of independent "Configure Monthly ERT"/"Override for this date" panels with one
    /// panel that shows the Monthly Default and Date Override sections together, opened by one
    /// compact [Edit] action). No routine ERT start time is collected anywhere in this editor (spec
    /// §1/§2/§3) — associates already know Early Ride happens 30 minutes before park open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsErtEditorOpen { get; set; }

    [ObservableProperty]
    public partial string? MonthlyErtRidesText { get; set; }

    /// <summary>"No ERT this month" (spec Part 7/11) — when checked, the rides field is ignored by <see cref="SaveMonthlyErtAsync"/> regardless of what it still shows, matching <see cref="Application.Ert.ErtConfigurationService"/>'s own server-side normalization.</summary>
    [ObservableProperty]
    public partial bool MonthlyErtNoErt { get; set; }

    [ObservableProperty]
    public partial string? MonthlyErtNoteText { get; set; }

    /// <summary>Set only by an explicit Save Monthly attempt — never updated by simply typing, since Monthly ERT saves only on the explicit button press (spec Part 7).</summary>
    [ObservableProperty]
    public partial string? MonthlyErtSaveError { get; set; }

    /// <summary>"[ ] Use override for {date}" (Phase 17D §3) — reveals/enables the override rides/reason fields when checked; pre-checked on open only when a date override already exists.</summary>
    [ObservableProperty]
    public partial bool ErtUseOverrideForDate { get; set; }

    [ObservableProperty]
    public partial string? OverrideRidesText { get; set; }

    [ObservableProperty]
    public partial bool OverrideNoErt { get; set; }

    [ObservableProperty]
    public partial string? OverrideReasonText { get; set; }

    [ObservableProperty]
    public partial string? OverrideSaveError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationalNotesCount))]
    [NotifyPropertyChangedFor(nameof(HasOperationalNotesCount))]
    public partial string? OperationalNotesText { get; set; }

    /// <summary>
    /// Persistent Operational Notes the supervisor has explicitly selected for the date currently in
    /// edit (Phase 11D Part V), in selection order (Part AP) — built from the frozen
    /// <see cref="AssociateDraftPayload.SelectedOperationalNotes"/> copies, never from a live
    /// <see cref="OperationalNote"/> lookup. Independent of <see cref="OperationalNotesText"/> (Part
    /// AA): selecting/deselecting a persistent note never reads or writes the manual notes text.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedOperationalNotes))]
    [NotifyPropertyChangedFor(nameof(OperationalNotesCount))]
    [NotifyPropertyChangedFor(nameof(HasOperationalNotesCount))]
    public partial IReadOnlyList<AssociateSelectedOperationalNoteRowViewModel> SelectedOperationalNotes { get; set; } = [];

    /// <summary>
    /// Persistent notes relevant to the date currently in edit that are not already selected (Phase
    /// 11D Part E/U) — recomputed on every date load and after every Add/Remove (Part AL), never
    /// polled. Uses the Associate-selected date, not Dashboard's Eastern "today".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableOperationalNoteSuggestions))]
    public partial IReadOnlyList<AssociateOperationalNoteSuggestionRowViewModel> AvailableOperationalNoteSuggestions { get; set; } = [];

    public bool HasSelectedOperationalNotes => SelectedOperationalNotes.Count > 0;

    /// <summary>
    /// The compact notes count (Phase 17D §5; Phase 22D: shown beside "Notes" on the section rail) —
    /// the selected persistent notes plus one for the manual notes entry when it carries any text,
    /// giving the associate a single at-a-glance total without a dedicated card.
    /// </summary>
    public int OperationalNotesCount => SelectedOperationalNotes.Count + (string.IsNullOrWhiteSpace(OperationalNotesText) ? 0 : 1);

    /// <summary>Phase 22D: the rail shows the count only when there is something to count (never a bare "0").</summary>
    public bool HasOperationalNotesCount => OperationalNotesCount > 0;

    public bool HasAvailableOperationalNoteSuggestions => AvailableOperationalNoteSuggestions.Count > 0;

    /// <summary>"Available for Aug 26" — the Associate-selected date, not Dashboard's Eastern "today" (Phase 11D Part E/U).</summary>
    public string AvailableOperationalNoteSuggestionsHeading =>
        $"Available for {SelectedDate.ToDateTime(TimeOnly.MinValue).ToString("MMM d", PresentationCulture.UnitedStates)}";

    [ObservableProperty]
    public partial DailyFeatureTypeOption SelectedDailyFeatureTypeOption { get; set; } = DailyFeatureTypeOption.All[0];

    [ObservableProperty]
    public partial string? DailyFeatureText { get; set; }

    /// <summary>
    /// The currently selected Associate Passdown PDF template (Phase 17C §12/§15) — a local
    /// device/presentation preference, never draft data (see <see cref="AssociatePassdownTemplatePreference"/>).
    /// Loaded once at construction (<see cref="PendingTemplatePreferenceLoad"/>) and persisted on every
    /// change; switching it never touches <see cref="Workspace"/> or triggers autosave (spec §17:
    /// "changing draft data" and "changing the template" are deliberately independent).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClassicTemplateSelected))]
    [NotifyPropertyChangedFor(nameof(IsCompactTemplateSelected))]
    [NotifyPropertyChangedFor(nameof(IsModernTemplateSelected))]
    public partial AssociatePassdownTemplateOption SelectedTemplateOption { get; set; } = AssociatePassdownTemplateOption.For(AssociatePassdownTemplate.Classic);

    /// <summary>
    /// Optional-section visibility for the Passdown Builder (Phase 8A) — all default to included,
    /// mirroring <see cref="PassdownSectionSelection.AllIncluded"/>. Day of week, date, Park Hours,
    /// Attendance, Early Ride Time, and Daily Feature are mandatory and have no corresponding
    /// property here.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeDailyAdmission { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeFastLane { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludePromotions { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeSeasonPasses { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeAddOns { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeHauntedAttractions { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeOperationalNotes { get; set; } = true;

    /// <summary>Phase 21C: the optional Shows section switch (off by default; old drafts load off).</summary>
    [ObservableProperty]
    public partial bool IncludeShows { get; set; }

    /// <summary>Phase 21C: the selected date's official shows (plus any selected show no longer on the schedule, explained).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShowRows))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllShowsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearShowsCommand))]
    public partial IReadOnlyList<AssociateShowRowViewModel> ShowRows { get; set; } = [];

    public bool HasShowRows => ShowRows.Count > 0;

    /// <summary>Phase 21C: why there are no rows ("not published", "couldn't check", …) — null when rows are listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShowsStatusText))]
    public partial string? ShowsStatusText { get; set; }

    public bool HasShowsStatusText => ShowsStatusText is not null;

    /// <summary>Phase 21C: "Official schedule checked 10:32 AM" (plus "latest check failed" when older data is kept).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShowsFreshnessText))]
    public partial string? ShowsFreshnessText { get; set; }

    public bool HasShowsFreshnessText => ShowsFreshnessText is not null;

    /// <summary>Phase 21C: selects every show currently on the official schedule (never a show that no longer is).</summary>
    [RelayCommand(CanExecute = nameof(HasShowRows))]
    private void SelectAllShows()
    {
        foreach (var row in ShowRows.Where(row => row.Row.State is AssociateShowRowState.Current or AssociateShowRowState.TimesChanged))
        {
            row.SetSelected(true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasShowRows))]
    private void ClearShows()
    {
        foreach (var row in ShowRows)
        {
            row.SetSelected(false);
        }
    }

    /// <summary>True while <see cref="CreatePdfCommand"/> is building/saving the PDF (spec Part I) — kept separate from <see cref="IsBusy"/> so a load/Check Online in progress is the only thing that disables the export action itself.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePdfCommand))]
    public partial bool IsExportingPdf { get; set; }

    /// <summary>Concise post-export feedback (spec Part I #8) — success or a user-facing failure message. A cancelled picker leaves this null; cancelling is normal, not an error (spec Part I "Cancellation").</summary>
    [ObservableProperty]
    public partial string? PdfExportMessage { get; set; }

    /// <summary>
    /// The real, authoritative page count of the most recently generated PDF (Phase 13D Part AA/AB) —
    /// never a preflight estimate. Defaults to 1 (no warning) until a PDF has actually been generated
    /// for the current session; only updated when generation actually produces bytes, so it is never
    /// reset to a false "no warning" state merely because <see cref="CreatePdfCommand"/> was pressed
    /// again (Part Z: repeated presses must not hide/reset the warning).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiPagePdfWarning))]
    [NotifyPropertyChangedFor(nameof(MultiPageWarningText))]
    public partial int PdfPageCount { get; set; } = 1;

    public bool HasMultiPagePdfWarning => PdfPageCount > 1;

    public string? MultiPageWarningText => HasMultiPagePdfWarning
        ? $"WARNING — Passdown is {PdfPageCount} pages. Reduce content before printing if possible."
        : null;

    /// <summary>Phase 18F: subtle feedback for the last Copy action ("Today's pricing copied", "No pricing selected", or a short failure message). Null when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string? CopyMessage { get; set; }

    /// <summary>True when <see cref="CopyMessage"/> reports a clipboard failure (shown in the error color) rather than a confirmation.</summary>
    [ObservableProperty]
    public partial bool CopyMessageIsError { get; set; }

    public AssociateViewModel(
        IAssociatePassdownService service,
        IClock clock,
        ILogger<AssociateViewModel>? logger = null,
        TimeSpan? autosaveDebounce = null,
        Func<TimeSpan, CancellationToken, Task>? autosaveDelay = null,
        IAssociatePassdownPdfExportService? pdfExportService = null,
        IPdfSaveFilePicker? pdfSavePicker = null,
        IErtConfigurationService? ertConfigurationService = null,
        IAssociatePassdownTemplatePreferenceStore? templatePreferenceStore = null,
        ISyncLocalChangeNotifier? syncLocalChangeNotifier = null,
        IClipboardService? clipboardService = null,
        IOfficialDataRefreshCoordinator? refreshCoordinator = null,
        IDataChangeNotifier? dataChangeNotifier = null,
        OfficialDataStatusViewModel? officialDataStatus = null,
        IUiDispatcher? uiDispatcher = null,
        IExternalLinkLauncher? externalLinkLauncher = null,
        IMorningMessagePreferenceStore? morningMessagePreferenceStore = null,
        IMorningMessageContentStore? morningMessageContentStore = null,
        IImageFilePicker? imageFilePicker = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _ertConfigurationService = ertConfigurationService ?? new UnavailableErtConfigurationService();
        _logger = logger;
        _autosaveDebounce = autosaveDebounce ?? DefaultAutosaveDebounce;
        _autosaveDelay = autosaveDelay ?? Task.Delay;
        _pdfExportService = pdfExportService ?? new AssociatePassdownPdfExportService(new AssociatePassdownPdfGenerator());
        _pdfSavePicker = pdfSavePicker ?? new UnavailablePdfSaveFilePicker();
        _templatePreferenceStore = templatePreferenceStore ?? new FileAssociatePassdownTemplatePreferenceStore(FileAssociatePassdownTemplatePreferenceStore.DefaultPath);
        _clipboardService = clipboardService ?? new UnavailableClipboardService();
        _refreshCoordinator = refreshCoordinator;
        _uiDispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();
        _externalLinkLauncher = externalLinkLauncher;
        OfficialDataStatus = officialDataStatus;

        // Phase 29: the Morning Message composer reads this screen's current date and on-screen edits
        // (the same projection Copy and Create PDF use); it never changes them.
        MorningMessage = new MorningMessageViewModel(
            _clipboardService,
            EffectiveWorkspaceForCopy,
            () => EasternToday,
            () => _clock.UtcNow,
            morningMessagePreferenceStore,
            morningMessageContentStore,
            imageFilePicker,
            logger);

        // "Today" in the park's own wall-clock time (spec §6.5/§6.6 use the same Eastern
        // convention for "today") rather than the host machine's arbitrary local time zone.
        SelectedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);

        // Phase 17C §15: loaded once here, independent of the date-load pipeline above — the
        // template preference is device/presentation state, never part of a date's workspace.
        _pendingTemplatePreferenceLoad = LoadTemplatePreferenceAsync();

        // Phase 18B REL-004: published from a background sync pass; every reaction runs on the UI thread.
        if (syncLocalChangeNotifier is not null)
        {
            syncLocalChangeNotifier.LocalDataChanged += (_, changed) =>
                Dispatcher.UIThread.Post(() => _pendingSyncReload = HandleSyncLocalChangesAsync(changed));
        }

        // Phase 19A: newer official data (an automatic check, or a refresh started elsewhere) reloads the
        // presentation of the selected date. Personal Sync changes keep their own REL-004 path above.
        if (dataChangeNotifier is not null)
        {
            dataChangeNotifier.Changed += (_, change) =>
            {
                if (change.Kind is DataChangeKind.OfficialData or DataChangeKind.HistoricalOfficialData)
                {
                    _uiDispatcher.Post(() => _pendingOfficialDataReload = HandleOfficialDataChangedAsync(change));
                }
            };
        }
    }

    /// <summary>The in-flight (or most recent) automatic date check, so a test can await it.</summary>
    public Task PendingAutoCheck => _pendingAutoCheck;

    /// <summary>The in-flight (or most recent) reload triggered by newer official data, so a test can await it.</summary>
    public Task PendingOfficialDataReload => _pendingOfficialDataReload;

    /// <summary>
    /// Phase 19A: after a date's local data is shown, quietly check it online when it is today or a
    /// future date whose official data is missing or stale (the coordinator decides; past dates never
    /// go online automatically). Any earlier automatic check is superseded first, so rapid date changes
    /// never fan out into overlapping requests, and a late result can never be applied to another date
    /// — the reload it triggers is keyed to the date it checked.
    /// </summary>
    private void StartAutomaticDateCheck(DateOnly date)
    {
        CancelAutomaticDateCheck();
        if (_refreshCoordinator is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _autoCheckCts = cts;
        _pendingAutoCheck = RunAutomaticDateCheckAsync(date, cts);
    }

    private void CancelAutomaticDateCheck()
    {
        _autoCheckCts?.Cancel();
        _autoCheckCts = null;
        IsAutoCheckingDate = false;
    }

    private async Task RunAutomaticDateCheckAsync(DateOnly date, CancellationTokenSource cts)
    {
        try
        {
            await _refreshCoordinator!.CheckDateIfStaleAsync(
                date,
                onCheckStarted: () => _uiDispatcher.Post(() =>
                {
                    if (_autoCheckCts == cts)
                    {
                        IsAutoCheckingDate = true;
                    }
                }),
                cts.Token).ConfigureAwait(true);

            // A completed check publishes an OfficialData change for this date; HandleOfficialDataChangedAsync
            // applies it (only if this date is still selected and the editor is clean).
        }
        catch (OperationCanceledException)
        {
            // Superseded by another date, Check Online, or shutdown.
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Automatic online check failed for {OperatingDate}; cached information is still shown.", date);
        }
        finally
        {
            if (_autoCheckCts == cts)
            {
                _autoCheckCts = null;
                IsAutoCheckingDate = false;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Phase 19A: re-reads the selected date's local workspace after official cached data changed, so
    /// Associate, Create PDF and Copy all read the same newer prices (they all consume
    /// <see cref="Workspace"/>). Never touches the network, never autosaves (a load is applied through
    /// <see cref="ApplyWorkspaceToFields"/>, which is not a user edit), and never overwrites unsaved
    /// edits: a dirty editor defers the reload until its next successful autosave. Manual values
    /// (attendance, pre-solds, gate/hours overrides, display-price overrides) live in the draft, which an
    /// online refresh never writes, so they always survive. Internal so tests can drive it directly.
    /// </summary>
    internal async Task HandleOfficialDataChangedAsync(DataChange change)
    {
        try
        {
            var date = SelectedDate;
            if (!change.MayAffect(date) || IsCheckingOnline || _draftDateInEdit != date)
            {
                return; // Check Online applies its own result; a load in flight reads fresh data itself.
            }

            if (_isDirty || _saveInFlight || IsErtEditorDirty())
            {
                _officialDataReloadDeferred = true;
                return;
            }

            await ReloadOfficialDataAsync(date).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to show newer official data for the selected date; the previous information is still shown.");
        }
    }

    private async Task ReloadOfficialDataAsync(DateOnly date)
    {
        _officialDataReloadDeferred = false;
        var ertPanelWasOpen = IsErtEditorOpen;

        var workspace = await _service.LoadAssociateDateAsync(date).ConfigureAwait(true);
        if (date != SelectedDate || _draftDateInEdit != date || _isDirty || _saveInFlight || IsCheckingOnline || IsErtEditorDirty())
        {
            _officialDataReloadDeferred = date == SelectedDate;
            return;
        }

        if (Workspace is { } current && current.OperatingDate == date && HasSameEditableOfficialContent(current, workspace))
        {
            // Nothing an associate edits changed (only verification timestamps): swap the workspace in
            // without rebuilding the product rows, so focus and scroll position are left alone.
            Workspace = workspace;
            _draftBaseHash = workspace.DraftContentHash;
            return;
        }

        ApplyWorkspaceToFields(workspace);
        IsErtEditorOpen = ertPanelWasOpen;
    }

    /// <summary>True when two workspaces for the same date show the same official products, prices and hours — differing at most in when they were verified.</summary>
    private static bool HasSameEditableOfficialContent(AssociateWorkspace current, AssociateWorkspace reloaded)
    {
        var unverified = AssociateVerificationInfo.NotCheckedYet;
        if (current.ParkHours with { Verification = unverified } != reloaded.ParkHours with { Verification = unverified }
            || current.Products.Count != reloaded.Products.Count)
        {
            return false;
        }

        return current.Products.Zip(reloaded.Products).All(pair =>
            pair.First with { Verification = unverified } == pair.Second with { Verification = unverified });
    }

    private Task _pendingSyncReload = Task.CompletedTask;

    /// <summary>
    /// Phase 18B REL-004 product rule: "auto-reload clean editors; preserve dirty editors and require
    /// explicit conflict resolution." Reloads the selected date only when it has no unsaved draft edits
    /// (and no save in flight) — re-checked after the read, since typing can happen while it is in
    /// flight. A dirty draft is left exactly as the associate has it; its next save is conditional on
    /// the version it loaded, so it becomes an explicit conflict rather than an overwrite. An open
    /// Early Ride Time panel keeps its unsaved values the same way and is only re-filled when clean.
    /// Internal so tests can drive it without a dispatcher.
    /// </summary>
    internal async Task HandleSyncLocalChangesAsync(IReadOnlyCollection<SyncEntityKey> changed)
    {
        try
        {
            var date = SelectedDate;
            if (_draftDateInEdit != date)
            {
                return; // A load of this date is already in flight and will read the new data itself.
            }

            var draftChanged = changed.Contains(new SyncEntityKey(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(date)));
            var monthlyChanged = changed.Contains(new SyncEntityKey(SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(date.Year, date.Month)));
            var overrideChanged = changed.Contains(new SyncEntityKey(SyncEntityType.ErtDateOverride, SyncEntityIdentity.ForErtDateOverride(date)));
            var notesChanged = changed.Any(key => key.EntityType == SyncEntityType.OperationalNote);
            if (!draftChanged && !monthlyChanged && !overrideChanged && !notesChanged)
            {
                return;
            }

            var ertPanelWasOpen = IsErtEditorOpen;
            var ertPanelClean = !IsErtEditorDirty();

            if (!_isDirty && !_saveInFlight)
            {
                var workspace = await _service.LoadAssociateDateAsync(date).ConfigureAwait(true);
                if (date == SelectedDate && _draftDateInEdit == date && !_isDirty && !_saveInFlight)
                {
                    ApplyWorkspaceToFields(workspace); // Closes the ERT panel but never touches its field values.
                    IsErtEditorOpen = ertPanelWasOpen;
                }
            }

            if (notesChanged && date == SelectedDate)
            {
                await RefreshOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);
            }

            if (ertPanelWasOpen && ertPanelClean && (monthlyChanged || overrideChanged) && date == SelectedDate && !IsErtEditorDirty())
            {
                await LoadErtEditorFieldsAsync(date).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reload is a convenience; a failure leaves the editor as it was, still protected by its
            // conditional saves.
            _logger?.LogWarning(ex, "Failed to reload the Associate screen after a Personal Sync change.");
        }
    }

    private bool IsErtEditorDirty() =>
        IsErtEditorOpen && _ertEditorLoadedFields is { } loaded && loaded != CurrentErtEditorFields();

    private ErtEditorFields CurrentErtEditorFields() =>
        new(MonthlyErtNoErt, MonthlyErtRidesText, MonthlyErtNoteText, ErtUseOverrideForDate, OverrideNoErt, OverrideRidesText, OverrideReasonText);

    private async Task LoadTemplatePreferenceAsync()
    {
        var preference = await _templatePreferenceStore.LoadAsync().ConfigureAwait(true);

        // Guards against OnSelectedTemplateOptionChanged immediately re-saving the value it was just
        // loaded with — a redundant but otherwise harmless write this avoids purely for cleanliness.
        _isLoadingTemplatePreference = true;
        try
        {
            SelectedTemplateOption = AssociatePassdownTemplateOption.For(preference.Template);
        }
        finally
        {
            _isLoadingTemplatePreference = false;
        }
    }

    /// <summary>Lets a test await the initial template-preference load deterministically instead of racing the constructor's fire-and-forget task.</summary>
    public Task PendingTemplatePreferenceLoad => _pendingTemplatePreferenceLoad;

    /// <summary>Lets a test await the most recently triggered template-preference save deterministically.</summary>
    public Task PendingTemplatePreferenceSave => _pendingTemplatePreferenceSave;

    public IReadOnlyList<AssociatePassdownTemplateOption> TemplateOptions => AssociatePassdownTemplateOption.All;

    /// <summary>
    /// Persists the new choice immediately (spec §15 "should persist locally") — never queued through
    /// the draft autosave debounce, since this is not draft data (spec §17: switching the template
    /// must never mark the draft unsaved or touch <see cref="Workspace"/>).
    /// </summary>
    partial void OnSelectedTemplateOptionChanged(AssociatePassdownTemplateOption value)
    {
        if (_isLoadingTemplatePreference)
        {
            return;
        }

        _pendingTemplatePreferenceSave = _templatePreferenceStore.SaveAsync(new AssociatePassdownTemplatePreference(value.Value));
    }

    public bool IsBusy => IsLoadingDate || IsCheckingOnline;

    /// <summary>
    /// A <see cref="DateTime"/>-shaped view of <see cref="SelectedDate"/> for Avalonia's
    /// <c>CalendarDatePicker</c> (spec §10.2's "professional Windows control" with a calendar
    /// flyout), which has no <see cref="DateOnly"/>-typed <c>SelectedDate</c>. Setting it goes
    /// through the same <see cref="SelectedDate"/> setter as every other date change, so it is
    /// still exactly one local-only load path (spec §6.3) — this is purely a binding-shape
    /// adapter, not a second way to change the date.
    /// </summary>
    public DateTime? SelectedDateForPicker
    {
        get => SelectedDate.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } picked)
            {
                SelectedDate = DateOnly.FromDateTime(picked.Date);
            }
        }
    }

    /// <summary>
    /// Phase 21B: the date header's text ("Friday, October 16, 2026" plus a Today/Yesterday/Tomorrow
    /// label), always relative to the Eastern operational date read at the moment it is shown.
    /// </summary>
    public AssociateDateNavigationPresentation DateNavigation => AssociateDateNavigationPresentation.Build(SelectedDate, EasternToday);

    /// <summary>
    /// Phase 21B: whether the calendar popup's built-in "today" marker (which follows the host's local
    /// date) may be shown — only when the host date agrees with the Eastern operational date.
    /// </summary>
    public bool IsCalendarTodayHighlighted =>
        AssociateDateNavigationPresentation.ShouldHighlightHostToday(_clock.UtcNow, TimeZoneInfo.Local, EasternToday);

    /// <summary>
    /// Phase 21B: re-reads "today" for the date header (e.g. when the calendar popup opens after the
    /// Eastern date has rolled over). Changes nothing else — never loads or retrieves.
    /// </summary>
    public void RefreshTodayIndicators()
    {
        OnPropertyChanged(nameof(DateNavigation));
        OnPropertyChanged(nameof(IsCalendarTodayHighlighted));
    }

    public bool HasWorkspace => Workspace is not null;

    /// <summary>
    /// Re-runs <see cref="ParkHoursCalculator.Calculate"/> against the workspace's official inputs
    /// plus whatever override fields are currently in the editable form (spec §8.2's "recalculate
    /// immediately when the manager changes the override") — never a save round-trip, and never a
    /// second, ViewModel-owned copy of the -30/-45 minute rule. Shared by <see cref="ParkHours"/> and
    /// the Phase 13F Early Ride Time derived-start properties below, so ERT/North Gate can never
    /// disagree with the Park Hours panel or with each other.
    /// </summary>
    private AssociateParkHoursView? LiveParkHours
    {
        get
        {
            if (Workspace is not { } workspace)
            {
                return null;
            }

            return RecomputeParkHours(
                workspace.ParkHours,
                ManualHoursOverrideEnabled, _manualOpeningTime, _manualClosingTime,
                PrivateBuyoutEnabled, _privateBuyoutOpeningTime, _privateBuyoutClosingTime,
                _northGateOverride, _avenueOverride);
        }
    }

    public ParkHoursPresentation? ParkHours => LiveParkHours is { } live ? ParkHoursPresentation.Build(live) : null;

    /// <summary>"September 2026" — the Associate-selected date's month/year (Phase 17B spec Part 6/7), used by both the effective-ERT "Not configured for ..." message and the Configure Monthly ERT panel's own heading.</summary>
    public string SelectedMonthLabel => SelectedDate.ToString("MMMM yyyy", PresentationCulture.UnitedStates);

    /// <summary>"Use override for Sep 19" — Phase 18B: formatted here in en-US rather than by a XAML <c>StringFormat</c>, which would use the machine culture.</summary>
    public string UseOverrideForDateLabel => $"Use override for {SelectedDate.ToString("MMM d", PresentationCulture.UnitedStates)}";

    private EffectiveErt EffectiveEarlyRideTime => Workspace?.EarlyRideTime ?? EffectiveErt.NotConfigured;

    public bool EarlyRideTimeIsConfigured => EffectiveEarlyRideTime.Kind == EffectiveErtKind.Configured;

    public bool EarlyRideTimeIsNotConfigured => EffectiveEarlyRideTime.Kind == EffectiveErtKind.NotConfigured;

    public bool EarlyRideTimeIsNoErt => EffectiveEarlyRideTime.Kind == EffectiveErtKind.NoErt;

    /// <summary>Whether the selected date's effective ERT came from a date override rather than the monthly default (Phase 17B spec Part 12) — drives the "Date override" caption and which action ("Override for this date" vs. "Remove override") the panel offers.</summary>
    public bool EarlyRideTimeIsOverride => EffectiveEarlyRideTime.Source == EffectiveErtSource.Override;

    /// <summary>
    /// The compact effective-ERT line (Phase 17B spec Part 12; revised Phase 17D §2 — rides only,
    /// never a start time) — built entirely from <see cref="Workspace"/>'s already-resolved
    /// <see cref="AssociateWorkspace.EarlyRideTime"/>, the same value the Dashboard/PDF/finalized
    /// snapshot read; this view never derives ERT from park hours (spec Part 1/31) or fabricates a
    /// value for <see cref="EffectiveErtKind.NotConfigured"/> (spec Part 6).
    /// </summary>
    public string EarlyRideTimeStatusText => EffectiveEarlyRideTime.Kind switch
    {
        EffectiveErtKind.NotConfigured => $"Not configured for {SelectedMonthLabel}",
        EffectiveErtKind.NoErt => "None",
        _ => EffectiveEarlyRideTime.Rides.Count > 0
            ? string.Join(", ", EffectiveEarlyRideTime.Rides)
            : "Configured",
    };

    /// <summary>
    /// Re-runs <see cref="ParkHoursCalculator.Calculate"/> against <paramref name="official"/>'s raw
    /// inputs plus whichever override values are supplied, returning an updated view with only
    /// <see cref="AssociateParkHoursView.Effective"/> replaced. Shared by the live <see cref="ParkHours"/>
    /// display and by <see cref="SaveDraftNowAsync"/> — both need the exact same recalculation, so
    /// there is exactly one place that runs it, never two copies that could drift out of sync.
    /// </summary>
    private static AssociateParkHoursView RecomputeParkHours(
        AssociateParkHoursView official,
        bool manualOverrideEnabled, TimeOnly? manualOpening, TimeOnly? manualClosing,
        bool privateBuyoutEnabled, TimeOnly? buyoutOpening, TimeOnly? buyoutClosing,
        TimeOnly? northGateOverride, TimeOnly? avenueOverride)
    {
        var effective = ParkHoursCalculator.Calculate(
            official.OfficialIsClosed, official.OfficialOpening, official.OfficialClosing,
            manualOverrideEnabled, manualOpening, manualClosing,
            privateBuyoutEnabled, buyoutOpening, buyoutClosing,
            northGateOverride, avenueOverride);

        return official with { Effective = effective };
    }

    /// <summary>
    /// Soft, non-blocking range check (spec Phase 13A finding, preserved unchanged by Phase 13E Part
    /// M — <see cref="ParkHoursCalculator"/> itself has no ordering rule at all, so treating this as a
    /// hard block would be inventing new domain semantics without evidence, and could wrongly reject a
    /// genuine overnight closing time). Distinct from <see cref="ManualOpeningTimeError"/>/
    /// <see cref="ManualClosingTimeError"/>, which guard unparseable/missing text — this only fires
    /// once both times are individually valid.
    /// </summary>
    public string? ManualHoursWarningText =>
        ManualHoursOverrideEnabled && _manualOpeningTime is { } manualOpen && _manualClosingTime is { } manualClose && manualClose <= manualOpen
            ? "Closing time must be later than opening time."
            : null;

    public string? PrivateBuyoutHoursWarningText =>
        PrivateBuyoutEnabled && _privateBuyoutOpeningTime is { } buyoutOpen && _privateBuyoutClosingTime is { } buyoutClose && buyoutClose <= buyoutOpen
            ? "Closing time must be later than opening time."
            : null;

    /// <summary>
    /// Combines the current parser error (if the typed text is syntactically/semantically invalid,
    /// spec Part L) with the "required while enabled" rule (Part R) — blank is fine while Manual Hours
    /// is off, but not once it's on. Never surfaces a raw parser exception (Part L).
    /// </summary>
    public string? ManualOpeningTimeError => _manualOpeningTimeParseError ?? RequiredTimeError(ManualHoursOverrideEnabled, _manualOpeningTime);

    public string? ManualClosingTimeError => _manualClosingTimeParseError ?? RequiredTimeError(ManualHoursOverrideEnabled, _manualClosingTime);

    public string? PrivateBuyoutOpeningTimeError => _privateBuyoutOpeningTimeParseError ?? RequiredTimeError(PrivateBuyoutEnabled, _privateBuyoutOpeningTime);

    public string? PrivateBuyoutClosingTimeError => _privateBuyoutClosingTimeParseError ?? RequiredTimeError(PrivateBuyoutEnabled, _privateBuyoutClosingTime);

    private static string? RequiredTimeError(bool sectionEnabled, TimeOnly? committedValue) =>
        sectionEnabled && committedValue is null ? OperationalTimeParser.InvalidTimeMessage : null;

    public IReadOnlyList<DailyFeatureTypeOption> DailyFeatureTypeOptions => DailyFeatureTypeOption.All;

    public bool HasProductSections => ProductSections.Count > 0;

    /// <summary>
    /// Phase 21A: shown on the Products section for a past date — either that only prices recorded for
    /// that date are listed, or <see cref="HistoricalPricingPolicy.UnavailableMessage"/> when none were.
    /// Null for today/future dates. Rebuilt with <see cref="ProductSections"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoricalPricingNotice))]
    public partial string? HistoricalPricingNotice { get; set; }

    public bool HasHistoricalPricingNotice => HistoricalPricingNotice is not null;

    /// <summary>
    /// Phase 20A: asks the app shell to open the read-only Season Price History screen — with a
    /// product key from a tracked row's "Price history" action, or null from the Products section's
    /// general entry point (which also reaches retired products). Navigation only; no retrieval.
    /// </summary>
    public event EventHandler<string?>? PriceHistoryRequested;

    [RelayCommand]
    private void OpenPriceHistory() => PriceHistoryRequested?.Invoke(this, null);

    private void RequestPriceHistory(string productKey) => PriceHistoryRequested?.Invoke(this, productKey);

    public bool HasCustomProducts => CustomProducts.Count > 0;

    public SaveStatusPresentation? SaveStatus => Workspace is { } workspace ? SaveStatusPresentation.Build(workspace.SaveStatus) : null;

    /// <summary>The footer's actual text: the in-progress autosave state when there is one, otherwise the last persisted <see cref="SaveStatus"/> (spec §8.3/§10.3's "Saved locally at 2:14 PM").</summary>
    public string SaveStatusText => AutosaveState switch
    {
        AssociateAutosaveState.Saving => "Saving…",
        AssociateAutosaveState.Unsaved => "Unsaved changes",
        AssociateAutosaveState.SaveFailed => "Save failed — changes not saved",
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
    /// <summary>Today's operational date: the park's Eastern calendar date at this moment, never the host machine's local date.</summary>
    public DateOnly EasternToday => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(_clock.UtcNow).DateTime);

    /// <summary>
    /// Phase 21A: Dashboard's "Open Passdown" always lands on today's Eastern date — read at click
    /// time, so a date rollover since the last load is honored. Goes through the ordinary
    /// <see cref="SelectedDate"/> path (unsaved edits on the previous date are flushed first); when
    /// today is already selected nothing reloads, so there is no redundant load or check. Returns
    /// true when the date changed. Calendar and History routes keep their explicit dates.
    /// </summary>
    public bool SelectEasternToday() => SelectEasternToday("Dashboard Open Passdown");

    private bool SelectEasternToday(string trigger)
    {
        var today = EasternToday;
        if (SelectedDate == today)
        {
            // Already today: nothing reloads, but the header's Today label may need to catch up with a
            // rollover that happened while this date stayed selected.
            RefreshTodayIndicators();
            return false;
        }

        _logger?.LogInformation("{Trigger} reset the Associate date from {PreviousDate} to today ({Today}).", trigger, SelectedDate, today);
        SelectedDate = today;
        return true;
    }

    /// <summary>
    /// Phase 21B: the date header's Today button — the same path as Dashboard → Open Passdown: today's
    /// Eastern date read at click time, unsaved edits flushed by the ordinary date switch, and no
    /// reload or retrieval when today is already selected.
    /// </summary>
    [RelayCommand]
    private void GoToToday() => SelectEasternToday("Associate Today");

    partial void OnSelectedDateChanged(DateOnly value)
    {
        if (_isRestoringSelectedDate)
        {
            return; // Phase 23A: a blocked switch putting the date back — the editor never left it.
        }

        CopyMessage = null; // a "copied" confirmation belongs to the date it was copied from.
        CancelAutomaticDateCheck(); // Phase 19A: an automatic check of the previous date is superseded.
        _pendingLoad = SwitchDateAsync(value);
    }

    private async Task SwitchDateAsync(DateOnly newDate)
    {
        var dateInEdit = _draftDateInEdit;
        if (!await FlushPendingAutosaveAsync().ConfigureAwait(true))
        {
            // Phase 23A: loading newDate would replace the unsaved edits on screen with newDate's stored
            // draft, so the switch is refused instead: the editor stays on the date it was editing, with
            // every edit intact, until a save succeeds. Only restored when no newer switch superseded
            // this one meanwhile.
            if (dateInEdit is { } previous && SelectedDate == newDate && previous != newDate)
            {
                _isRestoringSelectedDate = true;
                try
                {
                    SelectedDate = previous;
                }
                finally
                {
                    _isRestoringSelectedDate = false;
                }
            }

            SaveBlockedMessage = DateSwitchBlockedMessage;
            return;
        }

        await LoadDateAsync(newDate).ConfigureAwait(true);
    }

    private async Task LoadDateAsync(DateOnly date)
    {
        var version = ++_loadVersion;
        IsLoadingDate = true;
        LoadErrorMessage = null;
        SyncConflictMessage = null;
        try
        {
            var workspace = await _service.LoadAssociateDateAsync(date);
            if (version != _loadVersion)
            {
                return; // a newer date was selected before this one finished composing.
            }

            ApplyWorkspaceToFields(workspace);
            _officialDataReloadDeferred = false;
            await RefreshOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);

            // Phase 19A: cached data is already on screen; only now consider a quiet online check.
            if (version == _loadVersion)
            {
                StartAutomaticDateCheck(date);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load the local Associate draft/cache for {OperatingDate}.", date);
            if (version == _loadVersion)
            {
                LoadErrorMessage = "This date could not be loaded from your saved information. Try selecting it again.";

                // Phase 15C: a failed load must not leave the previous date's workspace looking
                // "current" for SelectedDate. Two concrete risks this closes: (1) ScheduleAutosave
                // reads _draftDateInEdit, not SelectedDate, to decide where an edit is saved — left
                // pointing at the old date, a keystroke made after this failure would silently
                // autosave onto the wrong date's record; clearing it makes ScheduleAutosave a no-op
                // (still marks AutosaveState Unsaved so the associate can see nothing is being
                // saved) until a load actually succeeds. (2) CanCreatePdf/CreatePdfAsync read
                // Workspace directly — left non-null, "Create PDF" would silently stay enabled and
                // generate a PDF for the stale prior date instead of the one currently selected.
                Workspace = null;
                _draftDateInEdit = null;
                _isDirty = false;
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
        CancelAutomaticDateCheck(); // Phase 19A: the explicit request runs now, never queued behind the quiet one.
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
            if (!await FlushPendingAutosaveAsync().ConfigureAwait(true))
            {
                // Phase 23A: the recompose below re-reads the stored draft, which would replace the
                // unsaved edits on screen — so nothing is checked (no network request) until they save.
                SaveBlockedMessage = CheckOnlineBlockedMessage;
                return;
            }

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

    /// <summary>
    /// Spec Part I: flushes pending edits, builds the PDF from the current workspace, and hands the
    /// bytes to the native save-file picker. A cancelled picker is treated as normal (no message);
    /// only an actual failure produces user-facing text, and any unexpected exception is caught so
    /// the screen never crashes mid-export.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreatePdf))]
    private async Task CreatePdfAsync()
    {
        IsExportingPdf = true;
        PdfExportMessage = null;
        try
        {
            if (!await FlushPendingAutosaveAsync().ConfigureAwait(true))
            {
                // Phase 23A: the PDF is built from the saved workspace, which does not hold the edits on
                // screen — never hand out a PDF that silently omits them.
                SaveBlockedMessage = PdfBlockedMessage;
                return;
            }

            if (Workspace is not { } workspace)
            {
                return;
            }

            var result = _pdfExportService.GeneratePdf(workspace, DateTimeOffset.Now, SelectedTemplateOption.Value);

            // Phase 13D Part AA/AB: reflects the real generated page count whenever a document was
            // actually produced — including an overflow that still generated onto extra pages.
            // Never overwritten by a validation failure that produced nothing (PageCount 0), so a
            // stale-but-real prior warning is not silently cleared by an unrelated failure.
            if (result.PageCount > 0)
            {
                PdfPageCount = result.PageCount;
            }

            if (result.PdfBytes is not { } pdfBytes)
            {
                _logger?.LogWarning("Associate PDF failed validation for {OperatingDate}: {Status}", workspace.OperatingDate, result.Preflight.Status);
                PdfExportMessage = result.Preflight.Reasons.Count > 0
                    ? string.Join(" ", result.Preflight.Reasons)
                    : "The PDF could not be created for this date yet.";
                return;
            }

            var fileName = string.Create(CultureInfo.InvariantCulture, $"Carowinds-Operations-{workspace.OperatingDate:yyyy-MM-dd}.pdf");
            var saveResult = await _pdfSavePicker.SaveAsync(pdfBytes, fileName).ConfigureAwait(true);

            PdfExportMessage = saveResult.Outcome switch
            {
                PdfSaveOutcome.Saved => "PDF saved.",
                PdfSaveOutcome.Cancelled => null,
                _ => saveResult.ErrorMessage ?? "The PDF could not be saved. Try again.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Create PDF failed unexpectedly for {OperatingDate}.", SelectedDate);
            PdfExportMessage = "The PDF could not be created. Try again.";
        }
        finally
        {
            IsExportingPdf = false;
        }
    }

    private bool CanCreatePdf() => HasWorkspace && !IsBusy && !IsExportingPdf;

    /// <summary>
    /// Phase 18F: copies Teams-friendly plain text for the currently open date (Phase 29: with the formatted
    /// version alongside when the saved default copy mode is Formatted; the plain text is unchanged). Built from the same
    /// <see cref="AssociatePassdownDocumentModelBuilder"/> model the PDF renders — over the current
    /// editable fields projected onto <see cref="Workspace"/> in memory (<see cref="WithDraftApplied"/>)
    /// — so copied pricing follows the PDF's section/product selections exactly. Never retrieves,
    /// refreshes, saves, finalizes, or changes the selected date; a clipboard failure is logged and
    /// reported as a short message, never thrown.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyForTeams))]
    private async Task CopyForTeamsAsync(AssociateTeamsCopyKind kind)
    {
        CopyMessage = null;
        CopyMessageIsError = false;

        if (Workspace is not { } workspace)
        {
            return;
        }

        var model = AssociatePassdownDocumentModelBuilder.Build(EffectiveWorkspaceForCopy()!, DateTimeOffset.Now);
        var message = AssociateTeamsTextFormatter.BuildMessage(model, kind);

        if (message is null)
        {
            CopyMessage = "No pricing selected";
            return;
        }

        // Phase 29: the saved default copy mode decides whether the formatted version goes alongside the
        // unchanged plain text; a failed formatted copy falls back to the same plain text.
        var outcome = await TeamsClipboard.CopyAsync(_clipboardService, message, MorningMessage.Preferences.DefaultCopyMode, _logger, $"{kind} for {workspace.OperatingDate}").ConfigureAwait(true);
        if (outcome == TeamsCopyOutcome.Failed)
        {
            CopyMessage = TeamsClipboard.FailureMessage;
            CopyMessageIsError = true;
            return;
        }

        CopyMessage = kind switch
        {
            AssociateTeamsCopyKind.OperationsSummary => "Operations summary copied",
            AssociateTeamsCopyKind.TodaysPricing => "Today's pricing copied",
            _ => AssociateTeamsTextFormatter.FormatPricing(model) is not null
                ? "Operations and pricing copied"
                : "Operations summary copied (no pricing selected)",
        } + (outcome == TeamsCopyOutcome.CopiedAsPlainTextFallback ? TeamsClipboard.FallbackSuffix : string.Empty);
    }

    private bool CanCopyForTeams(AssociateTeamsCopyKind kind) => HasWorkspace && !IsBusy;

    /// <summary>Phase 29: the Teams Morning Message composer for this screen's open date.</summary>
    public MorningMessageViewModel MorningMessage { get; }

    /// <summary>Phase 29: Copy ▾ → Morning Message… opens the composer over the current date's on-screen state.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyForTeamsAny))]
    private Task OpenMorningMessageAsync()
    {
        CopyMessage = null;
        CopyMessageIsError = false;
        return MorningMessage.OpenAsync();
    }

    /// <summary>
    /// Phase 29: Copy ▾ → OD Notes copies this date's OD Notes (typed in the Morning Message composer) on
    /// their own, in the saved default copy mode — never appended to the morning message.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyForTeamsAny))]
    private async Task CopyOdNotesAsync()
    {
        CopyMessage = null;
        CopyMessageIsError = false;
        if (Workspace is not { } workspace)
        {
            return;
        }

        var outcome = await MorningMessage.CopySavedOdNotesAsync(workspace.OperatingDate).ConfigureAwait(true);
        (CopyMessage, CopyMessageIsError) = outcome switch
        {
            null => ("No OD Notes for this date — add them in Morning Message.", false),
            TeamsCopyOutcome.Copied => ("OD Notes copied", false),
            TeamsCopyOutcome.CopiedAsPlainTextFallback => ("OD Notes copied" + TeamsClipboard.FallbackSuffix, false),
            _ => (TeamsClipboard.FailureMessage, true),
        };
    }

    private bool CanCopyForTeamsAny() => HasWorkspace && !IsBusy;

    /// <summary>
    /// The open date's workspace with the on-screen (possibly unsaved) edits applied in memory — the same
    /// projection autosave uses — read by Copy and the Morning Message. Null when nothing is loaded. Never saves.
    /// </summary>
    private AssociateWorkspace? EffectiveWorkspaceForCopy() =>
        Workspace is not { } workspace ? null
        : _draftDateInEdit == workspace.OperatingDate ? WithDraftApplied(workspace, BuildDraft())
        : workspace;

    /// <summary>
    /// Phase 21B: "More → PDF Style". Sets the same <see cref="SelectedTemplateOption"/> the PDF Style
    /// picker always used, so the saved template preference behaves exactly as before.
    /// </summary>
    [RelayCommand]
    private void SelectTemplate(AssociatePassdownTemplate template) => SelectedTemplateOption = AssociatePassdownTemplateOption.For(template);

    public bool IsClassicTemplateSelected => SelectedTemplateOption.Value == AssociatePassdownTemplate.Classic;

    public bool IsCompactTemplateSelected => SelectedTemplateOption.Value == AssociatePassdownTemplate.Compact;

    public bool IsModernTemplateSelected => SelectedTemplateOption.Value == AssociatePassdownTemplate.Modern;

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

    /// <summary>
    /// Explicit inclusion of one persistent Operational Note into the date currently in edit (Phase
    /// 11D Part B/Q). Flushes any pending manual-field edit first so the Application-layer
    /// read-modify-write in <see cref="IAssociatePassdownService.SelectOperationalNoteAsync"/>
    /// operates against exactly what is on screen, not stale on-disk state. The returned workspace
    /// (already persisted — this is an immediate save, not routed through the debounce timer) is
    /// applied back via <see cref="ApplyWorkspaceToFields"/> exactly like a Check Online result, so
    /// <see cref="SelectedOperationalNotes"/> and every other field stay consistent with what was
    /// actually saved.
    /// </summary>
    private async Task AddOperationalNoteAsync(Guid operationalNoteId)
    {
        if (!await FlushPendingAutosaveAsync().ConfigureAwait(true))
        {
            SaveBlockedMessage = NoteChangeBlockedMessage; // Phase 23A: the call below would reload over the unsaved edits.
            return;
        }

        var date = SelectedDate;
        try
        {
            var workspace = await _service.SelectOperationalNoteAsync(date, operationalNoteId).ConfigureAwait(true);
            if (date != SelectedDate)
            {
                return; // superseded by a date switch while the selection was in flight (Part Z).
            }

            ApplyWorkspaceToFields(workspace);
            await RefreshOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to select operational note {OperationalNoteId} for {OperatingDate}.", operationalNoteId, date);
        }
    }

    /// <summary>Removes a previously selected persistent note from the date currently in edit (Phase 11D Part AH) — mirrors <see cref="AddOperationalNoteAsync"/>.</summary>
    private async Task RemoveOperationalNoteAsync(Guid operationalNoteId)
    {
        if (!await FlushPendingAutosaveAsync().ConfigureAwait(true))
        {
            SaveBlockedMessage = NoteChangeBlockedMessage; // Phase 23A: the call below would reload over the unsaved edits.
            return;
        }

        var date = SelectedDate;
        try
        {
            var workspace = await _service.DeselectOperationalNoteAsync(date, operationalNoteId).ConfigureAwait(true);
            if (date != SelectedDate)
            {
                return;
            }

            ApplyWorkspaceToFields(workspace);
            await RefreshOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to deselect operational note {OperationalNoteId} for {OperatingDate}.", operationalNoteId, date);
        }
    }

    /// <summary>
    /// The one deliberate way an already-selected note's frozen copy can change (Phase 11E Part I) —
    /// pulls the source note's current display content in explicitly, never automatically (Part P).
    /// Mirrors <see cref="AddOperationalNoteAsync"/>/<see cref="RemoveOperationalNoteAsync"/> exactly:
    /// flush-then-call-then-apply, with the same in-flight date-switch guard (Part Z).
    /// </summary>
    private async Task ReplaceOperationalNoteAsync(Guid operationalNoteId)
    {
        if (!await FlushPendingAutosaveAsync().ConfigureAwait(true))
        {
            SaveBlockedMessage = NoteChangeBlockedMessage; // Phase 23A: the call below would reload over the unsaved edits.
            return;
        }

        var date = SelectedDate;
        try
        {
            var workspace = await _service.ReplaceOperationalNoteWithCurrentAsync(date, operationalNoteId).ConfigureAwait(true);
            if (date != SelectedDate)
            {
                return;
            }

            ApplyWorkspaceToFields(workspace);
            await RefreshOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to replace operational note {OperationalNoteId} for {OperatingDate}.", operationalNoteId, date);
        }
    }

    /// <summary>
    /// Recomputes <see cref="AvailableOperationalNoteSuggestions"/> for <paramref name="date"/> —
    /// every persistent note currently relevant for that date, minus whatever is already in
    /// <see cref="SelectedOperationalNotes"/> (Phase 11D Part U). Local-only; a failure degrades to
    /// an empty suggestion list rather than surfacing an error, since this is a secondary/advisory
    /// list, not the draft itself. Guarded against a date switch that completed while this call was
    /// in flight (Part Z) exactly like <see cref="AddOperationalNoteAsync"/>.
    /// </summary>
    private async Task RefreshOperationalNoteSuggestionsAsync(DateOnly date)
    {
        IReadOnlyList<OperationalNote> relevant;
        try
        {
            relevant = await _service.GetRelevantOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load operational note suggestions for {OperatingDate}.", date);
            relevant = [];
        }

        if (date != SelectedDate)
        {
            return;
        }

        var selectedIds = SelectedOperationalNotes.Select(row => row.SourceOperationalNoteId).ToHashSet();
        AvailableOperationalNoteSuggestions = relevant
            .Where(note => !selectedIds.Contains(note.Id))
            .Select(note => new AssociateOperationalNoteSuggestionRowViewModel(
                AssociateOperationalNoteSuggestionPresentation.Build(note, date), AddOperationalNoteAsync))
            .ToList();
    }

    /// <summary>
    /// Opens the single compact Early Ride Time editor (Phase 17D §3), loading both the persisted
    /// monthly record for the selected date's month and any existing date override in one operation
    /// so the Monthly Default and Date Override sections both start pre-filled. Monthly fields are
    /// pre-filled from the <em>persisted monthly record</em>, never from
    /// <see cref="EffectiveEarlyRideTime"/> — that value may currently be coming from a date override,
    /// and editing "the month" must always start from the month's own saved default.
    /// </summary>
    [RelayCommand]
    private async Task OpenErtEditorAsync()
    {
        var date = SelectedDate;
        MonthlyErtSaveError = null;
        OverrideSaveError = null;

        if (await LoadErtEditorFieldsAsync(date).ConfigureAwait(true))
        {
            IsErtEditorOpen = true;
        }
    }

    /// <summary>Fills the Early Ride Time panel from the persisted records and records their hashes as the base of the panel's next saves (Phase 18B REL-004). Returns false when superseded by a date switch.</summary>
    private async Task<bool> LoadErtEditorFieldsAsync(DateOnly date)
    {
        var existingMonthly = await _ertConfigurationService.GetMonthlyAsync(date.Year, date.Month).ConfigureAwait(true);
        var existingOverride = await _ertConfigurationService.GetOverrideAsync(date).ConfigureAwait(true);
        if (date != SelectedDate)
        {
            return false; // superseded by a date switch while the reads were in flight.
        }

        MonthlyErtNoErt = existingMonthly?.State == ErtConfigurationState.NoErt;
        MonthlyErtRidesText = existingMonthly is null ? null : string.Join(Environment.NewLine, existingMonthly.Rides);
        MonthlyErtNoteText = existingMonthly?.Note;

        ErtUseOverrideForDate = existingOverride is not null;
        OverrideNoErt = existingOverride?.State == ErtConfigurationState.NoErt;
        OverrideRidesText = existingOverride is null ? null : string.Join(Environment.NewLine, existingOverride.Rides);
        OverrideReasonText = existingOverride?.Reason;

        _monthlyErtBaseHash = IErtConfigurationService.ContentHashOf(existingMonthly);
        _overrideBaseHash = IErtConfigurationService.ContentHashOf(existingOverride);
        _ertEditorLoadedFields = CurrentErtEditorFields();
        return true;
    }

    [RelayCommand]
    private void CancelErtEditor()
    {
        IsErtEditorOpen = false;
        MonthlyErtSaveError = null;
        OverrideSaveError = null;
    }

    /// <summary>
    /// Validates and saves the Monthly Default section (spec Part 7/33) — only ever runs on this
    /// explicit "Save Monthly" action, never implicitly from typing. No start time is collected or
    /// sent (Phase 17D §1). On success, reloads the workspace so <see cref="EarlyRideTimeStatusText"/>
    /// and every other resolver-driven display refreshes together (spec Part 4's "one authoritative
    /// resolver").
    /// </summary>
    [RelayCommand]
    private async Task SaveMonthlyErtAsync()
    {
        var date = SelectedDate;
        var (state, rides) = BuildErtDraftFields(MonthlyErtNoErt, MonthlyErtRidesText);

        MonthlyErtSaveError = null;
        try
        {
            var result = await _ertConfigurationService.SaveMonthlyFromEditorAsync(
                new MonthlyErtDraft(date.Year, date.Month, state, StartTime: null, rides, NullIfBlank(MonthlyErtNoteText)), _monthlyErtBaseHash).ConfigureAwait(true);
            _monthlyErtBaseHash = result.ContentHash;
            if (result.ConflictDetected)
            {
                SyncConflictMessage = ErtSyncConflictMessage;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to save monthly Early Ride Time for {Year}-{Month}.", date.Year, date.Month);
            MonthlyErtSaveError = "Could not save. Try again.";
            return;
        }

        if (date != SelectedDate)
        {
            return; // superseded by a date switch while the save was in flight.
        }

        await RefreshWorkspaceAsync(date).ConfigureAwait(true);
        IsErtEditorOpen = true; // ApplyWorkspaceToFields closes it unconditionally; Phase 17D §3 keeps the shared panel open across an in-panel Save.
        _ertEditorLoadedFields = CurrentErtEditorFields();
    }

    /// <summary>Validates and saves the Date Override section (spec Part 8/9/33) — mirrors <see cref="SaveMonthlyErtAsync"/>, scoped to <see cref="SelectedDate"/> instead of its month. No start time is collected or sent (Phase 17D §1).</summary>
    [RelayCommand]
    private async Task SaveOverrideAsync()
    {
        var date = SelectedDate;
        var (state, rides) = BuildErtDraftFields(OverrideNoErt, OverrideRidesText);

        OverrideSaveError = null;
        try
        {
            var result = await _ertConfigurationService.SaveOverrideFromEditorAsync(
                new ErtDateOverrideDraft(date, state, StartTime: null, rides, NullIfBlank(OverrideReasonText)), _overrideBaseHash).ConfigureAwait(true);
            _overrideBaseHash = result.ContentHash;
            if (result.ConflictDetected)
            {
                SyncConflictMessage = ErtSyncConflictMessage;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to save Early Ride Time override for {OperatingDate}.", date);
            OverrideSaveError = "Could not save. Try again.";
            return;
        }

        if (date != SelectedDate)
        {
            return;
        }

        ErtUseOverrideForDate = true;
        await RefreshWorkspaceAsync(date).ConfigureAwait(true);
        IsErtEditorOpen = true;
        _ertEditorLoadedFields = CurrentErtEditorFields();
    }

    /// <summary>Removes the selected date's override (spec Part 8/28) — the date immediately falls back to the monthly default once the workspace reloads.</summary>
    [RelayCommand]
    private async Task RemoveOverrideAsync()
    {
        var date = SelectedDate;
        OverrideSaveError = null;
        try
        {
            var result = await _ertConfigurationService.RemoveOverrideFromEditorAsync(date, _overrideBaseHash).ConfigureAwait(true);
            _overrideBaseHash = result.ContentHash;
            if (result.ConflictDetected)
            {
                SyncConflictMessage = ErtSyncConflictMessage;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to remove Early Ride Time override for {OperatingDate}.", date);
            OverrideSaveError = "Could not remove. Try again.";
            return;
        }

        if (date != SelectedDate)
        {
            return;
        }

        ErtUseOverrideForDate = false;
        OverrideNoErt = false;
        OverrideRidesText = null;
        OverrideReasonText = null;
        await RefreshWorkspaceAsync(date).ConfigureAwait(true);
        IsErtEditorOpen = true;
        _ertEditorLoadedFields = CurrentErtEditorFields();
    }

    /// <summary>
    /// Shared validate-and-shape step for both the monthly and override editors (spec Part 33,
    /// revised Phase 17D §1): <see cref="ErtConfigurationState.NoErt"/> always clears rides (mirrors
    /// <c>ErtConfigurationService</c>'s own server-side normalization); otherwise rides are parsed
    /// with the same <see cref="ParseLines"/> newline-list idiom already used elsewhere in this class.
    /// No start time is collected or validated — Phase 17D removes the routine ERT time field entirely.
    /// </summary>
    private static (ErtConfigurationState State, IReadOnlyList<string> Rides) BuildErtDraftFields(bool noErt, string? ridesText) =>
        noErt ? (ErtConfigurationState.NoErt, []) : (ErtConfigurationState.Configured, ParseLines(ridesText));

    /// <summary>Reloads <see cref="Workspace"/> for <paramref name="date"/> after an ERT save/remove (spec Part 4/16) — a full <see cref="ApplyWorkspaceToFields"/> pass, mirroring how <see cref="AddOperationalNoteAsync"/>/<see cref="RemoveOperationalNoteAsync"/> already refresh the whole workspace after their own immediate, non-debounced service round-trip.</summary>
    private async Task RefreshWorkspaceAsync(DateOnly date)
    {
        var workspace = await _service.LoadAssociateDateAsync(date).ConfigureAwait(true);
        if (date != SelectedDate)
        {
            return;
        }

        ApplyWorkspaceToFields(workspace);
    }

    partial void OnManualHoursOverrideEnabledChanged(bool value)
    {
        NotifyGateOverrideDependents();
        ScheduleAutosave();
    }

    partial void OnPrivateBuyoutEnabledChanged(bool value)
    {
        NotifyGateOverrideDependents(); // a buyout can turn a Closed date Open, which is when entrance overrides apply.
        ScheduleAutosave();
    }

    partial void OnPrivateBuyoutDetailsChanged(string? value) => ScheduleAutosave();

    /// <summary>
    /// Phase 13E's typable-time commit path, shared by all four Manual Hours/Private Buyout fields
    /// via the delegates below so the actual parsing rule lives in exactly one place
    /// (<see cref="OperationalTimeParser"/>, spec Part G) even though each field still needs its own
    /// backing field/error/notifications. On success: the last-valid committed value updates, the
    /// field is rewritten to its canonical display if it isn't already (Part D/P — harmless no-op
    /// re-entry into the same handler when the text is already normalized), and autosave is scheduled.
    /// On failure: nothing about the committed value changes — an unparseable keystroke can never
    /// become active effective-hours data (Part N) — only the error message updates.
    /// </summary>
    private void CommitOperationalTimeText(
        string? text,
        Action<TimeOnly?> setCommitted,
        Action<string?> setParseError,
        Action<string?> setNormalizedText,
        Action notifyDependents)
    {
        if (!OperationalTimeParser.TryParse(text, out var parsed, out var error))
        {
            setParseError(error);
            return;
        }

        setParseError(null);
        setCommitted(parsed);
        notifyDependents();

        var normalized = parsed is { } time ? OperationalTimeFormatter.Format(time) : null;
        if (!string.Equals(normalized, text, StringComparison.Ordinal))
        {
            setNormalizedText(normalized);
        }

        ScheduleAutosave();
    }

    partial void OnManualOpeningTimeTextChanged(string? value) => CommitOperationalTimeText(
        value,
        setCommitted: v => _manualOpeningTime = v,
        setParseError: e => { _manualOpeningTimeParseError = e; OnPropertyChanged(nameof(ManualOpeningTimeError)); },
        setNormalizedText: t => ManualOpeningTimeText = t,
        notifyDependents: () =>
        {
            OnPropertyChanged(nameof(ManualOpeningTimeError));
            OnPropertyChanged(nameof(ParkHours));
            OnPropertyChanged(nameof(ManualHoursWarningText));
        });

    partial void OnManualClosingTimeTextChanged(string? value) => CommitOperationalTimeText(
        value,
        setCommitted: v => _manualClosingTime = v,
        setParseError: e => { _manualClosingTimeParseError = e; OnPropertyChanged(nameof(ManualClosingTimeError)); },
        setNormalizedText: t => ManualClosingTimeText = t,
        notifyDependents: () =>
        {
            OnPropertyChanged(nameof(ManualClosingTimeError));
            OnPropertyChanged(nameof(ParkHours));
            OnPropertyChanged(nameof(ManualHoursWarningText));
        });

    partial void OnPrivateBuyoutOpeningTimeTextChanged(string? value) => CommitOperationalTimeText(
        value,
        setCommitted: v => _privateBuyoutOpeningTime = v,
        setParseError: e => { _privateBuyoutOpeningTimeParseError = e; OnPropertyChanged(nameof(PrivateBuyoutOpeningTimeError)); },
        setNormalizedText: t => PrivateBuyoutOpeningTimeText = t,
        notifyDependents: () =>
        {
            OnPropertyChanged(nameof(PrivateBuyoutOpeningTimeError));
            OnPropertyChanged(nameof(ParkHours));
            OnPropertyChanged(nameof(PrivateBuyoutHoursWarningText));
        });

    partial void OnPrivateBuyoutClosingTimeTextChanged(string? value) => CommitOperationalTimeText(
        value,
        setCommitted: v => _privateBuyoutClosingTime = v,
        setParseError: e => { _privateBuyoutClosingTimeParseError = e; OnPropertyChanged(nameof(PrivateBuyoutClosingTimeError)); },
        setNormalizedText: t => PrivateBuyoutClosingTimeText = t,
        notifyDependents: () =>
        {
            OnPropertyChanged(nameof(PrivateBuyoutClosingTimeError));
            OnPropertyChanged(nameof(ParkHours));
            OnPropertyChanged(nameof(PrivateBuyoutHoursWarningText));
        });

    partial void OnNorthGateOverrideTextChanged(string? value) => CommitOperationalTimeText(
        value,
        setCommitted: v => _northGateOverride = v,
        setParseError: e => { _northGateOverrideParseError = e; OnPropertyChanged(nameof(NorthGateOverrideError)); },
        setNormalizedText: t => NorthGateOverrideText = t,
        notifyDependents: NotifyGateOverrideDependents);

    partial void OnAvenueOverrideTextChanged(string? value) => CommitOperationalTimeText(
        value,
        setCommitted: v => _avenueOverride = v,
        setParseError: e => { _avenueOverrideParseError = e; OnPropertyChanged(nameof(AvenueOverrideError)); },
        setNormalizedText: t => AvenueOverrideText = t,
        notifyDependents: NotifyGateOverrideDependents);

    private void NotifyGateOverrideDependents()
    {
        OnPropertyChanged(nameof(NorthGateOverrideError));
        OnPropertyChanged(nameof(AvenueOverrideError));
        OnPropertyChanged(nameof(ParkHours));
        OnPropertyChanged(nameof(NorthGateIsOverridden));
        OnPropertyChanged(nameof(AvenueIsOverridden));
        OnPropertyChanged(nameof(CanResetNorthGateOverride));
        OnPropertyChanged(nameof(CanResetAvenueOverride));
    }

    partial void OnProjectedAttendanceTextChanged(string? value)
    {
        if (TryParseNonNegativeInt(value, out var parsed, out var error))
        {
            ProjectedAttendanceError = null;
            _projectedAttendance = parsed;
            if (!_isRegroupingCountText)
            {
                ScheduleAutosave();
            }
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
            if (!_isRegroupingCountText)
            {
                ScheduleAutosave();
            }
        }
        else
        {
            PreSoldsError = error;
        }
    }

    partial void OnOperationalNotesTextChanged(string? value) => ScheduleAutosave();

    partial void OnSelectedDailyFeatureTypeOptionChanged(DailyFeatureTypeOption value) => ScheduleAutosave();

    partial void OnDailyFeatureTextChanged(string? value) => ScheduleAutosave();

    partial void OnIncludeDailyAdmissionChanged(bool value) => ScheduleAutosave();

    partial void OnIncludeFastLaneChanged(bool value) => ScheduleAutosave();

    partial void OnIncludePromotionsChanged(bool value) => ScheduleAutosave();

    partial void OnIncludeSeasonPassesChanged(bool value) => ScheduleAutosave();

    partial void OnIncludeAddOnsChanged(bool value) => ScheduleAutosave();

    partial void OnIncludeHauntedAttractionsChanged(bool value) => ScheduleAutosave();

    partial void OnIncludeOperationalNotesChanged(bool value) => ScheduleAutosave();

    partial void OnIncludeShowsChanged(bool value) => ScheduleAutosave();

    /// <summary>Pushes a freshly loaded/refreshed workspace's draft into the editable fields without treating that as a user edit (see <see cref="_isApplyingWorkspace"/>), then publishes the workspace itself.</summary>
    private void ApplyWorkspaceToFields(AssociateWorkspace workspace)
    {
        _isApplyingWorkspace = true;
        try
        {
            var draft = workspace.Draft;

            ManualHoursOverrideEnabled = draft.ManualHoursOverride.Enabled;
            _manualOpeningTime = draft.ManualHoursOverride.OpeningTime;
            _manualOpeningTimeParseError = null;
            ManualOpeningTimeText = FormatTimeOrNull(draft.ManualHoursOverride.OpeningTime);
            _manualClosingTime = draft.ManualHoursOverride.ClosingTime;
            _manualClosingTimeParseError = null;
            ManualClosingTimeText = FormatTimeOrNull(draft.ManualHoursOverride.ClosingTime);

            PrivateBuyoutEnabled = draft.PrivateBuyout.Enabled;
            _privateBuyoutOpeningTime = draft.PrivateBuyout.OpeningTime;
            _privateBuyoutOpeningTimeParseError = null;
            PrivateBuyoutOpeningTimeText = FormatTimeOrNull(draft.PrivateBuyout.OpeningTime);
            _privateBuyoutClosingTime = draft.PrivateBuyout.ClosingTime;
            _privateBuyoutClosingTimeParseError = null;
            PrivateBuyoutClosingTimeText = FormatTimeOrNull(draft.PrivateBuyout.ClosingTime);
            PrivateBuyoutDetails = draft.PrivateBuyout.Details;

            _northGateOverride = draft.NorthGateOpeningOverride;
            _northGateOverrideParseError = null;
            NorthGateOverrideText = FormatTimeOrNull(draft.NorthGateOpeningOverride);
            _avenueOverride = draft.AvenueOpeningOverride;
            _avenueOverrideParseError = null;
            AvenueOverrideText = FormatTimeOrNull(draft.AvenueOpeningOverride);
            NotifyGateOverrideDependents();

            _projectedAttendance = draft.ProjectedAttendance;
            ProjectedAttendanceText = CountEditorTextFor(ProjectedAttendanceText, draft.ProjectedAttendance);
            ProjectedAttendanceError = null;

            _preSolds = draft.PreSolds;
            PreSoldsText = CountEditorTextFor(PreSoldsText, draft.PreSolds);
            PreSoldsError = null;

            // Phase 17B: Early Ride Time is no longer part of the draft — nothing to populate here.
            // The effective-ERT display properties read workspace.EarlyRideTime directly; the
            // compact editor panel (Phase 17D §3) populates itself on demand (OpenErtEditorAsync)
            // since it needs the underlying monthly/override records, not the resolved effective value.
            IsErtEditorOpen = false;
            MonthlyErtSaveError = null;
            OverrideSaveError = null;

            OperationalNotesText = string.Join(Environment.NewLine, draft.OperationalNotes);

            var sourceStates = workspace.SelectedOperationalNoteSourceStates ?? EmptySourceStates;
            SelectedOperationalNotes = draft.SelectedOperationalNotes
                .Select(selection => new AssociateSelectedOperationalNoteRowViewModel(
                    AssociateSelectedOperationalNotePresentation.Build(
                        selection, sourceStates.GetValueOrDefault(selection.SourceOperationalNoteId, OperationalNoteSourceComparisonState.Unchanged)),
                    RemoveOperationalNoteAsync,
                    ReplaceOperationalNoteAsync))
                .ToList();

            SelectedDailyFeatureTypeOption = DailyFeatureTypeOption.For(draft.DailyFeatureType);
            DailyFeatureText = draft.DailyFeatureText;

            IncludeDailyAdmission = draft.SectionSelection.IncludeDailyAdmission;
            IncludeFastLane = draft.SectionSelection.IncludeFastLane;
            IncludePromotions = draft.SectionSelection.IncludePromotions;
            IncludeSeasonPasses = draft.SectionSelection.IncludeSeasonPasses;
            IncludeAddOns = draft.SectionSelection.IncludeAddOns;
            IncludeHauntedAttractions = draft.SectionSelection.IncludeHauntedAttractions;
            IncludeOperationalNotes = draft.SectionSelection.IncludeOperationalNotes;
            IncludeShows = draft.Shows?.IncludeInPassdown ?? false;

            BuildProductSections(workspace);
            BuildShowRows(workspace);

            _draftDateInEdit = workspace.OperatingDate;
            _draftBaseHash = workspace.DraftContentHash;
            _isDirty = false;
            SaveFailureReason = null;
            SaveBlockedMessage = null;
            AutosaveState = AssociateAutosaveState.Saved;
        }
        finally
        {
            _isApplyingWorkspace = false;
        }

        Workspace = workspace;
    }

    /// <summary>
    /// Groups the composed workspace's official products by display section (spec Phase 4D Part B,
    /// revised Phase 15A to resolve through <see cref="ProductDisplaySectionResolver"/> rather than
    /// grouping by raw <see cref="SourceCategory"/> — this is what stops the same real-world product
    /// from showing under two differently-named headings on the Products screen, matching the PDF's
    /// own fix). Pairs each section with its status banner (Phase 4B Part E) when it currently has
    /// zero rows, and rebuilds the custom-product rows from the draft's own
    /// <see cref="AssociateDraftPayload.CustomProducts"/> (never from <see cref="AssociateProduct"/>
    /// projections — that is how <see cref="CustomProductEntry.Notes"/> stays editable, spec Part G,
    /// without forcing a meaningless Notes field onto official products). A category whose resolved
    /// section is <see langword="null"/> (Bring-A-Friend Duo's own dated category, the dated ordinary
    /// Bring-A-Friend category) never contributes a section or a status banner here — both are already
    /// represented by their Promotions catalog counterpart. A section with neither product rows nor a
    /// status entry is not built at all — no fabricated empty section (spec Part B/H).
    /// </summary>
    private void BuildProductSections(AssociateWorkspace workspace)
    {
        var statusBySection = workspace.CategoryStatuses
            .Select(status => (Status: status, Section: ProductDisplaySectionResolver.Resolve(status.Category)))
            .Where(entry => entry.Section is not null)
            .ToDictionary(entry => entry.Section!.Value, entry => entry.Status);

        var officialBySection = ProductDisplaySectionResolver.ResolveAll(workspace.Products.Where(product => !product.IsCustom))
            .GroupBy(entry => entry.Section, entry => entry.Product)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<AssociateProduct>)group.ToList());

        var sections = officialBySection.Keys.Concat(statusBySection.Keys).Distinct().OrderBy(ProductDisplaySectionResolver.DisplayOrder);

        var result = new List<ProductCategorySectionViewModel>();
        foreach (var section in sections)
        {
            var products = officialBySection.GetValueOrDefault(section, []);
            var status = statusBySection.TryGetValue(section, out var sectionStatus)
                ? CategoryStatusPresentation.Build(sectionStatus, products.Count)
                : null;

            if (products.Count == 0 && status is null)
            {
                continue;
            }

            var rows = products.Select(product => new ProductRowViewModel(product, ScheduleAutosave, RequestPriceHistory)).ToList();
            result.Add(new ProductCategorySectionViewModel(
                ProductDisplaySectionDisplay.FriendlyName(section), status, rows, PassSourcePresentation.SourceNote(section, products),
                OfficialSourceLinkViewModel.TryCreate(PassSourcePresentation.SourceUrl(section, products), _externalLinkLauncher)));
        }

        ProductSections = result;
        HistoricalPricingNotice = BuildHistoricalPricingNotice(workspace);

        CustomProducts = new ObservableCollection<CustomProductRowViewModel>(
            workspace.Draft.CustomProducts.Select(custom => new CustomProductRowViewModel(custom, ScheduleAutosave, RemoveCustomProduct)));
    }

    /// <summary>Phase 21C: rows from the workspace's reconciled Shows section — this date's only.</summary>
    private void BuildShowRows(AssociateWorkspace workspace)
    {
        var section = workspace.Shows;
        ShowRows = section.Rows.Select(row => new AssociateShowRowViewModel(row, ScheduleAutosave)).ToList();

        var presentation = ShowSchedulePresentation.Build(section.Schedule, _clock.UtcNow);
        ShowsStatusText = ShowRows.Count == 0 ? presentation.StatusText : null;
        ShowsFreshnessText = presentation.FreshnessText;
    }

    internal static string? BuildHistoricalPricingNotice(AssociateWorkspace workspace) =>
        workspace.PricingTimeframe != AssociatePricingTimeframe.Historical
            ? null
            : workspace.IsHistoricalPricingUnavailable
                ? HistoricalPricingPolicy.UnavailableMessage
                : $"Showing prices recorded for {workspace.OperatingDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}. Current catalog products are not shown for past dates.";

    /// <summary>Converts the current editable fields into a full <see cref="AssociateDraftPayload"/>, including product overrides/custom products read from the current <see cref="ProductSections"/>/<see cref="CustomProducts"/> row view models (spec Phase 4D Parts D/E/F).</summary>
    private AssociateDraftPayload BuildDraft()
    {
        var existing = Workspace?.Draft ?? AssociateDraftPayload.CreateDefault();

        var productOverrides = ProductSections
            .SelectMany(section => section.Products)
            .ToDictionary(row => row.ProductIdentity, row => new ProductDraftOverride(row.IncludeOnPdf, row.DisplayPriceOverride));

        return existing with
        {
            ManualHoursOverride = new ManualHoursOverride(ManualHoursOverrideEnabled, _manualOpeningTime, _manualClosingTime),
            PrivateBuyout = new PrivateBuyoutOverride(PrivateBuyoutEnabled, _privateBuyoutOpeningTime, _privateBuyoutClosingTime, NullIfBlank(PrivateBuyoutDetails)),
            NorthGateOpeningOverride = _northGateOverride,
            AvenueOpeningOverride = _avenueOverride,
            ProjectedAttendance = _projectedAttendance,
            PreSolds = _preSolds,
            OperationalNotes = ParseLines(OperationalNotesText),
            DailyFeatureType = SelectedDailyFeatureTypeOption.Value,
            DailyFeatureText = NullIfBlank(DailyFeatureText),
            ProductOverrides = productOverrides,
            CustomProducts = CustomProducts.Select(row => row.ToEntry()).ToList(),
            SectionSelection = new PassdownSectionSelection(
                IncludeDailyAdmission,
                IncludeFastLane,
                IncludePromotions,
                IncludeSeasonPasses,
                IncludeAddOns,
                IncludeOperationalNotes,
                IncludeHauntedAttractions),
            Shows = AssociateShowsSection.BuildSelection(
                IncludeShows,
                ShowRows.Where(row => row.IsSelected).Select(row => row.Row),
                existing.Shows,
                _clock.UtcNow),
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
        if (AutosaveState != AssociateAutosaveState.SaveFailed)
        {
            AutosaveState = AssociateAutosaveState.Unsaved; // Phase 23: a failure stays visible until a save succeeds.
        }

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
    /// <remarks>
    /// Phase 23A: returns whether this attempt persisted. A failure leaves the edits dirty and on screen
    /// and is reported once — the caller decides whether anything is retried; this method never retries.
    /// </remarks>
    private async Task<bool> SaveDraftNowAsync(DateOnly date)
    {
        await _saveSemaphore.WaitAsync().ConfigureAwait(true);
        _saveInFlight = true;
        var saveFailed = false;
        try
        {
            _isDirty = false;
            AutosaveState = AssociateAutosaveState.Saving;
            var draft = BuildDraft();

            // Phase 18B REL-004: conditional on the version this editor loaded/last saved — a draft that
            // Personal Sync changed underneath it becomes an explicit conflict, never an overwrite.
            var saveResult = await _service.SaveEditorDraftAsync(date, draft, _draftBaseHash).ConfigureAwait(true);
            var status = saveResult.Status;
            if (_draftDateInEdit == date)
            {
                _draftBaseHash = saveResult.ContentHash;
            }

            if (saveResult.ConflictDetected)
            {
                SyncConflictMessage = DraftSyncConflictMessage;
            }

            if (Workspace is { } current && current.OperatingDate == date)
            {
                Workspace = WithDraftApplied(current, draft) with { SaveStatus = status, DraftContentHash = saveResult.ContentHash };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to autosave the Associate draft for {OperatingDate}.", date);
            _isDirty = true; // the edit is still only local; a later edit, Retry Save, or flush tries again (once).
            saveFailed = true;
            SaveFailureReason = ex.Message;
        }
        finally
        {
            _saveInFlight = false;
            if (!saveFailed)
            {
                SaveFailureReason = null;
                SaveBlockedMessage = null;
            }

            AutosaveState = saveFailed ? AssociateAutosaveState.SaveFailed
                : _isDirty ? AssociateAutosaveState.Unsaved
                : AssociateAutosaveState.Saved;
            _saveSemaphore.Release();
        }

        // Phase 19A: newer official data arrived while this editor had unsaved edits; now that they are
        // saved (and nothing new was typed meanwhile), show it.
        if (_officialDataReloadDeferred && !_isDirty && _draftDateInEdit == date && date == SelectedDate && !IsCheckingOnline)
        {
            _pendingOfficialDataReload = HandleOfficialDataChangedAsync(new DataChange(DataChangeKind.OfficialData, date));
        }

        return !saveFailed;
    }

    /// <summary>
    /// <paramref name="current"/> with <paramref name="draft"/> applied the way the PDF reads it: the
    /// draft itself, park hours recomputed from its manual/buyout/gate overrides, and its per-product
    /// include/price overrides reflected onto <see cref="AssociateWorkspace.Products"/>. Pure — used by
    /// <see cref="SaveDraftNowAsync"/> after a save and (Phase 18F) by <see cref="CopyForTeamsAsync"/>
    /// to see unsaved edits without saving them.
    /// </summary>
    private static AssociateWorkspace WithDraftApplied(AssociateWorkspace current, AssociateDraftPayload draft)
    {
        // The draft's manual/buyout override must be reflected in the workspace's
        // own ParkHours immediately, not only in the live ParkHours getter above — CreatePdfAsync
        // (and anything else reading Workspace directly) builds from this field, and until the
        // next full load/Check Online it would otherwise still show the pre-edit hours.
        var parkHours = RecomputeParkHours(
            current.ParkHours,
            draft.ManualHoursOverride.Enabled, draft.ManualHoursOverride.OpeningTime, draft.ManualHoursOverride.ClosingTime,
            draft.PrivateBuyout.Enabled, draft.PrivateBuyout.OpeningTime, draft.PrivateBuyout.ClosingTime,
            draft.NorthGateOpeningOverride, draft.AvenueOpeningOverride);

        // Phase 13D Part AC/AD: the draft's per-product include/price overrides must
        // also be reflected onto Workspace.Products immediately — this is what
        // AssociatePassdownDocumentModelBuilder actually reads (via AssociateProduct.EffectiveDisplayPrice/
        // IncludeOnPdf), not Draft.ProductOverrides directly. Before this fix, CreatePdfAsync's
        // FlushPendingAutosaveAsync -> SaveDraftNowAsync path updated Draft (so the override
        // survived a reload) but left Products holding whatever official/default values the last
        // full compose (initial load / date switch / Check Online) produced, so a custom value
        // typed and saved just before "Create PDF" never reached the PDF unless something else
        // happened to force a full recompose in between. Applies to every official product
        // (dated or catalog) uniformly — never special-cased by category/name.
        var updatedProducts = current.Products
            .Select(product =>
            {
                if (product.IsCustom)
                {
                    return product;
                }

                var productOverride = draft.ProductOverrides.GetValueOrDefault(product.ProductIdentity, ProductDraftOverride.Default);
                return product with
                {
                    IncludeOnPdf = productOverride.IncludeOnPdf,
                    DisplayPriceOverride = productOverride.DisplayPriceOverride,
                };
            })
            .ToList();

        return current with { Draft = draft, ParkHours = parkHours, Products = updatedProducts };
    }

    /// <summary>
    /// Synchronously drains any pending debounce/save for the date currently being edited (spec
    /// Part H/I) before the caller replaces it — a date switch or a Check Online recompose. Cancels
    /// the debounce timer itself (no need to wait 800ms for something about to be flushed anyway)
    /// and saves directly instead.
    ///
    /// Phase 15D: also called from App.axaml.cs's shutdown handler, so an edit made shortly before
    /// the window closes is never silently lost waiting on the 800ms debounce — internal (not
    /// private) purely for that cross-class call within this assembly.
    ///
    /// Phase 23A: returns true only when nothing on screen is left unsaved. The pre-23A version looped
    /// <c>while (_isDirty || _saveInFlight)</c>; a failed save leaves the edit dirty, and SQLite fails
    /// synchronously, so one persistent failure turned any flush (date change, Check Online, a note
    /// change, PDF, close) into an endless synchronous save loop on the UI thread — the RC.1–RC.7
    /// freeze. Now a failed attempt ends the flush at once (one attempt per flush, never a loop), and
    /// the pass count is capped even when every pass succeeds but the associate keeps typing.
    /// </summary>
    internal async Task<bool> FlushPendingAutosaveAsync()
    {
        _debounceCts?.Cancel();

        if (_draftDateInEdit is not { } date)
        {
            return true; // nothing loaded to attach edits to (a failed load clears the editor).
        }

        for (var pass = 0; _isDirty || _saveInFlight; pass++)
        {
            if (pass >= MaxFlushSavePasses || !await SaveDraftNowAsync(date).ConfigureAwait(true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Phase 23A: the explicit "Retry Save" action shown beside "Save failed — changes not saved". One
    /// attempt per press (the button is disabled while that attempt runs), saving whatever is on screen
    /// now; the failure, and anything it blocked, clears only once the write is confirmed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRetrySave))]
    private async Task RetrySaveAsync()
    {
        _debounceCts?.Cancel();
        if (_draftDateInEdit is { } date)
        {
            await SaveDraftNowAsync(date).ConfigureAwait(true);
        }
    }

    private bool CanRetrySave() => AutosaveState == AssociateAutosaveState.SaveFailed;

    /// <summary>Phase 23A: shown once when closing could not save the edits on screen (see <c>App.AttachShutdownFlush</c>).</summary>
    internal void ShowCloseBlockedBySaveFailure() => SaveBlockedMessage = CloseBlockedMessage;

    /// <summary>
    /// Phase 22D: when a count field loses focus, shows its valid value with thousands separators
    /// ("38500" → "38,500"). Never runs while typing (no caret jumps), leaves invalid text untouched
    /// so its error stays visible, and is display-only: the parsed value is unchanged, so no autosave
    /// is scheduled and nothing stored differs.
    /// </summary>
    public void RegroupCountFields()
    {
        _isRegroupingCountText = true;
        try
        {
            if (ProjectedAttendanceError is null && FormatCountForEditor(_projectedAttendance) is var attendance && ProjectedAttendanceText != attendance)
            {
                ProjectedAttendanceText = attendance;
            }

            if (PreSoldsError is null && FormatCountForEditor(_preSolds) is var preSolds && PreSoldsText != preSolds)
            {
                PreSoldsText = preSolds;
            }
        }
        finally
        {
            _isRegroupingCountText = false;
        }
    }

    /// <summary>
    /// The text a count field shows after a (re)load: the grouped value — except when the field already
    /// holds that same number (e.g. an official-data reload right after the associate's own autosave),
    /// where the text is left exactly as typed so a focused editor's caret never jumps.
    /// </summary>
    private static string? CountEditorTextFor(string? currentText, int? value) =>
        currentText is not null && TryParseNonNegativeInt(currentText, out var current, out _) && current == value
            ? currentText
            : FormatCountForEditor(value);

    /// <summary>A count field's display text: grouped, or empty when nothing was entered (never "0").</summary>
    private static string? FormatCountForEditor(int? count) => count is { } value ? OperationalCount.Format(value) : null;

    private static bool TryParseNonNegativeInt(string? text, out int? value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = null;
            return true;
        }

        if (!OperationalCount.TryParseEntry(text, out var parsed))
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

    private static string? FormatTimeOrNull(TimeOnly? value) => value is { } time ? OperationalTimeFormatter.Format(time) : null;

    /// <summary>Default <see cref="IErtConfigurationService"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Application.Ert.ErtConfigurationService"/>. Reads return "nothing configured" rather than throwing (so the effective-ERT display still renders NotConfigured correctly); only a Save/Remove attempt throws.</summary>
    private sealed class UnavailableErtConfigurationService : IErtConfigurationService
    {
        public Task<MonthlyErtConfiguration?> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default) =>
            Task.FromResult<MonthlyErtConfiguration?>(null);

        public Task<MonthlyErtConfiguration> SaveMonthlyAsync(MonthlyErtDraft draft, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Early Ride Time configuration is not available.");

        public Task<ErtDateOverride?> GetOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErtDateOverride?>(null);

        public Task<ErtDateOverride> SaveOverrideAsync(ErtDateOverrideDraft draft, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Early Ride Time configuration is not available.");

        public Task RemoveOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Early Ride Time configuration is not available.");

        public Task<ErtEditorSaveResult> SaveMonthlyFromEditorAsync(MonthlyErtDraft draft, string? expectedContentHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Early Ride Time configuration is not available.");

        public Task<ErtEditorSaveResult> SaveOverrideFromEditorAsync(ErtDateOverrideDraft draft, string? expectedContentHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Early Ride Time configuration is not available.");

        public Task<ErtEditorSaveResult> RemoveOverrideFromEditorAsync(DateOnly operatingDate, string? expectedContentHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Early Ride Time configuration is not available.");
    }

    /// <summary>Default <see cref="IPdfSaveFilePicker"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.AvaloniaPdfSaveFilePicker"/>.</summary>
    private sealed class UnavailablePdfSaveFilePicker : IPdfSaveFilePicker
    {
        public Task<PdfSaveResult> SaveAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(PdfSaveResult.Failed("PDF export is not available."));
    }

    /// <summary>Default <see cref="IClipboardService"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.AvaloniaClipboardService"/>.</summary>
    private sealed class UnavailableClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Clipboard is not available.");

        public Task SetTextAndHtmlAsync(string text, string htmlFragment, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Clipboard is not available.");

        public Task SetImageFileAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Clipboard is not available.");
    }
}
