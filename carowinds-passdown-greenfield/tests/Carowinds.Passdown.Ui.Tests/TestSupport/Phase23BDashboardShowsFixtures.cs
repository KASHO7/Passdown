using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Phase 23B: synthetic, non-production show schedules for the Dashboard's "Today's Shows" tests and the
/// isolated screenshot harness (tools/visual-fixtures links this folder). Every name, venue and time is
/// invented; show names are prefixed "Synthetic". Snapshots are generated at 10:45 AM ET on their own
/// operating date, so the Dashboard's "today" split (upcoming vs. "+N earlier") is exercised.
/// </summary>
internal static class Phase23BDashboardShowsFixtures
{
    public static readonly DateOnly Date = FakeOperationsDashboardService.DefaultDate; // 2026-08-26 (EDT)
    public static readonly DateTimeOffset GeneratedAt = new(2026, 8, 26, 14, 45, 0, TimeSpan.Zero); // 10:45 AM ET
    private static readonly DateTimeOffset CheckedAt = new(2026, 8, 26, 10, 5, 0, TimeSpan.Zero); // 6:05 AM ET

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    private static IReadOnlyList<TimeOnly> Every15(int fromHour, int fromMinute, int count) =>
        Enumerable.Range(0, count).Select(i => T(fromHour, fromMinute).AddMinutes(15 * i)).ToList();

    public static readonly ScheduledShow[] Typical =
    [
        new("syn-01", "Synthetic Midway Revue", "Midway Stage", [T(13), T(16, 30)]),
        new("syn-02", "Synthetic Street Band", null, [T(12), T(14), T(17)]),
        new("syn-03", "Synthetic Carolina Nights", "Harmony Hall", [T(19, 30), T(21)]),
        new("syn-04", "Synthetic Drumline", "Crossroads", [T(15, 15)]),
    ];

    public static readonly ScheduledShow[] Busy =
    [
        // The reported defect: one meet-and-greet with fourteen afternoon performances.
        new("syn-10", "Synthetic Camp Meet and Greet", "Synthetic Camp Gathering Spot", Every15(11, 15, 14)),
        new("syn-11", "Synthetic Clean Up Crew", "Crossroads", [T(11, 30), T(13), T(14, 30), T(16)]),
        new("syn-12", "Synthetic Character Dance Party", "Planet Plaza", Every15(12, 0, 12)),
        new("syn-13", "Synthetic Pirate Stunt Spectacular with Extended Finale and Fireworks Preview", "Harbor Amphitheater", [T(12, 30), T(15), T(17, 30)]),
        new("syn-14", "Synthetic Street Band", null, [T(10, 0), T(10, 30), T(12), T(14), T(17)]),
        new("syn-15", "Synthetic Acrobats", "The Exceptionally Long Synthetic Covered Pavilion Stage near the Carolina Harbor Entrance", [T(13, 45), T(15, 45), T(18, 15)]),
        new("syn-16", "Synthetic Bubble Show", "Carowinds Theatre", [T(11), T(13, 15), T(14, 45), T(16, 15), T(17, 45), T(19, 15)]),
        new("syn-17", "Synthetic Magic Hour", "Midway Stage", [T(14), T(16), T(18)]),
        new("syn-18", "Synthetic Late Night Scare Zone Parade", "County Fair", [T(22, 30), T(23, 15), T(0, 15), T(1, 0)]),
        new("syn-19", "Synthetic Dive Show", "Harbor Pool", Every15(11, 0, 10)),
        new("syn-20", "Synthetic Wake the Night", "Carowinds Theatre", [T(19), T(20), T(21, 30), T(22, 30)]),
        new("syn-21", "Synthetic Drumline", "Crossroads", [T(15, 15)]),
    ];

    public static ShowScheduleView Scheduled(params ScheduledShow[] shows) => new(Date, ShowScheduleState.Scheduled, shows, false, CheckedAt, CheckedAt);

    public static ShowScheduleView Empty(ShowScheduleState state) => new(Date, state, [], false, CheckedAt, CheckedAt);

    /// <summary>Last verified two days ago; nothing newer recorded.</summary>
    public static ShowScheduleView Stale() => new(Date, ShowScheduleState.Scheduled, Typical, false, CheckedAt.AddDays(-2), CheckedAt.AddDays(-2));

    /// <summary>Verified data kept after the latest check failed (last-known-good).</summary>
    public static ShowScheduleView FailedWithLastKnownGood() => new(Date, ShowScheduleState.Scheduled, Busy, true, CheckedAt.AddHours(-20), CheckedAt);

    /// <summary>Busy-day Phase 22C snapshots whose Today's Shows is <paramref name="view"/>, generated at <see cref="GeneratedAt"/>.</summary>
    public static void Apply(FakeOperationsDashboardService service, ShowScheduleView view) => ApplyTo(service, () => view);

    /// <summary>As <see cref="Apply"/>, reading the schedule on every load, so a test can change it between reloads.</summary>
    public static void ApplyTo(FakeOperationsDashboardService service, Func<ShowScheduleView> view)
    {
        var inner = service.SnapshotFactory ?? FakeOperationsDashboardService.DefaultSnapshot;
        service.SnapshotFactory = date => inner(date) with { TodayShows = view(), GeneratedAt = GeneratedAt };
    }
}
