using System.Globalization;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>Phase 37F: the vector icon a Weather Outlook prints. <see cref="None"/> prints no icon (the condition could not be classified).</summary>
public enum PdfWeatherIcon
{
    None,
    Sun,
    Moon,
    PartlyCloudy,
    PartlyCloudyNight,
    Cloudy,
    Rain,
    Thunderstorm,
    Snow,
    Fog,
}

/// <summary>
/// Phase 37F: the print-ready wording of one Weather Outlook — a forecast summary for the selected operating date's
/// park hours, frozen when the document is generated. Every member is display text already decided by
/// <see cref="PdfWeatherOutlookBuilder"/>; templates only choose which members fit. Value equality (strings and an
/// enum only), so two outlooks with the same wording compare equal. Never carries an alert.
/// </summary>
/// <param name="OperatingDate">The Passdown date the forecast was selected for; a document for any other date ignores it.</param>
/// <param name="TemperatureText">"63–74°F" or "72°F"; null when any covered hour has no temperature.</param>
/// <param name="ConditionText">The dominant NWS condition in sentence case ("Partly cloudy").</param>
/// <param name="ShortConditionText">A short form of the condition for narrow layouts; null when none is safe.</param>
/// <param name="RainText">"Rain chance up to 40%" or "Showers possible after 6 PM (up to 40%)"; null when any covered hour has no probability.</param>
/// <param name="ShortRainText">"Rain up to 40%"; null with <paramref name="RainText"/>.</param>
/// <param name="AttributionText">"NWS forecast 10:05 AM" (the NWS issue time), plus the covered period when the park had already opened.</param>
/// <param name="ShortAttributionText">"NWS 10:05 AM", with the same period qualifier.</param>
public sealed record PdfWeatherOutlook(
    DateOnly OperatingDate,
    PdfWeatherIcon Icon,
    string? TemperatureText,
    string? ConditionText,
    string? ShortConditionText,
    string? RainText,
    string? ShortRainText,
    string AttributionText,
    string ShortAttributionText);

/// <summary>
/// Phase 37F summary wording: turns a selected <see cref="OperatingHoursForecast"/> into <see cref="PdfWeatherOutlook"/>.
/// Pure and deterministic. Rules:
/// <list type="bullet">
/// <item>Temperature: the lowest and highest hourly forecast temperature over the covered hours, only when every hour has one.</item>
/// <item>Condition: the NWS short forecast covering the most time (ties → the earliest).</item>
/// <item>Rain: the highest hourly probability of precipitation, labelled "up to", only when every hour has one. A time is
/// named only when the first hours are all below <see cref="RainOnsetPercent"/> and a later hour reaches it — "possible
/// after", never "will rain at".</item>
/// <item>Source: the NWS issue time of the hourly forecast (its retrieval time, labelled as such, when NWS gave none).</item>
/// </list>
/// Nothing is invented: a value the forecast does not give is left out, and no outlook is produced when neither a
/// temperature nor a condition is known.
/// </summary>
public static class PdfWeatherOutlookBuilder
{
    /// <summary>The hourly probability from which a later rise is described with a time (NWS "chance" wording starts at 30%).</summary>
    public const int RainOnsetPercent = 30;

    /// <summary>Conditions longer than this use their category name in condensed layouts.</summary>
    public const int ShortConditionMaxLength = 14;

    public static PdfWeatherOutlook? Build(OperatingHoursForecast? forecast, DateTimeOffset generatedAt)
    {
        if (forecast is null || forecast.Hours.Count == 0)
        {
            return null;
        }

        var hours = forecast.Hours;
        var temperature = TemperatureText(hours);
        var (condition, daytime) = DominantCondition(forecast);
        var category = condition is null ? null : Classify(condition, daytime);

        if (temperature is null && condition is null)
        {
            return null;
        }

        var shortCondition = condition is null ? null
            : condition.Length <= ShortConditionMaxLength ? condition
            : category?.ShortLabel;

        var (rain, shortRain) = RainTexts(forecast);
        var (attribution, shortAttribution) = AttributionTexts(forecast, generatedAt);

        return new PdfWeatherOutlook(
            forecast.OperatingDate,
            category?.Icon ?? PdfWeatherIcon.None,
            temperature,
            condition,
            shortCondition,
            rain,
            shortRain,
            attribution,
            shortAttribution);
    }

