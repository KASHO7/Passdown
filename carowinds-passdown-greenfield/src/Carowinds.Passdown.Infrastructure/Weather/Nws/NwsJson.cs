using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Infrastructure.Weather.Nws;

/// <summary>An NWS response that cannot be used (missing required structure, a foreign link). Never shown to users verbatim.</summary>
public sealed class NwsFormatException(string message) : FormatException(message);

/// <summary>
/// Defensive readers for NWS GeoJSON. Missing or null values come back as null; a value whose unit code is
/// unknown, or whose quality-control flag marks it as failed, also comes back as null rather than a guess.
/// </summary>
internal static class NwsJson
{
    /// <summary>MADIS QC flags treated as unusable: X (rejected), B (subjective bad), Q (questioned).</summary>
    private static readonly HashSet<string> RejectedQualityControl = new(StringComparer.Ordinal) { "X", "B", "Q" };

    public static JsonElement Properties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            throw new NwsFormatException("Response has no properties object.");
        }

        return properties;
    }

    public static string? String(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() is { Length: > 0 } text ? text : null)
            : null;

    public static int? Int(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    public static bool? Bool(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public static DateTimeOffset? Time(JsonElement obj, string name) =>
        String(obj, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant
            : null;

    public static IEnumerable<JsonElement> Array(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    /// <summary>A <c>{ "unitCode", "value", "qualityControl" }</c> quantity, or null when absent, null or rejected by QC.</summary>
    public static (double Value, string Unit)? Quantity(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var quantity) || quantity.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (String(quantity, "qualityControl") is { } qc && RejectedQualityControl.Contains(qc))
        {
            return null;
        }

        if (!quantity.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            return null;
        }

        return (number, String(quantity, "unitCode") ?? string.Empty);
    }

    public static double? Fahrenheit((double Value, string Unit)? quantity) => quantity switch
    {
        { Unit: "wmoUnit:degC" } q => WeatherUnits.CelsiusToFahrenheit(q.Value),
        { Unit: "wmoUnit:degF" } q => q.Value,
        _ => null,
    };

    public static double? Mph((double Value, string Unit)? quantity) => quantity switch
    {
        { Unit: "wmoUnit:km_h-1" } q => WeatherUnits.KilometresPerHourToMph(q.Value),
        { Unit: "wmoUnit:m_s-1" } q => WeatherUnits.MetresPerSecondToMph(q.Value),
        _ => null,
    };

    public static int? Percent((double Value, string Unit)? quantity) => quantity switch
    {
        { Unit: "wmoUnit:percent" } q => (int)Math.Round(q.Value, MidpointRounding.AwayFromZero),
        _ => null,
    };

    public static int? Degrees((double Value, string Unit)? quantity) => quantity switch
    {
        { Unit: "wmoUnit:degree_(angle)" } q => (int)Math.Round(q.Value, MidpointRounding.AwayFromZero) % 360,
        _ => null,
    };

    /// <summary>A required link that must point at <c>https://api.weather.gov</c>; anything else is a format error.</summary>
    public static Uri ApiUri(JsonElement obj, string name)
    {
        if (String(obj, name) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri) && NwsHttpTransport.IsApiUri(uri))
        {
            return uri;
        }

        throw new NwsFormatException($"Link '{name}' is missing or not an api.weather.gov address.");
    }

    /// <summary>The last path segment of a zone or station link ("…/zones/forecast/NCZ071" → "NCZ071").</summary>
    public static string? LastSegment(string? link) =>
        link is null ? null : link.TrimEnd('/').Split('/') is { Length: > 0 } parts ? parts[^1] : null;
}
