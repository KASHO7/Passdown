using System.Text.Json;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 33: <see cref="IPdfPreviewLayoutPreferenceStore"/> backed by one small local JSON file
/// (<c>pdf-preview-layout.json</c>) under <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same
/// flat-file convention and atomic writer as the other device preferences; no SQLite table, no migration,
/// outside the install folder and outside the Personal Sync folder (never synced). Out-of-range values
/// (a negative width, an absurd zoom) fall back to the built-in default for that value.
/// </summary>
public sealed class FilePdfPreviewLayoutPreferenceStore(string configFilePath, ILogger<FilePdfPreviewLayoutPreferenceStore>? logger = null) : IPdfPreviewLayoutPreferenceStore
{
    public const double MinStoredZoom = 0.25;
    public const double MaxStoredZoom = 4.0;
    public const double MinStoredWidth = 200;
    public const double MaxStoredWidth = 4000;

    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "pdf-preview-layout.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public string ConfigFilePath { get; } = configFilePath;

    public async Task<PdfPreviewLayoutPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        var builtIn = PdfPreviewLayoutPreferences.BuiltIn;
        if (!File.Exists(ConfigFilePath))
        {
            return builtIn;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<PreferencesFile>(json, Options);
            if (file is null)
            {
                return builtIn;
            }

            var width = file.PreviewPaneWidth is { } w && double.IsFinite(w) && w >= MinStoredWidth && w <= MaxStoredWidth ? w : builtIn.PreviewPaneWidth;
            var zoom = file.Zoom is { } z && double.IsFinite(z) && z >= MinStoredZoom && z <= MaxStoredZoom ? z : builtIn.Zoom;
            var fit = file.FitMode is { } f && Enum.TryParse<PdfPreviewFitMode>(f, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) ? parsed : builtIn.FitMode;
            return new PdfPreviewLayoutPreferences(file.IsPreviewVisible ?? builtIn.IsPreviewVisible, width, fit, zoom);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("PDF preview layout preferences could not be read ({ExceptionType}); using built-in defaults.", ex.GetType().Name);
            return builtIn;
        }
    }

    public async Task SaveAsync(PdfPreviewLayoutPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var json = JsonSerializer.Serialize(
            new PreferencesFile(preferences.IsPreviewVisible, Math.Round(preferences.PreviewPaneWidth, 1), preferences.FitMode.ToString(), Math.Round(preferences.Zoom, 4)),
            Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's shape; nullable so "missing" is distinguishable from an explicit value.</summary>
    private sealed record PreferencesFile(bool? IsPreviewVisible, double? PreviewPaneWidth, string? FitMode, double? Zoom);
}