    private static string? TemperatureText(IReadOnlyList<HourlyForecastPeriod> hours)
    {
        if (hours.Any(hour => hour.TemperatureF is null))
        {
            return null;
        }

        var low = hours.Min(hour => hour.TemperatureF!.Value);
        var high = hours.Max(hour => hour.TemperatureF!.Value);
        if (low == high)
        {
            return $"{Degrees(low)}°F";
        }

        // A range with a negative end reads more clearly with "to" than with a dash beside a minus sign.
        return low < 0 ? $"{Degrees(low)}° to {Degrees(high)}°F" : $"{Degrees(low)}–{Degrees(high)}°F";
    }

    private static string Degrees(int value) =>
        value < 0 ? "−" + (-value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private static (string? Condition, bool Daytime) DominantCondition(OperatingHoursForecast forecast)
    {
        var groups = forecast.Hours
            .Select((hour, index) => (Hour: hour, Index: index, Text: Normalize(hour.ShortForecast)))
            .Where(item => item.Text is not null)
            .GroupBy(item => item.Text!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                group.First().Text,
                Weight = group.Sum(item => Overlap(item.Hour, forecast.CoverageStart, forecast.ClosesAt).TotalMinutes),
                FirstIndex = group.Min(item => item.Index),
                DaytimeWeight = group.Where(item => item.Hour.IsDaytime).Sum(item => Overlap(item.Hour, forecast.CoverageStart, forecast.ClosesAt).TotalMinutes),
            })
            .OrderByDescending(group => group.Weight)
            .ThenBy(group => group.FirstIndex)
            .FirstOrDefault();

        return groups is null ? (null, true) : (SentenceCase(groups.Text!), groups.DaytimeWeight * 2 >= groups.Weight);
    }

    private static TimeSpan Overlap(HourlyForecastPeriod hour, DateTimeOffset from, DateTimeOffset to)
    {
        var start = hour.Start > from ? hour.Start : from;
        var end = hour.End < to ? hour.End : to;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"Chance Showers And Thunderstorms" → "Chance showers and thunderstorms"; all-capital words (abbreviations) are kept.</summary>
    internal static string SentenceCase(string text)
    {
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var keep = word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));
            if (keep)
            {
                continue;
            }

