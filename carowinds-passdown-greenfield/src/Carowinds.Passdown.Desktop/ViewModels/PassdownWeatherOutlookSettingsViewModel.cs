using System.ComponentModel;
using Carowinds.Passdown.Application.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 37D: the one authoritative "Include Weather Outlook" value for the interactive app — the Phase 37F device
/// preference (<c>passdown-weather-outlook.json</c>, default on), now owned by a single shared instance. Settings →
/// Passdown Defaults ("Include Weather Outlook by default") binds to it directly, and the Passdown's "Weather Outlook"
/// checkbox (<see cref="AssociateViewModel.IncludeWeatherOutlook"/>) forwards changes to it and follows it through
/// <see cref="INotifyPropertyChanged.PropertyChanged"/>, so the two surfaces are always one value with one load and one
/// save per change — no second preference, file, watcher or poll. Never draft, Personal Sync, SQLite or snapshot data.
/// </summary>
public partial class PassdownWeatherOutlookSettingsViewModel : ViewModelBase
{
    public const string Description = "Include the Weather Outlook in Passdown PDFs when a valid forecast is available.";

    /// <summary>The existing Phase 37F failure semantics, stated honestly: the choice applies now but is not stored.</summary>
    public const string SaveFailedMessage = "This change couldn't be saved on this computer. It applies until the app closes; after a restart the previously saved choice is used.";

    private readonly IPassdownWeatherOutlookPreferenceStore _store;
    private readonly ILogger<PassdownWeatherOutlookSettingsViewModel>? _logger;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _isApplyingLoad;
    private bool _changedBeforeLoad;

    public PassdownWeatherOutlookSettingsViewModel(IPassdownWeatherOutlookPreferenceStore? store = null, ILogger<PassdownWeatherOutlookSettingsViewModel>? logger = null)
    {
        _store = store ?? new InMemoryPassdownWeatherOutlookPreferenceStore();
        _logger = logger;
        PendingLoad = LoadAsync();
    }

    /// <summary>The initial load from the preference file; consumers that must not use the default await it.</summary>
    public Task PendingLoad { get; }

    /// <summary>True once the saved choice (or the built-in default when none is saved) is known.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The last save started by a change; true when it was written.</summary>
    public Task<bool> PendingSave { get; private set; } = Task.FromResult(true);

    /// <summary>Whether Passdown PDFs (preview, Print, Save PDF, Create PDF) include the Weather Outlook.</summary>
    [ObservableProperty]
    public partial bool IncludeWeatherOutlook { get; set; } = PassdownWeatherOutlookPreferences.BuiltIn.IncludeWeatherOutlook;

    /// <summary>Set when the last change could not be written; cleared by the next successful save.</summary>
    [ObservableProperty]
    public partial string? SaveMessage { get; private set; }

    partial void OnIncludeWeatherOutlookChanged(bool value)
    {
        if (_isApplyingLoad)
        {
            return;
        }

        if (!IsLoaded)
        {
            _changedBeforeLoad = true; // a choice made in the first moments wins over the file read still in flight
        }

        PendingSave = SaveAsync();
    }

    private async Task LoadAsync()
    {
        PassdownWeatherOutlookPreferences preference;
        try
        {
            preference = await _store.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The store never throws by contract; stay usable with the default anyway.
            _logger?.LogWarning(ex, "Loading the Include Weather Outlook preference failed; using the default.");
            preference = PassdownWeatherOutlookPreferences.BuiltIn;
        }

        if (!_changedBeforeLoad)
        {
            _isApplyingLoad = true;
            try
            {
                IncludeWeatherOutlook = preference.IncludeWeatherOutlook;
            }
            finally
            {
                _isApplyingLoad = false;
            }
        }

        IsLoaded = true;
    }

    private async Task<bool> SaveAsync()
    {
        // One write at a time, and each writes the value current when it runs, so rapid toggles end with the latest
        // choice on disk and never interleave two atomic writes of the same file.
        await _saveGate.WaitAsync().ConfigureAwait(true);
        bool saved;
        try
        {
            await _store.SaveAsync(new PassdownWeatherOutlookPreferences(IncludeWeatherOutlook)).ConfigureAwait(true);
            saved = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A device preference is never worth an error dialog; the choice still applies for this session.
            _logger?.LogWarning(ex, "Saving the Include Weather Outlook preference failed.");
            saved = false;
        }
        finally
        {
            _saveGate.Release();
        }

        SaveMessage = saved ? null : SaveFailedMessage;
        return saved;
    }
}
