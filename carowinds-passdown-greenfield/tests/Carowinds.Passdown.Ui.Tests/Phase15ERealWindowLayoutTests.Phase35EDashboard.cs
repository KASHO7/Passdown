using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain.PassPerks;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35E Dashboard follow-up (run-alone real-window class): the approved Admissions Overview realized with
/// every card populated (synthetic price list and Pass Perks) — no control past the window edge, no card overlap,
/// no horizontal scrolling, Light and Dark, and a keyboard path through every action in reading order.
/// </summary>
public partial class Phase15ERealWindowLayoutTests
{
    private static OperationsDashboardSnapshot FullDashboard(DateOnly date)
    {
        var checkedAt = Phase22CDashboardFixtures.MorningCheckOf(date);
        var campaign = new PassPerkCampaign(date.Year, date.Month, "Synthetic Perks", YearInferred: true);
        var perk = PassPerkText.Interpret("Earn 2 FREE Synthetic Friend Tickets", null,
            [$"Visit twice between {date:MMMM} 1 and {date:MMMM} {date.Day} to receive 2 FREE Synthetic Friend Tickets, valid {date.AddMonths(1):MMMM} 5 through {date.AddMonths(2):MMMM} 3."],
            campaign, new Uri("https://www.sixflags.com/carowinds/perks-and-play"), checkedAt, isMonthlyListing: false);
        var document = PassPerksDocument.Empty.Merge(new PassPerksObservation(campaign, [perk]), checkedAt);
        var status = new OfficialDocumentStatus(OfficialDocumentFreshness.Current, checkedAt, checkedAt, null, null);

        return Phase22CDashboardFixtures.BusyDayWithPartialFailure(date) with
        {
            Pricing = new DashboardPricingSummary(
            [
                new("Park Ticket", 64.99m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Verified, checkedAt),
                new("Synthetic Ticket + All-Day Meal, Snack & Souvenir Drink Bundle With A Long Official Name", 84.99m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Stale, checkedAt),
                new("Synthetic Fast Lane", 70m, ProductDisplaySection.FastLane, AssociateVerificationState.Verified, checkedAt),
            ], 3),
            PassPerks = new PassPerksOverview(document, status, date, Phase22CDashboardFixtures.AfternoonOf(date)),
        };
    }

    private static (Window Window, OperationsDashboardViewModel ViewModel, FakeOperationsDashboardService Service) RealizeFullDashboard(double width, double height, ThemeVariant theme)
    {
        var service = new FakeOperationsDashboardService { SnapshotFactory = FullDashboard };
        Phase23BDashboardShowsFixtures.Apply(service, Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));
        var viewModel = new OperationsDashboardViewModel(service);
        var window = RealizeInWindow(new DashboardView { DataContext = viewModel }, width, height);
        viewModel.PendingLoad.GetAwaiter().GetResult();
        window.RequestedThemeVariant = theme;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, viewModel, service);
    }

    private static readonly string[] DashboardCardNames =
        ["Gate operations", "Passdown Status", "Today's shows", "Daily admission", "Active operational notes", "Pass Perks", "Data status"];

    [Theory]
    [InlineData(1920d, 1080d, "Light")]
    [InlineData(1536d, 960d, "Dark")]
    [InlineData(1280d, 720d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(700d, 640d, "Light")] // narrower than the app allows: the card columns stack
    public void Phase35E_Dashboard_EveryCardFits_NothingOverlaps_NoHorizontalScroll(double width, double height, string theme)
    {
        var (window, viewModel, _) = RealizeFullDashboard(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

        Assert.NotNull(viewModel.Presentation?.PassPerks);
        Assert.Equal(3, viewModel.Presentation!.Pricing.Rows.Count);
        AssertNoControlClipsHorizontally(window);

        var cards = DashboardCardNames
            .Select(name => (Name: name, Bounds: AbsoluteBounds(window.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == name), window)))
            .ToList();
        Assert.All(cards, card =>
        {
            Assert.True(card.Bounds.Height > 0, $"{card.Name} is not realized");
            Assert.True(card.Bounds.Right <= window.ClientSize.Width + 1.0, $"{card.Name} runs past the window edge");
        });
        for (var i = 0; i < cards.Count; i++)
        {
            for (var j = i + 1; j < cards.Count; j++)
            {
                var overlap = cards[i].Bounds.Intersect(cards[j].Bounds);
                Assert.False(overlap.Width > 1 && overlap.Height > 1, $"{cards[i].Name} overlaps {cards[j].Name}");
            }
        }

        var view = window.GetVisualDescendants().OfType<DashboardView>().Single();
        var expected = DashboardView.ModeForContentWidth(width - 48);
        Assert.Equal(expected, view.LayoutMode);
        var primary = AbsoluteBounds(window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DashboardPrimaryColumn"), window);
        var secondary = AbsoluteBounds(window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DashboardSecondaryColumn"), window);
        Assert.True(expected == DashboardLayoutMode.Narrow ? secondary.Top >= primary.Bottom - 0.5 : secondary.Left >= primary.Right + 15);

        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "DashboardScroller");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1.0, "no horizontal scrolling");
        window.Close();
    }

    [Fact]
    public void Phase35E_Dashboard_ResizingReflowsWithoutReading_AndBackAgain()
    {
        var (window, _, service) = RealizeFullDashboard(1920, 1080, ThemeVariant.Light);
        var view = window.GetVisualDescendants().OfType<DashboardView>().Single();
        var loads = service.LoadCalls.Count;
        Assert.Equal(DashboardLayoutMode.Wide, view.LayoutMode);

        window.Width = 960;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(DashboardLayoutMode.Medium, view.LayoutMode);
        AssertNoControlClipsHorizontally(window);

        window.Width = 1920;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(DashboardLayoutMode.Wide, view.LayoutMode);

        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Equal(0, service.CheckOnlineCallCount);
        window.Close();
    }

    [Fact]
    public void Phase35E_Dashboard_TabReachesEveryActionInReadingOrder()
    {
        var (window, _, service) = RealizeFullDashboard(1280, 860, ThemeVariant.Light);
        var expected = new[] { "Check Online", "View in Calendar", "Open History", "Open Passdown", "Review Notes", "View Shows", "View Pricing", "View Notes", "Pass Perks Details", "Source details" };

        var first = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Check Online");
        Assert.True(first.Focus(NavigationMethod.Tab));
        var visited = new List<string> { "Check Online" };
        for (var i = 0; i < 40 && visited.Count < 60; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (window.FocusManager?.GetFocusedElement() is Control focused && AutomationProperties.GetName(focused) is { Length: > 0 } name)
            {
                visited.Add(name);
            }
        }

        var positions = expected.Select(name => visited.IndexOf(name)).ToList();
        Assert.All(expected.Zip(positions), pair => Assert.True(pair.Second >= 0, $"Tab never reached '{pair.First}'"));
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.Equal(0, service.CheckOnlineCallCount); // focusing Check Online never runs it
        window.Close();
    }
}
