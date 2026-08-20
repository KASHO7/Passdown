using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Shared plumbing for accesso Passport's `POST .../api/request/getkeywordcalendar` endpoint,
/// used by both <see cref="DatedAdmissionSource"/> (Phase 3C, keyword `SnapWizardDailyTickets`;
/// see docs/retrieval/dated-admission-source.md) and <see cref="DatedFastLaneSource"/> (Phase 3D,
/// keyword `FastLaneOnly`; see docs/retrieval/dated-fast-lane-source.md).
///
/// Extracted here only after both live investigations independently proved these specific pieces
/// byte-for-byte identical — not because "both sources use accesso" in general (that would be
/// exactly the kind of generalized-framework-by-assumption CLAUDE.md/spec §5 warns against). Each
/// adapter keeps its own <c>keyword</c> and <c>display_zero_capacity</c> value (directly observed
/// to differ: `"1"` vs `"0"`), its own control flow, its own status decisions, and its own product
/// interpretation — none of that lives here.
/// </summary>
internal static class AccessoKeywordCalendarRequest
{
    public static readonly Uri Endpoint =
        new("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar");

    /// <summary>
    /// Public storefront identification headers — not credentials. Proven required (a request
    /// missing `referer`/`origin` gets a plain-text 400 "Bad Request") and identical for both
    /// keywords investigated so far.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string>
    {
        ["com-accessopassport-client"] = "cedarfair",
        ["com-accessopassport-language"] = "en",
        ["com-accessopassport-merchant-id"] = "1",
        ["com-accessopassport-app-id"] = "1500",
        ["referer"] = "https://cf-ca.buy.sixflags.com/",
        ["origin"] = "https://cf-ca.buy.sixflags.com",
    };

    /// <summary>
    /// Builds the JSON body. <paramref name="keyword"/> and <paramref name="displayZeroCapacity"/>
    /// are supplied by the caller because they are the two fields directly observed to differ
    /// between keywords; every other field here was confirmed byte-for-byte identical across both
    /// live investigations. No cart/session fields (`cart_id`/`cart_key`/`session_id`) are
    /// included — both investigations independently confirmed the endpoint accepts anonymous
    /// requests without them.
    /// </summary>
    public static string BuildBody(string keyword, string displayZeroCapacity, DateOnly startDate, DateOnly endDate) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["display_zero_capacity"] = displayZeroCapacity,
            ["keyword"] = keyword,
            ["end_date"] = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["start_date"] = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["request_type"] = "GetKeywordCalendar",
            ["_version"] = "6.33.1",
            ["application_id"] = "1500",
            ["merchant_id"] = "1",
            ["machine_id"] = "500",
            ["agent_id"] = "5",
            ["user_id"] = "5",
            ["device"] = "desktop",
            ["language"] = "en",
            ["request_token"] = Guid.NewGuid().ToString("N").ToUpperInvariant(),
        });
}

/// <summary>
/// Deserialization shape for the `getkeywordcalendar` response envelope — proven identical (same
/// field names, same single-object-or-array `D` behavior) across both keywords investigated.
/// Product-specific interpretation (grouping, comparison-price/qualifier rules, status decisions)
/// stays in each adapter; only the transport envelope shape lives here.
/// </summary>
internal sealed class AccessoKeywordCalendarApiResponse
{
    public AccessoKeywordCalendarService? Service { get; set; }
}

internal sealed class AccessoKeywordCalendarService
{
    /// <summary>`"OK"` when at least one date in the requested range matched; `"FAILED"` otherwise (observed for closed dates, dates beyond the published horizon, and past dates alike — see docs for both keywords).</summary>
    public string? Status { get; set; }

    [JsonPropertyName("error_msg")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("DATES")]
    public AccessoKeywordCalendarDates? Dates { get; set; }
}

internal sealed class AccessoKeywordCalendarDates
{
    /// <summary>
    /// The live endpoint returns a single JSON object when exactly one date in the requested
    /// range has data, and a JSON array when more than one date matches — directly observed for
    /// both keywords, not guessed. <see cref="SingleOrArrayConverter{T}"/> normalizes both shapes
    /// to a list so callers never have to branch on which one arrived.
    /// </summary>
    [JsonPropertyName("D")]
    [JsonConverter(typeof(SingleOrArrayConverter<AccessoKeywordCalendarDateEntry>))]
    public List<AccessoKeywordCalendarDateEntry>? Entries { get; set; }
}

internal sealed class AccessoKeywordCalendarDateEntry
{
    /// <summary>`yyyy-MM-dd`, e.g. `"2026-08-22"` — the date this entry actually confirms.</summary>
    public string? Date { get; set; }

    [JsonPropertyName("PS")]
    public AccessoKeywordCalendarPackageList? Packages { get; set; }
}

internal sealed class AccessoKeywordCalendarPackageList
{
    public List<AccessoKeywordCalendarPackage>? P { get; set; }
}

internal sealed class AccessoKeywordCalendarPackage
{
    /// <summary>The official accesso package id (spec's "official product/package id"). Stable per price tier — for `FastLaneOnly` this id repeats across different dates that share a price tier, unlike `SnapWizardDailyTickets`'s per-date ids (see docs/retrieval/dated-fast-lane-source.md).</summary>
    public string? Id { get; set; }

    /// <summary>Display name, e.g. `"Park Ticket"` or `"FAST LANE RESERVE"`.</summary>
    public string? Name { get; set; }

    [JsonPropertyName("CT")]
    public AccessoKeywordCalendarCustomerType? CustomerType { get; set; }
}

internal sealed class AccessoKeywordCalendarCustomerType
{
    /// <summary>Decimal string, e.g. `"49.00"` — pre-tax price, matching what the official page itself displays.</summary>
    [JsonPropertyName("retail_amount")]
    public string? RetailAmount { get; set; }
}

/// <summary>
/// Normalizes a JSON value that is sometimes a single object and sometimes an array of that same
/// object shape into a <see cref="List{T}"/> — the accesso calendar endpoint's own directly
/// observed behavior for its <c>D</c> field. Write is unused; nothing serializes this shape back
/// out.
/// </summary>
internal sealed class SingleOrArrayConverter<T> : JsonConverter<List<T>>
{
    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];
        }

        var single = JsonSerializer.Deserialize<T>(ref reader, options);
        return single is null ? [] : [single];
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        throw new NotSupportedException($"{nameof(SingleOrArrayConverter<T>)} is read-only.");
}
