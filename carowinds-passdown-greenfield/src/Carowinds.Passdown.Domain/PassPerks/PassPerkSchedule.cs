using System.Globalization;

namespace Carowinds.Passdown.Domain.PassPerks;

/// <summary>Phase 32: a perk's public status on an operating date (never a statement about one guest's pass).</summary>
public enum PassPerkStatus
{
    /// <summary>Listed at the last check and redeemable (or its qualifying period is open) on the date.</summary>
    Current,

    /// <summary>Listed, with a confirmed redemption start after the date.</summary>
    Upcoming,

    /// <summary>Its confirmed redemption end (or booking deadline) has passed.</summary>
    Expired,

    /// <summary>A complete later official check no longer listed it.</summary>
    NoLongerListed,

    /// <summary>Not re-confirmed recently (the last check failed or could not read the page fully).</summary>
    NotReconfirmed,
}

/// <summary>Phase 32: the status and the plain-language reason for it.</summary>
public sealed record PassPerkAssessment(PassPerkStatus Status, string Reason);

/// <summary>
/// Phase 32: the status rules. Recomputed on every read from the cached document and the operating date,
/// so a perk whose redemption ends moves out of Current without any refresh. Uses only perk-specific dates;
/// a month heading never makes a perk expire or start.
/// </summary>
public static class PassPerkStatusEvaluator
{
    /// <summary>A perk not re-verified within this window is shown as "not re-confirmed".</summary>
    public static readonly TimeSpan ReconfirmWindow = TimeSpan.FromHours(48);

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static PassPerkAssessment Evaluate(OfficialPassPerk perk, DateOnly operatingDate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(perk);

        if (perk.NoLongerListedSince is { } removed)
        {
            return new(PassPerkStatus.NoLongerListed, $"No longer listed on the official page (checked {Day(DateOnly.FromDateTime(removed.DateTime))}); last listed {Day(DateOnly.FromDateTime(perk.LastVerifiedAt.DateTime))}.");
        }

        var redemption = perk.Redemption is { IsPerkSpecific: true } r ? r : null;
        var booking = perk.DateOf(PassPerkDateRole.BookingDeadline);
        var qualifying = perk.QualifyingPeriod;

        if (redemption is not null && redemption.End < operatingDate)
        {
            return new(PassPerkStatus.Expired, $"Redemption ended {Day(redemption.End)}.");
        }

        if (redemption is null && booking is not null && booking.End < operatingDate)
        {
            return new(PassPerkStatus.Expired, $"Booking closed {Day(booking.End)}.");
        }

        if (perk.LastCheckIncomplete)
        {
            return new(PassPerkStatus.NotReconfirmed, $"The latest check could not fully read the official page; shown as last verified {Day(DateOnly.FromDateTime(perk.LastVerifiedAt.DateTime))}.");
        }

        if (now - perk.LastVerifiedAt > ReconfirmWindow)
        {
            return new(PassPerkStatus.NotReconfirmed, $"Not re-confirmed since {Day(DateOnly.FromDateTime(perk.LastVerifiedAt.DateTime))}.");
        }

        var qualifyingOpen = qualifying is not null && qualifying.Contains(operatingDate);
        if (!qualifyingOpen && qualifying?.Start is { } qualifyingStart && qualifyingStart > operatingDate)
        {
            return new(PassPerkStatus.Upcoming, $"Qualifying visits start {Day(qualifyingStart)}.");
        }

        if (!qualifyingOpen && redemption?.Start is { } start && start > operatingDate)
        {
            return new(PassPerkStatus.Upcoming, qualifying is not null && qualifying.End < operatingDate
                ? $"Qualifying period ended {Day(qualifying.End)}; earned rewards are redeemable from {Day(start)}."
                : $"Redeemable from {Day(start)}.");
        }

        if (qualifyingOpen)
        {
            return new(PassPerkStatus.Current, $"Qualifying visits through {Day(qualifying!.End)}.");
        }

        return new(PassPerkStatus.Current, redemption is not null
            ? $"Redeemable through {Day(redemption.End)}."
            : booking is not null ? $"Book by {Day(booking.End)}." : "Listed on the official page.");
    }

    private static string Day(DateOnly date) => date.ToString("MMM d, yyyy", Us);
}

