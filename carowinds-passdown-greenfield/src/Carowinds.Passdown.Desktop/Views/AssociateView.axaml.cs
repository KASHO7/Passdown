using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class AssociateView : UserControl
{
    public AssociateView()
    {
        InitializeComponent();

        // Phase 21B date popup: a day is committed by a click on a day cell or Enter/Space — browsing
        // with the arrow keys or the month/year views never changes the selected date (handledEventsToo:
        // the day button and Calendar already mark these events handled by the time they bubble here).
        DateCalendar.AddHandler(PointerReleasedEvent, DateCalendar_OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        DateCalendar.AddHandler(KeyDownEvent, DateCalendar_OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DateButton.Flyout?.Hide(); // never leave the popup open once this screen is gone.
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Phase 21B: every time the date popup opens it starts on the month of the currently selected date,
    /// with that date selected and today's marker re-evaluated — so reopening always reflects the current
    /// selection, never a month browsed to (and abandoned) last time. View state only.
    /// </summary>
    internal void DateFlyout_OnOpening(object? sender, EventArgs e)
    {
        if (DataContext is not AssociateViewModel viewModel || viewModel.SelectedDateForPicker is not { } selected)
        {
            return;
        }

        viewModel.RefreshTodayIndicators();
        DateCalendar.DisplayMode = CalendarMode.Month;
        DateCalendar.SelectedDate = selected;
        DateCalendar.DisplayDate = selected;
    }

    private void DateCalendar_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<CalendarDayButton>(includeSelf: true) is { IsEnabled: true })
        {
            CommitCalendarSelection();
        }
    }

    private void DateCalendar_OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                DateButton.Flyout?.Hide();
                DateButton.Focus(NavigationMethod.Tab);
                e.Handled = true;
                break;

            // In the month (day-grid) view Enter/Space commits the highlighted day; in the year/decade
            // views the Calendar's own handling drills back down a level instead.
            case Key.Enter or Key.Space when DateCalendar.DisplayMode == CalendarMode.Month:
                CommitCalendarSelection();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Hands the picked day to the view model's one date-selection path (a no-op when unchanged) and closes the popup.</summary>
    internal void CommitCalendarSelection()
    {
        if (DataContext is AssociateViewModel viewModel && DateCalendar.SelectedDate is { } picked)
        {
            viewModel.SelectedDateForPicker = picked;
        }

        DateButton.Flyout?.Hide();
        DateButton.Focus(NavigationMethod.Tab);
    }

    private void CalendarTodayButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DateButton.Flyout?.Hide();
        DateButton.Focus(NavigationMethod.Tab);
    }

    /// <summary>
    /// Phase 13E Part Q: pressing Enter in a Manual Hours/Private Buyout time field commits/normalizes
    /// it immediately, the same as Tab/click-away, instead of waiting for a later, unrelated focus
    /// change — and consumes the key so it can never fall through to an unrelated default button. Pure
    /// focus plumbing, not persistence/business logic (CLAUDE.md's Avalonia boundary): the actual
    /// parse/commit lives in <see cref="ViewModels.AssociateViewModel"/>'s
    /// <c>UpdateSourceTrigger=LostFocus</c>-bound properties, which this merely triggers by clearing
    /// focus.
    /// </summary>
    private void TimeTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
        {
            return;
        }

        TopLevel.GetTopLevel(textBox)?.FocusManager?.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
        e.Handled = true;
    }
}
