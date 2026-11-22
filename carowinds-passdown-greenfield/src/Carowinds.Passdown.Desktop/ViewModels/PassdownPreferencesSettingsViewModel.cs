using Carowinds.Passdown.Application.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 31D: Settings → Passdown Preferences. Seven independent checkboxes choosing which Include in
/// Passdown sections start checked for a date with no saved draft. Each change is saved at once on this
/// computer only (no Apply button, no Personal Sync). Saved drafts are never touched — a date that already
/// has a draft keeps its own saved selections.
/// </summary>
public partial class PassdownPreferencesSettingsViewModel : ViewModelBase
{
    public const string Description = "Choose which sections are selected automatically when starting a new Passdown.";
    public const string SaveFailedMessage = "This change couldn't be saved on this computer, so new Passdowns still use the previous defaults.";

    private readonly IPassdownDefaultsPreferenceStore? _store;
    private bool _isLoading;

    public PassdownPreferencesSettingsViewModel(IPassdownDefaultsPreferenceStore? store = null)
    {
        _store = store;
        Apply(PassdownDefaultSelections.BuiltIn);
        PendingLoad = LoadAsync();
    }

    /// <summary>Raised after a changed default has been saved, so an open never-saved date can show the new defaults.</summary>
    public event EventHandler<PassdownDefaultSelections>? DefaultsSaved;

    /// <summary>The initial load from the settings file; tests await it.</summary>
    public Task PendingLoad { get; }

    /// <summary>The last save started by a change; tests await it.</summary>
    public Task<bool> PendingSave { get; private set; } = Task.FromResult(true);

    [ObservableProperty]
    public partial bool DailyAdmission { get; set; }

    [ObservableProperty]
    public partial bool FastLane { get; set; }

    [ObservableProperty]
    public partial bool Promotions { get; set; }

    [ObservableProperty]
    public partial bool SeasonPasses { get; set; }

    [ObservableProperty]
    public partial bool AddOns { get; set; }

    [ObservableProperty]
    public partial bool OperationalNotes { get; set; }

    [ObservableProperty]
    public partial bool Shows { get; set; }

    /// <summary>Set when the last change could not be written; cleared by the next successful save.</summary>
    [ObservableProperty]
    public partial string? SaveMessage { get; set; }

    public PassdownDefaultSelections Current => new(DailyAdmission, FastLane, Promotions, SeasonPasses, AddOns, OperationalNotes, Shows);

    partial void OnDailyAdmissionChanged(bool value) => Save();

    partial void OnFastLaneChanged(bool value) => Save();

    partial void OnPromotionsChanged(bool value) => Save();

    partial void OnSeasonPassesChanged(bool value) => Save();

    partial void OnAddOnsChanged(bool value) => Save();

    partial void OnOperationalNotesChanged(bool value) => Save();

    partial void OnShowsChanged(bool value) => Save();

    private async Task LoadAsync()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            Apply(await _store.LoadAsync().ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Apply(PassdownDefaultSelections.BuiltIn); // the store never throws by contract; stay usable anyway.
        }
    }

    private void Apply(PassdownDefaultSelections defaults)
    {
        _isLoading = true;
        try
        {
            DailyAdmission = defaults.DailyAdmission;
            FastLane = defaults.FastLane;
            Promotions = defaults.Promotions;
            SeasonPasses = defaults.SeasonPasses;
            AddOns = defaults.AddOns;
            OperationalNotes = defaults.OperationalNotes;
            Shows = defaults.Shows;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Save()
    {
        if (_isLoading)
        {
            return;
        }

        PendingSave = SaveAsync(Current);
    }

    private async Task<bool> SaveAsync(PassdownDefaultSelections defaults)
    {
        var saved = true;
        if (_store is not null)
        {
            try
            {
                await _store.SaveAsync(defaults).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                saved = false;
            }
        }

        SaveMessage = saved ? null : SaveFailedMessage;
        if (saved)
        {
            DefaultsSaved?.Invoke(this, defaults);
        }

        return saved;
    }
}
