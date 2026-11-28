using System.Globalization;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>What the Calendar's selected-date Weather group can show (Phase 37D).</summary>
public enum CalendarWeatherState
{
    /// <summary>The weather cache has not been read yet, or nothing was ever retrieved.</summary>
    Loading,

    /// <summary>Forecast values for the selected date are shown.</summary>
    Forecast,

    /// <summary>The selected date is before today: NWS forecasts are not a historical record.</summary>
    PastDate,

    /// <summary>The cached forecast does not reach the selected date.</summary>
    BeyondHorizon,

    /// <summary>No usable forecast (never retrieved successfully, too old to keep, or retrieval not set up).</summary>
    Unavailable,
}

/// <summary>A label/value row in the Weather group; <see cref="IsStale"/> marks a value from an out-of-date forecast.</summary>
public sealed record CalendarWeatherRow(string Label, string Value, bool IsStale)
{
    public string AccessibleText => $"{Label}: {Value}{(IsStale ? ", may be out of date" : string.Empty)}";
}

/// <summary>One forecast hour in the Weather group's hourly disclosure. Missing values are words, never a number.</summary>
public sealed record CalendarWeatherHourRow(
    string TimeText,
    PdfWeatherIcon Icon,
    string TemperatureText,
    bool HasTemperature,
    string ConditionText,
    string RainText,
    bool HasRain,
    string? WindText,
    string AccessibleText)
{
    public bool HasIcon => Icon != PdfWeatherIcon.None;

    public bool HasWind => WindText is not null;
}

/// <summary>
/// Phase 37D: the Calendar's compact, date-specific Weather group for the selected date — a summary of the cached NWS
/// forecast, never a second Dashboard card. Pure: built from the monitor's immutable <see cref="WeatherSnapshot"/> (local
/// cache only, so selecting a date never requests anything), the selected date's already-loaded operating state and
/// effective hours, and the current instant. The operating window and its Eastern/DST/past-midnight rules are the Phase
/// 37F ones (<see cref="OperatingHoursForecastSelector.OperatingWindow"/>), and the wording of temperature, condition and
/// rain is the PDF's (<see cref="PdfWeatherOutlookBuilder"/>), so every surface describes the same hours the same way.
/// Freshness is the snapshot's existing policy (stale after 3 h, unavailable after 24 h); nothing is invented,
/// interpolated or carried over from another date, and a forecast is never presented as a measured condition.
/// </summary>
public sealed record CalendarWeatherPresentation
{
    public const string ProviderName = WeatherAttribution.ProviderName;
    public const string LoadingText = "Getting the forecast from the National Weather Service…";
    public const string PastDateText = "Past date. NWS forecasts cover today and upcoming days only, so no weather is shown for this date.";
    public const string StaleText = "May be out of date";
    public const string AlertsElsewhereText = "Current alert details are on the Dashboard.";

    /// <summary>At most this many alerts are named; the Dashboard keeps the full alert details.</summary>
    private const int MaxAlertLines = 2;

    public CalendarWeatherState State { get; init; }

    public bool HasForecast => State == CalendarWeatherState.Forecast;

    /// <summary>The one-line message for every state without values (loading, past, beyond horizon, unavailable).</summary>
    public string? MessageText { get; init; }

    public bool HasMessage => MessageText is not null;

    /// <summary>The icon for the summary condition (the PDF's vector set; <see cref="PdfWeatherIcon.None"/> when unclassified).</summary>
    public PdfWeatherIcon SummaryIcon { get; init; }

    public bool HasSummaryIcon => SummaryIcon != PdfWeatherIcon.None;

    /// <summary>The condition headline ("Partly cloudy"); null when NWS gave none for the period.</summary>
    public string? SummaryText { get; init; }

    public bool HasSummary => SummaryText is not null;

    /// <summary>Which period the values describe ("Park hours, 10 AM–10 PM"; "Park closed · general forecast for the day").</summary>
    public string? ScopeText { get; init; }

    public bool HasScope => ScopeText is not null;

    public IReadOnlyList<CalendarWeatherRow> Rows { get; init; } = [];

    /// <summary>Partial hourly coverage, or day-forecast-only values, said in words.</summary>
    public string? CoverageNoteText { get; init; }

    public bool HasCoverageNote => CoverageNoteText is not null;

    /// <summary>"National Weather Service forecast · issued 6:13 AM · updated 9:22 AM" (or "May be out of date …").</summary>
    public string? SourceText { get; init; }

