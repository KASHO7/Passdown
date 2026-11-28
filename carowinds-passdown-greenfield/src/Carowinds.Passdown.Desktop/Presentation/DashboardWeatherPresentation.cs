using System.Globalization;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One active NWS alert in the expanded Weather details (Phase 37C).</summary>
public sealed record DashboardWeatherAlertItem(
    string Event,
    string SeverityAndTimingText,
    string AppliesToText,
    string SourceText,
    Uri? Link,
    string AccessibleName)
{
    public bool HasLink => Link is not null;
}

/// <summary>A label/value row in the expanded Weather details.</summary>
public sealed record DashboardWeatherDetailRow(string Label, string Value, bool IsStale = false);

/// <summary>
/// Phase 37C: everything the Dashboard Weather card shows, built from a <see cref="WeatherSnapshot"/> (local cache
/// only), today's park hours and the current instant — the approved D4 mock-up's content without any of its sample
/// values. Observed, forecast and alert values stay in their own sections and carry their own source and time; a
/// part that is unavailable shows no value; a stale part shows its age; and alert status is "unknown" rather than
/// "none" whenever the last check is not current. Informational only: nothing here decides an operation.
/// </summary>
public sealed record DashboardWeatherPresentation
{
    public const string ProviderName = WeatherAttribution.ProviderName;
    public const string StaleHeaderText = "May be out of date";
    public const string ObservationUnavailableText = "Current conditions unavailable";
    public const string ForecastUnavailableText = "Forecast unavailable";
    public const string HighNotInForecastText = "Today's high not in current forecast";
    public const string RainChanceUnavailableText = "Rain chance not available";
    public const string LoadingText = "Getting weather from the National Weather Service…";
    public const string LoadingAlertText = "Alert status not yet checked.";
    public const string SafetyNoteText =
        "Informational only, not a warning system. NWS alerts for the NC and SC portions of the park are checked about every 5 minutes while the app is open. Park weather procedures and official warning channels govern operations.";