/// <summary>Phase 32: why a perk appears on a Calendar date.</summary>
public enum PassPerkCalendarEntryKind
{
    /// <summary>A short, date-specific redemption window covers this day.</summary>
    RedeemableThisDay,

    /// <summary>The first day of a longer confirmed redemption window.</summary>
    RedemptionBegins,

    /// <summary>The last day of a confirmed redemption window (or a stated "valid through" date).</summary>
    RedemptionEnds,

    /// <summary>A confirmed booking/purchase deadline ("Book by November 30").</summary>
    BookingDeadline,

    /// <summary>The last day of a confirmed qualifying-visit period.</summary>
    QualifyingEnds,
}

/// <summary>Phase 32: one perk on one Calendar date.</summary>
public sealed record PassPerkCalendarEntry(DateOnly Date, PassPerkCalendarEntryKind Kind, OfficialPassPerk Perk, PassPerkDateWindow Window);

/// <summary>
/// Phase 32: Calendar marker rules. A marker needs a perk-specific official date — never a month heading,
/// never a page-wide banner. Month-long redemption (the whole campaign month, e.g. "November 1 - November
/// 30") is summarized per month instead of repeated on every day. A short window (up to
/// <see cref="DailyMarkerMaxDays"/> days, e.g. a limited weekend) marks every day; a longer one marks its
/// first and last day. A perk a complete check no longer lists contributes no marker after it was last listed.
/// </summary>
public static class PassPerkCalendar
{
    public const int DailyMarkerMaxDays = 14;

    public static IReadOnlyList<PassPerkCalendarEntry> EntriesBetween(IEnumerable<OfficialPassPerk> perks, DateOnly first, DateOnly last)
    {
        ArgumentNullException.ThrowIfNull(perks);

        var entries = new List<PassPerkCalendarEntry>();
        foreach (var perk in perks)
        {
            var cutoff = perk.NoLongerListedSince is not null ? DateOnly.FromDateTime(perk.LastVerifiedAt.DateTime) : DateOnly.MaxValue;
            void Add(DateOnly date, PassPerkCalendarEntryKind kind, PassPerkDateWindow window)
            {
                if (date >= first && date <= last && date <= cutoff)
                {
                    entries.Add(new PassPerkCalendarEntry(date, kind, perk, window));
                }
            }

            foreach (var window in perk.Dates.Where(d => d.IsPerkSpecific))
            {
                switch (window.Role)
                {
                    case PassPerkDateRole.Redemption when window.Start is { } start:
                        if (IsMonthLong(perk, window))
                        {
                            break;
                        }

                        if (window.LengthInDays <= DailyMarkerMaxDays)
                        {
                            for (var day = start; day <= window.End; day = day.AddDays(1))
                            {
                                Add(day, PassPerkCalendarEntryKind.RedeemableThisDay, window);
                            }
                        }
                        else
                        {
                            Add(start, PassPerkCalendarEntryKind.RedemptionBegins, window);
                            Add(window.End, PassPerkCalendarEntryKind.RedemptionEnds, window);
                        }

                        break;
                    case PassPerkDateRole.Redemption:
                        Add(window.End, PassPerkCalendarEntryKind.RedemptionEnds, window);
                        break;
                    case PassPerkDateRole.BookingDeadline:
                        Add(window.End, PassPerkCalendarEntryKind.BookingDeadline, window);
                        break;
                    case PassPerkDateRole.QualifyingVisits:
                        Add(window.End, PassPerkCalendarEntryKind.QualifyingEnds, window);
                        break;
                }
            }
        }

        return entries
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Perk.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Perks listed for <paramref name="month"/> that have no date-specific marker that month: redemption
    /// covering the whole campaign month, or no perk-specific date at all (summarized, never marked daily).
    /// </summary>
    public static IReadOnlyList<OfficialPassPerk> MonthLongPerks(IEnumerable<OfficialPassPerk> perks, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(perks);
        return perks
            .Where(p => p.Campaign.Year == year && p.Campaign.Month == month)
            .Where(p => p.Redemption is null ? p.Dates.Count == 0 : IsMonthLong(p, p.Redemption))
            .OrderBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsMonthLong(OfficialPassPerk perk, PassPerkDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(perk);
        ArgumentNullException.ThrowIfNull(window);
        return window.Role == PassPerkDateRole.Redemption
            && window.Start == perk.Campaign.FirstDay
            && window.End == perk.Campaign.LastDay;
    }
}