    public bool HasSource => SourceText is not null;

    public bool IsStale { get; init; }

    /// <summary>Official NWS alerts in effect on the selected date (only from a current alert check).</summary>
    public IReadOnlyList<string> AlertLines { get; init; } = [];

    public bool HasAlerts => AlertLines.Count > 0;

    public string AlertsNoteText => AlertsElsewhereText;

    public IReadOnlyList<CalendarWeatherHourRow> Hours { get; init; } = [];

    public bool HasHours => Hours.Count > 0;

    public string HoursHeaderText { get; init; } = "Hourly forecast";

    /// <summary>The summary a screen reader announces for the group.</summary>
    public string AutomationText { get; init; } = "Weather";

    /// <summary>Builds the group for <paramref name="selected"/> (the loaded summary of the selected date).</summary>
    public static CalendarWeatherPresentation Build(WeatherSnapshot? snapshot, OperationsCalendarSelectedDateSummary selected, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var date = selected.Date;
        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(nowUtc).DateTime);

        if (date < today)
        {
            return Message(CalendarWeatherState.PastDate, PastDateText);
        }

        if (snapshot is null
            || (snapshot.RetrievalPermitted
                && snapshot.ForecastStatus.Availability == WeatherAvailability.NeverRetrieved
                && snapshot.HourlyStatus.Availability == WeatherAvailability.NeverRetrieved))
        {
            return Message(CalendarWeatherState.Loading, LoadingText);
        }

        if (!snapshot.RetrievalPermitted)
        {
            return Message(CalendarWeatherState.Unavailable, "Weather unavailable. Weather retrieval is not set up for the park's location.");
        }

        if (snapshot.Forecast is null && snapshot.Hourly is null)
        {
            var failure = LatestFailure(snapshot);
            return Message(CalendarWeatherState.Unavailable, $"Weather unavailable. {FailureSentence(failure)}");
        }

        var dayStart = OperatingHoursForecastSelector.StartOfEasternDay(date);
        var dayEnd = OperatingHoursForecastSelector.StartOfEasternDay(date.AddDays(1));
        var day = snapshot.Forecast is { } forecast
            ? WeatherSnapshotBuilder.SummarizeDays(forecast).FirstOrDefault(summary => summary.Date == date)
            : null;
        var forecastStale = snapshot.Forecast is not null && snapshot.ForecastStatus.Availability == WeatherAvailability.Stale;
        var hourlyStale = snapshot.Hourly is not null && snapshot.HourlyStatus.Availability == WeatherAvailability.Stale;

        // The window the values describe: the park's effective hours (from now on once they have started); the rest of
        // the day when closed, hours unknown, or the hours have ended. Never a window the date does not have.
        var window = Window(selected, date, dayStart, dayEnd, nowUtc);
        var hourly = snapshot.Hourly?.Periods
            .Where(period => period.End > period.Start && period.Start < window.To && period.End > window.From)
            .OrderBy(period => period.Start)
            .ToArray() ?? [];

        var dayHasHourly = snapshot.Hourly?.Periods.Any(period => period.Start < dayEnd && period.End > dayStart) == true;
        if (day is null && !dayHasHourly)
        {
            var last = LastForecastDate(snapshot);
            var through = last is { } lastDate && lastDate >= today
                ? $" The current forecast runs through {lastDate.ToString("dddd, MMM d", PresentationCulture.UnitedStates)}."
                : string.Empty;
            return Message(CalendarWeatherState.BeyondHorizon, $"No NWS forecast this far ahead.{through}");
        }

        var rows = new List<CalendarWeatherRow>();
        string? summary = null;
        var icon = PdfWeatherIcon.None;
        string? coverage = null;
        string? rainNote = null;
        var stale = false;

