using Carowinds.Passdown.Application.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 32: Settings → Calendar → "Show Pass Perks on Calendar" (default on). One shared instance: the
/// Settings checkbox writes it and the Calendar listens, so markers appear or disappear at once. Saved on
/// this computer only (never Personal Sync). Turning it off hides markers only — no perk data is deleted
/// and the Products Pass Perks list is unaffected.
/// </summary>
public partial class CalendarDisplaySettingsViewModel : ViewModelBase
{
    public const string SaveFailedMessage = "This change couldn't be saved on this computer, so it may not be kept after the app restarts.";

    private readonly ICalendarDisplayPreferenceStore? _store;
    private bool _isLoading;

    public CalendarDisplaySettingsViewModel(ICalendarDisplayPreferenceStore? store = null)
    {
        _store = store;
        _isLoading = true;
        ShowPassPerks = CalendarDisplayPreferences.BuiltIn.ShowPassPerks;
        _isLoading = false;
        PendingLoad = LoadAsync();
    }

    /// <summary>Raised whenever <see cref="ShowPassPerks"/> changes (loaded from the file or toggled).</summary>
    public event EventHandler<bool>? ShowPassPerksChanged;

    public Task PendingLoad { get; }

    public Task<bool> PendingSave { get; private set; } = Task.FromResult(true);

    [ObservableProperty]
    public partial bool ShowPassPerks { get; set; }

    [ObservableProperty]
    public partial string? SaveMessage { get; set; }

    partial void OnShowPassPerksChanged(bool value)
    {
        ShowPassPerksChanged?.Invoke(this, value);
        if (!_isLoading)
        {
            PendingSave = SaveAsync(value);
        }
    }

    private async Task LoadAsync()
    {
        if (_store is null)
        {
            return;
        }

        CalendarDisplayPreferences preferences;
        try
        {
            preferences = await _store.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            preferences = CalendarDisplayPreferences.BuiltIn; // the store never throws by contract; stay usable anyway.
        }

        _isLoading = true;
        try
        {
            ShowPassPerks = preferences.ShowPassPerks;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task<bool> SaveAsync(bool showPassPerks)
    {
        var saved = true;
        if (_store is not null)
        {
            try
            {
                await _store.SaveAsync(new CalendarDisplayPreferences(showPassPerks)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                saved = false;
            }
        }

        SaveMessage = saved ? null : SaveFailedMessage;
        return saved;
    }
}
