using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Phase 22C: clearly synthetic Dashboard snapshots (made-up show names, "Synthetic" note titles, an
/// invented $64.99 price) for the Operations Overview tests and the isolated screenshot harness. Never
/// real operational data. Every snapshot is generated at 2:00 PM Eastern on its operating date, so the
/// "upcoming vs earlier today" show split is deterministic.
/// </summary>
internal static class Phase22CDashboardFixtures
{
    public static readonly decimal SyntheticParkTicketPrice = 64.99m;

    private static readonly SourceCategory[] AllCategories =
    [
        SourceCategory.ParkHours,
        SourceCategory.DatedAdmissionTickets,
        SourceCategory.DatedFastLane,
        SourceCategory.HauntedAttractions,
        SourceCategory.SeasonPasses,
        SourceCategory.AddOns,
        SourceCategory.Promotions,
    ];

    /// <summary>2:00 PM Eastern (EDT, UTC−4) on <paramref name="date"/>.</summary>
    public static DateTimeOffset AfternoonOf(DateOnly date) => new(date.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero);

    public static DateTimeOffset MorningCheckOf(DateOnly date) => new(date.ToDateTime(new TimeOnly(10, 5)), TimeSpan.Zero);

    /// <summary>A typical verified operating day: verified hours, exact dated Park Ticket price, draft in progress, two notes, shows, all sources current (one not published).</summary>
    public static OperationsDashboardSnapshot VerifiedOperatingDay(DateOnly date)
    {
        var checkedAt = MorningCheckOf(date);
        return Base(date) with
        {
            ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.Open, OpenHours(10, 22, AssociateVerificationState.Verified, checkedAt)),
            PassdownStatus = new DashboardPassdownStatusSummary(DashboardPassdownStatus.DraftInProgress, checkedAt.AddHours(2), null),
            ProjectedAttendance = 24_000,
            PreSolds = 11_850,
            DailyFeatureType = DailyFeatureType.UselessFactOfTheDay,
            DailyFeatureText = "Synthetic fact: a coaster train at the top of a 325 ft drop has about 1.3 MJ of potential energy.",
            Notes = new DashboardNotesSummary(true, ["Synthetic: rain plan reviewed at 9 AM huddle."], 1),
            ActiveOperationalNotes = Notes(2, important: 1),
            DataHealth = Health(checkedAt, notPublished: [SourceCategory.HauntedAttractions]),
            TodayShows = Shows(date, checkedAt),
            Admission = ExactAdmission(date, checkedAt),
        };
    }

    /// <summary>A busy day with a partial official-source failure: Fast Lane unavailable, Promotions stale, many notes and shows.</summary>
    public static OperationsDashboardSnapshot BusyDayWithPartialFailure(DateOnly date)
    {
        var checkedAt = MorningCheckOf(date);
        return VerifiedOperatingDay(date) with
        {
            ProjectedAttendance = 38_500,
            PreSolds = 21_240,
            Notes = new DashboardNotesSummary(true, ["Synthetic: Gate 2 opens 30 minutes early.", "Synthetic: VIP tour at 3 PM.", "Synthetic: radio check channel 4."], 5),
            ActiveOperationalNotes = Notes(5, important: 2),
            DataHealth = Health(checkedAt, notPublished: [SourceCategory.HauntedAttractions], unavailable: [SourceCategory.DatedFastLane], stale: [SourceCategory.Promotions]),
        };
    }

    /// <summary>Nothing published for today: hours and dated prices not published, no shows published, no price.</summary>
    public static OperationsDashboardSnapshot NoPublishedData(DateOnly date)
    {
        var checkedAt = MorningCheckOf(date);
        var hours = new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
            new AssociateVerificationInfo(AssociateVerificationState.NotPublished, null, null, null),
            OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null);
        return Base(date) with
        {
            ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.NotPublished, hours),
            DataHealth = new DashboardDataHealthSummary(
                DashboardOverallDataStatus.NoPublishedData, 0, 0, 0, 0, AllCategories.Length - 3, 3,
                AllCategories.Select((c, i) => new DashboardSourceStatusEntry(c, i < AllCategories.Length - 3 ? DashboardSourceStatus.NotPublished : DashboardSourceStatus.NotChecked, null, i < 4 ? checkedAt : null)).ToList(),
                null),
            TodayShows = new ShowScheduleView(date, ShowScheduleState.NotPublished, [], false, checkedAt, checkedAt),
            Admission = UnavailableAdmission(AssociateVerificationState.NotPublished, checkedAt),
        };
    }

    /// <summary>A verified closed day.</summary>
    public static OperationsDashboardSnapshot ClosedPark(DateOnly date)
    {
        var checkedAt = MorningCheckOf(date);
        var hours = new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null),
            new AssociateVerificationInfo(AssociateVerificationState.Closed, checkedAt, null, null),
            OfficialIsClosed: true, OfficialOpening: null, OfficialClosing: null);
        return Base(date) with
        {
            ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.Closed, hours),
            DataHealth = Health(checkedAt, unavailable: [SourceCategory.DatedAdmissionTickets, SourceCategory.DatedFastLane]),
            TodayShows = new ShowScheduleView(date, ShowScheduleState.ParkClosed, [], false, checkedAt, checkedAt),
            Admission = UnavailableAdmission(AssociateVerificationState.Unavailable, checkedAt),
        };
    }

    /// <summary>Last-known-good after a failed recheck: stale hours and a stale exact price.</summary>
    public static OperationsDashboardSnapshot StaleAfterFailure(DateOnly date)
    {
        var verifiedYesterday = MorningCheckOf(date).AddDays(-1);
        return VerifiedOperatingDay(date) with
        {
            ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.Open, OpenHours(10, 22, AssociateVerificationState.Stale, verifiedYesterday)),
            DataHealth = Health(verifiedYesterday, stale: [SourceCategory.ParkHours, SourceCategory.DatedAdmissionTickets], verificationIssue: [SourceCategory.DatedFastLane]),
            Admission = ExactAdmission(date, verifiedYesterday) with { PriceState = AssociateVerificationState.Stale, CategoryState = AssociateVerificationState.Stale },
        };
    }

    private static OperationsDashboardSnapshot Base(DateOnly date) =>
        FakeOperationsDashboardService.DefaultSnapshot(date) with { GeneratedAt = AfternoonOf(date) };

    private static AssociateParkHoursView OpenHours(int open, int close, AssociateVerificationState state, DateTimeOffset verifiedAt)
    {
        var (o, c) = (new TimeOnly(open, 0), new TimeOnly(close, 0));
        return new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, o, c, false, null, null, false, null, null),
            new AssociateVerificationInfo(state, verifiedAt, state == AssociateVerificationState.Stale ? TimeSpan.FromDays(1) : null, null),
            OfficialIsClosed: false, OfficialOpening: o, OfficialClosing: c);
    }

    public static DashboardAdmissionSummary ExactAdmission(DateOnly date, DateTimeOffset verifiedAt) => new(
        DashboardAdmissionKind.ExactDated, DashboardAdmissionUnavailableReason.None, "Park Ticket", SyntheticParkTicketPrice, date,
        AssociateVerificationState.Verified, verifiedAt, AssociateVerificationState.Verified, verifiedAt);

    public static DashboardAdmissionSummary UnavailableAdmission(AssociateVerificationState categoryState, DateTimeOffset checkedAt) => new(
        DashboardAdmissionKind.NotAvailable, DashboardAdmissionUnavailableReason.NoProduct, null, null, null,
        categoryState, null, categoryState, checkedAt);

    private static DashboardOperationalNotesSummary Notes(int count, int important)
    {
        var items = Enumerable.Range(0, Math.Min(count, DashboardOperationalNotesSummary.MaxPreviewCount))
            .Select(i => new DashboardOperationalNoteItem(
                Guid.NewGuid(),
                i switch { 0 => "Synthetic: Fury 325 queue reroute near the plaza", 1 => "Synthetic: Guest Services relocation", _ => "Synthetic: parking lot C closed for paving" },
                OperationalNoteCategory.General,
                i < important ? OperationalNotePriority.Important : OperationalNotePriority.Normal,
                new DateOnly(2026, 10, 1),
                null))
            .ToList();
        return new DashboardOperationalNotesSummary(count, important, items, false);
    }

    private static DashboardDataHealthSummary Health(
        DateTimeOffset checkedAt,
        SourceCategory[]? notPublished = null,
        SourceCategory[]? unavailable = null,
        SourceCategory[]? stale = null,
        SourceCategory[]? verificationIssue = null)
    {
        DashboardSourceStatus StatusOf(SourceCategory c) =>
            verificationIssue?.Contains(c) == true ? DashboardSourceStatus.VerificationIssue
            : stale?.Contains(c) == true ? DashboardSourceStatus.Stale
            : unavailable?.Contains(c) == true ? DashboardSourceStatus.Unavailable
            : notPublished?.Contains(c) == true ? DashboardSourceStatus.NotPublished
            : DashboardSourceStatus.Current;

        var sources = AllCategories.Select(c =>
        {
            var status = StatusOf(c);
            var dated = c is SourceCategory.DatedAdmissionTickets or SourceCategory.DatedFastLane or SourceCategory.HauntedAttractions;
            return new DashboardSourceStatusEntry(c, status,
                dated ? null : status is DashboardSourceStatus.Current or DashboardSourceStatus.Stale ? checkedAt : null,
                dated ? checkedAt : null);
        }).ToList();

        int Count(DashboardSourceStatus s) => sources.Count(e => e.Status == s);
        var overall = Count(DashboardSourceStatus.VerificationIssue) > 0 ? DashboardOverallDataStatus.VerificationIssue
            : Count(DashboardSourceStatus.Stale) > 0 ? DashboardOverallDataStatus.SomeStale
            : Count(DashboardSourceStatus.Unavailable) > 0 ? DashboardOverallDataStatus.SomeUnavailable
            : Count(DashboardSourceStatus.Current) == 0 ? DashboardOverallDataStatus.NoPublishedData
            : DashboardOverallDataStatus.Current;

        var verified = sources.Where(e => e.LastVerifiedAt.HasValue).Select(e => e.LastVerifiedAt!.Value).ToList();
        return new DashboardDataHealthSummary(
            overall, Count(DashboardSourceStatus.Current), Count(DashboardSourceStatus.Stale), Count(DashboardSourceStatus.Unavailable),
            Count(DashboardSourceStatus.VerificationIssue), Count(DashboardSourceStatus.NotPublished), Count(DashboardSourceStatus.NotChecked),
            sources, verified.Count > 0 ? verified.Max() : null);
    }

    private static ShowScheduleView Shows(DateOnly date, DateTimeOffset checkedAt) => new(
        date,
        ShowScheduleState.Scheduled,
        [
            new ScheduledShow("SYN-001", "Synthetic Stage Revue", "Synthetic Amphitheater", [new TimeOnly(12, 0), new TimeOnly(15, 30), new TimeOnly(18, 0)]),
            new ScheduledShow("SYN-002", "Synthetic Street Band", null, [new TimeOnly(13, 0), new TimeOnly(16, 0)]),
            new ScheduledShow("SYN-003", "Synthetic Night Show", "Synthetic Plaza Stage", [new TimeOnly(20, 30)]),
        ],
        false,
        checkedAt,
        checkedAt);
}