        if (hourly.Length > 0)
        {
            // The PDF wording over the hours that are actually covered (never stretched over a gap or past the horizon).
            var covered = Contiguous(hourly);
            var coverageStart = covered[0].Start > window.From ? covered[0].Start : window.From;
            var coverageEnd = covered[^1].End < window.To ? covered[^1].End : window.To;
            var outlook = PdfWeatherOutlookBuilder.Build(
                new OperatingHoursForecast(date, window.From, coverageEnd, coverageStart, covered, snapshot.Hourly!.UpdateTime, snapshot.HourlyStatus.CheckedAt ?? nowUtc),
                nowUtc);
            stale |= hourlyStale;
            if (outlook is not null)
            {
                summary = outlook.ConditionText;
                icon = outlook.Icon;
                if (outlook.TemperatureText is { } temperature)
                {
                    rows.Add(new CalendarWeatherRow("Temperature", temperature, hourlyStale));
                }

                var (rain, rainPartial) = outlook.RainText is { } rainText ? (RainValue(rainText), false) : RainFallback(covered);
                rows.Add(new CalendarWeatherRow("Rain", rain, hourlyStale));
                if (rainPartial)
                {
                    rainNote = "NWS gave no rain chance for some of these hours.";
                }
            }

            if (coverageStart > window.From || coverageEnd < window.To)
            {
                coverage = $"Hourly forecast covers {Clock(coverageStart)}–{Clock(coverageEnd)} only{(window.IsParkHours ? $" of the park hours (closing {Clock(window.To)})" : " of this period")}.";
            }
        }
        else if (window.IsParkHours)
        {
            coverage = "No hourly forecast for the park hours yet; the values are the NWS day forecast.";
        }
        else
        {
            coverage = "No hourly forecast for this date yet; the values are the NWS day forecast.";
        }

        if (day is not null)
        {
            stale |= forecastStale;
            if (summary is null && (day.DaytimeForecast ?? day.NighttimeForecast) is { } dayText)
            {
                (summary, icon) = PdfWeatherOutlookBuilder.DescribeCondition(dayText, day.DaytimeForecast is not null);
            }

            var highLow = (day.HighF, day.LowF) switch
            {
                ({ } high, { } low) => $"{Degrees(high)}° / {Degrees(low)}°",
                ({ } high, null) => $"High {Degrees(high)}°",
                (null, { } low) => $"Low {Degrees(low)}° (high not in forecast)",
                _ => null,
            };
            if (highLow is not null)
            {
                rows.Add(new CalendarWeatherRow(day.HighF is not null && day.LowF is not null ? "High / low" : "Day", highLow, forecastStale));
            }

            if (hourly.Length == 0 && day.MaxPrecipitationChancePercent is { } dayRain)
            {
                rows.Add(new CalendarWeatherRow("Rain", $"Up to {dayRain}%", forecastStale));
            }
        }

        if (rainNote is not null)
        {
            coverage = coverage is null ? rainNote : $"{coverage} {rainNote}";
        }

        var hours = hourly
            .Select(HourRow)
            .ToArray();

        var issued = snapshot.Hourly?.UpdateTime ?? snapshot.Forecast?.UpdateTime;
        var checkedAt = new[] { snapshot.HourlyStatus.CheckedAt, snapshot.ForecastStatus.CheckedAt }.Max();
        var source = $"{ProviderName} forecast";
        if (issued is { } issuedAt)
        {
            source += $" · issued {EasternTimestampFormat.Relative(issuedAt, nowUtc)}";
        }

        if (checkedAt is { } at)
        {
            source += stale ? $" · {StaleText.ToLowerInvariant()}, last updated {EasternTimestampFormat.Relative(at, nowUtc)}" : $" · updated {EasternTimestampFormat.Relative(at, nowUtc)}";
        }

        var alerts = BuildAlertLines(snapshot, date, today, dayStart, dayEnd, nowUtc);

        var automation = new List<string> { $"Weather for {date.ToString("dddd, MMMM d", PresentationCulture.UnitedStates)}, {ProviderName} forecast." };
        if (stale)
        {
            automation.Add(StaleText + ".");
        }

        automation.Add(window.Scope + ".");
        if (summary is not null)
        {
            automation.Add(summary + ".");
        }

        automation.AddRange(rows.Select(row => row.AccessibleText + "."));
        if (coverage is not null)
        {
            automation.Add(coverage);
        }

        automation.AddRange(alerts.Select(line => line + "."));

