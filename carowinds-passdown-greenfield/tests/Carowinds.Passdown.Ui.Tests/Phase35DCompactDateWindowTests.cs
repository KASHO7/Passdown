using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35D follow-up real-window checks for the compact date selector: on today the Today button is hidden and its
/// space reclaimed; on another date it appears, returns to today with focus kept on the date button, and a Print right
/// after it prints today — never the previous date. The date picker and Tab order still work. Light and Dark at
/// 1280×720 and the 960×640 minimum. A partial of the RUN-ALONE <see cref="Phase33PreviewWindowTests"/> class.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    private static Button DateTodayButton(Window window) => (Button)Named(window, "DateTodayButton");

    [Theory]
    [InlineData(1280d, 720d, false)]
    [InlineData(1280d, 720d, true)]
    [InlineData(960d, 640d, false)]
    [InlineData(960d, 640d, true)]
    public async Task Phase35DFollowUp_TodayHiddenOnToday_ShownOtherwise_ReturnsWithFocus_AndFits(double width, double height, bool dark)
    {
        var w = await RealizeWorkspaceAsync(width, height, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        try
        {
            var dateText = (TextBlock)Named(w.Window, "SelectedDateText");
            var navigator = Named(w.Window, "DateNavigator");
            Assert.Equal(FixedDate.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), dateText.Text);
            Assert.False(DateTodayButton(w.Window).IsEffectivelyVisible);
            var narrow = navigator.Bounds.Width;
            AssertNoControlClipsHorizontally(w.Window);

            ByName<Button>(w.Window, "Previous day").Command!.Execute(null);
            PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted);
            Pump();
            var today = DateTodayButton(w.Window);
            Assert.True(today.IsEffectivelyVisible);
            Assert.Equal("Today", AutomationProperties.GetName(today));
            Assert.True(navigator.Bounds.Width >= narrow + today.Bounds.Width - 0.5, "the Today button's space is not reclaimed on today");
            AssertNoControlClipsHorizontally(w.Window);
            var nav = AbsoluteBounds(navigator, w.Window);
            var actions = AbsoluteBounds(Named(w.Window, "PassdownActions"), w.Window);
            Assert.False(nav.Intersects(actions), "the date bar overlaps Create PDF / Actions");

            today.Focus(NavigationMethod.Tab);
            var center = AbsoluteBounds(today, w.Window).Center;
            w.Window.MouseDown(center, MouseButton.Left);
            w.Window.MouseUp(center, MouseButton.Left);
            PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted && w.ViewModel.SelectedDate == FixedDate);
            Pump();
            Assert.False(DateTodayButton(w.Window).IsEffectivelyVisible);
            Assert.Same(Named(w.Window, "DateButton"), w.Window.FocusManager?.GetFocusedElement()); // focus is not lost with the hidden button
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35DFollowUp_PrintRightAfterToday_PrintsToday_NeverThePreviousDate()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        try
        {
            ByName<Button>(w.Window, "Previous day").Command!.Execute(null);
            PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted);
            w.PreviewDebounce.ElapseLatest();
            Pump();

            var print = ActionsItem(w.Window, "Print Passdown").Command!;
            CloseActions(w.Window);
            DateTodayButton(w.Window).Command!.Execute(null);
            if (print.CanExecute(null))
            {
                print.Execute(null);
            }

            PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted);
            if (w.Printer.Requests.Count == 0)
            {
                Execute(print, () => w.Printer.Requests.Count > 0);
            }

            PumpUntil(() => w.Printer.Requests.Count > 0);
            var today = FixedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var previous = FixedDate.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.All(w.Printer.Requests, r =>
            {
                var text = System.Text.Encoding.UTF8.GetString(r.PdfBytes);
                Assert.Contains(today, text);
                Assert.DoesNotContain(previous, text);
            });
            Assert.Equal(FixedDate, w.ViewModel.SelectedDate);
            Assert.Empty(w.Service.CheckOnlineCalls);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35DFollowUp_DatePicker_StillOpens_AndItsTodayReturns_TabOrderIncludesTodayOnlyOffToday()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        try
        {
            ByName<Button>(w.Window, "Next day").Command!.Execute(null);
            PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted);
            Pump();

            // Tab order off today: … Next day → Today → Create PDF.
            ByName<Button>(w.Window, "Next day").Focus(NavigationMethod.Tab);
            Pump();
            w.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            w.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Pump();
            Assert.Same(DateTodayButton(w.Window), w.Window.FocusManager?.GetFocusedElement());

            var dateButton = (Button)Named(w.Window, "DateButton");
            var flyout = Assert.IsType<Flyout>(dateButton.Flyout);
            flyout.ShowAt(dateButton);
            Pump();
            var calendarToday = w.Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CalendarTodayButton")
                ?? TopLevel.GetTopLevel(dateButton)!.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CalendarTodayButton");
            Assert.True(flyout.IsOpen);
            Assert.NotNull(calendarToday);
            calendarToday!.Command!.Execute(null);
            flyout.Hide();
            PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted && w.ViewModel.SelectedDate == FixedDate);
            Pump();
            Assert.False(DateTodayButton(w.Window).IsEffectivelyVisible);
        }
        finally
        {
            w.Window.Close();
        }
    }
}
