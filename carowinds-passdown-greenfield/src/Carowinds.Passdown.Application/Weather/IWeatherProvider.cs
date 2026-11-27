using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Weather;

public enum WeatherFetchOutcome
{
    /// <summary>A new value was retrieved and parsed.</summary>
    Retrieved,

    /// <summary>The provider confirmed the cached value is current (HTTP 304).</summary>
    NotModified,

    /// <summary>Nothing usable came back; the cached value must be kept unchanged.</summary>
    Failed,
}

public enum WeatherFailureKind
{
    None,
    Network,
    Timeout,
    RateLimited,
    ProviderError,
    NotFound,
    InvalidResponse,
}

/// <summary>Validators from an earlier response, sent so the provider can answer "not modified".</summary>
public sealed record WeatherConditionalHeaders(string? ETag, DateTimeOffset? LastModified)
{
    public static WeatherConditionalHeaders None { get; } = new(null, null);
}

/// <summary>
/// One provider call's result: the value (only for <see cref="WeatherFetchOutcome.Retrieved"/>), the cache
/// validators and expiry the provider sent, or the failure kind and any retry delay it asked for.
/// </summary>
public sealed record WeatherFetchResult<T>(
    WeatherFetchOutcome Outcome,
    T? Value,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset? ExpiresAt,
    WeatherFailureKind Failure,
    TimeSpan? RetryAfter,
    string? Detail)
    where T : class
{
    public static WeatherFetchResult<T> Retrieved(T value, string? etag, DateTimeOffset? lastModified, DateTimeOffset? expiresAt) =>
        new(WeatherFetchOutcome.Retrieved, value, etag, lastModified, expiresAt, WeatherFailureKind.None, null, null);

    public static WeatherFetchResult<T> NotModified(string? etag, DateTimeOffset? lastModified, DateTimeOffset? expiresAt) =>
        new(WeatherFetchOutcome.NotModified, null, etag, lastModified, expiresAt, WeatherFailureKind.None, null, null);

    public static WeatherFetchResult<T> Failed(WeatherFailureKind failure, TimeSpan? retryAfter = null, string? detail = null) =>
        new(WeatherFetchOutcome.Failed, null, null, null, null, failure, retryAfter, detail);
}

/// <summary>
/// Port to the weather provider (NWS). Each method makes at most one logical request and never throws for an
/// ordinary HTTP, network, timeout or parse failure: it returns <see cref="WeatherFetchOutcome.Failed"/>.
/// Caller cancellation is rethrown.
/// </summary>
public interface IWeatherProvider
{
    Task<WeatherFetchResult<WeatherPointMetadata>> GetPointAsync(WeatherReferencePoint point, CancellationToken cancellationToken);

    Task<WeatherFetchResult<WeatherForecast>> GetForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken);

    Task<WeatherFetchResult<HourlyWeatherForecast>> GetHourlyForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken);

    Task<WeatherFetchResult<IReadOnlyList<WeatherStation>>> GetStationsAsync(WeatherPointMetadata point, CancellationToken cancellationToken);

    Task<WeatherFetchResult<WeatherObservation>> GetLatestObservationAsync(WeatherStation station, WeatherReferencePoint distanceFrom, WeatherConditionalHeaders conditional, CancellationToken cancellationToken);

    Task<WeatherFetchResult<IReadOnlyList<WeatherAlert>>> GetActiveAlertsAsync(WeatherReferencePoint point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken);
}