        return new CalendarWeatherPresentation
        {
            State = CalendarWeatherState.Forecast,
            SummaryIcon = summary is null ? PdfWeatherIcon.None : icon,
            SummaryText = summary,
            ScopeText = window.Scope,
            Rows = rows,
            CoverageNoteText = coverage,
            SourceText = source,
            IsStale = stale,
            AlertLines = alerts,
            Hours = hours,
            HoursHeaderText = hours.Length == 1 ? "Hourly forecast (1 hour)" : $"Hourly forecast ({hours.Length} hours)",
            AutomationText = string.Join(" ", automation),
        };
    }

    /// <summary>Value equality including the lists, so an unchanged monitor report never re-renders the group.</summary>
    public bool Equals(CalendarWeatherPresentation? other) =>
        other is not null
        && State == other.State
        && MessageText == other.MessageText
        && SummaryIcon == other.SummaryIcon
        && SummaryText == other.SummaryText
        && ScopeText == other.ScopeText
        && CoverageNoteText == other.CoverageNoteText
        && SourceText == other.SourceText
        && IsStale == other.IsStale
        && HoursHeaderText == other.HoursHeaderText
        && AutomationText == other.AutomationText
        && Rows.SequenceEqual(other.Rows)
        && AlertLines.SequenceEqual(other.AlertLines)
        && Hours.SequenceEqual(other.Hours);

    public override int GetHashCode() => HashCode.Combine(State, SummaryText, ScopeText, SourceText, AutomationText, Hours.Count);

    private sealed record ForecastWindow(DateTimeOffset From, DateTimeOffset To, bool IsParkHours, string Scope);

    private static ForecastWindow Window(OperationsCalendarSelectedDateSummary selected, DateOnly date, DateTimeOffset dayStart, DateTimeOffset dayEnd, DateTimeOffset nowUtc)
    {
        var from = nowUtc > dayStart ? nowUtc : dayStart;
        if (selected.OperatingState == OperationsCalendarOperatingState.Closed)
        {
            return new(from, dayEnd, false, nowUtc > dayStart ? "Park closed · general forecast for the rest of the day" : "Park closed · general forecast for the day");
        }

        if (OperatingHoursForecastSelector.OperatingWindow(date, selected.ParkHours.Effective) is not { } hours)
        {
            return new(from, dayEnd, false, nowUtc > dayStart ? "Park hours not confirmed · general forecast for the rest of the day" : "Park hours not confirmed · general forecast for the day");
        }

        var (opensAt, closesAt) = hours;
        if (nowUtc >= closesAt)
        {
            return new(nowUtc, dayEnd > nowUtc ? dayEnd : nowUtc, false, "Park hours have ended · forecast for the rest of the day");
        }

        var opening = TimeOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(opensAt).DateTime);
        var closing = TimeOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(closesAt).DateTime);
        var overnight = closesAt > dayEnd ? " (closes after midnight)" : string.Empty;
        if (nowUtc > opensAt)
        {
            var hourStart = EasternTimeZone.ConvertFromUtc(nowUtc);
            return new(nowUtc, closesAt, true, $"Rest of park hours, {ClockText(new TimeOnly(hourStart.Hour, 0))}–{ClockText(closing)}{overnight}");
        }

        return new(opensAt, closesAt, true, $"Park hours, {ClockText(opening)}–{ClockText(closing)}{overnight}");
    }

    /// <summary>The hours from the first covered one up to the first gap (values past a gap are never merged in).</summary>
    private static HourlyForecastPeriod[] Contiguous(HourlyForecastPeriod[] periods)
    {
        var result = new List<HourlyForecastPeriod> { periods[0] };
        for (var i = 1; i < periods.Length; i++)
        {
            if (periods[i].Start > result[^1].End)
            {
                break;
            }

            result.Add(periods[i]);
        }

        return result.ToArray();
    }

    /// <summary>The highest probability the forecast gives, and whether some hours gave none (said in a note, never hidden).</summary>
    private static (string Value, bool Partial) RainFallback(IReadOnlyList<HourlyForecastPeriod> hours)
    {
        var chances = hours.Where(hour => hour.PrecipitationChancePercent is not null).Select(hour => hour.PrecipitationChancePercent!.Value).ToArray();
        return chances.Length == 0 ? ("Not given in the forecast", false) : ($"Up to {chances.Max()}%", true);
    }

    /// <summary>The PDF's rain wording without its own "Rain chance" prefix, since the row is already labelled "Rain".</summary>
    private static string RainValue(string pdfText)
    {
        const string Prefix = "Rain chance ";
        if (!pdfText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return pdfText; // "Showers possible after 6 PM (up to 40%)"
        }

        var rest = pdfText[Prefix.Length..];
        return char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    private static CalendarWeatherHourRow HourRow(HourlyForecastPeriod period)
    {
        var time = ClockText(TimeOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(period.Start).DateTime));
        var (condition, icon) = PdfWeatherOutlookBuilder.DescribeCondition(period.ShortForecast, period.IsDaytime);
        var temperature = period.TemperatureF is { } t ? $"{Degrees(t)}°" : "—";
        var rain = period.PrecipitationChancePercent is { } chance ? $"{chance}%" : "—";
        var wind = WindText(period);
        var accessible = string.Join(", ", new[]
        {
            time,
            period.TemperatureF is { } temp ? $"{Degrees(temp)} degrees" : "temperature not given",
            condition ?? "condition not given",
            period.PrecipitationChancePercent is { } c ? $"rain chance {c} percent" : "rain chance not given",
            wind is null ? null : $"wind {wind}",
        }.Where(part => part is not null));
        return new CalendarWeatherHourRow(time, icon, temperature, period.TemperatureF is not null, condition ?? "Not given", rain, period.PrecipitationChancePercent is not null, wind, accessible);
    }

    private static string? WindText(HourlyForecastPeriod period)
    {
        if (string.IsNullOrWhiteSpace(period.WindSpeedText))
        {
            return null;
        }

        var speed = period.WindSpeedText.Trim();
        return string.IsNullOrWhiteSpace(period.WindDirection) ? speed : $"{period.WindDirection.Trim()} {speed}";
    }

    private static IReadOnlyList<string> BuildAlertLines(WeatherSnapshot snapshot, DateOnly date, DateOnly today, DateTimeOffset dayStart, DateTimeOffset dayEnd, DateTimeOffset nowUtc)
    {
        // Only from a current check: a stale or failed alert feed is never read as "no alerts", and nothing is shown.
        if (snapshot.AlertsStatus.Availability != WeatherAvailability.Fresh || date < today)
        {
            return [];
        }

        var applicable = snapshot.ActiveAlerts
            .Where(alert => WeatherAlertRules.IsActiveAt(alert, nowUtc))
            .Where(alert =>
            {
                var begins = alert.Onset ?? alert.Effective ?? alert.Sent ?? nowUtc;
                var ends = WeatherAlertRules.EndsAt(alert);
                // WeatherAlertRules never treats an alert without an end time as active; should one arrive, it is in
                // effect now only and is never projected onto a later date.
                return ends is { } end ? begins < dayEnd && end > dayStart : date == today;
            })
            .ToArray();

        var lines = applicable
            .Take(MaxAlertLines)
            .Select(alert => WeatherAlertRules.EndsAt(alert) is { } end
                ? $"NWS alert: {alert.Event} · until {EasternTimestampFormat.Relative(end, nowUtc)}"
                : $"NWS alert: {alert.Event} · end time not given")
            .ToList();
        if (applicable.Length > MaxAlertLines)
        {
            lines.Add($"and {applicable.Length - MaxAlertLines} more NWS alert{(applicable.Length - MaxAlertLines == 1 ? string.Empty : "s")}");
        }

        return lines;
    }

    private static DateOnly? LastForecastDate(WeatherSnapshot snapshot)
    {
        var ends = new[]
        {
            snapshot.Forecast?.Periods.Select(period => (DateTimeOffset?)period.Start).Max(),
            snapshot.Hourly?.Periods.Select(period => (DateTimeOffset?)period.Start).Max(),
        }.Max();
        return ends is { } last ? DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(last).DateTime) : null;
    }

    private static CalendarWeatherPresentation Message(CalendarWeatherState state, string text) => new()
    {
        State = state,
        MessageText = text,
        AutomationText = $"Weather. {text}",
    };

    private static WeatherFailureKind? LatestFailure(WeatherSnapshot snapshot) =>
        new[] { snapshot.ForecastStatus, snapshot.HourlyStatus }
            .Where(status => status.LastRefreshFailed && status.LastAttemptAt is not null)
            .OrderByDescending(status => status.LastAttemptAt)
            .Select(status => (WeatherFailureKind?)status.LastFailure)
            .FirstOrDefault();

    private static string FailureSentence(WeatherFailureKind? failure) => failure switch
    {
        WeatherFailureKind.Network or WeatherFailureKind.Timeout => "The National Weather Service could not be reached.",
        WeatherFailureKind.RateLimited => "The National Weather Service asked the app to wait before trying again.",
        null or WeatherFailureKind.None => "No current National Weather Service forecast is available.",
        _ => "The National Weather Service returned an error.",
    };

    private static string Degrees(int value) =>
        value < 0 ? "−" + (-value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private static string Clock(DateTimeOffset instant) => ClockText(TimeOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(instant).DateTime));

    private static string ClockText(TimeOnly time) =>
        time.ToString(time.Minute == 0 ? "h tt" : "h:mm tt", PresentationCulture.UnitedStates);
}
