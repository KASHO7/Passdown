using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Weather;

/// <summary>
/// The cached state of one weather endpoint. <see cref="Value"/> is the last successfully retrieved value and
/// is never cleared by a failure (last-known-good). <see cref="CheckedAt"/> is the last time the provider
/// confirmed it (a 200 or a 304); <see cref="LastAttemptAt"/> includes failed attempts.
/// </summary>
public sealed record WeatherEndpointState<T>
    where T : class
{
    public T? Value { get; init; }

    public DateTimeOffset? RetrievedAt { get; init; }

    public DateTimeOffset? CheckedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? ETag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public WeatherFailureKind LastFailure { get; init; }

    public int ConsecutiveFailures { get; init; }

    public DateTimeOffset? NextAttemptNotBefore { get; init; }

    /// <summary>Validators to send, only while a value is held (a 304 without a value would be meaningless).</summary>
    public WeatherConditionalHeaders ConditionalHeaders =>
        Value is null ? WeatherConditionalHeaders.None : new WeatherConditionalHeaders(ETag, LastModified);
}

/// <summary>
/// Everything the weather cache file holds (Phase 37B). Local and disposable: never synced, never part of a
/// draft, History or PDF, and never in SQLite. <see cref="FormatVersion"/> is the file's own version; an
/// unknown version is read as an empty cache.
/// </summary>
public sealed record WeatherCacheDocument
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Keyed by <see cref="WeatherReferencePoint.Id"/>.</summary>
    public IReadOnlyDictionary<string, WeatherEndpointState<WeatherPointMetadata>> Points { get; init; } =
        new Dictionary<string, WeatherEndpointState<WeatherPointMetadata>>(StringComparer.Ordinal);

    public WeatherEndpointState<IReadOnlyList<WeatherStation>> Stations { get; init; } = new();

    public WeatherEndpointState<WeatherForecast> Forecast { get; init; } = new();

    public WeatherEndpointState<HourlyWeatherForecast> Hourly { get; init; } = new();

    /// <summary>Latest observation per station id, only for the stations the coordinator uses.</summary>
    public IReadOnlyDictionary<string, WeatherEndpointState<WeatherObservation>> Observations { get; init; } =
        new Dictionary<string, WeatherEndpointState<WeatherObservation>>(StringComparer.Ordinal);

    /// <summary>Active-alert lists keyed by <see cref="WeatherReferencePoint.Id"/>.</summary>
    public IReadOnlyDictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>> Alerts { get; init; } =
        new Dictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>>(StringComparer.Ordinal);

    public static WeatherCacheDocument Empty { get; } = new();
}

/// <summary>Port to the local weather cache file.</summary>
public interface IWeatherCacheStore
{
    /// <summary>The cached document, or null when there is none or it cannot be used (missing, corrupt, too large, unknown version).</summary>
    Task<WeatherCacheDocument?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the document atomically. Returns false (and keeps the previous file) when it cannot be written or exceeds the size limit.</summary>
    Task<bool> SaveAsync(WeatherCacheDocument document, CancellationToken cancellationToken = default);
}
