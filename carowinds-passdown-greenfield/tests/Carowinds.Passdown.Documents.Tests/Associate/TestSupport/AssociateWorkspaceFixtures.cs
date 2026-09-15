using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

/// <summary>
/// Hand-built <see cref="AssociateWorkspace"/> fixtures for Documents.Tests (spec Phase 4E Part K's
/// sample-day set), independent of any real repository/refresh plumbing. Every fixture pins
/// <see cref="PassdownSectionSelection.AllIncluded"/> explicitly (Phase 17D §6 changed
/// <see cref="AssociateDraftPayload.CreateDefault"/>'s own default to a partial selection) — these
/// fixtures exist to exercise PDF/grouping/pricing behavior across every section, independent of
/// whatever the production "new draft" default happens to be.
/// </summary>
internal static class AssociateWorkspaceFixtures
{
    private static readonly DateOnly Date = new(2026, 8, 21);

    /// <summary>The absolute minimum a workspace can carry — no draft entries, no products, hours never checked.</summary>
    public static AssociateWorkspace MinimalValid() => new(
        Date,
        AssociateDraftPayload.CreateDefault() with { SectionSelection = PassdownSectionSelection.AllIncluded },
        new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
            AssociateVerificationInfo.NotCheckedYet,
            OfficialIsClosed: false,
            OfficialOpening: null,
            OfficialClosing: null),
        Products: [],
        CategoryStatuses: [],
        AssociateSaveStatus.NeverSaved);

    /// <summary>A quiet day: hours known, a couple of products, nothing else filled in.</summary>
    public static AssociateWorkspace SparseDay()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(19, 0), false, null, null, false, null, null);
        var verification = Verified();

        var draft = AssociateDraftPayload.CreateDefault() with { SectionSelection = PassdownSectionSelection.AllIncluded };

        var products = new List<AssociateProduct>
        {
            Product("dated|DatedAdmissionTickets|1day", "1-Day Ticket", SourceCategory.DatedAdmissionTickets, 54.99m, verification: verification),
        };

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, verification, false, new TimeOnly(10, 0), new TimeOnly(19, 0)),
            products, [], AssociateSaveStatus.NeverSaved);
    }

    /// <summary>A typical, fully filled-in day (spec §11.5's "Typical associate day") — expected to render as exactly one Letter page.</summary>
    public static AssociateWorkspace NormalDay()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(21, 0), false, null, null, false, null, null);
        var verification = Verified();

        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProjectedAttendance = 18500,
            PreSolds = 4200,
            OperationalNotes =
            [
                "Fury 325 closed for scheduled maintenance until 11 AM.",
                "Extra staffing at North Gate for a school group arriving at 9:15 AM.",
                "Guest Services relocated temporarily to the Carolina Harbor breezeway.",
            ],
            DailyFeatureType = DailyFeatureType.QuestionOfTheDay,
            DailyFeatureText = "What year did Carowinds open?",
            CustomProducts =
            [
                new CustomProductEntry("c1", "Team Member Meal Voucher", 8.00m, "Cafeteria only, associates present ID.", true),
            ],
            SectionSelection = PassdownSectionSelection.AllIncluded,
        };

        var products = new List<AssociateProduct>
        {
            Product("dated|DatedAdmissionTickets|1day", "1-Day Ticket", SourceCategory.DatedAdmissionTickets, 64.99m, comparison: 79.99m, verification: verification),
            Product("dated|DatedAdmissionTickets|2day", "2-Day Ticket", SourceCategory.DatedAdmissionTickets, 89.99m, verification: verification),
            Product("dated|DatedFastLane|regular", "Fast Lane", SourceCategory.DatedFastLane, 39.99m, verification: verification),
            Product("dated|DatedFastLane|plus", "Fast Lane Plus", SourceCategory.DatedFastLane, 64.99m, verification: verification),
            Product("catalog|season-pass|gold", "Gold Season Pass", SourceCategory.SeasonPasses, 129.99m, qualifier: "Payment plan available", verification: verification),
            Product("catalog|addon|parking", "Preferred Parking", SourceCategory.AddOns, 25.00m, verification: verification),
        };

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, verification, false, new TimeOnly(9, 0), new TimeOnly(21, 0)),
            products, [], new AssociateSaveStatus(AssociateSaveState.Saved, DateTimeOffset.UtcNow),
            SelectedOperationalNoteSourceStates: null,
            EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(8, 30), ["Fury 325", "Intimidator", "Copperhead Strike"], "For pass members with the benefit."));
    }

    /// <summary>Heavy-product day (spec §11.5) — many included products across every category, but still expected to fit one page.</summary>
    public static AssociateWorkspace DenseDay()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var verification = Verified();

        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProjectedAttendance = 26000,
            PreSolds = 9100,
            OperationalNotes =
            [
                "Peak-day staffing plan in effect at all gates.",
                "Season pass processing tent open at the front plaza.",
            ],
            DailyFeatureType = DailyFeatureType.UselessFactOfTheDay,
            DailyFeatureText = "Fury 325 is one of the tallest and fastest giga coasters in the world.",
            SectionSelection = PassdownSectionSelection.AllIncluded,
        };

        var products = new List<AssociateProduct>();
        products.AddRange(CategoryRun(SourceCategory.DatedAdmissionTickets, "Ticket", 6, 44.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.DatedFastLane, "Fast Lane", 4, 34.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.SeasonPasses, "Season Pass", 5, 99.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.AddOns, "Add-On", 6, 14.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.Promotions, "Promotion", 3, 19.99m, verification));

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, verification, false, new TimeOnly(9, 0), new TimeOnly(22, 0)),
            products, [], new AssociateSaveStatus(AssociateSaveState.Saved, DateTimeOffset.UtcNow),
            SelectedOperationalNoteSourceStates: null,
            EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(8, 30), ["Fury 325", "Intimidator"], null));
    }

    /// <summary>Private-buyout day (spec §11.5): officially closed, but open under a private-event override with its own hours and details.</summary>
    public static AssociateWorkspace PrivateBuyoutDay()
    {
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(18, 0), new TimeOnly(23, 0), "Corporate buyout - Acme Corp. Gates open to badge holders only."),
            ProjectedAttendance = 5000,
            OperationalNotes = ["Public not admitted. All guests must present an Acme Corp event badge at the gate."],
            SectionSelection = PassdownSectionSelection.AllIncluded,
        };

        var effective = ParkHoursCalculator.Calculate(true, null, null, false, null, null, true, new TimeOnly(18, 0), new TimeOnly(23, 0));
        var verification = new AssociateVerificationInfo(AssociateVerificationState.Closed, DateTimeOffset.UtcNow.AddHours(-2), null, null);

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, verification, true, null, null),
            Products: [], CategoryStatuses: [], new AssociateSaveStatus(AssociateSaveState.Saved, DateTimeOffset.UtcNow));
    }

    /// <summary>Stale/not-fully-verified data day: official hours and one product are showing last-known-good data from a failed recheck.</summary>
    public static AssociateWorkspace StaleDataDay()
    {
        var lastVerifiedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(20, 0), false, null, null, false, null, null);
        var staleVerification = new AssociateVerificationInfo(AssociateVerificationState.Stale, lastVerifiedAt, TimeSpan.FromDays(3), "Official site did not respond.");

        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProjectedAttendance = 12000,
            SectionSelection = PassdownSectionSelection.AllIncluded,
        };

        var products = new List<AssociateProduct>
        {
            Product("dated|DatedAdmissionTickets|1day", "1-Day Ticket", SourceCategory.DatedAdmissionTickets, 54.99m, verification: staleVerification),
        };

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, staleVerification, false, new TimeOnly(10, 0), new TimeOnly(20, 0)),
            products, [], AssociateSaveStatus.NeverSaved);
    }

    /// <summary>Deliberately exceeds one page's estimated capacity (spec §11.4's "unusually busy days") so overflow behavior can be exercised end to end.</summary>
    public static AssociateWorkspace ExcessiveDay()
    {
        var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        var verification = Verified();

        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProjectedAttendance = 30000,
            PreSolds = 15000,
            OperationalNotes = Enumerable.Range(1, 6).Select(i => $"Operational note number {i} describing a staffing or guest-service situation for the day in enough detail to take up a full line.").ToList(),
            DailyFeatureType = DailyFeatureType.QuoteOfTheDay,
            DailyFeatureText = "\"The best way to predict the future is to create it.\"",
            SectionSelection = PassdownSectionSelection.AllIncluded,
        };

        var products = new List<AssociateProduct>();
        products.AddRange(CategoryRun(SourceCategory.DatedAdmissionTickets, "Ticket", 10, 44.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.DatedFastLane, "Fast Lane", 8, 34.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.SeasonPasses, "Season Pass", 8, 99.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.AddOns, "Add-On", 8, 14.99m, verification));
        products.AddRange(CategoryRun(SourceCategory.Promotions, "Promotion", 6, 19.99m, verification));

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, verification, false, new TimeOnly(9, 0), new TimeOnly(22, 0)),
            products, [], new AssociateSaveStatus(AssociateSaveState.Saved, DateTimeOffset.UtcNow),
            SelectedOperationalNoteSourceStates: null,
            EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(8, 0), ["Fury 325", "Intimidator", "Copperhead Strike", "Afterburn", "Nighthawk"], "For all pass members and Fast Lane Plus guests."));
    }

    private static AssociateVerificationInfo Verified() => new(AssociateVerificationState.Verified, DateTimeOffset.UtcNow.AddMinutes(-30), null, null);

    private static IEnumerable<AssociateProduct> CategoryRun(SourceCategory category, string label, int count, decimal basePrice, AssociateVerificationInfo verification) =>
        Enumerable.Range(1, count).Select(i => Product($"dated|{category}|{label}-{i}", $"{label} {i}", category, basePrice + i, verification: verification));

    private static AssociateProduct Product(
        string identity,
        string displayName,
        SourceCategory category,
        decimal price,
        decimal? comparison = null,
        string? qualifier = null,
        AssociateVerificationInfo? verification = null,
        bool includeOnPdf = true,
        decimal? displayPriceOverride = null) => new(
        identity,
        displayName,
        category,
        IsCustom: false,
        price,
        OfficialDisplayPrice: null,
        comparison,
        qualifier,
        DateSpecific: true,
        ApplicableDate: Date,
        verification ?? Verified(),
        includeOnPdf,
        displayPriceOverride);
}