            words[i] = i == 0
                ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
                : word.ToLowerInvariant();
        }

        return string.Join(' ', words);
    }

    private sealed record Category(PdfWeatherIcon Icon, string? ShortLabel);

    private static Category Classify(string condition, bool daytime)
    {
        var text = condition.ToLowerInvariant();
        if (text.Contains("thunder", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.Thunderstorm, "Storms");
        }

        if (text.Contains("sleet", StringComparison.Ordinal) || text.Contains("freezing", StringComparison.Ordinal) || text.Split(' ').Contains("ice"))
        {
            return new(PdfWeatherIcon.Snow, "Wintry mix");
        }

        if (text.Contains("snow", StringComparison.Ordinal) || text.Contains("flurr", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.Snow, "Snow");
        }

        if (text.Contains("shower", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.Rain, "Showers");
        }

        if (text.Contains("rain", StringComparison.Ordinal) || text.Contains("drizzle", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.Rain, "Rain");
        }

        foreach (var obscuration in new[] { "fog", "haze", "smoke", "mist" })
        {
            if (text.Contains(obscuration, StringComparison.Ordinal))
            {
                return new(PdfWeatherIcon.Fog, char.ToUpperInvariant(obscuration[0]) + obscuration[1..]);
            }
        }

        if (text.Contains("partly", StringComparison.Ordinal) || text.Contains("mostly sunny", StringComparison.Ordinal) || text.Contains("mostly clear", StringComparison.Ordinal))
        {
            return new(daytime ? PdfWeatherIcon.PartlyCloudy : PdfWeatherIcon.PartlyCloudyNight, "Partly cloudy");
        }

        if (text.Contains("cloudy", StringComparison.Ordinal) || text.Contains("overcast", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.Cloudy, "Cloudy");
        }

        if (text.Contains("sunny", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.Sun, "Sunny");
        }

        if (text.Contains("clear", StringComparison.Ordinal) || text.Contains("fair", StringComparison.Ordinal))
        {
            return new(daytime ? PdfWeatherIcon.Sun : PdfWeatherIcon.Moon, "Clear");
        }

        if (text.Contains("wind", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.None, "Windy");
        }

        if (text.Contains("breez", StringComparison.Ordinal))
        {
            return new(PdfWeatherIcon.None, "Breezy");
        }

        return new(PdfWeatherIcon.None, null);
    }

    private static (string? Full, string? Short) RainTexts(OperatingHoursForecast forecast)
    {
        var hours = forecast.Hours;
        if (hours.Any(hour => hour.PrecipitationChancePercent is null))
        {
            return (null, null);
        }

        var chances = hours.Select(hour => hour.PrecipitationChancePercent!.Value).ToArray();
        var max = chances.Max();
        var maxText = max.ToString(CultureInfo.InvariantCulture) + "%";

        if (chances.All(chance => chance == max))
        {
            // One value for every covered hour: no "up to" is needed.
            return ($"Rain chance {maxText}", $"Rain {maxText}");
        }

        var onset = Array.FindIndex(chances, chance => chance >= RainOnsetPercent);
        if (onset > 0 && hours[onset].Start > forecast.CoverageStart)
        {
            var word = PrecipitationWord(hours[onset].ShortForecast);
            var at = EasternTimeZone.ConvertFromUtc(hours[onset].Start);
            return ($"{word} possible after {ClockText(TimeOnly.FromDateTime(at.DateTime))} (up to {maxText})", $"Rain up to {maxText}");
        }

        return ($"Rain chance up to {maxText}", $"Rain up to {maxText}");
    }

    private static string PrecipitationWord(string? shortForecast)
    {
        var text = shortForecast?.ToLowerInvariant() ?? string.Empty;
        return text.Contains("thunder", StringComparison.Ordinal) ? "Storms"
            : text.Contains("shower", StringComparison.Ordinal) ? "Showers"
            : text.Contains("snow", StringComparison.Ordinal) ? "Snow"
            : "Rain";
    }

    private static (string Full, string Short) AttributionTexts(OperatingHoursForecast forecast, DateTimeOffset generatedAt)
    {
        string full;
        string brief;
        if (forecast.IssuedAt is { } issued)
        {
            var issuedEastern = EasternTimeZone.ConvertFromUtc(issued);
            var sameDay = issuedEastern.Date == EasternTimeZone.ConvertFromUtc(generatedAt).Date;
            var when = (sameDay ? string.Empty : issuedEastern.ToString("MMM d, ", PresentationCulture.UnitedStates))
                + issuedEastern.ToString("h:mm tt", PresentationCulture.UnitedStates);
            full = $"NWS forecast {when}";
            brief = $"NWS {when}";
        }
        else
        {
            // NWS gave no issue time: say when it was retrieved instead, never pass that off as the issue time.
            var retrieved = EasternTimeZone.ConvertFromUtc(forecast.RetrievedAt).ToString("h:mm tt", PresentationCulture.UnitedStates);
            full = $"NWS · retrieved {retrieved}";
            brief = full;
        }

        if (forecast.StartsAfterOpening)
        {
            // The park had already opened: the values describe only the rest of its hours, so say which.
            var from = ClockText(TimeOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(forecast.CoverageStart).DateTime));
            var to = ClockText(TimeOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(forecast.ClosesAt).DateTime));
            full += $" · covers {from}–{to}";
            brief += $" · {from}–{to}";
        }

        return (full, brief);
    }

    private static string ClockText(TimeOnly time) =>
        time.ToString(time.Minute == 0 ? "h tt" : "h:mm tt", PresentationCulture.UnitedStates);
}
