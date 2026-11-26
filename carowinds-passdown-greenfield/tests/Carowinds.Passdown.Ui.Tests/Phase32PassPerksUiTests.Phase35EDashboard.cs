using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35E Dashboard: the Dashboard's Pass Perks card keeps the Phase 32 semantics — qualifying, booking
/// and redemption dates are separate labelled lines, expired perks are left to Products, an unverified month
/// is "not verified" (never "no perks"), and nothing says an individual guest has earned a perk.
/// </summary>
public partial class Phase32PassPerksUiTests
{
    private static readonly DateOnly DashboardDay = new(2026, 11, 22);

    [Fact]
    public void Dashboard_PassPerks_KeepQualifyingAndRedemptionDatesSeparate_TodayInWords()
    {
        var card = DashboardPassPerksPresentation.Build(Overview(DashboardDay), loadFailed: false, DashboardDay);

        Assert.Equal("November Pass Perks", card.Heading);
        var bring = card.Items[0];
        Assert.Equal("Earn 2 FREE Bring-A-Friend Tickets", bring.Title);
        Assert.Equal(
            [("Last qualifying visit", "Today, November 22"), ("Redemption period", "Dec 5 – Jan 3")],
            bring.Dates.Select(d => (d.Label, d.Value)));
        Assert.Contains(DashboardPassPerksPresentation.PublicListingNote.TrimEnd('.'), bring.AccessibleSummary);
        Assert.DoesNotContain("you have", bring.AccessibleSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_PassPerks_BookingDeadline_AndAMonthLongPerk_HaveTheirOwnLabels()
    {
        Assert.Equal([("Book by", "Nov 30")], DashboardItem(Camp).Dates.Select(d => (d.Label, d.Value)));
        Assert.Equal([("Redemption period", "All November")], DashboardItem(Pretzel).Dates.Select(d => (d.Label, d.Value)));
        Assert.Equal("Upcoming", DashboardItem(Mug).StatusLabel);
    }

    [Fact]
    public void Dashboard_PassPerks_AreBounded_AndSayHowManyMoreProductsHolds()
    {
        var card = DashboardPassPerksPresentation.Build(Overview(DashboardDay), loadFailed: false, DashboardDay);

        Assert.Equal(DashboardPassPerksPresentation.MaxItems, card.Items.Count);
        Assert.Equal("2 more in Products", card.MoreText);
        Assert.Null(card.EmptyText);
        Assert.False(card.IsStatusWarning);
        Assert.Equal(PassPerksPresentation.Status(CheckedStatus).Text, card.StatusText);
    }

    [Fact]
    public void Dashboard_PassPerks_LeaveExpiredPerksToProducts()
    {
        // Jan 10: every November perk has ended (redemption/booking past) — none is shown as current.
        var day = new DateOnly(2027, 1, 10);
        var card = DashboardPassPerksPresentation.Build(Overview(day), loadFailed: false, day);

        Assert.Empty(card.Items);
        Assert.Equal("January's Pass Perks have not been verified.", card.EmptyText); // never "no perks"
    }

    [Fact]
    public void Dashboard_PassPerks_NotChecked_ReadFailure_AndAFailedCheck_AreSaidPlainly()
    {
        var notChecked = DashboardPassPerksPresentation.Build(null, loadFailed: false, DashboardDay);
        Assert.Equal(PassPerksPresentation.NotCheckedText, notChecked.EmptyText);
        Assert.Empty(notChecked.Items);

        var failed = DashboardPassPerksPresentation.Build(null, loadFailed: true, DashboardDay);
        Assert.Equal("Saved Pass Perks could not be read.", failed.EmptyText);
        Assert.True(failed.IsStatusWarning);

        var lastCheckFailed = new OfficialDocumentStatus(OfficialDocumentFreshness.LastCheckFailed, Checked, Checked.AddHours(30), "timeout", ParkPage);
        var stale = DashboardPassPerksPresentation.Build(Overview(DashboardDay, lastCheckFailed), loadFailed: false, DashboardDay);
        Assert.True(stale.IsStatusWarning);
        Assert.StartsWith("Unable to refresh; showing previously verified rewards", stale.StatusText);
        Assert.NotEmpty(stale.Items); // last-known-good perks stay visible
    }

    private static DashboardPassPerkPresentation DashboardItem(OfficialPassPerk perk) =>
        DashboardPassPerksPresentation.Item(
            new PassPerkEntry(perk, PassPerkStatusEvaluator.Evaluate(perk, DashboardDay, new DateTimeOffset(DashboardDay.ToDateTime(new TimeOnly(9, 0)), Est))),
            Terms,
            DashboardDay);
}
