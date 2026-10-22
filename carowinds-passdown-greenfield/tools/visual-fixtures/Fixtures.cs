using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Ui.Tests.TestSupport;

// Every value here is invented for screenshots. Nothing is copied from a real profile, draft, note or
// official response; show names are prefixed "Synthetic".
// Synthetic, non-production fixtures (Phase 22C–22D busy operating day: Dashboard and Associate).
internal static class BusyDayFixtures
{
    public static readonly DateTimeOffset Verified = new(2026, 10, 17, 6, 5, 0, TimeSpan.FromHours(-4)); // 6:05 AM EDT
    private static AssociateVerificationInfo Ok => new(AssociateVerificationState.Verified, Verified, null, null);

    private static AssociateParkHoursView Hours() => new(
        ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null),
        Ok, OfficialIsClosed: false, OfficialOpening: new TimeOnly(10, 0), OfficialClosing: new TimeOnly(22, 0));

    private static readonly ScheduledShow[] ShowList =
    [
        new("s1", "Synthetic Midway Revue", "Midway Stage", [new TimeOnly(13, 0), new TimeOnly(16, 30)]),
        new("s2", "Synthetic Starlight Spectacular", "Planet Snoopy", [new TimeOnly(18, 0)]),
        new("s3", "Synthetic Carolina Nights", "Harmony Hall", [new TimeOnly(19, 30), new TimeOnly(21, 0)]),
        new("s4", "Synthetic Drumline", null, [new TimeOnly(15, 15)]),
    ];

    private static ShowScheduleView Shows(DateOnly date) => new(date, ShowScheduleState.Scheduled, ShowList, false, Verified, Verified);

    private static AssociateProduct P(DateOnly date, string name, SourceCategory c, decimal? price, PriceKind kind = PriceKind.ExactDated, decimal? compare = null, string? qualifier = null, AssociateVerificationInfo? v = null, decimal? over = null) =>
        new($"syn|{c}|{name}", name, c, false, price, null, compare, qualifier, kind == PriceKind.ExactDated, date, v ?? Ok, true, over, kind);

    private static AssociateProduct Pass(DateOnly date, PassOffer offer, decimal? price, decimal? compare = null, string? qualifier = null) =>
        P(date, offer.DisplayName, SourceCategory.SeasonPasses, price, compare is null ? PriceKind.StandardCatalog : PriceKind.Sale, compare, qualifier,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, Verified, null, "Verified 3 Season Passes, 2 renewal offers, 2 Memberships (1 not available). Official note: *Synthetic processing fee disclosure."))
        with { PassOffer = offer, OfficialSourceUrl = new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes") };

    public static void ApplyBusyAssociate(FakeAssociatePassdownService service, bool partialFailure = true)
    {
        service.WorkspaceFactory = date =>
        {
            var stale = new AssociateVerificationInfo(AssociateVerificationState.Stale, Verified.AddDays(-3), TimeSpan.FromDays(3), null);
            var draft = AssociateDraftPayload.CreateDefault() with
            {
                ProjectedAttendance = 38500,
                PreSolds = 21240,
                DailyFeatureType = DailyFeatureType.QuestionOfTheDay,
                DailyFeatureText = @"If the drop is \(325\) ft, how long is the free fall? \(t = \sqrt{\frac{2h}{g}}\)",
                OperationalNotes = ["Synthetic: water ride closed until 2 PM for testing.", "Synthetic: extra staffing at Gate 1 after 6 PM."],
                Shows = new AssociateShowsSelection(true, [
                    new AssociateShowSelection("s1", ShowList[0].Name, ShowList[0].Venue, ShowList[0].Times, Verified),
                    new AssociateShowSelection("s3", ShowList[2].Name, ShowList[2].Venue, ShowList[2].Times, Verified)]),
            };
            List<AssociateProduct> products =
            [
                P(date, "Park Ticket", SourceCategory.DatedAdmissionTickets, 64.99m, compare: 89.99m),
                P(date, "2-Day Park Ticket", SourceCategory.DatedAdmissionTickets, 89.99m),
                P(date, "Military/First Responder", SourceCategory.MilitaryFirstResponder, 40m, PriceKind.StartingFrom, qualifier: "Valid ID required"),
                P(date, "Fast Lane", SourceCategory.DatedFastLane, 65m, v: partialFailure ? stale : null),
                P(date, "Fast Lane Plus", SourceCategory.DatedFastLane, 109m, v: partialFailure ? stale : null, over: 99m),
                // Phase 25: Season Passes and Memberships as separate groups (synthetic values).
                Pass(date, PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, 2027), 69.99m, 99.99m),
                Pass(date, PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027), 129.99m, 149.99m) with { RenewalPriceAmount = 119.99m },
                Pass(date, PassOffer.SeasonPass("Prestige", PassPriceType.NewPurchase, 2027), 239.99m) with { RenewalPriceAmount = 229.99m },
                Pass(date, PassOffer.Membership("Silver", PassPriceType.MonthlyRate), null),
                Pass(date, PassOffer.Membership("Gold", PassPriceType.MonthlyRate), 9.99m, qualifier: "Auto-renews after 12 months") with { InitiationFeeAmount = 24.99m },
                Pass(date, PassOffer.Membership("Prestige", PassPriceType.MonthlyRate), 21.99m, qualifier: "Auto-renews after 12 months") with { InitiationFeeAmount = 24.99m },
                P(date, "All Season Dining", SourceCategory.AddOns, 159.99m, PriceKind.StandardCatalog),
            ];
            List<AssociateProductCategoryStatus> statuses = partialFailure
                ? [new(SourceCategory.DatedFastLane, AssociateVerificationState.Stale, Verified.AddDays(-3), "Could not reach the official Fast Lane page this morning.")]
                : [];
            var draftWithSections = draft with { };
            return new AssociateWorkspace(date, draftWithSections, Hours(), products, statuses, AssociateSaveStatus.NeverSaved,
                EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(9, 0), ["Fury 325", "Copperhead Strike"], null)) { ShowSchedule = Shows(date) };
        };
    }

    public static void ApplyBusyDay(FakeOperationsDashboardService service)
    {
        service.SnapshotFactory = date =>
        {
            var categories = new[] { SourceCategory.ParkHours, SourceCategory.DatedAdmissionTickets, SourceCategory.DatedFastLane, SourceCategory.HauntedAttractions, SourceCategory.SeasonPasses, SourceCategory.AddOns, SourceCategory.Promotions };
            var sources = categories.Select(c => c switch
            {
                SourceCategory.DatedFastLane => new DashboardSourceStatusEntry(c, DashboardSourceStatus.Unavailable, Verified.AddDays(-1), Verified),
                SourceCategory.HauntedAttractions => new DashboardSourceStatusEntry(c, DashboardSourceStatus.NotApplicable, null, Verified),
                SourceCategory.Promotions => new DashboardSourceStatusEntry(c, DashboardSourceStatus.Stale, Verified.AddDays(-3), Verified),
                _ => new DashboardSourceStatusEntry(c, DashboardSourceStatus.Current, Verified, Verified),
            }).ToList();
            var health = new DashboardDataHealthSummary(DashboardOverallDataStatus.SomeUnavailable, CurrentCount: 4, StaleCount: 1, UnavailableCount: 1, VerificationIssueCount: 0, NotPublishedCount: 0, NotCheckedCount: 0, sources, Verified, NotApplicableCount: 1);
            var opNotes = new DashboardOperationalNotesSummary(3, 1,
            [
                new(Guid.NewGuid(), "Synthetic: coaster queue extended to Plaza", OperationalNoteCategory.General, OperationalNotePriority.Important, date, date),
                new(Guid.NewGuid(), "Gate 3 turnstile maintenance", OperationalNoteCategory.General, OperationalNotePriority.Normal, date.AddDays(-2), date.AddDays(3)),
                new(Guid.NewGuid(), "Season pass processing at Guest Services", OperationalNoteCategory.General, OperationalNotePriority.Normal, date, null),
            ], false);
            return new OperationsDashboardSnapshot(
                date,
                new DashboardParkStatusSummary(DashboardParkStatus.Open, Hours()),
                new DashboardPassdownStatusSummary(DashboardPassdownStatus.DraftInProgress, Verified.AddHours(2), null),
                ProjectedAttendance: 38500,
                PreSolds: 21240,
                EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(9, 0), ["Fury 325", "Copperhead Strike"], null),
                DailyFeatureType: DailyFeatureType.QuestionOfTheDay,
                DailyFeatureText: @"If Fury drops \(325\) ft, how long is the free fall? \(t = \sqrt{\frac{2h}{g}}\)",
                Notes: new DashboardNotesSummary(true, ["Tidal Wave closed until 2 PM for testing.", "Extra staffing at Gate 1 after 6 PM."], 2),
                ActiveOperationalNotes: opNotes,
                DataHealth: health,
                LatestFinalized: null,
                GeneratedAt: Verified.AddHours(3)) { TodayShows = Shows(date), Admission = Phase22CDashboardFixtures.ExactAdmission(date, Verified) };
        };
    }
}

