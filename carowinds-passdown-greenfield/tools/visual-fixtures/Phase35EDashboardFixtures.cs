using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

// Synthetic, non-production fixtures (Phase 35E Dashboard follow-up). Every product name starts with
// "Synthetic" and every price is invented; Pass Perks come from Phase32Fixtures (synthetic perks).
internal enum DashboardVariant
{
    /// <summary>The busy day plus today's dated price list and the month's Pass Perks.</summary>
    Typical,

    /// <summary>No draft yet, hours/prices/shows not checked, no Pass Perks checked, no notes.</summary>
    NothingYet,

    /// <summary>Hours and prices could not be rechecked (stale), the latest show check failed, Pass Perks read failed.</summary>
    Stale,

    /// <summary>Long official messages and names: overflow and wrapping.</summary>
    LongText,
}

internal static class Phase35EDashboardFixtures
{
    private static readonly DateTimeOffset Verified = BusyDayFixtures.Verified;

    public static void Apply(FakeOperationsDashboardService service, DashboardVariant variant)
    {
        var busy = service.SnapshotFactory!;
        var perks = Phase32Fixtures.Service().GetOverviewAsync().GetAwaiter().GetResult();
        service.SnapshotFactory = date =>
        {
            // The synthetic Pass Perks are for Oct 17, 2026 (the harness's Eastern "today"), so the Dashboard shows that date.
            date = perks.OperatingDate;
            var snapshot = busy(date);
            var pricing = Pricing(date, stale: variant == DashboardVariant.Stale, longNames: variant == DashboardVariant.LongText);
            return variant switch
            {
                DashboardVariant.NothingYet => snapshot with
                {
                    ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.NotChecked, NotCheckedHours()),
                    PassdownStatus = new DashboardPassdownStatusSummary(DashboardPassdownStatus.NoDraft, null, null),
                    ProjectedAttendance = null,
                    PreSolds = null,
                    DailyFeatureType = null,
                    DailyFeatureText = null,
                    Notes = DashboardNotesSummary.Empty,
                    ActiveOperationalNotes = DashboardOperationalNotesSummary.Empty,
                    TodayShows = ShowScheduleView.NotCheckedFor(date),
                    Admission = DashboardAdmissionSummary.NotChecked,
                    Pricing = DashboardPricingSummary.Empty,
                    PassPerks = null,
                },
                DashboardVariant.Stale => snapshot with
                {
                    ParkStatus = new DashboardParkStatusSummary(DashboardParkStatus.Open, StaleHours()),
                    TodayShows = snapshot.TodayShows with { LastCheckFailed = true },
                    Pricing = pricing,
                    PassPerks = null,
                    PassPerksLoadFailed = true,
                },
                DashboardVariant.LongText => snapshot with
                {
                    Pricing = pricing,
                    PassPerks = perks.OperatingDate == date ? perks : null,
                    PassdownStatus = new DashboardPassdownStatusSummary(DashboardPassdownStatus.Finalized, Verified.AddHours(2), Verified.AddHours(20)),
                },
                _ => snapshot with { Pricing = pricing, PassPerks = perks.OperatingDate == date ? perks : null },
            };
        };
    }

    private static DashboardPricingSummary Pricing(DateOnly date, bool stale, bool longNames)
    {
        var state = stale ? AssociateVerificationState.Stale : AssociateVerificationState.Verified;
        var items = new List<DashboardPriceItem>
        {
            new("Park Ticket", 64.99m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Verified, Verified),
            new(longNames ? "Synthetic Ticket + All-Day Meal, Snack & Souvenir Drink Bundle With A Long Official Name" : "Synthetic Ticket + Meal Bundle", 84.99m, ProductDisplaySection.DailyAdmission, state, Verified),
            new("Synthetic Military & First Responder Ticket", 49.99m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Verified, Verified),
            new("Synthetic Fast Lane", 70m, ProductDisplaySection.FastLane, AssociateVerificationState.Verified, Verified),
            new("Synthetic Fast Lane Plus", 95m, ProductDisplaySection.FastLane, AssociateVerificationState.Verified, Verified),
        };
        return new DashboardPricingSummary(items, longNames ? 7 : items.Count);
    }

    private static AssociateParkHoursView NotCheckedHours() => new(
        ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
        new AssociateVerificationInfo(AssociateVerificationState.NotCheckedYet, null, null, null), OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null);

    private static AssociateParkHoursView StaleHours() => new(
        ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null),
        new AssociateVerificationInfo(AssociateVerificationState.Stale, Verified.AddDays(-2), null, null), OfficialIsClosed: false, OfficialOpening: new TimeOnly(10, 0), OfficialClosing: new TimeOnly(22, 0));
}
