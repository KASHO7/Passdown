namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// The one seam between <see cref="Carowinds.Passdown.Desktop.ViewModels.PersonalSyncSettingsViewModel"/>
/// and Avalonia's native folder-picker API (mirrors <see cref="IPdfSaveFilePicker"/>'s
/// ViewModel/Avalonia split). Returns <see langword="null"/> when the user cancels — this project
/// never falls back to a guessed or hardcoded folder when the native picker is dismissed.
/// </summary>
public interface ISyncFolderPicker
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