// Synthetic, non-production fixtures (Phase 22E Calendar / Notes / History; extended in Phase 22F).
internal static partial class Fixtures
{
    public static readonly DateOnly Today = new(2026, 10, 17);
    public static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero); // 11:00 AM ET
    public static readonly DateTimeOffset Checked = new(2026, 10, 17, 14, 32, 0, TimeSpan.Zero);

    public static readonly DateOnly Busy = new(2026, 10, 24);
    public static readonly DateOnly Other = new(2026, 10, 25);
    public static readonly DateOnly NoShows = new(2026, 10, 22);
    public static readonly DateOnly Error = new(2026, 10, 23);
    public static readonly DateOnly NotPublished = new(2026, 10, 29);
    public static readonly DateOnly Stale = new(2026, 10, 31);
    public static readonly DateOnly Past = new(2026, 10, 10);

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    public static readonly ScheduledShow[] BusyShows =
    [
        new("r1", "Synthetic Resurgence", "Plaza Stage", [T(18, 30)]),
        new("r2", "Synthetic Wake the Night", "Carowinds Theatre", [T(19), T(20), T(21, 30), T(22, 30)]),
        new("r3", "Synthetic Monster Stomp Revue Featuring the Entire Midway Ensemble", "Midway Stage", [T(12), T(14), T(16), T(18), T(20)]),
        new("r4", "Synthetic Drumline", null, [T(13, 15), T(15, 15), T(17, 15)]),
        new("r5", "Synthetic Street Creatures", "Harmony Hall", [T(19, 30), T(20, 30), T(21, 30), T(22, 30), T(23, 30)]),
        new("r6", "Synthetic Late Night Howl", "County Fair Stage", [T(21), T(22, 15), T(23, 30), T(0, 30)]),
    ];

    public static readonly ScheduledShow[] OtherShows =
    [
        new("o1", "Synthetic Sunday Brass", "Plaza Stage", [T(13), T(15)]),
        new("o2", "Synthetic Fall Harvest Revue", "Carowinds Theatre", [T(14, 30), T(16, 30), T(18, 30)]),
    ];

    private static AssociateVerificationInfo Ok => new(AssociateVerificationState.Verified, Checked, null, null);

    private static AssociateParkHoursView Open(int open, int close) => new(
        ParkHoursCalculator.Calculate(false, T(open), T(close), false, null, null, false, null, null),
        Ok, OfficialIsClosed: false, OfficialOpening: T(open), OfficialClosing: T(close));

    private static ShowScheduleView ShowsFor(DateOnly date) => date switch
    {
        _ when date == Busy => new(date, ShowScheduleState.Scheduled, BusyShows, false, Checked, Checked),
        _ when date == Other => new(date, ShowScheduleState.Scheduled, OtherShows, false, Checked, Checked),
        _ when date == Stale => new(date, ShowScheduleState.Scheduled, OtherShows, true, Checked.AddDays(-2), Checked),
        _ when date == NoShows => new(date, ShowScheduleState.NoShows, [], false, Checked, Checked),
        _ when date == Error => new(date, ShowScheduleState.CouldNotCheck, [], true, null, Checked),
        _ when date == NotPublished => new(date, ShowScheduleState.NotPublished, [], false, Checked, Checked),
        _ when date < Today => new(date, ShowScheduleState.HistoricalNotAvailable, [], false, null, null),
        _ => ShowScheduleView.NotCheckedFor(date),
    };

    public static readonly DateOnly WinterFest = new(2026, 12, 5);
    public static readonly DateOnly NewYearsEve = new(2026, 12, 31);
    public static readonly DateOnly Historical = new(2026, 9, 12);
    public static readonly DateOnly FutureNotPublished = new(2027, 3, 13);

    private static AssociateParkHoursView Stamped(int open, int close, AssociateVerificationInfo info) => new(
        ParkHoursCalculator.Calculate(false, T(open), T(close), false, null, null, false, null, null),
        info, OfficialIsClosed: false, OfficialOpening: T(open), OfficialClosing: T(close));

    private static AssociateParkHoursView ClosedHours => new(
        ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null),
        new AssociateVerificationInfo(AssociateVerificationState.Closed, Checked, null, null), OfficialIsClosed: true, OfficialOpening: null, OfficialClosing: null);

    private static AssociateParkHoursView NotPublishedHours => new(
        ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
        new AssociateVerificationInfo(AssociateVerificationState.NotPublished, Checked, null, null), OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null);

    /// <summary>
    /// Synthetic operating calendar: past weekends open 10–6 (recorded history), October weekends 6 PM–midnight,
    /// WinterFest weekends and Dec 21–31 noon–9 PM, New Year's Eve 3 PM–1 AM, Jan 1–2 and everything from
    /// February 2027 on not published, every other date verified closed. Oct 31 is open with stale data.
    /// </summary>
    private static (OperationsCalendarOperatingState State, AssociateParkHoursView Hours) DayState(DateOnly d)
    {
        var weekend = d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
        var open = OperationsCalendarOperatingState.Open;
        if (d == Stale) return (open, Stamped(18, 0, new AssociateVerificationInfo(AssociateVerificationState.Stale, Checked.AddDays(-3), TimeSpan.FromDays(3), null)));
        if (d == NoShows || d == Error) return (open, Open(18, 0));
        if (d == NotPublished || d >= new DateOnly(2027, 2, 1) || d == new DateOnly(2027, 1, 1) || d == new DateOnly(2027, 1, 2)) return (OperationsCalendarOperatingState.NotPublished, NotPublishedHours);
        if (d == NewYearsEve) return (open, Stamped(15, 1, Ok));
        if (d.Month == 12 && d.Year == 2026 && (weekend || d.Day >= 21)) return (open, Stamped(12, 21, Ok));
        if (d < Today) return weekend ? (open, Stamped(10, 18, Ok)) : (OperationsCalendarOperatingState.Closed, ClosedHours);
        return weekend ? (open, Open(18, 0)) : (OperationsCalendarOperatingState.Closed, ClosedHours);
    }

    public static void ApplyCalendar(FakeOperationsCalendarService service)
    {
        service.SnapshotFactory = (month, selected) =>
        {
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var gridStart = month.AddDays(-(int)month.DayOfWeek);
            var gridEnd = monthEnd.AddDays(6 - (int)monthEnd.DayOfWeek);
            var days = new List<OperationsCalendarDay>();
            for (var d = gridStart; d <= gridEnd; d = d.AddDays(1))
            {
                var (state, hours) = DayState(d);
                var isOpen = state == OperationsCalendarOperatingState.Open;
                days.Add(new OperationsCalendarDay(d, d.Month == month.Month, d == Today, d < Today && isOpen, d == Today, isOpen && d >= Today.AddDays(-7) && d <= Today.AddDays(7) ? 1 : 0,
                    state, hours, ImportantOperationalNoteCount: d == Today ? 1 : 0));
            }

            var (selectedState, selectedHours) = DayState(selected);
            var selectedOpen = selectedState == OperationsCalendarOperatingState.Open;
            var summary = new OperationsCalendarSelectedDateSummary(selected, selected == Today,
                selected < Today && selectedOpen ? OperationsCalendarPassdownStatus.Finalized : selected == Today ? OperationsCalendarPassdownStatus.DraftInProgress : OperationsCalendarPassdownStatus.NoDraft,
                selected == Today ? Now.AddHours(-1) : null,
                selected < Today && selectedOpen ? new DateTimeOffset(selected.AddDays(1).ToDateTime(T(10, 2)), TimeSpan.Zero) : null,
                selected == Today ? 3 : 1, selectedState, selectedHours, selected == Today ? 1 : 0,
                selected < Today
                    ? null
                    : selectedOpen
                        ? new OperationsCalendarDailyAdmissionPrice(OperationsCalendarDailyAdmissionStatus.Available, "Park Ticket", 49.99m, PriceKind.ExactDated, null)
                        : null)
            {
                Shows = ShowsFor(selected),
            };
            return new OperationsCalendarSnapshot(month, Today, selected, days, summary, Now);
        };
    }

    public static void SeedNotes(FakeOperationalNotesService service)
    {
        OperationalNote N(string title, OperationalNoteCategory c, OperationalNotePriority p, int from, int? through, string? body = null, bool resolved = false) =>
            new(Guid.NewGuid(), title, body, c, p, Today.AddDays(from), through is { } t ? Today.AddDays(t) : null,
                resolved ? OperationalNoteStatus.Resolved : OperationalNoteStatus.Active,
                Now.AddDays(-5 + from), Now.AddHours(-3 + from), resolved ? Now.AddHours(-1) : null);

        var categories = Enum.GetValues<OperationalNoteCategory>();
        OperationalNoteCategory Cat(int i) => categories[i % categories.Length];
        service.Seed(
            N("Synthetic: South Gate scanner #3 out of service", Cat(0), OperationalNotePriority.Important, -2, 3, "Use scanners 1, 2 and 4."),
            N("Synthetic: Coaster queue extended toward Plaza", Cat(1), OperationalNotePriority.Normal, -1, null),
            N("Synthetic: Season pass processing moved to Guest Services window 4", Cat(2), OperationalNotePriority.Normal, 0, 0),
            N("Synthetic: Extra staffing at Gate 1 after 6 PM", Cat(3), OperationalNotePriority.Important, 0, 7),
            N("Synthetic: Stroller rental relocated for SCarowinds", Cat(0), OperationalNotePriority.Normal, -10, 20),
            N("Synthetic: Parking lot C closed for paving", Cat(1), OperationalNotePriority.Normal, -3, 1),
            N("Synthetic: Radio channel 6 static near the waterpark", Cat(2), OperationalNotePriority.Normal, -4, null),
            N("Synthetic: Locker kiosk firmware update", Cat(3), OperationalNotePriority.Normal, 2, 4));
    }

    private static FinalizedPassdownSnapshot Snapshot(DateOnly date) => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion, date, date.ToString("dddd, MMMM d, yyyy"),
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "6:00 PM - 12:00 AM", "Open", "6:00 PM", "6:15 PM", "Verified", null),
        new FinalizedAttendanceSnapshot(38500, 21240),
        new FinalizedEarlyRideTimeSnapshot(false, null, []),
        new FinalizedDailyFeatureSnapshot("Question of the Day", "Synthetic: how tall is the tallest coaster?"),
        ["Synthetic: South Gate scanner #3 out of service."],
        [
            new FinalizedProductCategorySnapshot("Daily Admission", FinalizedProductSectionTier.KeySelling,
            [
                new FinalizedProductRowSnapshot("Park Ticket", "$49.99", null, null, null),
                new FinalizedProductRowSnapshot("Military / First Responder", "From $40.00", null, null, null),
            ]),
            new FinalizedProductCategorySnapshot("Fast Lane", FinalizedProductSectionTier.KeySelling,
                [new FinalizedProductRowSnapshot("Fast Lane", "$65.00", null, null, null)]),
        ],
        false, date.AddDays(1).ToString("M/d/yyyy") + " 6:00 AM")
    {
        Shows = [new FinalizedShowSnapshot("Synthetic Wake the Night", "Carowinds Theatre", "7:00, 8:00, 9:30, 10:30 PM")],
    };

    public static void SeedHistory(FakePassdownHistoryService service)
    {
        for (var i = 1; i <= 9; i++)
        {
            var date = Today.AddDays(-i);
            var summary = new FinalizedPassdownSummary(date, new DateTimeOffset(date.AddDays(1).ToDateTime(T(10, 2)), TimeSpan.Zero),
                i == 4 ? FinalizationOrigin.Manual : FinalizationOrigin.Scheduled, i != 6, i != 6 ? 182_000 : null);
            service.Summaries.Add(summary);
            service.Details[date] = new PassdownHistoryDetail(summary, i == 2 ? Snapshot(date) with { OperationalNotes = [] } : Snapshot(date), null);
        }
    }
}

