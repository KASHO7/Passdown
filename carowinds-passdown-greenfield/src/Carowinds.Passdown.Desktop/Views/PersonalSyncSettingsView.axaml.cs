using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Carowinds.Passdown.Desktop.Views;

public partial class PersonalSyncSettingsView : UserControl
{
    public PersonalSyncSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Phase 23C: in the Appearance radio group an arrow key moves the selection, as in a native Windows
    /// radio group (Avalonia moves focus only). Presentation only — checking the option runs the same
    /// binding a click does. Tab and pointer focus never change the selection.
    /// </summary>
    private void OnAppearanceOptionGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod == NavigationMethod.Directional && e.Source is RadioButton { IsChecked: not true } option)
        {
            option.IsChecked = true;
        }
    }

    /// <summary>
    /// Phase 23C: Tab (or Shift+Tab) into the Appearance group lands on the checked option, as in a native
    /// radio group — the group is one Tab stop (<c>TabNavigation="Once"</c>) whose target follows the selection.
    /// </summary>
    private void OnAppearanceOptionCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true } option)
        {
            KeyboardNavigation.SetTabOnceActiveElement(AppearanceOptions, option);
        }
    }

    /// <summary>
    /// Phase 35F: the theme options now sit side by side. As in a native Windows radio group, Up / Left select the
    /// previous option and Down / Right the next one (wrapping); the selection runs the same binding a click does.
    /// </summary>
    private void OnRadioGroupKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Panel group || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var step = e.Key switch
        {
            Key.Up or Key.Left => -1,
            Key.Down or Key.Right => 1,
            _ => 0,
        };
        var options = group.Children.OfType<RadioButton>().Where(r => r.IsEffectivelyEnabled).ToList();
        var current = options.FindIndex(r => r.IsFocused);
        if (step == 0 || current < 0 || options.Count == 0)
        {
            return;
        }

        var next = options[(current + step + options.Count) % options.Count];
        next.Focus(NavigationMethod.Directional);
        next.IsChecked = true;
        e.Handled = true;
    }

    /// <summary>
    /// Phase 35A: the same native radio-group behavior for any other one-Tab-stop group (Interface animations):
    /// Tab into the group lands on its checked option.
    /// </summary>
    private void OnRadioGroupOptionCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Parent: InputElement group } option)
        {
            KeyboardNavigation.SetTabOnceActiveElement(group, option);
        }
    }

    /// <summary>
    /// Phase 35F: the section list scrolls the one Settings page so the chosen group's card starts at the top of the
    /// view (or as close as the page's end allows), and marks that entry current. Presentation only: no setting is read
    /// or written, and keyboard focus stays on the list entry.
    /// </summary>
    private void OnSectionNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sectionName } button || this.FindControl<Control>(sectionName) is not { } section)
        {
            return;
        }

        var top = section.TranslatePoint(default, SettingsGroups)?.Y ?? 0;
        var maxOffset = Math.Max(0, SettingsScroll.Extent.Height - SettingsScroll.Viewport.Height);
        _suppressScrollTracking = true;
        SettingsScroll.Offset = new Vector(SettingsScroll.Offset.X, Math.Clamp(top, 0, maxOffset));
        _suppressScrollTracking = false;
        SetCurrentSection(button);
    }

    private bool _suppressScrollTracking;

    /// <summary>Phase 35F: as the page scrolls, the entry for the group nearest the top of the view becomes current.</summary>
    private void OnSettingsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_suppressScrollTracking)
        {
            return;
        }

        var entries = SettingsSectionList.Children.OfType<Button>().ToList();
        var offset = SettingsScroll.Offset.Y;
        var atEnd = offset >= SettingsScroll.Extent.Height - SettingsScroll.Viewport.Height - 1 && offset > 0;
        Button? current = null;
        foreach (var entry in entries)
        {
            if (entry.Tag is string name && this.FindControl<Control>(name) is { IsVisible: true } section
                && section.TranslatePoint(default, SettingsGroups)?.Y is { } top
                && top <= offset + 120)
            {
                current = entry;
            }
        }

        if (atEnd)
        {
            current = entries.LastOrDefault();
        }

        if (current is not null)
        {
            SetCurrentSection(current);
        }
    }

    private void SetCurrentSection(Button current)
    {
        foreach (var entry in SettingsSectionList.Children.OfType<Button>())
        {
            entry.Classes.Set("active", ReferenceEquals(entry, current));
        }
    }
}
