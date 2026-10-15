using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 19A: whether one operating date's date-specific official data is missing or stale — the
/// question the Associate screen's quiet automatic check asks before going online. Deliberately
/// reuses the existing per-category <see cref="FreshnessPolicy"/> (daily at 6:00 AM Eastern) rather
/// than a second freshness system: a date's cached result is fresh when it was successfully checked
/// since the most recent daily boundary — exactly the condition under which the scheduled refresh
/// would consider that source not yet due. Kept separate from <see cref="OfficialDataStatus"/>, which
/// tracks app-wide comprehensive freshness, not one date.
/// </summary>
public static class DateCheckFreshness
{
    /// <summary>
    /// True when any dated official category has no cached result for <paramref name="date"/>, its most
    /// recent attempt failed, or its most recent success predates the category's current due boundary.
    /// Catalog sources are not date-specific and are kept current by the comprehensive check instead.
    /// </summary>
    public static bool IsMissingOrStale(
        DateOnly date,
        IReadOnlyList<DatedResultRecord> cachedForDate,
        IEnumerable<SourceCategory> datedCategories,
        IReadOnlyDictionary<SourceCategory, FreshnessPolicy> policies,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(cachedForDate);
        ArgumentNullException.ThrowIfNull(datedCategories);
        ArgumentNullException.ThrowIfNull(policies);

        var nowEastern = EasternTimeZone.ConvertFromUtc(nowUtc);
        foreach (var category in datedCategories.Distinct())
        {
            var record = cachedForDate.FirstOrDefault(r => r.Key.Category == category && r.Key.Date == date);
            if (record is null)
            {
                return true;
            }

            var latest = record.LastAttemptEvidence;
            if (!latest.Status.IsSuccessfulCompletion())
            {
                return true;
            }

            if (policies.TryGetValue(category, out var policy) && policy.IsDue(latest.TimeChecked, nowEastern))
            {
                return true;
            }
        }

        return false;
    }
}