// Phase 22F: Settings (Personal Sync) and Season Price History fixtures.
internal static partial class Fixtures
{
    public const string GoldPassKey = "syn|SeasonPasses|Gold Season Pass";

    public static (FakePersonalSyncSettingsService Settings, FakePersonalSyncCoordinator Coordinator, FakePersonalSyncConflictResolutionService Conflicts) Sync(SyncFixture kind)
    {
        var deviceId = new Guid("3f2a9c1e-5b7d-4e8a-9c6f-1d2e3a4b5c6d");
        const string root = @"C:\Users\associate\OneDrive - Personal\Carowinds Operations Sync";
        var settings = new FakePersonalSyncSettingsService { DeviceId = deviceId, SuggestedRoots = [@"C:\Users\associate\OneDrive - Personal"] };
        var coordinator = new FakePersonalSyncCoordinator();
        var conflicts = new FakePersonalSyncConflictResolutionService();
        var lastSync = Now.AddMinutes(-12);
        switch (kind)
        {
            case SyncFixture.UpToDate:
                settings.PersonalSyncEnabled = true; settings.SyncRootPath = root; settings.Status = PersonalSyncStatus.Ready;
                coordinator.CurrentStatus = PersonalSyncAutomationSnapshot.Initial with { State = PersonalSyncAutomationState.UpToDate, LastSuccessfulSyncUtc = lastSync, LastAttemptUtc = lastSync };
                break;
            case SyncFixture.Conflict:
                settings.PersonalSyncEnabled = true; settings.SyncRootPath = root; settings.Status = PersonalSyncStatus.Ready;
                coordinator.CurrentStatus = PersonalSyncAutomationSnapshot.Initial with { State = PersonalSyncAutomationState.ConflictAttention, ConflictCount = 2, LastSuccessfulSyncUtc = lastSync, LastAttemptUtc = Now };
                conflicts.NextConflicts =
                [
                    new SyncConflictSummary(SyncEntityType.AssociateDraft, "2026-10-17", "Passdown draft — Saturday, October 17, 2026", false, false, Guid.NewGuid(), 4, Now.AddMinutes(-30), Now.AddMinutes(-12), false),
                    new SyncConflictSummary(SyncEntityType.OperationalNote, Guid.NewGuid().ToString(), "Operational note — Synthetic: South Gate scanner #3 out of service", false, false, Guid.NewGuid(), 2, Now.AddHours(-2), Now.AddMinutes(-12), false),
                ];
                break;
            case SyncFixture.Error:
                settings.PersonalSyncEnabled = true; settings.SyncRootPath = root; settings.Status = PersonalSyncStatus.FolderUnavailable;
                settings.StatusDetail = "The sync folder can't be reached. Work is saved on this computer and will sync once the folder is back.";
                coordinator.CurrentStatus = PersonalSyncAutomationSnapshot.Initial with { State = PersonalSyncAutomationState.Error, LastSuccessfulSyncUtc = Now.AddDays(-1), LastAttemptUtc = Now, LastErrorMessage = "Couldn't write to the sync folder (access denied)." };
                break;
        }

        return (settings, coordinator, conflicts);
    }

