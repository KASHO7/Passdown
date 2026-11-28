using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D real-window checks for the Calendar's selected-date Weather group: inside the selected-date panel in Light
/// and Dark at the narrow (960 px) and medium widths, collapsed by default and expanded from the keyboard, named for
/// screen readers, and with nothing added to — and no size change of — the month's day cells. A partial of the
/// RUN-ALONE <see cref="OperationsCalendarViewTests"/> class. Headless renders are not Windows DPI evidence.
/// </summary>
public partial class OperationsCalendarViewTests
{
    private static OperationsCalendarSnapshot Phase37DOpenSnapshot(DateOnly month, DateOnly selected)
    {
        var snapshot = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
        var hours = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        return snapshot with
        {
            Selected = snapshot.Selected with
            {
                OperatingState = OperationsCalendarOperatingState.Open,
                ParkHours = new AssociateParkHoursView(hours, AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, OfficialOpening: hours.Opening, OfficialClosing: hours.Closing),
            },
        };
    }

    private static async Task<OperationsCalendarViewModel> Phase37DCalendarAsync(bool withWeather)
    {
        var viewModel = new OperationsCalendarViewModel(
            new FakeOperationsCalendarService { SnapshotFactory = Phase37DOpenSnapshot },
            new FakeClock(WeatherUiFixtures.Morning),
            uiDispatcher: new ImmediateUiDispatcher(),
            weatherMonitor: withWeather ? new FakeWeatherMonitor { Current = WeatherUiFixtures.Normal() } : null);
        await viewModel.PendingLoad;
        return viewModel;
    }

    [Theory]
    [InlineData(960d, 640d, false)]
    [InlineData(960d, 640d, true)]
    [InlineData(1280d, 860d, false)]
    [InlineData(1280d, 860d, true)]
    public async Task Phase37D_WeatherGroup_SitsInsideTheSelectedDatePanel_AndExpandsFromTheKeyboard(double width, double height, bool dark)
    {
        var viewModel = await Phase37DCalendarAsync(withWeather: true);
        var window = Phase35ERealize(viewModel, width, height, dark);

        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Selected date summary");
        var group = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "SelectedDateWeather");
        group.BringIntoView();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(group.IsEffectivelyVisible);
        Assert.Equal("Weather", AutomationProperties.GetName(group));
        var forecast = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "WeatherForecast");
        Assert.StartsWith("Weather for Saturday, October 17", AutomationProperties.GetName(forecast));

        var panelBounds = Phase35EBounds(panel, window);
        var groupBounds = Phase35EBounds(group, window);
        Assert.True(groupBounds.Left >= panelBounds.Left - 0.5 && groupBounds.Right <= panelBounds.Right + 0.5, $"Weather {groupBounds} escapes the panel {panelBounds}.");

        // The summary icon is a vector path in the theme's colours, never an emoji or image.
        var icons = group.GetVisualDescendants().OfType<Path>().Where(p => p.Classes.Contains("weatherIconBack") || p.Classes.Contains("weatherIconFront")).ToList();
        Assert.Contains(icons, p => p.Data is not null && p.IsEffectivelyVisible);
        Assert.Empty(group.GetVisualDescendants().OfType<Image>());

        // Collapsed by default; Space on the focused disclosure expands it, and the hourly rows stay inside the panel.
        var expander = group.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "WeatherHourlyExpander");
        Assert.False(expander.IsExpanded);
        Assert.Equal("Hourly forecast", AutomationProperties.GetName(expander));
        var header = expander.GetVisualDescendants().OfType<ToggleButton>().First();
        Assert.True(header.Focus(NavigationMethod.Tab));
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsWeatherHourlyExpanded);

        var rows = expander.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("weatherHourRow")).ToList();
        Assert.Equal(12, rows.Count);
        Assert.All(rows, row => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(row))));
        foreach (var row in rows.Take(2))
        {
            var bounds = Phase35EBounds(row, window);
            Assert.True(bounds.Right <= panelBounds.Right + 0.5, $"hour row {bounds} escapes the panel {panelBounds}.");
        }

        window.Close();
    }

    [Fact]
    public async Task Phase37D_DayCells_GainNoWeather_AndKeepTheirSize()
    {
        var plain = await Phase37DCalendarAsync(withWeather: false);
        var withWeather = await Phase37DCalendarAsync(withWeather: true);
        var plainWindow = Phase35ERealize(plain, 1280, 860, dark: false);
        var weatherWindow = Phase35ERealize(withWeather, 1280, 860, dark: false);

        Size[] CellSizes(Window window) => window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("calendarDay")).Select(b => b.Bounds.Size).ToArray();

        Assert.Equal(CellSizes(plainWindow), CellSizes(weatherWindow));
        foreach (var cell in weatherWindow.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("calendarDay")))
        {
            Assert.DoesNotContain(cell.GetVisualDescendants().OfType<Path>(), p => p.Classes.Contains("weatherIconBack") || p.Classes.Contains("weatherIconFront"));
            Assert.DoesNotContain(cell.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains('°') == true);
        }

        Assert.False(plainWindow.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "SelectedDateWeather").IsVisible);

        plainWindow.Close();
        weatherWindow.Close();
    }
}
