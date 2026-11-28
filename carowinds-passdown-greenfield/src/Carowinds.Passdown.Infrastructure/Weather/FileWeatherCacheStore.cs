using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Weather;

/// <summary>
/// Phase 37B: <see cref="IWeatherCacheStore"/> backed by one local JSON file (<c>weather-cache.json</c>) under
/// <see cref="AppPaths.LocalApplicationDataDirectory"/>, written with <see cref="AtomicFileWriter"/> like the
/// other small local files. No SQLite table, no migration; outside the install folder, the Personal Sync
/// folder and release packages. The file is disposable: a missing, unreadable, oversized or unknown-version
/// file is treated as no cache.
/// </summary>
public sealed class FileWeatherCacheStore(string cacheFilePath, ILogger<FileWeatherCacheStore>? logger = null) : IWeatherCacheStore
{
    /// <summary>Upper bound for the file; a larger document is not written and a larger file is not read.</summary>
    public const int MaxBytes = 256 * 1024;

    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "weather-cache.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public string CacheFilePath { get; } = cacheFilePath;

    public async Task<WeatherCacheDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var file = new FileInfo(CacheFilePath);
            if (!file.Exists)
            {
                return null;
            }

            if (file.Length > MaxBytes)
            {
                logger?.LogWarning("Weather cache is larger than {MaxBytes} bytes; ignoring it.", MaxBytes);
                return null;
            }

            var json = await File.ReadAllTextAsync(CacheFilePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<WeatherCacheDocument>(json, Options);
            if (document is null || document.FormatVersion != WeatherCacheDocument.CurrentFormatVersion)
            {
                logger?.LogInformation("Weather cache has an unknown format; ignoring it.");
                return null;
            }

            return document;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Weather cache could not be read ({ExceptionType}); ignoring it.", ex.GetType().Name);
            return null;
        }
    }

    public async Task<bool> SaveAsync(WeatherCacheDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonSerializer.Serialize(document, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            logger?.LogWarning("Weather cache would exceed {MaxBytes} bytes; not written.", MaxBytes);
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileWriter.WriteTextAsync(CacheFilePath, json, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Weather cache could not be written ({ExceptionType}).", ex.GetType().Name);
            return false;
        }
    }
}
