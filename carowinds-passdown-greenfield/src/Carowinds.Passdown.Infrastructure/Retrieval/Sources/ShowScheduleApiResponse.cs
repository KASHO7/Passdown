namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 21C deserialization shape for the <c>shows</c> part of the official FIMS operating-hours
/// response (<c>https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{id}?date=yyyyMMdd</c> — the
/// same endpoint <see cref="ParkHoursSource"/> reads; see docs/phase21c-official-show-schedules.md).
/// Kept separate from <see cref="ParkHoursApiResponse"/> so the park-hours contract is untouched; only
/// the fields <see cref="ShowScheduleSource"/> reads are modeled (no venues/events/descriptions).
/// </summary>
internal sealed class ShowScheduleApiResponse
{
    public List<ShowScheduleApiDate>? Dates { get; set; }

    public string? Error { get; set; }
}

internal sealed class ShowScheduleApiDate
{
    /// <summary><c>MM/dd/yyyy</c> — the date this entry confirms.</summary>
    public string? Date { get; set; }

    public bool IsParkClosed { get; set; }

    /// <summary>Read only to tell a published open day (park hours present) from an unpublished one.</summary>
    public List<ParkHoursApiOperating>? Operatings { get; set; }

    /// <summary>
    /// One entry per show the feed associates with the date. Observed live (2026-10-16): a show listed
    /// with blank <see cref="ShowScheduleApiItem.Times"/> is not performing that date (daytime shows on an
    /// evening-only SCarowinds night; Halloween shows still listed on December Winterfest dates).
    /// </summary>
    public List<ShowScheduleApiShow>? Shows { get; set; }
}

internal sealed class ShowScheduleApiShow
{
    /// <summary>Numeric FIMS show id, e.g. <c>2261</c>.</summary>
    public int? ShowId { get; set; }

    /// <summary>Official FIMS location id, e.g. <c>SHOW-030-00122</c> — the same id the official show pages carry as <c>fimsId</c>.</summary>
    public string? ExtLocationId { get; set; }

    public string? ShowName { get; set; }

    public List<ShowScheduleApiItem>? Items { get; set; }
}

internal sealed class ShowScheduleApiItem
{
    /// <summary>Venue text, e.g. <c>"Carowinds Theatre"</c>; sometimes blank, sometimes with trailing spaces.</summary>
    public string? AssignmentLocation { get; set; }

    /// <summary>Comma-separated 12-hour start times, e.g. <c>"07:00 PM, 08:00 PM"</c>; blank when none for this date.</summary>
    public string? Times { get; set; }
}
