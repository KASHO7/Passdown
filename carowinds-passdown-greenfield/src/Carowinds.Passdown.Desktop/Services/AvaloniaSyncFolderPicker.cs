using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary><see cref="ISyncFolderPicker"/> backed by Avalonia's native <see cref="IStorageProvider"/> — mirrors <see cref="AvaloniaDiagnosticExportFilePicker"/>.</summary>
public sealed class AvaloniaSyncFolderPicker : ISyncFolderPicker
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return null;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose Personal Sync Folder",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
