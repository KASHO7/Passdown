using Avalonia.Controls;
using Avalonia.Input;

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
}
