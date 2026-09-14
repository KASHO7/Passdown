using System.Collections.ObjectModel;
using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;
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
    private readonly IErtConfigurationService _ertConfigurationService;
    private readonly IAssociatePassdownPdfExportService _pdfExportService;
    private readonly IPdfSaveFilePicker _pdfSavePicker;
    private readonly IDiagnosticExportService _diagnosticExportService;
    private readonly IDiagnosticExportFilePicker _diagnosticExportPicker;
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
    [NotifyPropertyChangedFor(nameof(AvailableOperationalNoteSuggestionsHeading))]
    [NotifyPropertyChangedFor(nameof(SelectedMonthLabel))]
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
    public partial bool IsLoadingDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreatePdfCommand))]
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

    [ObservableProperty]
    public partial string? ProjectedAttendanceText { get; set; }

    [ObservableProperty]
    public partial string? ProjectedAttendanceError { get; set; }

    [ObservableProperty]
    public partial string? PreSoldsText { get; set; }

    [ObservableProperty]
    public partial string? PreSoldsError { get; set; }

    /// <summary>Whether the "Configure Monthly ERT" inline panel is open (Phase 17B spec Part 7) — independent of <see cref="IsOverrideEditorOpen"/>; only one is expected open at a time, but nothing enforces that beyond the UI only ever offering one "Configure"/"Override" action at once.</summary>
    [ObservableProperty]
    public partial bool IsMonthlyErtEditorOpen { get; set; }

    [ObservableProperty]
    public partial string? MonthlyErtStartTimeText { get; set; }

    [ObservableProperty]
    public partial string? MonthlyErtRidesText { get; set; }

    /// <summary>"No ERT this month" (spec Part 7/11) — when checked, the start time/rides fields are ignored by <see cref="SaveMonthlyErtAsync"/> regardless of what they still show, matching <see cref="Application.Ert.ErtConfigurationService"/>'s own server-side normalization.</summary>
    [ObservableProperty]
    public partial bool MonthlyErtNoErt { get; set; }

    [ObservableProperty]
    public partial string? MonthlyErtNoteText { get; set; }

    /// <summary>Set only by an explicit Save attempt (spec Part 9: reuse <see cref="OperationalTimeParser"/>'s error message) — never updated by simply typing, unlike Manual Hours' per-keystroke-commit fields, since Monthly ERT saves only on the explicit button press (spec Part 7).</summary>
    [ObservableProperty]
    public partial string? MonthlyErtSaveError { get; set; }

    /// <summary>Whether the "Override for this date" inline panel is open (Phase 17B spec Part 8).</summary>
    [ObservableProperty]
    public partial bool IsOverrideEditorOpen { get; set; }

    [ObservableProperty]
    public partial string? OverrideStartTimeText { get; set; }

    [ObservableProperty]
    public partial string? OverrideRidesText { get; set; }

    [ObservableProperty]
    public partial bool OverrideNoErt { get; set; }

    [ObservableProperty]
    public partial string? OverrideReasonText { get; set; }

    [ObservableProperty]
    public partial string? OverrideSaveError { get; set; }

    [ObservableProperty]
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

    public bool HasAvailableOperationalNoteSuggestions => AvailableOperationalNoteSuggestions.Count > 0;

    /// <summary>"Available for Aug 26" — the Associate-selected date, not Dashboard's Eastern "today" (Phase 11D Part E/U).</summary>
    public string AvailableOperationalNoteSuggestionsHeading =>
        $"Available for {SelectedDate.ToDateTime(TimeOnly.MinValue).ToString("MMM d", CultureInfo.InvariantCulture)}";

    [ObservableProperty]
    public partial DailyFeatureTypeOption SelectedDailyFeatureTypeOption { get; set; } = DailyFeatureTypeOption.All[0];

    [ObservableProperty]
    public partial string? DailyFeatureText { get; set; }

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

    /// <summary>True while <see cref="ExportDiagnosticsCommand"/> is building/saving the diagnostic ZIP (Phase 7F Part F) — mirrors <see cref="IsExportingPdf"/>; kept independent of <see cref="IsBusy"/> so a Check Online/date load doesn't block this local-only export.</summary>
    [ObservableProperty]
    public partial bool IsExportingDiagnostics { get; set; }

    /// <summary>Concise post-export feedback for the diagnostics action (mirrors <see cref="PdfExportMessage"/>). A cancelled picker leaves this null.</summary>
    [ObservableProperty]
    public partial string? DiagnosticsExportMessage { get; set; }

    public AssociateViewModel(
        IAssociatePassdownService service,
        IClock clock,
        ILogger<AssociateViewModel>? logger = null,
        TimeSpan? autosaveDebounce = null,
        Func<TimeSpan, CancellationToken, Task>? autosaveDelay = null,
        IAssociatePassdownPdfExportService? pdfExportService = null,
        IPdfSaveFilePicker? pdfSavePicker = null,
        IDiagnosticExportService? diagnosticExportService = null,
        IDiagnosticExportFilePicker? diagnosticExportPicker = null,
        IErtConfigurationService? ertConfigurationService = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _ertConfigurationService = ertConfigurationService ?? new UnavailableErtConfigurationService();
        _logger = logger;
        _autosaveDebounce = autosaveDebounce ?? DefaultAutosaveDebounce;
        _autosaveDelay = autosaveDelay ?? Task.Delay;
        _pdfExportService = pdfExportService ?? new AssociatePassdownPdfExportService(new AssociatePassdownPdfGenerator());
        _pdfSavePicker = pdfSavePicker ?? new UnavailablePdfSaveFilePicker();
        _diagnosticExportService = diagnosticExportService ?? new UnavailableDiagnosticExportService();
        _diagnosticExportPicker = diagnosticExportPicker ?? new UnavailableDiagnosticExportFilePicker();

        // "Today" in the park's own wall-clock time (spec §6.5/§6.6 use the same Eastern
        // convention for "today") rather than the host machine's arbitrary local time zone.
        SelectedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);
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
                PrivateBuyoutEnabled, _privateBuyoutOpeningTime, _privateBuyoutClosingTime);
        }
    }

    public ParkHoursPresentation? ParkHours => LiveParkHours is { } live ? ParkHoursPresentation.Build(live) : null;

    /// <summary>"September 2026" — the Associate-selected date's month/year (Phase 17B spec Part 6/7), used by both the effective-ERT "Not configured for ..." message and the Configure Monthly ERT panel's own heading.</summary>
    public string SelectedMonthLabel => SelectedDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private EffectiveErt EffectiveEarlyRideTime => Workspace?.EarlyRideTime ?? EffectiveErt.NotConfigured;

    public bool EarlyRideTimeIsConfigured => EffectiveEarlyRideTime.Kind == EffectiveErtKind.Configured;

    public bool EarlyRideTimeIsNotConfigured => EffectiveEarlyRideTime.Kind == EffectiveErtKind.NotConfigured;

    public bool EarlyRideTimeIsNoErt => EffectiveEarlyRideTime.Kind == EffectiveErtKind.NoErt;

    /// <summary>Whether the selected date's effective ERT came from a date override rather than the monthly default (Phase 17B spec Part 12) — drives the "Date override" caption and which action ("Override for this date" vs. "Remove override") the panel offers.</summary>
    public bool EarlyRideTimeIsOverride => EffectiveEarlyRideTime.Source == EffectiveErtSource.Override;

    /// <summary>
    /// The compact effective-ERT line (Phase 17B spec Part 12) — built entirely from
    /// <see cref="Workspace"/>'s already-resolved <see cref="AssociateWorkspace.EarlyRideTime"/>, the
    /// same value the Dashboard/PDF/finalized snapshot read; this view never derives ERT from park
    /// hours (spec Part 1/31) or fabricates a value for <see cref="EffectiveErtKind.NotConfigured"/>
    /// (spec Part 6).
    /// </summary>
    public string EarlyRideTimeStatusText => EffectiveEarlyRideTime.Kind switch
    {
        EffectiveErtKind.NotConfigured => $"Not configured for {SelectedMonthLabel}",
        EffectiveErtKind.NoErt => "None",
        _ => EffectiveEarlyRideTime.Rides.Count > 0
            ? $"{OperationalTimeFormatter.Format(EffectiveEarlyRideTime.StartTime!.Value)} — {string.Join(", ", EffectiveEarlyRideTime.Rides)}"
            : OperationalTimeFormatter.Format(EffectiveEarlyRideTime.StartTime!.Value),
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
        bool privateBuyoutEnabled, TimeOnly? buyoutOpening, TimeOnly? buyoutClosing)
    {
        var effective = ParkHoursCalculator.Calculate(
            official.OfficialIsClosed, official.OfficialOpening, official.OfficialClosing,
            manualOverrideEnabled, manualOpening, manualClosing,
            privateBuyoutEnabled, buyoutOpening, buyoutClosing);

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
            await RefreshOperationalNoteSuggestionsAsync(date).ConfigureAwait(true);
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
            await FlushPendingAutosaveAsync().ConfigureAwait(true);

            if (Workspace is not { } workspace)
            {
                return;
            }

            var result = _pdfExportService.GeneratePdf(workspace, DateTimeOffset.Now);

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

            var fileName = $"Carowinds-Operations-{workspace.OperatingDate:yyyy-MM-dd}.pdf";
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
    /// Phase 7F Part F/G: builds the sanitized diagnostic export ZIP (local-only — never triggers a
    /// network check) and hands it to the native save-file picker. Never flushes/reads draft edits;
    /// diagnostics are independent of whatever is currently on screen.
    /// </summary>
    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        IsExportingDiagnostics = true;
        DiagnosticsExportMessage = null;
        try
        {
            var export = await _diagnosticExportService.BuildZipAsync().ConfigureAwait(true);
            var saveResult = await _diagnosticExportPicker.SaveAsync(export.ZipBytes, export.SuggestedFileName).ConfigureAwait(true);

            DiagnosticsExportMessage = saveResult.Outcome switch
            {
                DiagnosticExportOutcome.Saved => "Diagnostics saved.",
                DiagnosticExportOutcome.Cancelled => null,
                _ => saveResult.ErrorMessage ?? "Diagnostics could not be saved. Try again.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Export Diagnostics failed unexpectedly.");
            DiagnosticsExportMessage = "Diagnostics could not be created. Try again.";
        }
        finally
        {
            IsExportingDiagnostics = false;
        }
    }

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
        await FlushPendingAutosaveAsync().ConfigureAwait(true);
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
        await FlushPendingAutosaveAsync().ConfigureAwait(true);
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
        await FlushPendingAutosaveAsync().ConfigureAwait(true);
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
    /// Opens the Configure Monthly ERT panel (Phase 17B spec Part 7), pre-filled from the
    /// <em>persisted monthly record</em> for the selected date's month — not from
    /// <see cref="Workspace"/>'s already-resolved <see cref="EffectiveEarlyRideTime"/>, since that
    /// value may currently be coming from a date override; editing "the month" must always start from
    /// the month's own saved default, never from whatever happens to be in effect today.
    /// </summary>
    [RelayCommand]
    private async Task OpenMonthlyErtEditorAsync()
    {
        var date = SelectedDate;
        MonthlyErtSaveError = null;

        var existing = await _ertConfigurationService.GetMonthlyAsync(date.Year, date.Month).ConfigureAwait(true);
        if (date != SelectedDate)
        {
            return; // superseded by a date switch while the read was in flight.
        }

        MonthlyErtNoErt = existing?.State == ErtConfigurationState.NoErt;
        MonthlyErtStartTimeText = existing?.StartTime is { } startTime ? OperationalTimeFormatter.Format(startTime) : null;
        MonthlyErtRidesText = existing is null ? null : string.Join(Environment.NewLine, existing.Rides);
        MonthlyErtNoteText = existing?.Note;
        IsMonthlyErtEditorOpen = true;
    }

    [RelayCommand]
    private void CancelMonthlyErtEditor()
    {
        IsMonthlyErtEditorOpen = false;
        MonthlyErtSaveError = null;
    }

    /// <summary>
    /// Validates and saves the Configure Monthly ERT panel (spec Part 7/9/33): reuses
    /// <see cref="OperationalTimeParser"/> exactly like Manual Hours, but — unlike Manual Hours'
    /// per-keystroke live commit — only ever runs on this explicit Save action (spec Part 7's "[ Save
    /// Monthly ERT ]" button), never implicitly from typing. On success, reloads the workspace so
    /// <see cref="EarlyRideTimeStatusText"/> and every other resolver-driven display refreshes
    /// together (spec Part 4's "one authoritative resolver").
    /// </summary>
    [RelayCommand]
    private async Task SaveMonthlyErtAsync()
    {
        var date = SelectedDate;

        if (!TryBuildErtDraftFields(MonthlyErtNoErt, MonthlyErtStartTimeText, MonthlyErtRidesText, out var state, out var startTime, out var rides, out var error))
        {
            MonthlyErtSaveError = error;
            return;
        }

        MonthlyErtSaveError = null;
        try
        {
            await _ertConfigurationService.SaveMonthlyAsync(
                new MonthlyErtDraft(date.Year, date.Month, state, startTime, rides, NullIfBlank(MonthlyErtNoteText))).ConfigureAwait(true);
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

        IsMonthlyErtEditorOpen = false;
        await RefreshWorkspaceAsync(date).ConfigureAwait(true);
    }

    /// <summary>Opens the Override for this date panel (spec Part 8), pre-filled from any existing override for the selected date.</summary>
    [RelayCommand]
    private async Task OpenOverrideEditorAsync()
    {
        var date = SelectedDate;
        OverrideSaveError = null;

        var existing = await _ertConfigurationService.GetOverrideAsync(date).ConfigureAwait(true);
        if (date != SelectedDate)
        {
            return;
        }

        OverrideNoErt = existing?.State == ErtConfigurationState.NoErt;
        OverrideStartTimeText = existing?.StartTime is { } startTime ? OperationalTimeFormatter.Format(startTime) : null;
        OverrideRidesText = existing is null ? null : string.Join(Environment.NewLine, existing.Rides);
        OverrideReasonText = existing?.Reason;
        IsOverrideEditorOpen = true;
    }

    [RelayCommand]
    private void CancelOverrideEditor()
    {
        IsOverrideEditorOpen = false;
        OverrideSaveError = null;
    }

    /// <summary>Validates and saves the date override panel (spec Part 8/9/33) — mirrors <see cref="SaveMonthlyErtAsync"/> exactly, scoped to <see cref="SelectedDate"/> instead of its month.</summary>
    [RelayCommand]
    private async Task SaveOverrideAsync()
    {
        var date = SelectedDate;

        if (!TryBuildErtDraftFields(OverrideNoErt, OverrideStartTimeText, OverrideRidesText, out var state, out var startTime, out var rides, out var error))
        {
            OverrideSaveError = error;
            return;
        }

        OverrideSaveError = null;
        try
        {
            await _ertConfigurationService.SaveOverrideAsync(
                new ErtDateOverrideDraft(date, state, startTime, rides, NullIfBlank(OverrideReasonText))).ConfigureAwait(true);
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

        IsOverrideEditorOpen = false;
        await RefreshWorkspaceAsync(date).ConfigureAwait(true);
    }

    /// <summary>Removes the selected date's override (spec Part 8/28) — the date immediately falls back to the monthly default once the workspace reloads.</summary>
    [RelayCommand]
    private async Task RemoveOverrideAsync()
    {
        var date = SelectedDate;
        OverrideSaveError = null;
        try
        {
            await _ertConfigurationService.RemoveOverrideAsync(date).ConfigureAwait(true);
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

        IsOverrideEditorOpen = false;
        await RefreshWorkspaceAsync(date).ConfigureAwait(true);
    }

    /// <summary>
    /// Shared validate-and-shape step for both the monthly and override editors (spec Part 33):
    /// <see cref="ErtConfigurationState.NoErt"/> always clears start time/rides (mirrors
    /// <c>ErtConfigurationService</c>'s own server-side normalization — validated here too so the
    /// associate sees the problem immediately rather than after a round trip); otherwise a valid,
    /// non-blank start time is required, reusing <see cref="OperationalTimeParser"/> so the accepted
    /// input grammar never diverges from Manual Hours/Private Buyout (spec Part 9). Rides are parsed
    /// with the same <see cref="ParseLines"/> newline-list idiom already used elsewhere in this class.
    /// </summary>
    private static bool TryBuildErtDraftFields(
        bool noErt, string? startTimeText, string? ridesText,
        out ErtConfigurationState state, out TimeOnly? startTime, out IReadOnlyList<string> rides, out string? error)
    {
        if (noErt)
        {
            state = ErtConfigurationState.NoErt;
            startTime = null;
            rides = [];
            error = null;
            return true;
        }

        state = ErtConfigurationState.Configured;
        rides = ParseLines(ridesText);

        if (!OperationalTimeParser.TryParse(startTimeText, out startTime, out var parseError) || startTime is null)
        {
            error = parseError ?? OperationalTimeParser.InvalidTimeMessage;
            return false;
        }

        error = null;
        return true;
    }

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

    partial void OnManualHoursOverrideEnabledChanged(bool value) => ScheduleAutosave();

    partial void OnPrivateBuyoutEnabledChanged(bool value) => ScheduleAutosave();

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

            _projectedAttendance = draft.ProjectedAttendance;
            ProjectedAttendanceText = draft.ProjectedAttendance?.ToString(CultureInfo.InvariantCulture);
            ProjectedAttendanceError = null;

            _preSolds = draft.PreSolds;
            PreSoldsText = draft.PreSolds?.ToString(CultureInfo.InvariantCulture);
            PreSoldsError = null;

            // Phase 17B: Early Ride Time is no longer part of the draft — nothing to populate here.
            // The effective-ERT display properties read workspace.EarlyRideTime directly; the
            // Configure/Override editor panels populate themselves on demand (OpenMonthlyErtEditorAsync/
            // OpenOverrideEditorAsync) since they need the underlying monthly/override record, not the
            // resolved effective value.
            IsMonthlyErtEditorOpen = false;
            IsOverrideEditorOpen = false;
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

        var sections = officialBySection.Keys.Concat(statusBySection.Keys).Distinct().OrderBy(section => (int)section);

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

            var rows = products.Select(product => new ProductRowViewModel(product, ScheduleAutosave)).ToList();
            result.Add(new ProductCategorySectionViewModel(ProductDisplaySectionDisplay.FriendlyName(section), status, rows));
        }

        ProductSections = result;

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
            ManualHoursOverride = new ManualHoursOverride(ManualHoursOverrideEnabled, _manualOpeningTime, _manualClosingTime),
            PrivateBuyout = new PrivateBuyoutOverride(PrivateBuyoutEnabled, _privateBuyoutOpeningTime, _privateBuyoutClosingTime, NullIfBlank(PrivateBuyoutDetails)),
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
                // The just-saved draft's manual/buyout override must be reflected in the workspace's
                // own ParkHours immediately, not only in the live ParkHours getter above — CreatePdfAsync
                // (and anything else reading Workspace directly) builds from this field, and until the
                // next full load/Check Online it would otherwise still show the pre-edit hours.
                var parkHours = RecomputeParkHours(
                    current.ParkHours,
                    draft.ManualHoursOverride.Enabled, draft.ManualHoursOverride.OpeningTime, draft.ManualHoursOverride.ClosingTime,
                    draft.PrivateBuyout.Enabled, draft.PrivateBuyout.OpeningTime, draft.PrivateBuyout.ClosingTime);

                // Phase 13D Part AC/AD: the just-saved draft's per-product include/price overrides must
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

                Workspace = current with { Draft = draft, SaveStatus = status, ParkHours = parkHours, Products = updatedProducts };
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
    ///
    /// Phase 15D: also called from App.axaml.cs's shutdown handler, so an edit made shortly before
    /// the window closes is never silently lost waiting on the 800ms debounce — internal (not
    /// private) purely for that cross-class call within this assembly.
    /// </summary>
    internal async Task FlushPendingAutosaveAsync()
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
    }

    /// <summary>Default <see cref="IPdfSaveFilePicker"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.AvaloniaPdfSaveFilePicker"/>.</summary>
    private sealed class UnavailablePdfSaveFilePicker : IPdfSaveFilePicker
    {
        public Task<PdfSaveResult> SaveAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(PdfSaveResult.Failed("PDF export is not available."));
    }

    /// <summary>Default <see cref="IDiagnosticExportService"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Infrastructure.Diagnostics.DiagnosticExportService"/>.</summary>
    private sealed class UnavailableDiagnosticExportService : IDiagnosticExportService
    {
        public Task<DiagnosticExportResult> BuildZipAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Diagnostic export is not available.");
    }

    /// <summary>Default <see cref="IDiagnosticExportFilePicker"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.AvaloniaDiagnosticExportFilePicker"/>.</summary>
    private sealed class UnavailableDiagnosticExportFilePicker : IDiagnosticExportFilePicker
    {
        public Task<DiagnosticExportSaveResult> SaveAsync(byte[] zipBytes, string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(DiagnosticExportSaveResult.Failed("Diagnostic export is not available."));
    }
}
