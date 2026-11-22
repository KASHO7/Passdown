using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Application.PassPerks;

/// <summary>Phase 32: one cached perk with its status on the operating date the overview was built for.</summary>
public sealed record PassPerkEntry(OfficialPassPerk Perk, PassPerkAssessment Assessment);

/// <summary>
/// Phase 32: the perks listed for one campaign month. <see cref="WasObserved"/> is false when no successful
/// check has ever seen that month's list — the month is then "not verified", never "no perks".
/// </summary>
public sealed record PassPerksMonthView(int Year, int Month, IReadOnlyList<PassPerkEntry> Entries, bool WasObserved, bool IsLatestCampaign)
{
    public DateOnly FirstDay => new(Year, Month, 1);
}

/// <summary>
/// Phase 32: everything the Products panel and the Calendar need, built from the cached document only.
/// Status is recomputed from <see cref="OperatingDate"/> on every read, so nothing depends on a refresh.
/// </summary>
public sealed record PassPerksOverview(PassPerksDocument? Document, OfficialDocumentStatus Status, DateOnly OperatingDate, DateTimeOffset Now)
{
    public static PassPerksOverview NotChecked(DateOnly operatingDate, DateTimeOffset now) =>
        new(null, OfficialDocumentStatus.NotChecked, operatingDate, now);

    public bool HasVerifiedData => Document is { Perks.Count: > 0 };

    public PassPerksProgramTerms? ProgramTerms => Document?.ProgramTerms;

    /// <summary>The operating month, then every month a check observed (newest first) — the month picker's choices.</summary>
    public IReadOnlyList<DateOnly> AvailableMonths
    {
        get
        {
            var months = new SortedSet<DateOnly> { new(OperatingDate.Year, OperatingDate.Month, 1) };
            foreach (var campaign in Document?.Campaigns ?? [])
            {
                months.Add(campaign.Campaign.FirstDay);
            }

            foreach (var perk in Document?.Perks ?? [])
            {
                months.Add(perk.Campaign.FirstDay);
            }

            return months.Reverse().ToList();
        }
    }

    public PassPerksMonthView ForMonth(int year, int month)
    {
        var key = $"{year:D4}-{month:D2}";
        var entries = (Document?.Perks ?? [])
            .Where(p => p.Campaign.Key == key)
            .Select(p => new PassPerkEntry(p, PassPerkStatusEvaluator.Evaluate(p, OperatingDate, Now)))
            .OrderBy(e => Rank(e.Assessment.Status))
            .ThenBy(e => e.Perk.RewardType == PassPerkRewardType.VisitBased ? 0 : 1)
            .ThenBy(e => e.Perk.Title, StringComparer.Ordinal)
            .ToList();
        var observed = Document?.Campaigns.Any(c => c.Campaign.Key == key) == true || entries.Count > 0;
        return new PassPerksMonthView(year, month, entries, observed, Document?.LatestCampaignKey == key);
    }

    /// <summary>Date-specific Calendar entries between two dates (inclusive), with each perk's status.</summary>
    public IReadOnlyList<(PassPerkCalendarEntry Entry, PassPerkAssessment Assessment)> CalendarEntries(DateOnly first, DateOnly last) =>
        PassPerkCalendar.EntriesBetween(Document?.Perks ?? [], first, last)
            .Select(e => (e, PassPerkStatusEvaluator.Evaluate(e.Perk, OperatingDate, Now)))
            .ToList();

    /// <summary>A month's perks that carry no daily marker (valid all month, or no perk-specific date).</summary>
    public IReadOnlyList<PassPerkEntry> MonthLongEntries(int year, int month) =>
        PassPerkCalendar.MonthLongPerks(Document?.Perks ?? [], year, month)
            .Where(p => p.NoLongerListedSince is null || p.LastVerifiedAt >= new DateTimeOffset(new DateTime(year, month, 1), TimeSpan.Zero))
            .Select(p => new PassPerkEntry(p, PassPerkStatusEvaluator.Evaluate(p, OperatingDate, Now)))
            .ToList();

    private static int Rank(PassPerkStatus status) => status switch
    {
        PassPerkStatus.Current => 0,
        PassPerkStatus.Upcoming => 1,
        PassPerkStatus.NotReconfirmed => 2,
        PassPerkStatus.Expired => 3,
        _ => 4,
    };
}

public interface IPassPerksService
{
    /// <summary>Reads the cached official Pass Perks only — never retrieves online.</summary>
    Task<PassPerksOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
}

public sealed class PassPerksService(IOfficialReferenceDocumentRepository documents, IClock clock) : IPassPerksService
{
    public async Task<PassPerksOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var operatingDate = DateOnly.FromDateTime(now.DateTime);
        var record = await documents.GetAsync(PassPerksDocumentIds.PassPerks, cancellationToken).ConfigureAwait(false);
        var status = OfficialDocumentStatus.From(record);
        var document = OfficialSeasonJson.TryDeserialize<PassPerksDocument>(record?.SuccessPayloadJson);
        return new PassPerksOverview(document, status, operatingDate, now);
    }
}