    public static void SeedPriceHistory(FakeSeasonPriceHistoryService service)
    {
        service.Products.Add(new PriceHistoryProduct(GoldPassKey, "Gold Season Pass", PriceHistoryFamily.SeasonPass, true, 3));
        service.Products.Add(new PriceHistoryProduct("syn|SeasonPasses|Prestige Season Pass", "Prestige Season Pass", PriceHistoryFamily.SeasonPass, true, 1));
        service.Products.Add(new PriceHistoryProduct("syn|AddOns|All Season Dining", "All Season Dining", PriceHistoryFamily.SeasonPassAddOn, true, 2));
        SeasonPriceHistoryInterval I(string key, int seq, decimal amount, PriceKind kind, string name, int fromDay, int toDay) =>
            new(key, seq, amount, kind, null, null, name, "Official season passes page", Now.AddDays(fromDay), Now.AddDays(toDay), SeasonPriceHistoryOrigin.Observed);
        service.Intervals[GoldPassKey] =
        [
            I(GoldPassKey, 1, 109.99m, PriceKind.StandardCatalog, "Gold Season Pass", -60, -31),
            I(GoldPassKey, 2, 99.99m, PriceKind.Sale, "Gold Season Pass", -30, -8),
            I(GoldPassKey, 3, 129.99m, PriceKind.StandardCatalog, "Gold Season Pass", -7, 0),
        ];
    }
}
