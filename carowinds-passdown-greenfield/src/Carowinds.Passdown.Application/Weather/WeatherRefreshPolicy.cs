namespace Carowinds.Passdown.Application.Weather;

/// <summary>
/// When one endpoint may be requested again after a successful check: never before <see cref="Floor"/>,
/// at <see cref="Target"/> when set (otherwise when the provider's cache expiry passes), and no later than
/// <see cref="Ceiling"/>. A provider expiry later than the target is respected up to the ceiling.
/// </summary>
public sealed record WeatherEndpointPolicy(TimeSpan Floor, TimeSpan? Target, TimeSpan Ceiling)
{
    public DateTimeOffset DueAt<T>(WeatherEndpointState<T> state)
        where T : class
    {
        DateTimeOffset due;
        if (state.CheckedAt is not { } checkedAt)
        {
            due = DateTimeOffset.MinValue;
        }
        else
        {
            due = Target is { } target ? checkedAt + target : state.ExpiresAt ?? checkedAt + Ceiling;
            if (state.ExpiresAt is { } expires && expires > due)
            {
                due = expires;
            }

            if (due < checkedAt + Floor)
            {
                due = checkedAt + Floor;
            }

            if (due > checkedAt + Ceiling)
            {
                due = checkedAt + Ceiling;
            }
        }

        return state.NextAttemptNotBefore is { } notBefore && notBefore > due ? notBefore : due;
    }
}

/// <summary>
/// The Phase 37B weather request and freshness policy (docs/phase37b-weather-retrieval-and-cache.md). Owner
/// decision D5: alerts every five minutes while the app is open, never faster than once a minute.
/// </summary>
public sealed record WeatherRefreshPolicy
{
    public WeatherEndpointPolicy Point { get; init; } = new(TimeSpan.FromHours(24), null, TimeSpan.FromDays(7));

    public WeatherEndpointPolicy Stations { get; init; } = new(TimeSpan.FromHours(24), null, TimeSpan.FromDays(7));

    public WeatherEndpointPolicy Forecast { get; init; } = new(TimeSpan.FromMinutes(30), null, TimeSpan.FromHours(3));

    public WeatherEndpointPolicy Hourly { get; init; } = new(TimeSpan.FromMinutes(30), null, TimeSpan.FromHours(3));

    public WeatherEndpointPolicy Observation { get; init; } = new(TimeSpan.FromMinutes(10), null, TimeSpan.FromHours(1));

    public WeatherEndpointPolicy Alerts { get; init; } = new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

    /// <summary>An observation is not requested again until it is at least this old (stations report about hourly).</summary>
    public TimeSpan ObservationRefreshAge { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>Delay after the 1st, 2nd, 3rd and later consecutive failures; a longer provider Retry-After wins.</summary>
    public IReadOnlyList<TimeSpan> FailureBackoff { get; init; } =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    /// <summary>How many stations, nearest first, are tried for the current observation.</summary>
    public int ObservationStationCount { get; init; } = 2;

    public TimeSpan ObservationStaleAfter { get; init; } = TimeSpan.FromMinutes(75);

    public TimeSpan ObservationUnavailableAfter { get; init; } = TimeSpan.FromHours(3);

    public TimeSpan ForecastStaleAfter { get; init; } = TimeSpan.FromHours(3);

    public TimeSpan ForecastUnavailableAfter { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan AlertCheckStaleAfter { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan BackoffAfter(int consecutiveFailures, TimeSpan? retryAfter)
    {
        var index = Math.Clamp(consecutiveFailures - 1, 0, FailureBackoff.Count - 1);
        var backoff = FailureBackoff[index];
        return retryAfter is { } asked && asked > backoff ? asked : backoff;
    }
}
