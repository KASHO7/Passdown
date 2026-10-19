namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Deserialization shape for `https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{id}`,
/// the endpoint the official park-hours page itself calls (see
/// docs/retrieval/park-hours-source.md). Only the fields <see cref="ParkHoursSource"/> actually
/// reads are modeled — the live response also carries <c>venues</c>, <c>shows</c>, and
/// <c>events</c>, which are deliberately omitted here rather than parsed and ignored.
/// <see cref="Error"/> covers the response shape a non-2xx/error reply from this endpoint uses
/// (<c>{"error":"...","message":"..."}</c>) so the adapter can detect it defensively even if it
/// ever arrived with a 200 status, the same way the official page's own client code guards against
/// it.
/// </summary>
internal sealed class ParkHoursApiResponse
{
    public List<ParkHoursApiDate>? Dates { get; set; }

    public string? Error { get; set; }
}

internal sealed class ParkHoursApiDate
{
    /// <summary>`MM/dd/yyyy`, e.g. `"08/20/2026"` — the date this entry actually confirms.</summary>
    public string? Date { get; set; }

    public bool IsParkClosed { get; set; }

    /// <summary>
    /// One entry per gate/area (`"Park"`, `"Waterpark"`, …). Empty when <see cref="IsParkClosed"/>
    /// is true, and also empty — with <see cref="IsParkClosed"/> false — when the schedule for
    /// this date has not been published yet (see docs/retrieval/park-hours-source.md's
    /// NotPublished row).
    /// </summary>
    public List<ParkHoursApiOperating>? Operatings { get; set; }
}

internal sealed class ParkHoursApiOperating
{
    /// <summary>
    /// Phase 22E.1: the operating type's official id — 24 for the main gate, 70 for the water park
    /// (read from the official page's own CMS configuration; the page matches entries by this id).
    /// </summary>
    public int? OperatingTypeId { get; set; }

    /// <summary>
    /// Display label for the entry: usually <c>"Park"</c>/<c>"Waterpark"</c>, but seasonal main-gate
    /// days carry other labels with the same id 24 (observed: <c>"Winterfest"</c>, <c>"New Years Eve"</c>).
    /// </summary>
    public string? OperatingTypeName { get; set; }

    public List<ParkHoursApiOperatingItem>? Items { get; set; }
}

internal sealed class ParkHoursApiOperatingItem
{
    /// <summary>24-hour `HH:mm`, e.g. `"10:00"`. Blank when not published for this date.</summary>
    public string? TimeFrom { get; set; }

    public string? TimeTo { get; set; }
}
