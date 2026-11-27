using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Weather;

/// <summary>Why no operating-hours forecast could be selected (Phase 37F). Every reason means the PDF omits its Weather Outlook.</summary>
public enum OperatingHoursForecastOmission
{
    /// <summary>A forecast was selected.</summary>
    None,

    /// <summary>No weather has been read yet, or no hourly forecast is available (never retrieved, offline, too old to keep).</summary>
    NoForecast,

    /// <summary>The date has no effective park operating hours (closed, not checked yet, or unusable times). No other period is substituted.</summary>
    NoParkHours,

    /// <summary>The hourly forecast was last retrieved longer ago than the PDF allows (<see cref="OperatingHoursForecastSelector.MaxForecastAge"/>).</summary>
    ForecastTooOld,

    /// <summary>The operating hours have already ended; a forecast is never shown for a past period.</summary>
    HoursEnded,

    /// <summary>The hourly forecast does not reach the operating hours at all (beyond the NWS horizon).</summary>
    OutsideForecast,

    /// <summary>The hourly forecast covers only part of the remaining operating hours (horizon ends inside them, or a gap).</summary>
    IncompleteCoverage,
}

/// <summary>
/// Phase 37F: the NWS hourly forecast periods that cover one operating date's park hours, selected at one instant.
/// Immutable; built only from an already-cached <see cref="WeatherSnapshot"/>, so selecting never requests anything.
/// <see cref="CoverageStart"/> is the opening time, or the selection instant once the park has opened (past hours are
/// never forecast); <see cref="Hours"/> cover [<see cref="CoverageStart"/>, <see cref="ClosesAt"/>) without a gap.
/// </summary>
public sealed record OperatingHoursForecast(
    DateOnly OperatingDate,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    DateTimeOffset CoverageStart,
    IReadOnlyList<HourlyForecastPeriod> Hours,
    DateTimeOffset? IssuedAt,
    DateTimeOffset RetrievedAt)
{
    /// <summary>True when the park had already opened at selection time, so the forecast covers only the rest of the hours.</summary>
    public bool StartsAfterOpening => CoverageStart > OpensAt;
}

/// <summary>A selected forecast, or the reason there is none.</summary>
public sealed record OperatingHoursForecastSelection(OperatingHoursForecast? Forecast, OperatingHoursForecastOmission Omission)
{
    public static OperatingHoursForecastSelection Omitted(OperatingHoursForecastOmission omission) => new(null, omission);
}

/// <summary>
/// Phase 37F forecast-data selection for the PDF Weather Outlook: pure, deterministic, Eastern-time aware. Wording and
/// layout live in the Documents project; this only decides which hourly NWS periods describe the park's hours.
/// </summary>
public static class OperatingHoursForecastSelector
{
    /// <summary>
    /// The oldest hourly forecast retrieval a PDF may use: the same three hours after which the app already marks the
    /// forecast "may be out of date" (<see cref="WeatherRefreshPolicy.ForecastStaleAfter"/>). Older forecasts are omitted.
    /// </summary>
    public static TimeSpan MaxForecastAge { get; } = new WeatherRefreshPolicy().ForecastStaleAfter;

    /// <summary>
    /// Selects the hourly periods for <paramref name="operatingDate"/>'s effective park hours as of <paramref name="now"/>.
    /// Closing at or before opening means the hours run past midnight into the next day. Missing hours, a stale or
    /// missing forecast, ended hours and incomplete coverage all omit the forecast; nothing is substituted.
    /// </summary>
    public static OperatingHoursForecastSelection Select(WeatherSnapshot? snapshot, DateOnly operatingDate, EffectiveHours hours, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(hours);

        if (hours.Status != EffectiveHoursStatus.Open || hours.Opening is not { } opening || hours.Closing is not { } closing || opening == closing)
        {
            return OperatingHoursForecastSelection.Omitted(OperatingHoursForecastOmission.NoParkHours);
        }

        if (snapshot?.Hourly is not { } hourly || snapshot.HourlyStatus.CheckedAt is not { } retrievedAt)
        {
            return OperatingHoursForecastSelection.Omitted(OperatingHoursForecastOmission.NoForecast);
        }

        if (now - retrievedAt > MaxForecastAge)
        {
            return OperatingHoursForecastSelection.Omitted(OperatingHoursForecastOmission.ForecastTooOld);
        }

        var opensAt = ToUtc(operatingDate, opening);
        var closesAt = ToUtc(closing > opening ? operatingDate : operatingDate.AddDays(1), closing);
        if (now >= closesAt)
        {
            return OperatingHoursForecastSelection.Omitted(OperatingHoursForecastOmission.HoursEnded);
        }

        var from = now > opensAt ? now : opensAt;
        var covering = hourly.Periods
            .Where(period => period.Start < closesAt && period.End > from && period.End > period.Start)
            .OrderBy(period => period.Start)
            .ToArray();

        if (covering.Length == 0)
        {
            return OperatingHoursForecastSelection.Omitted(OperatingHoursForecastOmission.OutsideForecast);
        }

        if (covering[0].Start > from || covering[^1].End < closesAt || HasGap(covering))
        {
            return OperatingHoursForecastSelection.Omitted(OperatingHoursForecastOmission.IncompleteCoverage);
        }

        return new OperatingHoursForecastSelection(
            new OperatingHoursForecast(operatingDate, opensAt, closesAt, from, covering, hourly.UpdateTime, retrievedAt),
            OperatingHoursForecastOmission.None);
    }

    private static bool HasGap(IReadOnlyList<HourlyForecastPeriod> periods)
    {
        for (var i = 1; i < periods.Count; i++)
        {
            if (periods[i].Start > periods[i - 1].End)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An Eastern wall-clock time as an instant. A time skipped by the spring-forward change moves an hour later; a repeated fall-back time uses standard time.</summary>
    internal static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var zone = EasternTimeZone.Instance;
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
