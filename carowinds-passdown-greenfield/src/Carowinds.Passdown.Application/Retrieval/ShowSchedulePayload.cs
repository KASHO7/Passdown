namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Phase 21C: one official show with at least one published performance on the confirmed date. The
/// show (identity, name, venue) and its performances (<see cref="Times"/>, the official Eastern
/// wall-clock start times, distinct and in operating-day order) are kept separate in meaning even
/// though one compact record stores both: a show listed by the feed with no times for the date is
/// never represented here — that is not a performance (see <see cref="ShowSchedulePayload"/>).
/// </summary>
/// <param name="ShowId">Stable identity from <see cref="Domain.Shows.ShowIdentity.Resolve"/> — the official FIMS id when present.</param>
/// <param name="Venue">The official location text, or null when the feed published none.</param>
public sealed record ScheduledShow(string ShowId, string Name, string? Venue, IReadOnlyList<TimeOnly> Times);

/// <summary>
/// Phase 21C: the official show schedule for one confirmed date — only ever attached to a
/// <see cref="Domain.Retrieval.RetrievalStatus.Verified"/> result. An empty <see cref="Shows"/> list is
/// a confirmed "no shows scheduled" (the park is open with published hours and the feed lists no
/// show); a date whose listed shows all have blank times is <c>NotPublished</c> instead and carries no
/// payload at all. Compact by design: no descriptions, images, raw response, or HTML.
/// </summary>
/// <param name="ListedWithoutTimesCount">How many shows the feed listed for the date with no times (not performing / not published) — evidence only, never displayed as a performance.</param>
public sealed record ShowSchedulePayload(IReadOnlyList<ScheduledShow> Shows, int ListedWithoutTimesCount = 0);
