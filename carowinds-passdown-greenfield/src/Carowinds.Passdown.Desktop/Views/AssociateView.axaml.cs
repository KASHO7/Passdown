using Avalonia.Controls;
using Avalonia.Input;

namespace Carowinds.Passdown.Desktop.Views;

public partial class AssociateView : UserControl
{
    public AssociateView()
    {
        InitializeComponent();
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
