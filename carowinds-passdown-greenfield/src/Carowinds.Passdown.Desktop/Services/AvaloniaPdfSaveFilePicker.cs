using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IPdfSaveFilePicker"/> backed by Avalonia's native <see cref="IStorageProvider"/>
/// (spec Part I: "use Avalonia's native storage/save-file APIs"). Never writes anywhere the user did
/// not explicitly choose through the picker — there is no fallback/default directory.
/// </summary>
public sealed class AvaloniaPdfSaveFilePicker : IPdfSaveFilePicker
{
    private static readonly FilePickerFileType PdfFileType = new("PDF Document") { Patterns = ["*.pdf"] };

    public async Task<PdfSaveResult> SaveAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return PdfSaveResult.Failed("No application window is available to save the PDF.");
        }

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Passdown PDF",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "pdf",
                FileTypeChoices = [PdfFileType],
            });

            if (file is null)
            {
                return PdfSaveResult.Cancelled();
            }

            await using (var stream = await file.OpenWriteAsync())
            {
                await stream.WriteAsync(pdfBytes, cancellationToken);
            }

            return PdfSaveResult.Saved(file.TryGetLocalPath() ?? file.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PdfSaveResult.Failed("The PDF could not be saved. Try again.");
        }
    }

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