    /// <summary>Short names for nearby NWS stations, keyed by the station that actually supplied the observation. Any other station shows its NWS name.</summary>
    private static readonly IReadOnlyDictionary<string, string> StationShortNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["KCLT"] = "Charlotte airport",
        ["KUZA"] = "Rock Hill airport",
        ["KAKH"] = "Gastonia airport",
        ["KEQY"] = "Monroe airport",
    };

    // ---- Overall state ----
    public bool IsLoading { get; init; }

    public bool IsContentVisible => !IsLoading;

    public string HeaderMetaText { get; init; } = ProviderName;

    public bool HeaderIsStale { get; init; }

    /// <summary>The one-sentence summary a screen reader announces for the card.</summary>
    public string AutomationText { get; init; } = string.Empty;

    // ---- Status strip (stale / unavailable) ----
    public bool HasStatusStrip => StatusHeadline is not null;

    public string? StatusHeadline { get; init; }

    public string? StatusDetail { get; init; }

    public string? StatusAlertText { get; init; }

    public bool HasStatusAlertText => StatusAlertText is not null;

    /// <summary>True for the stale treatment (stale colours); false for the neutral unavailable treatment.</summary>
    public bool StatusIsStale { get; init; }

    // ---- Alert strip ----
    public bool HasAlertStrip => AlertHeadline is not null;

    public string? AlertHeadline { get; init; }

    public string? AlertPrimaryText { get; init; }

    public string? AlertSecondaryText { get; init; }

    public bool HasAlertSecondaryText => AlertSecondaryText is not null;

    // ---- Observed ----
    public string ObservedKindText { get; init; } = "OBSERVED";

    public bool HasObservation => TemperatureText is not null;

    public bool ShowObservationUnavailable => !HasObservation;

    public string? TemperatureText { get; init; }

    public string? ConditionText { get; init; }

    public string? WindText { get; init; }

    public bool HasWindText => WindText is not null;

    public string? ObservationSourceText { get; init; }

    public bool ObservationIsStale { get; init; }

    // ---- Forecast ----
    public string ForecastKindText { get; init; } = "NWS FORECAST";

    public bool HasForecast { get; init; }

    public bool ShowForecastUnavailable => !HasForecast;

    public string? ForecastUnavailableDetail { get; init; }

    public string? HighText { get; init; }

    public bool HasHigh => HighText is not null;

    public bool ShowHighNotInForecast => HasForecast && HighText is null;

    public string? LowText { get; init; }

    public bool HasLow => LowText is not null;

    public string? RainChanceText { get; init; }

    public bool HasRainChance => RainChanceText is not null;

    public bool ShowRainChanceUnavailable => HasForecast && RainChanceText is null;

    public string? ForecastSourceText { get; init; }

    public bool ForecastIsStale { get; init; }

    // ---- Footer ----
    public string FooterText { get; init; } = string.Empty;

    public bool FooterIsStale { get; init; }

    public bool CanExpand => !IsLoading;

    // ---- Expanded details ----
    public IReadOnlyList<DashboardWeatherAlertItem> Alerts { get; init; } = [];

    public bool HasAlerts => Alerts.Count > 0;

    public string AlertsHeading => Alerts.Count == 1 ? "ACTIVE NWS ALERT" : $"ACTIVE NWS ALERTS ({Alerts.Count}) · MOST SEVERE FIRST";

    public IReadOnlyList<DashboardWeatherDetailRow> DetailRows { get; init; } = [];

    public bool HasDetailRows => DetailRows.Count > 0;

    public string? ObservationNoteText { get; init; }

    public bool HasObservationNote => ObservationNoteText is not null;

    public string LastUpdatedText { get; init; } = string.Empty;

    public bool LastUpdatedIsStale { get; init; }

    public string SafetyNote => SafetyNoteText;

    public Uri ForecastPage { get; init; } = WeatherAttribution.ForecastPage(CarowindsWeatherLocation.NorthCarolina);

    /// <summary>Builds the card for <paramref name="snapshot"/> (null while the cache has not been read yet).</summary>
    /// <param name="parkStatus">Today's park status from the Dashboard snapshot; null when not loaded.</param>
    /// <param name="operatingDate">The Dashboard's operating date; park hours apply only when it is today's Eastern date.</param>
    public static DashboardWeatherPresentation Build(
        WeatherSnapshot? snapshot,
        DashboardParkStatusSummary? parkStatus,
        DateOnly? operatingDate,
        DateTimeOffset nowUtc,
        WeatherLocation? location = null)
    {
        location ??= CarowindsWeatherLocation.Park;
        var forecastPage = WeatherAttribution.ForecastPage(location.ForecastPoint);

        if (snapshot is null
            || (snapshot.RetrievalPermitted
                && snapshot.ObservationStatus.Availability == WeatherAvailability.NeverRetrieved
                && snapshot.ForecastStatus.Availability == WeatherAvailability.NeverRetrieved
                && snapshot.AlertsStatus.Availability == WeatherAvailability.NeverRetrieved))
        {
            return new DashboardWeatherPresentation
            {
                IsLoading = true,
                ForecastPage = forecastPage,
                AutomationText = $"Weather. {LoadingText} {LoadingAlertText}",
            };
        }

        var todayEastern = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(nowUtc).DateTime);
        var observation = BuildObservation(snapshot, nowUtc);
        var forecast = BuildForecast(snapshot, parkStatus, operatingDate, todayEastern, nowUtc);
        var alerts = BuildAlerts(snapshot, nowUtc, location);

        var obsShown = observation.Temperature is not null;
        var forecastShown = forecast.Shown;
        var alertsFresh = snapshot.AlertsStatus.Availability == WeatherAvailability.Fresh;
        var alertCheckedText = snapshot.AlertsStatus.CheckedAt is { } alertChecked ? EasternTimestampFormat.Relative(alertChecked, nowUtc) : null;
        var unknownAlertText = alertCheckedText is null
            ? "Alert status unknown — no successful check yet."
            : $"Alert status unknown — alerts last checked {alertCheckedText}";

        var lastSuccess = new[] { snapshot.ObservationStatus.CheckedAt, snapshot.ForecastStatus.CheckedAt, snapshot.AlertsStatus.CheckedAt }.Max();
        var lastAttempt = new[] { snapshot.ObservationStatus.LastAttemptAt, snapshot.ForecastStatus.LastAttemptAt, snapshot.AlertsStatus.LastAttemptAt }.Max();
        var latestFailure = LatestFailure(snapshot);

        string? statusHeadline = null;
        string? statusDetail = null;
        string? statusAlert = null;
        var statusIsStale = false;
        string footer;
        var footerStale = false;
        var headerStale = false;

        if (!snapshot.RetrievalPermitted)
        {
            statusHeadline = "Weather unavailable.";
            statusDetail = "Weather retrieval is not set up for the park's location.";
            statusAlert = unknownAlertText;
            footer = "Not checked";
        }
        else if (!obsShown && !forecastShown)
        {
            statusHeadline = "Weather unavailable.";
            statusDetail = FailureSentence(latestFailure) ?? "No current National Weather Service data is available.";
            statusAlert = alertsFresh ? null : unknownAlertText;
            footer = lastAttempt is { } attempt ? $"Last attempt {EasternTimestampFormat.Relative(attempt, nowUtc)}" : "Not checked yet";
        }
        else
        {
            var anyStale = snapshot.ObservationStatus.Availability == WeatherAvailability.Stale
                || snapshot.ForecastStatus.Availability == WeatherAvailability.Stale
                || !alertsFresh;
            if (anyStale)
            {
                headerStale = true;
                statusIsStale = true;
                var failed = snapshot.ObservationStatus.LastRefreshFailed || snapshot.ForecastStatus.LastRefreshFailed || snapshot.AlertsStatus.LastRefreshFailed;
                statusHeadline = failed ? "Weather could not be refreshed." : "Weather has not been updated recently.";
                statusDetail = "Showing the last NWS data received.";
                statusAlert = alertsFresh ? null : unknownAlertText;
                footer = lastSuccess is { } success ? $"Last updated {EasternTimestampFormat.Relative(success, nowUtc)}" : "Not updated yet";
                footerStale = true;
            }
            else
            {
                footer = alerts.Count == 0 ? $"No active NWS alerts · checked {alertCheckedText}" : $"Alerts checked {alertCheckedText}";
            }
        }

        // Alert strip: the most severe active alert first; shown whenever a cached alert is still in effect.
        string? alertHeadline = null, alertPrimary = null, alertSecondary = null;
        if (alerts.Count > 0)
        {
            alertHeadline = alerts.Count == 1 ? "1 active NWS alert" : $"{alerts.Count} active NWS alerts";
            alertPrimary = $"{alerts[0].Alert.Event} · {UntilText(alerts[0].Alert, nowUtc)}";
            alertSecondary = alerts.Count switch
            {
                1 => $"Applies to {alerts[0].Portion}",
                2 => $"Also: {alerts[1].Alert.Event} ({alerts[1].PortionShort})",
                _ => $"Also: {alerts[1].Alert.Event} and {alerts.Count - 2} more",
            };
        }

        var details = new List<DashboardWeatherDetailRow>();
        if (forecast.ShortForecast is { } shortForecast)
        {
            details.Add(new DashboardWeatherDetailRow(forecast.ShortForecastLabel, shortForecast, forecast.Stale));
        }

        if (observation.Source is { } station)
        {
            details.Add(new DashboardWeatherDetailRow($"Humidity ({station.StationId})", station.RelativeHumidityPercent is { } rh ? $"{rh}%" : "Not reported", observation.Stale));
            details.Add(new DashboardWeatherDetailRow($"Gusts ({station.StationId})", station.WindGustMph is { } gust ? $"{Math.Round(gust):0} mph" : "Not reported", observation.Stale));
            if (station.HeatIndexF is { } heat)
            {
                details.Add(new DashboardWeatherDetailRow($"Heat index ({station.StationId})", $"{Math.Round(heat):0}°F", observation.Stale));
            }

            if (station.WindChillF is { } chill)
            {
                details.Add(new DashboardWeatherDetailRow($"Wind chill ({station.StationId})", $"{Math.Round(chill):0}°F", observation.Stale));
            }
        }

        if (snapshot.RetrievalPermitted && (obsShown || forecastShown))
        {
            var alertRow = snapshot.AlertsStatus.Availability switch
            {
                WeatherAvailability.Fresh => $"{(alerts.Count == 0 ? "None" : alerts.Count.ToString(CultureInfo.InvariantCulture))} active · checked {alertCheckedText}",
                WeatherAvailability.Stale => $"Not checked since {alertCheckedText}",
                _ => "Status unknown",
            };
            details.Add(new DashboardWeatherDetailRow("Alerts", alertRow, !alertsFresh));
        }

        string lastUpdated;
        var lastUpdatedStale = false;
        if (latestFailure is { } failure && lastAttempt is { } failedAt && (headerStale || statusHeadline is not null))
        {
            lastUpdated = $"Last attempt {EasternTimestampFormat.Relative(failedAt, nowUtc)} failed ({FailureShort(failure)}). Retrying automatically.";
            lastUpdatedStale = headerStale;
        }
        else
        {
            lastUpdated = lastSuccess is { } success ? $"Last updated {EasternTimestampFormat.Relative(success, nowUtc)}" : "Not updated yet";
        }

        var observationNote = observation.Source is { } source
            ? $"Observations come from the {StationDisplayName(source)} station ({source.StationId}), not from inside Carowinds; conditions at the park can differ."
            : null;

        return new DashboardWeatherPresentation
        {
            HeaderMetaText = headerStale ? StaleHeaderText : ProviderName,
            HeaderIsStale = headerStale,
            StatusHeadline = statusHeadline,
            StatusDetail = statusDetail,
            StatusAlertText = statusAlert,
            StatusIsStale = statusIsStale,
            AlertHeadline = alertHeadline,
            AlertPrimaryText = alertPrimary,
            AlertSecondaryText = alertSecondary,
            ObservedKindText = observation.KindText,
            TemperatureText = observation.Temperature,
            ConditionText = observation.Condition,
            WindText = observation.Wind,
            ObservationSourceText = observation.SourceText,
            ObservationIsStale = observation.Stale,
            ForecastKindText = forecast.KindText,
            HasForecast = forecastShown,
            ForecastUnavailableDetail = statusHeadline is null ? forecast.UnavailableDetail : null, // the strip already says why
            HighText = forecast.High,
            LowText = forecast.Low,
            RainChanceText = forecast.RainChance,
            ForecastSourceText = forecast.SourceText,
            ForecastIsStale = forecast.Stale,
            FooterText = footer,
            FooterIsStale = footerStale,
            Alerts = alerts.Select(alert => alert.Item).ToArray(),
            DetailRows = details,
            ObservationNoteText = observationNote,
            LastUpdatedText = lastUpdated,
            LastUpdatedIsStale = lastUpdatedStale,
            ForecastPage = forecastPage,
            AutomationText = Automation(observation, forecast, alerts.Count, statusHeadline, alertsFresh),
        };
    }

    /// <summary>"Charlotte airport" for the known nearby stations, otherwise the NWS station name.</summary>
    public static string StationDisplayName(WeatherObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return StationShortNames.TryGetValue(observation.StationId, out var shortName)
            ? shortName
            : string.IsNullOrWhiteSpace(observation.StationName) ? observation.StationId : observation.StationName.Trim();
    }

    /// <summary>
    /// The window the rain chance covers and its label: today's park hours (from now on, once they have started);
    /// the rest of today when the park is closed, has no hours, or has closed for the day.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To, string Label) PrecipitationWindow(
        DashboardParkStatusSummary? parkStatus, DateOnly? operatingDate, DateOnly todayEastern, DateTimeOffset nowUtc)
    {
        var endOfToday = ToUtc(todayEastern.AddDays(1), TimeOnly.MinValue);
        var nowEastern = EasternTimeZone.ConvertFromUtc(nowUtc);

        if (parkStatus is { Status: not DashboardParkStatus.Closed } park
            && operatingDate == todayEastern
            && park.Hours.Effective.Opening is { } opening
            && park.Hours.Effective.Closing is { } closing)
        {
            var open = ToUtc(todayEastern, opening);
            var close = ToUtc(closing > opening ? todayEastern : todayEastern.AddDays(1), closing);
            if (nowUtc < open)
            {
                return (open, close, $"{CompactTime(opening)}–{CompactTime(closing)} park hours");
            }

            if (nowUtc < close)
            {
                var fromHour = new TimeOnly(nowEastern.Hour, 0);
                return (nowUtc, close, $"{CompactTime(fromHour)}–{CompactTime(closing)} (rest of park hours)");
            }

            return (nowUtc, endOfToday, "rest of today (after park hours)");
        }

        var reason = parkStatus?.Status == DashboardParkStatus.Closed && operatingDate == todayEastern ? "park closed" : "no park hours";
        return (nowUtc, endOfToday, $"rest of today ({reason})");
    }

    private sealed record ObservationParts(string KindText, string? Temperature, string? Condition, string? Wind, string? SourceText, bool Stale, WeatherObservation? Source);

    private sealed record ForecastParts(bool Shown, string KindText, string? High, string? Low, string? RainChance, string? SourceText, bool Stale, string? ShortForecast, string ShortForecastLabel, string? UnavailableDetail);

    private sealed record ResolvedAlert(WeatherAlert Alert, string Portion, string PortionShort, DashboardWeatherAlertItem Item);

    private static ObservationParts BuildObservation(WeatherSnapshot snapshot, DateTimeOffset nowUtc)
    {
        if (snapshot.Observation is not { TemperatureF: { } temperature } observation)
        {
            return new ObservationParts("OBSERVED", null, null, null, null, false, null);
        }

        var stale = snapshot.ObservationStatus.Availability == WeatherAvailability.Stale;
        var distance = observation.DistanceKm is { } km ? $", {Math.Round(km):0} km from the park" : string.Empty;
        var time = EasternTimestampFormat.Relative(observation.Timestamp, nowUtc);
        var age = stale ? $" ({AgeText(nowUtc - observation.Timestamp)} ago)" : string.Empty;

        return new ObservationParts(
            $"OBSERVED · {StationDisplayName(observation).ToUpperInvariant()}",
            $"{Math.Round(temperature):0}°F",
            string.IsNullOrWhiteSpace(observation.Description) ? null : observation.Description.Trim(),
            ObservedWindText(observation),
            $"{observation.StationId}{distance} · {time}{age}",
            stale,
            observation);
    }

    private static ForecastParts BuildForecast(WeatherSnapshot snapshot, DashboardParkStatusSummary? parkStatus, DateOnly? operatingDate, DateOnly todayEastern, DateTimeOffset nowUtc)
    {
        var dateText = todayEastern.ToString("MMM d", PresentationCulture.UnitedStates);
        if (snapshot.Forecast is not { } forecast)
        {
            return new ForecastParts(false, "NWS FORECAST", null, null, null, null, false, null, "Today", FailureSentence(snapshot.ForecastStatus.LastRefreshFailed ? snapshot.ForecastStatus.LastFailure : null));
        }

        var today = WeatherSnapshotBuilder.SummarizeDays(forecast).FirstOrDefault(day => day.Date == todayEastern);
        if (today is null || (today.HighF is null && today.LowF is null))
        {
            return new ForecastParts(false, "NWS FORECAST", null, null, null, null, false, null, "Today", "No NWS forecast for today in the current data.");
        }

        var stale = snapshot.ForecastStatus.Availability == WeatherAvailability.Stale;
        var periodName = today.HighF is not null ? "TODAY" : "TONIGHT";

        string? rain = null;
        var window = PrecipitationWindow(parkStatus, operatingDate, todayEastern, nowUtc);
        if (snapshot.Hourly is { } hourly && WeatherSnapshotBuilder.MaxPrecipitationChance(hourly, window.From, window.To) is { } chance)
        {
            rain = $"{chance}%";
        }

        var source = $"Highest hourly rain chance, {window.Label}";
        if (forecast.UpdateTime is { } issued)
        {
            source += $" · issued {EasternTimestampFormat.Relative(issued, nowUtc)}";
        }

        if (stale && snapshot.ForecastStatus.CheckedAt is { } checkedAt)
        {
            source += $" · last checked {EasternTimestampFormat.Relative(checkedAt, nowUtc)}";
        }

        return new ForecastParts(
            true,
            $"NWS FORECAST · {periodName}, {dateText.ToUpperInvariant()}",
            today.HighF is { } high ? $"{high}°" : null,
            today.LowF is { } low ? $"{low}°" : null,
            rain,
            source,
            stale,
            today.DaytimeForecast ?? today.NighttimeForecast,
            today.HighF is not null ? "Today" : "Tonight",
            null);
    }

    private static List<ResolvedAlert> BuildAlerts(WeatherSnapshot snapshot, DateTimeOffset nowUtc, WeatherLocation location)
    {
        var result = new List<ResolvedAlert>();
        foreach (var alert in snapshot.ActiveAlerts)
        {
            if (!WeatherAlertRules.IsActiveAt(alert, nowUtc))
            {
                continue; // Defence in depth: an expired alert is never shown as active.
            }

            var pointIds = snapshot.AlertCoverage.TryGetValue(alert.Id, out var ids) ? ids : [];
            var points = location.Points.Where(point => pointIds.Contains(point.Id, StringComparer.Ordinal)).ToArray();
            var states = points.Select(point => point.ExpectedState).Distinct(StringComparer.Ordinal).ToArray();
            var (portion, portionShort) = states.Length switch
            {
                0 => ("the park (portion not identified)", "portion not identified"),
                1 => ($"{states[0]} portion only", $"{states[0]} portion"),
                _ => ($"{string.Join(" and ", states)} portions", $"{string.Join(" and ", states)} portions"),
            };

            var sender = string.IsNullOrWhiteSpace(alert.SenderName) ? ProviderName : alert.SenderName.Trim();
            var issued = alert.Sent ?? alert.Effective;
            var sourceText = issued is { } at ? $"{sender}, {EasternTimestampFormat.Relative(at, nowUtc)}" : sender;
            var link = points.Length > 0 ? WeatherAttribution.ForecastPage(points[0]) : WeatherAttribution.ForecastPage(location.ForecastPoint);
            var severity = string.IsNullOrWhiteSpace(alert.Severity) ? "Unknown severity" : alert.Severity.Trim();
            var timing = UntilText(alert, nowUtc);

            result.Add(new ResolvedAlert(
                alert,
                portion,
                portionShort,
                new DashboardWeatherAlertItem(
                    alert.Event,
                    $"{severity} · {timing}",
                    $"Applies to {portion}",
                    sourceText,
                    link,
                    $"{alert.Event}, {severity}, {timing}, applies to {portion}, issued by {sender}")));
        }

        return result;
    }

    private static string UntilText(WeatherAlert alert, DateTimeOffset nowUtc) =>
        WeatherAlertRules.EndsAt(alert) is { } ends ? $"until {EasternTimestampFormat.Relative(ends, nowUtc)}" : "end time not given";

    private static string? ObservedWindText(WeatherObservation observation)
    {
        if (observation.WindSpeedMph is not { } speed)
        {
            return null;
        }

        var mph = Math.Round(speed);
        if (mph < 1)
        {
            return "Wind calm";
        }

        var direction = observation.WindDirectionDegrees is { } degrees ? Compass(degrees) + " " : string.Empty;
        var gust = observation.WindGustMph is { } g && Math.Round(g) > mph ? $", gusts {Math.Round(g):0}" : string.Empty;
        return $"Wind {direction}{mph:0} mph{gust}";
    }

    private static string Compass(int degrees)
    {
        string[] points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        var normalized = ((degrees % 360) + 360) % 360;
        return points[(int)Math.Round(normalized / 45.0, MidpointRounding.AwayFromZero) % 8];
    }

    private static string AgeText(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var hours = (int)age.TotalHours;
        return hours > 0 ? $"{hours} h {age.Minutes} min" : $"{(int)age.TotalMinutes} min";
    }

    private static WeatherFailureKind? LatestFailure(WeatherSnapshot snapshot) =>
        new[] { snapshot.ObservationStatus, snapshot.ForecastStatus, snapshot.AlertsStatus }
            .Where(status => status.LastRefreshFailed && status.LastAttemptAt is not null)
            .OrderByDescending(status => status.LastAttemptAt)
            .Select(status => (WeatherFailureKind?)status.LastFailure)
            .FirstOrDefault();

    private static string? FailureSentence(WeatherFailureKind? failure) => failure switch
    {
        null or WeatherFailureKind.None => null,
        WeatherFailureKind.Network or WeatherFailureKind.Timeout => "The National Weather Service could not be reached.",
        WeatherFailureKind.RateLimited => "The National Weather Service asked the app to wait before trying again.",
        _ => "The National Weather Service returned an error.",
    };

    private static string FailureShort(WeatherFailureKind failure) => failure switch
    {
        WeatherFailureKind.Network or WeatherFailureKind.Timeout => "NWS did not respond",
        WeatherFailureKind.RateLimited => "NWS asked to wait",
        _ => "NWS error",
    };

    private static string Automation(ObservationParts observation, ForecastParts forecast, int alertCount, string? statusHeadline, bool alertsFresh)
    {
        var parts = new List<string> { $"Weather from the {ProviderName}." };
        if (statusHeadline is not null)
        {
            parts.Add(statusHeadline);
        }

        if (observation.Temperature is not null)
        {
            parts.Add($"{observation.KindText.ToLowerInvariant().Replace(" · ", " at ", StringComparison.Ordinal)}: {observation.Temperature}{(observation.Condition is null ? string.Empty : ", " + observation.Condition)}, {observation.SourceText}.");
        }
        else
        {
            parts.Add(ObservationUnavailableText + ".");
        }

        if (forecast.Shown)
        {
            var values = new List<string>();
            values.Add(forecast.High is { } high ? $"high {high}" : HighNotInForecastText.ToLowerInvariant());
            if (forecast.Low is { } low)
            {
                values.Add($"low {low}");
            }

            values.Add(forecast.RainChance is { } rain ? $"rain chance {rain}" : RainChanceUnavailableText.ToLowerInvariant());
            parts.Add($"Forecast: {string.Join(", ", values)}.");
        }
        else
        {
            parts.Add(ForecastUnavailableText + ".");
        }

        parts.Add(alertCount > 0
            ? $"{alertCount} active NWS alert{(alertCount == 1 ? string.Empty : "s")}."
            : alertsFresh ? "No active NWS alerts." : "Alert status unknown.");
        return string.Join(" ", parts);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var zone = EasternTimeZone.Instance;
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // A wall time skipped by the spring-forward change.
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string CompactTime(TimeOnly time) =>
        time.ToString(time.Minute == 0 ? "h tt" : "h:mm tt", PresentationCulture.UnitedStates);
}
