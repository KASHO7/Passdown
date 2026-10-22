using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>Phase 29: chooses a local image file (the DAP / placement chart) — never a web address, never downloads anything.</summary>
public interface IImageFilePicker
{
    /// <summary>The chosen local file path, or null when cancelled or unavailable.</summary>
    Task<string?> PickImageAsync(CancellationToken cancellationToken = default);
}

/// <summary><see cref="IImageFilePicker"/> backed by Avalonia's native <see cref="IStorageProvider"/> — mirrors <see cref="AvaloniaSyncFolderPicker"/>.</summary>
public sealed class AvaloniaImageFilePicker : IImageFilePicker
{
    public async Task<string?> PickImageAsync(CancellationToken cancellationToken = default)
    {
        if (GetTopLevel()?.StorageProvider is not { } storageProvider)
        {
            return null;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose DAP / Placement Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
