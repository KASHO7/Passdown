using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>
/// Phase 18B: the Personal Sync conflict comparison is user-facing text — AM/PM times, "$" prices and
/// dates must be en-US on any Windows region (pre-18B a de-DE PC lost AM/PM entirely and showed "$54,99").
/// </summary>
public class SyncConflictDetailCultureTests
{
    public static TheoryData<string> Cultures => ["en-GB", "fr-FR", "de-DE", "th-TH", "en-US"];

    private static T Under<T>(string culture, Func<T> render)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void DraftComparison_TimesPricesAndCounts_AreEnUs(string culture)
    {
        var payload = AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(10, 30), new TimeOnly(18, 0)),
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(19, 0), new TimeOnly(23, 0), null),
            ProjectedAttendance = 32500,
            CustomProducts = [new CustomProductEntry("x", "Souvenir Cup", 54.99m, null, true)],
        };
        var detail = new DraftConflictDetail(new DateOnly(2026, 9, 19), payload, null, true, false, Guid.NewGuid(), DateTimeOffset.UtcNow, false);

        var rows = Under(culture, detail.BuildFieldComparisons).ToDictionary(r => r.Label, r => r.LocalValue);

        Assert.Equal("10:30 AM – 6:00 PM", rows["Manual Hours"]);
        Assert.Equal("7:00 PM – 11:00 PM", rows["Private Buyout"]);
        Assert.Equal("32500", rows["Projected Attendance"]);
        Assert.Equal("Souvenir Cup ($54.99)", rows["Custom Products"]);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void NoteComparison_EffectiveDates_AreEnUs(string culture)
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var remote = new OperationalNoteSyncPayload(Guid.NewGuid(), "Gate change", null, default, default,
            new DateOnly(2026, 9, 19), new DateOnly(2026, 10, 3), default, now, now, null);
        var openEnded = remote with { EffectiveThrough = null };

        var ranged = Under(culture, () => new NoteConflictDetail(remote.Id, null, remote, false, true, Guid.NewGuid(), now, false).BuildFieldComparisons());
        var onward = Under(culture, () => new NoteConflictDetail(remote.Id, null, openEnded, false, true, Guid.NewGuid(), now, false).BuildFieldComparisons());

        Assert.Equal("Sep 19, 2026 – Oct 3, 2026", ranged.Single(r => r.Label == "Effective Dates").RemoteValue);
        Assert.Equal("Sep 19, 2026 onward", onward.Single(r => r.Label == "Effective Dates").RemoteValue);
    }
}
