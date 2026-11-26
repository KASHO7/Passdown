using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using Avalonia.VisualTree;
using Carowinds.Passdown.Desktop.Presentation;
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

        // Phase 33: editor | divider | preview sizing — view glue only; the rules live in PdfPreviewLayout.
        AssociateBodyGrid.SizeChanged += (_, e) =>
        {
            _preview?.UpdateAvailableWidth(e.NewSize.Width);
            ApplyPreviewLayout();
        };
        PreviewSplitter.DragCompleted += (_, _) => CommitSplitterWidth();

        // Phase 35C: the price popover — focus moves into it on open; scrolling or resizing the Products list closes it
        // unsaved, so it can never float detached from the row it edits.
        PriceEditorPopup.Opened += (_, _) =>
        {
            PriceEditorPanel.Classes.Remove("entering"); // fades to full opacity only under Window.motion
            FocusPriceEditorInput();
        };
        PriceEditorPopup.Closed += (_, _) => PriceEditorPanel.Classes.Add("entering");
        PriceEditorPanel.AddHandler(KeyDownEvent, PriceEditorPanel_OnKeyDown, RoutingStrategies.Tunnel);
        ProductsSectionScroll.ScrollChanged += (_, e) =>
        {
            if (e.OffsetDelta != default)
            {
                _priceEditor?.CancelIfOpen();
            }
        };
        ProductsSectionScroll.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged || e.HeightChanged)
            {
                _priceEditor?.CancelIfOpen();
            }
        };
        PreviewSplitter.AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right)
            {
                CommitSplitterWidth();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private PassdownPdfPreviewViewModel? _preview;
    private PriceEditorViewModel? _priceEditor;
    private Control? _priceEditAnchor;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_priceEditor is not null)
        {
            _priceEditor.Closed -= PriceEditor_OnClosed;
        }

        _priceEditor = (DataContext as AssociateViewModel)?.PriceEditor;
        if (_priceEditor is not null)
        {
            _priceEditor.Closed += PriceEditor_OnClosed;
        }

        if (_preview is not null)
        {
            _preview.PropertyChanged -= Preview_OnPropertyChanged;
        }

        _preview = (DataContext as AssociateViewModel)?.Preview;
        if (_preview is not null)
        {
            _preview.PropertyChanged += Preview_OnPropertyChanged;
            if (AssociateBodyGrid.Bounds.Width > 0)
            {
                _preview.UpdateAvailableWidth(AssociateBodyGrid.Bounds.Width);
            }
        }

        ApplyPreviewLayout();
    }

    private void Preview_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PassdownPdfPreviewViewModel.PaneMode) or nameof(PassdownPdfPreviewViewModel.Layout)
            or nameof(PassdownPdfPreviewViewModel.EffectivePreviewPaneWidth) or nameof(PassdownPdfPreviewViewModel.IsPreviewPaneShown))
        {
            ApplyPreviewLayout();
        }
    }

    /// <summary>Applies the view model's resolved pane layout to the three body columns.</summary>
    private void ApplyPreviewLayout()
    {
        var columns = AssociateBodyGrid.ColumnDefinitions;
        if (columns.Count != 3)
        {
            return;
        }

        var editor = columns[0];
        var preview = columns[2];
        var layout = _preview?.Layout ?? new PdfPreviewLayoutState(PdfPreviewPaneMode.EditorOnly, 0, 0);
        switch (layout.Mode)
        {
            case PdfPreviewPaneMode.Split:
                editor.MinWidth = PdfPreviewLayout.MinEditorWidth;
                editor.Width = new GridLength(1, GridUnitType.Star);
                preview.MinWidth = PdfPreviewLayout.MinPreviewWidth;
                preview.MaxWidth = Math.Max(PdfPreviewLayout.MinPreviewWidth, layout.MaxPreviewWidth);
                preview.Width = new GridLength(layout.PreviewWidth, GridUnitType.Pixel);
                break;

            case PdfPreviewPaneMode.PreviewOnly:
                editor.MinWidth = 0;
                editor.Width = new GridLength(0, GridUnitType.Pixel);
                preview.MinWidth = 0;
                preview.MaxWidth = double.PositiveInfinity;
                preview.Width = new GridLength(1, GridUnitType.Star);
                break;

            default:
                editor.MinWidth = 0;
                editor.Width = new GridLength(1, GridUnitType.Star);
                preview.MinWidth = 0;
                preview.MaxWidth = double.PositiveInfinity;
                preview.Width = new GridLength(0, GridUnitType.Pixel);
                break;
        }
    }

    private void CommitSplitterWidth()
    {
        if (_preview is null || AssociateBodyGrid.ColumnDefinitions.Count != 3)
        {
            return;
        }

        _preview.CommitPreviewPaneWidth(AssociateBodyGrid.ColumnDefinitions[2].ActualWidth);
        ApplyPreviewLayout(); // normalize: the splitter may have rewritten the editor column as a weighted star
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
    /// Phase 35D follow-up: the toolbar's Today button hides once today is selected, so keyboard focus moves to the
    /// date button beside it instead of being lost with the hidden button. Focus only — Click runs before the
    /// button's GoToTodayCommand, which still does the date change.
    /// </summary>
    private void DateTodayButton_OnClick(object? sender, RoutedEventArgs e) => DateButton.Focus(NavigationMethod.Tab);

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

    /// <summary>
    /// Phase 22D: attendance / pre-solds show thousands separators once the field loses focus. View
    /// glue only — the view model decides what (if anything) to regroup; typing is never rewritten.
    /// </summary>
    internal void CountTextBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AssociateViewModel viewModel)
        {
            viewModel.RegroupCountFields();
        }
    }

    /// <summary>
    /// Phase 35C: the Edit control that was activated (pointer, Enter or Space) becomes the popover's anchor. Click is
    /// raised before the button's command runs, so the anchor is set before the editor opens.
    /// </summary>
    private void PriceEditButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor)
        {
            // A partly hidden row is scrolled fully into view and laid out first, so that scroll is finished before the
            // editor opens (a later scroll closes it) and the popover is placed against the control's final position.
            anchor.BringIntoView();
            anchor.UpdateLayout();
            _priceEditAnchor = anchor; // focus returns here
            // Placed against the whole display-price cell (price + "Manual override" hint), so the hint stays visible.
            PriceEditorPopup.PlacementTarget = anchor.Parent is StackPanel { Classes: var classes } cell && classes.Contains("priceCell") ? cell : anchor;
        }
    }

    /// <summary>Phase 35C: Escape anywhere in the popover cancels; Enter in the price input saves (Enter on a button activates it).</summary>
    private void PriceEditorPanel_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_priceEditor is not { IsOpen: true } editor)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            editor.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is Visual source && (ReferenceEquals(source, PriceEditorTextBox) || source.FindAncestorOfType<TextBox>() == PriceEditorTextBox))
        {
            editor.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusPriceEditorInput()
    {
        PriceEditorTextBox.Focus(NavigationMethod.Tab);
        PriceEditorTextBox.SelectAll();
    }

    /// <summary>Phase 35C: focus returns to the Edit control that opened the editor, when it still exists on screen.</summary>
    private void PriceEditor_OnClosed(object? sender, bool committed)
    {
        var anchor = _priceEditAnchor;
        _priceEditAnchor = null;
        if (anchor is not null && anchor.IsAttachedToVisualTree() && anchor.IsEffectivelyVisible)
        {
            anchor.Focus(NavigationMethod.Tab);
        }
    }
}
