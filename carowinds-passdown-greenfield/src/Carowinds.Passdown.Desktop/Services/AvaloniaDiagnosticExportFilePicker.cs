using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IDiagnosticExportFilePicker"/> backed by Avalonia's native <see cref="IStorageProvider"/>
/// — mirrors <see cref="AvaloniaPdfSaveFilePicker"/>. Never writes anywhere the user did not
/// explicitly choose through the picker.
/// </summary>
public sealed class AvaloniaDiagnosticExportFilePicker : IDiagnosticExportFilePicker
{
    private static readonly FilePickerFileType ZipFileType = new("ZIP Archive") { Patterns = ["*.zip"] };

    public async Task<DiagnosticExportSaveResult> SaveAsync(byte[] zipBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return DiagnosticExportSaveResult.Failed("No application window is available to save diagnostics.");
        }

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Diagnostics",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "zip",
                FileTypeChoices = [ZipFileType],
            });

            if (file is null)
            {
                return DiagnosticExportSaveResult.Cancelled();
            }

            await using (var stream = await file.OpenWriteAsync())
            {
                await stream.WriteAsync(zipBytes, cancellationToken);
            }

            return DiagnosticExportSaveResult.Saved(file.TryGetLocalPath() ?? file.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DiagnosticExportSaveResult.Failed("Diagnostics could not be saved. Try again.");
        }
    }

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
