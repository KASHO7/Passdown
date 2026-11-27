using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 36C: the compact Early Ride Time editor panel of the Associate workspace (Phase 17D §3 — one panel with the
/// Monthly Default and Date Override sections, opened by Edit Attractions), extracted unchanged from
/// <see cref="AssociateViewModel"/> (docs/phase36c-ert-editor-maintainability.md).
/// </summary>
/// <remarks>
/// <para>
/// The editor owns only its own panel state: the open flag, the editable monthly/override fields, their save errors, the
/// content hashes its conditional saves are based on (Phase 18B REL-004) and the values captured when the panel was last
/// filled (its "clean" baseline). It persists through <see cref="IErtConfigurationService"/> only — Early Ride Time is not
/// part of the Passdown draft (Phase 17B), so nothing here reads, writes or autosaves the draft.
/// </para>
/// <para>
/// It owns no date. Every action reads the workspace's one selected operating date through the parent-supplied
/// <c>currentDate</c> callback at the moment it starts, and re-checks it after each await: a load or save that a date switch
/// superseded never fills the panel or refreshes the workspace for the new date. A save always writes the date (or month)
/// whose values the panel was showing when Save was pressed. After a successful save the parent's <c>refreshWorkspaceAsync</c>
/// reloads the selected date's workspace (closing the panel, as every workspace replacement does), and the editor then
/// reopens itself, as before.
/// </para>
/// </remarks>
public partial class EarlyRideTimeEditorViewModel : ObservableObject
{
    internal const string ErtSyncConflictMessage =
        "Early Ride Time was changed on another device while this editor was open. Your values are saved on this computer and the other version is kept too — choose which to keep under Personal Sync conflicts.";

    private readonly IErtConfigurationService _ertConfigurationService;
    private readonly Func<DateOnly> _currentDate;
    private readonly Func<DateOnly, Task> _refreshWorkspaceAsync;
    private readonly Action<string> _reportSyncConflict;
    private readonly ILogger? _logger;

    // Phase 18B REL-004: the content hash of each persisted record this editor last loaded or saved — the expected base
    // of its next save, so a version Personal Sync changed underneath the editor is never silently overwritten.
    private string? _monthlyErtBaseHash;
    private string? _overrideBaseHash;
    private ErtEditorFields? _ertEditorLoadedFields;

    /// <summary>The panel's editable values, captured when it was (re)loaded — a panel whose current values still equal these is "clean" and may be reloaded after a sync change.</summary>
    private sealed record ErtEditorFields(bool MonthlyNoErt, string? MonthlyRides, string? MonthlyNote, bool UseOverride, bool OverrideNoErt, string? OverrideRides, string? OverrideReason);

    /// <param name="ertConfigurationService">The one ERT application service (monthly defaults and date overrides).</param>
    /// <param name="currentDate">The workspace's selected operating date, read at the moment each action starts and after each await.</param>
    /// <param name="refreshWorkspaceAsync">Reloads the workspace for a date after a save (the parent ignores a superseded date).</param>
    /// <param name="reportSyncConflict">Shows a Personal Sync conflict message in the workspace.</param>
    /// <param name="logger">Optional; logs save failures with the date only, never the ride text.</param>
    public EarlyRideTimeEditorViewModel(
        IErtConfigurationService ertConfigurationService,
        Func<DateOnly> currentDate,
        Func<DateOnly, Task> refreshWorkspaceAsync,
        Action<string> reportSyncConflict,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ertConfigurationService);
        ArgumentNullException.ThrowIfNull(currentDate);
        ArgumentNullException.ThrowIfNull(refreshWorkspaceAsync);
        ArgumentNullException.ThrowIfNull(reportSyncConflict);

        _ertConfigurationService = ertConfigurationService;
        _currentDate = currentDate;
        _refreshWorkspaceAsync = refreshWorkspaceAsync;
        _reportSyncConflict = reportSyncConflict;
        _logger = logger;
    }

    /// <summary>
    /// Whether the single compact Early Ride Time editor panel is open (Phase 17D §3 — replaces the Phase 17B pair of
    /// independent "Configure Monthly ERT"/"Override for this date" panels with one panel that shows the Monthly Default and
    /// Date Override sections together, opened by one compact [Edit] action). No routine ERT start time is collected
    /// anywhere in this editor (spec §1/§2/§3) — associates already know Early Ride happens 30 minutes before park open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsErtEditorOpen { get; set; }

    [ObservableProperty]
    public partial string? MonthlyErtRidesText { get; set; }

    /// <summary>"No ERT this month" (spec Part 7/11) — when checked, the rides field is ignored by <see cref="SaveMonthlyErtAsync"/> regardless of what it still shows, matching <see cref="ErtConfigurationService"/>'s own server-side normalization.</summary>
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

    /// <summary>True while the panel is open with values that differ from what it last loaded or saved (unsaved edits the workspace must not replace).</summary>
    internal bool IsDirty =>
        IsErtEditorOpen && _ertEditorLoadedFields is { } loaded && loaded != CurrentErtEditorFields();

    /// <summary>
    /// Every workspace replacement closes the panel and clears its save errors (the effective-ERT display reads the
    /// workspace; the panel re-fills itself on demand). The editable values are left as they are.
    /// </summary>
    internal void CloseForWorkspaceReplacement()
    {
        IsErtEditorOpen = false;
        MonthlyErtSaveError = null;
        OverrideSaveError = null;
    }

    [RelayCommand]
    private async Task OpenErtEditorAsync()
    {
        var date = _currentDate();
        MonthlyErtSaveError = null;
        OverrideSaveError = null;

        if (await LoadErtEditorFieldsAsync(date).ConfigureAwait(true))
        {
            IsErtEditorOpen = true;
        }
    }

    /// <summary>Fills the Early Ride Time panel from the persisted records and records their hashes as the base of the panel's next saves (Phase 18B REL-004). Returns false when superseded by a date switch.</summary>
    internal async Task<bool> LoadErtEditorFieldsAsync(DateOnly date)
    {
        var existingMonthly = await _ertConfigurationService.GetMonthlyAsync(date.Year, date.Month).ConfigureAwait(true);
        var existingOverride = await _ertConfigurationService.GetOverrideAsync(date).ConfigureAwait(true);
        if (date != _currentDate())
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
    /// Validates and saves the Monthly Default section (spec Part 7/33) — only ever runs on this explicit "Save Monthly"
    /// action, never implicitly from typing. No start time is collected or sent (Phase 17D §1). On success, reloads the
    /// workspace so the effective Early Ride Time display and every other resolver-driven display refreshes together
    /// (spec Part 4's "one authoritative resolver").
    /// </summary>
    [RelayCommand]
    private async Task SaveMonthlyErtAsync()
    {
        var date = _currentDate();
        var (state, rides) = BuildErtDraftFields(MonthlyErtNoErt, MonthlyErtRidesText);

        MonthlyErtSaveError = null;
        try
        {
            var result = await _ertConfigurationService.SaveMonthlyFromEditorAsync(
                new MonthlyErtDraft(date.Year, date.Month, state, StartTime: null, rides, EditorText.NullIfBlank(MonthlyErtNoteText)), _monthlyErtBaseHash).ConfigureAwait(true);
            _monthlyErtBaseHash = result.ContentHash;
            if (result.ConflictDetected)
            {
                _reportSyncConflict(ErtSyncConflictMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to save monthly Early Ride Time for {Year}-{Month}.", date.Year, date.Month);
            MonthlyErtSaveError = "Could not save. Try again.";
            return;
        }

        if (date != _currentDate())
        {
            return; // superseded by a date switch while the save was in flight.
        }

        await _refreshWorkspaceAsync(date).ConfigureAwait(true);
        IsErtEditorOpen = true; // the workspace refresh closes it unconditionally; Phase 17D §3 keeps the shared panel open across an in-panel Save.
        _ertEditorLoadedFields = CurrentErtEditorFields();
    }

    /// <summary>Validates and saves the Date Override section (spec Part 8/9/33) — mirrors <see cref="SaveMonthlyErtAsync"/>, scoped to the selected date instead of its month. No start time is collected or sent (Phase 17D §1).</summary>
    [RelayCommand]
    private async Task SaveOverrideAsync()
    {
        var date = _currentDate();
        var (state, rides) = BuildErtDraftFields(OverrideNoErt, OverrideRidesText);

        OverrideSaveError = null;
        try
        {
            var result = await _ertConfigurationService.SaveOverrideFromEditorAsync(
                new ErtDateOverrideDraft(date, state, StartTime: null, rides, EditorText.NullIfBlank(OverrideReasonText)), _overrideBaseHash).ConfigureAwait(true);
            _overrideBaseHash = result.ContentHash;
            if (result.ConflictDetected)
            {
                _reportSyncConflict(ErtSyncConflictMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to save Early Ride Time override for {OperatingDate}.", date);
            OverrideSaveError = "Could not save. Try again.";
            return;
        }

        if (date != _currentDate())
        {
            return;
        }

        ErtUseOverrideForDate = true;
        await _refreshWorkspaceAsync(date).ConfigureAwait(true);
        IsErtEditorOpen = true;
        _ertEditorLoadedFields = CurrentErtEditorFields();
    }

    /// <summary>Removes the selected date's override (spec Part 8/28) — the date immediately falls back to the monthly default once the workspace reloads.</summary>
    [RelayCommand]
    private async Task RemoveOverrideAsync()
    {
        var date = _currentDate();
        OverrideSaveError = null;
        try
        {
            var result = await _ertConfigurationService.RemoveOverrideFromEditorAsync(date, _overrideBaseHash).ConfigureAwait(true);
            _overrideBaseHash = result.ContentHash;
            if (result.ConflictDetected)
            {
                _reportSyncConflict(ErtSyncConflictMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to remove Early Ride Time override for {OperatingDate}.", date);
            OverrideSaveError = "Could not remove. Try again.";
            return;
        }

        if (date != _currentDate())
        {
            return;
        }

        ErtUseOverrideForDate = false;
        OverrideNoErt = false;
        OverrideRidesText = null;
        OverrideReasonText = null;
        await _refreshWorkspaceAsync(date).ConfigureAwait(true);
        IsErtEditorOpen = true;
        _ertEditorLoadedFields = CurrentErtEditorFields();
    }

    /// <summary>
    /// Shared validate-and-shape step for both the monthly and override editors (spec Part 33, revised Phase 17D §1):
    /// <see cref="ErtConfigurationState.NoErt"/> always clears rides (mirrors <c>ErtConfigurationService</c>'s own
    /// server-side normalization); otherwise rides are parsed with the same newline-list rule as the Passdown's
    /// free-text fields (<see cref="EditorText.ParseLines"/>). No start time is collected or validated — Phase 17D
    /// removes the routine ERT time field entirely.
    /// </summary>
    private static (ErtConfigurationState State, IReadOnlyList<string> Rides) BuildErtDraftFields(bool noErt, string? ridesText) =>
        noErt ? (ErtConfigurationState.NoErt, []) : (ErtConfigurationState.Configured, EditorText.ParseLines(ridesText));

    private ErtEditorFields CurrentErtEditorFields() =>
        new(MonthlyErtNoErt, MonthlyErtRidesText, MonthlyErtNoteText, ErtUseOverrideForDate, OverrideNoErt, OverrideRidesText, OverrideReasonText);
}
