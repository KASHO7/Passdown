using System.Net;
using System.Text.Json;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Weather.Nws;

/// <summary>
/// <see cref="IWeatherProvider"/> over the NWS API (Phase 37B; not registered in the application yet). Maps
/// every HTTP, network, timeout and parse failure to a <see cref="WeatherFetchOutcome.Failed"/> result so a
/// failure can never reach the cache as data. Expiry comes from <c>Cache-Control: max-age</c>, else
/// <c>Expires</c>. Only caller cancellation is rethrown.
/// </summary>
public sealed class NwsWeatherProvider(INwsHttpTransport transport, Func<DateTimeOffset>? utcNow = null, ILogger<NwsWeatherProvider>? logger = null) : IWeatherProvider
{
    private static readonly Uri ApiRoot = new($"https://{NwsHttpTransport.ApiHost}/");

    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public Task<WeatherFetchResult<WeatherPointMetadata>> GetPointAsync(WeatherReferencePoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        return FetchAsync(new Uri(ApiRoot, $"points/{point.ToNwsPointText()}"), WeatherConditionalHeaders.None, NwsPointParser.Parse, cancellationToken);
    }

    public Task<WeatherFetchResult<WeatherForecast>> GetForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        return FetchAsync(point.ForecastUrl, conditional, json => NwsForecastParser.ParseTwelveHour(json, point), cancellationToken);
    }

    public Task<WeatherFetchResult<HourlyWeatherForecast>> GetHourlyForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        return FetchAsync(point.ForecastHourlyUrl, conditional, json => NwsForecastParser.ParseHourly(json, point), cancellationToken);
    }

    public Task<WeatherFetchResult<IReadOnlyList<WeatherStation>>> GetStationsAsync(WeatherPointMetadata point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        return FetchAsync(point.ObservationStationsUrl, WeatherConditionalHeaders.None, NwsStationParser.Parse, cancellationToken);
    }

    public Task<WeatherFetchResult<WeatherObservation>> GetLatestObservationAsync(WeatherStation station, WeatherReferencePoint distanceFrom, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(distanceFrom);
        if (!NwsStationParser.IsStationId(station.Id))
        {
            return Task.FromResult(WeatherFetchResult<WeatherObservation>.Failed(WeatherFailureKind.InvalidResponse, detail: "Unexpected station identifier."));
        }

        var distance = Math.Round(WeatherUnits.DistanceKm((double)distanceFrom.Latitude, (double)distanceFrom.Longitude, station.Latitude, station.Longitude), 1);
        return FetchAsync(new Uri(ApiRoot, $"stations/{station.Id}/observations/latest"), conditional, json => NwsObservationParser.Parse(json, station, distance), cancellationToken);
    }

    public Task<WeatherFetchResult<IReadOnlyList<WeatherAlert>>> GetActiveAlertsAsync(WeatherReferencePoint point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        return FetchAsync<IReadOnlyList<WeatherAlert>>(
            new Uri(ApiRoot, $"alerts/active?point={point.ToNwsPointText()}"),
            conditional,
            json =>
            {
                var parsed = NwsAlertParser.Parse(json);
                if (parsed.SkippedCount > 0)
                {
                    logger?.LogWarning("Skipped {Count} unusable NWS alert entries.", parsed.SkippedCount);
                }

                return parsed.Alerts;
            },
            cancellationToken);
    }

    private async Task<WeatherFetchResult<T>> FetchAsync<T>(Uri uri, WeatherConditionalHeaders conditional, Func<string, T> parse, CancellationToken cancellationToken)
        where T : class
    {
        NwsHttpResponse response;
        try
        {
            response = await transport.GetAsync(uri, conditional, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return Failure<T>(WeatherFailureKind.Timeout, uri, ex);
        }
        catch (HttpRequestException ex)
        {
            return Failure<T>(WeatherFailureKind.Network, uri, ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            return Failure<T>(WeatherFailureKind.InvalidResponse, uri, ex);
        }

        var expiresAt = response.MaxAge is { } maxAge ? _utcNow() + maxAge : response.Expires;
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotModified:
                return WeatherFetchResult<T>.NotModified(response.ETag, response.LastModified, expiresAt);

            case HttpStatusCode.OK when response.Body is not null:
                try
                {
                    return WeatherFetchResult<T>.Retrieved(parse(response.Body), response.ETag, response.LastModified, expiresAt);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
                {
                    return Failure<T>(WeatherFailureKind.InvalidResponse, uri, ex);
                }

            case HttpStatusCode.NotFound:
                return Failure<T>(WeatherFailureKind.NotFound, uri, null);

            case HttpStatusCode.TooManyRequests:
                return Failure<T>(WeatherFailureKind.RateLimited, uri, null, response.RetryAfter);

            default:
                return Failure<T>(WeatherFailureKind.ProviderError, uri, null, response.RetryAfter, $"HTTP {(int)response.StatusCode}");
        }
    }

    private WeatherFetchResult<T> Failure<T>(WeatherFailureKind kind, Uri uri, Exception? exception, TimeSpan? retryAfter = null, string? detail = null)
        where T : class
    {
        logger?.LogWarning("NWS request {Path} failed: {Kind} {Detail}{ExceptionType}", uri.AbsolutePath, kind, detail, exception?.GetType().Name);
        return WeatherFetchResult<T>.Failed(kind, retryAfter, detail ?? exception?.GetType().Name);
    }
}
