using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Weather;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Weather;

/// <summary>One endpoint failure in a refresh, for diagnostics and tests.</summary>
public sealed record WeatherEndpointFailure(string Endpoint, WeatherFailureKind Kind);

/// <summary>What one refresh did. <see cref="RequestCount"/> counts provider calls (each one logical HTTP request).</summary>
public sealed record WeatherRefreshReport(bool Permitted, int RequestCount, bool GridChanged, IReadOnlyList<WeatherEndpointFailure> Failures)
{
    public static WeatherRefreshReport NotPermitted { get; } = new(false, 0, false, []);
}

/// <summary>
/// Coordinates weather retrieval and the local cache (Phase 37B). Separate from the official-data refresh: it
/// never runs because of date selection or navigation, and nothing in the application calls it yet (the
/// feature is not activated). Each call requests only the endpoints that are due under
/// <see cref="WeatherRefreshPolicy"/>. Refreshes are single-flight: a call made while one runs joins it.
/// A failure never replaces cached values. No request is made unless the location is verified (decision D8).
/// </summary>
public sealed class WeatherRefreshCoordinator(
    IWeatherProvider provider,
    IWeatherCacheStore store,
    IClock clock,
    WeatherLocation location,
    WeatherRefreshPolicy? policy = null,
    ILogger<WeatherRefreshCoordinator>? logger = null)
{
    private readonly WeatherRefreshPolicy _policy = policy ?? new WeatherRefreshPolicy();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private Task<WeatherRefreshReport>? _inFlight;
    private volatile WeatherCacheDocument? _cache;

    public WeatherRefreshPolicy Policy => _policy;

    /// <summary>
    /// Requests whatever is due. While a refresh is running, returns that refresh; its cancellation belongs to
    /// the caller that started it.
    /// </summary>
    public Task<WeatherRefreshReport> RefreshDueAsync(CancellationToken cancellationToken = default)
    {
        if (!location.IsRetrievalPermitted)
        {
            return Task.FromResult(WeatherRefreshReport.NotPermitted);
        }

        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running)
            {
                return running;
            }

            _inFlight = RunAsync(cancellationToken);
            return _inFlight;
        }
    }

    /// <summary>The current weather built from the local cache only; never makes a request.</summary>
    public async Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var document = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return WeatherSnapshotBuilder.Build(document, clock.UtcNow, _policy, location);
    }

    /// <summary>The earliest instant any endpoint becomes due, or null when retrieval is not permitted.</summary>
    public async Task<DateTimeOffset?> GetNextDueAtAsync(CancellationToken cancellationToken = default)
    {
        if (!location.IsRetrievalPermitted)
        {
            return null;
        }

        var document = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var dues = new List<DateTimeOffset>();
        foreach (var point in location.Points)
        {
            dues.Add(_policy.Point.DueAt(PointState(document, point)));
            dues.Add(_policy.Alerts.DueAt(AlertState(document, point)));
        }

        if (PointState(document, location.ForecastPoint).Value is not null)
        {
            dues.Add(_policy.Stations.DueAt(document.Stations));
            dues.Add(_policy.Forecast.DueAt(document.Forecast));
            dues.Add(_policy.Hourly.DueAt(document.Hourly));
            if (document.Stations.Value is { Count: > 0 } stations)
            {
                dues.Add(ObservationDueAt(ObservationState(document, stations[0].Id)));
            }
        }

        return dues.Min();
    }

    private async Task<WeatherRefreshReport> RunAsync(CancellationToken cancellationToken)
    {
        var document = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var requests = 0;
        var gridChanged = false;
        var failures = new List<WeatherEndpointFailure>();

        // 1. Point → grid and zones, for every reference point.
        foreach (var point in location.Points)
        {
            var state = PointState(document, point);
            if (!IsDue(_policy.Point.DueAt(state)))
            {
                continue;
            }

            var result = await provider.GetPointAsync(point, cancellationToken).ConfigureAwait(false);
            requests++;
            Record(failures, $"points:{point.Id}", result);
            var updated = Apply(state, result);

            if (point == location.ForecastPoint && state.Value is { } previous && result.Value is { } current && !previous.HasSameGrid(current))
            {
                // NWS can move a point to another grid or office: everything derived from the old grid is discarded.
                gridChanged = true;
                logger?.LogInformation("Weather forecast grid changed from {OldGrid} to {NewGrid}; forecast cache cleared.", $"{previous.GridId}/{previous.GridX},{previous.GridY}", $"{current.GridId}/{current.GridX},{current.GridY}");
                document = document with
                {
                    Forecast = new(),
                    Hourly = new(),
                    Stations = new(),
                    Observations = new Dictionary<string, WeatherEndpointState<WeatherObservation>>(StringComparer.Ordinal),
                };
            }

            document = document with { Points = With(document.Points, point.Id, updated) };
            _cache = document;
        }

        // 2. Forecasts, stations and the current observation come from the forecast point's grid.
        if (PointState(document, location.ForecastPoint).Value is { } grid)
        {
            if (IsDue(_policy.Stations.DueAt(document.Stations)))
            {
                var result = await provider.GetStationsAsync(grid, cancellationToken).ConfigureAwait(false);
                requests++;
                Record(failures, "stations", result);
                document = InvalidateGridOnNotFound(document with { Stations = Apply(document.Stations, result) }, result);
                _cache = document;
            }

            if (IsDue(_policy.Forecast.DueAt(document.Forecast)))
            {
                var result = await provider.GetForecastAsync(grid, document.Forecast.ConditionalHeaders, cancellationToken).ConfigureAwait(false);
                requests++;
                Record(failures, "forecast", result);
                document = InvalidateGridOnNotFound(document with { Forecast = Apply(document.Forecast, result) }, result);
                _cache = document;
            }

            if (IsDue(_policy.Hourly.DueAt(document.Hourly)))
            {
                var result = await provider.GetHourlyForecastAsync(grid, document.Hourly.ConditionalHeaders, cancellationToken).ConfigureAwait(false);
                requests++;
                Record(failures, "hourly", result);
                document = InvalidateGridOnNotFound(document with { Hourly = Apply(document.Hourly, result) }, result);
                _cache = document;
            }

            // Nearest station first; the next one only when the nearer one has nothing fresh.
            foreach (var station in (document.Stations.Value ?? []).Take(_policy.ObservationStationCount))
            {
                var state = ObservationState(document, station.Id);
                if (IsDue(ObservationDueAt(state)))
                {
                    var result = await provider.GetLatestObservationAsync(station, location.ForecastPoint, state.ConditionalHeaders, cancellationToken).ConfigureAwait(false);
                    requests++;
                    Record(failures, $"observation:{station.Id}", result);
                    state = Apply(state, result);
                    document = document with { Observations = With(document.Observations, station.Id, state) };
                    _cache = document;
                }

                if (IsUsableNow(state.Value))
                {
                    break;
                }
            }
        }

        // 3. Active alerts for every reference point (both sides of the state line).
        foreach (var point in location.Points)
        {
            var state = AlertState(document, point);
            if (!IsDue(_policy.Alerts.DueAt(state)))
            {
                continue;
            }

            var result = await provider.GetActiveAlertsAsync(point, state.ConditionalHeaders, cancellationToken).ConfigureAwait(false);
            requests++;
            Record(failures, $"alerts:{point.Id}", result);
            document = document with { Alerts = With(document.Alerts, point.Id, Apply(state, result)) };
            _cache = document;
        }

        document = WeatherCacheRetention.Prune(document, clock.UtcNow, _policy.ObservationStationCount);
        _cache = document;

        if (requests > 0 && !await store.SaveAsync(document, cancellationToken).ConfigureAwait(false))
        {
            logger?.LogWarning("Weather cache could not be saved; the in-memory weather is kept for this session.");
        }

        return new WeatherRefreshReport(true, requests, gridChanged, failures);
    }

    private async Task<WeatherCacheDocument> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } loaded)
        {
            return loaded;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is null)
            {
                var document = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
                _cache = document is { FormatVersion: WeatherCacheDocument.CurrentFormatVersion } ? document : WeatherCacheDocument.Empty;
            }

            return _cache;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private bool IsDue(DateTimeOffset dueAt) => clock.UtcNow >= dueAt;

    private DateTimeOffset ObservationDueAt(WeatherEndpointState<WeatherObservation> state)
    {
        var due = _policy.Observation.DueAt(state);
        if (state.Value is { } observation && state.NextAttemptNotBefore is null)
        {
            var notYetReported = observation.Timestamp + _policy.ObservationRefreshAge;
            if (notYetReported > due)
            {
                due = notYetReported;
            }
        }

        return due;
    }

    private bool IsUsableNow(WeatherObservation? observation) =>
        observation is { TemperatureF: not null } && clock.UtcNow - observation.Timestamp <= _policy.ObservationStaleAfter;

    private WeatherEndpointState<T> Apply<T>(WeatherEndpointState<T> state, WeatherFetchResult<T> result)
        where T : class
    {
        var now = clock.UtcNow;
        switch (result.Outcome)
        {
            case WeatherFetchOutcome.Retrieved when result.Value is not null:
                return state with
                {
                    Value = result.Value,
                    RetrievedAt = now,
                    CheckedAt = now,
                    ExpiresAt = result.ExpiresAt,
                    ETag = result.ETag,
                    LastModified = result.LastModified,
                    LastAttemptAt = now,
                    LastFailure = WeatherFailureKind.None,
                    ConsecutiveFailures = 0,
                    NextAttemptNotBefore = null,
                };

            case WeatherFetchOutcome.NotModified when state.Value is not null:
                return state with
                {
                    CheckedAt = now,
                    ExpiresAt = result.ExpiresAt,
                    ETag = result.ETag ?? state.ETag,
                    LastModified = result.LastModified ?? state.LastModified,
                    LastAttemptAt = now,
                    LastFailure = WeatherFailureKind.None,
                    ConsecutiveFailures = 0,
                    NextAttemptNotBefore = null,
                };

            default:
                var failure = result.Outcome == WeatherFetchOutcome.Failed ? result.Failure : WeatherFailureKind.InvalidResponse;
                var failures = state.ConsecutiveFailures + 1;
                return state with
                {
                    LastAttemptAt = now,
                    LastFailure = failure,
                    ConsecutiveFailures = failures,
                    NextAttemptNotBefore = now + _policy.BackoffAfter(failures, result.RetryAfter),
                };
        }
    }

    /// <summary>A 404 from a grid endpoint means the point may have moved: the point is looked up again on the next refresh.</summary>
    private WeatherCacheDocument InvalidateGridOnNotFound<T>(WeatherCacheDocument document, WeatherFetchResult<T> result)
        where T : class
    {
        if (result.Failure != WeatherFailureKind.NotFound)
        {
            return document;
        }

        var point = PointState(document, location.ForecastPoint);
        return document with { Points = With(document.Points, location.ForecastPoint.Id, point with { CheckedAt = null, NextAttemptNotBefore = null }) };
    }

    private static void Record<T>(List<WeatherEndpointFailure> failures, string endpoint, WeatherFetchResult<T> result)
        where T : class
    {
        if (result.Outcome == WeatherFetchOutcome.Failed)
        {
            failures.Add(new WeatherEndpointFailure(endpoint, result.Failure));
        }
    }

    private static WeatherEndpointState<WeatherPointMetadata> PointState(WeatherCacheDocument document, WeatherReferencePoint point) =>
        document.Points.TryGetValue(point.Id, out var state) ? state : new();

    private static WeatherEndpointState<IReadOnlyList<WeatherAlert>> AlertState(WeatherCacheDocument document, WeatherReferencePoint point) =>
        document.Alerts.TryGetValue(point.Id, out var state) ? state : new();

    private static WeatherEndpointState<WeatherObservation> ObservationState(WeatherCacheDocument document, string stationId) =>
        document.Observations.TryGetValue(stationId, out var state) ? state : new();

    private static IReadOnlyDictionary<string, TValue> With<TValue>(IReadOnlyDictionary<string, TValue> source, string key, TValue value)
    {
        var copy = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            copy[pair.Key] = pair.Value;
        }

        copy[key] = value;
        return copy;
    }
}
